using Microsoft.EntityFrameworkCore;
using Tichu.Data;
using Tichu.Models;

namespace Tichu.Services
{
    /// <summary>소셜 로그인 콜백에서 계정을 찾거나 새로 만들어 게임 전역에서 쓰는 uid/닉네임을 돌려준다.</summary>
    public class AccountService
    {
        private readonly IDbContextFactory<TichuDbContext> _dbFactory;

        public AccountService(IDbContextFactory<TichuDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        public (string uid, string nickname) FindOrCreateAccount(string provider, string providerUserId, string suggestedNickname)
        {
            using var db = _dbFactory.CreateDbContext();

            var account = db.Accounts.FirstOrDefault(a => a.Provider == provider && a.ProviderUserId == providerUserId);
            if (account != null)
            {
                account.LastLoginAt = DateTime.UtcNow;
                db.SaveChanges();
                return (account.UserId, account.Nickname);
            }

            var nickname = string.IsNullOrWhiteSpace(suggestedNickname) ? "플레이어" : suggestedNickname.Trim();
            if (nickname.Length > 12) nickname = nickname.Substring(0, 12);

            var uid = Guid.NewGuid().ToString("N");
            db.Accounts.Add(new Account
            {
                UserId = uid,
                Provider = provider,
                ProviderUserId = providerUserId,
                Nickname = nickname,
                CreatedAt = DateTime.UtcNow,
                LastLoginAt = DateTime.UtcNow
            });
            db.SaveChanges();

            return (uid, nickname);
        }
    }
}
