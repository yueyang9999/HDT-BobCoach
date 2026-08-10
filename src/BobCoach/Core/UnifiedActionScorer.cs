using System.Collections.Generic;

namespace BobCoach.Engine
{
    public sealed class ScoredAction
    {
        public string ActionType = "";
        public int Cost;
        public double Score;
        public List<string> Reasons = new List<string>();
    }

    /// <summary>
    /// 统一动作评分。当前版本为确定性软评分，不依赖模型。
    /// 目标是把买/卖/升/刷/技能/发动放进同一个可比较的分数面。
    /// </summary>
    public static class UnifiedActionScorer
    {
        public static ScoredAction Score(
            string actionType,
            int cost,
            int liveGold,
            int boardPower,
            int opponentPower,
            int health,
            int turn,
            int tavernTier,
            double compMatch,
            double tripleProgress,
            bool hasValidTarget,
            bool boardFull,
            bool handWaiting)
        {
            var result = new ScoredAction
            {
                ActionType = actionType,
                Cost = cost,
            };

            if (liveGold < cost)
            {
                result.Score = -1000d;
                result.Reasons.Add("insufficient-gold");
                return result;
            }

            double powerRatio = opponentPower > 0
                ? (double)boardPower / opponentPower
                : 1.0;
            double survivalPenalty = health <= 10 ? 0.4 : health <= 15 ? 0.2 : 0.0;

            switch (actionType)
            {
                case "Upgrade":
                    result.Score = 0.45
                        + tavernTier * 0.02
                        + (liveGold - cost) * 0.08
                        - survivalPenalty
                        - (powerRatio < 0.7 ? 0.35 : 0.0);
                    result.Reasons.Add("upgrade-value");
                    if (powerRatio < 0.7)
                        result.Reasons.Add("weak-board-risk");
                    break;

                case "BuyMinion":
                case "BuySpell":
                    result.Score = 0.55
                        + compMatch * 0.35
                        + tripleProgress * 0.20
                        - (boardFull ? 0.15 : 0.0)
                        - survivalPenalty;
                    result.Reasons.Add("buy-value");
                    if (compMatch > 0.7) result.Reasons.Add("comp-match");
                    break;

                case "Refresh":
                    result.Score = 0.25
                        - (liveGold - cost) * 0.02
                        - survivalPenalty;
                    result.Reasons.Add("refresh-value");
                    break;

                case "SellMinion":
                    result.Score = handWaiting && boardFull ? 0.5 : -0.2;
                    result.Score += (liveGold + 1 - cost) * 0.05;
                    result.Reasons.Add("sell-for-space");
                    break;

                case "Activate":
                    result.Score = hasValidTarget ? 0.6 : -1000d;
                    result.Score += (liveGold - cost) * 0.05
                        + compMatch * 0.15
                        - survivalPenalty;
                    result.Reasons.Add(hasValidTarget ? "activate-value" : "invalid-target");
                    break;

                case "UseHeroPower":
                    result.Score = hasValidTarget ? 0.5 : -1000d;
                    result.Score += (liveGold - cost) * 0.06
                        - survivalPenalty;
                    result.Reasons.Add(hasValidTarget ? "hero-power-value" : "invalid-target");
                    break;

                default:
                    result.Score = 0.1d;
                    result.Reasons.Add("default-value");
                    break;
            }

            if (result.Score < -999d && !result.Reasons.Contains("insufficient-gold"))
                result.Reasons.Add("invalid-target");
            return result;
        }
    }
}
