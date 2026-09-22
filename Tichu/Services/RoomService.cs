using System.Collections.Concurrent;
using Tichu.Models;

namespace Tichu.Services
{
    /// <summary>
    /// 방 목록/참가자 관리 (싱글턴, 서버 메모리 저장 - DB 없음)
    /// </summary>
    public class RoomService
    {
        private readonly ConcurrentDictionary<int, GameRoom> _rooms = new();
        private int _nextRoomId = 1000;

        public List<GameRoom> GetRooms()
        {
            return _rooms.Values.OrderBy(r => r.CreatedAt).ToList();
        }

        public GameRoom? GetRoom(int roomId)
        {
            _rooms.TryGetValue(roomId, out var room);
            return room;
        }

        public GameRoom CreateRoom(string name, int targetScore, int turnTime, string hostUserId, string hostNickname)
        {
            var id = Interlocked.Increment(ref _nextRoomId);

            var room = new GameRoom
            {
                Id = id,
                Name = string.IsNullOrWhiteSpace(name) ? "티추 한판~!" : name.Trim(),
                TargetScore = targetScore is 500 or 1000 or 2000 ? targetScore : 1000,
                TurnTimeLimit = Math.Clamp(turnTime, 10, 120),
                HostUserId = hostUserId,
                CreatedAt = DateTime.UtcNow,
                Status = RoomStatus.Waiting
            };

            room.Players.Add(new Player
            {
                UserId = hostUserId,
                Nickname = hostNickname,
                IsHost = true,
                Seat = 0
            });

            _rooms[id] = room;
            return room;
        }

        public (GameRoom? room, string? error) JoinRoom(int roomId, string userId, string nickname, string connectionId)
        {
            if (!_rooms.TryGetValue(roomId, out var room))
                return (null, "존재하지 않는 방입니다.");

            lock (room.Lock)
            {
                var existing = room.Players.FirstOrDefault(p => p.UserId == userId);
                if (existing != null)
                {
                    // 재접속
                    existing.ConnectionId = connectionId;
                    existing.IsConnected = true;
                    return (room, null);
                }

                if (room.Status != RoomStatus.Waiting)
                    return (null, "이미 게임이 시작된 방입니다.");

                if (room.Players.Count >= 4)
                    return (null, "방 인원이 가득 찼습니다.");

                var usedSeats = room.Players.Select(p => p.Seat).ToHashSet();
                int seat = Enumerable.Range(0, 4).First(s => !usedSeats.Contains(s));

                room.Players.Add(new Player
                {
                    UserId = userId,
                    Nickname = nickname,
                    ConnectionId = connectionId,
                    Seat = seat,
                    IsHost = false
                });

                return (room, null);
            }
        }

        public (GameRoom? room, string? error) AddBot(int roomId, string requestingUserId)
        {
            if (!_rooms.TryGetValue(roomId, out var room))
                return (null, "존재하지 않는 방입니다.");

            lock (room.Lock)
            {
                if (room.HostUserId != requestingUserId)
                    return (null, "방장만 AI 봇을 추가할 수 있습니다.");

                if (room.Status != RoomStatus.Waiting)
                    return (null, "이미 게임이 시작된 방입니다.");

                if (room.Players.Count >= 4)
                    return (null, "방 인원이 가득 찼습니다.");

                var usedSeats = room.Players.Select(p => p.Seat).ToHashSet();
                int seat = Enumerable.Range(0, 4).First(s => !usedSeats.Contains(s));
                int botNumber = room.Players.Count(p => p.IsBot) + 1;

                room.Players.Add(new Player
                {
                    UserId = "bot-" + Guid.NewGuid().ToString("N"),
                    Nickname = $"AI 봇 {botNumber}",
                    Seat = seat,
                    IsBot = true,
                    IsReady = true,
                    IsConnected = true
                });

                return (room, null);
            }
        }

