using System;
using System.Collections.Generic;
using BobCoach.Engine;

internal static class EarlyTurnRecruitGateBehavior
{
    private static int _assertions;

    private static int Main()
    {
        try
        {
            var gate = new EarlyTurnRecruitGate();

            AssertTrue(gate.BlocksEarlyRecruit(
                1,
                EarlyTurnRecruitGate.AfkLegacyHeroCardId,
                EarlyTurnRecruitGate.AfkPowerCardId,
                null), "legacy hero with AFK power blocks T1");
            AssertTrue(gate.BlocksEarlyRecruit(
                2,
                EarlyTurnRecruitGate.AfkHeroCardId,
                "",
                null), "canonical AFK hero blocks T2");
            AssertTrue(gate.BlocksEarlyRecruit(
                1,
                "HERO",
                "",
                new[]
                {
                    new HeroPowerState { CardId = EarlyTurnRecruitGate.AfkPowerCardId },
                }), "current AFK power entity blocks recruit");
            AssertTrue(gate.BlocksEarlyRecruit(
                1,
                "HERO",
                "",
                new[]
                {
                    new HeroPowerState { SpecialRule = "AFK" },
                }), "resolved AFK special rule blocks recruit");
            AssertFalse(gate.BlocksEarlyRecruit(
                3,
                "HERO",
                "",
                new[]
                {
                    new HeroPowerState { SpecialRule = "AFK" },
                }), "AFK rule does not block after skip turns");
            AssertFalse(gate.BlocksEarlyRecruit(
                1,
                "HERO",
                "",
                new HeroPowerState[0]), "normal hero remains recruitable");

            gate.Observe(
                "HERO",
                new[]
                {
                    new HeroPowerState { SpecialRule = "AFK" },
                });
            AssertTrue(gate.BlocksEarlyRecruit(
                1, "HERO", "TB_BaconShop_UpdateDmgCap", null),
                "confirmed AFK survives hero power entity replacement");
            AssertFalse(gate.BlocksEarlyRecruit(
                1, "OTHER", "TB_BaconShop_UpdateDmgCap", null),
                "confirmed AFK is scoped to the observed hero");

            gate.Reset();
            AssertFalse(gate.BlocksEarlyRecruit(
                1, "HERO", "TB_BaconShop_UpdateDmgCap", null),
                "reset clears confirmed AFK state");

            Console.WriteLine("PASS early turn recruit gate assertions=" + _assertions);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL " + ex.Message);
            return 1;
        }
    }

    private static void AssertTrue(bool value, string label)
    {
        _assertions++;
        if (!value) throw new InvalidOperationException(label);
    }

    private static void AssertFalse(bool value, string label)
    {
        _assertions++;
        if (value) throw new InvalidOperationException(label);
    }
}
