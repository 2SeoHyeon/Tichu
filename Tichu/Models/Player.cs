namespace Tichu.Models
{
    public class Player
    {
        public string UserId { get; set; } = "";
        public string Nickname { get; set; } = "";
        public string ConnectionId { get; set; } = "";
        public bool IsConnected { get; set; } = true;
        public bool IsReady { get; set; }
        public bool IsHost { get; set; }

        /// <summary>
        /// 좌석 번호 (0~3). 0,2번이 한 팀 / 1,3번이 한 팀.
        /// </summary>
        public int Seat { get; set; }

        public int TeamId => Seat % 2;

        // ---- 라운드별 게임 상태 (서버 전용, 클라이언트로는 절대 그대로 보내지 않음) ----
        public List<Card> Hand { get; set; } = new();
        public bool CalledTichu { get; set; }
        public bool HasActedThisRound { get; set; }
        public bool HasFinishedThisRound { get; set; }
        public int FinishPosition { get; set; } = -1; // 0=1등 ...
        public int WonPileScore { get; set; } // 이번 라운드에 트릭으로 획득한 점수
    }
}
