using System.Collections.Concurrent;
using Tichu.Models;

namespace Tichu.Services
{
    /// <summary>
    /// 턴 제한시간 타이머와 AI 봇의 자동 진행을 방 단위로 예약/재예약한다.
    /// 사람 차례가 시간을 넘기면 GameEngine.HandleTimeout으로 자동 패스/자동 플레이 처리하고,
    /// 봇 차례(카드 교환 포함)면 잠깐 대기 후 봇이 스스로 행동한 뒤 다시 다음 상태를 예약한다.
    /// </summary>
    public class TurnTimerCoordinator
    {
        private readonly TimerService _timerService;
        private readonly GameEngine _engine;
        private readonly RoomBroadcaster _broadcaster;
        private readonly TichuRuleEngine _rules;
        private readonly ConcurrentDictionary<int, byte> _botBusy = new();

        public TurnTimerCoordinator(TimerService timerService, GameEngine engine, RoomBroadcaster broadcaster, TichuRuleEngine rules)
        {
            _timerService = timerService;
            _engine = engine;
            _broadcaster = broadcaster;
            _rules = rules;
        }

        public void Schedule(GameRoom room)
        {
            if (TryHandleBotTurn(room)) return;

            bool shouldRun;
            int seat;

            lock (room.Lock)
            {
                shouldRun = room.Status == RoomStatus.Playing
                    && room.Game != null
                    && room.Game.Phase == GamePhase.Playing
                    && !room.Game.DragonTrickPending;
                seat = room.Game?.CurrentTurnSeat ?? -1;
            }

            if (!shouldRun)
            {
                _timerService.StopTimer(room.Id);
                return;
            }

            _ = _timerService.StartTurnTimerAsync(room.Id, room.TurnTimeLimit, async () =>
            {
                bool acted;
                lock (room.Lock)
                {
                    acted = room.Status == RoomStatus.Playing
                        && room.Game != null
                        && room.Game.Phase == GamePhase.Playing
                        && room.Game.CurrentTurnSeat == seat
                        && !room.Game.DragonTrickPending;

                    if (acted)
                    {
                        _engine.HandleTimeout(room, seat);
                    }
                }

                if (acted)
                {
                    await _broadcaster.BroadcastRoomAsync(room);
                    if (room.Status == RoomStatus.Ended)
                    {
                        await _broadcaster.BroadcastLobbyAsync();
                    }
                    Schedule(room);
                }
            });
        }

        public void Stop(int roomId) => _timerService.StopTimer(roomId);

