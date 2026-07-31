using System;
using BobCoach.Engine;

namespace BobCoach.Engine.Decision
{
    public sealed class ExpectedShopEstimate
    {
        public bool IsAvailable { get; private set; }
        public double ExpectedValue { get; private set; }
        public double Coverage { get; private set; }
        public string Identity { get; private set; }

        private ExpectedShopEstimate(bool isAvailable, double expectedValue,
            double coverage, string identity)
        {
            IsAvailable = isAvailable;
            ExpectedValue = expectedValue;
            Coverage = coverage;
            Identity = identity ?? "unknown";
        }

        public static ExpectedShopEstimate Unavailable(string identity)
        {
            return new ExpectedShopEstimate(false, 0d, 0d, "unknown");
        }

        public static ExpectedShopEstimate Available(double expectedValue, double coverage,
            string identity)
        {
            if (!IsFinite(expectedValue) || !IsFinite(coverage) || coverage <= 0d
                || coverage > 1d || !IsResolved(identity))
                return Unavailable("unknown");
            return new ExpectedShopEstimate(true, expectedValue, coverage, identity);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        internal static bool IsResolved(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && !string.Equals(value.Trim(), "unknown", StringComparison.OrdinalIgnoreCase);
        }
    }

    public interface IExpectedShopPoolProvider
    {
        bool IsCompleteFor(string gameBuild, string seasonId, string cardPoolIdentity);
        ExpectedShopEstimate EstimateFromVersionMatchedFacts(GameState state);
    }

    public sealed class ExpectedShopEstimator
    {
        private readonly IExpectedShopPoolProvider _pool;

        public ExpectedShopEstimator(IExpectedShopPoolProvider pool = null)
        {
            _pool = pool;
        }

        public ExpectedShopEstimate Estimate(GameState state)
        {
            if (state == null || _pool == null
                || !ExpectedShopEstimate.IsResolved(state.GameBuild)
                || !ExpectedShopEstimate.IsResolved(state.SeasonId)
                || !ExpectedShopEstimate.IsResolved(state.CardPoolIdentity)
                || !_pool.IsCompleteFor(state.GameBuild, state.SeasonId,
                    state.CardPoolIdentity))
                return ExpectedShopEstimate.Unavailable("unknown");

            var estimate = _pool.EstimateFromVersionMatchedFacts(state);
            if (estimate == null || !estimate.IsAvailable
                || !string.Equals(estimate.Identity, state.CardPoolIdentity,
                    StringComparison.Ordinal))
                return ExpectedShopEstimate.Unavailable("unknown");
            return estimate;
        }
    }
}
