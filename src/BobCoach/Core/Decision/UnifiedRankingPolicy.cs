using System;
using System.Collections.Generic;
using System.Linq;
using BobCoach.Engine;

namespace BobCoach.Engine.Decision
{
    // This policy is diagnostic-only. DecisionOrchestrator keeps legacy-v1.1.0 visible in Shadow.
    public sealed class UnifiedRankingPolicy : IDecisionPolicy
    {
        private readonly ISeasonFeatureExtension _seasonExtension;
        private readonly CandidateGenerator _candidateGenerator;
        private readonly HardConstraintFilter _constraintFilter;
        private readonly Simulator _simulator;
        private readonly ActionFeatureExtractor _featureExtractor;
        private readonly LinearPairwiseRanker _ranker;
        private readonly RankingModelPayload _model;
        private readonly DecisionConfidenceEvaluator _confidenceEvaluator;

        public string PolicyId { get { return "unified-linear-v1"; } }

        private UnifiedRankingPolicy(RankingModelPayload model, ISeasonFeatureExtension seasonExtension,
            DecisionConfidenceEvaluator confidenceEvaluator)
        {
            _seasonExtension = seasonExtension;
            _candidateGenerator = new CandidateGenerator(new ActionEnumerator());
            _constraintFilter = new HardConstraintFilter();
            _simulator = new Simulator();
            _featureExtractor = new ActionFeatureExtractor(seasonExtension);
            _ranker = new LinearPairwiseRanker(model);
            _model = model;
            _confidenceEvaluator = confidenceEvaluator;
        }

        public static UnifiedRankingPolicy CreateProduction(RankingModelPayload model,
            ISeasonFeatureExtension seasonExtension)
        {
            return CreateProduction(model, seasonExtension, null, null);
        }

        public static UnifiedRankingPolicy CreateProduction(RankingModelPayload model,
            ISeasonFeatureExtension seasonExtension, ConfidenceCalibration calibration,
            ConfidenceCalibrationApprovalContext approvalContext)
        {
            if (model == null) throw new ArgumentNullException("model");
            if (seasonExtension == null) throw new ArgumentNullException("seasonExtension");
            RankingModelLoader.ValidatePayloadSchema(model);
            RequireAcceptedResolvedIdentity(model.ModelId, "model identity");
            RequireAcceptedResolvedIdentity(model.TrainingSetIdentity, "training set identity");
            RequireAcceptedResolvedIdentity(model.SeasonRange, "season range identity");
            RequireAcceptedResolvedIdentity(model.GameBuild, "game build identity");
            RequireAcceptedResolvedIdentity(model.SeasonId, "season identity");
            RequireAcceptedResolvedIdentity(model.CardPoolIdentity, "card pool identity");
            RequireAcceptedResolvedIdentity(model.EffectAcceptanceStatus, "effect acceptance");
            if (IsSynthetic(model))
                throw new ModelValidationException("Invalid ranking model artifact: synthetic artifact");
            if (!string.Equals(model.EffectAcceptanceStatus, "accepted", StringComparison.Ordinal))
                throw new ModelValidationException("Invalid ranking model artifact: effect acceptance");
            RequireAcceptedResolvedIdentity(seasonExtension.GameBuild, "season extension game build");
            RequireAcceptedResolvedIdentity(seasonExtension.SeasonId, "season extension season");
            RequireAcceptedResolvedIdentity(seasonExtension.CardPoolIdentity, "season extension card pool");
            if (!string.Equals(model.GameBuild, seasonExtension.GameBuild, StringComparison.Ordinal)
                || !string.Equals(model.SeasonId, seasonExtension.SeasonId, StringComparison.Ordinal)
                || !string.Equals(model.CardPoolIdentity, seasonExtension.CardPoolIdentity, StringComparison.Ordinal))
                throw new ModelValidationException("Invalid ranking model artifact: identity mismatch");
            return new UnifiedRankingPolicy(model, seasonExtension,
                new DecisionConfidenceEvaluator(calibration, approvalContext));
        }

