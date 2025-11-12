using Tichu.Models;
using Microsoft.EntityFrameworkCore;

namespace Tichu.Services
{
    public class RoomService
    {
        public RoomService()
        {
        }

        /// <summary>
        /// 모든 방 목록 조회
        /// </summary>
        public async Task<List<GameRoom>> GetRoomsAsync()
        {
            List<GameRoom> list = new List<GameRoom>();

            return list;
        }

        /// <summary>
        /// 새 방 생성
        /// </summary>
        public async Task<GameRoom> CreateRoomAsync(string name, int targetScore, int turnTime, string hostUserId)
        {
            var room = new GameRoom
            {
                Name = name ?? "티추 한판~!",
                TargetScore = targetScore,
                TurnTimeLimit = turnTime,
                HostUserId = hostUserId,
                CreatedAt = DateTime.Now,
                IsStarted = false
            };

            return room;
        }

        /// <summary>
        /// 방 참가
        /// </summary>
        public async Task<GameRoom?> JoinRoomAsync(int roomId, string userId, string nickname)
        {
            GameRoom room = new GameRoom();

            if (room == null || room.IsStarted || room.Players.Count >= 4)
                return null;

            if (!room.Players.Any(p => p.UserId == userId))
            {
                room.Players.Add(new Player
                {
                    UserId = userId,
                    Nickname = nickname,
                    IsReady = false
                });
            }

            return room;
        }

        /// <summary>
        /// 방 삭제
        /// </summary>
        public async Task DeleteRoomAsync(int roomId)
        {

        }
    }
}