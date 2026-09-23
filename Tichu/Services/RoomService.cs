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
        private readonly GameEngine _engine;

        public RoomService(GameEngine engine)
        {
            _engine = engine;
        }

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

        /// <summary>방장이 다른 플레이어를 강퇴. 대기 중이면 자리에서 완전히 제거하고,
        /// 게임 중이면 연결이 끊긴 것과 동일하게 처리해서(자리는 유지) 기존 타임아웃/봇 로직이
        /// 그대로 이어받아 게임이 끊기지 않게 한다.</summary>
        public (GameRoom? room, string? error, string? kickedConnectionId) KickPlayer(int roomId, string requestingUserId, string targetUserId)
        {
            if (!_rooms.TryGetValue(roomId, out var room)) return (null, "존재하지 않는 방입니다.", null);

            lock (room.Lock)
            {
                if (room.HostUserId != requestingUserId) return (null, "방장만 강퇴할 수 있습니다.", null);
                if (requestingUserId == targetUserId) return (null, "자기 자신은 강퇴할 수 없습니다.", null);

                var target = room.Players.FirstOrDefault(p => p.UserId == targetUserId);
                if (target == null) return (null, "대상을 찾을 수 없습니다.", null);
                // 대기 중인 봇은 자리를 비우기 위해 제거할 수 있지만, 게임 중인 봇은
                // 제거하면 진행 중인 라운드(손패/턴 순서)가 깨지므로 막는다.
                if (target.IsBot && room.Status != RoomStatus.Waiting) return (null, "게임 중인 AI 봇은 강퇴할 수 없습니다.", null);

                var kickedConnectionId = target.ConnectionId;

                if (room.Status == RoomStatus.Waiting)
                {
                    room.Players.Remove(target);

                    if (room.Players.Count == 0)
                    {
                        _rooms.TryRemove(roomId, out _);
                        return (null, null, kickedConnectionId);
                    }

                    if (target.IsHost)
                    {
                        var newHost = room.Players.OrderBy(p => p.Seat).First();
                        newHost.IsHost = true;
                        room.HostUserId = newHost.UserId;
                    }
                }
                else
                {
                    target.IsConnected = false;
                }

                return (room, null, kickedConnectionId);
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

        /// <summary>실제 사람(봇 아님)이 한 명이라도 연결되어 있는지</summary>
        public bool HasConnectedHuman(GameRoom room)
        {
            lock (room.Lock)
            {
                return room.Players.Any(p => !p.IsBot && p.IsConnected);
            }
        }

        /// <summary>방을 즉시 제거한다. 진행 중이던 게임/타이머/봇 루프도 멈추도록 상태를 Abandoned로 바꾼다.</summary>
        public GameRoom? DeleteRoom(int roomId)
        {
            if (_rooms.TryRemove(roomId, out var room))
            {
                lock (room.Lock) { room.Status = RoomStatus.Abandoned; }
                return room;
            }
            return null;
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
                        TichuCall = p.TichuCall.ToString(),
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
                dto.LastPlayEffectiveValue = game.LastPlay.Count == 1 ? _engine.GetEffectiveLastSingleValue(game) : null;
                dto.DragonTrickPending = game.DragonTrickPending;
                dto.LastDogFromSeat = game.LastDogFromSeat;
                dto.LastDogToSeat = game.LastDogToSeat;
                dto.DogMoveSeq = game.DogMoveSeq;
                dto.ExchangeSubmittedSeats = game.PendingExchange.Keys.ToList();
                dto.MahjongWish = game.MahjongWish;
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
