using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Tichu.Data;
using Tichu.Models;

namespace Tichu.Services
{
    /// <summary>소셜/이메일 로그인에서 계정을 찾거나 새로 만들어 게임 전역에서 쓰는 uid/닉네임을 돌려준다.</summary>
    public class AccountService
    {
        private readonly IDbContextFactory<TichuDbContext> _dbFactory;
        private readonly PasswordHasher<object> _hasher = new();

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

        /// <summary>이메일+비밀번호로 로그인하거나(계정이 없으면) 새로 등록한다.</summary>
        public (bool success, string? uid, string? nickname, bool isNewAccount, string? error) LoginOrRegisterWithEmail(string email, string password)
        {
            using var db = _dbFactory.CreateDbContext();

            var account = db.Accounts.FirstOrDefault(a => a.Provider == "Email" && a.ProviderUserId == email);
            if (account != null)
            {
                var verify = _hasher.VerifyHashedPassword(new object(), account.PasswordHash ?? "", password);
                if (verify == PasswordVerificationResult.Failed)
                {
                    return (false, null, null, false, "이메일 또는 비밀번호가 올바르지 않습니다.");
                }
                account.LastLoginAt = DateTime.UtcNow;
                db.SaveChanges();
                return (true, account.UserId, account.Nickname, false, null);
            }

            var uid = Guid.NewGuid().ToString("N");
            db.Accounts.Add(new Account
            {
                UserId = uid,
                Provider = "Email",
                ProviderUserId = email,
                Nickname = "",
                PasswordHash = _hasher.HashPassword(new object(), password),
                CreatedAt = DateTime.UtcNow,
                LastLoginAt = DateTime.UtcNow
            });
            db.SaveChanges();

            return (true, uid, "", true, null);
        }

        /// <summary>최초 로그인 후 닉네임 설정 단계에서 호출해 계정의 닉네임을 확정한다.</summary>
        public void SetNickname(string uid, string nickname)
        {
            using var db = _dbFactory.CreateDbContext();
            var account = db.Accounts.FirstOrDefault(a => a.UserId == uid);
            if (account != null)
            {
                account.Nickname = nickname;
                db.SaveChanges();
            }
        }
    }
}
