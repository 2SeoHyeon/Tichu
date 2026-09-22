namespace Tichu.Models
{
    public enum RoomStatus
    {
        Waiting,
        Playing,
        Ended
    }

    public class GameRoom
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int TargetScore { get; set; } = 1000;
        public int TurnTimeLimit { get; set; } = 30;
        public RoomStatus Status { get; set; } = RoomStatus.Waiting;
        public string HostUserId { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public List<Player> Players { get; set; } = new();

        public GameState? Game { get; set; }

        public readonly object Lock = new();
    }
}
