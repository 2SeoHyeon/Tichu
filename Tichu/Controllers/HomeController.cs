using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Tichu.Models;

namespace Tichu.Controllers
{
    public class HomeController : Controller
    {
        public const string UidCookie = "tichu_uid";
        public const string NickCookie = "tichu_nick";

        public IActionResult Index()
        {
            var (uid, nick) = ReadIdentity();
            if (uid != null && nick != null)
            {
                return RedirectToAction("Index", "Lobby");
            }
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Enter(string nickname)
        {
            nickname = (nickname ?? "").Trim();
            if (nickname.Length == 0 || nickname.Length > 12)
            {
                ViewBag.Error = "닉네임은 1~12자로 입력해주세요.";
                return View("Index");
            }

            var (existingUid, _) = ReadIdentity();
            var uid = existingUid ?? Guid.NewGuid().ToString("N");

            var cookieOptions = new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddDays(7),
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                IsEssential = true
            };

            Response.Cookies.Append(UidCookie, uid, cookieOptions);
            Response.Cookies.Append(NickCookie, nickname, cookieOptions);

            return RedirectToAction("Index", "Lobby");
        }

        public IActionResult MyPage()
        {
            var (uid, nick) = ReadIdentity();
            if (uid == null || nick == null) return RedirectToAction("Index");

            ViewBag.Nickname = nick;
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
