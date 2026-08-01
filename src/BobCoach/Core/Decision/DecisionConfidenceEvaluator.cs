using System;
using System.Collections.Generic;
using System.Linq;
using BobCoach.Engine;

namespace BobCoach.Engine.Decision
{
    public sealed class ConfidenceAssessment
    {
        public double Score { get; private set; }
        public bool ShouldFallback { get; private set; }
        public FallbackReason FallbackReason { get; private set; }
        public bool ShouldPlan { get; private set; }
        public double DistributionDistance { get; private set; }

        public ConfidenceAssessment(double score, bool shouldFallback,
            FallbackReason fallbackReason, bool shouldPlan, double distributionDistance)
        {
            Score = score;
            ShouldFallback = shouldFallback;
            FallbackReason = fallbackReason;
            ShouldPlan = shouldPlan;
            DistributionDistance = distributionDistance;
        }
    }

    // Calibration is deliberately external to the model artifact until real, version-matched
    // holdout data can produce and accept these thresholds.
    public sealed class ConfidenceCalibration
    {
        public string Status { get; private set; }
        public string GameBuild { get; private set; }
        public string SeasonId { get; private set; }
        public string CardPoolIdentity { get; private set; }
        public string TrainingSetIdentity { get; private set; }
        public string HoldoutIdentity { get; private set; }
        public string CalibrationIdentity { get; private set; }
        public string ModelId { get; private set; }
        public double MarginScale { get; private set; }
        public double SemanticCoverageFloor { get; private set; }
        public double ConfidenceThreshold { get; private set; }
        public double? PlanningThreshold { get; private set; }

        public ConfidenceCalibration(string status, string gameBuild, string seasonId,
            string cardPoolIdentity, string trainingSetIdentity, string holdoutIdentity,
            string calibrationIdentity, string modelId, double marginScale,
            double semanticCoverageFloor, double confidenceThreshold, double? planningThreshold)
        {
            Status = status;
            GameBuild = gameBuild;
            SeasonId = seasonId;
            CardPoolIdentity = cardPoolIdentity;
            TrainingSetIdentity = trainingSetIdentity;
            HoldoutIdentity = holdoutIdentity;
            CalibrationIdentity = calibrationIdentity;
            ModelId = modelId;
            MarginScale = marginScale;
            SemanticCoverageFloor = semanticCoverageFloor;
            ConfidenceThreshold = confidenceThreshold;
            PlanningThreshold = planningThreshold;
        }
    }

    public sealed class ConfidenceCalibrationApprovalContext
    {
        public string HoldoutIdentity { get; private set; }
        public string CalibrationIdentity { get; private set; }
        public string ModelId { get; private set; }

        public ConfidenceCalibrationApprovalContext(string holdoutIdentity,
            string calibrationIdentity, string modelId)
        {
            HoldoutIdentity = holdoutIdentity;
            CalibrationIdentity = calibrationIdentity;
            ModelId = modelId;
        }
    }

    public sealed class DecisionConfidenceEvaluator
    {
        private const double ConservativeFiniteDistance = 1000000000000d;
        private readonly ConfidenceCalibration _calibration;
        private readonly ConfidenceCalibrationApprovalContext _approvalContext;
        private readonly bool _allowBlockedEvaluationArtifact;

        public DecisionConfidenceEvaluator(ConfidenceCalibration calibration = null,
            ConfidenceCalibrationApprovalContext approvalContext = null)
            : this(calibration, approvalContext, false)
        {
        }

        private DecisionConfidenceEvaluator(ConfidenceCalibration calibration,
            ConfidenceCalibrationApprovalContext approvalContext,
            bool allowBlockedEvaluationArtifact)
        {
            _calibration = calibration;
            _approvalContext = approvalContext;
            _allowBlockedEvaluationArtifact = allowBlockedEvaluationArtifact;
        }

