using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BobCoach.Engine;

internal static class Season14RegistriesHarness
{
    private static int _assertions;
    private static ActivateRegistry _activate;
    private static LockboxRegistry _lockbox;
    private static FishbaitRegistry _fishbait;
    private static DarkGiftRegistry _darkGift;
    private static TrinketDefinitionRegistry _trinket;
    private static Season14RulesEvaluator _season14;
    private static CompGuideCatalog _compGuideCatalog;
    private static SeasonMechanicProfile _season14Profile;

    private static int Main()
    {
        try
        {
            RealConfiguredRegistriesLoad();
            ConfiguredRootIsSeason14Evidence();
            SeasonDataSelectorTests();
            MissingConfiguredRootFailsClosed();
            RealLookupsAndCoverage();
            Season14MinionPurchaseSourceTests();
            Season14MinionRegistryTests();
            SyntheticMinionRegistryRejectsInvalidPoolEntries();
            Season14TavernSpellPurchaseSourceTests();
            Season14TavernSpellRegistryTests();
            SyntheticTavernSpellRegistryRejectsInvalidPoolEntries();
            CompositeCardPurchaseFactSourceTests();
            RealSeasonMechanicProfile();
            SeasonMechanicProfileFailsClosed();
            RealSeason14RulesEvaluator();
            EmptyEvaluatorFailsClosed();
            Season14ExtractionGateContract();
            RealCompGuideCatalog();
            SyntheticCompGuideCatalog();
            CompGuideCatalogFailsClosed();
            BattlegroundsDuoCompGuideCatalogRejected();
            SyntheticAnomalyExclusion();
            Season14TrinketLifecycle();
            SyntheticTrinketFieldsAndLesser();
            SyntheticDarkGiftFields();
            BattlegroundsDuoIsRejected();
            CombatPoisonAndVenomMechanics();
            Console.WriteLine("PASS season14 registries assertions=" + _assertions);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL " + ex.Message);
            return 1;
        }
    }

    private static void RealConfiguredRegistriesLoad()
    {
        string root = Environment.GetEnvironmentVariable(BobCoachDataPaths.RootEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(root)) throw new Exception("BOB_COACH_DATA_ROOT is empty");
        string registryDir = Path.Combine(root, "generated", "registries");
        if (!Directory.Exists(registryDir)) throw new Exception("registry dir missing: " + registryDir);

        _activate = new ActivateRegistry();
        _activate.LoadFromConfiguredRoot();
        True(_activate.IsLoaded, "activate loaded");
        Equal(34, _activate.Count, "activate count");

        _lockbox = new LockboxRegistry();
        _lockbox.LoadFromConfiguredRoot();
        True(_lockbox.IsLoaded, "lockbox loaded");
        Equal(8, _lockbox.Count, "lockbox count");

        _fishbait = new FishbaitRegistry();
        _fishbait.LoadFromConfiguredRoot();
        True(_fishbait.IsLoaded, "fishbait loaded");
        Equal(6, _fishbait.Count, "fishbait count");

        _darkGift = new DarkGiftRegistry();
        _darkGift.LoadFromConfiguredRoot();
        True(_darkGift.IsLoaded,
            "dark gift loaded raw=" + _darkGift.Count
                + " official=" + _darkGift.OfficialGifts.Count
                + " offering=" + _darkGift.OfferingRows.Count
                + " rules=" + _darkGift.Rules.Count
                + " related=" + _darkGift.RelatedCards.Count);
        Equal(105, _darkGift.Count, "dark gift raw effect count");
        Equal(43, _darkGift.OfficialGifts.Count, "dark gift official gift count");
        Equal(8, _darkGift.OfferingRows.Count, "dark gift offering row count");
        Equal(3, _darkGift.Rules.Count, "dark gift rule section count");
        Equal(10, _darkGift.RelatedCards.Count, "dark gift related card count");
        True(_darkGift.OfficialGifts[0].Name.Length > 0, "official gift first name");
        True(_darkGift.Rules[0].Items.Count > 0, "official rules first items");

        _trinket = new TrinketDefinitionRegistry();
        _trinket.LoadFromConfiguredRoot();
        True(_trinket.IsLoaded, "trinket loaded");
        Equal(379, _trinket.Count, "trinket count");

        _compGuideCatalog = new CompGuideCatalog();
        _compGuideCatalog.LoadFromConfiguredRoot();
        True(_compGuideCatalog.IsLoaded, "comp guide catalog loaded");
        Equal(23, _compGuideCatalog.GuidesCount, "comp guide catalog guide count");
        Equal(138, _compGuideCatalog.CoreCardsCount, "comp guide catalog core card count");
        Equal(69, _compGuideCatalog.TrinketsCount, "comp guide catalog trinket count");

        _season14Profile = new SeasonMechanicProfile();
        _season14Profile.LoadFromConfiguredRoot();
    }

    private static void RealLookupsAndCoverage()
    {
        var activateSource = new ActivateFactSource(_activate);
        ActivateFact activate;
        True(activateSource.TryGet("BG36_180", out activate), "activate lookup");
        Equal("BG36_180", activate.CardId, "activate CardId");
        Equal("Living Prison", activate.NameEnUs, "activate NameEnUs");
        True(activate.TextEnUs.Length > 0, "activate TextEnUs");
        True(activate.TextZhCn.Length > 0, "activate TextZhCn");

        var lockboxSource = new LockboxFactSource(_lockbox);
        LockboxFact lockbox;
        True(lockboxSource.TryGet("BG36_520", out lockbox), "lockbox lookup");
        Equal("BG36_520", lockbox.CardId, "lockbox CardId");
        Equal("Bilgewater Breakout", lockbox.NameEnUs, "lockbox NameEnUs");

        var fishbaitSource = new FishbaitFactSource(_fishbait);
        FishbaitFact fishbait;
        True(fishbaitSource.TryGet("BG36_201", out fishbait), "fishbait lookup");
        Equal("BG36_201", fishbait.CardId, "fishbait CardId");
        Equal("Lurking Lionfish", fishbait.NameEnUs, "fishbait NameEnUs");

        var darkGiftSource = new DarkGiftFactSource(_darkGift);
        DarkGiftFact darkGift;
        True(darkGiftSource.TryGet("BG36_MidGameEffect_000t", out darkGift), "dark gift lookup");
        Equal("BG36_MidGameEffect_000t", darkGift.CardId, "dark gift CardId");
        Equal("Offensive Sacrifice", darkGift.NameEnUs, "dark gift NameEnUs");
        Equal("SPELL", darkGift.Type, "dark gift Type");

        var trinketSource = new TrinketDefinitionFactSource(_trinket);
        TrinketFact lesser;
        True(trinketSource.TryGet("BG30_MagicItem_301", out lesser), "lesser trinket lookup");
        True(lesser.IsLesser, "lesser trinket IsLesser");
        Equal("Eternal Portrait", lesser.NameEnUs, "lesser trinket NameEnUs");

        TrinketFact greater;
        True(trinketSource.TryGet("BG30_MagicItem_310", out greater), "greater trinket lookup");
        False(greater.IsLesser, "greater trinket IsLesser");

        Equal(0, _activate.VerifyCoverage(new[] { "BG36_180" }).Count, "activate coverage");
        Equal(0, _lockbox.VerifyCoverage(new[] { "BG36_520" }).Count, "lockbox coverage");
        Equal(0, _fishbait.VerifyCoverage(new[] { "BG36_201" }).Count, "fishbait coverage");
        Equal(0, _darkGift.VerifyCoverage(new[] { "BG36_MidGameEffect_000t" }).Count,
            "dark gift coverage");
    }

    private static void Season14MinionPurchaseSourceTests()
    {
        var source = new Season14MinionPurchaseFactSource();
        CardPurchaseFact fact;
        True(source.TryGet("BG36_241", out fact), "season14 minion source BG36_241");
        Equal("BG36_241", fact.CardId, "season14 minion source CardId");
        Equal(ShopItemKind.Minion, fact.Kind, "season14 minion source Kind");
        Equal(-1, fact.BaseCost, "season14 minion source BaseCost");
        False(source.TryGet("BG36_NOT_A_REAL_CARD", out fact),
            "season14 minion source unknown fails closed");
    }

    private static void Season14MinionRegistryTests()
    {
        var registry = new Season14MinionRegistry();
        registry.LoadFromConfiguredRoot();
        True(registry.IsLoaded, "season14 minion registry loaded");
        Equal(246, registry.Count, "season14 minion registry count");

        Season14MechanicCardFact fact;
        True(registry.TryGetPoolMinion("BG36_241", out fact),
            "season14 minion pool lookup");
        Equal("MINION", fact.Type, "season14 minion pool Type");
        Equal(0, fact.Cost, "season14 minion pool Cost");
        Equal(6, fact.TechLevel, "season14 minion pool TechLevel");
        True(fact.IsBattlegroundsPoolMinion,
            "season14 minion pool IsBattlegroundsPoolMinion");
        Equal(1, fact.Races.Count, "season14 minion pool Races count");
        Equal("DRAGON", fact.Races[0], "season14 minion pool Races first");
        False(registry.TryGetPoolMinion("BG36_NOT_A_REAL_CARD", out fact),
            "season14 minion pool unknown fails closed");
    }

