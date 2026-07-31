using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BobCoach.Engine;

namespace BobCoach.Engine.Decision
{
    public enum DecisionRuntimeMode { LegacyOnly, Shadow, Unified }

    public interface IShadowComparisonSink
    {
        void RecordComparison(ShadowComparison comparison);
    }

    public sealed class NoOpShadowComparisonSink : IShadowComparisonSink
    {
        public static readonly NoOpShadowComparisonSink Instance = new NoOpShadowComparisonSink();

        private NoOpShadowComparisonSink() { }

        public void RecordComparison(ShadowComparison comparison) { }
    }

    public interface IDecisionDiagnostics
    {
        void RecordRateLimited(string source, Exception exception);
    }

    public sealed class NoOpDecisionDiagnostics : IDecisionDiagnostics
    {
        public static readonly NoOpDecisionDiagnostics Instance = new NoOpDecisionDiagnostics();

        private NoOpDecisionDiagnostics() { }

        public void RecordRateLimited(string source, Exception exception) { }
    }

    public sealed class ShadowComparison
    {
        public string DecisionId { get; private set; }
        public string ComparisonId { get; private set; }
        public string StateFingerprint { get; private set; }
        public string ComparedActionFingerprint { get; private set; }
        public string LegacyPolicyId { get; private set; }
        public string UnifiedPolicyId { get; private set; }
        public string LegacySelectedActionKey { get; private set; }
        public string UnifiedSelectedActionKey { get; private set; }
        public bool Agrees { get; private set; }
        public double LegacyConfidence { get; private set; }
        public double UnifiedConfidence { get; private set; }
        public PlannerStatus LegacyPlannerStatus { get; private set; }
        public PlannerStatus UnifiedPlannerStatus { get; private set; }
        public FallbackReason LegacyFallbackReason { get; private set; }
        public FallbackReason UnifiedFallbackReason { get; private set; }
        public double ScoreMargin { get; private set; }

        public static ShadowComparison Create(string decisionId, GameState state,
            DecisionResult legacy, DecisionResult unified)
        {
            if (state == null) throw new ArgumentNullException("state");
            if (legacy == null) throw new ArgumentNullException("legacy");
            if (unified == null) throw new ArgumentNullException("unified");

            var stateFingerprint = Fingerprint(CanonicalJson.Serialize(GameStateSnapshot.From(state)));
            var comparedActionFingerprint = Fingerprint(CanonicalJson.Serialize(ComparedActionKeys(legacy, unified)));
            var legacyPolicyId = legacy.PolicyId ?? "";
            var unifiedPolicyId = unified.PolicyId ?? "";
            return new ShadowComparison
            {
                DecisionId = decisionId ?? "",
                StateFingerprint = stateFingerprint,
                ComparedActionFingerprint = comparedActionFingerprint,
                LegacyPolicyId = legacyPolicyId,
                UnifiedPolicyId = unifiedPolicyId,
                LegacySelectedActionKey = ActionKey(legacy.SelectedAction),
                UnifiedSelectedActionKey = ActionKey(unified.SelectedAction),
                Agrees = string.Equals(ActionKey(legacy.SelectedAction), ActionKey(unified.SelectedAction),
                    StringComparison.Ordinal),
                LegacyConfidence = legacy.Confidence,
                UnifiedConfidence = unified.Confidence,
                LegacyPlannerStatus = legacy.PlannerStatus,
                UnifiedPlannerStatus = unified.PlannerStatus,
                LegacyFallbackReason = legacy.FallbackReason,
                UnifiedFallbackReason = unified.FallbackReason,
                ScoreMargin = CalculateScoreMargin(legacy, unified),
                ComparisonId = Fingerprint(CanonicalJson.Serialize(new[]
                {
                    decisionId ?? "", stateFingerprint, comparedActionFingerprint, legacyPolicyId, unifiedPolicyId
                }))
            };
        }

