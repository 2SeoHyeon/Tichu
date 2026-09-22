namespace Tichu.Models
{
    public enum TichuComboType
    {
        None, Single, Pair, Triple, FullHouse, PairSequence, Straight, BombFour, BombStraight
    }

    public class TichuRuleEngine
    {
        // 불사조가 대신할 수 있는 랭크값 2~14(에이스). 마작/드래곤/도그 등 특수카드는
        // 실제 티츄 규칙상 불사조로 대신할 수 없어서 제외. 낮은 값부터 시도해서 여러 방식으로
        // 완성 가능한 스트레이트 등에서는 가장 낮은 완성을 기본으로 선택한다.
        private static readonly Dictionary<int, string> WildcardRankStrings = new()
        {
            { 2, "2" }, { 3, "3" }, { 4, "4" }, { 5, "5" }, { 6, "6" }, { 7, "7" },
            { 8, "8" }, { 9, "9" }, { 10, "10" }, { 11, "J" }, { 12, "Q" }, { 13, "K" }, { 14, "A" }
        };

        public TichuComboType DetectCombo(List<Card> cards)
        {
            if (cards == null || cards.Count == 0) return TichuComboType.None;
            var sorted = cards.OrderBy(c => c.RankValue).ToList();
            if (sorted.Count == 1) return TichuComboType.Single;

            if (sorted.Any(c => c.Rank == "Phoenix") && sorted.Count >= 2)
                return DetectComboWithPhoenix(sorted).type;

            return DetectComboRaw(sorted);
        }

        private TichuComboType DetectComboRaw(List<Card> cards)
        {
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

        /// <summary>
        /// 불사조가 포함된 조합을, 불사조를 1(마작)~14(에이스) 중 하나로 대신 채워서
        /// 페어/트리플/풀하우스/연속 페어/스트레이트가 완성되는지 찾는다.
        /// (불사조는 폭탄에는 절대 쓸 수 없음 - 무늬가 Special이라 폭탄 조건에서 자동으로 제외됨)
        /// </summary>
        private (TichuComboType type, int phoenixValue) DetectComboWithPhoenix(List<Card> sorted)
        {
            var withoutPhoenix = sorted.Where(c => c.Rank != "Phoenix").ToList();
            foreach (var (value, rank) in WildcardRankStrings)
            {
                // Suit="Special"로 두면 IsBombStraight의 "같은 무늬" 조건이 절대 성립하지 않아서
                // 불사조로 폭탄(스트레이트 폭탄)을 완성하는 일이 없음 (실제 규칙상 불사조는 폭탄에 못 씀)
                var dummy = new Card { Id = -1, Suit = "Special", Rank = rank };
                var candidate = withoutPhoenix.Concat(new[] { dummy }).OrderBy(c => c.RankValue).ToList();
                var type = DetectComboRaw(candidate);
                if (type != TichuComboType.None) return (type, value);
            }
            return (TichuComboType.None, 0);
        }

        /// <summary>
        /// 조합의 비교 기준값(최댓값). 여러 장짜리 조합(페어 이상)에서 불사조가 빈 자리를
        /// 채우고 있으면 대신하는 랭크값으로 계산한다. 싱글 불사조는 여기 해당하지 않음 -
        /// 싱글일 때의 실제 기준값은 GameEngine의 GetEffectiveLastSingleValue/previousEffectiveValue가
        /// 따로 처리하고, 불사조 자신을 내는 패의 세기는 항상 고정 랭크(15)를 그대로 써야 한다.
        /// </summary>
        private double ComboMaxValue(List<Card> cards, TichuComboType type)
        {
            if (type == TichuComboType.Single || !cards.Any(c => c.Rank == "Phoenix")) return cards.Max(c => c.RankValue);
            var (_, phoenixValue) = DetectComboWithPhoenix(cards.OrderBy(c => c.RankValue).ToList());
            return cards.Where(c => c.Rank != "Phoenix").Select(c => (double)c.RankValue).Append(phoenixValue).Max();
        }

        /// <param name="previousEffectiveValue">
        /// 이전 패가 싱글 불사조일 때, 그 불사조가 실제로 이겼던 값 + 0.5 (예: K를 이긴 불사조는 13.5).
        /// 이 값을 넘겨받으면 불사조의 고정 랭크(15) 대신 이 값으로 비교한다.
        /// </param>
        public bool IsStronger(List<Card> current, List<Card> previous, double? previousEffectiveValue = null)
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

                // 같은 타입이면 최댓값 비교 (불사조가 섞여 있으면 대신하는 랭크값 기준)
                var curMax = ComboMaxValue(current, curType);
                double prevMax = (curType == TichuComboType.Single && previousEffectiveValue.HasValue)
                    ? previousEffectiveValue.Value
                    : ComboMaxValue(previous, prevType);
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
