using Tichu.Models;

namespace Tichu.Services
{
    /// <summary>
    /// 티츄 라운드 진행 로직 (교환 -> 플레이 -> 라운드 종료 -> 다음 라운드).
    /// GameRoom.Lock 안에서 호출되어야 한다 (Hub에서 lock 처리).
    /// </summary>
    public class GameEngine
    {
        private readonly GameService _gameService;
        private readonly TichuRuleEngine _rules;

        public GameEngine(GameService gameService, TichuRuleEngine rules)
        {
            _gameService = gameService;
            _rules = rules;
        }

        public string? StartGame(GameRoom room, string requestUserId)
        {
            if (room.Status != RoomStatus.Waiting) return "이미 시작된 방입니다.";
            if (room.Players.Count != 4) return "4명이 모여야 시작할 수 있습니다.";
            var requester = room.Players.FirstOrDefault(p => p.UserId == requestUserId);
            if (requester == null || !requester.IsHost) return "방장만 게임을 시작할 수 있습니다.";
            if (!room.Players.All(p => p.IsReady)) return "모든 플레이어가 준비를 완료해야 합니다.";

            room.Status = RoomStatus.Playing;
            room.Game = new GameState();
            DealNewHand(room);
            return null;
        }

        public string? StartNextRound(GameRoom room, string requestUserId)
        {
            if (room.Game == null) return "게임이 없습니다.";
            if (room.Status == RoomStatus.Ended) return "이미 종료된 게임입니다.";
            var requester = room.Players.FirstOrDefault(p => p.UserId == requestUserId);
            if (requester == null || !requester.IsHost) return "방장만 다음 라운드를 시작할 수 있습니다.";
            if (room.Game.Phase != GamePhase.RoundEnd) return "라운드가 아직 끝나지 않았습니다.";

            room.Game.RoundNumber++;
            DealNewHand(room);
            return null;
        }

        private void DealNewHand(GameRoom room)
        {
            var game = room.Game!;
            var deck = _gameService.CreateShuffledDeck();
            var dealt = _gameService.DealBySeat(deck);

            game.PendingExchange.Clear();
            game.LastPlay.Clear();
            game.LastPlayerSeat = null;
            game.PassStreak = 0;
            game.CurrentTrickCards.Clear();
            game.FinishedSeatOrder.Clear();
            game.DragonTrickPending = false;
            game.Phase = GamePhase.Exchange;
            game.LastMessage = "카드를 교환하세요 (왼쪽/파트너/오른쪽에게 1장씩).";

            foreach (var p in room.Players)
            {
                p.Hand = dealt[p.Seat];
                p.TichuCall = TichuCallType.None;
                p.HasActedThisRound = false;
                p.HasFinishedThisRound = false;
                p.FinishPosition = -1;
                p.WonPileScore = 0;
            }
        }

        /// <summary>스몰 티츄: 카드 교환 단계부터 자신의 첫 카드를 내기 전까지 언제든 선언 가능 (±100점)</summary>
        public string? CallTichu(GameRoom room, string userId)
        {
            var game = room.Game;
            if (game == null) return "게임이 없습니다.";
            var player = room.Players.FirstOrDefault(p => p.UserId == userId);
            if (player == null) return "플레이어를 찾을 수 없습니다.";
            if (player.TichuCall != TichuCallType.None) return "이미 티츄를 불렀습니다.";
            if (player.HasActedThisRound || player.Hand.Count != 14) return "카드를 내기 전(14장을 그대로 들고 있을 때)에만 티츄를 부를 수 있습니다.";

            player.TichuCall = TichuCallType.Small;
            game.LastMessage = $"{player.Nickname}님이 티츄를 외쳤습니다!";
            return null;
        }

