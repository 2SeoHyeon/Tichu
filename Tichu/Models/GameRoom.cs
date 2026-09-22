namespace Tichu.Models
{
    public enum RoomStatus
    {
        Waiting,
        Playing,
        Ended,
        Abandoned  // 실제 유저가 모두 나가서 정리된 방 (봇만 남아 무한 진행되는 것을 막기 위함)
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
