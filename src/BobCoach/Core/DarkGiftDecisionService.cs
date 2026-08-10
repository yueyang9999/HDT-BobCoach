namespace BobCoach.Engine
{
    public sealed class DarkGiftDecision
    {
        public bool ShouldOpen;
        public double Score;
        public int Cost;
        public string Reason = "";
    }

    /// <summary>
    /// 暗赐“是否现在花 3 金币打开”的规则化决策服务。
    /// 资格规则来自官方：T3 起、每回合一次、每局最多 3 次、3 金币。
    /// 价值评分使用当前战力、血量、阵容协同和候选期望值。
    /// </summary>
    public static class DarkGiftDecisionService
    {
        public static DarkGiftDecision Decide(
            int turn,
            int liveGold,
            int usesRemaining,
            bool alreadyUsedThisTurn,
            int boardPower,
            int opponentPower,
            int health,
            double offeredExpectedValue,
            bool hasCoreSynergy)
        {
            if (turn < 3)
            {
                return new DarkGiftDecision
                {
                    ShouldOpen = false,
                    Cost = 3,
                    Reason = "not-unlocked",
                };
            }

            if (usesRemaining <= 0 || alreadyUsedThisTurn || liveGold < 3)
            {
                return new DarkGiftDecision
                {
                    ShouldOpen = false,
                    Cost = 3,
                    Reason = alreadyUsedThisTurn ? "used-this-turn"
                        : liveGold < 3 ? "insufficient-gold" : "uses-exhausted",
                };
            }

            double powerRatio = opponentPower > 0
                ? (double)boardPower / opponentPower
                : 1.0;
            bool desperate = health <= 10 && powerRatio < 0.7;
            if (desperate && offeredExpectedValue < 0.4)
            {
                return new DarkGiftDecision
                {
                    ShouldOpen = false,
                    Score = 0.1,
                    Cost = 3,
                    Reason = "desperate-low-value",
                };
            }

            double score = offeredExpectedValue * 0.55
                + (hasCoreSynergy ? 0.25 : 0.0)
                + (liveGold - 3) * 0.05
                - (desperate ? 0.15 : 0.0);
            return new DarkGiftDecision
            {
                ShouldOpen = score >= 0.45,
                Score = score,
                Cost = 3,
                Reason = score >= 0.45 ? "open" : "low-value",
            };
        }
    }
}
