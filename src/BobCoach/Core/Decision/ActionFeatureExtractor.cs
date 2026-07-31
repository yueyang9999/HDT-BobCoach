using System;
using System.Collections.Generic;
using System.Linq;
using BobCoach.Engine;

namespace BobCoach.Engine.Decision
{
    public sealed class ActionFeatureExtractor
    {
        private readonly ISeasonFeatureExtension _seasonExtension;

        public ActionFeatureExtractor(ISeasonFeatureExtension seasonExtension = null)
        {
            _seasonExtension = seasonExtension ?? NullSeasonFeatureExtension.Instance;
        }

        public ActionFeatureVector Extract(GameState state, ActionDescriptor action, GameState nextState)
        {
            var values = new double[ActionFeatureSchema.Names.Count];
            var coverage = new double[ActionFeatureSchema.Names.Count];
            if (state == null || action == null || action.Action == null || nextState == null)
                return new ActionFeatureVector(values, coverage);

            var currentBoard = state.BoardMinions ?? new List<MinionData>();
            var nextBoard = nextState.BoardMinions ?? new List<MinionData>();
            var attackDelta = Total(nextBoard, x => x.Attack) - Total(currentBoard, x => x.Attack);
            var healthDelta = Total(nextBoard, x => x.Health) - Total(currentBoard, x => x.Health);
            var keywordDelta = KeywordCount(nextBoard) - KeywordCount(currentBoard);
            var unitDelta = nextBoard.Count - currentBoard.Count;
            var healthRatio = Ratio(state.Health + state.Armor, state.MaxHealth);
            var lowHealthNeed = 1d - Clamp01(healthRatio);
            var legalFollowUps = LegalFollowUps(nextState);
            var pairCoverage = HasResolvedCardIdentities(
                state.BoardMinions, state.HandMinions, nextState.BoardMinions, nextState.HandMinions);
            var pairDelta = pairCoverage ? PairCount(nextState) - PairCount(state) : 0d;
            var tripleDelta = pairCoverage ? TripleProgress(nextState) - TripleProgress(state) : 0d;
            var simulatorCoverage = SimulatorCoverage(action.SimulatorSupport);
            var simulatorKnown = action.SimulatorSupport == SimulatorSupport.Deterministic
                || action.SimulatorSupport == SimulatorSupport.Partial;
            var target = ResolveTarget(state, action);
            var targetKnown = target != null && target.Tier >= 0;
            var immediatePowerProxy = attackDelta + healthDelta;
            var seasonValue = 0d;
            var seasonKnown = HasKnownSeason(state)
                && string.Equals(state.SeasonId, _seasonExtension.SeasonId, StringComparison.Ordinal)
                && _seasonExtension.TryEvaluate(state, action, nextState, out seasonValue);

            Set(values, coverage, "turn", state.Turn, 1d);
            Set(values, coverage, "tavern_tier", state.TavernTier, 1d);
            Set(values, coverage, "health_armor", healthRatio, state.MaxHealth > 0 ? 1d : 0d);
            Set(values, coverage, "gold_before", state.Gold, 1d);
            Set(values, coverage, "hand_slots_free", 10d - Count(state.HandMinions), state.HandMinions != null ? 1d : 0d);
            Set(values, coverage, "board_slots_free", 7d - Count(state.BoardMinions), state.BoardMinions != null ? 1d : 0d);
            Set(values, coverage, "immediate_attack_delta", attackDelta, 1d);
            Set(values, coverage, "immediate_health_delta", healthDelta, 1d);
            Set(values, coverage, "immediate_keyword_delta", keywordDelta, 1d);
            Set(values, coverage, "unit_count_delta", unitDelta, 1d);
            Set(values, coverage, "board_power_delta", 0d, 0d);
            Set(values, coverage, "net_gold_cost", state.Gold - nextState.Gold, 1d);
            Set(values, coverage, "remaining_gold", nextState.Gold, 1d);
            var allFollowUpCostsKnown = legalFollowUps.All(candidate => candidate.GoldCost != int.MaxValue);
            Set(values, coverage, "wasted_gold", allFollowUpCostsKnown ? WastedGold(nextState.Gold, legalFollowUps) : 0d,
                allFollowUpCostsKnown ? 1d : 0d);
            Set(values, coverage, "refresh_expected_value", 0d, 0d);
            Set(values, coverage, "freeze_expected_value", 0d, 0d);
            Set(values, coverage, "legal_follow_up_count", legalFollowUps.Count, 1d);
            Set(values, coverage, "playable_follow_up_count", legalFollowUps.Count(IsPlayableWithoutSemanticTarget), 1d);
            Set(values, coverage, "hand_congestion", Ratio(Count(nextState.HandMinions), 10), nextState.HandMinions != null ? 1d : 0d);
            Set(values, coverage, "board_congestion", Ratio(Count(nextState.BoardMinions), 7), nextState.BoardMinions != null ? 1d : 0d);
            Set(values, coverage, "pair_count_delta", pairDelta, pairCoverage ? 1d : 0d);
            Set(values, coverage, "triple_progress_delta", tripleDelta, pairCoverage ? 1d : 0d);
            Set(values, coverage, "scaling_engine_delta", 0d, 0d);
            Set(values, coverage, "protected_component_delta", 0d, 0d);
            Set(values, coverage, "expected_damage_exposure", 0d, 0d);
            Set(values, coverage, "low_health_tempo_need", lowHealthNeed, state.MaxHealth > 0 ? 1d : 0d);
            Set(values, coverage, "weak_board_level_risk", 0d, action.Action.Type == ActionType.Upgrade ? 0d : 1d);
            Set(values, coverage, "current_synergy", 0d, 0d);
            Set(values, coverage, "target_compatibility", 0d, 0d);
            Set(values, coverage, "abandoned_piece_count", 0d, 0d);
            Set(values, coverage, "abandoned_piece_value", 0d, 0d);
            Set(values, coverage, "transition_cost", 0d, 0d);
            Set(values, coverage, "unknown_effect_count", CountUnknownEffects(state, action, nextState), 1d);
            Set(values, coverage, "simulator_coverage", simulatorCoverage, simulatorKnown ? 1d : 0d);
            Set(values, coverage, "semantic_coverage", 0d, 0d);
            Set(values, coverage, "stochastic_range", 0d,
                action.SimulatorSupport == SimulatorSupport.Deterministic ? 1d : 0d);
            Set(values, coverage, "state_observation_completeness", ObservationCompleteness(state), 1d);
            Set(values, coverage, "low_health_x_immediate_power", lowHealthNeed * immediatePowerProxy,
                state.MaxHealth > 0 ? 1d : 0d);
            Set(values, coverage, "board_full_x_buy", Ratio(Count(nextState.BoardMinions), 7)
                * IsBuy(action.Action.Type), nextState.BoardMinions != null ? 1d : 0d);
            Set(values, coverage, "level_x_damage_exposure", 0d, 0d);
            Set(values, coverage, "pair_x_matching_shop", pairCoverage ? PairCount(state) * MatchingShopCount(state, target) : 0d,
                pairCoverage && target != null ? 1d : 0d);
            Set(values, coverage, "remaining_gold_x_follow_ups", nextState.Gold * legalFollowUps.Count, 1d);
            Set(values, coverage, "transition_x_protected_value", 0d, 0d);
            Set(values, coverage, "target_tier_delta", targetKnown ? target.Tier - state.TavernTier : 0d,
                targetKnown ? 1d : 0d);
            Set(values, coverage, "season_extension_value", seasonKnown ? seasonValue : 0d,
                seasonKnown ? 1d : 0d);

            return new ActionFeatureVector(values, coverage);
        }

