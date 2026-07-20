using System;
using System.Collections.Generic;
using BobCoach.Engine;

internal static class PrizeSpellDecisionBehavior
{
    private static int Main()
    {
        try
        {
            var registry = new PrizeSpellRegistry(new StubSource());
            AssertScore(registry, "BGS_Treasures_004", 2, 4, 30, 0.0);
            AssertScore(registry, "BGS_Treasures_023", 2, 4, 30, 3.0);
            AssertScore(registry, "BGS_Treasures_015", 10, 2, 10, 3.0);
            PrizeSpellPolicy missing;
            if (registry.TryGet("UNKNOWN", out missing)) throw new Exception("unknown policy succeeded");
            if (missing != null) throw new Exception("unknown policy was non-null");
            Console.WriteLine("PASS prize spell registry uses CardId policies and current scoring");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL " + ex.Message);
            return 1;
        }
    }

    private static void AssertScore(
        PrizeSpellRegistry registry,
        string cardId,
        int gold,
        int board,
        int hp,
        double expected)
    {
        PrizeSpellPolicy policy;
        if (!registry.TryGet(cardId, out policy)) throw new Exception("missing policy: " + cardId);
        double actual = PrizeSpellScorer.Score(policy, gold, board, hp);
        if (Math.Abs(expected - actual) > 0.000001)
            throw new Exception(cardId + " expected=" + expected + " actual=" + actual);
    }

    private sealed class StubSource : IPrizeSpellSource
    {
        private readonly Dictionary<string, PrizeSpellPolicy> _policies
            = new Dictionary<string, PrizeSpellPolicy>(StringComparer.Ordinal)
            {
                { "BGS_Treasures_004", new PrizeSpellPolicy(
                    "BGS_Treasures_004", 1, PrizeSpellRole.Discover) },
                { "BGS_Treasures_023", new PrizeSpellPolicy(
                    "BGS_Treasures_023", 4, PrizeSpellRole.Economy) },
                { "BGS_Treasures_015", new PrizeSpellPolicy(
                    "BGS_Treasures_015", 3, PrizeSpellRole.Tempo) },
            };

        public bool TryGet(string cardId, out PrizeSpellPolicy policy)
        {
            return _policies.TryGetValue(cardId ?? "", out policy);
        }
    }
}
