using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace BobCoach.Engine
{
    /// <summary>Immutable deterministic effects resolved from equipped trinket CardIds.</summary>
    public sealed class ActiveTrinketContext
    {
        internal static readonly ActiveTrinketContext Empty = new ActiveTrinketContext(
            new List<string>(), new List<string>(), false, false, false, false);

        private readonly bool _designerEyepatch;
        private readonly bool _cowrieNecklace;
        private readonly bool _ironforgeAnvil;
        private readonly bool _slammaSticker;

        internal ActiveTrinketContext(
            IList<string> resolvedCardIds,
            IList<string> unknownCardIds,
            bool designerEyepatch,
            bool cowrieNecklace,
            bool ironforgeAnvil,
            bool slammaSticker)
        {
            ResolvedCardIds = new ReadOnlyCollection<string>(
                new List<string>(resolvedCardIds ?? new List<string>()));
            UnknownCardIds = new ReadOnlyCollection<string>(
                new List<string>(unknownCardIds ?? new List<string>()));
            _designerEyepatch = designerEyepatch;
            _cowrieNecklace = cowrieNecklace;
            _ironforgeAnvil = ironforgeAnvil;
            _slammaSticker = slammaSticker;
        }

        public ReadOnlyCollection<string> ResolvedCardIds { get; private set; }
        public ReadOnlyCollection<string> UnknownCardIds { get; private set; }

        public EffectiveGameRules ApplyTo(EffectiveGameRules rules)
        {
            return (rules ?? EffectiveGameRules.Default).WithActiveTrinkets(this);
        }

        public int GetGoldenCopyRequirement(MinionData card, int defaultRequirement)
        {
            int requirement = Math.Max(2, defaultRequirement);
            if (_designerEyepatch && card != null
                && MinionData.TribeMatches(card.Tribe, "Pirate"))
                return 2;
            return requirement;
        }

        public int AdjustPurchaseCost(MinionData card, int baseCost)
        {
            if (baseCost == int.MaxValue) return baseCost;
            if (_cowrieNecklace && card != null && card.IsSpell && card.GrantsStats)
                return Math.Max(0, baseCost - 2);
            return baseCost;
        }

        public double GetCardSynergyScore(MinionData card)
        {
            if (card == null) return 0;
            double score = 0;
            if (_designerEyepatch && MinionData.TribeMatches(card.Tribe, "Pirate")) score += 0.35;
            if (_cowrieNecklace && card.IsSpell && card.GrantsStats) score += 0.35;
            if (_ironforgeAnvil && !card.IsSpell && string.IsNullOrEmpty(card.Tribe)) score += 0.35;
            if (_slammaSticker && MinionData.TribeMatches(card.Tribe, "Beast")) score += 0.35;
            return score;
        }

        public double GetBoardSynergyScore(IEnumerable<MinionData> board)
        {
            if (board == null) return 0;
            double score = 0;
            foreach (var card in board) score += GetCardSynergyScore(card);
            return score;
        }

        public void ApplyStartOfCombat(IList<CombatUnit> ownerBoard)
        {
            if (!_ironforgeAnvil || ownerBoard == null) return;
            foreach (var unit in ownerBoard)
            {
                if (unit == null || unit.MinionTypes == null || unit.MinionTypes.Count != 0) continue;
                unit.Attack *= 3;
                unit.Health *= 3;
                unit.MaxHealth *= 3;
            }
        }

        public void ApplySummon(CombatUnit summoned)
        {
            if (!_slammaSticker || summoned == null || summoned.MinionTypes == null) return;
            if (summoned.MinionTypes.Contains("Beast")) summoned.Attack *= 2;
        }
    }
}