        internal static DecisionConfidenceEvaluator CreateForEffectEvaluation(
            ConfidenceCalibration calibration,
            ConfidenceCalibrationApprovalContext approvalContext)
        {
            return new DecisionConfidenceEvaluator(calibration, approvalContext, true);
        }

        public ConfidenceAssessment Evaluate(GameState state, IList<RankedAction> rankedActions,
            ActionFeatureVector vector, RankingModelPayload payload)
        {
            var calibrated = HasAcceptedVersionMatchedCalibration(state, payload);
            var criticalStateComplete = CriticalStateCompleteness(state) == 1d;
            var featureVectorValid = HasValidFeatureVector(vector);
            var decisionCriticalCoverage = featureVectorValid
                && HasDecisionCriticalCoverage(vector, payload);
            var supportedCoverage = decisionCriticalCoverage && calibrated
                && vector["semantic_coverage"] >= _calibration.SemanticCoverageFloor
                && vector["simulator_coverage"] > 0d;
            var distance = DistributionDistance(vector, payload);
            var outOfDistribution = calibrated
                && distance > payload.OodDistanceThreshold;

            var score = 0d;
            if (calibrated && criticalStateComplete && supportedCoverage)
            {
                var marginScore = MarginScore(rankedActions, _calibration.MarginScale);
                var completeness = CriticalStateCompleteness(state);
                var coverage = CoverageScore(vector);
                var distribution = Math.Exp(-Math.Max(0d, distance));
                score = Clamp01(Math.Min(Math.Min(marginScore, completeness),
                    Math.Min(coverage, distribution)));
            }

            FallbackReason reason;
            if (!calibrated) reason = FallbackReason.ModelUnavailable;
            else if (!criticalStateComplete) reason = FallbackReason.CriticalStateMissing;
            else if (!supportedCoverage) reason = FallbackReason.UnsupportedMechanic;
            else if (outOfDistribution) reason = FallbackReason.OutOfDistribution;
            else if (score < _calibration.ConfidenceThreshold) reason = FallbackReason.LowConfidence;
            else reason = FallbackReason.None;

            var upstreamGatesPassed = calibrated && criticalStateComplete && supportedCoverage
                && !outOfDistribution;
            var shouldPlan = upstreamGatesPassed && _calibration.PlanningThreshold.HasValue
                && IsFinite(_calibration.PlanningThreshold.Value)
                && score <= _calibration.PlanningThreshold.Value;
            return new ConfidenceAssessment(score, reason != FallbackReason.None, reason,
                shouldPlan, distance);
        }

        public double DistributionDistance(ActionFeatureVector vector, RankingModelPayload payload)
        {
            if (!HasFiniteDistribution(vector, payload)) return ConservativeFiniteDistance;

            var sumSquares = 0d;
            for (var index = 0; index < ActionFeatureSchema.Names.Count; index++)
            {
                var value = vector.Values[index];
                var deviation = payload.StandardDeviations[index];
                var zScore = Math.Abs((value - payload.Means[index]) / deviation);
                var lowerExcess = value < payload.Minimums[index]
                    ? (payload.Minimums[index] - value) / deviation : 0d;
                var upperExcess = value > payload.Maximums[index]
                    ? (value - payload.Maximums[index]) / deviation : 0d;
                var normalizedExcess = Math.Max(lowerExcess, upperExcess);
                var contribution = Math.Max(zScore, normalizedExcess);
                if (!IsFinite(contribution)) return ConservativeFiniteDistance;
                sumSquares += contribution * contribution;
                if (!IsFinite(sumSquares)) return ConservativeFiniteDistance;
            }

            var distance = Math.Sqrt(sumSquares / ActionFeatureSchema.Names.Count);
            return IsFinite(distance) ? distance : ConservativeFiniteDistance;
        }