        /// <summary>라지(그랜드) 티츄: 카드 교환을 제출하기 전에만 선언 가능 (±200점)</summary>
        public string? CallGrandTichu(GameRoom room, string userId)
        {
            var game = room.Game;
            if (game == null) return "게임이 없습니다.";
            if (game.Phase != GamePhase.Exchange) return "라지 티츄는 카드 교환 전에만 부를 수 있습니다.";

            var player = room.Players.FirstOrDefault(p => p.UserId == userId);
            if (player == null) return "플레이어를 찾을 수 없습니다.";
            if (player.TichuCall != TichuCallType.None) return "이미 티츄를 불렀습니다.";
            if (game.PendingExchange.ContainsKey(player.Seat)) return "라지 티츄는 카드 교환을 제출하기 전에만 부를 수 있습니다.";

            player.TichuCall = TichuCallType.Grand;
            game.LastMessage = $"{player.Nickname}님이 라지 티츄를 외쳤습니다!";
            return null;
        }

        public string? SubmitExchange(GameRoom room, string userId, List<(int cardId, int toSeat)> picks)
        {
            var game = room.Game;
            if (game == null) return "게임이 없습니다.";
            if (game.Phase != GamePhase.Exchange) return "지금은 카드 교환 단계가 아닙니다.";

            var player = room.Players.FirstOrDefault(p => p.UserId == userId);
            if (player == null) return "플레이어를 찾을 수 없습니다.";
            if (game.PendingExchange.ContainsKey(player.Seat)) return "이미 교환 카드를 제출했습니다.";

            if (picks.Count != 3) return "정확히 3장을 선택해야 합니다.";
            var otherSeats = Enumerable.Range(0, 4).Where(s => s != player.Seat).ToHashSet();
            var targetSeats = picks.Select(p => p.toSeat).ToList();
            if (targetSeats.Distinct().Count() != 3 || !targetSeats.All(otherSeats.Contains))
                return "왼쪽/파트너/오른쪽 각 1명에게 정확히 1장씩 보내야 합니다.";

            var cardIds = picks.Select(p => p.cardId).ToList();
            if (cardIds.Distinct().Count() != 3) return "서로 다른 카드 3장을 선택해야 합니다.";

            var cards = player.Hand.Where(c => cardIds.Contains(c.Id)).ToList();
            if (cards.Count != 3) return "선택한 카드가 손패에 없습니다.";

            var list = new List<ExchangeCard>();
            foreach (var (cardId, toSeat) in picks)
            {
                var card = cards.First(c => c.Id == cardId);
                list.Add(new ExchangeCard { ToSeat = toSeat, Card = card });
            }

            game.PendingExchange[player.Seat] = list;

            if (game.PendingExchange.Count == 4)
            {
                ApplyExchange(room);
            }

            return null;
        }

        private void ApplyExchange(GameRoom room)
        {
            var game = room.Game!;
            var incoming = new Dictionary<int, List<Card>> { { 0, new() }, { 1, new() }, { 2, new() }, { 3, new() } };

            foreach (var (fromSeat, list) in game.PendingExchange)
            {
                var player = room.Players.First(p => p.Seat == fromSeat);
                foreach (var ex in list)
                {
                    player.Hand.RemoveAll(c => c.Id == ex.Card.Id);
                    incoming[ex.ToSeat].Add(ex.Card);
                }
            }

            foreach (var (seat, cards) in incoming)
            {
                room.Players.First(p => p.Seat == seat).Hand.AddRange(cards);
            }

            game.PendingExchange.Clear();
            game.Phase = GamePhase.Playing;

            // 방장이 항상 라운드의 첫 트릭을 리드한다
            var leader = room.Players.First(p => p.IsHost);
            game.CurrentTurnSeat = leader.Seat;
            game.LastMessage = $"{leader.Nickname}님(방장)부터 시작합니다.";
        }