        public GameRoom? LeaveRoom(int roomId, string userId)
        {
            if (!_rooms.TryGetValue(roomId, out var room)) return null;

            lock (room.Lock)
            {
                var player = room.Players.FirstOrDefault(p => p.UserId == userId);
                if (player == null) return room;

                if (room.Status == RoomStatus.Waiting)
                {
                    room.Players.Remove(player);

                    if (room.Players.Count == 0)
                    {
                        _rooms.TryRemove(roomId, out _);
                        return null;
                    }

                    if (player.IsHost)
                    {
                        var newHost = room.Players.OrderBy(p => p.Seat).First();
                        newHost.IsHost = true;
                        room.HostUserId = newHost.UserId;
                    }
                }
                else
                {
                    player.IsConnected = false;
                }

                return room;
            }
        }

        public GameRoom? SetConnectionState(string userId, string connectionId, bool connected)
        {
            var room = _rooms.Values.FirstOrDefault(r => r.Players.Any(p => p.ConnectionId == connectionId && p.UserId == userId));
            if (room == null) return null;

            lock (room.Lock)
            {
                var player = room.Players.FirstOrDefault(p => p.UserId == userId);
                if (player != null) player.IsConnected = connected;
            }
            return room;
        }

        public GameRoom? FindRoomByConnection(string connectionId)
        {
            return _rooms.Values.FirstOrDefault(r => r.Players.Any(p => p.ConnectionId == connectionId));
        }

        public bool ToggleReady(int roomId, string userId)
        {
            if (!_rooms.TryGetValue(roomId, out var room)) return false;
            lock (room.Lock)
            {
                var player = room.Players.FirstOrDefault(p => p.UserId == userId);
                if (player == null) return false;
                player.IsReady = !player.IsReady;
                return true;
            }
        }

        public void RemoveRoomIfEmpty(int roomId)
        {
            if (_rooms.TryGetValue(roomId, out var room) && room.Players.Count == 0)
            {
                _rooms.TryRemove(roomId, out _);
            }
        }

        public List<RoomListItemDto> GetRoomListDtos()
        {
            return GetRooms().Select(r => new RoomListItemDto
            {
                Id = r.Id,
                Name = r.Name,
                TargetScore = r.TargetScore,
                TurnTimeLimit = r.TurnTimeLimit,
                Status = r.Status.ToString(),
                PlayerCount = r.Players.Count
            }).ToList();
        }

        public RoomStateDto BuildRoomStateDto(GameRoom room, string viewerUserId)
        {
            var dto = new RoomStateDto
            {
                RoomId = room.Id,
                RoomName = room.Name,
                TargetScore = room.TargetScore,
                TurnTimeLimit = room.TurnTimeLimit,
                Status = room.Status.ToString(),
                HostUserId = room.HostUserId,
                Players = room.Players
                    .OrderBy(p => p.Seat)
                    .Select(p => new PlayerPublicDto
                    {
                        UserId = p.UserId,
                        Nickname = p.Nickname,
                        Seat = p.Seat,
                        TeamId = p.TeamId,
                        IsHost = p.IsHost,
                        IsReady = p.IsReady,
                        IsBot = p.IsBot,
                        IsConnected = p.IsConnected,
                        HandCount = p.Hand.Count,
                        CalledTichu = p.CalledTichu,
                        HasFinished = p.HasFinishedThisRound,
                        FinishPosition = p.FinishPosition
                    }).ToList()
            };

            var game = room.Game;
            if (game != null)
            {
                dto.Phase = game.Phase.ToString();
                dto.RoundNumber = game.RoundNumber;
                dto.TeamScores = game.TeamScores;
                dto.CurrentTurnSeat = game.CurrentTurnSeat;
                dto.LastPlay = game.LastPlay.Select(CardDto.From).ToList();
                dto.LastPlayerSeat = game.LastPlayerSeat;
                dto.DragonTrickPending = game.DragonTrickPending;
                dto.ExchangeSubmittedSeats = game.PendingExchange.Keys.ToList();
                dto.LastMessage = game.LastMessage;
                dto.History = game.History;

                var me = room.Players.FirstOrDefault(p => p.UserId == viewerUserId);
                if (me != null)
                {
                    dto.MySeat = me.Seat;
                    dto.MyHand = me.Hand.OrderBy(c => c.RankValue).Select(CardDto.From).ToList();
                }
            }

            return dto;
        }
    }
}
