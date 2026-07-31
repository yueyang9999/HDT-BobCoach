using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BobCoach.Engine;
using BobCoach.Engine.Decision;

namespace BobCoach.Engine
{
    public enum ActionType
    {
        BuyMinion, BuySpell, Refresh, Upgrade, SellMinion, FreezeShop,
        UseHeroPower, PickTrinket, PickDiscover, SendGoldToTeammate,
    }

    public sealed class GameAction
    {
        public ActionType Type;
        public int TargetIndex;
        public string CardId;
        public string PurchaseSource;
    }

    public sealed class GameState
    {
        public string GameBuild = "known-build";
        public string SeasonId = "known-season";
        public string CardPoolIdentity = "known-pool";
        public string NodeKey = "start";
        public int MutationMarker;
    }
}

internal static class AlgorithmBeamPlannerBehavior
{
    private static int Main()
    {
        try
        {
            ReturnsExplicitUnavailableExpectedShopEstimate();
            UsesOnlyCompleteVersionMatchedExpectedShopFacts();
            TraversesDeterministicallyWithinHardBounds();
            RetainsBestCompletePathWhenBudgetExpires();
            StopsUnavailableRefreshWithoutInventingAStateOrValue();
            KeepsProductionPlanningDiagnosticOnly();
            RunsLatencyHarnesses();
            Console.WriteLine("PASS beam planning is deterministic, bounded, and timeout-safe");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL " + ex.Message);
            return 1;
        }
    }

    private static void ReturnsExplicitUnavailableExpectedShopEstimate()
    {
        var estimate = new ExpectedShopEstimator().Estimate(new GameState());
        AssertTrue(!estimate.IsAvailable, "missing pool is unavailable");
        AssertEqual(0d, estimate.ExpectedValue, "unavailable expected value");
        AssertEqual(0d, estimate.Coverage, "unavailable coverage");
        AssertEqual("unknown", estimate.Identity, "unavailable identity");
    }

    private static void UsesOnlyCompleteVersionMatchedExpectedShopFacts()
    {
        var estimator = new ExpectedShopEstimator(new CompletePoolProvider());
        var available = estimator.Estimate(new GameState());
        AssertTrue(available.IsAvailable, "complete matching pool is available");
        AssertEqual(2.5d, available.ExpectedValue, "provider expected value");
        AssertEqual(0.8d, available.Coverage, "provider coverage");
        AssertEqual("known-pool", available.Identity, "provider identity");

        var unknown = estimator.Estimate(new GameState { GameBuild = "unknown" });
        AssertTrue(!unknown.IsAvailable, "unresolved build fails closed");
    }

    private static void TraversesDeterministicallyWithinHardBounds()
    {
        var graph = SyntheticGraph.Deep();
        var ranked = Roots(8);
        var original = new GameState();
        var first = new BeamPlanner(graph, new ExpectedShopEstimator(),
            new FakeClock(1), true).Plan(original, ranked, TimeSpan.FromMilliseconds(25));
        var second = new BeamPlanner(graph, new ExpectedShopEstimator(),
            new FakeClock(1), true).Plan(new GameState(), ranked, TimeSpan.FromMilliseconds(25));

        AssertEqual(PlannerStatus.Completed, first.Status, "completed status");
        AssertEqual(first.ActionKey, second.ActionKey, "deterministic plan");
        AssertEqual(string.Join("|", first.BestCompletePath),
            string.Join("|", second.BestCompletePath), "deterministic path");
        AssertTrue(first.ExpandedRoots <= 4, "Top-4 roots");
        AssertTrue(first.MaximumBeamWidth <= 6, "beam width");
        AssertTrue(first.MaximumDepth <= 4, "depth");
        AssertEqual(4, first.ExpandedRoots, "observed root count");
        AssertEqual(6, first.MaximumBeamWidth, "observed beam width");
        AssertEqual(4, first.MaximumDepth, "observed depth");
        AssertEqual(0, original.MutationMarker, "simulation states stay detached");
    }

    private static void RetainsBestCompletePathWhenBudgetExpires()
    {
        var result = new BeamPlanner(SyntheticGraph.Deep(), new ExpectedShopEstimator(),
            new FakeClock(30), true).Plan(new GameState(), Roots(8),
                TimeSpan.FromMilliseconds(25));

        AssertEqual(PlannerStatus.TimedOut, result.Status, "timeout status");
        AssertTrue(result.BestCompletePath.Count > 0, "best completed path retained");
        AssertTrue(result.ExpandedRoots > 0 && result.ExpandedRoots <= 4,
            "timeout retains bounded roots");
    }

    private static void StopsUnavailableRefreshWithoutInventingAStateOrValue()
    {
        var graph = SyntheticGraph.Deep();
        var refresh = Ranked("refresh", 100d, ActionType.Refresh);
        var result = new BeamPlanner(graph, new ExpectedShopEstimator(),
            new FakeClock(1), true).Plan(new GameState(), new[] { refresh },
                TimeSpan.FromMilliseconds(25));

        AssertEqual(PlannerStatus.Completed, result.Status, "unavailable refresh completes safely");
        AssertEqual(1, result.BestCompletePath.Count, "refresh path terminates at root");
        AssertEqual(0, graph.ApplyCount, "unavailable refresh is never simulated");
    }

