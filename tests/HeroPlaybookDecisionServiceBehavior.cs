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
            RhythmRenoLateGoldenizeOnly();
            RhythmSnakeT1OpenAndReuse();
            RhythmTrhUnlockThenBuddy();
            RhythmGalakrondUpgradeWhenAffordable();
            RhythmScabbsDiscoverStartsTurn2();
            RhythmHolmesGuessStartsTurn2();
            RhythmAkazamzarakFreeSecretEveryTurn();
            RhythmOthaarUnlocksTurn3();
            RhythmMillificentUnlocksTavernTier4();
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

    // ===== 英雄专属玩法节奏 (round67) =====
    // 雷诺·点金: 每局限1次, 板面成型前(≤T6)即使可负担也不该用。
    private static void RhythmRenoLateGoldenizeOnly()
    {
        var profile = new HeroPlaybookProfile
        {
            HeroCardId = "TB_BaconShop_HERO_41",
            HeroPowerCardIds = new List<string> { "TB_BaconShop_HP_046" },
            PowerType = "Active",
            PowerCost = 0,
            UsePurpose = "Buff",
            SpecialRule = "RENO",
            RequiresTarget = true,
            StrategyCoverage = "curated",
            Phases =
            {
                new HeroPlaybookPhase { MinTurn = 7, MaxTurn = 99, Priority = "High", GoldAtLeast = 0, TargetRequired = true },
            },
        };
        // T5 板面成型前: 即使有目标也不该点金
        var early = HeroPlaybookDecisionService.Decide(
            profile, "Active", 0, 6, 5, 2, 30, 5, 3, false, true);
        AssertFalse(early.ShouldUse, "Reno must not goldenize before turn 7");
        // T9 板面成型: 应使用
        var late = HeroPlaybookDecisionService.Decide(
            profile, "Active", 0, 6, 9, 4, 30, 5, 3, false, true);
        AssertTrue(late.ShouldUse, "Reno goldenizes once board matures (T9)");
    }

    // 蛇眼·好运当投: T1 开局必掷(正EV), 冷却后(>T1)金币够仍用。
    private static void RhythmSnakeT1OpenAndReuse()
    {
        var profile = new HeroPlaybookProfile
        {
            HeroCardId = "BG28_HERO_400",
            HeroPowerCardIds = new List<string> { "BG28_HERO_400p" },
            PowerType = "Active",
            PowerCost = 1,
            UsePurpose = "Economy",
            SpecialRule = "SNAKE_EYES",
            StrategyCoverage = "curated",
            Phases =
            {
                new HeroPlaybookPhase { MinTurn = 1, MaxTurn = 1, Priority = "Critical", GoldAtLeast = 1 },
                new HeroPlaybookPhase { MinTurn = 2, MaxTurn = 99, Priority = "High", GoldAtLeast = 1 },
            },
        };
        var t1 = HeroPlaybookDecisionService.Decide(
            profile, "Active", 1, 3, 1, 1, 30, 0, 4, false, false);
        AssertTrue(t1.ShouldUse, "snake eyes rolls on turn 1 (positive EV)");
        AssertTrue(t1.ActionValueBias >= 1.2, "snake T1 open is Critical");
        var t4 = HeroPlaybookDecisionService.Decide(
            profile, "Active", 1, 4, 4, 2, 30, 2, 4, false, false);
        AssertTrue(t4.ShouldUse, "snake eyes rerolls after cooldown when affordable");
    }

    // 牛头人酋长: T2 解锁后尽早发现伙伴, 持续补伙伴。
    private static void RhythmTrhUnlockThenBuddy()
    {
        var profile = new HeroPlaybookProfile
        {
            HeroCardId = "BG25_HERO_105",
            HeroPowerCardIds = new List<string> { "BG25_HERO_105p" },
            PowerType = "Active",
            PowerCost = 3,
            UsePurpose = "Resource",
            StrategyCoverage = "curated",
            Phases =
            {
                new HeroPlaybookPhase { MinTurn = 2, MaxTurn = 5, Priority = "Critical", GoldAtLeast = 3 },
                new HeroPlaybookPhase { MinTurn = 6, MaxTurn = 99, Priority = "High", GoldAtLeast = 3 },
            },
        };
        var t3 = HeroPlaybookDecisionService.Decide(
            profile, "Active", 3, 3, 3, 2, 30, 1, 4, false, false);
        AssertTrue(t3.ShouldUse, "tribe-leader discovers buddy as soon as affordable after unlock");
    }

    // 迦拉克隆·渴望进化(1费): 金币够就进化商店随从(节奏=可用即用)。
    private static void RhythmGalakrondUpgradeWhenAffordable()
    {
        var profile = new HeroPlaybookProfile
        {
            HeroCardId = "TB_BaconShop_HERO_02",
            HeroPowerCardIds = new List<string> { "TB_BaconShop_HP_011" },
            PowerType = "Active",
            PowerCost = 1,
            UsePurpose = "Resource",
            StrategyCoverage = "curated",
            Phases =
            {
                new HeroPlaybookPhase { MinTurn = 1, MaxTurn = 99, Priority = "High", GoldAtLeast = 1 },
            },
        };
        var broke = HeroPlaybookDecisionService.Decide(
            profile, "Active", 1, 0, 2, 1, 30, 0, 4, false, false);
        AssertFalse(broke.ShouldUse, "Galakrond blocked with no gold");
        var ok = HeroPlaybookDecisionService.Decide(
            profile, "Active", 1, 3, 3, 2, 30, 1, 4, false, false);
        AssertTrue(ok.ShouldUse, "Galakrond upgrades when affordable");
        AssertTrue(ok.ActionValueBias >= 0.8, "Galakrond window is High");
    }

    // 斯卡布斯·刀油(2费): T1 无对手战队可发现(窗口 T2 起)。
    private static void RhythmScabbsDiscoverStartsTurn2()
    {
        var profile = new HeroPlaybookProfile
        {
            HeroCardId = "BG21_HERO_010",
            HeroPowerCardIds = new List<string> { "BG21_HERO_010p" },
            PowerType = "Active",
            PowerCost = 2,
            UsePurpose = "Resource",
            StrategyCoverage = "curated",
            Phases =
            {
                new HeroPlaybookPhase { MinTurn = 2, MaxTurn = 99, Priority = "High", GoldAtLeast = 2 },
            },
        };
        var t1 = HeroPlaybookDecisionService.Decide(
            profile, "Active", 2, 3, 1, 1, 30, 0, 4, false, false);
        AssertFalse(t1.ShouldUse, "Scabbs does not discover before turn 2");
        var t3 = HeroPlaybookDecisionService.Decide(
            profile, "Active", 2, 4, 3, 2, 30, 2, 4, false, false);
        AssertTrue(t3.ShouldUse, "Scabbs discovers from turn 2 when affordable");
    }

    // 摩洛克·福尔摩斯(0费猜): T1 无上一场战斗可猜, T2 起每回合免费猜。
    private static void RhythmHolmesGuessStartsTurn2()
    {
        var profile = new HeroPlaybookProfile
        {
            HeroCardId = "BG23_HERO_303",
            HeroPowerCardIds = new List<string> { "BG23_HERO_303p2" },
            PowerType = "Active",
            PowerCost = 0,
            UsePurpose = "Economy",
            StrategyCoverage = "curated",
            Phases =
            {
                new HeroPlaybookPhase { MinTurn = 2, MaxTurn = 99, Priority = "High", GoldAtLeast = 0 },
            },
        };
        var t1 = HeroPlaybookDecisionService.Decide(
            profile, "Active", 0, 3, 1, 1, 30, 0, 4, false, false);
        AssertFalse(t1.ShouldUse, "Holmes cannot guess on turn 1");
        var t2 = HeroPlaybookDecisionService.Decide(
            profile, "Active", 0, 3, 2, 1, 30, 1, 4, false, false);
        AssertTrue(t2.ShouldUse, "Holmes guesses from turn 2");
    }

    // 阿卡扎曼扎拉克(0费奥秘): 每回合免费放一个奥秘。
    private static void RhythmAkazamzarakFreeSecretEveryTurn()
    {
        var profile = new HeroPlaybookProfile
        {
            HeroCardId = "TB_BaconShop_HERO_21",
            HeroPowerCardIds = new List<string> { "TB_BaconShop_HP_020" },
            PowerType = "Active",
            PowerCost = 0,
            UsePurpose = "Resource",
            StrategyCoverage = "curated",
            Phases =
            {
                new HeroPlaybookPhase { MinTurn = 1, MaxTurn = 99, Priority = "High", GoldAtLeast = 0 },
            },
        };
        var t1 = HeroPlaybookDecisionService.Decide(
            profile, "Active", 0, 0, 1, 1, 30, 0, 4, false, false);
        AssertTrue(t1.ShouldUse, "Akazamzarak places a secret on turn 1 (free)");
    }

    // 大主教奥萨尔(0费, 第3回合解锁): T2 未解锁不使用, T3 起激活折扣。
    private static void RhythmOthaarUnlocksTurn3()
    {
        var profile = new HeroPlaybookProfile
        {
            HeroCardId = "BG31_HERO_006",
            HeroPowerCardIds = new List<string> { "BG31_HERO_006p" },
            PowerType = "Active",
            PowerCost = 0,
            UnlockTurn = 3,
            UsePurpose = "Economy",
            StrategyCoverage = "curated",
            Phases =
            {
                new HeroPlaybookPhase { MinTurn = 3, MaxTurn = 99, Priority = "High", GoldAtLeast = 0 },
            },
        };
        var t2 = HeroPlaybookDecisionService.Decide(
            profile, "Active", 0, 5, 2, 2, 30, 1, 4, false, false);
        AssertFalse(t2.ShouldUse, "Othaar locked before turn 3");
        var t4 = HeroPlaybookDecisionService.Decide(
            profile, "Active", 0, 5, 4, 3, 30, 2, 4, false, false);
        AssertTrue(t4.ShouldUse, "Othaar activates spell discount from turn 3");
    }

    // 米尔菲丝(1费, 等级4解锁): 3本未解锁, 4本起发现磁力机械。
    private static void RhythmMillificentUnlocksTavernTier4()
    {
        var profile = new HeroPlaybookProfile
        {
            HeroCardId = "TB_BaconShop_HERO_17",
            HeroPowerCardIds = new List<string> { "TB_BaconShop_HP_015" },
            PowerType = "Active",
            PowerCost = 1,
            UnlockTier = 4,
            UsePurpose = "Resource",
            StrategyCoverage = "curated",
            Phases =
            {
                new HeroPlaybookPhase { MinTurn = 1, MaxTurn = 99, Priority = "High", GoldAtLeast = 1 },
            },
        };
        var tier3 = HeroPlaybookDecisionService.Decide(
            profile, "Active", 1, 6, 6, 3, 30, 3, 4, false, false);
        AssertFalse(tier3.ShouldUse, "Millificent locked before tavern tier 4");
        var tier4 = HeroPlaybookDecisionService.Decide(
            profile, "Active", 1, 6, 6, 4, 30, 3, 4, false, false);
        AssertTrue(tier4.ShouldUse, "Millificent discovers magnetic mech at tier 4");
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
