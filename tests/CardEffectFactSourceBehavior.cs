using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BobCoach.Engine;

internal static class CardEffectFactSourceBehavior
{
    private static int Main()
    {
        try
        {
            var facts = new CountingFactSource();
            var rules = new CountingRuleEvaluator(new CardEffectRuleEvaluator());
            var normalizer = new TestNormalizer();
            var source = new CachedCardEffectSource(facts, rules, normalizer);

            var signatures = new string[32];
            Parallel.For(0, signatures.Length, i =>
            {
                IReadOnlyList<CardEffectDefinition> effects;
                if (!source.TryGet("PARALLEL", out effects))
                    throw new InvalidOperationException("parallel derived effect missing");
                signatures[i] = Signature(effects);
            });
            if (signatures.Any(value => value != "discover=2.5/once"))
                throw new InvalidOperationException("parallel callers observed different results");
            AssertEqual(1, facts.Count("PARALLEL"), "parallel fact reads");
            AssertEqual(1, rules.Count("PARALLEL"), "parallel rule evaluations");

            IReadOnlyList<CardEffectDefinition> first;
            if (!source.TryGet("MUTABLE", out first) || Signature(first) != "discover=2.5/once")
                throw new InvalidOperationException("initial mutable fact signature mismatch");
            facts.Mutable.TextZhCn = "获得1枚铸币。";
            facts.Mutable.ScriptData = new[] { 9, 9, 9, 9, 9, 9 };
            IReadOnlyList<CardEffectDefinition> second;
            if (!source.TryGet("MUTABLE", out second) || Signature(second) != "discover=2.5/once")
                throw new InvalidOperationException("cached derived value retained mutable source state");
            AssertReadOnly(second);

            IReadOnlyList<CardEffectDefinition> normal;
            IReadOnlyList<CardEffectDefinition> golden;
            if (!source.TryGet("NORMAL", out normal) || !source.TryGet("GOLDEN", out golden)
                || Signature(normal) != Signature(golden))
                throw new InvalidOperationException("golden card did not share exact normal-card effects");
            AssertEqual(1, facts.Count("NORMAL"), "normal/golden shared fact reads");
            AssertEqual(1, rules.Count("NORMAL"), "normal/golden shared rule evaluations");

            AssertFailsTwice(source, facts, "UNKNOWN");
            AssertFailsTwice(source, facts, "WRONG_TYPE");
            AssertFailsTwice(source, facts, "DYNAMIC_FAILURE");
            AssertFailsTwice(source, facts, "THROW");
            IReadOnlyList<CardEffectDefinition> ignored;
            if (source.TryGet("", out ignored))
                throw new InvalidOperationException("empty id did not fail closed");
            if (source.TryGet("BAD_GOLDEN", out ignored)
                || source.TryGet("BAD_GOLDEN", out ignored))
                throw new InvalidOperationException("empty or unmapped golden id did not fail closed");
            AssertEqual(1, normalizer.Count("BAD_GOLDEN"), "unmapped golden negative cache");
            if (source.TryGet("NORMALIZER_THROW", out ignored)
                || source.TryGet("NORMALIZER_THROW", out ignored))
                throw new InvalidOperationException("throwing normalizer did not fail closed");
            AssertEqual(1, normalizer.Count("NORMALIZER_THROW"), "normalizer exception cache");

            Console.WriteLine("PASS card effect source concurrent cache and failure closure");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL " + ex.Message);
            return 1;
        }
    }

    private static void AssertFailsTwice(
        CachedCardEffectSource source, CountingFactSource facts, string cardId)
    {
        IReadOnlyList<CardEffectDefinition> ignored;
        if (source.TryGet(cardId, out ignored) || source.TryGet(cardId, out ignored))
            throw new InvalidOperationException(cardId + " did not fail closed");
        AssertEqual(1, facts.Count(cardId), cardId + " negative cache reads");
    }

    private static void AssertReadOnly(IReadOnlyList<CardEffectDefinition> effects)
    {
        var mutable = effects as IList<CardEffectDefinition>;
        if (mutable == null) return;
        try
        {
            mutable.Add(new CardEffectDefinition("invalid", 1, "once"));
            throw new InvalidOperationException("cached effects collection is mutable");
        }
        catch (NotSupportedException)
        {
        }
    }

