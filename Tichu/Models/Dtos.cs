namespace Tichu.Models
{
    public class RoomListItemDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int TargetScore { get; set; }
        public int TurnTimeLimit { get; set; }
        public string Status { get; set; } = "";
        public int PlayerCount { get; set; }
    }

    public class PlayerPublicDto
    {
        public string UserId { get; set; } = "";
        public string Nickname { get; set; } = "";
        public int Seat { get; set; }
        public int TeamId { get; set; }
        public bool IsHost { get; set; }
        public bool IsReady { get; set; }
        public bool IsBot { get; set; }
        public bool IsConnected { get; set; } = true;
        public int HandCount { get; set; }
        public string TichuCall { get; set; } = "None";
        public bool HasFinished { get; set; }
        public int FinishPosition { get; set; } = -1;
    }

    public class CardDto
    {
        public int Id { get; set; }
        public string Suit { get; set; } = "";
        public string Rank { get; set; } = "";
        public int RankValue { get; set; }

        public static CardDto From(Card c) => new()
        {
            Id = c.Id,
            Suit = c.Suit,
            Rank = c.Rank,
            RankValue = c.RankValue
        };
    }

    public class RoomStateDto
    {
        public int RoomId { get; set; }
        public string RoomName { get; set; } = "";
        public int TargetScore { get; set; }
        public int TurnTimeLimit { get; set; }
        public string Status { get; set; } = "";
        public string HostUserId { get; set; } = "";
        public List<PlayerPublicDto> Players { get; set; } = new();

        // ----- game (null while waiting) -----
        public string? Phase { get; set; }
        public int RoundNumber { get; set; }
        public Dictionary<int, int>? TeamScores { get; set; }
        public int CurrentTurnSeat { get; set; }
        public List<CardDto> LastPlay { get; set; } = new();
        public int? LastPlayerSeat { get; set; }
        // LastPlay가 싱글 1장일 때 실제로 이겨야 할 기준값 (불사조는 직전 카드+0.5, 리드일 땐 1.5)
        public double? LastPlayEffectiveValue { get; set; }
        public bool DragonTrickPending { get; set; }
        public int? LastDogFromSeat { get; set; }
        public int? LastDogToSeat { get; set; }
        public int DogMoveSeq { get; set; }
        public List<int> ExchangeSubmittedSeats { get; set; } = new();
        public string LastMessage { get; set; } = "";
        public List<RoundLogEntry> History { get; set; } = new();

        // ----- private to the viewer -----
        public List<CardDto> MyHand { get; set; } = new();
        public int MySeat { get; set; } = -1;
    }
}
