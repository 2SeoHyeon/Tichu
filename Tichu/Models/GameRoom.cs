namespace Tichu.Models
{
    public class GameRoom
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int TargetScore { get; set; } = 1000;
        public int TurnTimeLimit { get; set; } = 30;
        public bool IsStarted { get; set; }
        public string HostUserId { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public List<Player> Players { get; set; } = new();
    }
}