        public string? PlayCards(GameRoom room, string userId, List<int> cardIds)
        {
            var game = room.Game;
            if (game == null) return "게임이 없습니다.";
            if (game.Phase != GamePhase.Playing) return "지금은 카드를 낼 수 없습니다.";
            if (game.DragonTrickPending) return "드래곤 카드를 줄 상대를 먼저 선택하세요.";

            var player = room.Players.FirstOrDefault(p => p.UserId == userId);
            if (player == null) return "플레이어를 찾을 수 없습니다.";
            if (player.HasFinishedThisRound) return "이미 이번 라운드를 끝냈습니다.";
            if (game.CurrentTurnSeat != player.Seat) return "당신의 차례가 아닙니다.";
            if (cardIds == null || cardIds.Count == 0) return "낼 카드를 선택하세요.";

            var cards = player.Hand.Where(c => cardIds.Contains(c.Id)).ToList();
            if (cards.Count != cardIds.Distinct().Count()) return "선택한 카드가 손패에 없습니다.";

            // Dog: 리드일 때만, 단독으로만 낼 수 있음
            if (cards.Count == 1 && cards[0].Rank == "Dog")
            {
                if (game.LastPlay.Count != 0) return "개(Dog)는 트릭을 리드할 때만 낼 수 있습니다.";

                player.Hand.RemoveAll(c => c.Id == cards[0].Id);
                player.HasActedThisRound = true;
                game.CurrentTrickCards.Add(cards[0]);
                game.LastPlay.Clear();
                game.CurrentTrickCards.Clear();
                game.PassStreak = 0;
                game.LastPlayerSeat = null;

                bool finished = CheckFinish(room, player);
                if (!finished)
                {
                    int partnerSeat = (player.Seat + 2) % 4;
                    var partner = room.Players.First(p => p.Seat == partnerSeat);
                    game.CurrentTurnSeat = partner.HasFinishedThisRound ? NextActiveSeat(room, partnerSeat) : partnerSeat;
                    game.LastMessage = $"{player.Nickname}님이 개를 내서 {partner.Nickname}님에게 턴이 넘어갔습니다.";
                }
                return null;
            }

            var combo = _rules.DetectCombo(cards);
            if (combo == TichuComboType.None) return "유효하지 않은 카드 조합입니다.";

            if (game.LastPlay.Count > 0 && !_rules.IsStronger(cards, game.LastPlay))
                return "이전에 나온 패보다 강하지 않습니다.";

            foreach (var c in cards) player.Hand.Remove(c);
            player.HasActedThisRound = true;
            game.CurrentTrickCards.AddRange(cards);
            game.LastPlay = cards;
            game.LastPlayerSeat = player.Seat;
            game.PassStreak = 0;
            game.LastMessage = $"{player.Nickname}님이 카드를 냈습니다.";

            bool didFinish = CheckFinish(room, player);
            if (game.Phase != GamePhase.Playing) return null; // 라운드 종료됨

            game.CurrentTurnSeat = NextActiveSeat(room, player.Seat);
            return null;
        }

        public string? Pass(GameRoom room, string userId)
        {
            var game = room.Game;
            if (game == null) return "게임이 없습니다.";
            if (game.Phase != GamePhase.Playing) return "지금은 패스할 수 없습니다.";
            if (game.DragonTrickPending) return "드래곤 카드를 줄 상대를 먼저 선택하세요.";

            var player = room.Players.FirstOrDefault(p => p.UserId == userId);
            if (player == null) return "플레이어를 찾을 수 없습니다.";
            if (player.HasFinishedThisRound) return "이미 이번 라운드를 끝냈습니다.";
            if (game.CurrentTurnSeat != player.Seat) return "당신의 차례가 아닙니다.";
            if (game.LastPlay.Count == 0) return "리드 턴에는 패스할 수 없습니다. 카드를 내야 합니다.";

            game.PassStreak++;
            int activeCount = room.Players.Count(p => !p.HasFinishedThisRound);

            if (game.PassStreak >= activeCount - 1)
            {
                ResolveTrickWin(room);
            }
            else
            {
                game.CurrentTurnSeat = NextActiveSeat(room, player.Seat);
            }

            return null;
        }