        private static IEnumerable<string> ComparedActionKeys(DecisionResult legacy,
            DecisionResult unified)
        {
            return (legacy.RankedActions ?? new List<RankedAction>())
                .Concat(unified.RankedActions ?? new List<RankedAction>())
                .Where(action => action != null)
                .Select(action => action.ActionKey ?? "")
                .Distinct(StringComparer.Ordinal)
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToList();
        }

        private static string ActionKey(GameAction action)
        {
            return action == null ? "" : ActionDescriptor.CreateKey(action);
        }

        private static double CalculateScoreMargin(DecisionResult legacy, DecisionResult unified)
        {
            var legacyScore = legacy.RankedActions.Count == 0 ? 0d : legacy.RankedActions[0].Score;
            var unifiedScore = unified.RankedActions.Count == 0 ? 0d : unified.RankedActions[0].Score;
            return unifiedScore - legacyScore;
        }

        private static string Fingerprint(string value)
        {
            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? "")))
                    .Replace("-", "").ToLowerInvariant();
            }
        }
    }

    public sealed class DecisionOrchestrator
    {
        private readonly IDecisionPolicy _legacy;
        private readonly IDecisionPolicy _unified;
        private readonly IShadowComparisonSink _comparisonSink;
        private readonly IDecisionDiagnostics _diagnostics;
        private readonly DecisionRuntimeMode _mode;

        public DecisionOrchestrator(IDecisionPolicy legacy, IDecisionPolicy unified,
            IShadowComparisonSink comparisonSink, DecisionRuntimeMode mode,
            IDecisionDiagnostics diagnostics = null)
        {
            if (legacy == null) throw new ArgumentNullException("legacy");
            if (unified == null) throw new ArgumentNullException("unified");
            _legacy = legacy;
            _unified = unified;
            _comparisonSink = comparisonSink ?? NoOpShadowComparisonSink.Instance;
            _diagnostics = diagnostics ?? NoOpDecisionDiagnostics.Instance;
            _mode = mode == DecisionRuntimeMode.Shadow || mode == DecisionRuntimeMode.Unified
                ? mode
                : DecisionRuntimeMode.LegacyOnly;
        }

        public DecisionResult Decide(GameState state)
        {
            return DecideCore(state, null);
        }

        public DecisionResult Decide(GameState state, string decisionId)
        {
            if (string.IsNullOrWhiteSpace(decisionId)) throw new ArgumentException("decisionId");
            return DecideCore(state, decisionId);
        }

        private DecisionResult DecideCore(GameState state, string decisionId)
        {
            var legacy = SafeDecide(_legacy, state, FallbackReason.NoSafeAction);
            if (_mode == DecisionRuntimeMode.LegacyOnly) return legacy;

            var unified = SafeDecide(_unified, state, FallbackReason.ModelUnavailable);
            TryRecordComparison(decisionId, state, legacy, unified);
            return legacy;
        }

        private void TryRecordComparison(string decisionId, GameState state,
            DecisionResult legacy, DecisionResult unified)
        {
            try
            {
                _comparisonSink.RecordComparison(ShadowComparison.Create(decisionId, state, legacy, unified));
            }
            catch (Exception ex) { TryRecordDiagnostic("shadow-comparison", ex); }
        }

        private DecisionResult SafeDecide(IDecisionPolicy policy, GameState state,
            FallbackReason exceptionReason)
        {
            try
            {
                var result = policy.Decide(state);
                if (result == null) throw new InvalidOperationException("policy returned null");
                return result;
            }
            catch (Exception ex)
            {
                TryRecordDiagnostic(policy.PolicyId, ex);
                return UnavailableDecisionPolicy.For(exceptionReason).Decide(state);
            }
        }

        private void TryRecordDiagnostic(string source, Exception exception)
        {
            try { _diagnostics.RecordRateLimited(source, exception); }
            catch { }
        }
    }
}