        public DecisionResult Decide(GameState state)
        {
            if (!HasMatchingStateIdentity(state))
                return UnavailableDecisionPolicy.For(FallbackReason.ModelUnavailable).Decide(state);

            var candidates = _constraintFilter.Apply(state, _candidateGenerator.Generate(state));
            var evaluations = new List<CandidateEvaluation>();
            foreach (var candidate in candidates)
            {
                var nextState = _simulator.Simulate(state, candidate.Action);
                if (nextState == null) continue;
                var features = _featureExtractor.Extract(state, candidate, nextState);
                ApplySimulatorUncertainty(candidate, features);
                evaluations.Add(new CandidateEvaluation(candidate, features));
            }
            var ranked = _ranker.Rank(evaluations.Select(item =>
                new LinearRankedCandidate(item.Descriptor, item.Features)));
            if (ranked.Count == 0)
                return UnavailableDecisionPolicy.For(FallbackReason.NoSafeAction).Decide(state);

            foreach (var rankedAction in ranked)
            {
                var evaluation = evaluations.FirstOrDefault(item => string.Equals(
                    item.Descriptor.ActionKey, rankedAction.ActionKey, StringComparison.Ordinal));
                if (evaluation != null) evaluation.Ranked = rankedAction;
            }
            var top = evaluations.First(item => string.Equals(item.Descriptor.ActionKey,
                ranked[0].ActionKey, StringComparison.Ordinal));
            var assessment = _confidenceEvaluator.Evaluate(state, ranked, top.Features, _model);
            return new DecisionResult(PolicyId, ranked[0].Action, ranked, assessment.Score,
                PlannerStatus.NotRequested, assessment.FallbackReason,
                assessment.ShouldFallback ? "shadow-only conservative fallback"
                    : "shadow-only diagnostic ranking");
        }

        private static void ApplySimulatorUncertainty(ActionDescriptor descriptor,
            ActionFeatureVector features)
        {
            if (descriptor == null || features == null) return;
            var simulatorCoverage = ActionFeatureSchema.IndexOf("simulator_coverage");
            var unknownEffects = ActionFeatureSchema.IndexOf("unknown_effect_count");
            var complete = descriptor.SimulatorSupport == SimulatorSupport.Deterministic
                || descriptor.SimulatorSupport == SimulatorSupport.ExpectedValue;
            features.Values[simulatorCoverage] = complete ? 1d : 0d;
            features.Coverage[simulatorCoverage] = 1d;
            if (!complete)
            {
                features.Values[unknownEffects] += 1d;
                features.Coverage[unknownEffects] = 1d;
            }
        }

        private sealed class CandidateEvaluation
        {
            public ActionDescriptor Descriptor { get; private set; }
            public ActionFeatureVector Features { get; private set; }
            public RankedAction Ranked { get; set; }

            public CandidateEvaluation(ActionDescriptor descriptor, ActionFeatureVector features)
            {
                Descriptor = descriptor;
                Features = features;
            }
        }

        private bool HasMatchingStateIdentity(GameState state)
        {
            return state != null && IsResolved(state.GameBuild) && IsResolved(state.SeasonId)
                && IsResolved(state.CardPoolIdentity)
                && string.Equals(state.GameBuild, _seasonExtension.GameBuild, StringComparison.Ordinal)
                && string.Equals(state.SeasonId, _seasonExtension.SeasonId, StringComparison.Ordinal)
                && string.Equals(state.CardPoolIdentity, _seasonExtension.CardPoolIdentity,
                    StringComparison.Ordinal);
        }

        private static void RequireAcceptedResolvedIdentity(string value, string name)
        {
            if (!IsResolved(value)) throw new ModelValidationException("Invalid ranking model artifact: " + name);
        }

        private static bool IsResolved(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && !string.Equals(value.Trim(), "unknown", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSynthetic(RankingModelPayload model)
        {
            return HasSyntheticPrefix(model.ModelId) || HasSyntheticPrefix(model.TrainingSetIdentity)
                || HasSyntheticPrefix(model.SeasonRange) || HasSyntheticPrefix(model.GameBuild)
                || HasSyntheticPrefix(model.SeasonId) || HasSyntheticPrefix(model.CardPoolIdentity);
        }

        private static bool HasSyntheticPrefix(string value)
        {
            return value != null
                && value.Trim().StartsWith("synthetic-", StringComparison.OrdinalIgnoreCase);
        }
    }
}
