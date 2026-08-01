using System;
using System.IO;
using BobCoach.Engine;

namespace BobCoach.Engine.Decision
{
    internal sealed class EffectEvaluationContext
    {
        public string ProtocolId { get; private set; }
        public string ProtocolVersion { get; private set; }
        public string ApprovalStatus { get; private set; }
        public string ApprovedBy { get; private set; }
        public DateTime ApprovedAtUtc { get; private set; }
        public DateTime FrozenAtUtc { get; private set; }
        public DateTime HoldoutOpenedAtUtc { get; private set; }
        public string ProtocolSha256 { get; private set; }
        public string BenchmarkId { get; private set; }
        public string BaselinePolicyId { get; private set; }
        public string CandidatePolicyId { get; private set; }
        public string CandidateModelId { get; private set; }
        public string CandidateModelPayloadSha256 { get; private set; }
        public string HoldoutIdentity { get; private set; }
        public string HoldoutChecksumSha256 { get; private set; }
        public string ActualHoldoutChecksumSha256 { get; private set; }
        public string GameBuild { get; private set; }
        public string SeasonId { get; private set; }
        public string CardPoolIdentity { get; private set; }
        public string TrainingSetIdentity { get; private set; }
        public string SeasonRange { get; private set; }
        public ConfidenceCalibration Calibration { get; private set; }
        public ConfidenceCalibrationApprovalContext CalibrationApproval { get; private set; }

        public EffectEvaluationContext(string protocolId, string protocolVersion,
            string approvalStatus, string approvedBy, DateTime approvedAtUtc, DateTime frozenAtUtc,
            DateTime holdoutOpenedAtUtc, string protocolSha256, string benchmarkId,
            string baselinePolicyId, string candidatePolicyId, string candidateModelId,
            string candidateModelPayloadSha256, string holdoutIdentity,
            string holdoutChecksumSha256, string actualHoldoutChecksumSha256,
            string gameBuild, string seasonId, string cardPoolIdentity,
            string trainingSetIdentity, string seasonRange,
            ConfidenceCalibration calibration,
            ConfidenceCalibrationApprovalContext calibrationApproval)
        {
            ProtocolId = protocolId;
            ProtocolVersion = protocolVersion;
            ApprovalStatus = approvalStatus;
            ApprovedBy = approvedBy;
            ApprovedAtUtc = approvedAtUtc;
            FrozenAtUtc = frozenAtUtc;
            HoldoutOpenedAtUtc = holdoutOpenedAtUtc;
            ProtocolSha256 = protocolSha256;
            BenchmarkId = benchmarkId;
            BaselinePolicyId = baselinePolicyId;
            CandidatePolicyId = candidatePolicyId;
            CandidateModelId = candidateModelId;
            CandidateModelPayloadSha256 = candidateModelPayloadSha256;
            HoldoutIdentity = holdoutIdentity;
            HoldoutChecksumSha256 = holdoutChecksumSha256;
            ActualHoldoutChecksumSha256 = actualHoldoutChecksumSha256;
            GameBuild = gameBuild;
            SeasonId = seasonId;
            CardPoolIdentity = cardPoolIdentity;
            TrainingSetIdentity = trainingSetIdentity;
            SeasonRange = seasonRange;
            Calibration = calibration;
            CalibrationApproval = calibrationApproval;
        }
    }

    public sealed class DecisionPolicyBundle
    {
        public IDecisionPolicy Legacy { get; private set; }
        public IDecisionPolicy Unified { get; private set; }
        public CandidateGenerator Candidates { get; private set; }
        public string ModelId { get; private set; }
        public string ModelPayloadSha256 { get; private set; }

        internal DecisionPolicyBundle(IDecisionPolicy legacy, IDecisionPolicy unified,
            CandidateGenerator candidates, string modelId, string modelPayloadSha256)
        {
            Legacy = legacy;
            Unified = unified;
            Candidates = candidates;
            ModelId = string.IsNullOrWhiteSpace(modelId) ? "unknown" : modelId;
            ModelPayloadSha256 = string.IsNullOrWhiteSpace(modelPayloadSha256)
                ? "unknown" : modelPayloadSha256;
        }
    }

