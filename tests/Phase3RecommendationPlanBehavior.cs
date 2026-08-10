using System;
using BobCoach.Engine;

internal static class Phase3RecommendationPlanBehavior
{
    private static int _assertions;

    private static int Main()
    {
        try
        {
            PlanHashIncludesLiveGoldAndAction();
            PlanHashChangesWhenMarkersChange();
            Console.WriteLine("PASS phase3 recommendation plan assertions=" + _assertions);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL " + ex.Message);
            return 1;
        }
    }

    private static void PlanHashIncludesLiveGoldAndAction()
    {
        var plan = new RecommendationPlan
        {
            Turn = 5,
            LiveGold = 4,
            RecommendedActionType = "BuyMinion",
            StatusHint = "拿→随从",
            ShopMarkerCardIds = new System.Collections.Generic.List<string> { "BG36_241" },
        };
        string hash = RecommendationPlan.ComputeHash(plan);
        AssertContains(hash, "5|4|BuyMinion", "hash includes live gold and action");
    }

    private static void PlanHashChangesWhenMarkersChange()
    {
        var first = new RecommendationPlan
        {
            Turn = 1,
            LiveGold = 3,
            RecommendedActionType = "Upgrade",
        };
        var second = new RecommendationPlan
        {
            Turn = 1,
            LiveGold = 3,
            RecommendedActionType = "Upgrade",
            UpgradeHintText = "升2本",
        };
        AssertFalse(RecommendationPlan.ComputeHash(first)
            == RecommendationPlan.ComputeHash(second), "hash must change with plan text");
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

    private static void AssertContains(string text, string value, string message)
    {
        _assertions++;
        if (text == null || text.IndexOf(value, StringComparison.Ordinal) < 0)
            throw new InvalidOperationException(message + " missing " + value);
    }
}
