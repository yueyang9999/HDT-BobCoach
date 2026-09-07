using System;
using BobCoach.Engine;

internal static class CombatPhaseSignalBehavior
{
    private static int _assertions;

    private static int Main()
    {
        try
        {
            HdtFlagDominates();
            PowerLogMainCombatCatchesWhenHdtLags();
            RecruitPhasesAreNotCombat();
            UnknownPhaseDegradesToHdtFlagOnly();
            NullPhaseIsSafe();
            Console.WriteLine("PASS combat phase signal assertions=" + _assertions);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL " + ex.Message);
            return 1;
        }
    }

    private static void Assert(bool cond, string what)
    {
        _assertions++;
        if (!cond) throw new Exception("CombatPhaseSignal: " + what);
    }

    // HDT flag=true 即战斗(Power.log 阶段可滞后)
    private static void HdtFlagDominates()
    {
        Assert(CombatPhaseSignal.IsInCombat("shop", true), "hdt=true shop");
        Assert(CombatPhaseSignal.IsInCombat("", true), "hdt=true empty");
        Assert(CombatPhaseSignal.IsInCombat("MAIN_ACTION", true), "hdt=true action(lagging)");
    }

    // I-20260907-18 核心: HDT flag 滞后(false)但 Power.log 已 MAIN_COMBAT → 判战斗拦截
    private static void PowerLogMainCombatCatchesWhenHdtLags()
    {
        Assert(CombatPhaseSignal.IsInCombat("MAIN_COMBAT", false), "powerlog combat + hdt lag");
    }

    // 招募各阶段不是战斗
    private static void RecruitPhasesAreNotCombat()
    {
        foreach (var ph in new[] { "MAIN_ACTION", "MAIN_START", "MAIN_READY", "MAIN_CLEANUP", "shop" })
            Assert(!CombatPhaseSignal.IsInCombat(ph, false), "recruit phase " + ph);
    }

    private static void UnknownPhaseDegradesToHdtFlagOnly()
    {
        Assert(!CombatPhaseSignal.IsInCombat("", false), "empty phase");
    }

    private static void NullPhaseIsSafe()
    {
        Assert(!CombatPhaseSignal.IsInCombat(null, false), "null phase");
    }
}
