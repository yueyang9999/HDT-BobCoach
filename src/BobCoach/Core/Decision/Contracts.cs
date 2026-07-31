using System.Collections.Generic;
using BobCoach.Engine;

namespace BobCoach.Engine.Decision
{
    public enum PlannerStatus { NotRequested, Completed, TimedOut, Unsupported }

    public enum FallbackReason
    {
        None,
        ModelUnavailable,
        CriticalStateMissing,
        UnsupportedMechanic,
        OutOfDistribution,
        LowConfidence,
        NoSafeAction,
    }

    public sealed class DecisionFactor
    {
        public string FeatureName { get; private set; }
        public double Contribution { get; private set; }

        public DecisionFactor(string featureName, double contribution)
        {
            FeatureName = featureName ?? "";
            Contribution = contribution;
        }
    }

    public sealed class RankedAction
    {
        public GameAction Action { get; private set; }
        public string ActionKey { get; private set; }
        public double Score { get; private set; }
        public IList<DecisionFactor> Factors { get; private set; }

        public RankedAction(GameAction action, string actionKey, double score,
            IList<DecisionFactor> factors)
        {
            Action = action;
            ActionKey = actionKey;
            Score = score;
            Factors = factors ?? new List<DecisionFactor>();
        }
    }

    public sealed class DecisionResult
    {
        public string PolicyId { get; private set; }
        public GameAction SelectedAction { get; private set; }
        public IList<RankedAction> RankedActions { get; private set; }
        public double Confidence { get; private set; }
        public PlannerStatus PlannerStatus { get; private set; }
        public FallbackReason FallbackReason { get; private set; }
        public string Explanation { get; private set; }

        public DecisionResult(string policyId, GameAction selectedAction,
            IList<RankedAction> rankedActions, double confidence,
            PlannerStatus plannerStatus, FallbackReason fallbackReason, string explanation)
        {
            PolicyId = policyId ?? "";
            SelectedAction = selectedAction;
            RankedActions = rankedActions ?? new List<RankedAction>();
            Confidence = confidence;
            PlannerStatus = plannerStatus;
            FallbackReason = fallbackReason;
            Explanation = explanation ?? "";
        }
    }

    public interface IDecisionPolicy
    {
        string PolicyId { get; }
        DecisionResult Decide(GameState state);
    }
}
