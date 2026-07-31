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
        private readonly ExpectedShopEstimator _shopEstimator;
        private readonly BeamPlanner _planner;
        private readonly ExplanationBuilder _explanationBuilder;

        public string PolicyId { get { return "unified-linear-v1"; } }
        public CandidateGenerator Candidates { get { return _candidateGenerator; } }

        private UnifiedRankingPolicy(RankingModelPayload model, CandidateGenerator candidateGenerator,
            ISeasonFeatureExtension seasonExtension,
            DecisionConfidenceEvaluator confidenceEvaluator,
            IExpectedShopPoolProvider shopPoolProvider)
        {
            _seasonExtension = seasonExtension;
            _candidateGenerator = candidateGenerator;
            _constraintFilter = new HardConstraintFilter();
            _simulator = new Simulator();
            _shopEstimator = new ExpectedShopEstimator(shopPoolProvider);
            _featureExtractor = new ActionFeatureExtractor(seasonExtension, _shopEstimator);
            _ranker = new LinearPairwiseRanker(model);
            _model = model;
            _confidenceEvaluator = confidenceEvaluator;
            _planner = new BeamPlanner(new ProductionPlanningTransitionSource(this, _simulator),
                _shopEstimator);
            _explanationBuilder = new ExplanationBuilder();
        }

        internal static UnifiedRankingPolicy CreateProduction(RankingModelPayload model,
            ISeasonFeatureExtension seasonExtension)
        {
            return CreateProduction(model, new CandidateGenerator(new ActionEnumerator()),
                seasonExtension, null, null, null);
        }

        internal static UnifiedRankingPolicy CreateProduction(RankingModelPayload model,
            CandidateGenerator candidates, ISeasonFeatureExtension seasonExtension)
        {
            return CreateProduction(model, candidates, seasonExtension, null, null, null);
        }

        internal static UnifiedRankingPolicy CreateProduction(RankingModelPayload model,
            ISeasonFeatureExtension seasonExtension, ConfidenceCalibration calibration,
            ConfidenceCalibrationApprovalContext approvalContext)
        {
            return CreateProduction(model, new CandidateGenerator(new ActionEnumerator()),
                seasonExtension, calibration, approvalContext, null);
        }

        internal static UnifiedRankingPolicy CreateProduction(RankingModelPayload model,
            ISeasonFeatureExtension seasonExtension, ConfidenceCalibration calibration,
            ConfidenceCalibrationApprovalContext approvalContext,
            IExpectedShopPoolProvider shopPoolProvider)
        {
            return CreateProduction(model, new CandidateGenerator(new ActionEnumerator()),
                seasonExtension, calibration, approvalContext, shopPoolProvider);
        }

        private static UnifiedRankingPolicy CreateProduction(RankingModelPayload model,
            CandidateGenerator candidates, ISeasonFeatureExtension seasonExtension,
            ConfidenceCalibration calibration,
            ConfidenceCalibrationApprovalContext approvalContext,
            IExpectedShopPoolProvider shopPoolProvider)
        {
            if (model == null) throw new ArgumentNullException("model");
            if (candidates == null) throw new ArgumentNullException("candidates");
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
            return new UnifiedRankingPolicy(model, candidates, seasonExtension,
                new DecisionConfidenceEvaluator(calibration, approvalContext), shopPoolProvider);
        }

        public DecisionResult Decide(GameState state)
        {
            if (!HasMatchingStateIdentity(state))
                return UnavailableDecisionPolicy.For(FallbackReason.ModelUnavailable).Decide(state);

            var evaluations = EvaluateCandidates(state, false);
            var ranked = Rank(evaluations);
            if (ranked.Count == 0)
                return UnavailableDecisionPolicy.For(FallbackReason.NoSafeAction).Decide(state);

            var top = evaluations.First(item => string.Equals(item.Descriptor.ActionKey,
                ranked[0].ActionKey, StringComparison.Ordinal));
            var assessment = _confidenceEvaluator.Evaluate(state, ranked, top.Features, _model);
            var planning = assessment.ShouldPlan
                ? _planner.Plan(state, ranked, TimeSpan.FromMilliseconds(25d))
                : PlanningResult.NotRequested();
            return new DecisionResult(PolicyId, ranked[0].Action, ranked, assessment.Score,
                planning.Status, assessment.FallbackReason,
                _explanationBuilder.Build(ranked[0], planning));
        }

        private IList<CandidateEvaluation> EvaluateCandidates(GameState state,
            bool deterministicOnly)
        {
            var candidates = _constraintFilter.Apply(state,
                _candidateGenerator.Generate(state));
            var evaluations = new List<CandidateEvaluation>();
            foreach (var candidate in candidates)
            {
                if (deterministicOnly
                    && candidate.SimulatorSupport != SimulatorSupport.Deterministic)
                    continue;
                var nextState = _simulator.Simulate(state, candidate.Action);
                if (nextState == null) continue;
                var features = _featureExtractor.Extract(state, candidate, nextState);
                ApplySimulatorUncertainty(candidate, features);
                evaluations.Add(new CandidateEvaluation(candidate, features));
            }
            return evaluations;
        }

        private IList<RankedAction> Rank(IList<CandidateEvaluation> evaluations)
        {
            return _ranker.Rank((evaluations ?? new List<CandidateEvaluation>()).Select(item =>
                new LinearRankedCandidate(item.Descriptor, item.Features)));
        }

        private IList<RankedAction> RankDeterministic(GameState state)
        {
            return Rank(EvaluateCandidates(state, true));
        }

        private static void ApplySimulatorUncertainty(ActionDescriptor descriptor,
            ActionFeatureVector features)
        {
            if (descriptor == null || features == null) return;
            var simulatorCoverage = ActionFeatureSchema.IndexOf("simulator_coverage");
            var unknownEffects = ActionFeatureSchema.IndexOf("unknown_effect_count");
            var refreshValue = ActionFeatureSchema.IndexOf("refresh_expected_value");
            var expectedValueComplete = descriptor.Action != null
                && descriptor.Action.Type == ActionType.Refresh
                && descriptor.SimulatorSupport == SimulatorSupport.ExpectedValue
                && features.Coverage[refreshValue] > 0d;
            var complete = descriptor.SimulatorSupport == SimulatorSupport.Deterministic
                || expectedValueComplete;
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

            public CandidateEvaluation(ActionDescriptor descriptor, ActionFeatureVector features)
            {
                Descriptor = descriptor;
                Features = features;
            }
        }

        private sealed class ProductionPlanningTransitionSource : IPlanningTransitionSource
        {
            private readonly UnifiedRankingPolicy _policy;
            private readonly Simulator _simulator;

            public ProductionPlanningTransitionSource(UnifiedRankingPolicy policy,
                Simulator simulator)
            {
                _policy = policy;
                _simulator = simulator;
            }

            public PlanningTransition Apply(GameState state, RankedAction action)
            {
                if (state == null || action == null || action.Action == null
                    || (action.Action.Type != ActionType.BuyMinion
                        && action.Action.Type != ActionType.SellMinion
                        && action.Action.Type != ActionType.Upgrade))
                    return PlanningTransition.Terminal();
                var next = _simulator.Simulate(state, action.Action);
                return next == null ? PlanningTransition.Terminal()
                    : new PlanningTransition(next, true);
            }

            public IList<RankedAction> Rank(GameState state)
            {
                return _policy.RankDeterministic(state);
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
