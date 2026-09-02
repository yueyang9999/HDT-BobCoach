using System;
using System.Collections.Generic;
using BobCoach.Engine;

internal static class HeroPlaybookDecisionServiceBehavior
{
    private static int _assertions;

    private static int Main()
    {
        try
        {
            CenariusEarlyEconomyShouldUse();
            CenariusCriticalHealthIsBlocked();
            CenariusStopsAfterCuratedWindow();
            ExhaustedPowerIsBlocked();
            DecreasingCostPowerMatchesWithRuntimeCost();
            DecreasingCostPowerStillBlockedWhenGoldBelowRuntimeCost();
            CostDecayBiasIncreasesWhenCheaper();
            TargetedPowerWithoutTargetIsBlocked();
            Console.WriteLine("PASS hero playbook decision service assertions=" + _assertions);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL " + ex.Message);
            return 1;
        }
    }

    private static void CenariusEarlyEconomyShouldUse()
    {
        var decision = HeroPlaybookDecisionService.Decide(
            CenariusProfile(),
            "Active",
            3,
            3,
            1,
            1,
            30,
            0,
            3,
            false,
            false);
        AssertTrue(decision.ShouldUse, "Cenarius early economy power is used");
        AssertTrue(decision.ActionValueBias > 0.8,
            "Cenarius early economy power receives a strong bounded bias");
        AssertEqual("early-economy", decision.Reason,
            "Cenarius reason comes from the matching phase");
    }

    private static void CenariusCriticalHealthIsBlocked()
    {
        var decision = HeroPlaybookDecisionService.Decide(
            CenariusProfile(),
            "Active",
            3,
            3,
            1,
            1,
            7,
            0,
            3,
            false,
            false);
        AssertFalse(decision.ShouldUse,
            "Cenarius does not override critical health");
    }

    private static void CenariusStopsAfterCuratedWindow()
    {
        var decision = HeroPlaybookDecisionService.Decide(
            CenariusProfile(),
            "Active",
            3,
            10,
            6,
            3,
            30,
            3,
            3,
            false,
            false);
        AssertFalse(decision.ShouldUse,
            "Cenarius is not used unconditionally after the curated window");
    }

    private static void ExhaustedPowerIsBlocked()
    {
        var decision = HeroPlaybookDecisionService.Decide(
            CenariusProfile(),
            "Active",
            3,
            3,
            1,
            1,
            30,
            0,
            3,
            true,
            false);
        AssertFalse(decision.ShouldUse,
            "exhausted hero powers are blocked");
    }

    private static void TargetedPowerWithoutTargetIsBlocked()
    {
        var profile = new HeroPlaybookProfile
        {
            HeroCardId = "TARGETED_HERO",
            HeroPowerCardIds = new List<string> { "TARGETED_POWER" },
            PowerType = "Active",
            PowerCost = 2,
            UsePurpose = "Buff",
            RequiresTarget = true,
            StrategyCoverage = "facts-only",
            Phases =
            {
                new HeroPlaybookPhase
                {
                    MinTurn = 1,
                    MaxTurn = 99,
                    Priority = "Normal",
                    GoldAtLeast = 2,
                    TargetRequired = true,
                },
            },
        };
        var decision = HeroPlaybookDecisionService.Decide(
            profile,
            "Active",
            2,
            3,
            1,
            1,
            30,
            0,
            0,
            false,
            false);
        AssertFalse(decision.ShouldUse,
            "targeted hero power requires a valid target");
    }

