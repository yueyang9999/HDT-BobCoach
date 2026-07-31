using System;
using System.Collections.Generic;
using System.Linq;

namespace BobCoach.Engine
{
    public enum ActionType
    {
        BuyMinion,
        BuySpell,
        SellMinion,
        Refresh,
        Upgrade,
        FreezeShop,
        UseHeroPower,
    }

    public sealed class GameAction
    {
        public ActionType Type;
    }

    public sealed class MinionData
    {
        public int EntityId;
    }

    public sealed class GameState
    {
        public int Turn;
        public int Gold;
        public int TavernTier;
        public bool FrozenShop;
        public bool HeroPowerExhausted;
        public int FreeRefreshCount;
        public List<MinionData> ShopMinions = new List<MinionData>();
        public List<MinionData> BoardMinions = new List<MinionData>();
        public List<MinionData> HandMinions = new List<MinionData>();
    }
}

namespace BobCoach.Engine.Decision
{
    public sealed class ActionDescriptor
    {
        public GameAction Action;
        public string ActionKey;
        public int EntityId;
        public int GoldCost;
    }
}

internal static class AlgorithmObservedActionInferenceBehavior
{
    private static int Main()
    {
        try
        {
            IgnoresTurnStartShopReplacement();
            InfersPaidRefreshWithinTheSameTurn();
            InfersFreeRefreshWithinTheSameTurn();
            MarksMultipleMatchingCandidatesUnresolved();
            IgnoresStateChangesWithoutMatchingCandidates();
            Console.WriteLine("PASS observed actions require unique player-action evidence");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL " + ex.Message);
            return 1;
        }
    }

    private static void IgnoresTurnStartShopReplacement()
    {
        var previous = State(4, 5, 0, 11, 12);
        var current = State(5, 6, 0, 21, 22);

        AssertActions(new string[0], Infer(previous, current, Refresh(1)),
            "turn-start automatic shop replacement");
    }

    private static void InfersPaidRefreshWithinTheSameTurn()
    {
        var previous = State(4, 5, 0, 11, 12);
        var current = State(4, 4, 0, 21, 22);

        AssertActions(new[] { "Refresh|-1||" }, Infer(previous, current, Refresh(1)),
            "paid refresh");
    }

    private static void InfersFreeRefreshWithinTheSameTurn()
    {
        var previous = State(4, 5, 1, 11, 12);
        var current = State(4, 5, 0, 21, 22);

        AssertActions(new[] { "Refresh|-1||" }, Infer(previous, current, Refresh(0)),
            "free refresh");
    }

    private static void MarksMultipleMatchingCandidatesUnresolved()
    {
        var previous = State(4, 5, 0, 11);
        previous.FrozenShop = false;
        var current = State(4, 5, 0);
        current.FrozenShop = true;
        current.HandMinions.Add(new BobCoach.Engine.MinionData { EntityId = 11 });
        var candidates = new[]
        {
            Candidate(BobCoach.Engine.ActionType.BuyMinion, "BuyMinion|0|CARD_A|shop", 11, 3),
            Candidate(BobCoach.Engine.ActionType.FreezeShop, "FreezeShop|-1||", 0, 0),
        };

        AssertActions(new[] { "observation|unresolved|unknown" },
            Infer(previous, current, candidates), "multiple matching candidates");
    }

    private static void IgnoresStateChangesWithoutMatchingCandidates()
    {
        var previous = State(4, 5, 0, 11);
        var current = State(4, 3, 0, 11);

        AssertActions(new string[0], Infer(previous, current, Refresh(1)),
            "unmatched state change");
    }

    private static BobCoach.Engine.GameState State(int turn, int gold, int freeRefreshes,
        params int[] shopIds)
    {
        var state = new BobCoach.Engine.GameState
        {
            Turn = turn,
            Gold = gold,
            TavernTier = 3,
            FreeRefreshCount = freeRefreshes,
        };
        foreach (var id in shopIds)
            state.ShopMinions.Add(new BobCoach.Engine.MinionData { EntityId = id });
        return state;
    }

    private static BobCoach.Engine.Decision.ActionDescriptor Refresh(int goldCost)
    {
        return Candidate(BobCoach.Engine.ActionType.Refresh, "Refresh|-1||", 0, goldCost);
    }

    private static BobCoach.Engine.Decision.ActionDescriptor Candidate(
        BobCoach.Engine.ActionType type, string key, int entityId, int goldCost)
    {
        return new BobCoach.Engine.Decision.ActionDescriptor
        {
            Action = new BobCoach.Engine.GameAction { Type = type },
            ActionKey = key,
            EntityId = entityId,
            GoldCost = goldCost,
        };
    }

    private static IList<string> Infer(BobCoach.Engine.GameState previous,
        BobCoach.Engine.GameState current,
        params BobCoach.Engine.Decision.ActionDescriptor[] candidates)
    {
        return BobCoach.Engine.Decision.ObservedActionInference.Infer(
            previous, current, candidates);
    }

    private static void AssertActions(IEnumerable<string> expected, IEnumerable<string> actual,
        string label)
    {
        var expectedText = string.Join(",", expected);
        var actualText = string.Join(",", actual ?? Enumerable.Empty<string>());
        if (!string.Equals(expectedText, actualText, StringComparison.Ordinal))
            throw new InvalidOperationException(label + " expected=" + expectedText
                + " actual=" + actualText);
    }
}
