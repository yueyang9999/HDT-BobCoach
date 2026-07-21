using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace BobCoach.Engine
{
    /// <summary>Immutable deterministic effects resolved from equipped trinket CardIds.</summary>
    public sealed class ActiveTrinketContext
    {
        internal static readonly ActiveTrinketContext Empty = new ActiveTrinketContext(
            new List<string>(), new List<string>(), false, false, false, false,
            false, false, false, false);

        private readonly bool _designerEyepatch;
        private readonly bool _cowrieNecklace;
        private readonly bool _ironforgeAnvil;
        private readonly bool _slammaSticker;
        private readonly bool _emeraldDreamcatcher;
        private readonly bool _stegodonPortrait;
        private readonly bool _tinyfinOnesie;
        private readonly bool _dramalocSticker;

        internal ActiveTrinketContext(
            IList<string> resolvedCardIds,
            IList<string> unknownCardIds,
            bool designerEyepatch,
            bool cowrieNecklace,
            bool ironforgeAnvil,
            bool slammaSticker,
            bool emeraldDreamcatcher,
            bool stegodonPortrait,
            bool tinyfinOnesie,
            bool dramalocSticker)
        {
            ResolvedCardIds = new ReadOnlyCollection<string>(
                new List<string>(resolvedCardIds ?? new List<string>()));
            UnknownCardIds = new ReadOnlyCollection<string>(
                new List<string>(unknownCardIds ?? new List<string>()));
            _designerEyepatch = designerEyepatch;
            _cowrieNecklace = cowrieNecklace;
            _ironforgeAnvil = ironforgeAnvil;
            _slammaSticker = slammaSticker;
            _emeraldDreamcatcher = emeraldDreamcatcher;
            _stegodonPortrait = stegodonPortrait;
            _tinyfinOnesie = tinyfinOnesie;
            _dramalocSticker = dramalocSticker;
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
                && HasTribe(card.Tribe, "Pirate", "海盗"))
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
            if (_designerEyepatch && HasTribe(card.Tribe, "Pirate", "海盗")) score += 0.35;
            if (_cowrieNecklace && card.IsSpell && card.GrantsStats) score += 0.35;
            if (_ironforgeAnvil && !card.IsSpell && string.IsNullOrEmpty(card.Tribe)) score += 0.35;
            if (_slammaSticker && HasTribe(card.Tribe, "Beast", "野兽")) score += 0.35;
            if (_emeraldDreamcatcher && HasTribe(card.Tribe, "Dragon", "龙")) score += 0.35;
            if (_stegodonPortrait && HasTribe(card.Tribe, "Beast", "野兽")) score += 0.35;
            if (_dramalocSticker && HasTribe(card.Tribe, "Murloc", "鱼人")) score += 0.35;
            return score;
        }

        public double GetBoardSynergyScore(IEnumerable<MinionData> board)
        {
            if (board == null) return 0;
            double score = 0;
            foreach (var card in board) score += GetCardSynergyScore(card);
            return score;
        }

        public void ApplyStartOfCombat(
            IList<CombatUnit> ownerBoard, IList<MinionData> ownerHand = null)
        {
            if (ownerBoard == null) return;

            bool hasBoardUnit = false;
            int highestBoardAttack = 0;
            foreach (var unit in ownerBoard)
            {
                if (unit == null) continue;
                if (!hasBoardUnit || unit.Attack > highestBoardAttack)
                    highestBoardAttack = unit.Attack;
                hasBoardUnit = true;
            }

            MinionData highestHealthHandCard = null;
            int highestHandAttack = 0;
            bool hasHandCard = false;
            if (ownerHand != null)
            {
                foreach (var card in ownerHand)
                {
                    if (card == null) continue;
                    if (highestHealthHandCard == null
                        || card.Health > highestHealthHandCard.Health)
                        highestHealthHandCard = card;
                    if (!hasHandCard || card.Attack > highestHandAttack)
                        highestHandAttack = card.Attack;
                    hasHandCard = true;
                }
            }

            if (_ironforgeAnvil)
            {
                foreach (var unit in ownerBoard)
                {
                    if (unit == null || unit.MinionTypes == null || unit.MinionTypes.Count != 0) continue;
                    unit.Attack *= 3;
                    unit.Health *= 3;
                    unit.MaxHealth *= 3;
                }
            }

            if (_emeraldDreamcatcher && hasBoardUnit)
            {
                foreach (var unit in ownerBoard)
                {
                    if (HasMinionType(unit, "Dragon", "龙")) unit.Attack = highestBoardAttack;
                }
            }

            if (_stegodonPortrait)
            {
                int shielded = 0;
                foreach (var unit in ownerBoard)
                {
                    if (!HasMinionType(unit, "Beast", "野兽")) continue;
                    unit.DivineShield = true;
                    if (++shielded == 2) break;
                }
            }

            if (_tinyfinOnesie && highestHealthHandCard != null)
            {
                foreach (var unit in ownerBoard)
                {
                    if (unit == null) continue;
                    unit.Attack += highestHealthHandCard.Attack;
                    unit.Health += highestHealthHandCard.Health;
                    unit.MaxHealth += highestHealthHandCard.Health;
                    break;
                }
            }

            if (_dramalocSticker && hasHandCard)
            {
                foreach (var unit in ownerBoard)
                {
                    if (HasMinionType(unit, "Murloc", "鱼人")) unit.Attack += highestHandAttack;
                }
            }
        }

        private static bool HasTribe(string tribeField, string english, string chinese)
        {
            return MinionData.TribeMatches(tribeField, english)
                || MinionData.TribeMatches(tribeField, chinese);
        }

        private static bool HasMinionType(CombatUnit unit, string english, string chinese)
        {
            return unit != null && unit.MinionTypes != null
                && (unit.MinionTypes.Contains(english) || unit.MinionTypes.Contains(chinese));
        }

        public void ApplySummon(CombatUnit summoned)
        {
            if (!_slammaSticker || summoned == null || summoned.MinionTypes == null) return;
            if (HasMinionType(summoned, "Beast", "野兽")) summoned.Attack *= 2;
        }
    }
}