    private static void Season14TavernSpellPurchaseSourceTests()
    {
        var source = new Season14TavernSpellPurchaseFactSource();
        CardPurchaseFact fact;
        True(source.TryGet("BG36_246", out fact), "season14 spell source BG36_246");
        Equal("BG36_246", fact.CardId, "season14 spell source CardId");
        Equal(ShopItemKind.TavernSpell, fact.Kind, "season14 spell source Kind");
        Equal(2, fact.BaseCost, "season14 spell source BaseCost");
        False(source.TryGet("BG36_NOT_A_REAL_CARD", out fact),
            "season14 spell source unknown fails closed");
    }

    private static void Season14TavernSpellRegistryTests()
    {
        var registry = new Season14TavernSpellRegistry();
        registry.LoadFromConfiguredRoot();
        True(registry.IsLoaded, "season14 spell registry loaded");
        Equal(75, registry.Count, "season14 spell registry count");

        Season14MechanicCardFact fact;
        True(registry.TryGetPoolSpell("BG36_246", out fact),
            "season14 spell pool lookup");
        Equal("BATTLEGROUND_SPELL", fact.Type, "season14 spell pool Type");
        Equal(2, fact.Cost, "season14 spell pool Cost");
        Equal(4, fact.TechLevel, "season14 spell pool TechLevel");
        True(fact.IsBattlegroundsPoolSpell,
            "season14 spell pool IsBattlegroundsPoolSpell");
        True(fact.BattlegroundsAssociatedRaces.Count == 1
            && fact.BattlegroundsAssociatedRaces[0] == "DRAGON",
            "season14 spell pool associated races");
        False(registry.TryGetPoolSpell("BG36_NOT_A_REAL_CARD", out fact),
            "season14 spell pool unknown fails closed");
    }

    private static void SyntheticTavernSpellRegistryRejectsInvalidPoolEntries()
    {
        const string json = @"{
  ""cards"": [{
    ""cardId"": ""BG36_SPELL_VALID"",
    ""type"": ""BATTLEGROUND_SPELL"",
    ""cost"": 3,
    ""techLevel"": 5,
    ""battlegroundsAssociatedRaces"": [""DRAGON""],
    ""isBattlegroundsPoolSpell"": true
  }, {
    ""cardId"": ""BG36_SPELL_BAD_TYPE"",
    ""type"": ""MINION"",
    ""cost"": 3,
    ""techLevel"": 5,
    ""isBattlegroundsPoolSpell"": true
  }, {
    ""cardId"": ""BG36_SPELL_BAD_COST"",
    ""type"": ""BATTLEGROUND_SPELL"",
    ""cost"": -1,
    ""techLevel"": 5,
    ""isBattlegroundsPoolSpell"": true
  }, {
    ""cardId"": ""BG36_SPELL_BAD_POOL"",
    ""type"": ""BATTLEGROUND_SPELL"",
    ""cost"": 3,
    ""techLevel"": 5,
    ""isBattlegroundsPoolSpell"": false
  }]
}";

        var registry = new Season14TavernSpellRegistry();
        registry.LoadFromJson(json);
        True(registry.IsLoaded, "synthetic spell registry loaded");
        Equal(4, registry.Count, "synthetic spell registry count");

