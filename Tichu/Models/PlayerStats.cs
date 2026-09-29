namespace Tichu.Models
{
    public class PlayerStats
    {
        public string UserId { get; set; } = "";
        public string Nickname { get; set; } = "";
        public int GamesPlayed { get; set; }
        public int GamesWon { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
