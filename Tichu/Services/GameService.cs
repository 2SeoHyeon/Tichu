using Tichu.Models;

namespace Tichu.Services
{
    /// <summary>
    /// 덱 생성/셔플/분배 유틸리티
    /// </summary>
    public class GameService
    {
        /// <summary>
        /// 새 카드 덱 생성 (52장 + 특수 4장), 셔플까지 완료된 상태로 반환
        /// </summary>
        public List<Card> CreateShuffledDeck()
        {
            var suits = new[] { "S", "H", "C", "D" };
            var ranks = new[] { "A", "K", "Q", "J", "10", "9", "8", "7", "6", "5", "4", "3", "2" };
            var deck = new List<Card>();
            int id = 1;

            foreach (var suit in suits)
            {
                foreach (var rank in ranks)
                {
                    deck.Add(new Card { Id = id++, Suit = suit, Rank = rank });
                }
            }

            // 특수 카드 추가
            deck.Add(new Card { Id = id++, Suit = "Special", Rank = "Dragon" });
            deck.Add(new Card { Id = id++, Suit = "Special", Rank = "Phoenix" });
            deck.Add(new Card { Id = id++, Suit = "Special", Rank = "Dog" });
            deck.Add(new Card { Id = id++, Suit = "Special", Rank = "Mahjong" });

            var rng = Random.Shared;
            return deck.OrderBy(_ => rng.Next()).ToList();
        }

        /// <summary>
        /// 4인 기준으로 14장씩 나누기
        /// </summary>
        public Dictionary<int, List<Card>> DealBySeat(List<Card> deck)
        {
            var result = new Dictionary<int, List<Card>>();
            int perPlayer = deck.Count / 4;

            for (int seat = 0; seat < 4; seat++)
            {
                result[seat] = deck.Skip(seat * perPlayer).Take(perPlayer).ToList();
            }

            return result;
        }
    }
}
