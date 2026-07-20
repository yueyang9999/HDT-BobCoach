using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BobCoach.Engine;

internal static class CardSemanticSourceBehavior
{
    private sealed class FakeFactSource : ICardSemanticFactSource
    {
        public int Calls;
        public readonly ManualResetEventSlim ConcurrentCallObserved
            = new ManualResetEventSlim(false);

        public bool TryGet(string cardId, out CardSemanticFact fact)
        {
            int calls = Interlocked.Increment(ref Calls);
            if (cardId == "CONCURRENT")
            {
                if (calls == 1)
                    ConcurrentCallObserved.Wait(TimeSpan.FromMilliseconds(500));
                else
                    ConcurrentCallObserved.Set();
            }
            if (cardId == "THROW") throw new InvalidOperationException("boundary failure");
            fact = new CardSemanticFact
            {
                CardId = cardId == "MISMATCH" ? "OTHER" : cardId,
                Mechanics = new List<string> { "Battlecry" },
                TextZhCn = "LOCAL_TEXT_MUST_NOT_BE_RETURNED",
            };
            return cardId == "KNOWN" || cardId == "MISMATCH" || cardId == "CONCURRENT";
        }
    }

    private static int Main()
    {
        var facts = new FakeFactSource();
        ICardSemanticSource source = new CachedCardSemanticSource(
            facts, new CardSemanticRuleEvaluator());

        CardSemanticsData first;
        CardSemanticsData second;
        if (!source.TryGet("KNOWN", out first) || !first.HasMechanic("BATTLECRY"))
            return Fail("known local fact did not return derived semantics");
        if (!source.TryGet("KNOWN", out second) || !object.ReferenceEquals(first, second))
            return Fail("derived semantics were not reused from the process cache");
        if (facts.Calls != 1)
            return Fail("known CardId was read from the fact boundary more than once");

        CardSemanticsData missing;
        if (source.TryGet("UNKNOWN", out missing) || source.TryGet("UNKNOWN", out missing))
            return Fail("unknown CardId did not fail closed");
        if (facts.Calls != 2)
            return Fail("unknown CardId was not negative-cached for this process");

        CardSemanticsData mismatch;
        if (source.TryGet("MISMATCH", out mismatch))
            return Fail("mismatched CardId was accepted from the local fact boundary");

        CardSemanticsData throwing;
        try
        {
            if (source.TryGet("THROW", out throwing))
                return Fail("throwing fact boundary returned semantics");
        }
        catch
        {
            return Fail("throwing fact boundary escaped instead of failing closed");
        }

        var concurrentFacts = new FakeFactSource();
        ICardSemanticSource concurrentSource = new CachedCardSemanticSource(
            concurrentFacts, new CardSemanticRuleEvaluator());
        var results = new CardSemanticsData[8];
        Parallel.For(0, results.Length, i =>
        {
            CardSemanticsData value;
            if (concurrentSource.TryGet("CONCURRENT", out value))
                results[i] = value;
        });
        if (concurrentFacts.Calls != 1)
            return Fail("concurrent readers crossed the fact boundary more than once");
        for (int i = 0; i < results.Length; i++)
        {
            if (results[i] == null || !object.ReferenceEquals(results[0], results[i]))
                return Fail("concurrent readers did not receive one cached derived result");
        }

        Console.WriteLine("PASS semantic source caches one derived result and fails closed at the local fact boundary");
        return 0;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine("FAIL " + message);
        return 1;
    }
}
