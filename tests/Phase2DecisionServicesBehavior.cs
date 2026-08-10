using System;
using BobCoach.Engine;

internal static class Phase2DecisionServicesBehavior
{
    private static int _assertions;

    private static int Main()
    {
        try
        {
            UnifiedActionScorerRejectsUnaffordableActions();
            HeroSkillDecisionServicePassiveNeverPrompts();
            HeroSkillDecisionServiceActiveChecksTargets();
            DarkGiftDecisionServiceRespectsOfficialGates();
            LiveSafetyGateSuppressesRiskyUpgrade();
            Console.WriteLine("PASS phase2 decision services assertions=" + _assertions);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL " + ex.Message);
            return 1;
        }
    }

    private static void UnifiedActionScorerRejectsUnaffordableActions()
    {
        var unaffordable = UnifiedActionScorer.Score(
            "Upgrade", 5, 3, 10, 10, 30, 5, 2, 0.2, 0.0, false, false, false);
        AssertTrue(unaffordable.Score < 0, "unaffordable action score");
        AssertContains(unaffordable.Reasons, "insufficient-gold", "reason");

        var affordable = UnifiedActionScorer.Score(
            "BuyMinion", 3, 6, 10, 10, 30, 5, 2, 0.8, 0.0, false, false, false);
        AssertTrue(affordable.Score > 0, "affordable buy score");
    }

    private static void HeroSkillDecisionServicePassiveNeverPrompts()
    {
        var decision = HeroSkillDecisionService.Decide(
            "Passive", 0, 1, 1, 5, 3, 2, 3, 4, "None", true);
        AssertFalse(decision.ShouldUse, "passive must not prompt");
        AssertEqual("passive", decision.Reason, "passive reason");
    }

    private static void HeroSkillDecisionServiceActiveChecksTargets()
    {
        var noTarget = HeroSkillDecisionService.Decide(
            "Active", 1, 1, 1, 3, 3, 2, 0, 0, "Buff", false);
        AssertFalse(noTarget.ShouldUse, "buff with no board target");

        var valid = HeroSkillDecisionService.Decide(
            "Active", 1, 1, 1, 5, 3, 2, 3, 2, "Buff", true);
        AssertTrue(valid.ShouldUse, "buff with target");
    }

    private static void DarkGiftDecisionServiceRespectsOfficialGates()
    {
        var early = DarkGiftDecisionService.Decide(
            2, 5, 3, false, 10, 10, 30, 0.7, true);
        AssertFalse(early.ShouldOpen, "dark gift not before turn 3");

        var used = DarkGiftDecisionService.Decide(
            5, 5, 3, true, 10, 10, 30, 0.7, true);
        AssertFalse(used.ShouldOpen, "dark gift once per turn");

        var exhausted = DarkGiftDecisionService.Decide(
            5, 5, 0, false, 10, 10, 30, 0.7, true);
        AssertFalse(exhausted.ShouldOpen, "dark gift max uses");

        var noGold = DarkGiftDecisionService.Decide(
            5, 2, 3, false, 10, 10, 30, 0.7, true);
        AssertFalse(noGold.ShouldOpen, "dark gift gold gate");

        var good = DarkGiftDecisionService.Decide(
            5, 8, 3, false, 10, 10, 30, 0.9, true);
        AssertTrue(good.ShouldOpen, "dark gift high value");
    }

    private static void LiveSafetyGateSuppressesRiskyUpgrade()
    {
        var scene = LiveSceneAnalyzer.Analyze(
            turn: 6,
            tavernTier: 3,
            health: 10,
            maxHealth: 30,
            boardMinionCount: 4,
            handMinionCount: 1,
            shopMinionCount: 5,
            boardPower: 20,
            opponentAveragePower: 40,
            normalRecruitAvailable: true,
            liveGold: 8,
            maxGold: 10,
            upgradeCost: 8,
            refreshCost: 1,
            manualRefreshAllowed: true,
            canBuyAny: true);
        var safety = LiveSafetyGate.Evaluate(scene, 1, 3);
        AssertTrue(safety.SuppressUpgrade, "desperate weak board must suppress upgrade");
        AssertFalse(safety.SuppressRefresh, "refresh remains allowed");
        AssertFalse(safety.SuppressBuy, "buy remains allowed");
    }

    private static void AssertTrue(bool value, string message)
    {
        _assertions++;
        if (!value) throw new InvalidOperationException(message);
    }

    private static void AssertFalse(bool value, string message)
    {
        _assertions++;
        if (value) throw new InvalidOperationException(message);
    }

    private static void AssertEqual(string expected, string actual, string message)
    {
        _assertions++;
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            throw new InvalidOperationException(
                message + ": expected=" + expected + " actual=" + actual);
    }

    private static void AssertContains(System.Collections.Generic.IList<string> values,
        string expected, string message)
    {
        _assertions++;
        foreach (var value in values)
            if (string.Equals(value, expected, StringComparison.Ordinal)) return;
        throw new InvalidOperationException(message + " missing " + expected);
    }
}
