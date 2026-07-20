using System;

namespace BobCoach.Engine
{
    public static class UpgradePrizeEvaluator
    {
        public static int GetPrizeTier(UpgradePrizeRule rule, int triggerTurn)
        {
            if (rule == null || triggerTurn <= 0) return 0;
            int tier = rule.InitialTier
                + Math.Max(0, triggerTurn - 1) / rule.ImprovesEveryTurns;
            return Math.Min(rule.MaxTier, tier);
        }

        public static TavernUpgradeOccurrence CreateOccurrence(
            UpgradePrizeRule rule, int turn, int fromTier, int toTier, string source)
        {
            if (rule == null || turn <= 0 || fromTier <= 0 || toTier != fromTier + 1
                || string.IsNullOrEmpty(source)) return null;
            string occurrenceId = rule.SourceId + ":discover_prize_after_upgrade@"
                + turn + ":" + fromTier + ">" + toTier;
            return new TavernUpgradeOccurrence(
                occurrenceId, turn, fromTier, toTier, source);
        }

        public static PrizeDiscoverExpectation CreateExpectation(
            UpgradePrizeRule rule, TavernUpgradeOccurrence occurrence)
        {
            if (rule == null || occurrence == null) return null;
            int prizeTier = GetPrizeTier(rule, occurrence.Turn);
            if (prizeTier <= 0) return null;
            return new PrizeDiscoverExpectation(
                occurrence.OccurrenceId, occurrence.Turn,
                prizeTier, 1, rule.SourceId);
        }
    }
}
