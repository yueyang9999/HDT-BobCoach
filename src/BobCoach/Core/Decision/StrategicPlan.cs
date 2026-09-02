using System.Collections.Generic;
using System.Linq;

namespace BobCoach.Engine
{
    public enum StrategyObjective
    {
        Flexible,
        Economy,
        SearchCore,
        Stabilize,
        TechPivot,
        Survival,
    }

    public sealed class PlannedStep
    {
        public GameAction Action;
        public double Value;
        public string Reason = "";
    }

    public sealed class StrategyPlan
    {
        public StrategyObjective Objective = StrategyObjective.Flexible;
        public string CompDir = "";
        public string GuideId = "";
        public List<PlannedStep> Steps = new List<PlannedStep>();
        public double ExpectedValue;
        public double Risk;
        public string Reason = "";
        public string FallbackActionKey = "";
        public string PlanHash = "";
    }

    public static class StrategyPlanText
    {
        public static string ToCompactString(StrategyPlan plan)
        {
            if (plan == null) return "";
            var actions = plan.Steps == null
                ? ""
                : string.Join(";", plan.Steps
                    .Select(step => ActionKey(step != null ? step.Action : null)));
            return string.Join("|",
                plan.Objective.ToString(),
                plan.CompDir ?? "",
                plan.GuideId ?? "",
                actions,
                plan.Reason ?? "",
                plan.FallbackActionKey ?? "");
        }

        public static string BuildPlanHash(StrategyPlan plan)
        {
            string value = ToCompactString(plan);
            if (string.IsNullOrEmpty(value)) return "none";
            unchecked
            {
                int hash = 17;
                foreach (char ch in value)
                    hash = hash * 31 + ch;
                return hash.ToString("x8");
            }
        }

        private static string ActionKey(GameAction action)
        {
            if (action == null) return "-";
            return action.Type + ":" + action.TargetIndex + ":" + (action.CardId ?? "");
        }
    }
}
