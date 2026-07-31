using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BobCoach.Engine;

namespace BobCoach.Engine.Decision
{
    public interface IMonotonicElapsedTimeSource
    {
        TimeSpan Elapsed { get; }
    }

    public sealed class StopwatchElapsedTimeSource : IMonotonicElapsedTimeSource
    {
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        public TimeSpan Elapsed { get { return _stopwatch.Elapsed; } }
    }

    public sealed class PlanningTransition
    {
        public GameState State { get; private set; }
        public bool CanContinue { get; private set; }

        public PlanningTransition(GameState state, bool canContinue)
        {
            State = state;
            CanContinue = canContinue && state != null;
        }

        public static PlanningTransition Terminal()
        {
            return new PlanningTransition(null, false);
        }
    }

    public interface IPlanningTransitionSource
    {
        PlanningTransition Apply(GameState state, RankedAction action);
        IList<RankedAction> Rank(GameState state);
    }

    public sealed class PlanningResult
    {
        public PlannerStatus Status { get; private set; }
        public string ActionKey { get; private set; }
        public IList<string> BestCompletePath { get; private set; }
        public int ExpandedRoots { get; private set; }
        public int MaximumBeamWidth { get; private set; }
        public int MaximumDepth { get; private set; }

        private PlanningResult(PlannerStatus status, string actionKey, IList<string> path,
            int expandedRoots, int maximumBeamWidth, int maximumDepth)
        {
            Status = status;
            ActionKey = actionKey ?? "";
            BestCompletePath = path == null
                ? new List<string>() : new List<string>(path);
            ExpandedRoots = expandedRoots;
            MaximumBeamWidth = maximumBeamWidth;
            MaximumDepth = maximumDepth;
        }

        public static PlanningResult NotRequested()
        {
            return new PlanningResult(PlannerStatus.NotRequested, "", new List<string>(),
                0, 0, 0);
        }

        public static PlanningResult Completed(string actionKey, IList<string> path,
            int expandedRoots, int maximumBeamWidth, int maximumDepth)
        {
            return new PlanningResult(PlannerStatus.Completed, actionKey, path,
                expandedRoots, maximumBeamWidth, maximumDepth);
        }

        public static PlanningResult TimedOut(string actionKey, IList<string> path,
            int expandedRoots, int maximumBeamWidth, int maximumDepth)
        {
            return new PlanningResult(PlannerStatus.TimedOut, actionKey, path,
                expandedRoots, maximumBeamWidth, maximumDepth);
        }

        public static PlanningResult Unsupported(string actionKey)
        {
            return new PlanningResult(PlannerStatus.Unsupported, actionKey,
                new List<string>(), 0, 0, 0);
        }
    }

    public sealed class BeamPlanner
    {
        private const int RootLimit = 4;
        private const int BeamWidth = 6;
        private const int DepthLimit = 4;

        private readonly IPlanningTransitionSource _source;
        private readonly ExpectedShopEstimator _shopEstimator;
        private readonly IMonotonicElapsedTimeSource _clock;
        private readonly bool _allowRootReplacement;

        public BeamPlanner(IPlanningTransitionSource source,
            ExpectedShopEstimator shopEstimator = null,
            IMonotonicElapsedTimeSource clock = null,
            bool allowRootReplacement = false)
        {
            _source = source;
            _shopEstimator = shopEstimator ?? new ExpectedShopEstimator();
            _clock = clock ?? new StopwatchElapsedTimeSource();
            _allowRootReplacement = allowRootReplacement;
        }

        public PlanningResult Plan(GameState state, IList<RankedAction> ranked,
            TimeSpan budget)
        {
            if (state == null || ranked == null || ranked.Count == 0)
                return PlanningResult.NotRequested();

            var orderedRoots = StableRank(ranked).Take(RootLimit).ToList();
            var rankedRootKey = orderedRoots[0].ActionKey ?? "";
            if (_source == null) return PlanningResult.Unsupported(rankedRootKey);

            var started = _clock.Elapsed;
            var roots = orderedRoots.Select(root => CreateNode(state, root, null)).ToList();
            var bestComplete = Best(null, roots);
            var maximumWidth = roots.Count;
            var maximumDepth = roots.Count == 0 ? 0 : 1;
            var beam = roots.Where(node => node.CanContinue).ToList();

            for (var depth = 2; depth <= DepthLimit && beam.Count > 0; depth++)
            {
                if (BudgetExpired(started, budget))
                    return TimedOut(rankedRootKey, bestComplete, roots.Count,
                        maximumWidth, maximumDepth);

                var expanded = new List<PlanningNode>();
                foreach (var parent in beam)
                {
                    if (BudgetExpired(started, budget))
                        return TimedOut(rankedRootKey, bestComplete, roots.Count,
                            maximumWidth, maximumDepth);

                    var children = _source.Rank(parent.State);
                    foreach (var action in StableRank(children).Take(BeamWidth))
                    {
                        var child = CreateNode(parent.State, action, parent);
                        expanded.Add(child);
                        bestComplete = Best(bestComplete, new[] { child });
                    }
                }

                expanded = expanded.OrderByDescending(node => node.PathScore)
                    .ThenBy(node => node.PathKey, StringComparer.Ordinal)
                    .Take(BeamWidth).ToList();
                maximumWidth = Math.Max(maximumWidth, expanded.Count);
                if (expanded.Count > 0) maximumDepth = depth;
                bestComplete = Best(bestComplete, expanded);
                beam = expanded.Where(node => node.CanContinue).ToList();
            }

            if (BudgetExpired(started, budget))
                return TimedOut(rankedRootKey, bestComplete, roots.Count,
                    maximumWidth, maximumDepth);
            return Complete(rankedRootKey, bestComplete, roots.Count, maximumWidth,
                maximumDepth);
        }

