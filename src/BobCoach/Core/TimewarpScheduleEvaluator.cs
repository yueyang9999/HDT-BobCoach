using System.Collections.Generic;
using System.Linq;

namespace BobCoach.Engine
{
    /// <summary>查询固定时空扭曲计划；不把计划标记为实际发生。</summary>
    public static class TimewarpScheduleEvaluator
    {
        public static IList<TimewarpVisit> GetExpectedVisitsAtTurn(
            EffectiveGameRules rules, int turn)
        {
            if (turn <= 0) return new List<TimewarpVisit>();
            var effectiveRules = rules ?? EffectiveGameRules.Default;
            return effectiveRules.TimewarpVisits
                .Where(visit => visit != null && visit.Turn == turn)
                .Where(visit => effectiveRules.LesserTimewarpEnabled
                    || visit.Kind != "lesser")
                .OrderBy(visit => visit.Kind)
                .ThenBy(visit => visit.Id)
                .ToList();
        }
    }
}