    private static string Signature(IEnumerable<CardEffectDefinition> effects)
    {
        return string.Join(";", effects.Select(effect =>
            effect.Type + "=" + effect.ValueGold + "/" + effect.Per));
    }

    private static void AssertEqual(int expected, int actual, string label)
    {
        if (expected != actual)
            throw new InvalidOperationException(label + " expected=" + expected + " actual=" + actual);
    }

    private sealed class CountingFactSource : ICardEffectFactSource
    {
        private readonly object _gate = new object();
        private readonly Dictionary<string, int> _counts
            = new Dictionary<string, int>(StringComparer.Ordinal);

        public CardEffectFact Mutable { get; private set; }

        public CountingFactSource()
        {
            Mutable = Fact("MUTABLE", "发现一张牌。");
        }

        public bool TryGet(string cardId, out CardEffectFact fact)
        {
            lock (_gate)
            {
                int count;
                _counts.TryGetValue(cardId, out count);
                _counts[cardId] = count + 1;
            }
            if (cardId == "PARALLEL") Thread.Sleep(20);
            if (cardId == "THROW") throw new InvalidOperationException("fact source failure");
            if (cardId == "UNKNOWN") { fact = null; return false; }
            if (cardId == "WRONG_TYPE")
            {
                fact = Fact(cardId, "发现一张牌.");
                fact.CardType = CardEffectCardType.Unknown;
                return true;
            }
            if (cardId == "DYNAMIC_FAILURE")
            {
                fact = Fact(cardId, "获取{6}张牌。");
                return true;
            }
            if (cardId == "MUTABLE") { fact = Mutable; return true; }
            fact = Fact(cardId, "发现一张牌。");
            return true;
        }

        public int Count(string cardId)
        {
            lock (_gate)
            {
                int count;
                return _counts.TryGetValue(cardId, out count) ? count : 0;
            }
        }

        private static CardEffectFact Fact(string cardId, string text)
        {
            return new CardEffectFact
            {
                CardId = cardId,
                CardType = CardEffectCardType.Minion,
                TextZhCn = text,
                ScriptData = new[] { 0, 0, 0, 0, 0, 0 },
                Attack = 1,
                Health = 1,
                Tier = 1,
            };
        }
    }

    private sealed class CountingRuleEvaluator : ICardEffectRuleEvaluator
    {
        private readonly ICardEffectRuleEvaluator _inner;
        private readonly object _gate = new object();
        private readonly Dictionary<string, int> _counts
            = new Dictionary<string, int>(StringComparer.Ordinal);

        public CountingRuleEvaluator(ICardEffectRuleEvaluator inner)
        {
            _inner = inner;
        }

        public bool TryEvaluate(
            CardEffectFact fact, out IReadOnlyList<CardEffectDefinition> effects)
        {
            lock (_gate)
            {
                int count;
                _counts.TryGetValue(fact.CardId, out count);
                _counts[fact.CardId] = count + 1;
            }
            return _inner.TryEvaluate(fact, out effects);
        }

        public int Count(string cardId)
        {
            lock (_gate)
            {
                int count;
                return _counts.TryGetValue(cardId, out count) ? count : 0;
            }
        }
    }

    private sealed class TestNormalizer : ICardEffectCardIdNormalizer
    {
        private readonly object _gate = new object();
        private readonly Dictionary<string, int> _counts
            = new Dictionary<string, int>(StringComparer.Ordinal);

        public bool TryNormalize(string cardId, out string normalCardId)
        {
            lock (_gate)
            {
                int count;
                _counts.TryGetValue(cardId ?? "", out count);
                _counts[cardId ?? ""] = count + 1;
            }
            normalCardId = "";
            if (cardId == "NORMALIZER_THROW")
                throw new InvalidOperationException("normalizer failure");
            if (string.IsNullOrEmpty(cardId) || cardId == "BAD_GOLDEN") return false;
            normalCardId = cardId == "GOLDEN" ? "NORMAL" : cardId;
            return true;
        }

        public int Count(string cardId)
        {
            lock (_gate)
            {
                int count;
                return _counts.TryGetValue(cardId, out count) ? count : 0;
            }
        }
    }
}
