namespace BobCoach.Engine
{
    public sealed class LiveSafetyDecision
    {
        public bool SuppressUpgrade;
        public bool SuppressRefresh;
        public bool SuppressBuy;
        public string Reason = "";
    }

    /// <summary>
    /// 实时安全门禁。只做硬约束和明显高风险抑制，不替代策略评分；
    /// 防止“金币不足仍推荐刷新”“残血弱战力仍强行升本”等严重误导。
    /// </summary>
    public static class LiveSafetyGate
    {
        public static LiveSafetyDecision Evaluate(
            LiveSceneSnapshot scene,
            int refreshCost,
            int minBuyCost)
        {
            var decision = new LiveSafetyDecision();
            if (scene == null || scene.Economy == null)
            {
                decision.Reason = "unknown-scene";
                return decision;
            }

            if (!scene.NormalRecruitAvailable)
            {
                decision.SuppressUpgrade = true;
                decision.SuppressRefresh = true;
                decision.SuppressBuy = true;
                decision.Reason = "no-recruit-window";
                return decision;
            }

            if (scene.Economy.LiveGold < refreshCost)
            {
                decision.SuppressRefresh = true;
                decision.Reason = "insufficient-refresh-gold";
            }

            if (scene.Economy.LiveGold < minBuyCost)
            {
                decision.SuppressBuy = true;
                if (string.IsNullOrEmpty(decision.Reason))
                    decision.Reason = "insufficient-buy-gold";
            }

            if (scene.Survival.Desperate
                && scene.Economy.CanUpgrade
                && scene.Economy.GoldAfterUpgrade <= 1
                && scene.Survival.PowerRatio < 0.7)
            {
                decision.SuppressUpgrade = true;
                decision.Reason = "desperate-weak-board-upgrade-risk";
            }

            return decision;
        }
    }
}
