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

        public BeamPlanner(IPlanningTransitionSource source,
            ExpectedShopEstimator shopEstimator = null,
            IMonotonicElapsedTimeSource clock = null)
        {
            _source = source;
            _shopEstimator = shopEstimator ?? new ExpectedShopEstimator();
            _clock = clock ?? new StopwatchElapsedTimeSource();
        }

        public PlanningResult Plan(GameState state, IList<RankedAction> ranked,
            TimeSpan budget)
        {
            if (state == null || ranked == null || ranked.Count == 0)
                return PlanningResult.NotRequested();

            var started = _clock.Elapsed;
            var orderedRoots = StableRank(ranked).Take(RootLimit).ToList();
            var rankedRootKey = orderedRoots[0].ActionKey ?? "";
            if (_source == null) return PlanningResult.Unsupported(rankedRootKey);

            var roots = new List<PlanningNode>();
            PlanningNode bestComplete = null;
            var expandedRoots = 0;
            var maximumWidth = 0;
            var maximumDepth = 0;

            foreach (var root in orderedRoots)
            {
                if (BudgetExpired(started, budget))
                    return TimedOut(rankedRootKey, bestComplete, expandedRoots,
                        maximumWidth, maximumDepth);

                var node = CreateNode(state, root, null);
                expandedRoots++;
                if (node != null)
                {
                    roots.Add(node);
                    bestComplete = Best(bestComplete, new[] { node });
                    maximumWidth = Math.Max(maximumWidth, roots.Count);
                    maximumDepth = 1;
                }

                if (BudgetExpired(started, budget))
                    return TimedOut(rankedRootKey, bestComplete, expandedRoots,
                        maximumWidth, maximumDepth);
            }

            var beam = roots.Where(node => node.CanContinue).ToList();

            for (var depth = 2; depth <= DepthLimit && beam.Count > 0; depth++)
            {
                var expanded = new List<PlanningNode>();
                foreach (var parent in beam)
                {
                    if (BudgetExpired(started, budget))
                        return TimedOut(rankedRootKey, bestComplete, expandedRoots,
                            maximumWidth, maximumDepth);

                    var children = _source.Rank(parent.State);
                    if (BudgetExpired(started, budget))
                        return TimedOut(rankedRootKey, bestComplete, expandedRoots,
                            maximumWidth, maximumDepth);

                    foreach (var action in StableTraversal(children).Take(BeamWidth))
                    {
                        if (BudgetExpired(started, budget))
                            return TimedOut(rankedRootKey, bestComplete, expandedRoots,
                                maximumWidth, maximumDepth);

                        var child = CreateNode(parent.State, action, parent);
                        if (child != null)
                        {
                            expanded.Add(child);
                            bestComplete = Best(bestComplete, new[] { child });
                            maximumWidth = Math.Max(maximumWidth,
                                Math.Min(BeamWidth, expanded.Count));
                            maximumDepth = Math.Max(maximumDepth, depth);
                        }

                        if (BudgetExpired(started, budget))
                            return TimedOut(rankedRootKey, bestComplete, expandedRoots,
                                maximumWidth, maximumDepth);
                    }
                }

                expanded = expanded.OrderBy(node => node.PathKey, StringComparer.Ordinal)
                    .Take(BeamWidth).ToList();
                maximumWidth = Math.Max(maximumWidth, expanded.Count);
                if (expanded.Count > 0) maximumDepth = depth;
                bestComplete = Best(bestComplete, expanded);
                beam = expanded.Where(node => node.CanContinue).ToList();
            }

            if (BudgetExpired(started, budget))
                return TimedOut(rankedRootKey, bestComplete, expandedRoots,
                    maximumWidth, maximumDepth);
            return Complete(rankedRootKey, bestComplete, expandedRoots, maximumWidth,
                maximumDepth);
        }

        private PlanningNode CreateNode(GameState state, RankedAction action,
            PlanningNode parent)
        {
            if (action == null || action.Action == null) return null;
            if (action.Action.Type == ActionType.Refresh)
            {
                _shopEstimator.Estimate(state);
                return null;
            }
            if (!IsDeterministic(action.Action.Type)) return null;

            var transition = _source.Apply(state, action);
            if (transition == null || transition.State == null) return null;

            var path = parent == null
                ? new List<string>() : new List<string>(parent.Path);
            path.Add(action.ActionKey ?? "");
            return new PlanningNode(transition.State, path, transition.CanContinue);
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
            return PlanningResult.Completed(rankedRootKey, best == null ? null : best.Path,
                roots, width, depth);
        }

        private PlanningResult TimedOut(string rankedRootKey, PlanningNode best,
            int roots, int width, int depth)
        {
            return PlanningResult.TimedOut(rankedRootKey, best == null ? null : best.Path,
                roots, width, depth);
        }

        private static IEnumerable<RankedAction> StableRank(IEnumerable<RankedAction> actions)
        {
            return (actions ?? Enumerable.Empty<RankedAction>())
                .Where(action => action != null)
                .OrderByDescending(action => FiniteScore(action.Score))
                .ThenBy(action => action.ActionKey ?? "", StringComparer.Ordinal);
        }

        private static IEnumerable<RankedAction> StableTraversal(
            IEnumerable<RankedAction> actions)
        {
            return (actions ?? Enumerable.Empty<RankedAction>())
                .Where(action => action != null)
                .OrderBy(action => action.ActionKey ?? "", StringComparer.Ordinal);
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
                .OrderByDescending(candidate => candidate.Path.Count)
                .ThenBy(candidate => candidate.PathKey, StringComparer.Ordinal)
                .FirstOrDefault();
        }

        private sealed class PlanningNode
        {
            public GameState State { get; private set; }
            public IList<string> Path { get; private set; }
            public string PathKey { get; private set; }
            public bool CanContinue { get; private set; }

            public PlanningNode(GameState state, IList<string> path, bool canContinue)
            {
                State = state;
                Path = path ?? new List<string>();
                PathKey = string.Join("\u001f", Path);
                CanContinue = canContinue && state != null;
            }
        }
    }
}
