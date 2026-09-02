using System;
using System.Collections.Generic;
using System.Linq;

namespace BobCoach.Engine
{
    internal sealed class ShopLayoutDecision
    {
        public int LayoutCount { get; private set; }
        public bool UseDenseSlots { get; private set; }

        public ShopLayoutDecision(int layoutCount, bool useDenseSlots)
        {
            LayoutCount = Math.Max(1, Math.Min(7, layoutCount));
            UseDenseSlots = useDenseSlots;
        }
    }

    internal sealed class ShopLayoutSlotPolicy
    {
        private bool _refreshTierUpdatePending;

        public bool AwaitingCompleteShop { get; private set; }
        public int LastRefreshTier { get; private set; }

        public ShopLayoutSlotPolicy()
        {
            Reset(1);
        }

        public void Reset(int initialTavernTier)
        {
            LastRefreshTier = NormalizeTier(initialTavernTier);
            _refreshTierUpdatePending = false;
            AwaitingCompleteShop = true;
        }

        public void BeginRefresh()
        {
            AwaitingCompleteShop = true;
        }

        public void MarkRefreshDetected()
        {
            AwaitingCompleteShop = true;
            _refreshTierUpdatePending = true;
        }

        public bool ObserveShopTier(
            int currentTavernTier,
            int visibleCount,
            int expectedSlotCount)
        {
            if (!_refreshTierUpdatePending
                || visibleCount <= 0
                || expectedSlotCount <= 0
                || visibleCount < expectedSlotCount)
                return false;

            LastRefreshTier = NormalizeTier(currentTavernTier);
            _refreshTierUpdatePending = false;
            return true;
        }

        public void ObservePurchase()
        {
            AwaitingCompleteShop = false;
        }

        public ShopLayoutDecision Resolve(int tierSlotCount, IEnumerable<int> rawSlots,
            bool replenishingShopActive)
        {
            int expectedCount = Math.Max(1, Math.Min(7, tierSlotCount));
            var slots = (rawSlots ?? Enumerable.Empty<int>())
                .Where(slot => slot >= 0)
                .ToList();
            int visibleCount = slots.Count;

            if (replenishingShopActive)
            {
                AwaitingCompleteShop = false;
                return new ShopLayoutDecision(Math.Max(1, visibleCount), true);
            }

            if (AwaitingCompleteShop && visibleCount < expectedCount)
            {
                int maxRawSlot = slots.Count > 0 ? slots.Max() : -1;
                return new ShopLayoutDecision(Math.Max(expectedCount, maxRawSlot + 1), false);
            }

            if (AwaitingCompleteShop && visibleCount >= expectedCount)
                AwaitingCompleteShop = false;

            return new ShopLayoutDecision(Math.Max(1, visibleCount), true);
        }

        private static int NormalizeTier(int tavernTier)
        {
            return Math.Max(1, Math.Min(6, tavernTier));
        }
    }
}
