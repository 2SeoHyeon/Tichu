namespace Tichu.Models
{
    public class Player
    {
        public int Id { get; set; }
        public string UserId { get; set; } = "";
        public string Nickname { get; set; } = "";
        public bool IsReady { get; set; }
    }

}
