using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Tichu.Models;

namespace Tichu.Controllers;

public class LobbyController : Controller
{
    private readonly ILogger<LobbyController> _logger;

    public LobbyController(ILogger<LobbyController> logger)
    {
        _logger = logger;
    }

    public IActionResult Index()
    {
        List<GameRoom> list = new List<GameRoom>();

        return View(list);
    }

    public IActionResult Create()
    {
        GameRoom model = new GameRoom();

        return View(model);
    }

    public IActionResult Room()
    {
        GameRoom model = new GameRoom();

        return View(model);
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
