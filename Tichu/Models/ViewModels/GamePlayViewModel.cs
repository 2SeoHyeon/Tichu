namespace Tichu.Models.ViewModels
{
    public class GamePlayViewModel
    {
        public int RoomId { get; set; }
        public string RoomName { get; set; } = "";
        public int TurnTimeLimit { get; set; } = 30;   // 초
        public bool IsHost { get; set; }
        public string Nickname { get; set; } = "";
        public string UserId { get; set; } = "";
    }
}
