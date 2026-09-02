using System;
using System.Collections.Generic;
using BobCoach.Engine;

internal static class StrategicPlanContractBehavior
{
    private static int Main()
    {
        try
        {
            PlanHashIsDeterministic();
            MemoryRetainsObservedCoresAcrossTurns();
            MemoryTracksPairsWithoutDuplication();
            MemoryCopyIsDefensive();
            MemoryResetClearsCrossGameEvidence();
            MemoryBoundsObservedEvidence();
            SearchKeepsBestBoundedSequence();
            SearchPrunesWorseBeamCandidates();
            SearchIsBoundedAndDeterministic();
            SearchHandlesNoExpansionWithoutInfiniteLoop();
            SearchRejectsNonFiniteScores();
            SearchDropsNullExpansionSteps();
            SearchReturnsEmptyWhenOnlyInvalidRoots();
            SearchUsesSequenceContext();
            SearchHandlesNullExpansionWithoutFailure();
            Console.WriteLine("PASS strategic plan contract");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL " + ex.Message);
            return 1;
        }
    }

    private static void PlanHashIsDeterministic()
    {
        var first = Plan("Economy");
        var second = Plan("Economy");
        if (StrategyPlanText.BuildPlanHash(first)
            != StrategyPlanText.BuildPlanHash(second))
            throw new InvalidOperationException("deterministic hash");
    }

    private static void MemoryRetainsObservedCoresAcrossTurns()
    {
        var memory = new StrategicMemory();
        memory.Observe(State(turn: 1), Mainline("Beast", "core-a"), Plan("Economy"));
        memory.Observe(State(turn: 2, coreId: "core-b"),
            Mainline("Beast", "core-b"), Plan("SearchCore"));
        var snapshot = memory.Snapshot;
        if (!snapshot.ObservedCoreCardIds.Contains("core-a"))
            throw new InvalidOperationException("old core");
        if (!snapshot.ObservedCoreCardIds.Contains("core-b"))
            throw new InvalidOperationException("new core");
        if (snapshot.LastTurn != 2)
            throw new InvalidOperationException("turn");
        if (snapshot.Objective != "SearchCore")
            throw new InvalidOperationException("objective");
    }

    private static void MemoryTracksPairsWithoutDuplication()
    {
        var memory = new StrategicMemory();
        memory.Observe(PairState(), Mainline("Pirate"), Plan("Flexible"));
        memory.Observe(PairState(), Mainline("Pirate"), Plan("Flexible"));
        var snapshot = memory.Snapshot;
        if (snapshot.ObservedPairCardIds.Count != 1)
            throw new InvalidOperationException("pair dedupe");
        if (!snapshot.ObservedPairCardIds.Contains("busker"))
            throw new InvalidOperationException("busker pair");
    }

    private static void MemoryCopyIsDefensive()
    {
        var memory = new StrategicMemory();
        memory.Observe(State(1), Mainline("Beast", "core-a"), Plan("Economy"));
        var snapshot = memory.Snapshot;
        snapshot.ObservedCoreCardIds.Add("external");
        if (memory.Snapshot.ObservedCoreCardIds.Contains("external"))
            throw new InvalidOperationException("copy");
    }

    private static void MemoryResetClearsCrossGameEvidence()
    {
        var memory = new StrategicMemory();
        memory.Observe(State(1), Mainline("Beast", "core-a"), Plan("Economy"));
        memory.Reset();
        var snapshot = memory.Snapshot;
        if (snapshot.ObservedCoreCardIds.Count != 0)
            throw new InvalidOperationException("reset core memory");
        if (snapshot.ObservedPairCardIds.Count != 0)
            throw new InvalidOperationException("reset pair memory");
        if (snapshot.LastTurn != 0)
            throw new InvalidOperationException("reset turn");
    }

