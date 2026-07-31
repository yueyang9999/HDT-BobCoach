using System;
using System.IO;
using BobCoach.Engine;

namespace BobCoach.Engine.Decision
{
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

            var legacy = new LegacyDecisionPolicy(engine);
            var candidates = new CandidateGenerator(new ActionEnumerator());
            return Unavailable(legacy, candidates);
        }

        public static DecisionPolicyBundle Create(DecisionEngine engine, Stream modelArtifact,
            ISeasonFeatureExtension seasonExtension, RankingModelRuntimeContext runtimeContext)
        {
            if (engine == null) throw new ArgumentNullException("engine");

            var legacy = new LegacyDecisionPolicy(engine);
            var candidates = new CandidateGenerator(new ActionEnumerator());
            if (modelArtifact == null || seasonExtension == null)
                return Unavailable(legacy, candidates);

            try
            {
                var model = RankingModelLoader.LoadForRuntime(modelArtifact, runtimeContext);
                var unified = UnifiedRankingPolicy.CreateProduction(model, candidates,
                    seasonExtension);
                return new DecisionPolicyBundle(legacy, unified, candidates,
                    model.ModelId, model.PayloadSha256);
            }
            catch (ModelValidationException)
            {
                return Unavailable(legacy, candidates);
            }
            catch (IOException)
            {
                return Unavailable(legacy, candidates);
            }
        }

        private static DecisionPolicyBundle Unavailable(IDecisionPolicy legacy,
            CandidateGenerator candidates)
        {
            return new DecisionPolicyBundle(legacy,
                UnavailableDecisionPolicy.For(FallbackReason.ModelUnavailable), candidates,
                "unknown", "unknown");
        }
    }
}
