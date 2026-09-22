namespace Tichu.Models
{
    public enum GamePhase
    {
        Exchange,   // 카드 교환 단계
        Playing,    // 실제 플레이 단계
        RoundEnd    // 라운드 종료 (다음 라운드 대기)
    }

    public class ExchangeCard
    {
        public int ToSeat { get; set; }
        public Card Card { get; set; } = null!;
    }

    public class RoundLogEntry
    {
        public int Round { get; set; }
        public int Team0Gain { get; set; }
        public int Team1Gain { get; set; }
        public string Note { get; set; } = "";
    }

    public class GameState
    {
        public GamePhase Phase { get; set; } = GamePhase.Exchange;
        public int RoundNumber { get; set; } = 1;

        public Dictionary<int, int> TeamScores { get; set; } = new() { { 0, 0 }, { 1, 0 } };

        /// <summary>좌석별 교환 카드 제출 (아직 4명 모두 제출 전)</summary>
        public Dictionary<int, List<ExchangeCard>> PendingExchange { get; set; } = new();

        public List<Card> LastPlay { get; set; } = new();
        public int? LastPlayerSeat { get; set; }
        public int CurrentTurnSeat { get; set; }
        public int PassStreak { get; set; }

        /// <summary>현재 트릭에 쌓인 카드 (승자가 가져갈 점수 카드들)</summary>
        public List<Card> CurrentTrickCards { get; set; } = new();

        public List<int> FinishedSeatOrder { get; set; } = new();

        public bool DragonTrickPending { get; set; }

        public List<RoundLogEntry> History { get; set; } = new();

        public string LastMessage { get; set; } = "";
    }
}
