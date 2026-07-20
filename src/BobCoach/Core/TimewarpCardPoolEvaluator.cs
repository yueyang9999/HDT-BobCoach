using System.Linq;

namespace BobCoach.Engine
{
    /// <summary>判断时空扭曲卡池合并是否已生效；不提供或猜测成员目录。</summary>
    public static class TimewarpCardPoolEvaluator
    {
        public static bool IsMerged(EffectiveGameRules rules, string kind, int turn)
        {
            if (string.IsNullOrEmpty(kind) || turn <= 0) return false;
            return (rules ?? EffectiveGameRules.Default).TimewarpPoolMergeRules
                .Any(rule => rule != null && rule.Kind == kind && turn >= rule.Turn);
        }
    }
}