    // R248: cost-decreasing hero (Togwaggle: power -1 per turn) static PowerCost=11,
    // runtime cost=7 (turn 5), gold 8 -> should use (pre-fix GoldAtLeast=11 never matches)
    private static void DecreasingCostPowerMatchesWithRuntimeCost()
    {
        var profile = new HeroPlaybookProfile
        {
            HeroCardId = "BG23_HERO_305",
            HeroPowerCardIds = new List<string> { "BG23_HERO_305p" },
            PowerType = "Active",
            PowerCost = 11,
            UsePurpose = "Resource",
            StrategyCoverage = "facts-only",
            Phases =
            {
                new HeroPlaybookPhase
                {
                    MinTurn = 1,
                    MaxTurn = 99,
                    Priority = "Normal",
                    GoldAtLeast = 11,
                },
            },
        };
        var decision = HeroPlaybookDecisionService.Decide(
            profile, "Active", 7, 8, 5, 3, 25, 3, 4, false, false);
        AssertTrue(decision.ShouldUse, "decreasing-cost power matches with runtime cost 7");
        AssertEqual(7, decision.Cost, "decision cost reflects runtime cost");
    }

    private static void DecreasingCostPowerStillBlockedWhenGoldBelowRuntimeCost()
    {
        var profile = new HeroPlaybookProfile
        {
            HeroCardId = "BG23_HERO_305",
            HeroPowerCardIds = new List<string> { "BG23_HERO_305p" },
            PowerType = "Active",
            PowerCost = 11,
            UsePurpose = "Resource",
            StrategyCoverage = "facts-only",
            Phases =
            {
                new HeroPlaybookPhase
                {
                    MinTurn = 1,
                    MaxTurn = 99,
                    Priority = "Normal",
                    GoldAtLeast = 11,
                },
            },
        };
        var decision = HeroPlaybookDecisionService.Decide(
            profile, "Active", 7, 6, 5, 3, 25, 3, 4, false, false);
        AssertFalse(decision.ShouldUse, "decreasing-cost power blocked when gold < runtime cost");
    }

    // R253: COST_DECAY - cheaper power gets higher bias
    private static void CostDecayBiasIncreasesWhenCheaper()
    {
        var profile = new HeroPlaybookProfile
        {
            HeroCardId = "BG23_HERO_305",
            HeroPowerCardIds = new List<string> { "BG23_HERO_305p" },
            PowerType = "Active",
            PowerCost = 11,
            UsePurpose = "Resource",
            SpecialRule = "COST_DECAY",
            StrategyCoverage = "facts-only",
            Phases =
            {
                new HeroPlaybookPhase
                {
                    MinTurn = 1,
                    MaxTurn = 99,
                    Priority = "Normal",
                    GoldAtLeast = 11,
                },
            },
        };
        var expensive = HeroPlaybookDecisionService.Decide(
            profile, "Active", 11, 11, 5, 3, 25, 3, 4, false, false);
        var cheap = HeroPlaybookDecisionService.Decide(
            profile, "Active", 7, 8, 5, 3, 25, 3, 4, false, false);
        AssertTrue(expensive.ShouldUse && cheap.ShouldUse, "both affordable");
        AssertTrue(cheap.ActionValueBias > expensive.ActionValueBias,
            "cheaper cost-decay power gets higher bias");
    }

    private static HeroPlaybookProfile CenariusProfile()
    {
        return new HeroPlaybookProfile
        {
            HeroCardId = "BG32_HERO_001",
            HeroPowerCardIds =
                new List<string> { "BG32_HERO_001p" },
            PowerType = "Active",
            PowerCost = 3,
            UsePurpose = "Economy",
            SpecialRule = "CENARIUS",
            StrategyCoverage = "curated",
            Phases =
            {
                new HeroPlaybookPhase
                {
                    MinTurn = 1,
                    MaxTurn = 5,
                    Priority = "Critical",
                    GoldAtLeast = 3,
                    HealthAtLeast = 8,
                    TargetRequired = false,
                    Reason = "early-economy",
                },
            },
        };
    }

    private static void AssertTrue(bool condition, string label)
    {
        _assertions++;
        if (!condition)
            throw new InvalidOperationException(label);
    }

    private static void AssertFalse(bool condition, string label)
    {
        _assertions++;
        if (condition)
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
