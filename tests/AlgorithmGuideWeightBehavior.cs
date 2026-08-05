using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BobCoach.Engine;

internal static class AlgorithmGuideWeightBehavior
{
    private const string GuidesJson = @"{
  ""guides"": [{
    ""id"": ""SYNTH_GUIDE"",
    ""name"": ""SYNTH_GUIDE_NAME"",
    ""tier"": ""T0"",
    ""tribes"": [],
    ""coreCards"": [""SYNTH_CARD""],
    ""trinkets"": [""SYNTH_TRINKET""],
    ""strategy"": ""SYNTH_STRATEGY""
  }]
}";

    private const string WeightsJson = @"{
  ""counts"": {
    ""guides"": 1,
    ""coreEntries"": 2,
    ""trinketEntries"": 1
  },
  ""coreCards"": {
    ""SYNTH_CARD"": {
      ""name"": ""SYNTH_CARD"",
      ""officialName"": ""SYNTH_CARD_OFFICIAL"",
      ""officialCardId"": ""SYNTH_CARD_ID"",
      ""occurrences"": 1,
      ""uniqueGuides"": 1,
      ""multiCopyGuides"": [],
      ""duplicateBonus"": 0,
      ""rawWeight"": 1000,
      ""heuristicWeight"": 1000,
      ""tierCounts"": { ""T0"": 1, ""T1"": 0, ""T2"": 0 }
    },
    ""SYNTH_NULL_CARD"": {
      ""name"": ""SYNTH_NULL_CARD"",
      ""officialName"": ""SYNTH_NULL_CARD"",
      ""officialCardId"": ""SYNTH_NULL_CARD_ID"",
      ""occurrences"": 1,
      ""uniqueGuides"": 1,
      ""multiCopyGuides"": [],
      ""duplicateBonus"": 0,
      ""rawWeight"": null,
      ""heuristicWeight"": null,
      ""tierCounts"": { ""T0"": 0, ""T1"": 1, ""T2"": 0 }
    }
  },
  ""trinkets"": {
    ""SYNTH_TRINKET"": {
      ""name"": ""SYNTH_TRINKET"",
      ""officialName"": ""SYNTH_TRINKET_OFFICIAL"",
      ""officialCardId"": ""SYNTH_TRINKET_ID"",
      ""occurrences"": 1,
      ""uniqueGuides"": 1,
      ""multiCopyGuides"": [],
      ""duplicateBonus"": 0,
      ""rawWeight"": null,
      ""heuristicWeight"": null,
      ""tierCounts"": { ""T0"": 0, ""T1"": 1, ""T2"": 0 }
    }
  }
}";

    private static int _assertions;

    [STAThread]
    private static int Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += ResolveDependency;
        try
        {
            RunBehavior();
            Console.WriteLine(
                "PASS algorithm guide weight behavior assertions=" + _assertions);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static Assembly ResolveDependency(object sender, ResolveEventArgs args)
    {
        var assemblyName = new AssemblyName(args.Name).Name;
        var hdtDirectory = Environment.GetEnvironmentVariable("BOBCOACH_HDT_DIR");
        if (string.IsNullOrWhiteSpace(hdtDirectory)) return null;

        var dllPath = Path.Combine(hdtDirectory, assemblyName + ".dll");
        if (File.Exists(dllPath)) return Assembly.LoadFrom(dllPath);
        var executablePath = Path.Combine(hdtDirectory, assemblyName + ".exe");
        return File.Exists(executablePath)
            ? Assembly.LoadFrom(executablePath)
            : null;
    }

    private static void RunBehavior()
    {
        var emptyCatalog = CreateEmptyCatalog();
        var loadedCatalog = CreateLoadedCatalog();

        AssertKnownWeightConsumedByShopScores(emptyCatalog, loadedCatalog);
        AssertKnownWeightConsumedByBuyIndices(emptyCatalog, loadedCatalog);
        AssertKnownWeightConsumedByBestAction(emptyCatalog, loadedCatalog);
        AssertDefaultGuideTierIsNull();
        AssertLoadedCatalogNullTierFailClosedAcrossDecisionPaths(
            emptyCatalog, loadedCatalog);
        AssertBestActionFailClosed(emptyCatalog, loadedCatalog);
        AssertFailClosed(emptyCatalog, loadedCatalog);
    }

    private static void AssertKnownWeightConsumedByShopScores(
        object emptyCatalog, object loadedCatalog)
    {
        var baseline = Score(NewEngine(emptyCatalog), "SYNTH_CARD", "T0");
        var weighted = Score(NewEngine(loadedCatalog), "SYNTH_CARD", "T0");
        var delta = weighted - baseline;

        AssertTrue(delta > 1.0,
            "known guide weight must raise shop card score, delta=" + delta);
        AssertNear(60.0, delta, 0.001,
            "T0 + rawWeight 1000 adds 60 score");
    }

    private static void AssertKnownWeightConsumedByBuyIndices(
        object emptyCatalog, object loadedCatalog)
    {
        var state = BuyState();
        var baseline = NewEngine(emptyCatalog).GetRecommendedBuyIndices(state, 1);
        var weighted = NewEngine(loadedCatalog).GetRecommendedBuyIndices(state, 1);

        AssertTrue(baseline.Count > 0 && baseline[0] != 1,
            "baseline buy recommendation must not select weighted card");
        AssertEqual(1, weighted.Count, "weighted buy recommendation count");
        AssertEqual(1, weighted[0],
            "known guide weight must influence recommended buy index");
    }

    private static void AssertKnownWeightConsumedByBestAction(
        object emptyCatalog, object loadedCatalog)
    {
        var state = BestActionState();
        var baseline = NewEngine(emptyCatalog).GetBestAction(state);
        var weighted = NewEngine(loadedCatalog).GetBestAction(state);

        AssertTrue(baseline != null && baseline.Type == ActionType.BuyMinion,
            "baseline best action must be buy minion");
        AssertTrue(weighted != null && weighted.Type == ActionType.BuyMinion,
            "weighted best action must be buy minion");
        AssertTrue(baseline.TargetIndex != 1,
            "baseline best action must not select weighted card");
        AssertEqual(1, weighted.TargetIndex,
            "known guide weight must influence best action");
    }

    private static void AssertBestActionFailClosed(
        object emptyCatalog, object loadedCatalog)
    {
        AssertBestActionSame(
            emptyCatalog, emptyCatalog, "SYNTH_CARD", "T0",
            "best action with unloaded catalog must fail closed");
        AssertBestActionSame(
            emptyCatalog, loadedCatalog, "SYNTH_UNKNOWN", "T0",
            "best action with unknown card must fail closed");
        AssertBestActionSame(
            emptyCatalog, loadedCatalog, "SYNTH_CARD", "T999",
            "best action with unknown tier must fail closed");
        AssertBestActionSame(
            emptyCatalog, loadedCatalog, "SYNTH_NULL_CARD", "T0",
            "best action with missing raw weight must fail closed");
        AssertBestActionSame(
            emptyCatalog, loadedCatalog, "SYNTH_CARD", null,
            "best action with missing current guide tier must fail closed");
    }

    private static void AssertBestActionSame(
        object emptyCatalog, object loadedCatalog,
        string weightedCardName, string tier, string label)
    {
        var baselineState = BestActionState();
        baselineState.ShopMinions[1].CardName = weightedCardName;
        SetTier(baselineState, tier);
        var baselineAction = NewEngine(emptyCatalog).GetBestAction(baselineState);

        var loadedState = BestActionState();
        loadedState.ShopMinions[1].CardName = weightedCardName;
        SetTier(loadedState, tier);
        var loadedAction = NewEngine(loadedCatalog).GetBestAction(loadedState);

        AssertEqual(baselineAction.Type, loadedAction.Type, label + " type");
        AssertEqual(baselineAction.TargetIndex, loadedAction.TargetIndex,
            label + " target");
    }

    private static void AssertFailClosed(
        object emptyCatalog, object loadedCatalog)
    {
        AssertScoreUnchanged(
            emptyCatalog, emptyCatalog, "SYNTH_CARD", "T0",
            "unloaded catalog must fail closed");
        AssertScoreUnchanged(
            emptyCatalog, loadedCatalog, "SYNTH_UNKNOWN", "T0",
            "unknown card must fail closed");
        AssertScoreUnchanged(
            emptyCatalog, loadedCatalog, "SYNTH_CARD", "T999",
            "unknown tier must fail closed");
        AssertScoreUnchanged(
            emptyCatalog, loadedCatalog, "SYNTH_NULL_CARD", "T0",
            "missing raw weight must fail closed");
        AssertScoreUnchanged(
            emptyCatalog, loadedCatalog, "SYNTH_CARD", null,
            "missing current guide tier must fail closed");
    }

    private static void AssertScoreUnchanged(
        object emptyCatalog, object loadedCatalog,
        string cardName, string tier, string label)
    {
        var baseline = Score(NewEngine(emptyCatalog), cardName, tier);
        var actual = Score(NewEngine(loadedCatalog), cardName, tier);
        AssertNear(baseline, actual, 0.0001, label);
    }

    private static void AssertDefaultGuideTierIsNull()
    {
        var state = new GameState();
        AssertTrue(
            state.CurrentCompGuideTier == null,
            "new GameState must default CurrentCompGuideTier to null");
    }

    private static void AssertLoadedCatalogNullTierFailClosedAcrossDecisionPaths(
        object emptyCatalog, object loadedCatalog)
    {
        var emptyScores = Score(NewEngine(emptyCatalog), "SYNTH_CARD", null);
        var loadedScores = Score(NewEngine(loadedCatalog), "SYNTH_CARD", null);
        AssertNear(emptyScores, loadedScores, 0.0001,
            "loaded catalog with null tier must not change shop card score");

        var emptyBuyState = BuyState();
        SetTier(emptyBuyState, null);
        var loadedBuyState = BuyState();
        SetTier(loadedBuyState, null);
        var emptyBuy = NewEngine(emptyCatalog)
            .GetRecommendedBuyIndices(emptyBuyState, 1);
        var loadedBuy = NewEngine(loadedCatalog)
            .GetRecommendedBuyIndices(loadedBuyState, 1);
        AssertEqual(emptyBuy.Count, loadedBuy.Count,
            "loaded catalog with null tier must not change buy index count");
        for (var i = 0; i < emptyBuy.Count; i++)
        {
            AssertEqual(emptyBuy[i], loadedBuy[i],
                "loaded catalog with null tier must not change buy index " + i);
        }

        var emptyActionState = BestActionState();
        SetTier(emptyActionState, null);
        var loadedActionState = BestActionState();
        SetTier(loadedActionState, null);
        var emptyAction = NewEngine(emptyCatalog).GetBestAction(emptyActionState);
        var loadedAction = NewEngine(loadedCatalog).GetBestAction(loadedActionState);
        AssertEqual(emptyAction.Type, loadedAction.Type,
            "loaded catalog with null tier must not change best action type");
        AssertEqual(emptyAction.TargetIndex, loadedAction.TargetIndex,
            "loaded catalog with null tier must not change best action target");
    }

    private static object CreateEmptyCatalog()
    {
        var type = typeof(DecisionEngine).Assembly.GetType(
            "BobCoach.Engine.CompGuideCatalog");
        if (type == null)
            throw new InvalidOperationException("CompGuideCatalog type not found");
        return Activator.CreateInstance(type, nonPublic: true);
    }

    private static object CreateLoadedCatalog()
    {
        var catalog = CreateEmptyCatalog();
        var method = catalog.GetType().GetMethod(
            "LoadFromJson", BindingFlags.Instance | BindingFlags.NonPublic);
        if (method == null)
            throw new InvalidOperationException("CompGuideCatalog.LoadFromJson missing");
        method.Invoke(catalog, new object[] { GuidesJson, WeightsJson });
        return catalog;
    }

    private static DecisionEngine NewEngine(object catalog)
    {
        var engine = new DecisionEngine();
        var field = typeof(DecisionEngine).GetField(
            "_compGuideCatalog", BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null)
            throw new InvalidOperationException("DecisionEngine._compGuideCatalog missing");
        field.SetValue(engine, catalog);
        return engine;
    }

    private static double Score(DecisionEngine engine, string cardName, string tier)
    {
        var state = SingleCardState(cardName, tier);
        var scores = engine.GetShopCardScores(state);
        AssertEqual(1, scores.Count,
            "shop score must contain synthetic card");
        return scores[0].Score;
    }

    private static GameState SingleCardState(string cardName, string tier)
    {
        var state = new GameState
        {
            GameActive = true,
            Gold = 3,
            TavernTier = 1,
            Turn = 1,
            Phase = "shop",
            ShopMinions = new List<MinionData>
            {
                new MinionData
                {
                    CardId = "SYNTH_CARD_ID",
                    CardName = cardName,
                    EntityId = 1,
                    Position = 0,
                    Tier = 1,
                    Attack = 2,
                    Health = 2,
                },
            },
        };
        SetTier(state, tier);
        return state;
    }

    private static GameState BuyState()
    {
        var state = new GameState
        {
            GameActive = true,
            Gold = 10,
            TavernTier = 1,
            Turn = 1,
            Phase = "shop",
            ShopMinions = new List<MinionData>
            {
                new MinionData
                {
                    CardId = "SYNTH_LOW_ID",
                    CardName = "SYNTH_LOW",
                    EntityId = 1,
                    Position = 0,
                    Tier = 2,
                    Attack = 4,
                    Health = 4,
                },
                new MinionData
                {
                    CardId = "SYNTH_CARD_ID",
                    CardName = "SYNTH_CARD",
                    EntityId = 2,
                    Position = 1,
                    Tier = 1,
                    Attack = 2,
                    Health = 2,
                },
            },
        };
        SetTier(state, "T0");
        return state;
    }

    private static GameState BestActionState()
    {
        var state = BuyState();
        state.BoardMinions = new List<MinionData>
        {
            new MinionData
            {
                CardId = "SYNTH_BOARD_ID",
                CardName = "SYNTH_BOARD",
                EntityId = 3,
                Position = 0,
                Tier = 2,
                Attack = 4,
                Health = 4,
            },
        };
        return state;
    }

    private static void SetTier(GameState state, string tier)
    {
        var field = typeof(GameState).GetField(
            "CurrentCompGuideTier", BindingFlags.Instance | BindingFlags.Public);
        if (field == null)
            throw new InvalidOperationException("GameState.CurrentCompGuideTier missing");
        field.SetValue(state, tier);
    }

    private static void AssertNear(
        double expected, double actual, double tolerance, string label)
    {
        if (Math.Abs(expected - actual) > tolerance)
            throw new InvalidOperationException(
                label + " expected=" + expected + " actual=" + actual);
        _assertions++;
    }

    private static void AssertEqual<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(
                label + " expected=" + expected + " actual=" + actual);
        _assertions++;
    }

    private static void AssertTrue(bool condition, string label)
    {
        if (!condition)
            throw new InvalidOperationException(label);
        _assertions++;
    }
}
