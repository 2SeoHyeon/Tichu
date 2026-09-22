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
        private readonly ConcurrentDictionary<int, byte> _botBusy = new();

        public TurnTimerCoordinator(TimerService timerService, GameEngine engine, RoomBroadcaster broadcaster)
        {
            _timerService = timerService;
            _engine = engine;
            _broadcaster = broadcaster;
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

            if (game.LastPlay.Count == 0)
            {
                // 리드: 개(Dog)를 제외한 가장 낮은 카드로 안전하게 시작
                var lead = handCards.Where(c => c.Rank != "Dog").OrderBy(c => c.RankValue).FirstOrDefault()
                           ?? handCards.OrderBy(c => c.RankValue).First();
                _engine.PlayCards(room, bot.UserId, new List<int> { lead.Id });
                return;
            }

            int needed = game.LastPlay.Count;
            double lastMax = needed == 1 ? _engine.GetEffectiveLastSingleValue(game) : game.LastPlay.Max(c => c.RankValue);
            List<Card>? candidate = null;

            if (needed is 1 or 2 or 3)
            {
                candidate = handCards
                    .GroupBy(c => c.RankValue)
                    .Where(g => g.Count() >= needed && g.Key > lastMax)
                    .OrderBy(g => g.Key)
                    .Select(g => g.Take(needed).ToList())
                    .FirstOrDefault();
            }

            if (candidate != null)
            {
                _engine.PlayCards(room, bot.UserId, candidate.Select(c => c.Id).ToList());
            }
            else
            {
                _engine.Pass(room, bot.UserId);
            }
        }
    }
}
