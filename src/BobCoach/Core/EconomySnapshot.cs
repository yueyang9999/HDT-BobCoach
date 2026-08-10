using System.Collections.Generic;

namespace BobCoach.Engine
{
    /// <summary>
    /// 统一铸币快照。所有费用合法性、评分和 UI 可支付判断只消费这份快照，
    /// 不允许决策层和渲染层各自重新计算 liveGold。
    /// </summary>
    public sealed class EconomySnapshot
    {
        public int LiveGold;
        public int MaxGold;
        public int SpentGold;
        public bool CanRefresh;
        public bool CanUpgrade;
        public bool CanBuyAny;
        public int GoldAfterUpgrade;
        public int GoldAfterRefresh;
        public List<string> AffordableActions = new List<string>();

        public static EconomySnapshot Build(
            int liveGold,
            int maxGold,
            int? upgradeCost,
            int refreshCost,
            bool manualRefreshAllowed,
            bool canBuyAny)
        {
            int safeGold = liveGold < 0 ? 0 : liveGold;
            int safeMax = maxGold < safeGold ? safeGold : maxGold;
            int effectiveUpgradeCost = upgradeCost.HasValue && upgradeCost.Value >= 0
                ? upgradeCost.Value
                : -1;

            var snapshot = new EconomySnapshot
            {
                LiveGold = safeGold,
                MaxGold = safeMax,
                SpentGold = safeMax >= safeGold ? safeMax - safeGold : 0,
                CanRefresh = manualRefreshAllowed && (safeGold >= refreshCost || refreshCost == 0),
                CanUpgrade = effectiveUpgradeCost >= 0 && safeGold >= effectiveUpgradeCost,
                CanBuyAny = canBuyAny,
                GoldAfterUpgrade = effectiveUpgradeCost >= 0 && safeGold >= effectiveUpgradeCost
                    ? safeGold - effectiveUpgradeCost
                    : safeGold,
                GoldAfterRefresh = refreshCost >= 0 && safeGold >= refreshCost
                    ? safeGold - refreshCost
                    : safeGold,
            };

            if (snapshot.CanRefresh) snapshot.AffordableActions.Add("Refresh");
            if (snapshot.CanUpgrade) snapshot.AffordableActions.Add("Upgrade");
            if (snapshot.CanBuyAny) snapshot.AffordableActions.Add("Buy");
            return snapshot;
        }
    }
}
