using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
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

namespace BobCoach.Engine.Decision
{
    public sealed class RankingModelPayload
    {
        public double[] Means { get; set; }
        public double[] StandardDeviations { get; set; }
        public double[] Weights { get; set; }
        public double[] ActionTypeBiases { get; set; }
        public double Intercept { get; set; }
    }

    public sealed class ActionDescriptor
    {
        public GameAction Action { get; private set; }
        public string ActionKey { get; private set; }

        public ActionDescriptor(GameAction action, string actionKey)
        {
            Action = action;
            ActionKey = actionKey;
        }
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
            TreatsAllNullRootsAsNotRequested();
            TraversesDeterministicallyWithinHardBounds();
            IgnoresUncalibratedContinuationScores();
            RetainsBestCompletePathWhenBudgetExpires();
            ExcludesUnavailableAndUnsupportedPathsFromCompletedResults();
            StopsAtEveryInterruptibleBudgetBoundary();
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

        var resolvedIdentity = ExpectedShopEstimate.Unavailable("known-pool");
        AssertEqual("unknown", resolvedIdentity.Identity,
            "unavailable estimate never retains a resolved identity");
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

    private static void TreatsAllNullRootsAsNotRequested()
    {
        var result = new BeamPlanner(SyntheticGraph.Deep(), new ExpectedShopEstimator(),
            new FakeClock(0.01d)).Plan(new GameState(),
                new RankedAction[] { null, null }, TimeSpan.FromMilliseconds(25));

        AssertEqual(PlannerStatus.NotRequested, result.Status,
            "all-null roots are not a planning request");
    }