        private static void Set(double[] values, double[] coverage, string name, double value, double present)
        {
            var index = ActionFeatureSchema.IndexOf(name);
            values[index] = IsFinite(value) ? value : 0d;
            coverage[index] = present == 1d ? 1d : 0d;
        }

        private static double Total(IEnumerable<MinionData> minions, Func<MinionData, int> selector)
        {
            return minions == null ? 0d : minions.Where(x => x != null).Sum(x => (double)selector(x));
        }

        private static int KeywordCount(IEnumerable<MinionData> minions)
        {
            if (minions == null) return 0;
            return minions.Where(x => x != null).Sum(x =>
                (x.Taunt ? 1 : 0) + (x.DivineShield ? 1 : 0) + (x.Windfury ? 1 : 0)
                + (x.MegaWindfury ? 1 : 0) + (x.Stealth ? 1 : 0) + (x.Reborn ? 1 : 0)
                + (x.Poisonous ? 1 : 0) + (x.Venomous ? 1 : 0) + (x.Cleave ? 1 : 0)
                + (x.Overkill ? 1 : 0));
        }

        private static double Ratio(double numerator, double denominator)
        {
            return denominator <= 0d ? 0d : Clamp(numerator / denominator, -5d, 5d);
        }

        private static double Clamp01(double value) { return Clamp(value, 0d, 1d); }
        private static double Clamp(double value, double min, double max) { return Math.Max(min, Math.Min(max, value)); }
        private static bool IsFinite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private static int Count<T>(ICollection<T> values) { return values == null ? 0 : values.Count; }
        private static double IsBuy(ActionType type) { return type == ActionType.BuyMinion || type == ActionType.BuySpell ? 1d : 0d; }

