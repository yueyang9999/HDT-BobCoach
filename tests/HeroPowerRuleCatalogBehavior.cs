using System;
using BobCoach.Engine;

internal static class HeroPowerRuleCatalogBehavior
{
    private static int Main()
    {
        try
        {
            UtilityPowersAreNeverEnumerated();
            TargetRulesMatchKnownHeroPowers();
            CenariusPowerNeedsNoBoardTarget();
            CenariusPermanentGoldCapAdvances();
            UnknownPowerDoesNotAdvanceGoldCap();
            NextTurnGoldBattlecryIsHeldOnlyOnTurnOne();
            EudoraRulesAreKnown();
            ChromieRulesAreKnown();
            FinleyReplacementPowerSelectionIsKnown();
            Console.WriteLine("PASS hero power rule catalog assertions=" + _assertions);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL " + ex.Message);
            return 1;
        }
    }

    private static void UtilityPowersAreNeverEnumerated()
    {
        AssertTrue(HeroPowerRuleCatalog.IsUtilityPowerCardId(
            "TB_BaconShop_UpdateDmgCap"), "utility power id rejected");
        AssertFalse(HeroPowerRuleCatalog.IsUtilityPowerCardId(
            "TB_BaconShop_HP_028"), "real power id retained");
    }

    private static void TargetRulesMatchKnownHeroPowers()
    {
        AssertFalse(HeroPowerRuleCatalog.RequiresTarget(
            "TB_BaconShop_HERO_28", "TOCKI", false),
            "Tocki refresh has no board target");
        AssertTrue(HeroPowerRuleCatalog.RequiresTarget(
            "BG25_HERO_103", "TALON", false),
            "Talon chooses a friendly minion");
        AssertFalse(HeroPowerRuleCatalog.RequiresTarget(
            "TB_BaconShop_HERO_28", "TOCKI", true),
            "discover powers choose from the discover panel");
        AssertTrue(HeroPowerRuleCatalog.RequiresTarget(
            "TB_BaconShop_HERO_67", "summon_highest_hp", true),
            "Hooktusk removes a friendly minion before its discover panel");
    }

    private static void CenariusPowerNeedsNoBoardTarget()
    {
        AssertFalse(HeroPowerRuleCatalog.RequiresTarget(
            "BG32_HERO_001", "CENARIUS", false),
            "Cenarius max-gold power has no board target");
        AssertTrue(HeroPowerRuleCatalog.IsNoTargetSpecialRule(
            "CENARIUS", "BG32_HERO_001", "BG32_HERO_001p"),
            "Cenarius special rule is in the no-target catalog");
    }

    private static void CenariusPermanentGoldCapAdvances()
    {
        AssertEqual(11, HeroPowerRuleCatalog.AdvancePermanentGoldCap(
            "BG32_HERO_001p", 10),
            "Cenarius advances the permanent gold cap");
        AssertEqual(20, HeroPowerRuleCatalog.AdvancePermanentGoldCap(
            "BG32_HERO_001p", 20),
            "Cenarius gold cap remains bounded at 20");
    }

    private static void UnknownPowerDoesNotAdvanceGoldCap()
    {
        AssertEqual(10, HeroPowerRuleCatalog.AdvancePermanentGoldCap(
            "UNKNOWN_POWER", 10),
            "unknown powers do not change the gold cap");
    }

    private static void NextTurnGoldBattlecryIsHeldOnlyOnTurnOne()
    {
        AssertTrue(HeroPowerRuleCatalog.ShouldHoldNextTurnGoldBattlecry(
            1, "BG26_135"), "turn one busker held in hand");
        AssertTrue(HeroPowerRuleCatalog.ShouldHoldNextTurnGoldBattlecry(
            1, "BG26_135_G"), "turn one golden busker held in hand");
        AssertFalse(HeroPowerRuleCatalog.ShouldHoldNextTurnGoldBattlecry(
            2, "BG26_135"), "turn two busker can be played");
    }

