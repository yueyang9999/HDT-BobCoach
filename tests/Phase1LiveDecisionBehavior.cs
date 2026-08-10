using System;
using BobCoach.Engine;

internal static class Phase1LiveDecisionBehavior
{
    private static int _assertions;

    private static int Main()
    {
        try
        {
            EconomySnapshotBuildsAffordableActions();
            EconomySnapshotFailsClosedAtZeroGold();
            LiveSceneAnalyzerFlagsDesperateSurvival();
            Console.WriteLine("PASS phase1 live decision assertions=" + _assertions);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL " + ex.Message);
            return 1;
        }
    }

    private static void EconomySnapshotBuildsAffordableActions()
    {
        var economy = EconomySnapshot.Build(5, 5, 5, 1, true, true);
        AssertTrue(economy.CanUpgrade, "affordable upgrade");
        AssertTrue(economy.CanRefresh, "affordable refresh");
        AssertTrue(economy.CanBuyAny, "affordable buy");
        AssertEqual(0, economy.GoldAfterUpgrade, "upgrade consumes all gold");
        AssertEqual(4, economy.GoldAfterRefresh, "refresh consumes one gold");
        AssertEqual(3, economy.AffordableActions.Count, "affordable action list");
    }

    private static void EconomySnapshotFailsClosedAtZeroGold()
    {
        var economy = EconomySnapshot.Build(0, 3, 5, 1, true, false);
        AssertEqual(0, economy.LiveGold, "zero live gold");
        AssertFalse(economy.CanUpgrade, "upgrade not affordable");
        AssertFalse(economy.CanRefresh, "refresh not affordable");
        AssertFalse(economy.CanBuyAny, "buy not affordable");
        AssertEqual(0, economy.AffordableActions.Count, "no affordable actions");
    }

    private static void LiveSceneAnalyzerFlagsDesperateSurvival()
    {
        var scene = LiveSceneAnalyzer.Analyze(
            turn: 8,
            tavernTier: 4,
            health: 10,
            maxHealth: 30,
            boardMinionCount: 5,
            handMinionCount: 2,
            shopMinionCount: 5,
            boardPower: 20,
            opponentAveragePower: 40,
            normalRecruitAvailable: true,
            liveGold: 4,
            maxGold: 10,
            upgradeCost: 9,
            refreshCost: 1,
            manualRefreshAllowed: true,
            canBuyAny: true);
        AssertTrue(scene.Survival.LowHealth, "low health");
        AssertTrue(scene.Survival.Desperate, "desperate");
        AssertEqual("Desperate", scene.Survival.Level, "desperate level");
        AssertEqual(0.5, scene.Survival.PowerRatio, "power ratio");
        AssertEqual(3, scene.Economy.GoldAfterRefresh, "economy after refresh");
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

    private static void AssertEqual(int expected, int actual, string message)
    {
        _assertions++;
        if (expected != actual)
            throw new InvalidOperationException(
                message + ": expected=" + expected + " actual=" + actual);
    }

    private static void AssertEqual(double expected, double actual, string message)
    {
        _assertions++;
        if (Math.Abs(expected - actual) > 0.0001)
            throw new InvalidOperationException(
                message + ": expected=" + expected + " actual=" + actual);
    }

    private static void AssertEqual(string expected, string actual, string message)
    {
        _assertions++;
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            throw new InvalidOperationException(
                message + ": expected=" + expected + " actual=" + actual);
    }
}