        private static bool HasKnownSeason(GameState state)
        {
            return state != null && !string.IsNullOrEmpty(state.GameBuild) && !string.IsNullOrEmpty(state.SeasonId)
                && !string.IsNullOrEmpty(state.CardPoolIdentity)
                && !string.Equals(state.GameBuild, "unknown", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(state.SeasonId, "unknown", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(state.CardPoolIdentity, "unknown", StringComparison.OrdinalIgnoreCase);
        }

        private static IList<ActionDescriptor> LegalFollowUps(GameState state)
        {
            if (state == null || !state.GameActive) return new List<ActionDescriptor>();
            var candidates = new ActionEnumerator().Enumerate(state, state.HeroCardId)
                .Select(candidate => ActionDescriptor.From(state, candidate));
            return new HardConstraintFilter().Apply(state, candidates);
        }

        private static bool IsPlayableWithoutSemanticTarget(ActionDescriptor candidate)
        {
            if (candidate == null || candidate.Action == null || candidate.GoldCost == int.MaxValue) return false;
            return candidate.Action.Type != ActionType.BuyMinion
                && candidate.Action.Type != ActionType.BuySpell
                || candidate.EntityId != 0;
        }

        private static double WastedGold(int gold, IEnumerable<ActionDescriptor> candidates)
        {
            var remaining = gold;
            foreach (var candidate in candidates.OrderByDescending(x => x.GoldCost)
                .ThenBy(x => x.ActionKey, StringComparer.Ordinal))
            {
                if (candidate.GoldCost >= 0 && candidate.GoldCost <= remaining)
                    remaining -= candidate.GoldCost;
            }
            return remaining;
        }

        private static bool HasResolvedCardIdentities(params IEnumerable<MinionData>[] groups)
        {
            return groups.All(group => group != null && group.All(card => card != null
                && !string.IsNullOrEmpty(card.CardId)
                && !string.Equals(card.CardId, "unknown", StringComparison.OrdinalIgnoreCase)));
        }

        private static Dictionary<string, int> OwnedCounts(GameState state)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var card in (state.BoardMinions ?? new List<MinionData>()).Concat(state.HandMinions ?? new List<MinionData>()))
            {
                if (card == null || card.Golden || card.IsSpell || string.IsNullOrEmpty(card.CardId)) continue;
                counts[card.CardId] = counts.ContainsKey(card.CardId) ? counts[card.CardId] + 1 : 1;
            }
            return counts;
        }

        private static double PairCount(GameState state) { return OwnedCounts(state).Sum(x => Math.Floor(x.Value / 2d)); }
        private static double TripleProgress(GameState state) { return OwnedCounts(state).Sum(x => Math.Min(x.Value, 3) / 3d); }

        private static MinionData ResolveTarget(GameState state, ActionDescriptor action)
        {
            var index = action.Action.TargetIndex;
            if ((action.Action.Type == ActionType.BuyMinion || action.Action.Type == ActionType.BuySpell)
                && index >= 0 && state.ShopMinions != null && index < state.ShopMinions.Count) return state.ShopMinions[index];
            if (action.Action.Type == ActionType.SellMinion
                && index >= 0 && state.BoardMinions != null && index < state.BoardMinions.Count) return state.BoardMinions[index];
            return null;
        }

        private static int MatchingShopCount(GameState state, MinionData target)
        {
            if (target == null || string.IsNullOrEmpty(target.CardId) || state.ShopMinions == null) return 0;
            return state.ShopMinions.Count(card => card != null && card.CardId == target.CardId);
        }

        private static double SimulatorCoverage(SimulatorSupport support)
        {
            if (support == SimulatorSupport.Deterministic) return 1d;
            if (support == SimulatorSupport.Partial) return 0.5d;
            return 0d;
        }

        private static int CountUnknownEffects(GameState state, ActionDescriptor action, GameState nextState)
        {
            var target = ResolveTarget(state, action);
            var unknown = target == null ? 0 : 1;
            foreach (var card in nextState.BoardMinions ?? new List<MinionData>())
                if (card != null) unknown++;
            return unknown;
        }

        private static double ObservationCompleteness(GameState state)
        {
            var present = 0;
            if (state.GameActive) present++;
            if (!string.IsNullOrEmpty(state.Phase)) present++;
            if (state.Gold >= 0) present++;
            if (state.TavernTier > 0) present++;
            if (state.MaxHealth > 0) present++;
            if (state.BoardMinions != null) present++;
            if (state.HandMinions != null) present++;
            if (state.ShopMinions != null) present++;
            if (state.Opponents != null) present++;
            return Ratio(present, 9d);
        }
    }
}
