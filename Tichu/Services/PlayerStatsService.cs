using System.Collections.Concurrent;
using System.Text.Json;
using Tichu.Models;

namespace Tichu.Services
{
    /// <summary>
    /// 플레이어 전적(게임 수/승수)을 닉네임 쿠키의 uid 기준으로 저장한다.
    /// 서버 프로세스가 켜져 있는 동안은 메모리 + 파일(App_Data/player_stats.json)로
    /// 유지되지만, 렌더 같은 호스팅의 디스크가 배포마다 초기화되는 환경이라면
    /// 새 배포 시 초기화될 수 있다 (영구 보존하려면 별도 DB 연결이 필요).
    /// </summary>
    public class PlayerStatsService
    {
        private readonly ConcurrentDictionary<string, PlayerStats> _stats = new();
        private readonly string _filePath;
        private readonly object _fileLock = new();

        public PlayerStatsService(IHostEnvironment env) : this(env.ContentRootPath)
        {
        }

        /// <summary>DI 없이(테스트 등) 콘텐츠 루트 경로를 직접 넘겨 생성할 때 사용</summary>
        public PlayerStatsService(string contentRootPath)
        {
            var dir = Path.Combine(contentRootPath, "App_Data");
            Directory.CreateDirectory(dir);
            _filePath = Path.Combine(dir, "player_stats.json");
            Load();
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_filePath)) return;
                var json = File.ReadAllText(_filePath);
                var list = JsonSerializer.Deserialize<List<PlayerStats>>(json);
                if (list == null) return;
                foreach (var s in list) _stats[s.UserId] = s;
            }
            catch
            {
                // 손상된 파일 등은 무시하고 빈 상태로 시작
            }
        }

        private void Save()
        {
            lock (_fileLock)
            {
                try
                {
                    var json = JsonSerializer.Serialize(_stats.Values.ToList(), new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(_filePath, json);
                }
                catch
                {
                    // 디스크 쓰기 실패해도 게임 진행에는 영향 없게 무시
                }
            }
        }

        public PlayerStats GetStats(string userId)
        {
            return _stats.TryGetValue(userId, out var s)
                ? s
                : new PlayerStats { UserId = userId, Nickname = "", GamesPlayed = 0, GamesWon = 0 };
        }

        /// <summary>봇이 아닌 실제 유저들의 이번 판 결과를 반영한다.</summary>
        public void RecordGameResult(IEnumerable<(string userId, string nickname, bool won)> results)
        {
            foreach (var (userId, nickname, won) in results)
            {
                _stats.AddOrUpdate(userId,
                    _ => new PlayerStats { UserId = userId, Nickname = nickname, GamesPlayed = 1, GamesWon = won ? 1 : 0, UpdatedAt = DateTime.UtcNow },
                    (_, existing) =>
                    {
                        existing.Nickname = nickname;
                        existing.GamesPlayed += 1;
                        if (won) existing.GamesWon += 1;
                        existing.UpdatedAt = DateTime.UtcNow;
                        return existing;
                    });
            }
            Save();
        }

        public List<PlayerStats> GetTopPlayers(int count)
        {
            return _stats.Values
                .Where(s => s.GamesPlayed > 0)
                .OrderByDescending(s => s.GamesWon)
                .ThenByDescending(s => s.GamesPlayed == 0 ? 0 : (double)s.GamesWon / s.GamesPlayed)
                .Take(count)
                .ToList();
        }
    }
}
