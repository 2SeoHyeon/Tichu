using Microsoft.AspNetCore.Mvc;
using Tichu.Models;
using Tichu.Models.ViewModels;
using Tichu.Services;

namespace Tichu.Controllers
{
    public class LobbyController : Controller
    {
        private readonly RoomService _roomService;

        public LobbyController(RoomService roomService)
        {
            _roomService = roomService;
        }

        public IActionResult Index()
        {
            var identity = RequireIdentity();
            if (identity == null) return RedirectToAction("Index", "Home");

            var vm = new LobbyViewModel
            {
                Nickname = identity.Value.nick,
                Rooms = _roomService.GetRooms()
            };
            return View(vm);
        }

        [HttpGet]
        public IActionResult Create()
        {
            if (RequireIdentity() == null) return RedirectToAction("Index", "Home");
            return View(new GameRoom());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(string name, int targetScore, int turnTimeLimit)
        {
            var identity = RequireIdentity();
            if (identity == null) return RedirectToAction("Index", "Home");

            var room = _roomService.CreateRoom(name, targetScore, turnTimeLimit, identity.Value.uid, identity.Value.nick);
            return RedirectToAction("Room", new { id = room.Id });
        }

        [HttpGet]
        public IActionResult Room(int id)
        {
            var identity = RequireIdentity();
            if (identity == null) return RedirectToAction("Index", "Home");

            var room = _roomService.GetRoom(id);
            if (room == null)
            {
                TempData["Error"] = "존재하지 않는 방입니다.";
                return RedirectToAction("Index");
            }

            var isHost = room.HostUserId == identity.Value.uid;
            var vm = new GamePlayViewModel
            {
                RoomId = room.Id,
                RoomName = room.Name,
                TurnTimeLimit = room.TurnTimeLimit,
                IsHost = isHost,
                Nickname = identity.Value.nick,
                UserId = identity.Value.uid
            };
            return View(vm);
        }

        private (string uid, string nick)? RequireIdentity()
        {
            var uid = Request.Cookies.TryGetValue(HomeController.UidCookie, out var u) ? u : null;
            var nick = Request.Cookies.TryGetValue(HomeController.NickCookie, out var n) ? n : null;
            if (string.IsNullOrWhiteSpace(uid) || string.IsNullOrWhiteSpace(nick)) return null;
            return (uid, nick);
        }
    }
}
