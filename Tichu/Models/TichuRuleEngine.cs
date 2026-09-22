namespace Tichu.Models
{
    public enum TichuComboType
    {
        None, Single, Pair, Triple, FullHouse, PairSequence, Straight, BombFour, BombStraight
    }

    public class TichuRuleEngine
    {
        public TichuComboType DetectCombo(List<Card> cards)
        {
            if (cards == null || cards.Count == 0) return TichuComboType.None;

            cards = cards.OrderBy(c => c.RankValue).ToList();

            if (cards.Count == 1) return TichuComboType.Single;
            if (cards.Count == 2 && cards[0].RankValue == cards[1].RankValue) return TichuComboType.Pair;
            if (cards.Count == 3 && cards.All(c => c.RankValue == cards[0].RankValue)) return TichuComboType.Triple;
            if (cards.Count == 4 && cards.All(c => c.RankValue == cards[0].RankValue)) return TichuComboType.BombFour;

            if (cards.Count == 5 && IsFullHouse(cards)) return TichuComboType.FullHouse;
            if (cards.Count >= 5 && IsBombStraight(cards)) return TichuComboType.BombStraight; // 무늬 동일 + 연속
            if (cards.Count >= 4 && cards.Count % 2 == 0 && IsPairSequence(cards)) return TichuComboType.PairSequence;
            if (cards.Count >= 5 && IsStraight(cards)) return TichuComboType.Straight;

            return TichuComboType.None;
        }

        public bool IsStronger(List<Card> current, List<Card> previous)
        {
            var curType = DetectCombo(current);
            var prevType = DetectCombo(previous);

            if (curType == TichuComboType.None) return false;

            // 이전 패가 없으면 누구든 낼 수 있음
            if (prevType == TichuComboType.None) return true;

            // 같은 타입, 같은 장수만 비교 (폭탄은 예외 처리 아래)
            if (curType == prevType)
            {
                // 장수 다르면 불가 (예: 더 많은 카드로 같은 타입 못침)
                if (current.Count != previous.Count) return false;

                // 같은 타입이면 최댓값 비교
                var curMax = current.Max(c => c.RankValue);
                var prevMax = previous.Max(c => c.RankValue);
                return curMax > prevMax;
            }

            // 폭탄은 일반 족보를 언제든 이김
            if (IsBomb(curType) && !IsBomb(prevType)) return true;
            if (!IsBomb(curType) && IsBomb(prevType)) return false;

            // 폭탄 vs 폭탄: 우선순위 비교 (Straight Bomb > Four Bomb), 같으면 최댓값 비교
            if (IsBomb(curType) && IsBomb(prevType))
            {
                int curPriority = BombPriority(curType);
                int prevPriority = BombPriority(prevType);
                if (curPriority != prevPriority) return curPriority > prevPriority;

                return current.Max(c => c.RankValue) > previous.Max(c => c.RankValue);
            }

            // 타입이 다르면 일반적으로 낼 수 없음
            return false;
        }

        public static bool IsBomb(TichuComboType t)
            => t == TichuComboType.BombFour || t == TichuComboType.BombStraight;

        private static int BombPriority(TichuComboType t)
            => t == TichuComboType.BombStraight ? 2 : (t == TichuComboType.BombFour ? 1 : 0);

        private bool IsStraight(List<Card> cards)
            => cards.All(c => !c.IsSpecial || c.Rank == "Mahjong")
               && cards.Zip(cards.Skip(1), (a, b) => b.RankValue - a.RankValue).All(d => d == 1);

        private bool IsFullHouse(List<Card> cards)
        {
            var groups = cards.GroupBy(c => c.RankValue).ToList();
            return groups.Count == 2 && groups.Any(g => g.Count() == 3);
        }

        private bool IsPairSequence(List<Card> cards)
        {
            var groups = cards.GroupBy(c => c.RankValue).OrderBy(g => g.Key).ToList();
            if (groups.Any(g => g.Count() != 2)) return false;
            if (groups.Any(g => cards.First(c => c.RankValue == g.Key).IsSpecial)) return false;

            return groups.Zip(groups.Skip(1), (a, b) => b.Key - a.Key).All(d => d == 1);
        }

        private bool IsBombStraight(List<Card> cards)
        {
            if (cards.Count < 5) return false;
            var suit = cards[0].Suit;
            if (suit == "Special") return false;
            // 같은 무늬 + 연속
            return cards.All(c => c.Suit == suit) && IsStraight(cards);
        }
    }
}
