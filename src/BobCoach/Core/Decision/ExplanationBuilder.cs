using System;
using System.Collections.Generic;
using System.Linq;

namespace BobCoach.Engine.Decision
{
    public sealed class ExplanationBuilder
    {
        private sealed class ReasonPhrases
        {
            public string Positive { get; private set; }
            public string Negative { get; private set; }

            public ReasonPhrases(string positive, string negative)
            {
                Positive = positive;
                Negative = negative;
            }
        }

        private static readonly IDictionary<string, ReasonPhrases> KnownReasons =
            new Dictionary<string, ReasonPhrases>(StringComparer.Ordinal)
            {
                { "board_power_delta", new ReasonPhrases(
                    "board power supports this action", "board power weighs against this action") },
                { "remaining_gold", new ReasonPhrases(
                    "remaining gold supports this action", "remaining gold weighs against this action") },
                { "unknown_effect_count", new ReasonPhrases(
                    "the uncertain effect profile supports this action",
                    "the uncertain effect profile weighs against this action") },
                { "legal_follow_up_count", new ReasonPhrases(
                    "legal follow-up options support this action",
                    "legal follow-up options weigh against this action") },
                { "playable_follow_up_count", new ReasonPhrases(
                    "playable follow-up options support this action",
                    "playable follow-up options weigh against this action") },
                { "expected_damage_exposure", new ReasonPhrases(
                    "expected damage exposure supports this action",
                    "expected damage exposure weighs against this action") },
                { "low_health_tempo_need", new ReasonPhrases(
                    "low-health tempo needs support this action",
                    "low-health tempo needs weigh against this action") },
                { "weak_board_level_risk", new ReasonPhrases(
                    "weak-board level risk supports this action",
                    "weak-board level risk weighs against this action") },
                { "hand_congestion", new ReasonPhrases(
                    "hand congestion supports this action", "hand congestion weighs against this action") },
                { "board_congestion", new ReasonPhrases(
                    "board congestion supports this action", "board congestion weighs against this action") },
                { "triple_progress_delta", new ReasonPhrases(
                    "triple progress supports this action", "triple progress weighs against this action") },
                { "transition_cost", new ReasonPhrases(
                    "transition cost supports this action", "transition cost weighs against this action") },
                { "simulator_coverage", new ReasonPhrases(
                    "simulation coverage supports this action",
                    "simulation coverage weighs against this action") },
                { "semantic_coverage", new ReasonPhrases(
                    "understood effect coverage supports this action",
                    "understood effect coverage weighs against this action") },
                { "state_observation_completeness", new ReasonPhrases(
                    "observed state completeness supports this action",
                    "observed state completeness weighs against this action") },
            };

        public string Build(RankedAction selected, PlanningResult planning)
        {
            if (selected == null) return "";

            var reasons = (selected.Factors ?? new List<DecisionFactor>())
                .Where(factor => factor != null
                    && factor.Contribution != 0d
                    && KnownReasons.ContainsKey(factor.FeatureName ?? ""))
                .OrderByDescending(factor => Math.Abs(factor.Contribution))
                .ThenBy(factor => factor.FeatureName, StringComparer.Ordinal)
                .Select(factor => factor.Contribution > 0d
                    ? KnownReasons[factor.FeatureName].Positive
                    : KnownReasons[factor.FeatureName].Negative)
                .Distinct(StringComparer.Ordinal)
                .Take(3)
                .ToList();

            if (reasons.Count < 3 && planning != null
                && planning.Status == PlannerStatus.Completed)
                reasons.Add("follow-up path stays flexible");

            return string.Join("; ", reasons.Take(3));
        }
    }
}
