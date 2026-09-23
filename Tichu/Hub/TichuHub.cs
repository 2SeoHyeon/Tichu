using Microsoft.AspNetCore.SignalR;
using Tichu.Models;
using Tichu.Services;

namespace Tichu.Hub
{
    public class ExchangePickDto
    {
        public int CardId { get; set; }
        public int ToSeat { get; set; }
    }

    public class TichuHub : Microsoft.AspNetCore.SignalR.Hub
    {
        private readonly RoomService _roomService;
        private readonly GameEngine _engine;
        private readonly RoomBroadcaster _broadcaster;
        private readonly TurnTimerCoordinator _timers;

        public TichuHub(RoomService roomService, GameEngine engine, RoomBroadcaster broadcaster, TurnTimerCoordinator timers)
        {
            _roomService = roomService;
            _engine = engine;
            _broadcaster = broadcaster;
            _timers = timers;
        }

        // ---------------- 로비 ----------------

        public async Task JoinLobby()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "lobby");
            await Clients.Caller.SendAsync("RoomListUpdated", _roomService.GetRoomListDtos());
        }

        public async Task LeaveLobby()
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, "lobby");
        }

        // ---------------- 방 ----------------

        public async Task JoinRoom(int roomId, string userId, string nickname)
        {
            var (room, error) = _roomService.JoinRoom(roomId, userId, nickname, Context.ConnectionId);
            if (room == null)
            {
                await Clients.Caller.SendAsync("ErrorMessage", error ?? "방에 참가할 수 없습니다.");
                return;
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, $"room_{roomId}");
            await _broadcaster.BroadcastRoomAsync(room);
            await _broadcaster.BroadcastLobbyAsync();
            _timers.Schedule(room);
        }

        public async Task LeaveRoom(int roomId, string userId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"room_{roomId}");
            var room = _roomService.LeaveRoom(roomId, userId);
            if (room != null)
            {
                if (!_roomService.HasConnectedHuman(room))
                {
                    _timers.Stop(room.Id);
                    _roomService.DeleteRoom(room.Id);
                }
                else
                {
                    await _broadcaster.BroadcastRoomAsync(room);
                }
            }
            await _broadcaster.BroadcastLobbyAsync();
        }

        public async Task AddBot(int roomId, string userId)
        {
            var (room, error) = _roomService.AddBot(roomId, userId);
            if (room == null)
            {
                await Clients.Caller.SendAsync("ErrorMessage", error ?? "AI 봇을 추가할 수 없습니다.");
                return;
            }

            await _broadcaster.BroadcastRoomAsync(room);
            await _broadcaster.BroadcastLobbyAsync();
        }

        public async Task KickPlayer(int roomId, string userId, string targetUserId)
        {
            var (room, error, kickedConnectionId) = _roomService.KickPlayer(roomId, userId, targetUserId);
            if (error != null)
            {
                await Clients.Caller.SendAsync("ErrorMessage", error);
                return;
            }

            if (!string.IsNullOrEmpty(kickedConnectionId))
            {
                await Clients.Client(kickedConnectionId).SendAsync("Kicked");
                await Groups.RemoveFromGroupAsync(kickedConnectionId, $"room_{roomId}");
            }

            if (room != null)
            {
                if (!_roomService.HasConnectedHuman(room))
                {
                    _timers.Stop(room.Id);
                    _roomService.DeleteRoom(room.Id);
                }
                else
                {
                    await _broadcaster.BroadcastRoomAsync(room);
                    _timers.Schedule(room);
                }
            }
            await _broadcaster.BroadcastLobbyAsync();
        }

        public async Task ToggleReady(int roomId, string userId)
        {
            if (_roomService.ToggleReady(roomId, userId))
            {
                var room = _roomService.GetRoom(roomId);
                if (room != null) await _broadcaster.BroadcastRoomAsync(room);
            }
        }

        public async Task SendChat(int roomId, string userId, string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            var room = _roomService.GetRoom(roomId);
            var nickname = room?.Players.FirstOrDefault(p => p.UserId == userId)?.Nickname ?? "???";
            var text = message.Length > 300 ? message[..300] : message;
            await Clients.Group($"room_{roomId}").SendAsync("ChatMessage", nickname, text);
        }

        // ---------------- 게임 진행 ----------------

        public async Task StartGame(int roomId, string userId)
        {
            var room = _roomService.GetRoom(roomId);
            if (room == null) return;

            string? error;
            lock (room.Lock) { error = _engine.StartGame(room, userId); }

            if (error != null) { await Clients.Caller.SendAsync("ErrorMessage", error); return; }

            await _broadcaster.BroadcastRoomAsync(room);
            await _broadcaster.BroadcastLobbyAsync();
        }

        public async Task CallTichu(int roomId, string userId)
        {
            await RunEngineAction(roomId, room => _engine.CallTichu(room, userId));
        }

        public async Task CallGrandTichu(int roomId, string userId)
        {
            await RunEngineAction(roomId, room => _engine.CallGrandTichu(room, userId));
        }

        public async Task SubmitExchange(int roomId, string userId, List<ExchangePickDto> picks)
        {
            var list = (picks ?? new List<ExchangePickDto>()).Select(p => (p.CardId, p.ToSeat)).ToList();
            await RunEngineAction(roomId, room => _engine.SubmitExchange(room, userId, list));
        }

        public async Task PlayCards(int roomId, string userId, List<int> cardIds, int? mahjongWish = null)
        {
            await RunEngineAction(roomId, room => _engine.PlayCards(room, userId, cardIds, mahjongWish));
        }

        public async Task Pass(int roomId, string userId)
        {
            await RunEngineAction(roomId, room => _engine.Pass(room, userId));
        }

        public async Task GiveDragonTrick(int roomId, string userId, int targetSeat)
        {
            await RunEngineAction(roomId, room => _engine.GiveDragonTrick(room, userId, targetSeat));
        }

        public async Task StartNextRound(int roomId, string userId)
        {
            await RunEngineAction(roomId, room => _engine.StartNextRound(room, userId));
        }

        private async Task RunEngineAction(int roomId, Func<GameRoom, string?> action)
        {
            var room = _roomService.GetRoom(roomId);
            if (room == null) return;

            string? error;
            lock (room.Lock) { error = action(room); }

            if (error != null)
            {
                await Clients.Caller.SendAsync("ErrorMessage", error);
                return;
            }

            await _broadcaster.BroadcastRoomAsync(room);
            if (room.Status == RoomStatus.Ended)
            {
                await _broadcaster.BroadcastLobbyAsync();
            }
            _timers.Schedule(room);
        }

        // ---------------- 연결 해제 ----------------

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var room = _roomService.FindRoomByConnection(Context.ConnectionId);
            if (room != null)
            {
                var player = room.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId);
                if (player != null)
                {
                    GameRoom? remaining;
                    if (room.Status == RoomStatus.Waiting)
                    {
                        remaining = _roomService.LeaveRoom(room.Id, player.UserId);
                    }
                    else
                    {
                        lock (room.Lock) { player.IsConnected = false; }
                        remaining = room;
                    }

                    if (remaining != null)
                    {
                        if (!_roomService.HasConnectedHuman(remaining))
                        {
                            _timers.Stop(remaining.Id);
                            _roomService.DeleteRoom(remaining.Id);
                        }
                        else
                        {
                            await _broadcaster.BroadcastRoomAsync(remaining);
                        }
                    }
                    await _broadcaster.BroadcastLobbyAsync();
                }
            }

            await base.OnDisconnectedAsync(exception);
        }
    }
}
