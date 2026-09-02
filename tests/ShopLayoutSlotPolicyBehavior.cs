using System;
using System.Linq;
using BobCoach.Engine;

internal static class ShopLayoutSlotPolicyBehavior
{
    private static int Main()
    {
        var policy = new ShopLayoutSlotPolicy();

        policy.Reset(1);
        Equal(1, policy.LastRefreshTier,
            "a new game must start with the tier-one shop width");

        policy.MarkRefreshDetected();
        Equal(true, policy.ObserveShopTier(2, 5, 5),
            "a real refresh must consume the current tavern tier");
        Equal(2, policy.LastRefreshTier,
            "a tier-two refresh must advance the remembered shop tier");

        policy.ObservePurchase();
        Equal(false, policy.ObserveShopTier(3, 4, 5),
            "buying after upgrading must not masquerade as a refresh");
        Equal(2, policy.LastRefreshTier,
            "buying after upgrading must preserve the last real refresh tier");

        policy.BeginRefresh();
        Equal(false, policy.ObserveShopTier(3, 4, 5),
            "a frozen cross-turn shop must not consume the current tavern tier");
        Equal(2, policy.LastRefreshTier,
            "a frozen cross-turn shop must keep its original layout tier");

        policy.MarkRefreshDetected();
        Equal(true, policy.ObserveShopTier(3, 5, 5),
            "the next actual refresh must consume the upgraded tavern tier");
        Equal(3, policy.LastRefreshTier,
            "the next actual refresh must update the layout tier");

        policy.MarkRefreshDetected();
        Equal(false, policy.ObserveShopTier(6, 6, 7),
            "upgrade to tier six with old six-card shop must not advance layout");
        Equal(3, policy.LastRefreshTier,
            "old shop before a tier-six refresh must keep the previous layout tier");
        Equal(true, policy.ObserveShopTier(6, 7, 7),
            "tier-six refresh must consume the new slot count after full shop appears");
        Equal(6, policy.LastRefreshTier,
            "tier-six refresh must update layout to seven-slot width");

        policy.BeginRefresh();
        var lateRightSlot = policy.Resolve(5, new[] { 0, 1, 2, 3 }, false);
        Equal(5, lateRightSlot.LayoutCount,
            "T2 refresh with the rightmost entity still missing must keep five layout slots");
        Equal(false, lateRightSlot.UseDenseSlots,
            "incomplete refresh must preserve raw shop slots");
        Equal(true, policy.AwaitingCompleteShop,
            "partial refresh must remain in the waiting state");

        var lateMiddleSlot = policy.Resolve(5, new[] { 0, 1, 3, 4 }, false);
        Equal(5, lateMiddleSlot.LayoutCount,
            "refresh with a middle entity missing must keep the full layout width");
        Equal(false, lateMiddleSlot.UseDenseSlots,
            "refresh with a middle entity missing must not compress the gap");

        var complete = policy.Resolve(5, new[] { 0, 1, 2, 3, 4 }, false);
        Equal(5, complete.LayoutCount,
            "complete T2 shop must use its five visible cards");
        Equal(true, complete.UseDenseSlots,
            "complete shop may use dense slots for normal centering");
        Equal(false, policy.AwaitingCompleteShop,
            "observing the complete shop must end refresh waiting");

        policy.ObservePurchase();
        var afterPurchase = policy.Resolve(5, new[] { 0, 1, 2, 3 }, false);
        Equal(4, afterPurchase.LayoutCount,
            "confirmed purchase must center the four remaining cards");
        Equal(true, afterPurchase.UseDenseSlots,
            "confirmed purchase must compact remaining cards");

        policy.BeginRefresh();
        policy.ObservePurchase();
        var purchaseWhileWaiting = policy.Resolve(5, new[] { 0, 1, 2, 3 }, false);
        Equal(4, purchaseWhileWaiting.LayoutCount,
            "purchase during a partial snapshot must still center remaining cards");
        Equal(true, purchaseWhileWaiting.UseDenseSlots,
            "purchase during a partial snapshot must compact remaining cards");
        Equal(false, policy.AwaitingCompleteShop,
            "purchase during a partial snapshot must exit the waiting state");

        policy.BeginRefresh();
        var replenishing = policy.Resolve(5, new[] { 1, 3, 4 }, true);
        Equal(3, replenishing.LayoutCount,
            "replenishing tavern must follow actual visible card count");
        Equal(true, replenishing.UseDenseSlots,
            "replenishing tavern must compact live cards");
        Equal(false, policy.AwaitingCompleteShop,
            "replenishing tavern must not leave stale refresh waiting state");

        for (int tier = 1; tier <= 6; tier++)
        {
            int expectedSlots = tier <= 1 ? 4 : (tier <= 3 ? 5 : (tier <= 5 ? 6 : 7));
            var fullSlots = Enumerable.Range(0, expectedSlots).ToArray();
            var partialSlots = fullSlots.Take(expectedSlots - 1).ToArray();

            policy.BeginRefresh();
            var partialTurnStart = policy.Resolve(expectedSlots, partialSlots, false);
            Equal(expectedSlots, partialTurnStart.LayoutCount,
                "T" + tier + " partial turn-start snapshot must keep the theoretical layout width");
            Equal(false, partialTurnStart.UseDenseSlots,
                "T" + tier + " partial turn-start snapshot must preserve raw slots");

            var completeTurnStart = policy.Resolve(expectedSlots, fullSlots, false);
            Equal(expectedSlots, completeTurnStart.LayoutCount,
                "T" + tier + " complete turn-start snapshot must use the full layout width");
            Equal(true, completeTurnStart.UseDenseSlots,
                "T" + tier + " complete turn-start snapshot may use dense slots");

            policy.ObservePurchase();
            var confirmedPurchase = policy.Resolve(expectedSlots, partialSlots, false);
            Equal(expectedSlots - 1, confirmedPurchase.LayoutCount,
                "T" + tier + " confirmed purchase must center the remaining cards");
            Equal(true, confirmedPurchase.UseDenseSlots,
                "T" + tier + " confirmed purchase must use dense slots");
        }

        Console.WriteLine("PASS shop layout slot policy");
        return 0;
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!Equals(expected, actual))
            throw new InvalidOperationException(message + ": expected=" + expected + " actual=" + actual);
    }
}