    private static void FinleyReplacementPowerSelectionIsKnown()
    {
        AssertTrue(HeroPowerRuleCatalog.IsFinleySeedPower(
            "TB_BaconShop_HP_057"), "finley adventure seed recognized");
        AssertTrue(HeroPowerRuleCatalog.IsLuckyRollPower(
            "BG28_HERO_400p"), "lucky roll replacement recognized");
        AssertEqual(2000, HeroPowerRuleCatalog.GetPrimaryHeroPowerPreference(
            "TB_BaconShop_HERO_40", "BG28_HERO_400p", true),
            "replacement power preferred over finley seed");
        AssertEqual(-1000, HeroPowerRuleCatalog.GetPrimaryHeroPowerPreference(
            "TB_BaconShop_HERO_40", "TB_BaconShop_HP_057", true),
            "finley seed deprioritized after replacement appears");
        AssertEqual(0, HeroPowerRuleCatalog.GetPrimaryHeroPowerPreference(
            "TB_BaconShop_HERO_40", "TB_BaconShop_HP_057", false),
            "finley seed remains usable before replacement appears");
        AssertFalse(HeroPowerRuleCatalog.ShouldHoldNextTurnGoldBattlecry(
            1, "BG26_135", "BG28_HERO_400p"),
            "lucky roll can play busker on turn one");
    }

    private static void EudoraRulesAreKnown()
    {
        AssertTrue(HeroPowerRuleCatalog.IsEudoraHero(
            "TB_BaconShop_HERO_64"), "eudora base hero recognized");
        AssertTrue(HeroPowerRuleCatalog.IsEudoraHero(
            "TB_BaconShop_HERO_64_SKIN_E"), "eudora skin resolved to same rules");
        AssertTrue(HeroPowerRuleCatalog.IsEudoraPower(
            "TB_BaconShop_HP_074"), "eudora power recognized");
        AssertTrue(HeroPowerRuleCatalog.GetSpecialRuleOverride(
            "TB_BaconShop_HERO_64_SKIN_E",
            "TB_BaconShop_HP_074") == "DIG_GOLDEN",
            "eudora power gets dig rule override");
        AssertTrue(HeroPowerRuleCatalog.ShouldReserveGoldForEudora(
            4, 2), "eudora keeps reserving gold while digging");
        AssertFalse(HeroPowerRuleCatalog.ShouldReserveGoldForEudora(
            8, 0), "eudora stops reserving gold after dig completes");
        AssertFalse(HeroPowerRuleCatalog.RequiresTarget(
            "TB_BaconShop_HERO_64", "DIG_GOLDEN", false),
            "eudora dig power does not need a board target");
    }

    private static void ChromieRulesAreKnown()
    {
        AssertTrue(HeroPowerRuleCatalog.IsChromieHero(
            "BG34_HERO_001"), "chromie hero recognized");
        AssertTrue(HeroPowerRuleCatalog.IsChromiePower(
            "BG34_HERO_001p"), "chromie power recognized");
        AssertTrue(HeroPowerRuleCatalog.GetSpecialRuleOverride(
            "BG34_HERO_001", "BG34_HERO_001p") == "CHROMIE",
            "chromie power gets spell-refresh rule override");
        AssertTrue(HeroPowerRuleCatalog.GetHeroCardIdForPower(
            "TB_BaconShop_HERO_KelThuzad",
            "BG34_HERO_001p") == "BG34_HERO_001",
            "global recorder resolves chromie base hero from power id");
    }

    private static void AssertTrue(bool condition, string label)
    {
        _assertions++;
        if (!condition) throw new InvalidOperationException(label);
    }

    private static void AssertFalse(bool condition, string label)
    {
        _assertions++;
        if (condition) throw new InvalidOperationException(label);
    }

    private static void AssertEqual(int expected, int actual, string label)
    {
        _assertions++;
        if (expected != actual)
            throw new InvalidOperationException(
                label + " expected=" + expected + " actual=" + actual);
    }

    private static int _assertions;
}
