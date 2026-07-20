using System;
using System.Collections.Generic;
using BobCoach.Engine;

internal static class SemanticSynergyEvaluatorBehavior
{
    private sealed class FakeSemanticSource : ICardSemanticSource
    {
        private readonly Dictionary<string, CardSemanticsData> _rows
            = new Dictionary<string, CardSemanticsData>(StringComparer.Ordinal);

        public void Add(string cardId, CardSemanticsData semantics)
        {
            _rows[cardId] = semantics;
        }

        public bool TryGet(string cardId, out CardSemanticsData semantics)
        {
            return _rows.TryGetValue(cardId, out semantics);
        }
    }

    private static int Main()
    {
        var source = new FakeSemanticSource();
        source.Add("BRANN", new CardSemanticsData(
            new string[0], new CardSemanticCombo[0],
            new[] { "TRIGGER_BATTLECRY_EXTRA" }));
        source.Add("BATTLECRY_CARD", new CardSemanticsData(
            new[] { "BATTLECRY" },
            new[] { new CardSemanticCombo("TRIGGER_BATTLECRY_EXTRA", 3.0) },
            new string[0]));

        var evaluator = new SemanticSynergyEvaluator(source);
        float score = evaluator.ComputeWeightedShopScore(
            new[] { "BRANN" }, new[] { "BATTLECRY_CARD" });
        if (Math.Abs(score - 0.25f) > 0.000001f)
            return Fail("one approved 3.0 match was not normalized by the retained divisor 12");

        source.Add("MOIRA", new CardSemanticsData(
            new string[0], new CardSemanticCombo[0],
            new[] { "TRIGGER_BATTLECRY_EXTRA", "TRIGGER_DEATHRATTLE_EXTRA" }));
        source.Add("DUAL_TRIGGER", new CardSemanticsData(
            new[] { "BATTLECRY", "DEATHRATTLE" },
            new[]
            {
                new CardSemanticCombo("TRIGGER_BATTLECRY_EXTRA", 3.0),
                new CardSemanticCombo("TRIGGER_DEATHRATTLE_EXTRA", 3.0),
            },
            new string[0]));
        float ratio = evaluator.ComputeMatchRatio(
            new[] { "MOIRA" }, "DUAL_TRIGGER");
        if (Math.Abs(ratio - 1.0f) > 0.000001f)
            return Fail("multi-provider board did not satisfy both candidate relationships");

        Console.WriteLine("PASS semantic synergy preserves weighted score and multi-provider match ratio");
        return 0;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine("FAIL " + message);
        return 1;
    }
}
