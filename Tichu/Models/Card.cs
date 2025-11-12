namespace Tichu.Models
{
    public class Card
    {
        public int Id { get; set; }

        /// <summary>
        /// 카드 무늬 (♠, ♥, ♣, ♦ 또는 Special)
        /// </summary>
        public string Suit { get; set; } = string.Empty;

        /// <summary>
        /// 카드 랭크 (A, 2~10, J, Q, K, Dragon, Phoenix, Dog, Mahjong)
        /// </summary>
        public string Rank { get; set; } = string.Empty;

        /// <summary>
        /// 카드 점수 계산을 위한 값 (A=14, K=13, ... 2=2, 특수카드는 별도 처리)
        /// </summary>
        public int RankValue
        {
            get
            {
                return Rank switch
                {
                    "A" => 14,
                    "K" => 13,
                    "Q" => 12,
                    "J" => 11,
                    "10" => 10,
                    "9" => 9,
                    "8" => 8,
                    "7" => 7,
                    "6" => 6,
                    "5" => 5,
                    "4" => 4,
                    "3" => 3,
                    "2" => 2,
                    "Mahjong" => 1,     // 마작은 제일 낮음
                    "Phoenix" => 15,    // 불사조 (와일드카드)
                    "Dragon" => 20,     // 드래곤 (최강)
                    "Dog" => 0,         // 개 (턴 넘김)
                    _ => 0
                };
            }
        }

        /// <summary>
        /// 특수 카드 여부 (Dragon, Phoenix, Dog, Mahjong)
        /// </summary>
        public bool IsSpecial =>
            Rank is "Dragon" or "Phoenix" or "Dog" or "Mahjong";

        /// <summary>
        /// 카드 이미지 경로 (웹에서 표시할 때 사용)
        /// </summary>
        public string ImagePath
        {
            get
            {
                if (IsSpecial)
                    return $"/images/cards/front/{Rank.ToLower()}.svg";
                else
                    return $"/images/cards/front/{Suit.ToLower()}_{Rank}.svg";
            }
        }

        public override string ToString()
        {
            return $"{Rank}{Suit}";
        }
    }
}
