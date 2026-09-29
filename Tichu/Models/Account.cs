namespace Tichu.Models
{
    /// <summary>소셜 로그인(Google/Naver/Kakao)으로 연결된 계정. Provider+ProviderUserId 조합이 UserId(tichu_uid)로 이어진다.</summary>
    public class Account
    {
        public int Id { get; set; }
        public string UserId { get; set; } = "";
        public string Provider { get; set; } = "";
        public string ProviderUserId { get; set; } = "";
        public string Nickname { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime LastLoginAt { get; set; }
    }
}
