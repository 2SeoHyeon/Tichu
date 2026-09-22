using Microsoft.AspNetCore.SignalR;
using Tichu.Hub;
using Tichu.Models;

namespace Tichu.Services
{
    /// <summary>
    /// 방 상태를 각 플레이어에게 (자신의 손패 포함) 개별 전송하고,
    /// 로비 목록 갱신을 모든 로비 접속자에게 브로드캐스트한다.
    /// IHubContext를 쓰므로 Hub 인스턴스의 생명주기와 무관하게 어디서든 호출 가능.
    /// </summary>
    public class RoomBroadcaster
    {
        private readonly IHubContext<TichuHub> _hub;
        private readonly RoomService _roomService;

        public RoomBroadcaster(IHubContext<TichuHub> hub, RoomService roomService)
        {
            _hub = hub;
            _roomService = roomService;
        }

        public async Task BroadcastRoomAsync(GameRoom room)
        {
            List<(string connectionId, RoomStateDto dto)> sends;

            lock (room.Lock)
            {
                sends = room.Players
                    .Where(p => !string.IsNullOrEmpty(p.ConnectionId))
                    .Select(p => (p.ConnectionId, _roomService.BuildRoomStateDto(room, p.UserId)))
                    .ToList();
            }

            foreach (var (connectionId, dto) in sends)
            {
                await _hub.Clients.Client(connectionId).SendAsync("RoomState", dto);
            }
        }

        public async Task BroadcastLobbyAsync()
        {
            var list = _roomService.GetRoomListDtos();
            await _hub.Clients.Group("lobby").SendAsync("RoomListUpdated", list);
        }
    }
}
