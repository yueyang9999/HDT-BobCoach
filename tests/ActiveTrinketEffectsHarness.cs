using System;
using System.Collections.Generic;
using System.Linq;
using BobCoach.Engine;

internal static class ActiveTrinketEffectsHarness
{
    // These are fixed live CardIds. Test minions below are synthetic states, not synthetic trinkets.
    private const string DesignerEyepatchId = "BG30_MagicItem_439";
    private const string CowrieNecklaceId = "BG35_MagicItem_921";
    private const string IronforgeAnvilId = "BG30_MagicItem_403";
    private const string SlammaStickerId = "BG30_MagicItem_540";

    private static int Main()
    {
        var registry = new TrinketEffectRegistry();

        if (!string.Equals(
            TrinketEffectRegistry.RuleSetVersion,
            "hdt-1.53.5-hearthdb-2026-07-21",
            StringComparison.Ordinal))
            return Fail("equipped-trinket rules must expose their audited local ruleset version");
        if (TestExactResolutionAndDiagnostics(registry) != 0) return 1;
        if (TestExactStatSpellClassification() != 0) return 1;
        if (TestUnknownDiagnosticDeduplication() != 0) return 1;
        if (TestGoldenRequirement(registry) != 0) return 1;
        if (TestTavernSpellCost(registry) != 0) return 1;
        if (TestSynergyScoring(registry) != 0) return 1;
        if (TestCombatEffectsAndContextIsolation(registry) != 0) return 1;
        if (TestCombatSimulatorContextWiring(registry) != 0) return 1;
        if (TestEyepatchPurchaseGoldenResolution(registry) != 0) return 1;
        if (TestSimulationCopyAndOfferIsolation(registry) != 0) return 1;

        Console.WriteLine("PASS active trinket effects are local, exact, and simulation-safe");
        return 0;
    }

    private static int TestExactStatSpellClassification()
    {
        if (!TrinketEffectRegistry.IsStatGrantingTavernSpell("BG28_168")
            || TrinketEffectRegistry.IsStatGrantingTavernSpell("BG28_500")
            || TrinketEffectRegistry.IsStatGrantingTavernSpell("TEST_UNKNOWN_STAT_SPELL"))
            return Fail("stat-granting Tavern spells must use an exact fail-closed CardId registry");

        return 0;
    }

    private static int TestUnknownDiagnosticDeduplication()
    {
        var tracker = new UnknownTrinketDiagnosticTracker();
        if (!tracker.ShouldReport("TEST_UNKNOWN_TRINKET")
            || tracker.ShouldReport("TEST_UNKNOWN_TRINKET")
            || tracker.ShouldReport("")
            || !tracker.ShouldReport("TEST_UNKNOWN_TRINKET_2"))
            return Fail("unknown equipped trinkets must be reported once per exact CardId per game");

        tracker.Reset();
        if (!tracker.ShouldReport("TEST_UNKNOWN_TRINKET"))
            return Fail("unknown equipped trinket diagnostics must reset for the next game");

        return 0;
    }

    private static int TestExactResolutionAndDiagnostics(TrinketEffectRegistry registry)
    {
        ActiveTrinketContext context = registry.Resolve(new[]
        {
            DesignerEyepatchId,
            DesignerEyepatchId.ToLowerInvariant(),
            "UNKNOWN_ACTIVE_TRINKET",
        });

        if (context.ResolvedCardIds.Count != 1
            || context.ResolvedCardIds[0] != DesignerEyepatchId
            || context.UnknownCardIds.Count != 2
            || !context.UnknownCardIds.Contains(DesignerEyepatchId.ToLowerInvariant())
            || !context.UnknownCardIds.Contains("UNKNOWN_ACTIVE_TRINKET"))
            return Fail("known effects must resolve by exact CardId and unknown IDs must be diagnostics only");

        return 0;
    }

    private static int TestGoldenRequirement(TrinketEffectRegistry registry)
    {
        var context = registry.Resolve(new[] { DesignerEyepatchId });
        var pirate = new MinionData { CardId = "TEST_PIRATE", Tribe = "Pirate", Tier = 3 };
        var nonPirate = new MinionData { CardId = "TEST_BEAST", Tribe = "Beast", Tier = 3 };

        if (context.GetGoldenCopyRequirement(pirate, 3) != 2
            || context.GetGoldenCopyRequirement(nonPirate, 3) != 3)
            return Fail("pirate-only golden-copy requirement changed another tribe or missed pirates");

        return 0;
    }

