namespace BobCoach.Engine
{
    internal static class PrizeSpellScorer
    {
        public static double Score(
            PrizeSpellPolicy policy,
            int gold,
            int boardCount,
            int hitPoints)
        {
            if (policy == null) return 0.0;

            double score = 0.0;
            if (gold <= 3 && policy.Role == PrizeSpellRole.Economy) score += 2.0;
            if (boardCount < 3 && policy.Role == PrizeSpellRole.Tempo) score += 2.0;
            if (boardCount >= 5 && policy.Role == PrizeSpellRole.Scaling) score += 1.5;
            if (hitPoints <= 15 && policy.Role == PrizeSpellRole.Tempo) score += 1.0;
            if (policy.PrizeTier >= 4) score += 1.0;
            return score;
        }
    }
}
