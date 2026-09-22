namespace Tichu.Models
{
    public enum TichuCallType
    {
        None,
        Small,  // 스몰 티츄: 교환 단계부터 첫 카드를 내기 전까지 선언 가능, ±100점
        Grand   // 라지(그랜드) 티츄: 교환하기 전에만 선언 가능, ±200점
    }

    public class Player
    {
        public string UserId { get; set; } = "";
        public string Nickname { get; set; } = "";
        public string ConnectionId { get; set; } = "";
        public bool IsConnected { get; set; } = true;
        public bool IsReady { get; set; }
        public bool IsHost { get; set; }
        public bool IsBot { get; set; }

        /// <summary>
        /// 좌석 번호 (0~3). 0,2번이 한 팀 / 1,3번이 한 팀.
        /// </summary>
        public int Seat { get; set; }

        public int TeamId => Seat % 2;

        // ---- 라운드별 게임 상태 (서버 전용, 클라이언트로는 절대 그대로 보내지 않음) ----
        public List<Card> Hand { get; set; } = new();
        public TichuCallType TichuCall { get; set; } = TichuCallType.None;
        public bool HasActedThisRound { get; set; }
        public bool HasFinishedThisRound { get; set; }
        public int FinishPosition { get; set; } = -1; // 0=1등 ...
        public int WonPileScore { get; set; } // 이번 라운드에 트릭으로 획득한 점수
    }
}