        private PlanningNode CreateNode(GameState state, RankedAction action,
            PlanningNode parent)
        {
            var path = parent == null
                ? new List<string>() : new List<string>(parent.Path);
            path.Add(action.ActionKey ?? "");
            var score = (parent == null ? 0d : parent.PathScore) + FiniteScore(action.Score);

            if (action.Action == null) return new PlanningNode(null, path, score, false);
            if (action.Action.Type == ActionType.Refresh)
            {
                var estimate = _shopEstimator.Estimate(state);
                if (!estimate.IsAvailable)
                    return new PlanningNode(null, path,
                        parent == null ? FiniteScore(action.Score) : parent.PathScore, false);
                return new PlanningNode(null, path, score + estimate.ExpectedValue, false);
            }
            if (!IsDeterministic(action.Action.Type))
                return new PlanningNode(null, path, score, false);

            var transition = _source.Apply(state, action);
            if (transition == null)
                return new PlanningNode(null, path, score, false);
            return new PlanningNode(transition.State, path, score,
                transition.CanContinue);
        }

        private static bool IsDeterministic(ActionType type)
        {
            return type == ActionType.BuyMinion || type == ActionType.SellMinion
                || type == ActionType.Upgrade;
        }

        private bool BudgetExpired(TimeSpan started, TimeSpan budget)
        {
            return budget <= TimeSpan.Zero || _clock.Elapsed - started >= budget;
        }

        private PlanningResult Complete(string rankedRootKey, PlanningNode best,
            int roots, int width, int depth)
        {
            var actionKey = SelectedRoot(rankedRootKey, best);
            return PlanningResult.Completed(actionKey, best == null ? null : best.Path,
                roots, width, depth);
        }

        private PlanningResult TimedOut(string rankedRootKey, PlanningNode best,
            int roots, int width, int depth)
        {
            var actionKey = SelectedRoot(rankedRootKey, best);
            return PlanningResult.TimedOut(actionKey, best == null ? null : best.Path,
                roots, width, depth);
        }

        private string SelectedRoot(string rankedRootKey, PlanningNode best)
        {
            return _allowRootReplacement && best != null && best.Path.Count > 0
                ? best.Path[0] : rankedRootKey;
        }

        private static IEnumerable<RankedAction> StableRank(IEnumerable<RankedAction> actions)
        {
            return (actions ?? Enumerable.Empty<RankedAction>())
                .Where(action => action != null)
                .OrderByDescending(action => FiniteScore(action.Score))
                .ThenBy(action => action.ActionKey ?? "", StringComparer.Ordinal);
        }

        private static double FiniteScore(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value)
                ? double.MinValue : value;
        }

        private static PlanningNode Best(PlanningNode current,
            IEnumerable<PlanningNode> candidates)
        {
            return (candidates ?? Enumerable.Empty<PlanningNode>())
                .Where(candidate => candidate != null)
                .Concat(current == null ? Enumerable.Empty<PlanningNode>()
                    : new[] { current })
                .OrderByDescending(candidate => candidate.PathScore)
                .ThenBy(candidate => candidate.PathKey, StringComparer.Ordinal)
                .FirstOrDefault();
        }

        private sealed class PlanningNode
        {
            public GameState State { get; private set; }
            public IList<string> Path { get; private set; }
            public string PathKey { get; private set; }
            public double PathScore { get; private set; }
            public bool CanContinue { get; private set; }

            public PlanningNode(GameState state, IList<string> path, double pathScore,
                bool canContinue)
            {
                State = state;
                Path = path ?? new List<string>();
                PathKey = string.Join("\u001f", Path);
                PathScore = pathScore;
                CanContinue = canContinue && state != null;
            }
        }
    }
}