        private bool HasAcceptedVersionMatchedCalibration(GameState state,
            RankingModelPayload payload)
        {
            if (_calibration == null || _approvalContext == null || state == null
                || payload == null) return false;
            var payloadStatusAccepted = string.Equals(payload.EffectAcceptanceStatus,
                "accepted", StringComparison.Ordinal);
            var payloadStatusAllowedForEvaluation = _allowBlockedEvaluationArtifact
                && string.Equals(payload.EffectAcceptanceStatus, "blocked-by-season-data",
                    StringComparison.Ordinal);
            if (!string.Equals(_calibration.Status, "accepted", StringComparison.Ordinal)
                || (!payloadStatusAccepted && !payloadStatusAllowedForEvaluation))
                return false;
            if (!IsResolved(_calibration.HoldoutIdentity)
                || !IsResolved(_calibration.CalibrationIdentity)
                || !IsResolved(_approvalContext.HoldoutIdentity)
                || !IsResolved(_approvalContext.CalibrationIdentity)
                || !IsResolved(_approvalContext.ModelId)
                || !IsResolved(_calibration.GameBuild) || !IsResolved(_calibration.SeasonId)
                || !IsResolved(_calibration.CardPoolIdentity)
                || !IsResolved(_calibration.TrainingSetIdentity)
                || !IsResolved(_calibration.ModelId)
                || !IsResolved(payload.ModelId) || !IsResolved(payload.TrainingSetIdentity)
                || !IsResolved(payload.SeasonRange) || !IsResolved(payload.GameBuild)
                || !IsResolved(payload.SeasonId) || !IsResolved(payload.CardPoolIdentity)
                || !IsResolved(state.GameBuild) || !IsResolved(state.SeasonId)
                || !IsResolved(state.CardPoolIdentity)) return false;
            if (!string.Equals(_calibration.HoldoutIdentity, _approvalContext.HoldoutIdentity,
                    StringComparison.Ordinal)
                || !string.Equals(_calibration.CalibrationIdentity,
                    _approvalContext.CalibrationIdentity, StringComparison.Ordinal)
                || !string.Equals(_calibration.ModelId, _approvalContext.ModelId,
                    StringComparison.Ordinal)
                || !string.Equals(_calibration.ModelId, payload.ModelId, StringComparison.Ordinal)
                || !string.Equals(_calibration.GameBuild, payload.GameBuild, StringComparison.Ordinal)
                || !string.Equals(_calibration.SeasonId, payload.SeasonId, StringComparison.Ordinal)
                || !string.Equals(_calibration.CardPoolIdentity, payload.CardPoolIdentity,
                    StringComparison.Ordinal)
                || !string.Equals(_calibration.TrainingSetIdentity, payload.TrainingSetIdentity,
                    StringComparison.Ordinal)
                || !string.Equals(state.GameBuild, payload.GameBuild, StringComparison.Ordinal)
                || !string.Equals(state.SeasonId, payload.SeasonId, StringComparison.Ordinal)
                || !string.Equals(state.CardPoolIdentity, payload.CardPoolIdentity,
                    StringComparison.Ordinal)) return false;
            return IsFinitePositive(_calibration.MarginScale)
                && IsFiniteUnitInterval(_calibration.SemanticCoverageFloor)
                && IsFiniteUnitInterval(_calibration.ConfidenceThreshold)
                && (!_calibration.PlanningThreshold.HasValue
                    || IsFiniteUnitInterval(_calibration.PlanningThreshold.Value))
                && IsFinitePositive(payload.OodDistanceThreshold)
                && HasFiniteDistribution(null, payload);
        }

        private static bool HasDecisionCriticalCoverage(ActionFeatureVector vector,
            RankingModelPayload payload)
        {
            if (vector == null || vector.Coverage == null || payload == null
                || payload.Weights == null || vector.Coverage.Length != ActionFeatureSchema.Names.Count
                || payload.Weights.Length != ActionFeatureSchema.Names.Count) return false;
            for (var index = 0; index < ActionFeatureSchema.Names.Count; index++)
            {
                if (!IsFinite(vector.Coverage[index]) || vector.Coverage[index] < 0d
                    || vector.Coverage[index] > 1d) return false;
                if (payload.Weights[index] != 0d && vector.Coverage[index] == 0d) return false;
            }
            return true;
        }

