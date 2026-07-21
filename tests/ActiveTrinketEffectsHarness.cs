using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BobCoach.Engine;

internal static class ActiveTrinketEffectsHarness
{
    // These are fixed live CardIds. Test minions below are synthetic states, not synthetic trinkets.
    private const string DesignerEyepatchId = "BG30_MagicItem_439";
    private const string CowrieNecklaceId = "BG35_MagicItem_921";
    private const string IronforgeAnvilId = "BG30_MagicItem_403";
    private const string SlammaStickerId = "BG30_MagicItem_540";
    private const string EmeraldDreamcatcherId = "BG30_MagicItem_542";
    private const string StegodonPortraitId = "BG35_MagicItem_702";
    private const string TinyfinOnesieId = "BG30_MagicItem_441";
    private const string DramalocStickerId = "BG35_MagicItem_754";

    private static int Main()
    {
        var registry = new TrinketEffectRegistry();

        if (!string.Equals(
            TrinketEffectRegistry.RuleSetVersion,
            "hdt-1.53.5-hearthdb-2026-07-22",
            StringComparison.Ordinal))
            return Fail("equipped-trinket rules must expose their audited local ruleset version");
        if (TestExactResolutionAndDiagnostics(registry) != 0) return 1;
        if (TestExactStatSpellClassification() != 0) return 1;
        if (TestUnknownDiagnosticDeduplication() != 0) return 1;
        if (TestGoldenRequirement(registry) != 0) return 1;
        if (TestTavernSpellCost(registry) != 0) return 1;
        if (TestSynergyScoring(registry) != 0) return 1;
        if (TestCombatEffectsAndContextIsolation(registry) != 0) return 1;
        if (TestExpandedStartOfCombatEffects(registry) != 0) return 1;
        if (TestCombatSimulatorContextWiring(registry) != 0) return 1;
        if (TestEyepatchPurchaseGoldenResolution(registry) != 0) return 1;
        if (TestSimulationCopyAndOfferIsolation(registry) != 0) return 1;
        if (TestProductionExtractionBoundary() != 0) return 1;

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
            EmeraldDreamcatcherId,
            StegodonPortraitId,
            TinyfinOnesieId,
            DramalocStickerId,
            DesignerEyepatchId.ToLowerInvariant(),
            "UNKNOWN_ACTIVE_TRINKET",
        });

        if (context.ResolvedCardIds.Count != 5
            || context.ResolvedCardIds[0] != DesignerEyepatchId
            || !context.ResolvedCardIds.Contains(EmeraldDreamcatcherId)
            || !context.ResolvedCardIds.Contains(StegodonPortraitId)
            || !context.ResolvedCardIds.Contains(TinyfinOnesieId)
            || !context.ResolvedCardIds.Contains(DramalocStickerId)
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
        var pirate = new MinionData { CardId = "TEST_PIRATE", Tribe = "海盗" };
        var beast = new MinionData { CardId = "TEST_BEAST", Tribe = "Beast" };
        var statSpell = new MinionData { CardId = "TEST_STAT_SPELL", IsSpell = true, GrantsStats = true };
        var ordinarySpell = new MinionData { CardId = "TEST_ORDINARY_SPELL", IsSpell = true };
        var typeless = new MinionData { CardId = "TEST_TYPELESS" };
        var dragon = new MinionData { CardId = "TEST_DRAGON", Tribe = "龙" };
        var murloc = new MinionData { CardId = "TEST_MURLOC", Tribe = "鱼人" };

        if (!HasPositiveTargetOnlyScore(registry.Resolve(new[] { DesignerEyepatchId }), pirate, beast)
            || !HasPositiveTargetOnlyScore(registry.Resolve(new[] { CowrieNecklaceId }), statSpell, ordinarySpell)
            || !HasPositiveTargetOnlyScore(registry.Resolve(new[] { IronforgeAnvilId }), typeless, beast)
            || !HasPositiveTargetOnlyScore(registry.Resolve(new[] { SlammaStickerId }), beast, pirate)
            || !HasPositiveTargetOnlyScore(registry.Resolve(new[] { EmeraldDreamcatcherId }), dragon, pirate)
            || !HasPositiveTargetOnlyScore(registry.Resolve(new[] { StegodonPortraitId }), beast, pirate)
            || !HasPositiveTargetOnlyScore(registry.Resolve(new[] { DramalocStickerId }), murloc, pirate))
            return Fail("each equipped trinket must give positive synergy only to its own target card and board");

        return 0;
    }

    private static int TestExpandedStartOfCombatEffects(TrinketEffectRegistry registry)
    {
        var dreamBoard = new List<CombatUnit>
        {
            Unit("DREAM_DRAGON_LEFT", 3, 6, "龙"),
            Unit("DREAM_PIRATE", 12, 4, "Pirate"),
            Unit("DREAM_DRAGON_RIGHT", 9, 6, "Dragon"),
        };
        registry.Resolve(new[] { EmeraldDreamcatcherId })
            .ApplyStartOfCombat(dreamBoard, new List<MinionData>());
        if (dreamBoard[0].Attack != 12 || dreamBoard[1].Attack != 12
            || dreamBoard[2].Attack != 12)
            return Fail("Dreamcatcher must set only Dragons to the owner's highest board Attack");

        var stegodonBoard = new List<CombatUnit>
        {
            Unit("STEGODON_PIRATE", 2, 2, "Pirate"),
            Unit("STEGODON_BEAST_1", 3, 3, "野兽"),
            Unit("STEGODON_BEAST_2", 4, 4, "Beast"),
            Unit("STEGODON_BEAST_3", 5, 5, "Beast"),
        };
        registry.Resolve(new[] { StegodonPortraitId })
            .ApplyStartOfCombat(stegodonBoard, new List<MinionData>());
        if (stegodonBoard[0].DivineShield || !stegodonBoard[1].DivineShield
            || !stegodonBoard[2].DivineShield || stegodonBoard[3].DivineShield)
            return Fail("Stegodon must give Divine Shield to exactly the two left-most Beasts");

        var tinyfinBoard = new List<CombatUnit>
        {
            Unit("TINYFIN_LEFT", 2, 3, "Pirate"),
            Unit("TINYFIN_RIGHT", 8, 8, "Murloc"),
        };
        var tinyfinHand = new List<MinionData>
        {
            new MinionData { CardId = "HAND_HIGH_HEALTH", Attack = 4, Health = 9 },
            new MinionData { CardId = "HAND_HIGH_ATTACK", Attack = 10, Health = 5 },
        };
        var tinyfin = registry.Resolve(new[] { TinyfinOnesieId });
        tinyfin.ApplyStartOfCombat(tinyfinBoard, tinyfinHand);
        if (tinyfinBoard[0].Attack != 6 || tinyfinBoard[0].Health != 12
            || tinyfinBoard[0].MaxHealth != 12 || tinyfinBoard[1].Attack != 8)
            return Fail("Tinyfin must add both stats of the owner's highest-Health hand minion to the left-most unit");
        var noHandBoard = new List<CombatUnit> { Unit("TINYFIN_NO_HAND", 3, 4, "Beast") };
        tinyfin.ApplyStartOfCombat(noHandBoard, new List<MinionData>());
        if (noHandBoard[0].Attack != 3 || noHandBoard[0].Health != 4)
            return Fail("Tinyfin must fail closed when the owner has no hand minion");

        var dramalocBoard = new List<CombatUnit>
        {
            Unit("DRAMALOC_MURLOC_1", 2, 3, "鱼人"),
            Unit("DRAMALOC_PIRATE", 4, 4, "Pirate"),
            Unit("DRAMALOC_MURLOC_2", 5, 6, "Murloc"),
        };
        var dramalocHand = new List<MinionData>
        {
            new MinionData { CardId = "HAND_ATTACK_1", Attack = 7, Health = 2 },
            new MinionData { CardId = "HAND_ATTACK_2", Attack = 3, Health = 20 },
        };
        registry.Resolve(new[] { DramalocStickerId })
            .ApplyStartOfCombat(dramalocBoard, dramalocHand);
        if (dramalocBoard[0].Attack != 9 || dramalocBoard[1].Attack != 4
            || dramalocBoard[2].Attack != 12)
            return Fail("Dramaloc must add the owner's highest hand Attack only to Murlocs");

        return 0;
    }

    private static CombatUnit Unit(string cardId, int attack, int health, string tribe)
    {
        return new CombatUnit
        {
            CardId = cardId,
            Attack = attack,
            Health = health,
            MaxHealth = health,
            Alive = true,
            MinionTypes = new List<string> { tribe },
        };
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

        var tinyfinBoard = new List<MinionData>
        {
            new MinionData { CardId = "TEST_TINYFIN_TARGET", Attack = 1, Health = 1, Tier = 1 },
        };
        var tinyfinEnemy = new List<MinionData>
        {
            new MinionData { CardId = "TEST_TINYFIN_ENEMY", Attack = 5, Health = 5, Tier = 1 },
        };
        var tinyfinHand = new List<MinionData>
        {
            new MinionData { CardId = "TEST_TINYFIN_HAND", Attack = 10, Health = 10, Tier = 1 },
        };
        var tinyfinResult = simulator.Simulate(
            tinyfinBoard, tinyfinEnemy, playerHand: tinyfinHand,
            playerTrinkets: registry.Resolve(new[] { TinyfinOnesieId }));

        if (!attackerResult.PlayerWon || defenderResult.PlayerWon || !tinyfinResult.PlayerWon)
            return Fail("CombatSimulator must route each board and hand only to its owner's trinket context");

        return 0;
    }

    private static int TestProductionExtractionBoundary()
    {
        string sourcePath = Path.Combine(
            Directory.GetCurrentDirectory(), "src", "BobCoach", "GameStateExtractor.cs");
        string source = File.ReadAllText(sourcePath);
        int activeIndex = source.IndexOf(
            "ExtractActiveTrinkets(entities, state);", StringComparison.Ordinal);
        int goldIndex = source.IndexOf("TrackGold(state, entities)", StringComparison.Ordinal);
        if (activeIndex < 0 || goldIndex < 0 || activeIndex >= goldIndex)
            return Fail("active trinkets must be merged before gold tracking and resource calculation");

        const string exactClassifier = "TrinketEffectRegistry.IsStatGrantingTavernSpell(";
        int classifierCalls = source.Split(
            new[] { exactClassifier }, StringSplitOptions.None).Length - 1;
        if (classifierCalls != 2)
            return Fail("shop and hand stat-spell classification must share the exact CardId registry");

        int methodStart = source.IndexOf(
            "private void ExtractActiveTrinkets", StringComparison.Ordinal);
        int methodEnd = source.IndexOf(
            "private List<Engine.TrinketOption> ExtractTrinketOffer", StringComparison.Ordinal);
        if (methodStart < 0 || methodEnd <= methodStart)
            return Fail("active-trinket extraction method could not be audited");
        string method = source.Substring(methodStart, methodEnd - methodStart);
        if (!method.Contains("e.IsBattlegroundsTrinket && e.IsInPlay")
            || method.Contains("HasLocalTrinketFact")
            || !method.Contains("UnknownCardIds")
            || !method.Contains("_unknownTrinketDiagnosticTracker.ShouldReport(cardId)")
            || !method.Contains("ExtractorLog(\"DIAG ActiveTrinket: unknown CardId=\" + cardId)"))
            return Fail("unknown equipped CardIds must reach exact once-per-game extractor diagnostics without fact filtering");

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
