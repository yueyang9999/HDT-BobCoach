using System.Collections.Generic;

namespace BobCoach.Engine
{
    internal static class TrinketReasonFormatter
    {
        public static string Format(bool isUnrated, IList<string> ruleIds)
        {
            if (isUnrated) return "未知";

            var reasons = new List<string>();
            var seen = new HashSet<string>();
            if (ruleIds != null)
            {
                foreach (string ruleId in ruleIds)
                {
                    string reason = ToReason(ruleId);
                    if (!string.IsNullOrEmpty(reason) && seen.Add(reason))
                        reasons.Add(reason);
                }
            }
            return reasons.Count == 0
                ? "规则匹配"
                : "规则匹配：" + string.Join("、", reasons);
        }

        private static string ToReason(string ruleId)
        {
            switch (ruleId)
            {
                case "scaling": return "成长";
                case "economy": return "经济";
                case "protection": return "防护";
                case "generation": return "资源生成";
                case "replacement": return "替换代价";
                case "avenge": return "复仇触发";
                case "golden": return "金色相关";
                case "dominant_tribe": return "主流派";
                default: return "";
            }
        }
    }
}
