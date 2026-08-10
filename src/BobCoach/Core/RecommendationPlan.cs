using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BobCoach.Engine
{
    /// <summary>
    /// 单一推荐计划。文字、商店标记、卖牌标记、发动标记、升级提示
    /// 必须从同一份 RecommendationPlan 生成；渲染层禁止再次猜测动作含义。
    /// </summary>
    public sealed class RecommendationPlan
    {
        public string RecommendedActionType = "";
        public string StatusHint = "";
        public string UpgradeHintText = "";
        public string ActivateCardId = "";
        public int ActivateCost;
        public int LiveGold = -1;
        public int Turn = -1;
        public List<string> ShopMarkerCardIds = new List<string>();
        public List<int> SellMarkerBoardIndices = new List<int>();
        public string PlanHash = "";

        public static RecommendationPlan FromVisualPlan(
            VisualPlan plan,
            int liveGold,
            int turn)
        {
            var recommendation = new RecommendationPlan
            {
                RecommendedActionType = plan == null ? "" : plan.RecommendedActionType,
                StatusHint = plan == null ? "" : plan.Status.HintLine,
                UpgradeHintText = plan == null ? "" : plan.UpgradeHint?.Reason ?? "",
                ActivateCardId = plan == null ? "" : plan.ActivateHint?.CardId ?? "",
                ActivateCost = plan == null ? 0 : plan.ActivateHint?.Cost ?? 0,
                LiveGold = liveGold,
                Turn = turn,
            };
            if (plan == null) return recommendation;
            if (plan.ShopMarkers != null)
                recommendation.ShopMarkerCardIds.AddRange(
                    plan.ShopMarkers.Select(marker => marker.CardName ?? ""));
            if (plan.SellMarkers != null)
                recommendation.SellMarkerBoardIndices.AddRange(
                    plan.SellMarkers.Select(marker => marker.BoardIndex));
            recommendation.PlanHash = ComputeHash(recommendation);
            return recommendation;
        }

        public IList<string> Validate()
        {
            var errors = new List<string>();
            if (RecommendedActionType == "Activate"
                && string.IsNullOrEmpty(ActivateCardId))
                errors.Add("activate action requires activate hint");
            if (LiveGold < 0)
                errors.Add("live gold must be known");
            if (RecommendedActionType == "BuyMinion"
                && ShopMarkerCardIds.Count == 0)
                errors.Add("buy action requires shop marker");
            return errors;
        }

        public static string ComputeHash(RecommendationPlan plan)
        {
            if (plan == null) return "";
            var builder = new StringBuilder();
            builder.Append(plan.Turn).Append('|');
            builder.Append(plan.LiveGold).Append('|');
            builder.Append(plan.RecommendedActionType).Append('|');
            builder.Append(plan.StatusHint).Append('|');
            builder.Append(plan.UpgradeHintText).Append('|');
            builder.Append(plan.ActivateCardId).Append(':').Append(plan.ActivateCost).Append('|');
            builder.Append(string.Join(",", plan.ShopMarkerCardIds)).Append('|');
            builder.Append(string.Join(",", plan.SellMarkerBoardIndices));
            plan.PlanHash = builder.ToString();
            return plan.PlanHash;
        }
    }
}
