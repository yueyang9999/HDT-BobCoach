using System;
using System.Collections.Generic;
using BobCoach.Engine;

internal static class T35HeroPlaybookLiveBehavior
{
    private static int _assertions;

    private static int Main()
    {
        try
        {
            CenariusEarlyEconomyRanksAsPrimaryAction();
            CenariusDoesNotOverrideAffordableUpgrade();
            Console.WriteLine("PASS T35 hero playbook live behavior assertions=" + _assertions);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL " + ex.Message);
            return 1;
        }
    }

    private static void CenariusEarlyEconomyRanksAsPrimaryAction()
    {
        var state = CenariusState(1, 3, 4);

        var engine = new DecisionEngine();
        var ranked = engine.GetRankedCandidates(state, 12);
        AssertTrue(ranked.Count > 0, "Cenarius has ranked candidates");
        AssertEqual(
            ActionType.UseHeroPower,
            ranked[0].Action.Type,
            "Cenarius early economy power is the primary ranked action");
        AssertEqual(
            "BG32_HERO_001p",
            ranked[0].Action.CardId,
            "primary ranked action uses the Cenarius hero power");
        var visible = engine.GetBestAction(state);
        AssertEqual(
            ActionType.UseHeroPower,
            visible.Type,
            "visible legacy action also uses Cenarius early economy power");
        AssertEqual(
            "BG32_HERO_001p",
            visible.CardId,
            "visible legacy action uses the Cenarius hero power");
    }

    private static void CenariusDoesNotOverrideAffordableUpgrade()
    {
        var state = CenariusState(2, 10, 10);
        state.BoardMinions.Add(BoardCard("BG36_345", 3, 3, 1, 0));

        var engine = new DecisionEngine();
        var visible = engine.GetBestAction(state);
        AssertEqual(
            ActionType.Upgrade,
            visible.Type,
            "Cenarius early economy must not override an affordable tier-2 upgrade");
    }

    private static GameState CenariusState(int turn, int gold, int maxGold)
    {
        return new GameState
        {
            GameActive = true,
            NormalRecruitAvailable = true,
            Phase = "shop",
            Turn = turn,
            TavernTier = 1,
            Gold = gold,
            LiveGold = gold,
            MaxGold = maxGold,
            Health = 30,
            MaxHealth = 30,
            HeroCardId = "BG32_HERO_001",
            HeroName = "Forest Lord Cenarius",
            HeroPowerCardId = "BG32_HERO_001p",
            HeroPowerType = "Active",
            HeroPowerCost = 3,
            HeroPowerExhausted = false,
            HeroPowerUnlockTurn = 1,
            HeroPowerUnlockTier = 1,
            EffectiveRules = EffectiveGameRules.Default,
            BoardMinions = new List<MinionData>(),
            HandMinions = new List<MinionData>(),
            ShopMinions = new List<MinionData>
            {
                ShopCard("BG36_345", 3, 3, 1, 0),
                ShopCard("BG36_345", 3, 3, 1, 1),
                ShopCard("BG31_330", 2, 1, 1, 2),
            },
            TrinketOffer = new List<TrinketOption>(),
            DiscoverOptions = new List<TrinketOption>(),
            HeroPowers = new List<HeroPowerState>
            {
                new HeroPowerState
                {
                    CardId = "BG32_HERO_001p",
                    BaseHeroCardId = "BG32_HERO_001",
                    Cost = 3,
                    IsActive = true,
                    IsUnlocked = true,
                    IsPrimary = true,
                    Exhausted = false,
                    UnlockTurn = 1,
                    UnlockTier = 1,
                },
            },
        };
    }

    private static MinionData ShopCard(
        string cardId,
        int attack,
        int health,
        int tier,
        int position)
    {
        return new MinionData
        {
            CardId = cardId,
            Attack = attack,
            Health = health,
            Tier = tier,
            Position = position,
            Cost = 3,
        };
    }

    private static MinionData BoardCard(
        string cardId,
        int attack,
        int health,
        int tier,
        int position)
    {
        return new MinionData
        {
            CardId = cardId,
            Attack = attack,
            Health = health,
            Tier = tier,
            Position = position,
            Cost = 3,
        };
    }

    private static void AssertTrue(bool condition, string label)
    {
        _assertions++;
        if (!condition)
            throw new InvalidOperationException(label);
    }

    private static void AssertEqual<T>(
        T expected,
        T actual,
        string label)
    {
        _assertions++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(
                label + " expected=" + expected + " actual=" + actual);
    }
}