    public static class DecisionPolicyFactory
    {
        public static DecisionPolicyBundle Create(DecisionEngine engine, Stream modelArtifact,
            ISeasonFeatureExtension seasonExtension)
        {
            if (engine == null) throw new ArgumentNullException("engine");

            var provider = seasonExtension as IRankingModelRuntimeContextProvider;
            var runtimeContext = provider == null ? null : provider.GetRuntimeContext();
            return Create(engine, modelArtifact, seasonExtension, runtimeContext);
        }

        internal static DecisionPolicyBundle CreateForEffectEvaluation(DecisionEngine engine,
            Stream modelArtifact, ISeasonFeatureExtension seasonExtension,
            EffectEvaluationContext context)
        {
            if (engine == null) throw new ArgumentNullException("engine");
            if (modelArtifact == null) throw new ArgumentNullException("modelArtifact");
            if (seasonExtension == null) throw new ArgumentNullException("seasonExtension");
            if (context == null) throw new ArgumentNullException("context");

            var legacy = new LegacyDecisionPolicy(engine);
            var candidates = new CandidateGenerator(new ActionEnumerator());
            var model = RankingModelLoader.Load(modelArtifact);
            ValidateEffectEvaluationContext(model, seasonExtension, context);
            var unified = UnifiedRankingPolicy.CreateForEffectEvaluation(model, candidates,
                seasonExtension, context.Calibration, context.CalibrationApproval);
            return new DecisionPolicyBundle(legacy, unified, candidates,
                model.ModelId, model.PayloadSha256);
        }

        public static DecisionPolicyBundle Create(DecisionEngine engine, Stream modelArtifact,
            ISeasonFeatureExtension seasonExtension, RankingModelRuntimeContext runtimeContext)
        {
            if (engine == null) throw new ArgumentNullException("engine");

            var legacy = new LegacyDecisionPolicy(engine);
            var candidates = new CandidateGenerator(new ActionEnumerator());
            if (modelArtifact == null || seasonExtension == null)
                return Unavailable(legacy, candidates);

            RankingModelPayload model;
            try
            {
                model = RankingModelLoader.LoadForRuntime(modelArtifact, runtimeContext);
            }
            catch (ModelValidationException)
            {
                return Unavailable(legacy, candidates);
            }
            catch (IOException)
            {
                return Unavailable(legacy, candidates);
            }

            var unified = UnifiedRankingPolicy.CreateProduction(model, candidates,
                seasonExtension);
            return new DecisionPolicyBundle(legacy, unified, candidates,
                model.ModelId, model.PayloadSha256);
        }

        private static DecisionPolicyBundle Unavailable(IDecisionPolicy legacy,
            CandidateGenerator candidates)
        {
            return new DecisionPolicyBundle(legacy,
                UnavailableDecisionPolicy.For(FallbackReason.ModelUnavailable), candidates,
                "unknown", "unknown");
        }