    private static int TestTavernSpellCost(TrinketEffectRegistry registry)
    {
        var context = registry.Resolve(new[] { CowrieNecklaceId });
        var state = new GameState { GameActive = true, ActiveTrinketContext = context };
        state.EffectiveRules = context.ApplyTo(EffectiveGameRules.Default);

        var statSpell = new MinionData
        {
            CardId = "TEST_STAT_SPELL", IsSpell = true, GrantsStats = true, Cost = 5,
        };
        var ordinarySpell = new MinionData
        {
            CardId = "TEST_ORDINARY_SPELL", IsSpell = true, GrantsStats = false, Cost = 5,
        };
        var minion = new MinionData { CardId = "TEST_MINION", Tier = 2, Cost = 5 };
        int statSpellCost = GameRuleEvaluator.GetPurchaseCost(state, statSpell, "", state.EffectiveRules);
        int ordinarySpellCost = GameRuleEvaluator.GetPurchaseCost(
            state, ordinarySpell, "", state.EffectiveRules);
        int minionCost = GameRuleEvaluator.GetPurchaseCost(state, minion, "", state.EffectiveRules);

        if (statSpellCost != 3 || ordinarySpellCost != 5 || minionCost != 3)
            return Fail("Cowrie must discount only Tavern spells that grant stats by exactly 2");

        return 0;
    }

    private static int TestSynergyScoring(TrinketEffectRegistry registry)
    {
        var pirate = new MinionData { CardId = "TEST_PIRATE", Tribe = "Pirate" };
        var beast = new MinionData { CardId = "TEST_BEAST", Tribe = "Beast" };
        var statSpell = new MinionData { CardId = "TEST_STAT_SPELL", IsSpell = true, GrantsStats = true };
        var ordinarySpell = new MinionData { CardId = "TEST_ORDINARY_SPELL", IsSpell = true };
        var typeless = new MinionData { CardId = "TEST_TYPELESS" };

        if (!HasPositiveTargetOnlyScore(registry.Resolve(new[] { DesignerEyepatchId }), pirate, beast)
            || !HasPositiveTargetOnlyScore(registry.Resolve(new[] { CowrieNecklaceId }), statSpell, ordinarySpell)
            || !HasPositiveTargetOnlyScore(registry.Resolve(new[] { IronforgeAnvilId }), typeless, beast)
            || !HasPositiveTargetOnlyScore(registry.Resolve(new[] { SlammaStickerId }), beast, pirate))
            return Fail("each equipped trinket must give positive synergy only to its own target card and board");

        return 0;
    }

    private static bool HasPositiveTargetOnlyScore(
        ActiveTrinketContext context, MinionData target, MinionData nonTarget)
    {
        return context.GetCardSynergyScore(target) > 0
            && context.GetCardSynergyScore(nonTarget) == 0
            && context.GetBoardSynergyScore(new List<MinionData> { target }) > 0
            && context.GetBoardSynergyScore(new List<MinionData> { nonTarget }) == 0;
    }

    private static int TestCombatEffectsAndContextIsolation(TrinketEffectRegistry registry)
    {
        var ownerContext = registry.Resolve(new[] { IronforgeAnvilId, SlammaStickerId });
        var opponentContext = registry.Resolve(new[] { DesignerEyepatchId });
        var ownerBoard = new List<CombatUnit>
        {
            new CombatUnit
            {
                CardId = "TEST_TYPELESS", Attack = 2, Health = 3, MaxHealth = 3, Alive = true,
                MinionTypes = new List<string>(),
            },
            new CombatUnit
            {
                CardId = "TEST_BEAST_ON_BOARD", Attack = 2, Health = 3, MaxHealth = 3, Alive = true,
                MinionTypes = new List<string> { "Beast" },
            },
        };
        var opponentBoard = new List<CombatUnit>
        {
            new CombatUnit
            {
                CardId = "TEST_OPPONENT_TYPELESS", Attack = 2, Health = 3, MaxHealth = 3, Alive = true,
                MinionTypes = new List<string>(),
            },
        };
        var summonedBeast = new CombatUnit
        {
            CardId = "TEST_SUMMONED_BEAST", Attack = 4, Health = 2, MaxHealth = 2, Alive = true,
            MinionTypes = new List<string> { "Beast" },
        };
        var summonedNonBeast = new CombatUnit
        {
            CardId = "TEST_SUMMONED_NON_BEAST", Attack = 4, Health = 2, MaxHealth = 2, Alive = true,
            MinionTypes = new List<string> { "Pirate" },
        };

        ownerContext.ApplyStartOfCombat(ownerBoard);
        opponentContext.ApplyStartOfCombat(opponentBoard);
        ownerContext.ApplySummon(summonedBeast);
        ownerContext.ApplySummon(summonedNonBeast);

        if (ownerBoard[0].Attack != 6 || ownerBoard[0].Health != 9 || ownerBoard[0].MaxHealth != 9
            || ownerBoard[1].Attack != 2 || ownerBoard[1].Health != 3 || ownerBoard[1].MaxHealth != 3
            || opponentBoard[0].Attack != 2 || opponentBoard[0].Health != 3 || opponentBoard[0].MaxHealth != 3
            || summonedBeast.Attack != 8 || summonedBeast.Health != 2 || summonedBeast.MaxHealth != 2
            || summonedNonBeast.Attack != 4 || summonedNonBeast.Health != 2 || summonedNonBeast.MaxHealth != 2)
            return Fail("Anvil and Slamma must affect only their owner's typeless board and summoned Beasts");

        return 0;
    }

