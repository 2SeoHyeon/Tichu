using Tichu.Models;

namespace Tichu.Services
{
    public class GameService
    {
        private readonly TichuRuleEngine _ruleEngine;

        public GameService(TichuRuleEngine ruleEngine)
        {
            _ruleEngine = ruleEngine;
        }

        /// <summary>
        /// 새 카드 덱 생성 (52장 + 특수 4장)
        /// </summary>
        public List<Card> CreateDeck()
        {
            var suits = new[] { "♠", "♥", "♣", "♦" };
            var ranks = new[] { "A", "K", "Q", "J", "10", "9", "8", "7", "6", "5", "4", "3", "2" };
            var deck = new List<Card>();

            foreach (var suit in suits)
            {
                foreach (var rank in ranks)
                {
                    deck.Add(new Card { Suit = suit, Rank = rank });
                }
            }

            // 특수 카드 추가
            deck.Add(new Card { Suit = "Special", Rank = "Dragon" });
            deck.Add(new Card { Suit = "Special", Rank = "Phoenix" });
            deck.Add(new Card { Suit = "Special", Rank = "Dog" });
            deck.Add(new Card { Suit = "Special", Rank = "Mahjong" });

            return deck;
        }

        /// <summary>
        /// 카드 섞기
        /// </summary>
        public List<Card> Shuffle(List<Card> deck)
        {
            var rng = new Random();
            return deck.OrderBy(_ => rng.Next()).ToList();
        }

        /// <summary>
        /// 플레이어에게 카드 나누기 (4인 기준) - 무조건 4인
        /// </summary>
        public Dictionary<string, List<Card>> DealCards(List<Card> deck, List<Player> players)
        {
            var result = new Dictionary<string, List<Card>>();
            int cardsPerPlayer = deck.Count / players.Count;

            for (int i = 0; i < players.Count; i++)
            {
                result[players[i].UserId] = deck.Skip(i * cardsPerPlayer).Take(cardsPerPlayer).ToList();
            }

            return result;
        }

        /// <summary>
        /// 카드 족보 비교 (턴 유효성 판단)
        /// </summary>
        public bool IsValidPlay(List<Card> currentPlay, List<Card> lastPlay)
        {
            if (lastPlay == null || lastPlay.Count == 0) return true;
            return _ruleEngine.IsStronger(currentPlay, lastPlay);
        }
    }
}