        private static void ValidateEffectEvaluationContext(RankingModelPayload model,
            ISeasonFeatureExtension seasonExtension, EffectEvaluationContext context)
        {
            RequireResolved(context.ProtocolId, "protocol identity");
            RequireResolved(context.ProtocolVersion, "protocol version");
            if (!IsProtocolVersion(context.ProtocolVersion))
                throw InvalidEvaluation("protocol version");
            RequireResolved(context.ApprovedBy, "protocol approver");
            RequireResolved(context.BenchmarkId, "benchmark identity");
            RequireResolved(context.HoldoutIdentity, "holdout identity");
            if (!string.Equals(context.BenchmarkId, "season-2026-next-v1",
                    StringComparison.Ordinal))
                throw InvalidEvaluation("benchmark identity");
            if (!string.Equals(context.ApprovalStatus, "owner-approved", StringComparison.Ordinal))
                throw InvalidEvaluation("protocol approval");
            if (context.ApprovedAtUtc.Kind != DateTimeKind.Utc
                || context.FrozenAtUtc.Kind != DateTimeKind.Utc
                || context.HoldoutOpenedAtUtc.Kind != DateTimeKind.Utc
                || context.ApprovedAtUtc > context.FrozenAtUtc
                || context.FrozenAtUtc > context.HoldoutOpenedAtUtc)
                throw InvalidEvaluation("protocol approval and freeze ordering");
            if (!IsSha256(context.ProtocolSha256)
                || !IsSha256(context.HoldoutChecksumSha256)
                || !IsSha256(context.ActualHoldoutChecksumSha256)
                || !IsSha256(context.CandidateModelPayloadSha256))
                throw InvalidEvaluation("checksum binding");
            if (!string.Equals(context.HoldoutChecksumSha256,
                    context.ActualHoldoutChecksumSha256, StringComparison.Ordinal)
                || !string.Equals(context.BaselinePolicyId, "legacy-v1.1.0",
                    StringComparison.Ordinal)
                || !string.Equals(context.CandidatePolicyId, "unified-linear-v1",
                    StringComparison.Ordinal)
                || !string.Equals(context.CandidateModelId, model.ModelId,
                    StringComparison.Ordinal)
                || !string.Equals(context.CandidateModelPayloadSha256, model.PayloadSha256,
                    StringComparison.Ordinal)
                || !string.Equals(context.GameBuild, model.GameBuild, StringComparison.Ordinal)
                || !string.Equals(context.SeasonId, model.SeasonId, StringComparison.Ordinal)
                || !string.Equals(context.CardPoolIdentity, model.CardPoolIdentity,
                    StringComparison.Ordinal)
                || !string.Equals(context.TrainingSetIdentity, model.TrainingSetIdentity,
                    StringComparison.Ordinal)
                || !string.Equals(context.SeasonRange, model.SeasonRange,
                    StringComparison.Ordinal)
                || !string.Equals(context.GameBuild, seasonExtension.GameBuild,
                    StringComparison.Ordinal)
                || !string.Equals(context.SeasonId, seasonExtension.SeasonId,
                    StringComparison.Ordinal)
                || !string.Equals(context.CardPoolIdentity, seasonExtension.CardPoolIdentity,
                    StringComparison.Ordinal))
                throw InvalidEvaluation("identity binding");

            var calibration = context.Calibration;
            var approval = context.CalibrationApproval;
            if (calibration == null || approval == null
                || !string.Equals(calibration.Status, "accepted", StringComparison.Ordinal)
                || !string.Equals(calibration.GameBuild, model.GameBuild, StringComparison.Ordinal)
                || !string.Equals(calibration.SeasonId, model.SeasonId, StringComparison.Ordinal)
                || !string.Equals(calibration.CardPoolIdentity, model.CardPoolIdentity,
                    StringComparison.Ordinal)
                || !string.Equals(calibration.TrainingSetIdentity, model.TrainingSetIdentity,
                    StringComparison.Ordinal)
                || !string.Equals(calibration.ModelId, model.ModelId, StringComparison.Ordinal)
                || !string.Equals(calibration.HoldoutIdentity, context.HoldoutIdentity,
                    StringComparison.Ordinal)
                || !string.Equals(approval.HoldoutIdentity, context.HoldoutIdentity,
                    StringComparison.Ordinal)
                || !string.Equals(approval.CalibrationIdentity,
                    calibration.CalibrationIdentity, StringComparison.Ordinal)
                || !string.Equals(approval.ModelId, model.ModelId, StringComparison.Ordinal))
                throw InvalidEvaluation("calibration binding");
        }

        private static void RequireResolved(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value)
                || string.Equals(value.Trim(), "unknown", StringComparison.OrdinalIgnoreCase)
                || value.Trim().StartsWith("synthetic-", StringComparison.OrdinalIgnoreCase))
                throw InvalidEvaluation(name);
        }

        private static bool IsSha256(string value)
        {
            if (value == null || value.Length != 64) return false;
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if (!((character >= '0' && character <= '9')
                    || (character >= 'a' && character <= 'f'))) return false;
            }
            return true;
        }

        private static bool IsProtocolVersion(string value)
        {
            if (value == null) return false;
            var parts = value.Split('.');
            if (parts.Length != 3) return false;
            foreach (var part in parts)
            {
                if (part.Length == 0) return false;
                if (part.Length > 1 && part[0] == '0') return false;
                for (var index = 0; index < part.Length; index++)
                    if (part[index] < '0' || part[index] > '9') return false;
            }
            return true;
        }

        private static ModelValidationException InvalidEvaluation(string reason)
        {
            return new ModelValidationException(
                "Invalid effect evaluation context: " + reason);
        }
    }
}