        /// <returns>지금 처리할 봇이 있어서(혹은 이미 예약되어 있어서) 사람용 턴 타이머를 시작할 필요가 없으면 true</returns>
        private bool TryHandleBotTurn(GameRoom room)
        {
            string? botUserId = null;

            lock (room.Lock)
            {
                var game = room.Game;
                if (room.Status != RoomStatus.Playing || game == null) return false;

                if (game.Phase == GamePhase.Exchange)
                {
                    botUserId = room.Players
                        .FirstOrDefault(p => p.IsBot && !game.PendingExchange.ContainsKey(p.Seat))
                        ?.UserId;
                }
                else if (game.Phase == GamePhase.Playing)
                {
                    if (game.DragonTrickPending)
                    {
                        var giver = room.Players.FirstOrDefault(p => p.Seat == game.LastPlayerSeat);
                        if (giver is { IsBot: true }) botUserId = giver.UserId;
                    }
                    else
                    {
                        var current = room.Players.FirstOrDefault(p => p.Seat == game.CurrentTurnSeat);
                        if (current is { IsBot: true } && !current.HasFinishedThisRound) botUserId = current.UserId;
                    }
                }
            }

            if (botUserId == null) return false;

            _timerService.StopTimer(room.Id);

            if (!_botBusy.TryAdd(room.Id, 0)) return true; // 이미 이 방에 봇 행동이 예약되어 있음

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(Random.Shared.Next(600, 1400));
                    lock (room.Lock)
                    {
                        PerformBotAction(room, botUserId);
                    }
                    await _broadcaster.BroadcastRoomAsync(room);
                    if (room.Status == RoomStatus.Ended)
                    {
                        await _broadcaster.BroadcastLobbyAsync();
                    }
                }
                finally
                {
                    _botBusy.TryRemove(room.Id, out _);
                }
                Schedule(room);
            });

            return true;
        }

        private void PerformBotAction(GameRoom room, string botUserId)
        {
            var bot = room.Players.FirstOrDefault(p => p.UserId == botUserId);
            var game = room.Game;
            if (bot == null || game == null) return;

            if (game.Phase == GamePhase.Exchange)
            {
                if (game.PendingExchange.ContainsKey(bot.Seat)) return;
                var hand = bot.Hand.OrderBy(c => c.RankValue).ToList();
                if (hand.Count < 3) return;

                var picks = new List<(int cardId, int toSeat)>
                {
                    (hand[0].Id, (bot.Seat + 1) % 4),
                    (hand[1].Id, (bot.Seat + 2) % 4),
                    (hand[2].Id, (bot.Seat + 3) % 4)
                };
                _engine.SubmitExchange(room, bot.UserId, picks);
                return;
            }

            if (game.Phase != GamePhase.Playing) return;

            if (game.DragonTrickPending)
            {
                if (game.LastPlayerSeat != bot.Seat) return;
                int opponentSeat = (bot.Seat + 1) % 4;
                _engine.GiveDragonTrick(room, bot.UserId, opponentSeat);
                return;
            }

            if (game.CurrentTurnSeat != bot.Seat || bot.HasFinishedThisRound) return;

            var handCards = bot.Hand;
            if (handCards.Count == 0) return;

            // 마작 소원이 걸려있으면 실제 규칙상 의무이므로 (파트너 보호보다도) 최우선으로 만족시키려 시도
            if (game.MahjongWish.HasValue)
            {
                var wishPlay = TryBuildMahjongWishFulfillment(game, handCards);
                if (wishPlay != null)
                {
                    _engine.PlayCards(room, bot.UserId, wishPlay.Select(c => c.Id).ToList());
                    return;
                }
            }

            if (game.LastPlay.Count == 0)
            {
                // 리드: 자연스러운 스트레이트(5장)를 갖고 있으면 가장 낮은 것으로, 없으면
                // 개(Dog)를 제외한 가장 낮은 단독 카드로 안전하게 시작
                var leadStraight = FindLowestStraight(handCards.Where(c => c.Rank != "Dog").ToList(), 5, 0);
                if (leadStraight != null)
                {
                    _engine.PlayCards(room, bot.UserId, leadStraight.Select(c => c.Id).ToList());
                    return;
                }

                var lead = handCards.Where(c => c.Rank != "Dog").OrderBy(c => c.RankValue).FirstOrDefault()
                           ?? handCards.OrderBy(c => c.RankValue).First();
                _engine.PlayCards(room, bot.UserId, new List<int> { lead.Id });
                return;
            }

            // 같은 팀(파트너)이 이미 이기고 있는 판이면 굳이 빼앗지 않고 패스 (파트너에게 리드를 넘겨줌)
            bool partnerIsWinning = game.LastPlayerSeat.HasValue && game.LastPlayerSeat.Value % 2 == bot.Seat % 2;
            if (partnerIsWinning)
            {
                _engine.Pass(room, bot.UserId);
                return;
            }

            int needed = game.LastPlay.Count;
            double lastMax = needed == 1 ? _engine.GetEffectiveLastSingleValue(game) : game.LastPlay.Max(c => c.RankValue);
            var prevType = _rules.DetectCombo(game.LastPlay);
            var candidate = FindBotCandidate(handCards, prevType, needed, lastMax);

            if (candidate != null)
            {
                _engine.PlayCards(room, bot.UserId, candidate.Select(c => c.Id).ToList());
            }
            else
            {
                _engine.Pass(room, bot.UserId);
            }
        }

        /// <summary>
        /// 직전 패의 조합 타입(prevType)에 맞춰 손패에서 이길 수 있는 가장 낮은 패를 찾는다.
        /// 싱글/페어/트리플/포카드 폭탄은 같은 숫자 그룹으로, 스트레이트/연속 페어/풀하우스는
        /// 전용 탐색으로 처리한다. 폭탄으로 다른 타입을 맞받아치는 것은 지원하지 않음(패스로 넘어감).
        /// </summary>
        private List<Card>? FindBotCandidate(List<Card> handCards, TichuComboType prevType, int needed, double lastMax)
        {
            switch (prevType)
            {
                case TichuComboType.Single:
                case TichuComboType.Pair:
                case TichuComboType.Triple:
                case TichuComboType.BombFour:
                    return handCards
                        .GroupBy(c => c.RankValue)
                        .Where(g => g.Count() >= needed && g.Key > lastMax)
                        .OrderBy(g => g.Key)
                        .Select(g => g.Take(needed).ToList())
                        .FirstOrDefault();

                case TichuComboType.Straight:
                    return needed >= 5 ? FindLowestStraight(handCards, needed, lastMax) : null;

                case TichuComboType.PairSequence:
                    return needed >= 4 && needed % 2 == 0 ? FindLowestPairSequence(handCards, needed / 2, lastMax) : null;

                case TichuComboType.FullHouse:
                    return needed == 5 ? FindFullHouse(handCards, lastMax) : null;

                default:
                    return null;
            }
        }

        /// <summary>손패에서 길이 length인 자연스러운 스트레이트 중 최댓값이 lastMax보다 큰 가장 낮은 것을 찾는다
        /// (불사조 등 와일드카드는 쓰지 않음 - 봇 로직은 단순하게 유지).</summary>
        private List<Card>? FindLowestStraight(List<Card> handCards, int length, double lastMax)
        {
            var byValue = handCards
                .Where(c => !c.IsSpecial || c.Rank == "Mahjong")
                .GroupBy(c => c.RankValue)
                .ToDictionary(g => g.Key, g => g.First());

            for (int start = 1; start + length - 1 <= 14; start++)
            {
                bool ok = true;
                for (int i = 0; i < length; i++)
                {
                    if (!byValue.ContainsKey(start + i)) { ok = false; break; }
                }
                if (!ok) continue;

                int top = start + length - 1;
                if (top <= lastMax) continue;
                return Enumerable.Range(start, length).Select(v => byValue[v]).ToList();
            }
            return null;
        }

        /// <summary>손패에서 pairCount개가 이어지는 연속 페어 중 최댓값이 lastMax보다 큰 가장 낮은 것을 찾는다.</summary>
        private List<Card>? FindLowestPairSequence(List<Card> handCards, int pairCount, double lastMax)
        {
            var pairGroups = handCards
                .Where(c => !c.IsSpecial)
                .GroupBy(c => c.RankValue)
                .Where(g => g.Count() >= 2)
                .ToDictionary(g => g.Key, g => g.Take(2).ToList());

            for (int start = 2; start + pairCount - 1 <= 14; start++)
            {
                bool ok = true;
                for (int i = 0; i < pairCount; i++)
                {
                    if (!pairGroups.ContainsKey(start + i)) { ok = false; break; }
                }
                if (!ok) continue;

                int top = start + pairCount - 1;
                if (top <= lastMax) continue;
                return Enumerable.Range(start, pairCount).SelectMany(v => pairGroups[v]).ToList();
            }
            return null;
        }

        /// <summary>손패에서 만들 수 있는 풀하우스(트리플+페어) 중 최댓값이 lastMax보다 크면서
        /// 그 최댓값이 가장 작은 조합을 찾는다 (서버의 비교 기준 = 5장 중 최댓값과 동일하게 맞춤).</summary>
        private List<Card>? FindFullHouse(List<Card> handCards, double lastMax)
        {
            var groups = handCards.Where(c => !c.IsSpecial).GroupBy(c => c.RankValue).ToList();
            var triples = groups.Where(g => g.Count() >= 3).OrderBy(g => g.Key).ToList();
            var pairs = groups.Where(g => g.Count() >= 2).OrderBy(g => g.Key).ToList();

            List<Card>? best = null;
            double bestMax = double.MaxValue;
            foreach (var t in triples)
            {
                foreach (var p in pairs)
                {
                    if (p.Key == t.Key) continue;
                    double max = Math.Max(t.Key, p.Key);
                    if (max <= lastMax) continue;
                    if (max < bestMax)
                    {
                        bestMax = max;
                        best = t.Take(3).Concat(p.Take(2)).ToList();
                    }
                }
            }
            return best;
        }

        /// <summary>
        /// 마작 소원을 지금 낼 수 있는 패로 만족시킬 수 있으면 그 카드 목록을 반환한다
        /// (없으면 null, 즉 소원 무시하고 평소대로 진행). GameEngine의 CheckMahjongWishViolation과
        /// 같은 범위(싱글/페어/트리플)만 다룬다.
        /// </summary>
        private List<Card>? TryBuildMahjongWishFulfillment(GameState game, List<Card> handCards)
        {
            int wish = game.MahjongWish!.Value;
            var wishCards = handCards.Where(c => c.RankValue == wish).ToList();
            if (wishCards.Count == 0) return null;

            if (game.LastPlay.Count == 0)
            {
                return new List<Card> { wishCards[0] };
            }

            int needed = game.LastPlay.Count;
            if (needed > 3) return null;

            double lastMax = needed == 1 ? _engine.GetEffectiveLastSingleValue(game) : game.LastPlay.Max(c => c.RankValue);
            if (wish <= lastMax) return null;

            if (wishCards.Count >= needed) return wishCards.Take(needed).ToList();

            var phoenix = handCards.FirstOrDefault(c => c.Rank == "Phoenix");
            if (phoenix != null && wishCards.Count >= needed - 1)
            {
                return wishCards.Concat(new[] { phoenix }).ToList();
            }

            return null;
        }
    }
}