    private static void MemoryBoundsObservedEvidence()
    {
        var memory = new StrategicMemory();
        for (int index = 0; index < 80; index++)
        {
            string coreId = "core-" + index.ToString("D3");
            memory.Observe(
                State(1, coreId), Mainline("Beast", coreId), Plan("Economy"));
        }
        if (memory.Snapshot.ObservedCoreCardIds.Count > 64)
            throw new InvalidOperationException("core memory bound");

        memory.Reset();
        for (int index = 0; index < 48; index++)
        {
            string pairId = "pair-" + index.ToString("D3");
            memory.Observe(
                PairState(pairId), Mainline("Pirate"), Plan("Flexible"));
        }
        if (memory.Snapshot.ObservedPairCardIds.Count > 32)
            throw new InvalidOperationException("pair memory bound");
    }

    private static void SearchKeepsBestBoundedSequence()
    {
        var root = Step("a", 0);
        var result = SequenceSearchPlanner.Search(
            new[] { root },
            step => new[]
            {
                Step(step.Action.CardId + "-1", 10),
                Step(step.Action.CardId + "-2", 1),
            },
            steps =>
            {
                double sum = 0;
                foreach (var step in steps) sum += step.Value;
                return sum;
            },
            maxDepth: 3,
            beamWidth: 2);
        if (result.Count != 3)
            throw new InvalidOperationException("sequence length");
        if (result[1].Action.CardId != "a-1")
            throw new InvalidOperationException("best branch");
    }

    private static void SearchPrunesWorseBeamCandidates()
    {
        var result = SequenceSearchPlanner.Search(
            new[] { Step("low", 1), Step("high", 9) },
            step => new PlannedStep[0],
            steps =>
            {
                double sum = 0;
                foreach (var step in steps) sum += step.Value;
                return sum;
            },
            maxDepth: 2,
            beamWidth: 1);
        if (result.Count != 1)
            throw new InvalidOperationException("beam width");
        if (result[0].Action.CardId != "high")
            throw new InvalidOperationException("best root");
    }

    private static void SearchIsBoundedAndDeterministic()
    {
        string expected = "";
        for (int index = 0; index < 20; index++)
        {
            var result = SequenceSearchPlanner.Search(
                new[] { Step("a", 0), Step("b", 1) },
                step => new[] { Step(step.Action.CardId + "-x", 2) },
                steps =>
                {
                    double sum = 0;
                    foreach (var step in steps) sum += step.Value;
                    return sum;
                },
                maxDepth: 3,
                beamWidth: 2);
            string key = "";
            foreach (var step in result) key += step.Action.CardId + ";";
            if (index == 0) expected = key;
            else if (key != expected)
                throw new InvalidOperationException("deterministic");
            if (result.Count > 3)
                throw new InvalidOperationException("depth bound");
        }
    }

    private static void SearchHandlesNoExpansionWithoutInfiniteLoop()
    {
        var result = SequenceSearchPlanner.Search(
            new[] { Step("only", 1) },
            step => new PlannedStep[0],
            steps => 1,
            maxDepth: 8,
            beamWidth: 4);
        if (result.Count != 1)
            throw new InvalidOperationException("no expansion");
    }

    private static void SearchRejectsNonFiniteScores()
    {
        var result = SequenceSearchPlanner.Search(
            new[] { Step("good", 1), Step("bad", 2) },
            step => new PlannedStep[0],
            steps =>
            {
                foreach (var step in steps)
                    if (step.Action.CardId == "bad") return double.NaN;
                double sum = 0;
                foreach (var step in steps) sum += step.Value;
                return sum;
            },
            maxDepth: 1,
            beamWidth: 1);
        if (result.Count != 1)
            throw new InvalidOperationException("non-finite score bound");
        if (result[0].Action.CardId != "good")
            throw new InvalidOperationException("non-finite score ranking");
    }

    private static void SearchDropsNullExpansionSteps()
    {
        var result = SequenceSearchPlanner.Search(
            new[] { Step("root", 0) },
            step => new PlannedStep[] { null },
            steps =>
            {
                double sum = 0;
                foreach (var step in steps) sum += step.Value;
                return sum;
            },
            maxDepth: 2,
            beamWidth: 1);
        if (result.Count != 1)
            throw new InvalidOperationException("null expansion step");
    }