        private void ResolveTrickWin(GameRoom room)
        {
            var game = room.Game!;
            bool isDragonTrick = game.LastPlay.Count == 1 && game.LastPlay[0].Rank == "Dragon";

            if (isDragonTrick)
            {
                game.DragonTrickPending = true;
                var winner = room.Players.First(p => p.Seat == game.LastPlayerSeat);
                game.LastMessage = $"{winner.Nickname}님이 드래곤으로 트릭을 이겼습니다. 상대에게 카드를 넘겨주세요.";
                return;
            }

            var trickScore = game.CurrentTrickCards.Sum(c => c.ScoreValue);
            var winnerPlayer = room.Players.First(p => p.Seat == game.LastPlayerSeat);
            winnerPlayer.WonPileScore += trickScore;

            game.CurrentTrickCards.Clear();
            game.LastPlay.Clear();
            game.PassStreak = 0;

            game.CurrentTurnSeat = winnerPlayer.HasFinishedThisRound
                ? NextActiveSeat(room, winnerPlayer.Seat)
                : winnerPlayer.Seat;
            game.LastMessage = $"{winnerPlayer.Nickname}님이 트릭을 가져갔습니다 ({trickScore}점).";
            game.LastPlayerSeat = null;
        }

        public string? GiveDragonTrick(GameRoom room, string userId, int targetSeat)
        {
            var game = room.Game;
            if (game == null) return "게임이 없습니다.";
            if (!game.DragonTrickPending) return "지금은 드래곤 카드를 넘길 수 없습니다.";

            var player = room.Players.FirstOrDefault(p => p.UserId == userId);
            if (player == null || player.Seat != game.LastPlayerSeat) return "드래곤을 낸 사람만 넘겨줄 수 있습니다.";
            if (targetSeat < 0 || targetSeat > 3 || targetSeat % 2 == player.Seat % 2) return "상대팀 플레이어를 선택해야 합니다.";

            var target = room.Players.First(p => p.Seat == targetSeat);
            var trickScore = game.CurrentTrickCards.Sum(c => c.ScoreValue);
            target.WonPileScore += trickScore;

            game.CurrentTrickCards.Clear();
            game.LastPlay.Clear();
            game.PassStreak = 0;
            game.DragonTrickPending = false;

            game.CurrentTurnSeat = player.HasFinishedThisRound ? NextActiveSeat(room, player.Seat) : player.Seat;
            game.LastMessage = $"드래곤 트릭이 {target.Nickname}님에게 넘어갔습니다 ({trickScore}점).";
            game.LastPlayerSeat = null;
            return null;
        }

        private int NextActiveSeat(GameRoom room, int fromSeat)
        {
            for (int i = 1; i <= 4; i++)
            {
                int s = (fromSeat + i) % 4;
                var p = room.Players.First(pl => pl.Seat == s);
                if (!p.HasFinishedThisRound) return s;
            }
            return fromSeat;
        }

        /// <returns>이 플레이어가 손패를 다 써서 라운드가 종료 처리되었으면 true</returns>
        private bool CheckFinish(GameRoom room, Player player)
        {
            if (player.Hand.Count != 0) return false;

            var game = room.Game!;
            player.HasFinishedThisRound = true;
            player.FinishPosition = game.FinishedSeatOrder.Count;
            game.FinishedSeatOrder.Add(player.Seat);

            if (game.FinishedSeatOrder.Count == 2)
            {
                int a = game.FinishedSeatOrder[0], b = game.FinishedSeatOrder[1];
                if (a % 2 == b % 2)
                {
                    EndRound(room, doubleWinTeam: a % 2);
                    return true;
                }
            }

            if (game.FinishedSeatOrder.Count >= 3)
            {
                EndRound(room, doubleWinTeam: null);
                return true;
            }

            return false;
        }