        Season14MechanicCardFact fact;
        True(registry.TryGetPoolSpell("BG36_SPELL_VALID", out fact),
            "synthetic valid pool spell");
        Equal(1, fact.BattlegroundsAssociatedRaces.Count,
            "synthetic spell associated races parsed");
        Equal("DRAGON", fact.BattlegroundsAssociatedRaces[0],
            "synthetic spell associated races value");
        False(registry.TryGetPoolSpell("BG36_SPELL_BAD_TYPE", out fact),
            "synthetic non-spell rejected");
        False(registry.TryGetPoolSpell("BG36_SPELL_BAD_COST", out fact),
            "synthetic negative cost rejected");
        False(registry.TryGetPoolSpell("BG36_SPELL_BAD_POOL", out fact),
            "synthetic non-pool spell rejected");
    }

    private static void SyntheticMinionRegistryRejectsInvalidPoolEntries()
    {
        const string json = @"{
  ""cards"": [{
    ""cardId"": ""BG36_VALID"",
    ""type"": ""MINION"",
    ""cost"": 0,
    ""techLevel"": 3,
    ""race"": ""DRAGON"",
    ""races"": [""DRAGON""],
    ""isBattlegroundsPoolMinion"": true
  }, {
    ""cardId"": ""BG36_BAD_TYPE"",
    ""type"": ""SPELL"",
    ""cost"": 0,
    ""techLevel"": 3,
    ""races"": [],
    ""isBattlegroundsPoolMinion"": true
  }, {
    ""cardId"": ""BG36_BAD_COST"",
    ""type"": ""MINION"",
    ""cost"": 3,
    ""techLevel"": 3,
    ""races"": [],
    ""isBattlegroundsPoolMinion"": true
  }, {
    ""cardId"": ""BG36_BAD_TIER"",
    ""type"": ""MINION"",
    ""cost"": 0,
    ""techLevel"": 0,
    ""races"": [],
    ""isBattlegroundsPoolMinion"": true
  }, {
    ""cardId"": ""BG36_BAD_POOL"",
    ""type"": ""MINION"",
    ""cost"": 0,
    ""techLevel"": 3,
    ""races"": [],
    ""isBattlegroundsPoolMinion"": false
  }]
}";

        var registry = new Season14MinionRegistry();
        registry.LoadFromJson(json);
        True(registry.IsLoaded, "synthetic minion registry loaded");
        Equal(5, registry.Count, "synthetic minion registry count");

        Season14MechanicCardFact fact;
        True(registry.TryGetPoolMinion("BG36_VALID", out fact),
            "synthetic valid pool minion");
        Equal(1, fact.Races.Count, "synthetic minion Races parsed");
        Equal("DRAGON", fact.Races[0], "synthetic minion Races value");
        False(registry.TryGetPoolMinion("BG36_BAD_TYPE", out fact),
            "synthetic non-minion rejected");
        False(registry.TryGetPoolMinion("BG36_BAD_COST", out fact),
            "synthetic nonzero cost rejected");
        False(registry.TryGetPoolMinion("BG36_BAD_TIER", out fact),
            "synthetic invalid tier rejected");
        False(registry.TryGetPoolMinion("BG36_BAD_POOL", out fact),
            "synthetic non-pool minion rejected");
    }

    private static void CompositeCardPurchaseFactSourceTests()
    {
        var composite = new CompositeCardPurchaseFactSource(
            new SyntheticCardPurchaseFactSource("A", ShopItemKind.Minion, -1),
            new SyntheticCardPurchaseFactSource("B", ShopItemKind.TavernSpell, 3));

        CardPurchaseFact fact;
        True(composite.TryGet("A", out fact), "composite first source wins");
        Equal("A", fact.CardId, "composite first source CardId");
        Equal(ShopItemKind.Minion, fact.Kind, "composite first source Kind");
        Equal(-1, fact.BaseCost, "composite first source BaseCost");

        True(composite.TryGet("B", out fact), "composite second source fallback");
        Equal("B", fact.CardId, "composite fallback CardId");
        Equal(ShopItemKind.TavernSpell, fact.Kind, "composite fallback Kind");
        Equal(3, fact.BaseCost, "composite fallback BaseCost");

        False(composite.TryGet("C", out fact),
            "composite unknown fails closed");
    }

    private static void ConfiguredRootIsSeason14Evidence()
    {
        string expectedRoot = Path.GetFullPath(@"E:\HDT-BobCoach-evidence\season14");
        string configuredRoot =
            Environment.GetEnvironmentVariable(BobCoachDataPaths.RootEnvironmentVariable);
        Equal(expectedRoot, Path.GetFullPath(configuredRoot),
            "BOB_COACH_DATA_ROOT is season14 evidence root");
        Equal(expectedRoot, Path.GetFullPath(BobCoachDataPaths.Root),
            "BobCoachDataPaths.Root uses season14 evidence root");
        Equal(expectedRoot, Path.GetFullPath(_activate.ConfiguredRoot),
            "activate ConfiguredRoot");
        Equal(expectedRoot, Path.GetFullPath(_lockbox.ConfiguredRoot),
            "lockbox ConfiguredRoot");
        Equal(expectedRoot, Path.GetFullPath(_fishbait.ConfiguredRoot),
            "fishbait ConfiguredRoot");
        Equal(expectedRoot, Path.GetFullPath(_darkGift.ConfiguredRoot),
            "dark gift ConfiguredRoot");
        Equal(expectedRoot, Path.GetFullPath(_trinket.ConfiguredRoot),
            "trinket ConfiguredRoot");
        Equal(expectedRoot, Path.GetFullPath(_compGuideCatalog.ConfiguredRoot),
            "comp guide catalog ConfiguredRoot");
        Equal(expectedRoot, Path.GetFullPath(_season14Profile.ConfiguredRoot),
            "season14 profile ConfiguredRoot");

        string appDataRoot = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "bob-coach"));
        False(expectedRoot.Equals(appDataRoot, StringComparison.OrdinalIgnoreCase),
            "season14 root is not APPDATA bob-coach root");
        False(expectedRoot.StartsWith(
            appDataRoot + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase),
            "season14 root is not under APPDATA bob-coach root");
    }

    private static void MissingConfiguredRootFailsClosed()
    {
        string previous = Environment.GetEnvironmentVariable(
            BobCoachDataPaths.RootEnvironmentVariable);
        string previousSelector = Environment.GetEnvironmentVariable(
            BobCoachDataPaths.SeasonSelectorEnvironmentVariable);
        string expectedRoot = Path.GetFullPath(@"E:\HDT-BobCoach-evidence\season14");
        try
        {
            Environment.SetEnvironmentVariable(BobCoachDataPaths.RootEnvironmentVariable, null);
            Environment.SetEnvironmentVariable(
                BobCoachDataPaths.SeasonSelectorEnvironmentVariable, null);
            var activate = new ActivateRegistry();
            activate.LoadFromConfiguredRoot();
            False(activate.IsLoaded, "activate fails without configured root");
            Equal(null, activate.ConfiguredRoot,
                "activate does not fall back to APPDATA root");

            var trinket = new TrinketDefinitionRegistry();
            trinket.LoadFromConfiguredRoot();
            False(trinket.IsLoaded, "trinket fails without configured root");
            Equal(null, trinket.ConfiguredRoot,
                "trinket does not fall back to APPDATA root");

            var compGuide = new CompGuideCatalog();
            compGuide.LoadFromConfiguredRoot();
            True(compGuide.IsLoaded,
                "comp guide loads via default selector without configured root");
            Equal(expectedRoot, Path.GetFullPath(compGuide.ConfiguredRoot),
                "comp guide default selector root");

            var minion = new Season14MinionRegistry();
            minion.LoadFromConfiguredRoot();
            True(minion.IsLoaded,
                "minion registry loads via default selector without configured root");
            Equal(expectedRoot, Path.GetFullPath(minion.ConfiguredRoot),
                "minion registry default selector root");

            var spell = new Season14TavernSpellRegistry();
            spell.LoadFromConfiguredRoot();
            True(spell.IsLoaded,
                "spell registry loads via default selector without configured root");
            Equal(expectedRoot, Path.GetFullPath(spell.ConfiguredRoot),
                "spell registry default selector root");
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                BobCoachDataPaths.RootEnvironmentVariable, previous);
            Environment.SetEnvironmentVariable(
                BobCoachDataPaths.SeasonSelectorEnvironmentVariable, previousSelector);
        }
    }

    private static void SeasonDataSelectorTests()
    {
        string previousRoot = Environment.GetEnvironmentVariable(
            BobCoachDataPaths.RootEnvironmentVariable);
        string previousSelector = Environment.GetEnvironmentVariable(
            BobCoachDataPaths.SeasonSelectorEnvironmentVariable);
        string expectedRoot = Path.GetFullPath(@"E:\HDT-BobCoach-evidence\season14");
        try
        {
            Environment.SetEnvironmentVariable(
                BobCoachDataPaths.RootEnvironmentVariable, @"E:\HDT-BobCoach-evidence\season14");
            Environment.SetEnvironmentVariable(
                BobCoachDataPaths.SeasonSelectorEnvironmentVariable, null);

            var fromDataRoot = new SeasonDataSelector();
            string root = fromDataRoot.ResolveConfiguredRoot();
            Equal(expectedRoot, Path.GetFullPath(root),
                "selector resolves from data root parent");
            Equal("season14", fromDataRoot.SelectedSeasonId,
                "selector selected season from data root");
            Equal("36.2.248348", fromDataRoot.GameBuild,
                "selector game build from data root");
            Equal("0bcc979ae21df891", fromDataRoot.CardPoolIdentity,
                "selector card pool identity from data root");
            True(fromDataRoot.SelectorPath.EndsWith(
                "season_selector.json", StringComparison.OrdinalIgnoreCase),
                "selector path resolved");

            Environment.SetEnvironmentVariable(
                BobCoachDataPaths.SeasonSelectorEnvironmentVariable,
                @"E:\HDT-BobCoach-evidence\season_selector.json");
            var fromExplicitSelector = new SeasonDataSelector();
            root = fromExplicitSelector.ResolveConfiguredRoot();
            Equal(expectedRoot, Path.GetFullPath(root),
                "explicit selector path resolves");
            Equal("season14", fromExplicitSelector.SelectedSeasonId,
                "explicit selector selected season");

            Environment.SetEnvironmentVariable(
                BobCoachDataPaths.RootEnvironmentVariable, null);
            Environment.SetEnvironmentVariable(
                BobCoachDataPaths.SeasonSelectorEnvironmentVariable, null);
            var fromDefault = new SeasonDataSelector();
            root = fromDefault.ResolveConfiguredRoot();
            Equal(expectedRoot, Path.GetFullPath(root),
                "default selector resolves without configured root");
            Equal("season14", fromDefault.SelectedSeasonId,
                "default selector selected season");

            Environment.SetEnvironmentVariable(
                BobCoachDataPaths.SeasonSelectorEnvironmentVariable,
                Path.Combine(Path.GetTempPath(),
                    "bobcoach-missing-selector-" + Guid.NewGuid().ToString("N") + ".json"));
            var missing = new SeasonDataSelector();
            root = missing.ResolveConfiguredRoot();
            Equal(null, root,
                "missing explicit selector fails without APPDATA fallback");
            Equal(null, missing.SelectedSeasonId,
                "missing explicit selector leaves no season");
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                BobCoachDataPaths.RootEnvironmentVariable, previousRoot);
            Environment.SetEnvironmentVariable(
                BobCoachDataPaths.SeasonSelectorEnvironmentVariable, previousSelector);
        }
    }

    private static void RealCompGuideCatalog()
    {
        CompGuide firstGuide = _compGuideCatalog.Guides[0];
        CompGuide guide;
        True(_compGuideCatalog.TryGetGuide(firstGuide.Id, out guide),
            "real comp guide lookup");
        Equal(firstGuide.Id, guide.Id, "real comp guide Id");
        True(guide.Tribes.Count > 0, "real comp guide tribes");
        True(guide.CoreCards.Count > 0, "real comp guide core cards");
        True(guide.Trinkets.Count > 0, "real comp guide trinkets");
        True(guide.Strategy.Length > 0, "real comp guide strategy");

        CompGuideCardWeight core;
        True(_compGuideCatalog.TryGetWeight(
            CompGuideWeightKind.Core, "提图斯·瑞文戴尔", out core),
            "real core weight lookup");
        Equal("BG25_354", core.OfficialCardId, "real core OfficialCardId");
        Equal(8, core.RawWeight.Value, "real core RawWeight");
        Equal(8, core.HeuristicWeight.Value, "real core HeuristicWeight");

        CompGuideCardWeight trinket;
        True(_compGuideCatalog.TryGetWeight(
            CompGuideWeightKind.Trinket, "獠牙脚链", out trinket),
            "real trinket weight lookup");
        Equal("BG35_MagicItem_701", trinket.OfficialCardId,
            "real trinket OfficialCardId");
        Equal(1, trinket.RawWeight.Value, "real trinket RawWeight");
        Equal(1, trinket.HeuristicWeight.Value, "real trinket HeuristicWeight");

        CompGuideCardWeight missing;
        False(_compGuideCatalog.TryGetWeight(
            CompGuideWeightKind.Core, "不存在", out missing),
            "real unknown weight lookup fails");
    }

    private static void SyntheticCompGuideCatalog()
    {
        const string guidesJson = @"{
  ""guides"": [{
    ""id"": ""SYNTH_GUIDE"",
    ""name"": ""SYNTH_GUIDE_NAME"",
    ""tier"": ""T0"",
    ""tribes"": [""元素""],
    ""coreCards"": [""SYNTH_CARD_TEXT""],
    ""trinkets"": [""SYNTH_TRINKET_TEXT""],
    ""strategy"": ""SYNTH_STRATEGY""
  }]
}";
        const string weightsJson = @"{
  ""counts"": {
    ""guides"": 1,
    ""coreEntries"": 1,
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
      ""rawWeight"": 3,
      ""heuristicWeight"": 3,
      ""tierCounts"": { ""T0"": 1, ""T1"": 0, ""T2"": 0 }
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

        var catalog = new CompGuideCatalog();
        catalog.LoadFromJson(guidesJson, weightsJson);
        True(catalog.IsLoaded, "synthetic comp guide catalog loaded");
        Equal(1, catalog.GuidesCount, "synthetic comp guide count");
        Equal(1, catalog.CoreCardsCount, "synthetic core card count");
        Equal(1, catalog.TrinketsCount, "synthetic trinket count");

        CompGuide guide;
        True(catalog.TryGetGuide("SYNTH_GUIDE", out guide), "synthetic guide lookup");
        Equal("SYNTH_GUIDE_NAME", guide.Name, "synthetic guide Name");
        Equal("T0", guide.Tier, "synthetic guide Tier");
        Equal(1, guide.Tribes.Count, "synthetic guide tribes");
        Equal("SYNTH_CARD_TEXT", guide.CoreCards[0], "synthetic guide CoreCards");
        Equal("SYNTH_TRINKET_TEXT", guide.Trinkets[0], "synthetic guide Trinkets");
        Equal("SYNTH_STRATEGY", guide.Strategy, "synthetic guide Strategy");

        CompGuideCardWeight core;
        True(catalog.TryGetWeight(
            CompGuideWeightKind.Core, "SYNTH_CARD", out core),
            "synthetic core weight lookup");
        Equal("SYNTH_CARD_ID", core.OfficialCardId, "synthetic core OfficialCardId");
        Equal(3, core.RawWeight.Value, "synthetic core RawWeight");

        CompGuideCardWeight trinket;
        True(catalog.TryGetWeight(
            CompGuideWeightKind.Trinket, "SYNTH_TRINKET", out trinket),
            "synthetic trinket weight lookup");
        Equal("SYNTH_TRINKET_ID", trinket.OfficialCardId,
            "synthetic trinket OfficialCardId");
        False(trinket.RawWeight.HasValue, "synthetic trinket RawWeight stays null");
        False(trinket.HeuristicWeight.HasValue,
            "synthetic trinket HeuristicWeight stays null");

        int tierWeight;
        True(catalog.TryGetTierWeight("T0", out tierWeight), "synthetic tier T0");
        Equal(3, tierWeight, "synthetic tier T0 value");
        True(catalog.TryGetTierWeight("T1", out tierWeight), "synthetic tier T1");
        Equal(2, tierWeight, "synthetic tier T1 value");
        True(catalog.TryGetTierWeight("T2", out tierWeight), "synthetic tier T2");
        Equal(1, tierWeight, "synthetic tier T2 value");
        True(catalog.TryGetTierWeight("T3", out tierWeight), "synthetic tier T3");
        Equal(0, tierWeight, "synthetic tier T3 value");
        False(catalog.TryGetTierWeight("T4", out tierWeight),
            "synthetic unknown tier T4 fails closed");
    }

    private static void CompGuideCatalogFailsClosed()
    {
        const string guidesJson = @"{
  ""guides"": [{
    ""id"": ""SYNTH_GUIDE"",
    ""name"": ""SYNTH_GUIDE_NAME"",
    ""tier"": ""T0"",
    ""tribes"": [],
    ""coreCards"": [],
    ""trinkets"": [],
    ""strategy"": """"
  }]
}";
        const string weightsJson = @"{
  ""counts"": {
    ""guides"": 1,
    ""coreEntries"": 1,
    ""trinketEntries"": 1
  },
  ""coreCards"": {
    ""SYNTH_CARD"": {
      ""name"": ""SYNTH_CARD"",
      ""officialName"": ""SYNTH_CARD"",
      ""officialCardId"": ""SYNTH_CARD_ID"",
      ""occurrences"": 1,
      ""uniqueGuides"": 1,
      ""multiCopyGuides"": [],
      ""duplicateBonus"": 0,
      ""rawWeight"": 3,
      ""heuristicWeight"": 3,
      ""tierCounts"": { ""T0"": 1, ""T1"": 0, ""T2"": 0 }
    }
  },
  ""trinkets"": {
    ""SYNTH_TRINKET"": {
      ""name"": ""SYNTH_TRINKET"",
      ""officialName"": ""SYNTH_TRINKET"",
      ""officialCardId"": ""SYNTH_TRINKET_ID"",
      ""occurrences"": 1,
      ""uniqueGuides"": 1,
      ""multiCopyGuides"": [],
      ""duplicateBonus"": 0,
      ""rawWeight"": 1,
      ""heuristicWeight"": 1,
      ""tierCounts"": { ""T1"": 1 }
    }
  }
}";

        var unloaded = new CompGuideCatalog();
        False(unloaded.IsLoaded, "default comp guide catalog not loaded");
        CompGuide guide;
        False(unloaded.TryGetGuide("SYNTH_GUIDE", out guide),
            "default comp guide lookup fails");
        CompGuideCardWeight weight;
        False(unloaded.TryGetWeight(CompGuideWeightKind.Core, "SYNTH_CARD", out weight),
            "default comp guide weight lookup fails");
        int tierWeight;
        False(unloaded.TryGetTierWeight("T0", out tierWeight),
            "default comp guide tier lookup fails");

        var emptyGuides = new CompGuideCatalog();
        emptyGuides.LoadFromJson(@"{ ""guides"": [] }", weightsJson);
        False(emptyGuides.IsLoaded, "empty guides fail closed");

        var emptyWeights = new CompGuideCatalog();
        emptyWeights.LoadFromJson(guidesJson, @"{
  ""counts"": {
    ""guides"": 1,
    ""coreEntries"": 0,
    ""trinketEntries"": 0
  },
  ""coreCards"": {},
  ""trinkets"": {}
}");
        False(emptyWeights.IsLoaded, "empty weight sections fail closed");

        var badJson = new CompGuideCatalog();
        badJson.LoadFromJson("{not-json", weightsJson);
        False(badJson.IsLoaded, "bad guides json fails closed");

        var badWeights = new CompGuideCatalog();
        badWeights.LoadFromJson(guidesJson, "{not-json");
        False(badWeights.IsLoaded, "bad weights json fails closed");
    }

    private static void BattlegroundsDuoCompGuideCatalogRejected()
    {
        const string guidesJson = @"{
  ""guides"": [{
    ""id"": ""SYNTH_GUIDE"",
    ""name"": ""SYNTH_GUIDE_NAME"",
    ""tier"": ""T0"",
    ""tribes"": [],
    ""coreCards"": [""SYNTH_CARD_TEXT""],
    ""trinkets"": [],
    ""strategy"": ""SYNTH_BGDUO_STRATEGY""
  }]
}";
        const string weightsJson = @"{
  ""counts"": {
    ""guides"": 1,
    ""coreEntries"": 1,
    ""trinketEntries"": 1
  },
  ""coreCards"": {
    ""SYNTH_CARD"": {
      ""name"": ""SYNTH_CARD"",
      ""officialName"": ""SYNTH_CARD"",
      ""officialCardId"": ""BGDUO_SYNTH_CARD"",
      ""occurrences"": 1,
      ""uniqueGuides"": 1,
      ""multiCopyGuides"": [],
      ""duplicateBonus"": 0,
      ""rawWeight"": 3,
      ""heuristicWeight"": 3,
      ""tierCounts"": { ""T0"": 1 }
    }
  },
  ""trinkets"": {}
}";

        var fromGuide = new CompGuideCatalog();
        fromGuide.LoadFromJson(guidesJson, weightsJson);
        False(fromGuide.IsLoaded, "BGDUO guide string rejected");

        const string cleanGuidesJson = @"{
  ""guides"": [{
    ""id"": ""SYNTH_GUIDE"",
    ""name"": ""SYNTH_GUIDE_NAME"",
    ""tier"": ""T0"",
    ""tribes"": [],
    ""coreCards"": [],
    ""trinkets"": [],
    ""strategy"": """"
  }]
}";
        var fromWeight = new CompGuideCatalog();
        fromWeight.LoadFromJson(cleanGuidesJson, weightsJson);
        False(fromWeight.IsLoaded, "BGDUO weight string rejected");
    }

    private static void RealSeasonMechanicProfile()
    {
        True(_season14Profile.IsLoaded, "real season14 mechanic profile loaded");
        True(_season14Profile.IsMechanicEnabled("activate"),
            "real profile activates activate");
        True(_season14Profile.IsMechanicEnabled("lockbox"),
            "real profile activates lockbox");
        True(_season14Profile.IsMechanicEnabled("fishbait"),
            "real profile activates fishbait");
        True(_season14Profile.IsMechanicEnabled("darkGift"),
            "real profile activates darkGift");
        True(_season14Profile.IsMechanicEnabled("trinket"),
            "real profile activates trinket");
        False(_season14Profile.IsMechanicEnabled("anomaly"),
            "real profile deactivates anomaly");
    }

    private static void SeasonMechanicProfileFailsClosed()
    {
        const string validJson = @"{
  ""_meta"": {
    ""schemaVersion"": 1,
    ""season"": ""season14"",
    ""gameBuild"": ""36.2.248348"",
    ""cardPoolIdentity"": ""0bcc979ae21df891""
  },
  ""mechanisms"": {
    ""activate"": ""active"",
    ""lockbox"": ""active"",
    ""fishbait"": ""active"",
    ""darkGift"": ""active"",
    ""trinket"": ""active"",
    ""anomaly"": ""inactive""
  }
}";
        var valid = new SeasonMechanicProfile();
        valid.LoadFromJson(
            validJson, "season14", "36.2.248348", "0bcc979ae21df891");
        True(valid.IsLoaded, "synthetic profile loaded");
        True(valid.IsMechanicEnabled("trinket"), "synthetic trinket active");
        False(valid.IsMechanicEnabled("anomaly"), "synthetic anomaly inactive");
        False(valid.IsMechanicEnabled("unknown"), "unknown mechanism fails closed");

        var empty = new SeasonMechanicProfile();
        False(empty.IsLoaded, "empty profile not loaded");
        False(empty.IsMechanicEnabled("trinket"), "empty profile fails closed");

        var badJson = new SeasonMechanicProfile();
        badJson.LoadFromJson("{", "season14", "36.2.248348", "0bcc979ae21df891");
        False(badJson.IsLoaded, "bad profile JSON fails closed");

        const string missingMetaJson = @"{
  ""mechanisms"": {
    ""activate"": ""active"",
    ""lockbox"": ""active"",
    ""fishbait"": ""active"",
    ""darkGift"": ""active"",
    ""trinket"": ""active"",
    ""anomaly"": ""inactive""
  }
}";
        var missingMeta = new SeasonMechanicProfile();
        missingMeta.LoadFromJson(
            missingMetaJson, "season14", "36.2.248348", "0bcc979ae21df891");
        False(missingMeta.IsLoaded, "missing profile meta fails closed");

        const string schemaMismatchJson = @"{
  ""_meta"": {
    ""schemaVersion"": 2,
    ""season"": ""season14"",
    ""gameBuild"": ""36.2.248348"",
    ""cardPoolIdentity"": ""0bcc979ae21df891""
  },
  ""mechanisms"": {
    ""activate"": ""active"",
    ""lockbox"": ""active"",
    ""fishbait"": ""active"",
    ""darkGift"": ""active"",
    ""trinket"": ""active"",
    ""anomaly"": ""inactive""
  }
}";
        var schemaMismatch = new SeasonMechanicProfile();
        schemaMismatch.LoadFromJson(
            schemaMismatchJson, "season14", "36.2.248348", "0bcc979ae21df891");
        False(schemaMismatch.IsLoaded, "unsupported profile schema fails closed");

        const string seasonMismatchJson = @"{
  ""_meta"": {
    ""schemaVersion"": 1,
    ""season"": ""season15"",
    ""gameBuild"": ""36.2.248348"",
    ""cardPoolIdentity"": ""0bcc979ae21df891""
  },
  ""mechanisms"": {
    ""activate"": ""active"",
    ""lockbox"": ""active"",
    ""fishbait"": ""active"",
    ""darkGift"": ""active"",
    ""trinket"": ""active"",
    ""anomaly"": ""inactive""
  }
}";
        var seasonMismatch = new SeasonMechanicProfile();
        seasonMismatch.LoadFromJson(
            seasonMismatchJson, "season14", "36.2.248348", "0bcc979ae21df891");
        False(seasonMismatch.IsLoaded, "profile season mismatch fails closed");

        const string unknownMechanismJson = @"{
  ""_meta"": {
    ""schemaVersion"": 1,
    ""season"": ""season14"",
    ""gameBuild"": ""36.2.248348"",
    ""cardPoolIdentity"": ""0bcc979ae21df891""
  },
  ""mechanisms"": {
    ""activate"": ""active"",
    ""lockbox"": ""active"",
    ""fishbait"": ""active"",
    ""darkGift"": ""active"",
    ""trinket"": ""active"",
    ""anomaly"": ""inactive"",
    ""mystery"": ""active""
  }
}";
        var unknownMechanism = new SeasonMechanicProfile();
        unknownMechanism.LoadFromJson(
            unknownMechanismJson, "season14", "36.2.248348", "0bcc979ae21df891");
        False(unknownMechanism.IsLoaded, "unknown profile mechanism fails closed");

        const string incompleteMechanismJson = @"{
  ""_meta"": {
    ""schemaVersion"": 1,
    ""season"": ""season14"",
    ""gameBuild"": ""36.2.248348"",
    ""cardPoolIdentity"": ""0bcc979ae21df891""
  },
  ""mechanisms"": {
    ""activate"": ""active"",
    ""lockbox"": ""active"",
    ""fishbait"": ""active"",
    ""darkGift"": ""active"",
    ""trinket"": ""active""
  }
}";
        var incompleteMechanism = new SeasonMechanicProfile();
        incompleteMechanism.LoadFromJson(
            incompleteMechanismJson, "season14", "36.2.248348", "0bcc979ae21df891");
        False(incompleteMechanism.IsLoaded,
            "incomplete profile mechanism set fails closed");

        const string unknownStatusJson = @"{
  ""_meta"": {
    ""schemaVersion"": 1,
    ""season"": ""season14"",
    ""gameBuild"": ""36.2.248348"",
    ""cardPoolIdentity"": ""0bcc979ae21df891""
  },
  ""mechanisms"": {
    ""activate"": ""active"",
    ""lockbox"": ""active"",
    ""fishbait"": ""active"",
    ""darkGift"": ""active"",
    ""trinket"": ""enabled"",
    ""anomaly"": ""inactive""
  }
}";
        var unknownStatus = new SeasonMechanicProfile();
        unknownStatus.LoadFromJson(
            unknownStatusJson, "season14", "36.2.248348", "0bcc979ae21df891");
        False(unknownStatus.IsLoaded, "unknown profile status fails closed");

        const string bgduoJson = @"{
  ""_meta"": {
    ""schemaVersion"": 1,
    ""season"": ""season14"",
    ""gameBuild"": ""36.2.248348"",
    ""cardPoolIdentity"": ""0bcc979ae21df891""
  },
  ""mechanisms"": {
    ""activate"": ""active"",
    ""lockbox"": ""active"",
    ""fishbait"": ""active"",
    ""darkGift"": ""active"",
    ""trinket"": ""active"",
    ""anomaly"": ""bgduo""
  }
}";
        var bgduo = new SeasonMechanicProfile();
        bgduo.LoadFromJson(
            bgduoJson, "season14", "36.2.248348", "0bcc979ae21df891");
        False(bgduo.IsLoaded, "profile BGDUO rejected");

        string previousRoot = Environment.GetEnvironmentVariable(
            BobCoachDataPaths.RootEnvironmentVariable);
        string previousSelector = Environment.GetEnvironmentVariable(
            BobCoachDataPaths.SeasonSelectorEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(BobCoachDataPaths.RootEnvironmentVariable, null);
            Environment.SetEnvironmentVariable(
                BobCoachDataPaths.SeasonSelectorEnvironmentVariable,
                Path.Combine(Path.GetTempPath(),
                    "bobcoach-missing-profile-selector-"
                        + Guid.NewGuid().ToString("N") + ".json"));
            var missingRoot = new SeasonMechanicProfile();
            missingRoot.LoadFromConfiguredRoot();
            False(missingRoot.IsLoaded,
                "missing configured root profile fails closed");
            Equal(null, missingRoot.ConfiguredRoot,
                "missing configured root profile has no root");
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                BobCoachDataPaths.RootEnvironmentVariable, previousRoot);
            Environment.SetEnvironmentVariable(
                BobCoachDataPaths.SeasonSelectorEnvironmentVariable, previousSelector);
        }
    }

    private static void RealSeason14RulesEvaluator()
    {
        _season14 = new Season14RulesEvaluator();
        _season14.LoadFromConfiguredRoot();
        True(_season14.IsLoaded, "season14 evaluator loaded");
        True(_season14.IsMechanicEnabled("trinket"),
            "season14 evaluator trinket active");
        False(_season14.IsMechanicEnabled("anomaly"),
            "season14 evaluator anomaly inactive");
        False(_season14.IsMechanicEnabled("unknown"),
            "season14 evaluator unknown mechanism fails closed");
        Equal(1, _season14.ExcludedAnomalyIds.Count, "season14 excluded anomaly count");
        Equal("BG36_Anomaly_002", _season14.ExcludedAnomalyIds[0],
            "season14 excluded anomaly id");

        ActivateFact activate;
        True(_season14.TryGetActivate("BG36_180", out activate),
            "season14 activate lookup");
        Equal("Living Prison", activate.NameEnUs, "season14 activate NameEnUs");

        LockboxFact lockbox;
        True(_season14.TryGetLockbox("BG36_520", out lockbox),
            "season14 lockbox lookup");
        Equal("Bilgewater Breakout", lockbox.NameEnUs, "season14 lockbox NameEnUs");

        FishbaitFact fishbait;
        True(_season14.TryGetFishbait("BG36_201", out fishbait),
            "season14 fishbait lookup");
        Equal("Lurking Lionfish", fishbait.NameEnUs, "season14 fishbait NameEnUs");

        DarkGiftFact darkGift;
        True(_season14.TryGetDarkGift("BG36_MidGameEffect_000t", out darkGift),
            "season14 dark gift lookup");
        Equal("Offensive Sacrifice", darkGift.NameEnUs, "season14 dark gift NameEnUs");

        string tier;
        True(_season14.TryGetOfferingTier(3, out tier), "offering tier turn 3");
        Equal("Tier 2", tier, "offering tier turn 3 value");
        True(_season14.TryGetOfferingTier(4, out tier), "offering tier turn 4");
        Equal("Tier 2 or Tier 3", tier, "offering tier turn 4 value");
        True(_season14.TryGetOfferingTier(5, out tier), "offering tier turn 5");
        Equal("Tier 3", tier, "offering tier turn 5 value");
        True(_season14.TryGetOfferingTier(6, out tier), "offering tier turn 6");
        Equal("Tier 3 or Tier 4", tier, "offering tier turn 6 value");
        True(_season14.TryGetOfferingTier(7, out tier), "offering tier turn 7");
        Equal("Tier 4", tier, "offering tier turn 7 value");
        True(_season14.TryGetOfferingTier(8, out tier), "offering tier turn 8");
        Equal("Tier 4 or Tier 5", tier, "offering tier turn 8 value");
        True(_season14.TryGetOfferingTier(9, out tier), "offering tier turn 9");
        Equal("Tier 4 or Tier 5 or Tier 6", tier, "offering tier turn 9 value");
        True(_season14.TryGetOfferingTier(10, out tier), "offering tier turn 10");
        Equal("Tier 5 or Tier 6", tier, "offering tier turn 10 value");
        False(_season14.TryGetOfferingTier(0, out tier), "offering tier turn 0");
        False(_season14.TryGetOfferingTier(2, out tier), "offering tier turn 2");
        False(_season14.TryGetOfferingTier(11, out tier), "offering tier turn 11");

        IList<string> gifts;
        True(_season14.TryGetEligibleGifts(6, out gifts), "eligible gifts turn 6");
        True(gifts.Contains("Offensive Sacrifice"), "offensive eligible turn 6");
        True(_season14.TryGetEligibleGifts(9, out gifts), "eligible gifts turn 9");
        True(gifts.Contains("Offensive Sacrifice"), "offensive eligible turn 9");
        True(_season14.TryGetEligibleGifts(10, out gifts), "eligible gifts turn 10");
        False(gifts.Contains("Offensive Sacrifice"), "offensive ineligible turn 10");
        True(gifts.Contains("Persisting Horror"), "persisting eligible turn 10");
        False(gifts.Contains("Titanic Strength"), "titanic ineligible turn 10");
        True(_season14.TryGetEligibleGifts(11, out gifts), "eligible gifts turn 11");
        True(gifts.Contains("Persisting Horror"), "persisting eligible turn 11");
        True(gifts.Contains("Titanic Strength"), "titanic eligible turn 11");
    }

    private static void EmptyEvaluatorFailsClosed()
    {
        var evaluator = new Season14RulesEvaluator();
        False(evaluator.IsLoaded, "empty evaluator not loaded");
        False(evaluator.IsMechanicEnabled("trinket"),
            "empty evaluator trinket fails closed");
        False(evaluator.IsMechanicEnabled("anomaly"),
            "empty evaluator anomaly fails closed");

        ActivateFact activate;
        False(evaluator.TryGetActivate("BG36_180", out activate),
            "empty evaluator activate fails");
        LockboxFact lockbox;
        False(evaluator.TryGetLockbox("BG36_520", out lockbox),
            "empty evaluator lockbox fails");
        FishbaitFact fishbait;
        False(evaluator.TryGetFishbait("BG36_201", out fishbait),
            "empty evaluator fishbait fails");
        DarkGiftFact darkGift;
        False(evaluator.TryGetDarkGift("BG36_MidGameEffect_000t", out darkGift),
            "empty evaluator dark gift fails");

        string tier;
        False(evaluator.TryGetOfferingTier(3, out tier),
            "empty evaluator offering tier fails");
        IList<string> gifts;
        False(evaluator.TryGetEligibleGifts(6, out gifts),
            "empty evaluator eligible gifts fails");

        var unloadedRegistries = new Season14RulesEvaluator();
        unloadedRegistries.LoadFromRegistries(
            new ActivateRegistry(),
            new LockboxRegistry(),
            new FishbaitRegistry(),
            new DarkGiftRegistry());
        False(unloadedRegistries.IsLoaded, "unloaded registries evaluator not loaded");
        False(unloadedRegistries.IsMechanicEnabled("trinket"),
            "unloaded registries evaluator trinket fails closed");
        False(unloadedRegistries.TryGetActivate("BG36_180", out activate),
            "unloaded registries activate fails");
    }

    private static void Season14ExtractionGateContract()
    {
        True(Season14RulesEvaluator.IsMechanicActiveOrLegacy(
                null, "trinket"),
            "null evaluator keeps legacy extraction");
        True(Season14RulesEvaluator.IsMechanicActiveOrLegacy(
                new Season14RulesEvaluator(), "trinket"),
            "unloaded evaluator keeps legacy extraction");
        True(Season14RulesEvaluator.IsMechanicActiveOrLegacy(
                _season14, "trinket"),
            "loaded active profile keeps trinket extraction");
        False(Season14RulesEvaluator.IsMechanicActiveOrLegacy(
                _season14, "anomaly"),
            "loaded inactive profile disables anomaly extraction");
        False(Season14RulesEvaluator.IsMechanicActiveOrLegacy(
                _season14, "unknown"),
            "loaded unknown profile disables extraction");
    }

    private static void SyntheticAnomalyExclusion()
    {
        var registry = new AnomalyRegistry(new SyntheticAnomalyDefinitionSource());
        var resolved = AnomalyContext.Resolve(
            new[] { "BG36_Anomaly_002" },
            registry,
            false);
        Equal("BG36_Anomaly_002", resolved.PrimaryAnomalyId,
            "anomaly primary without exclusion");

        var filtered = AnomalyContext.Resolve(
            new[] { "BG36_Anomaly_002" },
            registry,
            false,
            _season14.ExcludedAnomalyIds);
        Equal("", filtered.PrimaryAnomalyId, "anomaly excluded from primary");
        False(filtered.HasAmbiguousPrimary, "excluded anomaly not ambiguous");
    }

    private static void Season14TrinketLifecycle()
    {
        var magicBatch = new PowerLogChoiceBatch
        {
            SourceCardId = "BG30_MagicItem_703t",
            ChoiceId = -1,
        };

        var legacy = new TrinketChoiceBatchLifecycle();
        var legacyBinding = legacy.Observe(magicBatch, 5);
        Equal(TrinketChoiceContext.AnomalyExtra, legacyBinding.Context,
            "legacy magic source anomaly extra");

        var season = new TrinketChoiceBatchLifecycle();
        var seasonBinding = season.ObserveSeason14(magicBatch, 5, _season14);
        Equal(TrinketChoiceContext.OtherTrinket, seasonBinding.Context,
            "season14 magic source other trinket");

        var scheduledLesserBatch = new PowerLogChoiceBatch
        {
            SourceCardId = "BG30_Trinket_001",
            ChoiceId = -1,
        };
        var scheduledLesser = new TrinketChoiceBatchLifecycle();
        var lesserBinding = scheduledLesser.ObserveSeason14(
            scheduledLesserBatch, 5, _season14);
        Equal(TrinketChoiceContext.ScheduledLesser, lesserBinding.Context,
            "season14 scheduled lesser context");
        Equal(6, lesserBinding.TargetTurn, "season14 scheduled lesser target turn");

        var scheduledGreaterBatch = new PowerLogChoiceBatch
        {
            SourceCardId = "BG30_Trinket_002",
            ChoiceId = -1,
        };
        var scheduledGreater = new TrinketChoiceBatchLifecycle();
        var greaterBinding = scheduledGreater.ObserveSeason14(
            scheduledGreaterBatch, 8, _season14);
        Equal(TrinketChoiceContext.ScheduledGreater, greaterBinding.Context,
            "season14 scheduled greater context");
        Equal(9, greaterBinding.TargetTurn, "season14 scheduled greater target turn");

        var unloaded = new TrinketChoiceBatchLifecycle();
        var unloadedBinding = unloaded.ObserveSeason14(
            magicBatch, 5, new Season14RulesEvaluator());
        Equal(TrinketChoiceContext.AnomalyExtra, unloadedBinding.Context,
            "unloaded evaluator falls back to legacy");

        const string inactiveTrinketProfileJson = @"{
  ""_meta"": {
    ""schemaVersion"": 1,
    ""season"": ""season14"",
    ""gameBuild"": ""36.2.248348"",
    ""cardPoolIdentity"": ""0bcc979ae21df891""
  },
  ""mechanisms"": {
    ""activate"": ""active"",
    ""lockbox"": ""active"",
    ""fishbait"": ""active"",
    ""darkGift"": ""active"",
    ""trinket"": ""inactive"",
    ""anomaly"": ""inactive""
  }
}";
        var inactiveTrinketProfile = new SeasonMechanicProfile();
        inactiveTrinketProfile.LoadFromJson(
            inactiveTrinketProfileJson,
            "season14",
            "36.2.248348",
            "0bcc979ae21df891");
        var inactiveTrinketEvaluator = new Season14RulesEvaluator();
        inactiveTrinketEvaluator.LoadFromRegistries(
            _activate,
            _lockbox,
            _fishbait,
            _darkGift,
            inactiveTrinketProfile);
        True(inactiveTrinketEvaluator.IsLoaded,
            "inactive trinket evaluator loaded");
        False(inactiveTrinketEvaluator.IsMechanicEnabled("trinket"),
            "inactive trinket evaluator disables trinket");

        var disabled = new TrinketChoiceBatchLifecycle();
        var disabledBinding = disabled.ObserveSeason14(
            scheduledLesserBatch, 5, inactiveTrinketEvaluator);
        Equal(null, disabledBinding,
            "inactive trinket profile prevents lifecycle binding");
        Equal(null, disabled.Current,
            "inactive trinket profile leaves lifecycle current null");
    }

    private static void SyntheticTrinketFieldsAndLesser()
    {
        const string json = @"{
  ""lesser"": [{
    ""cardId"": ""SYNTH_TRINKET_L"",
    ""name_en"": ""SYNTH_EN"",
    ""name_cn"": ""SYNTH_CN"",
    ""cost"": 1,
    ""score"": 0,
    ""text"": ""SYNTH_TEXT"",
    ""text_en"": ""SYNTH_EN_TEXT"",
    ""text_cn"": ""SYNTH_CN_TEXT"",
    ""effects"": [],
    ""tribes"": []
  }],
  ""greater"": [{
    ""cardId"": ""SYNTH_TRINKET_G"",
    ""name_en"": ""SYNTH_G_EN"",
    ""name_cn"": ""SYNTH_G_CN"",
    ""cost"": 2,
    ""score"": 0,
    ""text"": ""SYNTH_G_TEXT"",
    ""text_en"": ""SYNTH_G_EN_TEXT"",
    ""text_cn"": ""SYNTH_G_CN_TEXT"",
    ""effects"": [],
    ""tribes"": []
  }]
}";

        var registry = new TrinketDefinitionRegistry();
        registry.LoadFromJson(json);
        True(registry.IsLoaded, "synthetic trinket loaded");
        var source = new TrinketDefinitionFactSource(registry);

        TrinketFact lesser;
        True(source.TryGet("SYNTH_TRINKET_L", out lesser), "synthetic lesser lookup");
        True(lesser.IsLesser, "synthetic lesser IsLesser");
        Equal(1, lesser.Cost, "synthetic lesser Cost");
        Equal("SYNTH_EN_TEXT", lesser.TextEnUs, "synthetic lesser TextEnUs");
        Equal("SYNTH_CN_TEXT", lesser.TextZhCn, "synthetic lesser TextZhCn");

        TrinketFact greater;
        True(source.TryGet("SYNTH_TRINKET_G", out greater), "synthetic greater lookup");
        False(greater.IsLesser, "synthetic greater IsLesser");
        Equal("SYNTH_G_EN_TEXT", greater.TextEnUs, "synthetic greater TextEnUs");
        Equal("SYNTH_G_CN_TEXT", greater.TextZhCn, "synthetic greater TextZhCn");

        TrinketFact missing;
        False(source.TryGet("SYNTH_MISSING", out missing), "synthetic trinket missing");
    }

    private static void SyntheticDarkGiftFields()
    {
        const string json = @"{
  ""official"": {
    ""offeringTable"": {
      ""rows"": [{
        ""turn"": ""3"",
        ""minionTier"": ""Tier 2"",
        ""sourceRow"": 1
      }]
    },
    ""gifts"": [{
      ""name"": ""SYNTH_GIFT"",
      ""minimumTurn"": ""3"",
      ""maximumTurn"": ""-"",
      ""effect"": ""SYNTH_EFFECT"",
      ""sourceRow"": 1
    }],
    ""rules"": {
      ""0"": {
        ""title"": ""SYNTH_RULES"",
        ""items"": [""SYNTH_ITEM""]
      }
    }
  },
  ""rawEffects"": [{
    ""cardId"": ""SYNTH_DG_1"",
    ""name_en"": ""SYNTH_DG_EN"",
    ""name_cn"": ""SYNTH_DG_CN"",
    ""type"": ""SPELL"",
    ""text_en"": ""SYNTH_DG_EN_TEXT"",
    ""text_cn"": ""SYNTH_DG_CN_TEXT""
  }],
  ""relatedCards"": [{
    ""cardId"": ""SYNTH_RELATED_1"",
    ""dbfId"": 123,
    ""name_en"": ""SYNTH_RELATED_EN"",
    ""name_cn"": ""SYNTH_RELATED_CN"",
    ""type"": ""BATTLEGROUND_SPELL"",
    ""set"": ""BATTLEGROUNDS"",
    ""cardClass"": ""NEUTRAL"",
    ""cost"": 4,
    ""techLevel"": 3,
    ""attack"": null,
    ""health"": null,
    ""armor"": null,
    ""spellSchool"": ""TAVERN"",
    ""race"": """",
    ""races"": [],
    ""mechanics"": [],
    ""referencedTags"": [],
    ""text_en"": ""SYNTH_RELATED_EN_TEXT"",
    ""text_cn"": ""SYNTH_RELATED_CN_TEXT"",
    ""isBattlegroundsPoolSpell"": false
  }]
}";

        var registry = new DarkGiftRegistry();
        registry.LoadFromJson(json);
        True(registry.IsLoaded, "synthetic dark gift loaded");
        Equal(1, registry.Count, "synthetic dark gift raw count");
        Equal(1, registry.OfficialGifts.Count, "synthetic dark gift official count");
        Equal(1, registry.OfferingRows.Count, "synthetic dark gift offering count");
        Equal(1, registry.Rules.Count, "synthetic dark gift rule count");
        Equal(1, registry.RelatedCards.Count, "synthetic dark gift related count");
        Equal("Tier 2", registry.OfferingRows[0].MinionTier, "synthetic dark gift MinionTier");
        Equal("SYNTH_GIFT", registry.OfficialGifts[0].Name, "synthetic dark gift Name");
        Equal("SYNTH_RULES", registry.Rules[0].Title, "synthetic dark gift Rules Title");

        var source = new DarkGiftFactSource(registry);
        DarkGiftFact fact;
        True(source.TryGet("SYNTH_DG_1", out fact), "synthetic dark gift lookup");
        Equal("SYNTH_DG_EN_TEXT", fact.TextEnUs, "synthetic dark gift TextEnUs");
        Equal("SYNTH_DG_CN_TEXT", fact.TextZhCn, "synthetic dark gift TextZhCn");
    }

    private static void BattlegroundsDuoIsRejected()
    {
        const string activateJson = @"{
  ""cards"": [{
    ""cardId"": ""BGDUO_ACTIVATE_000"",
    ""name_en"": ""Duo"",
    ""name_cn"": ""Duo"",
    ""type"": ""MINION""
  }]
}";
        var activate = new ActivateRegistry();
        activate.LoadFromJson(activateJson);
        False(activate.IsLoaded, "BGDUO activate rejected");
        Equal(0, activate.Count, "BGDUO activate count");

        const string darkGiftJson = @"{
  ""official"": {
    ""offeringTable"": {
      ""rows"": [{
        ""turn"": ""3"",
        ""minionTier"": ""Tier 2"",
        ""sourceRow"": 1
      }]
    },
    ""gifts"": [{
      ""name"": ""Gift"",
      ""minimumTurn"": ""3"",
      ""maximumTurn"": ""-"",
      ""effect"": ""Effect"",
      ""sourceRow"": 1
    }],
    ""rules"": {
      ""0"": {
        ""title"": ""Rules"",
        ""items"": [""Item""]
      }
    }
  },
  ""rawEffects"": [{
    ""cardId"": ""BGDUO_DARK_000"",
    ""name_en"": ""Duo"",
    ""name_cn"": ""Duo"",
    ""type"": ""SPELL"",
    ""text_en"": ""Text"",
    ""text_cn"": ""Text""
  }],
  ""relatedCards"": [{
    ""cardId"": ""SYNTH_RELATED_1"",
    ""dbfId"": 123,
    ""name_en"": ""Related"",
    ""name_cn"": ""Related"",
    ""type"": ""SPELL"",
    ""set"": ""BATTLEGROUNDS"",
    ""cardClass"": ""NEUTRAL"",
    ""cost"": 0,
    ""techLevel"": 0,
    ""attack"": null,
    ""health"": null,
    ""armor"": null,
    ""spellSchool"": """",
    ""race"": """",
    ""races"": [],
    ""mechanics"": [],
    ""referencedTags"": [],
    ""text_en"": ""Text"",
    ""text_cn"": ""Text"",
    ""isBattlegroundsPoolSpell"": false
  }]
}";
        var darkGift = new DarkGiftRegistry();
        darkGift.LoadFromJson(darkGiftJson);
        False(darkGift.IsLoaded, "BGDUO dark gift rejected");
        Equal(0, darkGift.Count, "BGDUO dark gift count");

        const string trinketJson = @"{
  ""lesser"": [{
    ""cardId"": ""BGDUO_TRINKET_000"",
    ""name_en"": ""Duo"",
    ""name_cn"": ""Duo"",
    ""cost"": 0,
    ""score"": 0,
    ""text"": ""Text"",
    ""effects"": [],
    ""tribes"": []
  }],
  ""greater"": [{
    ""cardId"": ""SYNTH_TRINKET_G"",
    ""name_en"": ""Synth"",
    ""name_cn"": ""Synth"",
    ""cost"": 0,
    ""score"": 0,
    ""text"": ""Text"",
    ""effects"": [],
    ""tribes"": []
  }]
}";
        var trinket = new TrinketDefinitionRegistry();
        trinket.LoadFromJson(trinketJson);
        False(trinket.IsLoaded, "BGDUO trinket rejected");
        Equal(0, trinket.Count, "BGDUO trinket count");
    }

    private static void CombatPoisonAndVenomMechanics()
    {
        True(CombatEffects.GetHandlers("BG25_016").Rally != null,
            "season14 rally handler registered");

        var poisonAttacker = Unit("poison", 1, 5, poisonous: true);
        var poisonTarget = Unit("poisonTarget", 0, 5);
        ExecuteSingleHit(poisonAttacker, poisonTarget);
        Equal(0, poisonTarget.Health, "poison kills target");
        False(poisonTarget.Alive, "poison target dead");

        var venomAttacker = Unit("venom", 1, 5, venomous: true);
        var venomTarget = Unit("venomTarget", 0, 5);
        ExecuteSingleHit(venomAttacker, venomTarget);
        Equal(0, venomTarget.Health, "venom first hit kills target");
        True(venomAttacker.VenomousUsed, "venom consumed on first hit");
        False(venomTarget.Alive, "venom target dead");

        var shieldVenom = Unit("shieldVenom", 1, 5, venomous: true);
        var shieldTarget = Unit("shieldTarget", 0, 5, divineShield: true);
        ExecuteSingleHit(shieldVenom, shieldTarget);
        False(shieldTarget.DivineShield, "venom removes divine shield");
        True(shieldTarget.Alive, "venom shield target alive");
        True(shieldVenom.VenomousUsed, "venom consumed by divine shield");
        Equal(5, shieldTarget.Health, "venom shield target health");

        var spentVenom = Unit("spentVenom", 1, 5, venomous: true);
        spentVenom.VenomousUsed = true;
        var spentTarget = Unit("spentTarget", 0, 5);
        ExecuteSingleHit(spentVenom, spentTarget);
        Equal(4, spentTarget.Health, "spent venom deals attack damage");
        True(spentTarget.Alive, "spent venom target alive");

        var defenderVenom = Unit("defenderVenom", 5, 5, venomous: true);
        var normalAttacker = Unit("normalAttacker", 1, 10);
        ExecuteSingleHit(normalAttacker, defenderVenom);
        Equal(4, defenderVenom.Health, "venom defender takes normal damage");
        True(defenderVenom.VenomousUsed, "venom defender consumed on counterattack");
        Equal(0, normalAttacker.Health, "venom counterattack kills attacker");
        False(normalAttacker.Alive, "venom counterattack attacker dead");

        var cleaveVenom = Unit("cleaveVenom", 1, 10, venomous: true, cleave: true);
        var cleavePrimary = Unit("cleavePrimary", 0, 5);
        var cleaveAdjacent = Unit("cleaveAdjacent", 0, 5);
        var cleaveAttackers = new List<CombatUnit> { cleaveVenom };
        var cleaveDefenders = new List<CombatUnit> { cleaveAdjacent, cleavePrimary };
        var cleaveContext = NewContext(cleaveAttackers, cleaveDefenders);
        InvokeCombat("ExecuteCleave",
            cleaveVenom, cleavePrimary, cleaveAttackers, cleaveDefenders, cleaveContext);
        Equal(0, cleavePrimary.Health, "cleave primary venom kill");
        False(cleavePrimary.Alive, "cleave primary dead");
        Equal(0, cleaveAdjacent.Health, "cleave adjacent venom kill");
        False(cleaveAdjacent.Alive, "cleave adjacent dead");
        True(cleaveVenom.VenomousUsed, "cleave consumes venom");

        var rallyVenom = Unit("rallyVenom", 1, 10, venomous: true, rally: true);
        var rallyTarget = Unit("rallyTarget", 0, 5);
        var rallyAttackers = new List<CombatUnit> { rallyVenom };
        var rallyDefenders = new List<CombatUnit> { rallyTarget };
        var rallyContext = NewContext(rallyAttackers, rallyDefenders);
        InvokeCombat("DoAttack", rallyAttackers, rallyDefenders, rallyContext);
        Equal(0, rallyTarget.Health, "rally venom kills target");
        False(rallyTarget.Alive, "rally venom target dead");
        True(rallyVenom.VenomousUsed, "rally consumes venom");

        var rallyShieldVenom = Unit("rallyShieldVenom", 1, 10,
            venomous: true, rally: true);
        var rallyShieldTarget = Unit("rallyShieldTarget", 0, 5, divineShield: true);
        var rallyShieldAttackers = new List<CombatUnit> { rallyShieldVenom };
        var rallyShieldDefenders = new List<CombatUnit> { rallyShieldTarget };
        var rallyShieldContext = NewContext(rallyShieldAttackers, rallyShieldDefenders);
        InvokeCombat("DoAttack", rallyShieldAttackers, rallyShieldDefenders, rallyShieldContext);
        False(rallyShieldTarget.DivineShield, "rally shield removed");
        True(rallyShieldVenom.VenomousUsed, "rally shield consumes venom");
        True(rallyShieldTarget.Alive, "rally shield target alive");
        Equal(4, rallyShieldTarget.Health, "rally shield target melee damage");

        var startVenom = Unit("startVenom", 1, 10, venomous: true);
        var startTarget = Unit("startTarget", 0, 5);
        var startAttackers = new List<CombatUnit> { startVenom };
        var startDefenders = new List<CombatUnit> { startTarget };
        var startContext = NewContext(startAttackers, startDefenders);
        InvokeCombat("DoAttackSingle", startVenom, startAttackers, startDefenders, startContext);
        Equal(0, startTarget.Health, "start of combat venom kills target");
        False(startTarget.Alive, "start of combat venom target dead");
        True(startVenom.VenomousUsed, "start of combat consumes venom");
    }

    private static CombatUnit Unit(string id, int attack, int health,
        bool poisonous = false, bool venomous = false, bool divineShield = false,
        bool cleave = false, bool rally = false)
    {
        return new CombatUnit
        {
            CardId = "test_" + id,
            NameCn = id,
            Attack = attack,
            Health = health,
            MaxHealth = health,
            BaseAttack = attack,
            BaseHealth = health,
            Alive = true,
            WindfuryAttacksLeft = 1,
            MinionTypes = new List<string>(),
            Poisonous = poisonous,
            Venomous = venomous,
            DivineShield = divineShield,
            Cleave = cleave,
            HasRally = rally,
        };
    }

    private static CombatContext NewContext(
        List<CombatUnit> attackers, List<CombatUnit> defenders)
    {
        return new CombatContext(
            attackers,
            defenders,
            new Random(1),
            null,
            null,
            ActiveTrinketContext.Empty,
            ActiveTrinketContext.Empty);
    }

    private static void ExecuteSingleHit(CombatUnit attacker, CombatUnit target)
    {
        var attackers = new List<CombatUnit> { attacker };
        var defenders = new List<CombatUnit> { target };
        var context = NewContext(attackers, defenders);
        InvokeCombat("ExecuteSingleHit",
            attacker, target, attackers, defenders, context);
    }

    private static void InvokeCombat(string methodName, params object[] args)
    {
        var method = typeof(CombatSimulator).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (method == null) throw new Exception("combat method missing: " + methodName);
        method.Invoke(new CombatSimulator(), args);
    }

    private sealed class SyntheticAnomalyDefinitionSource : IAnomalyDefinitionSource
    {
        public bool TryGet(string cardId, out AnomalyDefinition definition)
        {
            if (string.Equals(cardId, "BG36_Anomaly_002", StringComparison.Ordinal))
            {
                definition = new AnomalyDefinition
                {
                    AnomalyCardId = "BG36_Anomaly_002",
                    Lifecycle = "primary",
                    Scope = "solo",
                    Rules = new List<AnomalyRegistry.TypedRule>(),
                };
                return true;
            }

            definition = null;
            return false;
        }
    }

    private sealed class SyntheticCardPurchaseFactSource : ICardPurchaseFactSource
    {
        private readonly string _cardId;
        private readonly ShopItemKind _kind;
        private readonly int _baseCost;

        internal SyntheticCardPurchaseFactSource(
            string cardId, ShopItemKind kind, int baseCost)
        {
            _cardId = cardId;
            _kind = kind;
            _baseCost = baseCost;
        }

        public bool TryGet(string cardId, out CardPurchaseFact fact)
        {
            fact = new CardPurchaseFact();
            if (!string.Equals(_cardId, cardId, StringComparison.Ordinal))
                return false;
            fact = new CardPurchaseFact
            {
                CardId = _cardId,
                Kind = _kind,
                BaseCost = _baseCost,
            };
            return true;
        }
    }

    private static void True(bool condition, string label)
    {
        if (!condition) throw new Exception(label + " expected true");
        _assertions++;
    }

    private static void False(bool condition, string label)
    {
        if (condition) throw new Exception(label + " expected false");
        _assertions++;
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception(label + " expected=" + expected + " actual=" + actual);
        _assertions++;
    }
}