    private static void TraversesDeterministicallyWithinHardBounds()
    {
        var graph = SyntheticGraph.Deep();
        var ranked = Roots(8);
        var original = new GameState();
        var first = new BeamPlanner(graph, new ExpectedShopEstimator(),
            new FakeClock(0.01d)).Plan(original, ranked, TimeSpan.FromMilliseconds(25));
        var second = new BeamPlanner(graph, new ExpectedShopEstimator(),
            new FakeClock(0.01d)).Plan(new GameState(), ranked, TimeSpan.FromMilliseconds(25));

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

    private static void IgnoresUncalibratedContinuationScores()
    {
        var graph = SyntheticGraph.UncalibratedScores();
        var result = new BeamPlanner(graph, new ExpectedShopEstimator(),
            new FakeClock(0.01d)).Plan(new GameState(), new[] { Ranked("root", 10d) },
                TimeSpan.FromMilliseconds(25));

        var expandedChildren = graph.AppliedActionKeys.Skip(1).Take(6).ToList();
        AssertEqual("root/child-a|root/child-b|root/child-c|root/child-d|root/child-e|root/child-f",
            string.Join("|", expandedChildren),
            "continuation traversal uses ordinal keys rather than uncalibrated scores");
        AssertEqual("root/child-a", result.BestCompletePath[1],
            "uncalibrated child score cannot select the retained path");
    }

    private static void RetainsBestCompletePathWhenBudgetExpires()
    {
        var result = new BeamPlanner(SyntheticGraph.Deep(), new ExpectedShopEstimator(),
            new FakeClock(5)).Plan(new GameState(), Roots(8),
                TimeSpan.FromMilliseconds(25));

        AssertEqual(PlannerStatus.TimedOut, result.Status, "timeout status");
        AssertTrue(result.BestCompletePath.Count > 0, "best completed path retained");
        AssertTrue(result.ExpandedRoots > 0 && result.ExpandedRoots <= 4,
            "timeout retains bounded roots");
    }

    private static void ExcludesUnavailableAndUnsupportedPathsFromCompletedResults()
    {
        var graph = SyntheticGraph.Deep();
        var refresh = Ranked("refresh", 100d, ActionType.Refresh);
        var result = new BeamPlanner(graph, new ExpectedShopEstimator(),
            new FakeClock(0.01d)).Plan(new GameState(), new[] { refresh },
                TimeSpan.FromMilliseconds(25));

        AssertEqual(PlannerStatus.Completed, result.Status, "unavailable refresh completes safely");
        AssertEqual(0, result.BestCompletePath.Count,
            "unavailable refresh is not a completed path");
        AssertEqual(0, graph.ApplyCount, "unavailable refresh is never simulated");

        var unsupported = new BeamPlanner(graph, new ExpectedShopEstimator(),
            new FakeClock(0.01d)).Plan(new GameState(),
                new[] { Ranked("discover", 100d, ActionType.PickDiscover) },
                TimeSpan.FromMilliseconds(25));
        AssertEqual(0, unsupported.BestCompletePath.Count,
            "unsupported stochastic action is not a completed path");
        AssertEqual(0, graph.ApplyCount, "unsupported action is never simulated");

        var nullAction = new BeamPlanner(graph, new ExpectedShopEstimator(),
            new FakeClock(0.01d)).Plan(new GameState(),
                new[] { new RankedAction(null, "null-action", 100d, null) },
                TimeSpan.FromMilliseconds(25));
        AssertEqual(0, nullAction.BestCompletePath.Count,
            "null action is not a completed path");

        var nullTransition = new BeamPlanner(SyntheticGraph.NullTransition(),
            new ExpectedShopEstimator(), new FakeClock(0.01d)).Plan(new GameState(),
                new[] { Ranked("null-transition", 100d) }, TimeSpan.FromMilliseconds(25));
        AssertEqual(0, nullTransition.BestCompletePath.Count,
            "null transition is not a completed path");

        var terminal = new BeamPlanner(SyntheticGraph.Terminal(),
            new ExpectedShopEstimator(), new FakeClock(0.01d)).Plan(new GameState(),
                new[] { Ranked("terminal", 100d) }, TimeSpan.FromMilliseconds(25));
        AssertEqual(0, terminal.BestCompletePath.Count,
            "terminal transition without a state is not a completed path");
    }

    private static void StopsAtEveryInterruptibleBudgetBoundary()
    {
        var rootGraph = SyntheticGraph.Deep();
        var rootResult = new BeamPlanner(rootGraph, new ExpectedShopEstimator(),
            new FakeClock(10d)).Plan(new GameState(), Roots(4),
                TimeSpan.FromMilliseconds(25));
        AssertEqual(PlannerStatus.TimedOut, rootResult.Status,
            "root boundary timeout status");
        AssertEqual(1, rootGraph.ApplyCount,
            "budget expiry prevents the next root transition");

        var rankClock = new ManualClock();
        var rankGraph = SyntheticGraph.AdvanceAfterRank(rankClock, 30d);
        var rankResult = new BeamPlanner(rankGraph, new ExpectedShopEstimator(), rankClock)
            .Plan(new GameState(), new[] { Ranked("root", 10d) },
                TimeSpan.FromMilliseconds(25));
        AssertEqual(PlannerStatus.TimedOut, rankResult.Status,
            "post-rank boundary timeout status");
        AssertEqual(1, rankGraph.ApplyCount,
            "timeout after ranking prevents a child transition");

        var applyClock = new ManualClock();
        var applyGraph = SyntheticGraph.AdvanceAfterFirstChildApply(applyClock, 30d);
        var applyResult = new BeamPlanner(applyGraph, new ExpectedShopEstimator(), applyClock)
            .Plan(new GameState(), new[] { Ranked("root", 10d) },
                TimeSpan.FromMilliseconds(25));
        AssertEqual(PlannerStatus.TimedOut, applyResult.Status,
            "post-apply boundary timeout status");
        AssertEqual(2, applyGraph.ApplyCount,
            "timeout after one child prevents subsequent child transitions");
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

        var exposesReplacementSwitch = typeof(BeamPlanner).GetConstructors(
            BindingFlags.Instance | BindingFlags.Public).Any(constructor =>
                constructor.GetParameters().Any(parameter => parameter.ParameterType == typeof(bool)));
        AssertTrue(!exposesReplacementSwitch,
            "production constructors cannot expose root replacement");
    }

    private static void RunsLatencyHarnesses()
    {
        Console.WriteLine("VALIDATION machine={0} os={1} processors={2} framework={3}",
            Environment.MachineName, Environment.OSVersion, Environment.ProcessorCount,
            Environment.Version);
        var featureCount = ActionFeatureSchema.Names.Count;
        var ranker = new LinearPairwiseRanker(new RankingModelPayload
        {
            Means = new double[featureCount],
            StandardDeviations = Enumerable.Repeat(1d, featureCount).ToArray(),
            Weights = Enumerable.Repeat(0.01d, featureCount).ToArray(),
            ActionTypeBiases = new double[ActionTypeBiases.OrderedTypes.Count],
            Intercept = 0d,
        });
        var rankingCandidates = Enumerable.Range(0, 16).Select(index =>
            new LinearRankedCandidate(new ActionDescriptor(new GameAction
            {
                Type = ActionType.BuyMinion,
                CardId = "candidate-" + index,
                PurchaseSource = "synthetic",
                TargetIndex = -1,
            }, "candidate-" + index), new ActionFeatureVector(
                Enumerable.Repeat((double)index, featureCount).ToArray(),
                Enumerable.Repeat(1d, featureCount).ToArray()))).ToList();

        var rankingSamples = new List<double>();
        for (var index = 0; index < 1000; index++)
        {
            var stopwatch = Stopwatch.StartNew();
            ranker.Rank(rankingCandidates);
            stopwatch.Stop();
            rankingSamples.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        var planningSamples = new List<double>();
        for (var index = 0; index < 500; index++)
        {
            var stopwatch = Stopwatch.StartNew();
            new BeamPlanner(SyntheticGraph.Deep(), new ExpectedShopEstimator(),
                new FakeClock(0.01d)).Plan(new GameState(), Roots(8),
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

    private sealed class ManualClock : IMonotonicElapsedTimeSource
    {
        private TimeSpan _elapsed;
        public TimeSpan Elapsed { get { return _elapsed; } }
        public void Advance(double milliseconds)
        {
            _elapsed += TimeSpan.FromMilliseconds(milliseconds);
        }
    }

    private sealed class SyntheticGraph : IPlanningTransitionSource
    {
        private readonly bool _replacement;
        private readonly bool _uncalibratedScores;
        private readonly ManualClock _clock;
        private readonly double _advanceAfterRank;
        private readonly double _advanceAfterFirstChildApply;
        private readonly bool _nullTransition;
        private readonly bool _terminal;
        public int ApplyCount { get; private set; }
        public IList<string> AppliedActionKeys { get; private set; }

        private SyntheticGraph(bool replacement = false, bool uncalibratedScores = false,
            ManualClock clock = null, double advanceAfterRank = 0d,
            double advanceAfterFirstChildApply = 0d, bool nullTransition = false,
            bool terminal = false)
        {
            _replacement = replacement;
            _uncalibratedScores = uncalibratedScores;
            _clock = clock;
            _advanceAfterRank = advanceAfterRank;
            _advanceAfterFirstChildApply = advanceAfterFirstChildApply;
            _nullTransition = nullTransition;
            _terminal = terminal;
            AppliedActionKeys = new List<string>();
        }

        public static SyntheticGraph Deep() { return new SyntheticGraph(); }
        public static SyntheticGraph Replacement() { return new SyntheticGraph(replacement: true); }
        public static SyntheticGraph UncalibratedScores()
        { return new SyntheticGraph(uncalibratedScores: true); }
        public static SyntheticGraph AdvanceAfterRank(ManualClock clock, double milliseconds)
        { return new SyntheticGraph(clock: clock, advanceAfterRank: milliseconds); }
        public static SyntheticGraph AdvanceAfterFirstChildApply(ManualClock clock,
            double milliseconds)
        { return new SyntheticGraph(clock: clock, advanceAfterFirstChildApply: milliseconds); }
        public static SyntheticGraph NullTransition()
        { return new SyntheticGraph(nullTransition: true); }
        public static SyntheticGraph Terminal()
        { return new SyntheticGraph(terminal: true); }

        public PlanningTransition Apply(GameState state, RankedAction action)
        {
            ApplyCount++;
            AppliedActionKeys.Add(action.ActionKey);
            if (_clock != null && _advanceAfterFirstChildApply > 0d
                && action.ActionKey.IndexOf("/child-", StringComparison.Ordinal) >= 0
                && AppliedActionKeys.Count(key => key.IndexOf("/child-",
                    StringComparison.Ordinal) >= 0) == 1)
                _clock.Advance(_advanceAfterFirstChildApply);
            if (_nullTransition) return null;
            if (_terminal) return PlanningTransition.Terminal();
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
            if (_clock != null && _advanceAfterRank > 0d)
                _clock.Advance(_advanceAfterRank);
            if (_uncalibratedScores)
            {
                return new[] { "h", "g", "f", "e", "d", "c", "b", "a" }
                    .Select((suffix, index) => Ranked(state.NodeKey + "/child-" + suffix,
                        index == 0 ? 1000000d : -index)).ToList();
            }
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