        private void EndRound(GameRoom room, int? doubleWinTeam)
        {
            var game = room.Game!;
            game.Phase = GamePhase.RoundEnd;
            game.DragonTrickPending = false;

            var before = new Dictionary<int, int>(game.TeamScores);
            string note;

            if (doubleWinTeam != null)
            {
                game.TeamScores[doubleWinTeam.Value] += 200;
                var teamName = doubleWinTeam.Value == 0 ? "A팀 (좌석 0,2)" : "B팀 (좌석 1,3)";
                note = $"{teamName} 더블 승 (1-2 피니시)! +200점";
                game.CurrentTrickCards.Clear();
                game.LastPlay.Clear();
            }
            else
            {
                // 라운드가 끝나는 마지막 플레이로 진행 중이던 트릭은 그 플레이어가 가져간 것으로 처리
                if (game.LastPlayerSeat.HasValue && game.CurrentTrickCards.Count > 0)
                {
                    var trickWinner = room.Players.First(p => p.Seat == game.LastPlayerSeat.Value);
                    trickWinner.WonPileScore += game.CurrentTrickCards.Sum(c => c.ScoreValue);
                }
                game.CurrentTrickCards.Clear();
                game.LastPlay.Clear();

                int loserSeat = Enumerable.Range(0, 4).First(s => !game.FinishedSeatOrder.Contains(s));
                var loser = room.Players.First(p => p.Seat == loserSeat);
                var firstPlayer = room.Players.First(p => p.Seat == game.FinishedSeatOrder[0]);

                // 남은 손패 점수 -> 상대팀
                int loserHandScore = loser.Hand.Sum(c => c.ScoreValue);
                int opponentTeam = 1 - loser.TeamId;
                game.TeamScores[opponentTeam] += loserHandScore;
                loser.Hand.Clear();

                // 패자가 모은 트릭 점수 -> 1등에게
                firstPlayer.WonPileScore += loser.WonPileScore;
                loser.WonPileScore = 0;

                foreach (var p in room.Players)
                    game.TeamScores[p.TeamId] += p.WonPileScore;

                note = $"{loser.Nickname}님이 마지막까지 남았습니다. (손패 {loserHandScore}점 상대팀 획득)";
            }

            foreach (var p in room.Players)
            {
                if (p.TichuCall == TichuCallType.None) continue;

                int points = p.TichuCall == TichuCallType.Grand ? 200 : 100;
                string label = p.TichuCall == TichuCallType.Grand ? "라지 티츄" : "티츄";
                int bonus = p.FinishPosition == 0 ? points : -points;
                game.TeamScores[p.TeamId] += bonus;
                note += p.FinishPosition == 0
                    ? $" / {p.Nickname} {label} 성공 +{points}"
                    : $" / {p.Nickname} {label} 실패 -{points}";
            }

            game.History.Add(new RoundLogEntry
            {
                Round = game.RoundNumber,
                Team0Gain = game.TeamScores[0] - before[0],
                Team1Gain = game.TeamScores[1] - before[1],
                Note = note
            });
            game.LastMessage = note;

            if (game.TeamScores[0] >= room.TargetScore || game.TeamScores[1] >= room.TargetScore)
            {
                room.Status = RoomStatus.Ended;
            }
        }

        public string? HandleTimeout(GameRoom room, int seat)
        {
            var game = room.Game;
            if (game == null || game.Phase != GamePhase.Playing || game.DragonTrickPending) return null;
            if (game.CurrentTurnSeat != seat) return null;

            var player = room.Players.First(p => p.Seat == seat);
            if (game.LastPlay.Count == 0)
            {
                var card = player.Hand.Where(c => c.Rank != "Dog").OrderBy(c => c.RankValue).FirstOrDefault()
                           ?? player.Hand.OrderBy(c => c.RankValue).First();
                return PlayCards(room, player.UserId, new List<int> { card.Id });
            }

            return Pass(room, player.UserId);
        }
    }
}
