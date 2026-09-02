using System;
using BobCoach.Engine;

internal static class BattlegroundsHeroIdentityBehavior
{
    private static int Main()
    {
        try
        {
            AcceptsSkinHeroIds();
            NormalizesSkinHeroIds();
            RejectsPlaceholderHeroIds();
            Console.WriteLine("PASS battlegrounds hero identity accepts skins and normalizes base ids");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL " + ex.Message);
            return 1;
        }
    }

    private static void AcceptsSkinHeroIds()
    {
        AssertTrue(BattlegroundsHeroIdentity.IsEligibleHeroCardId(
            "TB_BaconShop_HERO_27_SKIN_F"), "TB skin hero accepted");
        AssertTrue(BattlegroundsHeroIdentity.IsEligibleHeroCardId(
            "BG21_HERO_020_SKIN_H"), "BG skin hero accepted");
        AssertTrue(BattlegroundsHeroIdentity.IsEligibleHeroCardId(
            "BG24_HERO_100_SKIN_E"), "BG skin hero accepted");
    }

    private static void NormalizesSkinHeroIds()
    {
        AssertEqual("TB_BaconShop_HERO_27",
            BattlegroundsHeroIdentity.NormalizeHeroCardId("TB_BaconShop_HERO_27_SKIN_F"),
            "TB skin base id");
        AssertEqual("BG21_HERO_020",
            BattlegroundsHeroIdentity.NormalizeHeroCardId("BG21_HERO_020_SKIN_H"),
            "BG skin base id");
        AssertEqual("BG24_HERO_100",
            BattlegroundsHeroIdentity.NormalizeHeroCardId("BG24_HERO_100_SKIN_E"),
            "BG skin base id");
    }

    private static void RejectsPlaceholderHeroIds()
    {
        AssertTrue(!BattlegroundsHeroIdentity.IsEligibleHeroCardId(
            "TB_BaconShop_HERO_PH"), "placeholder rejected");
        AssertTrue(!BattlegroundsHeroIdentity.IsEligibleHeroCardId(
            "BG21_HERO_020_SKIN_Hp"), "power suffix rejected");
    }

    private static void AssertTrue(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
    }

    private static void AssertEqual<T>(T expected, T actual, string label)
    {
        if (!expected.Equals(actual))
            throw new InvalidOperationException(label + " expected=" + expected + " actual=" + actual);
    }
}
