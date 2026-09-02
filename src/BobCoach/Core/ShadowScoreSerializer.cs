using System;
using System.Collections.Generic;
using System.Globalization;
using BobCoach.Engine.Decision;

namespace BobCoach.Engine
{
    /// <summary>
    /// Shadow 统一评分序列化器。
    /// 与插件可见的 legacy 推荐解耦，只负责把同一候选面的评分稳定写成回放审计 JSON。
    /// </summary>
    public static class ShadowScoreSerializer
    {
        public static string SerializeRecommended(GameAction action)
        {
            if (action == null) return "null";
            return string.Format("{{\"type\":\"{0}\",\"target\":{1}}}",
                action.Type, action.TargetIndex);
        }

        public static string SerializeScores(GameState state, IList<RankedAction> ranked)
        {
            if (state == null || ranked == null || ranked.Count == 0)
                return "[]";

            var items = new List<string>();
            var bestKey = ranked[0].ActionKey ?? "";
            var seenKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach (var item in ranked)
            {
                if (item == null || item.Action == null) continue;
                var actionKey = item.ActionKey ?? "";
                if (!seenKeys.Add(actionKey)) continue;

                var action = item.Action;
                bool isRecommended = string.Equals(actionKey, bestKey,
                    StringComparison.Ordinal);
                int cost = 0;
                try
                {
                    cost = ActionDescriptor.From(state, action).GoldCost;
                }
                catch
                {
                    cost = action.Cost;
                }

                items.Add(string.Format(
                    "{{\"type\":\"{0}\",\"target\":{1},\"cardId\":\"{2}\",\"cost\":{3},\"score\":{4},\"recommended\":{5}}}",
                    action.Type,
                    action.TargetIndex,
                    (action.CardId ?? "").Replace("\"", "\\\""),
                    cost,
                    item.Score.ToString("F4", CultureInfo.InvariantCulture),
                    isRecommended ? "true" : "false"));
            }

            return "[" + string.Join(",", items) + "]";
        }
    }
}