    private static int TestCombatSimulatorContextWiring(TrinketEffectRegistry registry)
    {
        var anvil = registry.Resolve(new[] { IronforgeAnvilId });
        var noEffects = registry.Resolve(new string[0]);
        var attackerTypeless = new List<MinionData>
        {
            new MinionData { CardId = "TEST_ATTACKER_TYPELESS", Attack = 2, Health = 3, Tier = 1 },
        };
        var defenderTypeless = new List<MinionData>
        {
            new MinionData { CardId = "TEST_DEFENDER_TYPELESS", Attack = 2, Health = 3, Tier = 1 },
        };
        var unbuffedEnemy = new List<MinionData>
        {
            new MinionData { CardId = "TEST_UNBUFFED_ENEMY", Attack = 5, Health = 5, Tier = 1, Tribe = "Beast" },
        };
        var simulator = new CombatSimulator();

        var attackerResult = simulator.Simulate(attackerTypeless, unbuffedEnemy, anvil, noEffects);
        var defenderResult = simulator.Simulate(unbuffedEnemy, defenderTypeless, noEffects, anvil);

        if (!attackerResult.PlayerWon || defenderResult.PlayerWon)
            return Fail("CombatSimulator must route Anvil only to the matching attacker or defender context");

        return 0;
    }

    private static int TestEyepatchPurchaseGoldenResolution(TrinketEffectRegistry registry)
    {
        var simulator = new Simulator();
        var pirateState = CreatePurchaseState(registry, "Pirate");
        var pirateNext = simulator.Simulate(pirateState,
            new GameAction { Type = ActionType.BuyMinion, TargetIndex = 0 });
        if (pirateNext.BoardMinions.Count != 0 || pirateNext.HandMinions.Count != 1
            || !pirateNext.HandMinions[0].Golden)
            return Fail("Eyepatch pirate purchase must combine two normal copies into one golden");

        var beastState = CreatePurchaseState(registry, "Beast");
        var beastNext = simulator.Simulate(beastState,
            new GameAction { Type = ActionType.BuyMinion, TargetIndex = 0 });
        if (beastNext.BoardMinions.Count != 2 || beastNext.HandMinions.Count != 0
            || beastNext.BoardMinions.Any(card => card.Golden))
            return Fail("Eyepatch must not make non-Pirate purchases golden at two copies");

        return 0;
    }

    private static GameState CreatePurchaseState(TrinketEffectRegistry registry, string tribe)
    {
        var context = registry.Resolve(new[] { DesignerEyepatchId });
        var owned = new MinionData
        {
            CardId = "TEST_" + tribe.ToUpperInvariant(), Tribe = tribe, Tier = 3,
            Attack = 3, Health = 3,
        };
        return new GameState
        {
            GameActive = true,
            Gold = 3,
            ActiveTrinketContext = context,
            EffectiveRules = context.ApplyTo(EffectiveGameRules.Default),
            BoardMinions = new List<MinionData> { owned },
            ShopMinions = new List<MinionData>
            {
                new MinionData
                {
                    CardId = owned.CardId, Tribe = tribe, Tier = 3, Attack = 3, Health = 3,
                },
            },
        };
    }

    private static int TestSimulationCopyAndOfferIsolation(TrinketEffectRegistry registry)
    {
        var context = registry.Resolve(new[] { DesignerEyepatchId });
        var state = new GameState
        {
            GameActive = true,
            ActiveTrinkets = new List<string> { DesignerEyepatchId },
            ActiveTrinketContext = context,
            TrinketOffer = new List<TrinketOption>
            {
                new TrinketOption { CardId = CowrieNecklaceId },
            },
        };

        var next = new Simulator().Simulate(state, new GameAction { Type = ActionType.FreezeShop });
        next.ActiveTrinkets.Add("MUTATED_IN_SIMULATION");

        if (!ReferenceEquals(next.ActiveTrinketContext, context)
            || next.ActiveTrinkets.Count != 2 || state.ActiveTrinkets.Count != 1)
            return Fail("shallow simulation copies must preserve immutable effect context without sharing ID lists");

        // Equipped-effect resolution accepts only active CardIds. It intentionally has no input from
        // quote recommendations or the private TrinketRecommendationsVisible presentation switch.
        var offerOnly = registry.Resolve(new string[0]);
        if (offerOnly.ResolvedCardIds.Count != 0 || offerOnly.UnknownCardIds.Count != 0)
            return Fail("trinket offers must not be consumed as equipped effects");

        return 0;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine("FAIL " + message);
        return 1;
    }
}

namespace BobCoach.Engine
{
    // GameState requires this unrelated production type; the focused harness does not exercise anomalies.
    public sealed class AnomalyContext
    {
        public static readonly AnomalyContext Empty = new AnomalyContext();
    }
}