        private static bool HasValidFeatureVector(ActionFeatureVector vector)
        {
            var count = ActionFeatureSchema.Names.Count;
            if (vector == null || vector.Values == null || vector.Values.Length != count
                || vector.Coverage == null || vector.Coverage.Length != count) return false;
            for (var index = 0; index < count; index++)
            {
                if (!IsFinite(vector.Values[index]) || !IsFinite(vector.Coverage[index])
                    || vector.Coverage[index] < 0d || vector.Coverage[index] > 1d) return false;
            }
            return true;
        }

        private static double CoverageScore(ActionFeatureVector vector)
        {
            if (vector == null || vector.Coverage == null
                || vector.Coverage.Length != ActionFeatureSchema.Names.Count) return 0d;
            var average = vector.Coverage.Average();
            var semanticCoverage = vector["semantic_coverage"];
            if (!IsFinite(average) || !IsFinite(semanticCoverage)) return 0d;
            return Clamp01(Math.Min(average, semanticCoverage));
        }

        private static double CriticalStateCompleteness(GameState state)
        {
            if (state == null) return 0d;
            var present = 0;
            if (state.GameActive) present++;
            if (string.Equals(state.Phase, "shop", StringComparison.Ordinal)) present++;
            if (state.Gold >= 0) present++;
            if (state.TavernTier > 0) present++;
            if (state.MaxHealth > 0) present++;
            if (state.BoardMinions != null) present++;
            if (state.HandMinions != null) present++;
            if (state.ShopMinions != null) present++;
            if (state.Opponents != null) present++;
            return present / 9d;
        }

        private static double MarginScore(IList<RankedAction> rankedActions, double marginScale)
        {
            if (rankedActions == null || rankedActions.Count == 0) return 0d;
            var top = rankedActions[0];
            if (top == null || !IsFinite(top.Score)) return 0d;
            var runnerUpScore = rankedActions.Count > 1 && rankedActions[1] != null
                ? rankedActions[1].Score : top.Score;
            if (!IsFinite(runnerUpScore)) return 0d;
            return Clamp01((top.Score - runnerUpScore) / marginScale);
        }

        private static bool HasFiniteDistribution(ActionFeatureVector vector,
            RankingModelPayload payload)
        {
            var count = ActionFeatureSchema.Names.Count;
            if (payload == null || payload.FeatureSchemaVersion != ActionFeatureSchema.Version
                || payload.FeatureNames == null || payload.FeatureNames.Length != count
                || payload.Means == null || payload.Means.Length != count
                || payload.StandardDeviations == null || payload.StandardDeviations.Length != count
                || payload.Minimums == null || payload.Minimums.Length != count
                || payload.Maximums == null || payload.Maximums.Length != count
                || (vector != null && (vector.Values == null || vector.Values.Length != count)))
                return false;
            for (var index = 0; index < count; index++)
            {
                if (!string.Equals(payload.FeatureNames[index], ActionFeatureSchema.Names[index],
                    StringComparison.Ordinal)
                    || !IsFinite(payload.Means[index])
                    || !IsFinitePositive(payload.StandardDeviations[index])
                    || !IsFinite(payload.Minimums[index]) || !IsFinite(payload.Maximums[index])
                    || payload.Minimums[index] > payload.Maximums[index]
                    || (vector != null && !IsFinite(vector.Values[index]))) return false;
            }
            return true;
        }

        private static bool IsResolved(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && !string.Equals(value.Trim(), "unknown", StringComparison.OrdinalIgnoreCase);
        }

        private static double Clamp01(double value)
        {
            return IsFinite(value) ? Math.Max(0d, Math.Min(1d, value)) : 0d;
        }

        private static bool IsFiniteUnitInterval(double value)
        {
            return IsFinite(value) && value >= 0d && value <= 1d;
        }

        private static bool IsFinitePositive(double value)
        {
            return IsFinite(value) && value > 0d;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