    private static void SearchReturnsEmptyWhenOnlyInvalidRoots()
    {
        var result = SequenceSearchPlanner.Search(
            new PlannedStep[] { null, new PlannedStep() },
            step => new PlannedStep[0],
            steps => 1,
            maxDepth: 2,
            beamWidth: 1);
        if (result.Count != 0)
            throw new InvalidOperationException("invalid roots");
    }

    private static void SearchUsesSequenceContext()
    {
        var result = SequenceSearchPlanner.Search(
            new[] { Step("a", 0) },
            (sequence, last) =>
            {
                if (sequence.Count == 1 && last.Action.CardId == "a")
                    return new[] { Step("a-x", 1) };
                if (sequence.Count == 2 && last.Action.CardId == "a-x")
                    return new[] { Step("a-y", 1) };
                return new PlannedStep[0];
            },
            steps =>
            {
                double sum = 0;
                foreach (var step in steps) sum += step.Value;
                return sum;
            },
            maxDepth: 3,
            beamWidth: 1);
        if (result.Count != 3)
            throw new InvalidOperationException("sequence context length");
        if (result[2].Action.CardId != "a-y")
            throw new InvalidOperationException("sequence context branch");
    }

    private static void SearchHandlesNullExpansionWithoutFailure()
    {
        var result = SequenceSearchPlanner.Search(
            new[] { Step("only", 1) },
            (Func<PlannedStep, IReadOnlyList<PlannedStep>>)null,
            steps => 1,
            maxDepth: 4,
            beamWidth: 2);
        if (result.Count != 1)
            throw new InvalidOperationException("null expansion");
    }

    private static PlannedStep Step(string cardId, double value)
    {
        return new PlannedStep
        {
            Action = new GameAction
            {
                Type = ActionType.BuyMinion,
                CardId = cardId,
                TargetIndex = 0,
            },
            Value = value,
            Reason = "test",
        };
    }

    private static StrategyPlan Plan(string objective)
    {
        return new StrategyPlan
        {
            Objective = (StrategyObjective)Enum.Parse(
                typeof(StrategyObjective), objective),
            CompDir = "Pirate",
            Steps = new List<PlannedStep>
            {
                new PlannedStep
                {
                    Action = new GameAction
                    {
                        Type = ActionType.BuyMinion,
                        TargetIndex = 0,
                        CardId = "busker",
                    },
                    Reason = "economy",
                },
            },
        };
    }

    private static GameState State(int turn, string coreId = "core-a")
    {
        return new GameState
        {
            PlayerId = 1,
            Turn = turn,
            BoardMinions = new List<MinionData>
            {
                new MinionData { CardId = coreId, Tier = 1 },
            },
        };
    }

    private static GameState PairState(string cardId = "busker")
    {
        return new GameState
        {
            PlayerId = 1,
            Turn = 1,
            BoardMinions = new List<MinionData>
            {
                new MinionData { CardId = cardId, Tier = 1 },
                new MinionData { CardId = cardId, Tier = 1 },
            },
        };
    }

    private static MainlinePlan Mainline(
        string compDir, string coreCardId = null)
    {
        var plan = new MainlinePlan { CompDir = compDir };
        if (!string.IsNullOrEmpty(coreCardId))
            plan.CoreCardIds.Add(coreCardId);
        return plan;
    }
}

namespace BobCoach.Engine
{
    public enum ActionType
    {
        BuyMinion,
    }

    public sealed class GameAction
    {
        public ActionType Type;
        public int TargetIndex;
        public string CardId = "";
    }

    public sealed class MinionData
    {
        public string CardId = "";
        public string CardName = "";
        public int Tier;
        public int Position;
    }

    public sealed class OpponentData
    {
        public bool Alive = true;
        public int Health;
        public List<MinionData> BoardMinions = new List<MinionData>();
    }

    public sealed class MainlinePlan
    {
        public string CompDir = "";
        public string GuideId = "";
        public List<string> CoreCardIds = new List<string>();
    }

    public sealed class GameState
    {
        public int PlayerId;
        public int Turn;
        public int TavernTier;
        public List<MinionData> BoardMinions = new List<MinionData>();
        public List<MinionData> HandMinions = new List<MinionData>();
        public List<OpponentData> Opponents = new List<OpponentData>();
    }
}