    private static void KeepsProductionPlanningDiagnosticOnly()
    {
        var ranked = new[]
        {
            Ranked("ranked-root", 10d),
            Ranked("planned-root", 9d),
        };
        var result = new BeamPlanner(SyntheticGraph.Replacement(),
            new ExpectedShopEstimator(), new FakeClock(1)).Plan(
                new GameState(), ranked, TimeSpan.FromMilliseconds(25));

        AssertEqual("ranked-root", result.ActionKey,
            "diagnostic planning cannot replace ranked root");
    }

    private static void RunsLatencyHarnesses()
    {
        var rankingSamples = new List<double>();
        for (var index = 0; index < 1000; index++)
        {
            var stopwatch = Stopwatch.StartNew();
            Roots(16).OrderByDescending(item => item.Score)
                .ThenBy(item => item.ActionKey, StringComparer.Ordinal).ToList();
            stopwatch.Stop();
            rankingSamples.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        var planningSamples = new List<double>();
        for (var index = 0; index < 500; index++)
        {
            var stopwatch = Stopwatch.StartNew();
            new BeamPlanner(SyntheticGraph.Deep(), new ExpectedShopEstimator(),
                new FakeClock(1), true).Plan(new GameState(), Roots(8),
                    TimeSpan.FromMilliseconds(25));
            stopwatch.Stop();
            planningSamples.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        PrintLatency("ranking", rankingSamples);
        PrintLatency("planning", planningSamples);
        AssertTrue(Percentile(rankingSamples, 0.95d) <= 10d, "ranking P95 latency");
        AssertTrue(Percentile(planningSamples, 0.95d) <= 25d, "planning P95 latency");
    }

    private static void PrintLatency(string name, IList<double> samples)
    {
        Console.WriteLine("LATENCY {0} P50={1:F3}ms P95={2:F3}ms P99={3:F3}ms",
            name, Percentile(samples, 0.50d), Percentile(samples, 0.95d),
            Percentile(samples, 0.99d));
    }

    private static double Percentile(IList<double> samples, double percentile)
    {
        var ordered = samples.OrderBy(value => value).ToList();
        var index = (int)Math.Ceiling(percentile * ordered.Count) - 1;
        return ordered[Math.Max(0, Math.Min(index, ordered.Count - 1))];
    }

    private static IList<RankedAction> Roots(int count)
    {
        return Enumerable.Range(0, count)
            .Select(index => Ranked("root-" + index, 100d - index)).ToList();
    }

    private static RankedAction Ranked(string key, double score,
        ActionType type = ActionType.BuyMinion)
    {
        return new RankedAction(new GameAction
        {
            Type = type, TargetIndex = -1, CardId = key, PurchaseSource = "synthetic",
        }, key, score, null);
    }

    private static void AssertEqual<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(label + " expected=" + expected + " actual=" + actual);
    }

    private static void AssertTrue(bool value, string label)
    {
        if (!value) throw new InvalidOperationException(label);
    }

    private sealed class CompletePoolProvider : IExpectedShopPoolProvider
    {
        public bool IsCompleteFor(string gameBuild, string seasonId, string cardPoolIdentity)
        {
            return gameBuild == "known-build" && seasonId == "known-season"
                && cardPoolIdentity == "known-pool";
        }

        public ExpectedShopEstimate EstimateFromVersionMatchedFacts(GameState state)
        {
            return ExpectedShopEstimate.Available(2.5d, 0.8d, "known-pool");
        }
    }

    private sealed class FakeClock : IMonotonicElapsedTimeSource
    {
        private readonly TimeSpan _step;
        private TimeSpan _elapsed;

        public FakeClock(double stepMilliseconds)
        {
            _step = TimeSpan.FromMilliseconds(stepMilliseconds);
        }

        public TimeSpan Elapsed
        {
            get
            {
                _elapsed += _step;
                return _elapsed;
            }
        }
    }

    private sealed class SyntheticGraph : IPlanningTransitionSource
    {
        private readonly bool _replacement;
        public int ApplyCount { get; private set; }

        private SyntheticGraph(bool replacement) { _replacement = replacement; }
        public static SyntheticGraph Deep() { return new SyntheticGraph(false); }
        public static SyntheticGraph Replacement() { return new SyntheticGraph(true); }

        public PlanningTransition Apply(GameState state, RankedAction action)
        {
            ApplyCount++;
            return new PlanningTransition(new GameState
            {
                GameBuild = state.GameBuild,
                SeasonId = state.SeasonId,
                CardPoolIdentity = state.CardPoolIdentity,
                NodeKey = action.ActionKey,
                MutationMarker = state.MutationMarker + 1,
            }, true);
        }

        public IList<RankedAction> Rank(GameState state)
        {
            if (Depth(state) >= 4) return new List<RankedAction>();
            var bonus = _replacement && state.NodeKey == "planned-root" ? 1000d : 0d;
            return Enumerable.Range(0, 8).Select(index => Ranked(
                state.NodeKey + "/child-" + index, bonus + 20d - index)).ToList();
        }

        private static int Depth(GameState state)
        {
            return state.NodeKey.Split(new[] { "/child-" }, StringSplitOptions.None).Length;
        }
    }
}
