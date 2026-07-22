using System;
using System.Collections.Generic;
using System.Reflection;
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
    private const string EternalPortraitId = "BG30_MagicItem_301";
    private const string RivendarePortraitId = "BG30_MagicItem_310";
    private const string HolyMalletId = "BG30_MagicItem_902";
    private const string TrainingCertificateId = "BG30_MagicItem_962";
    private const string ValorousMedallionId = "BG30_MagicItem_970";
    private const string GreaterValorousMedallionId = "BG30_MagicItem_970t";
    private const string BalefulIncenseId = "BG32_MagicItem_360";
    private const string BartendOTronOilcanId = "BG30_MagicItem_705";
    private const string KarazhanChessSetId = "BG30_MagicItem_972";

    private static int Main()
    {
        var registry = new TrinketEffectRegistry();

        if (!string.Equals(
            TrinketEffectRegistry.RuleSetVersion,
            "hdt-1.53.5-hearthdb-2026-07-22-r4",
            StringComparison.Ordinal))
            return Fail("equipped-trinket rules must expose their audited local ruleset version");
        if (TestKarazhanChessSetSummonSemantics(registry) != 0) return 1;
        if (TestExactResolutionAndDiagnostics(registry) != 0) return 1;
        if (TestExactStatSpellClassification() != 0) return 1;
        if (TestUnknownDiagnosticDeduplication() != 0) return 1;
        if (TestGoldenRequirement(registry) != 0) return 1;
        if (TestTavernSpellCost(registry) != 0) return 1;
        if (TestOilcanUpgradeRules(registry) != 0) return 1;
        if (TestSynergyScoring(registry) != 0) return 1;
        if (TestCombatEffectsAndContextIsolation(registry) != 0) return 1;
        if (TestExpandedStartOfCombatEffects(registry) != 0) return 1;
        if (TestPhase2StartOfCombatEffects(registry) != 0) return 1;
        if (TestCombatSimulatorContextWiring(registry) != 0) return 1;
        if (TestRebornUsesOwnerSummonEffects(registry) != 0) return 1;
        if (TestRebornRequiresBoardSpace(registry) != 0) return 1;
        if (TestExplicitStartOfCombatOrdering(registry) != 0) return 1;
        if (TestCombatSimulatorStartOfCombatOrdering(registry) != 0) return 1;
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
            CowrieNecklaceId,
            IronforgeAnvilId,
            SlammaStickerId,
            EmeraldDreamcatcherId,
            StegodonPortraitId,
            TinyfinOnesieId,
            DramalocStickerId,
            EternalPortraitId,
            RivendarePortraitId,
            HolyMalletId,
            TrainingCertificateId,
            ValorousMedallionId,
            GreaterValorousMedallionId,
            BalefulIncenseId,
            BartendOTronOilcanId,
            KarazhanChessSetId,
            EternalPortraitId,
            DesignerEyepatchId.ToLowerInvariant(),
            "UNKNOWN_ACTIVE_TRINKET",
        });

        if (context.ResolvedCardIds.Count != 17
            || context.ResolvedCardIds[0] != DesignerEyepatchId
            || !context.ResolvedCardIds.Contains(CowrieNecklaceId)
            || !context.ResolvedCardIds.Contains(IronforgeAnvilId)
            || !context.ResolvedCardIds.Contains(SlammaStickerId)
            || !context.ResolvedCardIds.Contains(EmeraldDreamcatcherId)
            || !context.ResolvedCardIds.Contains(StegodonPortraitId)
            || !context.ResolvedCardIds.Contains(TinyfinOnesieId)
            || !context.ResolvedCardIds.Contains(DramalocStickerId)
            || !context.ResolvedCardIds.Contains(EternalPortraitId)
            || !context.ResolvedCardIds.Contains(RivendarePortraitId)
            || !context.ResolvedCardIds.Contains(HolyMalletId)
            || !context.ResolvedCardIds.Contains(TrainingCertificateId)
            || !context.ResolvedCardIds.Contains(ValorousMedallionId)
            || !context.ResolvedCardIds.Contains(GreaterValorousMedallionId)
            || !context.ResolvedCardIds.Contains(BalefulIncenseId)
            || !context.ResolvedCardIds.Contains(BartendOTronOilcanId)
            || !context.ResolvedCardIds.Contains(KarazhanChessSetId)
            || context.UnknownCardIds.Count != 2
            || !context.UnknownCardIds.Contains(DesignerEyepatchId.ToLowerInvariant())
            || !context.UnknownCardIds.Contains("UNKNOWN_ACTIVE_TRINKET"))
            return Fail("known effects must resolve by exact CardId and unknown IDs must be diagnostics only");

        return 0;
    }

    private static int TestKarazhanChessSetSummonSemantics(TrinketEffectRegistry registry)
    {
        var chessSet = registry.Resolve(new[] { KarazhanChessSetId, SlammaStickerId });
        if (!chessSet.ResolvedCardIds.Contains(KarazhanChessSetId)
            || chessSet.UnknownCardIds.Count != 0)
            return Fail("Karazhan Chess Set must resolve only from its exact audited CardId");

        var left = Unit("TEST_KARAZHAN_LEFT", 3, 4, "Beast");
        left.Position = 0;
        left.BaseAttack = 3;
        left.BaseHealth = 4;
        left.DivineShield = true;
        left.Reborn = true;
        left.Taunt = true;
        left.HasStartOfCombat = true;
        left.HasDeathrattle = true;
        left.DeathrattleCount = 1;
        left.Mechanics.Add("TEST_MECHANIC");
        left.Extra["source"] = "original";
        var right = Unit("TEST_KARAZHAN_RIGHT", 9, 9, "Pirate");
        right.Position = 1;
        var attacker = new List<CombatUnit> { left, right };
        var defenderLeft = Unit("TEST_KARAZHAN_DEFENDER", 7, 7, "Beast");
        var defender = new List<CombatUnit> { defenderLeft };
        var context = new CombatContext(attacker, defender, new Random(1),
            attackerTrinkets: chessSet,
            defenderTrinkets: registry.Resolve(new string[0]));

        CombatEffects.Register(left.CardId, new CardHandlers
        {
            StartOfCombat = (ctx, own, enemy) =>
            {
                left.Attack += 2;
                left.Health += 1;
                left.MaxHealth += 1;
            }
        });
        InvokeStartOfCombat(attacker, defender, context);

        if (attacker.Count != 3 || context.AllUnits.Count != 4
            || !ReferenceEquals(attacker[0], left)
            || !ReferenceEquals(attacker[2], right)
            || ReferenceEquals(attacker[1], left))
            return Fail("Karazhan Chess Set must summon one copy beside the current left-most owner minion");

        CombatUnit copy = attacker[1];
        if (left.Attack != 5 || left.Health != 5 || left.MaxHealth != 5
            || copy.CardId != left.CardId || copy.Attack != 10
            || copy.Health != 5 || copy.MaxHealth != 5
            || !copy.DivineShield || !copy.Reborn || !copy.Taunt
            || copy.DeathrattleCount != 1 || copy.Position != 1
            || right.Position != 2
            || defender.Count != 1 || !ReferenceEquals(defender[0], defenderLeft))
            return Fail("Karazhan must copy post-minion-effect state for its owner and apply Slamma exactly once");

        if (ReferenceEquals(copy.MinionTypes, left.MinionTypes)
            || ReferenceEquals(copy.Mechanics, left.Mechanics)
            || ReferenceEquals(copy.Extra, left.Extra)
            || copy.StartOfCombatTriggered || copy.DeathrattleTriggered
            || copy.AvengeTriggered || copy.RebornUsed || copy.KilledBy != null)
            return Fail("Karazhan copies must have independent collections and fresh runtime trigger state");

        copy.MinionTypes.Add("Pirate");
        copy.Mechanics.Add("COPY_ONLY");
        copy.Extra["copy"] = true;
        if (left.MinionTypes.Contains("Pirate") || left.Mechanics.Contains("COPY_ONLY")
            || left.Extra.ContainsKey("copy"))
            return Fail("Karazhan copy mutations must not leak back to the source minion");

        var fullBoard = new List<CombatUnit>();
        for (int i = 0; i < 7; i++)
            fullBoard.Add(Unit("TEST_KARAZHAN_FULL_" + i, 1, 1, "Beast"));
        var fullEnemy = new List<CombatUnit> { Unit("TEST_KARAZHAN_FULL_ENEMY", 1, 1, "Pirate") };
        var fullContext = new CombatContext(fullBoard, fullEnemy, new Random(1),
            attackerTrinkets: registry.Resolve(new[] { KarazhanChessSetId }));
        InvokeStartOfCombat(fullBoard, fullEnemy, fullContext);
        if (fullBoard.Count != 7 || fullContext.AllUnits.Count != 8
            || fullContext.LastSummoned != null)
            return Fail("Karazhan Chess Set must fail closed when the owner's board is full");

        var wrongCase = registry.Resolve(new[] { KarazhanChessSetId.ToLowerInvariant() });
        if (wrongCase.ResolvedCardIds.Count != 0 || wrongCase.UnknownCardIds.Count != 1)
            return Fail("Karazhan Chess Set matching must remain exact and case-sensitive");

        return 0;
    }

    private static void InvokeStartOfCombat(
        List<CombatUnit> attacker, List<CombatUnit> defender, CombatContext context)
    {
        var method = typeof(CombatSimulator).GetMethod("PhaseStartOfCombat",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (method == null) throw new InvalidOperationException("PhaseStartOfCombat not found");
        method.Invoke(new CombatSimulator(), new object[] { attacker, defender, context });
    }

    private static int TestOilcanUpgradeRules(TrinketEffectRegistry registry)
    {
        var oilcan = registry.Resolve(new[] { BartendOTronOilcanId });
        var state = new GameState
        {
            GameActive = true,
            Gold = 4,
            TavernTier = 2,
            Turn = 2,
            LastUpgradeTurn = 2,
            TavernUpgradeCost = -1,
            ActiveTrinketContext = oilcan,
            EffectiveRules = oilcan.ApplyTo(EffectiveGameRules.Default),
            BoardMinions = new List<MinionData>(),
            HandMinions = new List<MinionData>(),
            ShopMinions = new List<MinionData>(),
        };

        if (contextHasNoUnknown(oilcan) == false)
            return Fail("Oilcan must resolve as a known exact CardId");
        if (GameRuleEvaluator.GetUpgradeCost(state, state.EffectiveRules) != 4)
            return Fail("Oilcan must reduce the fallback upgrade cost by exactly 3");
        if (!new ActionEnumerator().Enumerate(state).Any(action => action.Type == ActionType.Upgrade))
            return Fail("ActionEnumerator must allow upgrade when gold meets Oilcan's discounted cost");

        var simulated = new Simulator().Simulate(state, new GameAction { Type = ActionType.Upgrade });
        if (simulated.TavernTier != 3 || simulated.Gold != 0)
            return Fail("Simulator must deduct Oilcan's discounted upgrade cost and advance the tavern tier");

        state.Turn = 10;
        state.LastUpgradeTurn = 1;
        if (GameRuleEvaluator.GetUpgradeCost(state, state.EffectiveRules) != 0)
            return Fail("Oilcan fallback upgrade cost must clamp at zero");

        state.TavernUpgradeCost = 5;
        state.Gold = 4;
        if (GameRuleEvaluator.GetUpgradeCost(state, state.EffectiveRules) != 5
            || new ActionEnumerator().Enumerate(state).Any(action => action.Type == ActionType.Upgrade))
            return Fail("Observed HDT upgrade cost must be treated as already effective without double discount");

        string phaseSourcePath = Path.Combine(
            Directory.GetCurrentDirectory(), "src", "BobCoach", "Core", "TurnPhaseEngine.cs");
        string phaseSource = File.ReadAllText(phaseSourcePath);
        if (!phaseSource.Contains("GameRuleEvaluator.GetUpgradeCost(state, rules)"))
            return Fail("TurnPhaseEngine must use the shared effective upgrade-cost evaluator");

        var wrongCase = registry.Resolve(new[] { BartendOTronOilcanId.ToLowerInvariant() });
        if (wrongCase.ResolvedCardIds.Count != 0 || wrongCase.UnknownCardIds.Count != 1)
            return Fail("Oilcan matching must remain exact and case-sensitive");
        return 0;
    }

    private static bool contextHasNoUnknown(ActiveTrinketContext context)
    {
        return context != null && context.UnknownCardIds.Count == 0;
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

    private static int TestPhase2StartOfCombatEffects(TrinketEffectRegistry registry)
    {
        var portraitBoard = new List<CombatUnit>
        {
            Unit("BG25_008", 5, 7, "Undead"),
            Unit("BG25_008_G", 10, 14, "Undead"),
            Unit("TEST_ETERNAL_LOOKALIKE", 5, 7, "Undead"),
            Unit("BG25_354", 6, 4, "Undead"),
            Unit("BG25_354_G", 12, 8, "Undead"),
        };
        portraitBoard[3].MaxHealth = 10;
        portraitBoard[4].MaxHealth = 16;
        registry.Resolve(new[] { EternalPortraitId, RivendarePortraitId })
            .ApplyStartOfCombat(portraitBoard);
        if (!portraitBoard[0].Taunt || !portraitBoard[0].Reborn
            || !portraitBoard[1].Taunt || !portraitBoard[1].Reborn
            || portraitBoard[2].Taunt || portraitBoard[2].Reborn
            || portraitBoard[3].Health != 8 || portraitBoard[3].MaxHealth != 20
            || portraitBoard[4].Health != 16 || portraitBoard[4].MaxHealth != 32)
            return Fail("portraits must target only exact normal and golden Eternal Knight or Titus CardIds");

        var edgeBoard = new List<CombatUnit>
        {
            Unit("EDGE_LEFT", 4, 4, "Undead"),
            Unit("EDGE_MIDDLE", 5, 5, "Pirate"),
            Unit("EDGE_RIGHT", 6, 6, "Undead"),
        };
        registry.Resolve(new[] { HolyMalletId, BalefulIncenseId })
            .ApplyStartOfCombat(edgeBoard);
        if (!edgeBoard[0].DivineShield || edgeBoard[1].DivineShield
            || !edgeBoard[2].DivineShield || !edgeBoard[0].Reborn
            || edgeBoard[1].Reborn || !edgeBoard[2].Reborn)
            return Fail("edge rules must affect only the owner's left-most and right-most valid targets");

        var singleBoard = new List<CombatUnit> { Unit("SINGLE_UNDEAD", 3, 5, "Undead") };
        registry.Resolve(new[] { HolyMalletId, BalefulIncenseId })
            .ApplyStartOfCombat(singleBoard);
        if (!singleBoard[0].DivineShield || !singleBoard[0].Reborn
            || singleBoard[0].Attack != 3 || singleBoard[0].Health != 5)
            return Fail("edge rules must handle a single valid target exactly once");

        var trainingBoard = new List<CombatUnit>
        {
            Unit("TRAINING_FIRST_TIE", 2, 3, "Beast"),
            Unit("TRAINING_SECOND_TIE", 2, 4, "Pirate"),
            Unit("TRAINING_THIRD_TIE", 2, 5, "Dragon"),
            Unit("TRAINING_HIGH", 8, 8, "Mech"),
        };
        registry.Resolve(new[] { TrainingCertificateId })
            .ApplyStartOfCombat(trainingBoard);
        if (trainingBoard[0].Attack != 4 || trainingBoard[0].Health != 6
            || trainingBoard[0].MaxHealth != 6
            || trainingBoard[1].Attack != 4 || trainingBoard[1].Health != 8
            || trainingBoard[1].MaxHealth != 8
            || trainingBoard[2].Attack != 2 || trainingBoard[2].Health != 5
            || trainingBoard[3].Attack != 8 || trainingBoard[3].Health != 8)
            return Fail("Training Certificate must stably double exactly the first two lowest-Attack units");

        var medallionBoard = new List<CombatUnit>
        {
            Unit("MEDALLION_OWNER", 1, 2, "Beast"),
            Unit("MEDALLION_OWNER_2", 3, 4, "Pirate"),
        };
        var opponentBoard = new List<CombatUnit>
        {
            Unit("MEDALLION_OPPONENT", 7, 9, "Undead"),
        };
        registry.Resolve(new[] { ValorousMedallionId, GreaterValorousMedallionId })
            .ApplyStartOfCombat(medallionBoard);
        if (medallionBoard[0].Attack != 9 || medallionBoard[0].Health != 10
            || medallionBoard[0].MaxHealth != 10
            || medallionBoard[1].Attack != 11 || medallionBoard[1].Health != 12
            || medallionBoard[1].MaxHealth != 12
            || opponentBoard[0].Attack != 7 || opponentBoard[0].Health != 9
            || opponentBoard[0].MaxHealth != 9)
            return Fail("medallions must stack on their owner only and keep current and maximum Health aligned");

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

    private static int TestCombatSimulatorStartOfCombatOrdering(TrinketEffectRegistry registry)
    {
        var playerBoard = new List<MinionData>
        {
            new MinionData
            {
                CardId = "BG25_040", Attack = 9, Health = 1, Tier = 1, Tribe = "龙",
            },
            new MinionData
            {
                CardId = "TEST_ORDER_TYPELESS", Attack = 10, Health = 1, Tier = 1,
            },
        };
        var opponentBoard = new List<MinionData>
        {
            new MinionData
            {
                CardId = "TEST_ORDER_OPPONENT", Attack = 20, Health = 12, Tier = 1,
            },
        };

        var result = new CombatSimulator().Simulate(
            playerBoard, opponentBoard,
            registry.Resolve(new[] { EmeraldDreamcatcherId }),
            registry.Resolve(new string[0]));

        if (result.PlayerWon || result.PlayerSurvivors != 0 || result.OpponentSurvivors != 0)
            return Fail("equipped trinket start-of-combat effects must resolve after minion effects");

        return 0;
    }

    private static int TestRebornUsesOwnerSummonEffects(TrinketEffectRegistry registry)
    {
        var slamma = registry.Resolve(new[] { SlammaStickerId });
        var rebornBeast = new CombatUnit
        {
            CardId = "TEST_REBORN_BEAST",
            Attack = 12,
            BaseAttack = 4,
            Health = 0,
            MaxHealth = 12,
            BaseHealth = 1,
            Alive = false,
            Reborn = true,
            DivineShield = true,
            Taunt = true,
            Stealthed = true,
            WindfuryAttacksLeft = 0,
            DeathrattleTriggered = true,
            AvengeTriggered = true,
            KilledBy = new CombatUnit(),
            MinionTypes = new List<string> { "Beast" },
        };
        var context = new CombatContext(
            new List<CombatUnit> { rebornBeast },
            new List<CombatUnit> { Unit("TEST_REBORN_OPPONENT", 1, 1, "Pirate") },
            new Random(1), attackerTrinkets: slamma);
        context.OnDeath(rebornBeast);
        context.AttackerSide.Remove(rebornBeast);
        context.ProcessEvents();

        var playerBoard = new List<MinionData>
        {
            new MinionData
            {
                CardId = "TEST_REBORN_COMBAT_BEAST", Attack = 4, Health = 1,
                Tier = 1, Tribe = "Beast", Reborn = true,
            },
            new MinionData
            {
                CardId = "TEST_REBORN_SUPPORT", Attack = 0, Health = 100,
                Tier = 1, Taunt = true,
            },
        };
        var opponentBoard = new List<MinionData>
        {
            new MinionData
            {
                CardId = "TEST_REBORN_COMBAT_OPPONENT", Attack = 1, Health = 9, Tier = 1,
            },
        };
        var simulator = new CombatSimulator();
        var withSlamma = simulator.Simulate(
            playerBoard, opponentBoard, slamma, registry.Resolve(new string[0]));
        var withoutSlamma = simulator.Simulate(
            playerBoard, opponentBoard,
            registry.Resolve(new string[0]), registry.Resolve(new string[0]));

        if (!rebornBeast.Alive || !rebornBeast.RebornUsed
            || context.AttackerSide.Count != 1
            || !ReferenceEquals(context.AttackerSide[0], rebornBeast)
            || context.AllUnits.Count != 2
            || rebornBeast.Attack != 8 || rebornBeast.Health != 1 || rebornBeast.MaxHealth != 1
            || rebornBeast.DivineShield || rebornBeast.Taunt || rebornBeast.Stealthed
            || rebornBeast.WindfuryAttacksLeft != 1 || rebornBeast.KilledBy != null
            || rebornBeast.DeathrattleTriggered || rebornBeast.AvengeTriggered
            || !withSlamma.PlayerWon || withoutSlamma.PlayerWon)
            return Fail("Reborn Beasts must reset combat state and trigger their owner's summon effect exactly once");

        return 0;
    }

    private static int TestRebornRequiresBoardSpace(TrinketEffectRegistry registry)
    {
        var reborn = Unit("TEST_REBORN_NO_SPACE", 4, 4, "Beast");
        reborn.Reborn = true;
        var attacker = new List<CombatUnit> { reborn };
        for (int i = 0; i < 6; i++)
            attacker.Add(Unit("TEST_REBORN_ALLY_" + i, 1, 1, "Beast"));
        var context = new CombatContext(attacker,
            new List<CombatUnit> { Unit("TEST_REBORN_NO_SPACE_OPPONENT", 1, 1, "Pirate") },
            new Random(1), attackerTrinkets: registry.Resolve(new string[0]));

        reborn.Alive = false;
        context.OnDeath(reborn);
        context.AttackerSide.Remove(reborn);
        context.AttackerSide.Add(Unit("TEST_REBORN_SLOT_FILLER", 1, 1, "Beast"));
        context.ProcessEvents();

        if (reborn.Alive || context.AttackerSide.Contains(reborn) || !reborn.RebornUsed)
            return Fail("Reborn must remain dead when its board slot is filled before resolution");

        return 0;
    }

    private static int TestExplicitStartOfCombatOrdering(TrinketEffectRegistry registry)
    {
        var order = new List<string>();
        var attackerBoardUnit = Unit("TEST_ORDER_ATTACKER_BOARD", 1, 1, "Dragon");
        attackerBoardUnit.HasStartOfCombat = true;
        var defenderBoardUnit = Unit("TEST_ORDER_DEFENDER_BOARD", 1, 1, "Pirate");
        defenderBoardUnit.HasStartOfCombat = true;
        var attacker = new List<CombatUnit> { attackerBoardUnit };
        var defender = new List<CombatUnit> { defenderBoardUnit };
        var attackerHand = new List<MinionData> { new MinionData { CardId = "TEST_ORDER_ATTACKER_HAND" } };
        var defenderHand = new List<MinionData> { new MinionData { CardId = "TEST_ORDER_DEFENDER_HAND" } };
        var context = new CombatContext(attacker, defender, new Random(1), attackerHand, defenderHand,
            registry.Resolve(new[] { EmeraldDreamcatcherId }), registry.Resolve(new string[0]));

        context.AttackerPriorityHeroPower = (ctx, own, enemy) => order.Add("attacker-hero-priority");
        context.DefenderPriorityHeroPower = (ctx, own, enemy) => order.Add("defender-hero-priority");
        context.AttackerHeroPower = (ctx, own, enemy) => order.Add("attacker-hero-normal");
        context.DefenderHeroPower = (ctx, own, enemy) => order.Add("defender-hero-normal");
        context.AttackerTrinketHandlers = new List<Action<CombatContext, List<CombatUnit>, List<CombatUnit>>>
        {
            (ctx, own, enemy) => order.Add("attacker-trinket")
        };
        context.DefenderTrinketHandlers = new List<Action<CombatContext, List<CombatUnit>, List<CombatUnit>>>
        {
            (ctx, own, enemy) => order.Add("defender-trinket")
        };
        CombatEffects.Register("TEST_ORDER_ATTACKER_BOARD", new CardHandlers
        {
            StartOfCombat = (ctx, own, enemy) => order.Add("attacker-board")
        });
        CombatEffects.Register("TEST_ORDER_DEFENDER_BOARD", new CardHandlers
        {
            StartOfCombat = (ctx, own, enemy) => order.Add("defender-board")
        });
        CombatEffects.Register("TEST_ORDER_ATTACKER_HAND", new CardHandlers
        {
            StartOfCombat = (ctx, own, enemy) => order.Add("attacker-hand")
        });
        CombatEffects.Register("TEST_ORDER_DEFENDER_HAND", new CardHandlers
        {
            StartOfCombat = (ctx, own, enemy) => order.Add("defender-hand")
        });

        var method = typeof(CombatSimulator).GetMethod("PhaseStartOfCombat",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (method == null) return Fail("PhaseStartOfCombat must remain testable for its explicit ordering contract");
        method.Invoke(new CombatSimulator(), new object[] { attacker, defender, context });

        var expected = new[]
        {
            "attacker-hero-priority", "defender-hero-priority",
            "attacker-hero-normal", "defender-hero-normal",
            "attacker-board", "defender-board",
            "attacker-hand", "defender-hand",
            "attacker-trinket", "defender-trinket",
        };
        if (order.Count != expected.Length)
            return Fail("PhaseStartOfCombat must execute every registered stage exactly once");
        for (int i = 0; i < expected.Length; i++)
            if (!string.Equals(order[i], expected[i], StringComparison.Ordinal))
                return Fail("PhaseStartOfCombat order must be priority hero, normal hero, board, hand, then trinket");
        if (attackerBoardUnit.Attack != 1)
            return Fail("ordering harness must not infer trinket order from mutated board stats");

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
