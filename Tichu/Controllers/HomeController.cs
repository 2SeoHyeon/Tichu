using System.Diagnostics;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Tichu.Models;
using Tichu.Services;

namespace Tichu.Controllers
{
    public class HomeController : Controller
    {
        public const string UidCookie = "tichu_uid";
        public const string NickCookie = "tichu_nick";

        private readonly PlayerStatsService _stats;
        private readonly IConfiguration _config;
        private readonly AccountService _accounts;

        public HomeController(PlayerStatsService stats, IConfiguration config, AccountService accounts)
        {
            _stats = stats;
            _config = config;
            _accounts = accounts;
        }

        public IActionResult Index()
        {
            var (uid, nick) = ReadIdentity();
            if (uid != null && nick != null)
            {
                return RedirectToAction("Index", "Lobby");
            }

            SetSocialLoginViewBag();
            return View();
        }

        private void SetSocialLoginViewBag()
        {
            bool googleEnabled = !string.IsNullOrEmpty(_config["Auth:Google:ClientId"]);
            bool naverEnabled = !string.IsNullOrEmpty(_config["Auth:Naver:ClientId"]);
            bool kakaoEnabled = !string.IsNullOrEmpty(_config["Auth:Kakao:ClientId"]);
            ViewBag.GoogleEnabled = googleEnabled;
            ViewBag.NaverEnabled = naverEnabled;
            ViewBag.KakaoEnabled = kakaoEnabled;
            ViewBag.SocialLoginEnabled = googleEnabled || naverEnabled || kakaoEnabled;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult EmailAuth(string email, string password)
        {
            email = (email ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(email) || !email.Contains('@') || !email.Contains('.'))
            {
                ViewBag.Error = "올바른 이메일을 입력해주세요.";
                SetSocialLoginViewBag();
                return View("Index");
            }
            if (string.IsNullOrEmpty(password) || password.Length < 6)
            {
                ViewBag.Error = "비밀번호는 6자 이상이어야 합니다.";
                SetSocialLoginViewBag();
                return View("Index");
            }

            var (success, uid, nickname, isNewAccount, error) = _accounts.LoginOrRegisterWithEmail(email, password);
            if (!success)
            {
                ViewBag.Error = error;
                SetSocialLoginViewBag();
                return View("Index");
            }

            var cookieOptions = new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddDays(7),
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                IsEssential = true
            };
            Response.Cookies.Append(UidCookie, uid!, cookieOptions);

            if (isNewAccount || string.IsNullOrEmpty(nickname))
            {
                return RedirectToAction("SetupNickname");
            }

            Response.Cookies.Append(NickCookie, nickname!, cookieOptions);
            return RedirectToAction("Index", "Lobby");
        }

        [HttpGet]
        public IActionResult SetupNickname()
        {
            var uid = Request.Cookies.TryGetValue(UidCookie, out var u) ? u : null;
            if (string.IsNullOrWhiteSpace(uid)) return RedirectToAction("Index");

            var (_, nick) = ReadIdentity();
            if (nick != null) return RedirectToAction("Index", "Lobby");

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SetupNickname(string nickname)
        {
            var uid = Request.Cookies.TryGetValue(UidCookie, out var u) ? u : null;
            if (string.IsNullOrWhiteSpace(uid)) return RedirectToAction("Index");

            nickname = (nickname ?? "").Trim();
            if (nickname.Length == 0 || nickname.Length > 12)
            {
                ViewBag.Error = "닉네임은 1~12자로 입력해주세요.";
                return View();
            }

            _accounts.SetNickname(uid!, nickname);

            var cookieOptions = new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddDays(7),
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                IsEssential = true
            };
            Response.Cookies.Append(NickCookie, nickname, cookieOptions);

            return RedirectToAction("Index", "Lobby");
        }

        [HttpGet]
        public IActionResult ExternalLogin(string provider)
        {
            var props = new AuthenticationProperties { RedirectUri = "/Lobby" };
            return Challenge(props, provider);
        }

        public IActionResult MyPage()
        {
            var (uid, nick) = ReadIdentity();
            if (uid == null || nick == null) return RedirectToAction("Index");

            ViewBag.Nickname = nick;
            ViewBag.MyStats = _stats.GetStats(uid);
            ViewBag.TopPlayers = _stats.GetTopPlayers(5);
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult UpdateNickname(string nickname)
        {
            var (uid, _) = ReadIdentity();
            if (uid == null) return RedirectToAction("Index");

            nickname = (nickname ?? "").Trim();
            if (nickname.Length == 0 || nickname.Length > 12)
            {
                ViewBag.Error = "닉네임은 1~12자로 입력해주세요.";
                ViewBag.Nickname = nickname;
                return View("MyPage");
            }

            var cookieOptions = new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddDays(7),
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                IsEssential = true
            };
            Response.Cookies.Append(UidCookie, uid, cookieOptions);
            Response.Cookies.Append(NickCookie, nickname, cookieOptions);

            TempData["Saved"] = true;
            return RedirectToAction("MyPage");
        }

        public IActionResult ChangeNickname()
        {
            Response.Cookies.Delete(UidCookie);
            Response.Cookies.Delete(NickCookie);
            return RedirectToAction("Index");
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View("~/Views/Shared/Error.cshtml", new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        private (string? uid, string? nick) ReadIdentity()
        {
            var uid = Request.Cookies.TryGetValue(UidCookie, out var u) ? u : null;
            var nick = Request.Cookies.TryGetValue(NickCookie, out var n) ? n : null;
            return (string.IsNullOrWhiteSpace(uid) ? null : uid, string.IsNullOrWhiteSpace(nick) ? null : nick);
        }
    }
}
