using Microsoft.EntityFrameworkCore;
using Tichu.Data;
using Tichu.Models;

namespace Tichu.Services
{
    /// <summary>플레이어 전적(게임 수/승수)을 DB(Postgres 또는 로컬 SQLite)에 저장한다.</summary>
    public class PlayerStatsService
    {
        private readonly IDbContextFactory<TichuDbContext> _dbFactory;

        public PlayerStatsService(IDbContextFactory<TichuDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        public PlayerStats GetStats(string userId)
        {
            using var db = _dbFactory.CreateDbContext();
            return db.PlayerStats.Find(userId)
                ?? new PlayerStats { UserId = userId, Nickname = "", GamesPlayed = 0, GamesWon = 0 };
        }

        /// <summary>봇이 아닌 실제 유저들의 이번 판 결과를 반영한다.</summary>
        public void RecordGameResult(IEnumerable<(string userId, string nickname, bool won)> results)
        {
            using var db = _dbFactory.CreateDbContext();
            foreach (var (userId, nickname, won) in results)
            {
                var existing = db.PlayerStats.Find(userId);
                if (existing == null)
                {
                    db.PlayerStats.Add(new PlayerStats
                    {
                        UserId = userId,
                        Nickname = nickname,
                        GamesPlayed = 1,
                        GamesWon = won ? 1 : 0,
                        UpdatedAt = DateTime.UtcNow
                    });
                }
                else
                {
                    existing.Nickname = nickname;
                    existing.GamesPlayed += 1;
                    if (won) existing.GamesWon += 1;
                    existing.UpdatedAt = DateTime.UtcNow;
                }
            }
            db.SaveChanges();
        }

        public List<PlayerStats> GetTopPlayers(int count)
        {
            using var db = _dbFactory.CreateDbContext();
            return db.PlayerStats
                .Where(s => s.GamesPlayed > 0)
                .AsEnumerable()
                .OrderByDescending(s => s.GamesWon)
                .ThenByDescending(s => s.GamesPlayed == 0 ? 0 : (double)s.GamesWon / s.GamesPlayed)
                .Take(count)
                .ToList();
        }
    }
}
