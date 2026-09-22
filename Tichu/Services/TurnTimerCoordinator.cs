using Tichu.Models;

namespace Tichu.Services
{
    /// <summary>
    /// 턴 제한시간 타이머를 방 단위로 예약/재예약한다.
    /// 시간 초과 시 GameEngine.HandleTimeout으로 자동 패스/자동 플레이 처리 후 상태를 브로드캐스트하고,
    /// 다음 턴을 위한 타이머를 다시 예약한다.
    /// </summary>
    public class TurnTimerCoordinator
    {
        private readonly TimerService _timerService;
        private readonly GameEngine _engine;
        private readonly RoomBroadcaster _broadcaster;

        public TurnTimerCoordinator(TimerService timerService, GameEngine engine, RoomBroadcaster broadcaster)
        {
            _timerService = timerService;
            _engine = engine;
            _broadcaster = broadcaster;
        }

        public void Schedule(GameRoom room)
        {
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
    }
}
