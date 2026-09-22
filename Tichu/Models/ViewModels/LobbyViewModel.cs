namespace Tichu.Models.ViewModels
{
    public class LobbyViewModel
    {
        public string Nickname { get; set; } = "";
        public List<GameRoom> Rooms { get; set; } = new();
    }
}
