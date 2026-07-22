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
            false, false, false, false, false, false, false, false, false, false, false, false,
            false);

        private readonly bool _designerEyepatch;
        private readonly bool _cowrieNecklace;
        private readonly bool _ironforgeAnvil;
        private readonly bool _slammaSticker;
        private readonly bool _emeraldDreamcatcher;
        private readonly bool _stegodonPortrait;
        private readonly bool _tinyfinOnesie;
        private readonly bool _dramalocSticker;
        private readonly bool _eternalPortrait;
        private readonly bool _rivendarePortrait;
        private readonly bool _holyMallet;
        private readonly bool _trainingCertificate;
        private readonly bool _valorousMedallion;
        private readonly bool _greaterValorousMedallion;
        private readonly bool _balefulIncense;
        private readonly bool _bartendOTronOilcan;
        private readonly bool _karazhanChessSet;

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
            bool dramalocSticker,
            bool eternalPortrait,
            bool rivendarePortrait,
            bool holyMallet,
            bool trainingCertificate,
            bool valorousMedallion,
            bool greaterValorousMedallion,
            bool balefulIncense,
            bool bartendOTronOilcan,
            bool karazhanChessSet)
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
            _eternalPortrait = eternalPortrait;
            _rivendarePortrait = rivendarePortrait;
            _holyMallet = holyMallet;
            _trainingCertificate = trainingCertificate;
            _valorousMedallion = valorousMedallion;
            _greaterValorousMedallion = greaterValorousMedallion;
            _balefulIncense = balefulIncense;
            _bartendOTronOilcan = bartendOTronOilcan;
            _karazhanChessSet = karazhanChessSet;
        }

        public ReadOnlyCollection<string> ResolvedCardIds { get; private set; }
        public ReadOnlyCollection<string> UnknownCardIds { get; private set; }

        internal int UpgradeCostDelta
        {
            get { return _bartendOTronOilcan ? -3 : 0; }
        }

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
            IList<CombatUnit> ownerBoard, IList<MinionData> ownerHand = null,
            CombatContext combatContext = null)
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

            if (_eternalPortrait)
            {
                foreach (var unit in ownerBoard)
                {
                    if (!HasExactCardId(unit, "BG25_008", "BG25_008_G")) continue;
                    unit.Taunt = true;
                    unit.Reborn = true;
                }
            }

            if (_rivendarePortrait)
            {
                foreach (var unit in ownerBoard)
                {
                    if (!HasExactCardId(unit, "BG25_354", "BG25_354_G")) continue;
                    unit.Health *= 2;
                    unit.MaxHealth *= 2;
                }
            }

            if (_holyMallet)
            {
                CombatUnit left = FirstUnit(ownerBoard);
                CombatUnit right = LastUnit(ownerBoard);
                if (left != null) left.DivineShield = true;
                if (right != null && !ReferenceEquals(left, right)) right.DivineShield = true;
            }

            if (_trainingCertificate)
            {
                int first = -1;
                int second = -1;
                for (int i = 0; i < ownerBoard.Count; i++)
                {
                    CombatUnit unit = ownerBoard[i];
                    if (unit == null) continue;
                    if (first < 0 || unit.Attack < ownerBoard[first].Attack)
                    {
                        second = first;
                        first = i;
                    }
                    else if (second < 0 || unit.Attack < ownerBoard[second].Attack)
                    {
                        second = i;
                    }
                }
                DoubleStats(first >= 0 ? ownerBoard[first] : null);
                DoubleStats(second >= 0 ? ownerBoard[second] : null);
            }

            if (_valorousMedallion || _greaterValorousMedallion)
            {
                int statGain = (_valorousMedallion ? 2 : 0)
                    + (_greaterValorousMedallion ? 6 : 0);
                foreach (var unit in ownerBoard) AddStats(unit, statGain, statGain);
            }

            if (_balefulIncense)
            {
                CombatUnit leftUndead = FirstMinionType(ownerBoard, "Undead", "亡灵");
                CombatUnit rightUndead = LastMinionType(ownerBoard, "Undead", "亡灵");
                if (leftUndead != null) leftUndead.Reborn = true;
                if (rightUndead != null && !ReferenceEquals(leftUndead, rightUndead))
                    rightUndead.Reborn = true;
            }

            if (_karazhanChessSet && combatContext != null)
            {
                CombatUnit source = FirstUnit(ownerBoard);
                List<CombatUnit> side = ownerBoard as List<CombatUnit>;
                if (source != null && side != null
                    && (ReferenceEquals(side, combatContext.AttackerSide)
                        || ReferenceEquals(side, combatContext.DefenderSide)))
                {
                    int windfuryAttacksLeft = source.WindfuryAttacksLeft;
                    CombatUnit copy = CopyForSummon(source);
                    CombatUnit summoned = combatContext.SpawnToken(
                        side, copy, side.IndexOf(source));
                    if (summoned != null)
                        summoned.WindfuryAttacksLeft = windfuryAttacksLeft;
                }
            }
        }

        private static CombatUnit CopyForSummon(CombatUnit source)
        {
            CombatUnit copy = source.ShallowCopy();
            copy.MinionTypes = source.MinionTypes == null
                ? new List<string>() : new List<string>(source.MinionTypes);
            copy.Mechanics = source.Mechanics == null
                ? new List<string>() : new List<string>(source.Mechanics);
            copy.Extra = source.Extra == null
                ? new Dictionary<string, object>()
                : new Dictionary<string, object>(source.Extra);
            copy.Position = 999;
            copy.Alive = true;
            copy.RebornUsed = false;
            copy.DeathrattleTriggered = false;
            copy.AvengeTriggered = false;
            copy.StartOfCombatTriggered = false;
            copy.DeathCountWitnessed = 0;
            copy.KilledBy = null;
            return copy;
        }

        private static bool HasExactCardId(CombatUnit unit, string normalCardId, string goldenCardId)
        {
            return unit != null
                && (string.Equals(unit.CardId, normalCardId, StringComparison.Ordinal)
                    || string.Equals(unit.CardId, goldenCardId, StringComparison.Ordinal));
        }

        private static CombatUnit FirstUnit(IList<CombatUnit> board)
        {
            for (int i = 0; i < board.Count; i++)
                if (board[i] != null) return board[i];
            return null;
        }

        private static CombatUnit LastUnit(IList<CombatUnit> board)
        {
            for (int i = board.Count - 1; i >= 0; i--)
                if (board[i] != null) return board[i];
            return null;
        }

        private static CombatUnit FirstMinionType(
            IList<CombatUnit> board, string english, string chinese)
        {
            for (int i = 0; i < board.Count; i++)
                if (HasMinionType(board[i], english, chinese)) return board[i];
            return null;
        }

        private static CombatUnit LastMinionType(
            IList<CombatUnit> board, string english, string chinese)
        {
            for (int i = board.Count - 1; i >= 0; i--)
                if (HasMinionType(board[i], english, chinese)) return board[i];
            return null;
        }

        private static void DoubleStats(CombatUnit unit)
        {
            if (unit == null) return;
            unit.Attack *= 2;
            unit.Health *= 2;
            unit.MaxHealth *= 2;
        }

        private static void AddStats(CombatUnit unit, int attack, int health)
        {
            if (unit == null) return;
            unit.Attack += attack;
            unit.Health += health;
            unit.MaxHealth += health;
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
