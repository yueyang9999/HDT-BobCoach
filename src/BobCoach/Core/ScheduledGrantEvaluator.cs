using System.Collections.Generic;

namespace BobCoach.Engine
{
    public sealed class ScheduledGrantOccurrence
    {
        internal ScheduledGrantOccurrence(ScheduledGrant grant, int triggerTurn)
            : this(grant, triggerTurn, grant.Id + "@" + triggerTurn)
        {
        }

        internal ScheduledGrantOccurrence(
            ScheduledGrant grant, int triggerTurn, string occurrenceId)
        {
            Grant = grant;
            TriggerTurn = triggerTurn;
            OccurrenceId = occurrenceId;
        }

        public ScheduledGrant Grant { get; private set; }
        public int TriggerTurn { get; private set; }
        public string OccurrenceId { get; private set; }
    }

    /// <summary>计算当前到期资源；不伪造游戏已经发放的卡牌或发现候选。</summary>
    public static class ScheduledGrantEvaluator
    {
        public static IList<ScheduledGrantOccurrence> GetDue(
            GameState state, EffectiveGameRules rules)
        {
            var result = new List<ScheduledGrantOccurrence>();
            if (state == null || state.Turn <= 0) return result;
            var effectiveRules = rules ?? EffectiveGameRules.Default;
            foreach (var grant in effectiveRules.ScheduledGrants)
            {
                if (grant != null && grant.Kind == "tier_locked_minion_discover")
                {
                    if (state.Turn != 1) continue;
                    foreach (var tier in grant.Tiers)
                    {
                        var tierOccurrence = new ScheduledGrantOccurrence(
                            grant, state.Turn, grant.Id + "@choose-tier-" + tier);
                        if (state.ClaimedScheduledGrantOccurrences == null
                            || !state.ClaimedScheduledGrantOccurrences.Contains(
                                tierOccurrence.OccurrenceId))
                            result.Add(tierOccurrence);
                    }
                    continue;
                }
                bool isDue = grant != null
                    && ((grant.Turn > 0 && state.Turn == grant.Turn)
                        || (grant.EveryTurns > 0 && state.Turn % grant.EveryTurns == 0));
                if (isDue)
                {
                    var occurrence = new ScheduledGrantOccurrence(grant, state.Turn);
                    if (state.ClaimedScheduledGrantOccurrences == null
                        || !state.ClaimedScheduledGrantOccurrences.Contains(occurrence.OccurrenceId))
                        result.Add(occurrence);
                }
            }
            return result;
        }

        public static bool MarkClaimed(GameState state, ScheduledGrantOccurrence occurrence)
        {
            if (state == null || occurrence == null
                || string.IsNullOrEmpty(occurrence.OccurrenceId)) return false;
            if (state.ClaimedScheduledGrantOccurrences == null)
                state.ClaimedScheduledGrantOccurrences = new HashSet<string>();
            return state.ClaimedScheduledGrantOccurrences.Add(occurrence.OccurrenceId);
        }
    }
}
