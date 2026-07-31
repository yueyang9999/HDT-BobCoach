using System;
using System.Collections.Generic;
using System.Linq;
using BobCoach.Engine;

namespace BobCoach.Engine.Decision
{
    public sealed class ActionFeatureExtractor
    {
        private readonly ISeasonFeatureExtension _seasonExtension;
        private readonly ExpectedShopEstimator _shopEstimator;

        public ActionFeatureExtractor(ISeasonFeatureExtension seasonExtension = null,
            ExpectedShopEstimator shopEstimator = null)
        {
            _seasonExtension = seasonExtension ?? NullSeasonFeatureExtension.Instance;
            _shopEstimator = shopEstimator ?? new ExpectedShopEstimator();
        }

        public ActionFeatureVector Extract(GameState state, ActionDescriptor action, GameState nextState)
        {
            var values = new double[ActionFeatureSchema.Names.Count];
            var coverage = new double[ActionFeatureSchema.Names.Count];
            if (state == null || action == null || action.Action == null || nextState == null)
                return new ActionFeatureVector(values, coverage);

            var sourceHandAvailable = HasCompleteMinionCollection(state.HandMinions);
            var sourceBoardAvailable = HasCompleteMinionCollection(state.BoardMinions);
            var nextHandAvailable = HasCompleteMinionCollection(nextState.HandMinions);
            var nextBoardAvailable = HasCompleteMinionCollection(nextState.BoardMinions);
            var boardsAvailable = sourceBoardAvailable && nextBoardAvailable;
            var currentBoard = state.BoardMinions;
            var nextBoard = nextState.BoardMinions;
            var attackDelta = boardsAvailable ? Total(nextBoard, x => x.Attack) - Total(currentBoard, x => x.Attack) : 0d;
            var healthDelta = boardsAvailable ? Total(nextBoard, x => x.Health) - Total(currentBoard, x => x.Health) : 0d;
            var unitDelta = boardsAvailable ? nextBoard.Count - currentBoard.Count : 0d;
            var healthRatio = Ratio(state.Health + state.Armor, state.MaxHealth);
            var lowHealthNeed = 1d - Clamp01(healthRatio);
            var pairCoverage = HasResolvedCardIdentities(
                state.BoardMinions, state.HandMinions, nextState.BoardMinions, nextState.HandMinions);
            var pairDelta = pairCoverage ? PairCount(nextState) - PairCount(state) : 0d;
            var tripleDelta = pairCoverage ? TripleProgress(nextState) - TripleProgress(state) : 0d;
            var seasonValue = 0d;
            var seasonKnown = HasExactSeasonProvenance(state, _seasonExtension)
                && _seasonExtension.TryEvaluate(state, action, nextState, out seasonValue);
            if (!IsFinite(seasonValue)) seasonKnown = false;
            var refreshEstimate = action.Action.Type == ActionType.Refresh
                ? _shopEstimator.Estimate(state)
                : ExpectedShopEstimate.Unavailable("unknown");

            Set(values, coverage, "turn", state.Turn, 1d);
            Set(values, coverage, "tavern_tier", state.TavernTier, 1d);
            Set(values, coverage, "health_armor", healthRatio, state.MaxHealth > 0 ? 1d : 0d);
            Set(values, coverage, "gold_before", state.Gold, 1d);
            Set(values, coverage, "hand_slots_free", sourceHandAvailable ? 10d - Count(state.HandMinions) : 0d,
                sourceHandAvailable ? 1d : 0d);
            Set(values, coverage, "board_slots_free", sourceBoardAvailable ? 7d - Count(state.BoardMinions) : 0d,
                sourceBoardAvailable ? 1d : 0d);
            Set(values, coverage, "immediate_attack_delta", attackDelta, boardsAvailable ? 1d : 0d);
            Set(values, coverage, "immediate_health_delta", healthDelta, boardsAvailable ? 1d : 0d);
            // These rule, effect, entity, and mechanic features require version-matched facts.
            Set(values, coverage, "immediate_keyword_delta", 0d, 0d);
            Set(values, coverage, "unit_count_delta", unitDelta, boardsAvailable ? 1d : 0d);
            Set(values, coverage, "board_power_delta", 0d, 0d);
            Set(values, coverage, "net_gold_cost", state.Gold - nextState.Gold, 1d);
            Set(values, coverage, "remaining_gold", nextState.Gold, 1d);
            Set(values, coverage, "wasted_gold", 0d, 0d);
            SetEstimate(values, coverage, "refresh_expected_value", refreshEstimate);
            Set(values, coverage, "freeze_expected_value", 0d, 0d);
            Set(values, coverage, "legal_follow_up_count", 0d, 0d);
            Set(values, coverage, "playable_follow_up_count", 0d, 0d);
            Set(values, coverage, "hand_congestion", nextHandAvailable ? Ratio(Count(nextState.HandMinions), 10) : 0d,
                nextHandAvailable ? 1d : 0d);
            Set(values, coverage, "board_congestion", nextBoardAvailable ? Ratio(Count(nextState.BoardMinions), 7) : 0d,
                nextBoardAvailable ? 1d : 0d);
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
            Set(values, coverage, "unknown_effect_count", 0d, 0d);
            Set(values, coverage, "simulator_coverage", 0d, 0d);
            Set(values, coverage, "semantic_coverage", 0d, 0d);
            Set(values, coverage, "stochastic_range", 0d, 0d);
            Set(values, coverage, "state_observation_completeness", ObservationCompleteness(state), 1d);
            Set(values, coverage, "low_health_x_immediate_power", 0d, 0d);
            Set(values, coverage, "board_full_x_buy", nextBoardAvailable
                ? Ratio(Count(nextState.BoardMinions), 7) * IsBuy(action.Action.Type) : 0d,
                nextBoardAvailable ? 1d : 0d);
            Set(values, coverage, "level_x_damage_exposure", 0d, 0d);
            Set(values, coverage, "pair_x_matching_shop", 0d, 0d);
            Set(values, coverage, "remaining_gold_x_follow_ups", 0d, 0d);
            Set(values, coverage, "transition_x_protected_value", 0d, 0d);
            Set(values, coverage, "target_tier_delta", 0d, 0d);
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

        private static void SetEstimate(double[] values, double[] coverage, string name,
            ExpectedShopEstimate estimate)
        {
            var index = ActionFeatureSchema.IndexOf(name);
            if (estimate == null || !estimate.IsAvailable || !IsFinite(estimate.ExpectedValue)
                || !IsFinite(estimate.Coverage) || estimate.Coverage <= 0d
                || estimate.Coverage > 1d)
            {
                values[index] = 0d;
                coverage[index] = 0d;
                return;
            }
            values[index] = estimate.ExpectedValue;
            coverage[index] = estimate.Coverage;
        }

        private static double Total(IEnumerable<MinionData> minions, Func<MinionData, int> selector)
        {
            return minions == null ? 0d : minions.Where(x => x != null).Sum(x => (double)selector(x));
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

        private static bool HasExactSeasonProvenance(GameState state, ISeasonFeatureExtension extension)
        {
            return state != null && extension != null
                && IsKnownIdentity(state.GameBuild)
                && IsKnownIdentity(state.SeasonId)
                && IsKnownIdentity(state.CardPoolIdentity)
                && IsKnownIdentity(extension.GameBuild)
                && IsKnownIdentity(extension.SeasonId)
                && IsKnownIdentity(extension.CardPoolIdentity)
                && string.Equals(state.GameBuild, extension.GameBuild, StringComparison.Ordinal)
                && string.Equals(state.SeasonId, extension.SeasonId, StringComparison.Ordinal)
                && string.Equals(state.CardPoolIdentity, extension.CardPoolIdentity, StringComparison.Ordinal);
        }

        private static bool IsKnownIdentity(string identity)
        {
            return !string.IsNullOrWhiteSpace(identity)
                && !string.Equals(identity.Trim(), "unknown", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasResolvedCardIdentities(params IEnumerable<MinionData>[] groups)
        {
            return groups.All(group => group != null && group.All(card => card != null
                && IsKnownIdentity(card.CardId)));
        }

        private static bool HasCompleteMinionCollection(IEnumerable<MinionData> minions)
        {
            return minions != null && minions.All(card => card != null);
        }

        private static Dictionary<string, int> OwnedCounts(GameState state)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var card in (state.BoardMinions ?? new List<MinionData>()).Concat(state.HandMinions ?? new List<MinionData>()))
            {
                if (card == null || card.Golden || card.IsSpell || !IsKnownIdentity(card.CardId)) continue;
                counts[card.CardId] = counts.ContainsKey(card.CardId) ? counts[card.CardId] + 1 : 1;
            }
            return counts;
        }

        private static double PairCount(GameState state) { return OwnedCounts(state).Sum(x => Math.Floor(x.Value / 2d)); }
        private static double TripleProgress(GameState state) { return OwnedCounts(state).Sum(x => Math.Min(x.Value, 3) / 3d); }

        private static double ObservationCompleteness(GameState state)
        {
            var present = 0;
            if (state.GameActive) present++;
            if (string.Equals(state.Phase, "shop", StringComparison.Ordinal)) present++;
            if (state.Gold >= 0) present++;
            if (state.TavernTier > 0) present++;
            if (state.MaxHealth > 0) present++;
            if (HasCompleteMinionCollection(state.BoardMinions)) present++;
            if (HasCompleteMinionCollection(state.HandMinions)) present++;
            if (HasCompleteMinionCollection(state.ShopMinions)) present++;
            if (state.Opponents != null) present++;
            return Ratio(present, 9d);
        }
    }
}
