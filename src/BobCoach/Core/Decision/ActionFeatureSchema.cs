using System;
using System.Collections.Generic;
using BobCoach.Engine;

namespace BobCoach.Engine.Decision
{
    public static class ActionFeatureSchema
    {
        public const int Version = 1;

        public static readonly IList<string> Names = new[]
        {
            "turn", "tavern_tier", "health_armor", "gold_before", "hand_slots_free", "board_slots_free",
            "immediate_attack_delta", "immediate_health_delta", "immediate_keyword_delta", "unit_count_delta", "board_power_delta",
            "net_gold_cost", "remaining_gold", "wasted_gold", "refresh_expected_value", "freeze_expected_value",
            "legal_follow_up_count", "playable_follow_up_count", "hand_congestion", "board_congestion",
            "pair_count_delta", "triple_progress_delta", "scaling_engine_delta", "protected_component_delta",
            "expected_damage_exposure", "low_health_tempo_need", "weak_board_level_risk",
            "current_synergy", "target_compatibility", "abandoned_piece_count", "abandoned_piece_value", "transition_cost",
            "unknown_effect_count", "simulator_coverage", "semantic_coverage", "stochastic_range", "state_observation_completeness",
            "low_health_x_immediate_power", "board_full_x_buy", "level_x_damage_exposure", "pair_x_matching_shop",
            "remaining_gold_x_follow_ups", "transition_x_protected_value", "target_tier_delta", "season_extension_value"
        };

        public static int IndexOf(string name)
        {
            if (name == null) throw new ArgumentNullException("name");
            for (var i = 0; i < Names.Count; i++)
                if (string.Equals(Names[i], name, StringComparison.Ordinal)) return i;
            throw new ArgumentOutOfRangeException("name", "Unknown action feature.");
        }
    }

    public sealed class ActionFeatureVector
    {
        public double[] Values { get; private set; }
        public double[] Coverage { get; private set; }

        public double this[string name] { get { return Values[ActionFeatureSchema.IndexOf(name)]; } }

        public ActionFeatureVector(double[] values, double[] coverage)
        {
            if (values == null) throw new ArgumentNullException("values");
            if (coverage == null) throw new ArgumentNullException("coverage");
            if (values.Length != ActionFeatureSchema.Names.Count
                || coverage.Length != ActionFeatureSchema.Names.Count)
                throw new ArgumentException("Feature values and coverage must match the v1 schema.");
            Values = values;
            Coverage = coverage;
        }
    }

    public interface ISeasonFeatureExtension
    {
        string GameBuild { get; }
        string SeasonId { get; }
        string CardPoolIdentity { get; }
        bool TryEvaluate(GameState state, ActionDescriptor action, GameState nextState, out double value);
    }

    public sealed class NullSeasonFeatureExtension : ISeasonFeatureExtension
    {
        public static readonly NullSeasonFeatureExtension Instance = new NullSeasonFeatureExtension();
        public string GameBuild { get { return "unknown"; } }
        public string SeasonId { get { return "none"; } }
        public string CardPoolIdentity { get { return "unknown"; } }

        private NullSeasonFeatureExtension() { }

        public bool TryEvaluate(GameState state, ActionDescriptor action, GameState nextState, out double value)
        {
            value = 0d;
            return false;
        }
    }

    // Action type is categorical and deliberately kept outside the ordered 45-value schema.
    public static class ActionTypeBiases
    {
        public static readonly ActionType[] OrderedTypes = (ActionType[])Enum.GetValues(typeof(ActionType));

        public static double[] OneHot(ActionType type)
        {
            var result = new double[OrderedTypes.Length];
            for (var i = 0; i < OrderedTypes.Length; i++)
                if (OrderedTypes[i] == type) result[i] = 1d;
            return result;
        }
    }
}
