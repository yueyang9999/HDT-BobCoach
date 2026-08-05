using System;
using System.Collections.Generic;
using System.Linq;
using BobCoach.Engine;
using BobCoach.Engine.Decision;

internal static class AlgorithmObservedActionInferenceBehavior
{
    private static int Main()
    {
        try
        {
            EnumeratesTrinketOffersAsPickActions();
            DoesNotEnumerateTrinketsWithoutAnOffer();
            IgnoresTurnStartShopReplacement();
            InfersPaidRefreshWithinTheSameTurn();
            InfersFreeRefreshWithinTheSameTurn();
            InfersImmediatelyConsumedBuySpellFromGeneratedCandidates();
            RejectsBuySpellWithoutMatchingGoldSpend();
            RejectsBuySpellAcrossTurns();
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

    private static void EnumeratesTrinketOffersAsPickActions()
    {
        var state = State(4, 5, 0);
        state.TrinketOffer.Add(new TrinketOption
        {
            CardId = "TRINKET_A",
            TrinketName = "Trinket A",
            EntityId = 101,
        });
        state.TrinketOffer.Add(new TrinketOption
        {
            CardId = "TRINKET_B",
            TrinketName = "Trinket B",
            EntityId = 102,
        });

        var actions = new ActionEnumerator().Enumerate(state);
        var picks = actions.Where(action => action.Type == ActionType.PickTrinket).ToList();

        AssertTrue(picks.Count == 2,
            "trinket offer enumerates two PickTrinket actions");
        AssertTrue(picks[0].TargetIndex == 0 && picks[0].CardId == "TRINKET_A",
            "first PickTrinket target maps to first trinket offer");
        AssertTrue(picks[1].TargetIndex == 1 && picks[1].CardId == "TRINKET_B",
            "second PickTrinket target maps to second trinket offer");
    }

    private static void DoesNotEnumerateTrinketsWithoutAnOffer()
    {
        var state = State(4, 5, 0);
        var actions = new ActionEnumerator().Enumerate(state);

        AssertTrue(!actions.Any(action => action.Type == ActionType.PickTrinket),
            "empty trinket offer fails closed");
    }

    private static void IgnoresTurnStartShopReplacement()
    {
        var previous = State(4, 5, 0, 11, 12);
        var current = State(5, 6, 0, 21, 22);

        AssertActions(new string[0], Infer(previous, current, Refresh(previous)),
            "turn-start automatic shop replacement");
    }

    private static void InfersPaidRefreshWithinTheSameTurn()
    {
        var previous = State(4, 5, 0, 11, 12);
        var current = State(4, 4, 0, 21, 22);

        AssertActions(new[] { "Refresh|-1||" }, Infer(previous, current, Refresh(previous)),
            "paid refresh");
    }

    private static void InfersFreeRefreshWithinTheSameTurn()
    {
        var previous = State(4, 5, 1, 11, 12);
        var current = State(4, 5, 0, 21, 22);

        AssertActions(new[] { "Refresh|-1||" }, Infer(previous, current, Refresh(previous)),
            "free refresh");
    }

    private static void InfersImmediatelyConsumedBuySpellFromGeneratedCandidates()
    {
        var previous = State(4, 1, 0, 11, 12);
        previous.ShopMinions[0].CardId = "SPELL_A";
        previous.ShopMinions[0].CardName = "Spell A";
        previous.ShopMinions[0].IsSpell = true;
        previous.ShopMinions[0].Cost = 1;
        var current = State(4, 0, 0, 12);
        var candidates = new CandidateGenerator(new ActionEnumerator()).Generate(previous);

        AssertTrue(candidates.Any(candidate => candidate.Action.Type == ActionType.BuySpell),
            "generated candidates include BuySpell");
        AssertTrue(candidates.Any(candidate => candidate.Action.Type == ActionType.Refresh),
            "generated candidates include Refresh");
        AssertActions(new[] { "BuySpell|0|SPELL_A|tavern_shop" },
            Infer(previous, current, candidates.ToArray()),
            "immediately consumed tavern spell from generated candidates");
    }

    private static void RejectsBuySpellWithoutMatchingGoldSpend()
    {
        var previous = State(4, 5, 0, 11, 12);
        previous.ShopMinions[0].IsSpell = true;
        previous.ShopMinions[0].Cost = 2;
        previous.ShopMinions[0].CardId = "SPELL_A";
        var current = State(4, 4, 0, 12);

        AssertActions(new string[0], Infer(previous, current,
            Candidate(previous, ActionType.BuySpell, 0, "SPELL_A", "tavern_shop")),
            "spell purchase gold mismatch");
    }

    private static void RejectsBuySpellAcrossTurns()
    {
        var previous = State(4, 5, 0, 11, 12);
        previous.ShopMinions[0].IsSpell = true;
        previous.ShopMinions[0].Cost = 2;
        previous.ShopMinions[0].CardId = "SPELL_A";
        var current = State(5, 3, 0, 12);

        AssertActions(new string[0], Infer(previous, current,
            Candidate(previous, ActionType.BuySpell, 0, "SPELL_A", "tavern_shop")),
            "spell purchase across turns");
    }

    private static void MarksMultipleMatchingCandidatesUnresolved()
    {
        var previous = State(4, 5, 0, 11);
        previous.FrozenShop = false;
        var current = State(4, 5, 0);
        current.FrozenShop = true;
        current.HandMinions.Add(new MinionData { EntityId = 11 });
        var candidates = new[]
        {
            Candidate(previous, ActionType.BuyMinion, 0, "CARD_A", "tavern_shop"),
            Candidate(previous, ActionType.FreezeShop, -1, null, ""),
        };

        AssertActions(new[] { "observation|unresolved|unknown" },
            Infer(previous, current, candidates), "multiple matching candidates");
    }

    private static void IgnoresStateChangesWithoutMatchingCandidates()
    {
        var previous = State(4, 5, 0, 11);
        var current = State(4, 3, 0, 11);

        AssertActions(new string[0], Infer(previous, current, Refresh(previous)),
            "unmatched state change");
    }

    private static GameState State(int turn, int gold, int freeRefreshes,
        params int[] shopIds)
    {
        var state = new GameState
        {
            GameActive = true,
            Turn = turn,
            Gold = gold,
            TavernTier = 3,
            FreeRefreshCount = freeRefreshes,
        };
        foreach (var id in shopIds)
            state.ShopMinions.Add(new MinionData
            {
                EntityId = id,
                CardId = "CARD_" + id,
                CardName = "Card " + id,
                Tier = 1,
                Cost = 3,
            });
        return state;
    }

    private static ActionDescriptor Refresh(GameState state)
    {
        return Candidate(state, ActionType.Refresh, -1, null, "");
    }

    private static ActionDescriptor Candidate(GameState state, ActionType type,
        int targetIndex, string cardId, string purchaseSource)
    {
        return ActionDescriptor.From(state, new GameAction
        {
            Type = type,
            TargetIndex = targetIndex,
            CardId = cardId,
            PurchaseSource = purchaseSource,
        });
    }

    private static IList<string> Infer(GameState previous, GameState current,
        params ActionDescriptor[] candidates)
    {
        return ObservedActionInference.Infer(previous, current, candidates);
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

    private static void AssertTrue(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
    }
}
