using System;
using System.Collections.Generic;
using System.Linq;

namespace BobCoach.Engine
{
    public sealed class StrategicMemorySnapshot
    {
        public int PlayerId;
        public int LastTurn;
        public string CompDir = "";
        public string GuideId = "";
        public string Objective = "Flexible";
        public HashSet<string> ObservedCoreCardIds =
            new HashSet<string>(StringComparer.Ordinal);
        public HashSet<string> ObservedPairCardIds =
            new HashSet<string>(StringComparer.Ordinal);
        public string LastBoardSignature = "";
        public string LastOpponentProfileKey = "";
        public string LastPlanHash = "";
        public string LastPlanSequence = "";

        public StrategicMemorySnapshot Copy()
        {
            return new StrategicMemorySnapshot
            {
                PlayerId = PlayerId,
                LastTurn = LastTurn,
                CompDir = CompDir ?? "",
                GuideId = GuideId ?? "",
                Objective = Objective ?? "Flexible",
                ObservedCoreCardIds = new HashSet<string>(
                    ObservedCoreCardIds ?? new HashSet<string>(),
                    StringComparer.Ordinal),
                ObservedPairCardIds = new HashSet<string>(
                    ObservedPairCardIds ?? new HashSet<string>(),
                    StringComparer.Ordinal),
                LastBoardSignature = LastBoardSignature ?? "",
                LastOpponentProfileKey = LastOpponentProfileKey ?? "",
                LastPlanHash = LastPlanHash ?? "",
                LastPlanSequence = LastPlanSequence ?? "",
            };
        }
    }

    public sealed class StrategicMemory
    {
        private const int MaxObservedCoreCardIds = 64;
        private const int MaxObservedPairCardIds = 32;
        private StrategicMemorySnapshot _current = new StrategicMemorySnapshot();
        private readonly Queue<string> _observedCoreOrder = new Queue<string>();
        private readonly Queue<string> _observedPairOrder = new Queue<string>();

        public StrategicMemorySnapshot Snapshot
        {
            get { return _current.Copy(); }
        }

        public void Reset()
        {
            _current = new StrategicMemorySnapshot();
            _observedCoreOrder.Clear();
            _observedPairOrder.Clear();
        }

        internal StrategicMemorySnapshot Observe(
            GameState state,
            MainlinePlan mainline,
            StrategyPlan strategyPlan)
        {
            if (state == null || mainline == null)
                return Snapshot;

            _current.PlayerId = state.PlayerId;
            _current.LastTurn = state.Turn;
            _current.CompDir = mainline.CompDir ?? "";
            _current.GuideId = mainline.GuideId ?? "";
            _current.Objective = strategyPlan != null
                ? strategyPlan.Objective.ToString()
                : "Flexible";
            _current.LastPlanHash = strategyPlan != null
                ? strategyPlan.PlanHash ?? ""
                : "";
            _current.LastPlanSequence = strategyPlan != null
                ? StrategyPlanText.ToCompactString(strategyPlan)
                : "";
            _current.LastBoardSignature = BuildBoardSignature(state);
            _current.LastOpponentProfileKey = BuildOpponentKey(state);

            AddObservedCores(state, mainline.CoreCardIds);
            AddObservedPairs(state);
            return Snapshot;
        }

        private void AddObservedCores(
            GameState state,
            ICollection<string> coreCardIds)
        {
            if (coreCardIds == null || coreCardIds.Count == 0) return;
            foreach (var minion in AllMinions(state))
            {
                if (minion == null || string.IsNullOrEmpty(minion.CardId))
                    continue;
                string baseId = NormalizeGolden(minion.CardId);
                if (coreCardIds.Contains(baseId)
                    || coreCardIds.Contains(minion.CardId))
                {
                    if (_current.ObservedCoreCardIds.Add(baseId))
                    {
                        _observedCoreOrder.Enqueue(baseId);
                        while (_observedCoreOrder.Count > MaxObservedCoreCardIds)
                            _current.ObservedCoreCardIds.Remove(
                                _observedCoreOrder.Dequeue());
                    }
                }
            }
        }

        private void AddObservedPairs(GameState state)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var minion in AllMinions(state))
            {
                if (minion == null || string.IsNullOrEmpty(minion.CardId))
                    continue;
                string baseId = NormalizeGolden(minion.CardId);
                int count;
                counts.TryGetValue(baseId, out count);
                counts[baseId] = count + 1;
            }
            foreach (var pair in counts)
                if (pair.Value >= 2)
                {
                    if (_current.ObservedPairCardIds.Add(pair.Key))
                    {
                        _observedPairOrder.Enqueue(pair.Key);
                        while (_observedPairOrder.Count > MaxObservedPairCardIds)
                            _current.ObservedPairCardIds.Remove(
                                _observedPairOrder.Dequeue());
                    }
                }
        }

        private static IEnumerable<MinionData> AllMinions(GameState state)
        {
            if (state.BoardMinions != null)
                foreach (var minion in state.BoardMinions)
                    yield return minion;
            if (state.HandMinions != null)
                foreach (var minion in state.HandMinions)
                    yield return minion;
        }

        private static string NormalizeGolden(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return "";
            return cardId.EndsWith("_G", StringComparison.Ordinal)
                ? cardId.Substring(0, cardId.Length - 2)
                : cardId;
        }

        private static string BuildBoardSignature(GameState state)
        {
            var ids = new List<string>();
            if (state.BoardMinions != null)
                ids.AddRange(state.BoardMinions
                    .Where(minion => minion != null)
                    .Select(minion => NormalizeGolden(minion.CardId)
                        + ":" + minion.Position));
            ids.Sort(StringComparer.Ordinal);
            return state.Turn + "|" + state.TavernTier + "|"
                + string.Join(",", ids);
        }

        private static string BuildOpponentKey(GameState state)
        {
            if (state.Opponents == null) return "none";
            var stats = state.Opponents
                .Where(opponent => opponent != null && opponent.Alive)
                .Select(opponent => opponent.Health + "|"
                    + (opponent.BoardMinions != null
                        ? opponent.BoardMinions.Count : 0))
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            return stats.Length == 0 ? "none" : string.Join(";", stats);
        }
    }
}
