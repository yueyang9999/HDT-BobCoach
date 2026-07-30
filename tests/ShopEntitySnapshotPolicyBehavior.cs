using System;
using System.Collections.Generic;
using BobCoach.Engine;

namespace HearthDb.Enums
{
    public enum CardType
    {
        INVALID = 0,
        GAME = 1,
        PLAYER = 2,
        HERO = 3,
        MINION = 4,
        SPELL = 5,
        BATTLEGROUND_SPELL = 42,
    }
}

internal static class ShopEntitySnapshotPolicyBehavior
{
    private static int Main()
    {
        Assert(ShopEntitySnapshotPolicy.IsChangeDetectionCandidate(HearthDb.Enums.CardType.MINION),
            "minion must trigger shop change detection");
        Assert(ShopEntitySnapshotPolicy.IsChangeDetectionCandidate(HearthDb.Enums.CardType.SPELL),
            "spell must trigger shop change detection");
        Assert(ShopEntitySnapshotPolicy.IsChangeDetectionCandidate(HearthDb.Enums.CardType.BATTLEGROUND_SPELL),
            "battleground spell must trigger shop change detection");
        Assert(!ShopEntitySnapshotPolicy.IsChangeDetectionCandidate(HearthDb.Enums.CardType.HERO),
            "non-shop card type must not trigger shop change detection");

        Assert(ShopEntitySnapshotPolicy.IsStateHashCandidate(HearthDb.Enums.CardType.MINION, 1),
            "live minion must enter state hash");
        Assert(!ShopEntitySnapshotPolicy.IsStateHashCandidate(HearthDb.Enums.CardType.MINION, 0),
            "zero-health minion must remain excluded from state hash");
        Assert(ShopEntitySnapshotPolicy.IsStateHashCandidate(HearthDb.Enums.CardType.SPELL, 0),
            "zero-health spell must enter state hash");
        Assert(ShopEntitySnapshotPolicy.IsStateHashCandidate(HearthDb.Enums.CardType.BATTLEGROUND_SPELL, 0),
            "zero-health battleground spell must enter state hash");
        Assert(!ShopEntitySnapshotPolicy.IsStateHashCandidate(HearthDb.Enums.CardType.HERO, 30),
            "non-shop card type must remain excluded from state hash");

        var detector = new PurchaseDetector();
        var minionIds = new List<int> { 101, 102, 103, 104 };
        detector.Snapshot(minionIds.Count, minionIds);
        var withLateSpell = new List<int> { 101, 102, 103, 104, 105 };
        Equal(ShopChangeType.Refreshed,
            detector.DetectChange(withLateSpell.Count, withLateSpell),
            "late tavern spell must invalidate the four-slot snapshot");

        detector.Snapshot(withLateSpell.Count, withLateSpell);
        Equal(ShopChangeType.Purchased,
            detector.DetectChange(4, new List<int> { 101, 103, 104, 105 }),
            "purchase behavior must remain unchanged");

        detector.Snapshot(7, new List<int> { 201, 202, 203, 204, 205, 206, 207 });
        Equal(ShopChangeType.Refreshed,
            detector.DetectChange(6, new List<int> { 301, 302, 303, 304, 305, 306 }),
            "a partial new refresh must not be misclassified as a purchase when its count is lower");

        Equal(ShopChangeType.Refreshed,
            detector.DetectChange(7, new List<int> { 301, 302, 303, 304, 305, 306, 307 }),
            "the final entity of a seven-slot refresh must remain classified as refresh");

        detector.Snapshot(7, new List<int> { 401, 402, 403, 404, 405, 406, 407 });
        Equal(ShopChangeType.Refreshed,
            detector.DetectChange(6, new List<int> { 401, 402, 501, 502, 503, 504 }),
            "a mixed old/new animation frame must remain classified as refresh");

        detector.Snapshot(4, new List<int> { 601, 602, 603, 604 });
        Equal(ShopChangeType.None,
            detector.DetectChange(4, new List<int> { 604, 602, 601, 603 }),
            "entity enumeration order alone must not trigger a refresh");

        Console.WriteLine("PASS shop entity snapshot policy");
        return 0;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Equal(ShopChangeType expected, ShopChangeType actual, string message)
    {
        if (actual != expected)
        {
            Console.Error.WriteLine("FAIL " + message + ": expected=" + expected + " actual=" + actual);
            Environment.Exit(1);
        }
    }
}
