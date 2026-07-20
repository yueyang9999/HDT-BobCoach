using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BobCoach.Engine;

internal static class HeroPowerFactSourceBehavior
{
    private static int _assertions;

    private static int Main()
    {
        try
        {
            var facts = new CountingFactSource();
            var rules = new CountingRuleEvaluator();
            var source = new CachedHeroStrategySource(facts, rules);

            Parallel.For(0, 32, _ =>
            {
                HeroStrategy strategy;
                if (!source.TryGet("VALID", out strategy) || strategy.SpecialRule != "VALID")
                    throw new InvalidOperationException("concurrent valid lookup failed");
            });
            AssertEqual(1, facts.ValidCalls, "valid fact single flight");
            AssertEqual(1, rules.ValidCalls, "valid rule single flight");

            HeroStrategy first;
            AssertTrue(source.TryGet("VALID", out first), "first cached lookup");
            first.SpecialRule = "MUTATED";
            first.TribeAffinity["DRAGON"] = 99f;
            first.SynergyTags.Add("MUTATED");
            HeroStrategy second;
            AssertTrue(source.TryGet("VALID", out second), "second cached lookup");
            AssertEqual("VALID", second.SpecialRule, "cached scalar is defensive");
            AssertEqual(0.20f, second.TribeAffinity["DRAGON"], "cached dictionary is defensive");
            AssertTrue(!second.SynergyTags.Contains("MUTATED"), "cached set is defensive");
            AssertTrue(!object.ReferenceEquals(first, second), "cached strategy returns copies");

            HeroStrategy ignored;
            AssertTrue(!source.TryGet("UNKNOWN", out ignored), "unknown first failure");
            AssertTrue(!source.TryGet("UNKNOWN", out ignored), "unknown negative cache");
            AssertEqual(1, facts.UnknownCalls, "unknown fact single flight");
            AssertEqual(0, rules.UnknownCalls, "unknown does not evaluate rules");

            AssertTrue(!source.TryGet("RULE_FAIL", out ignored), "rule failure closes");
            AssertTrue(!source.TryGet("RULE_FAIL", out ignored), "rule failure caches");
            AssertEqual(1, facts.RuleFailCalls, "rule-fail fact single flight");
            AssertEqual(1, rules.RuleFailCalls, "rule-fail evaluation single flight");

            AssertTrue(!source.TryGet("FACT_THROW", out ignored), "fact exception closes");
            AssertTrue(!source.TryGet("FACT_THROW", out ignored), "fact exception caches");
            AssertEqual(1, facts.ThrowCalls, "fact exception single flight");
            AssertTrue(!source.TryGet("", out ignored), "empty CardId closes");
            AssertTrue(!source.TryGet(null, out ignored), "null CardId closes");

            Console.WriteLine("PASS hero strategy concurrent cache, negative cache, and defensive copies assertions="
                + _assertions);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL " + ex.Message);
            return 1;
        }
    }

    private sealed class CountingFactSource : IHeroPowerFactSource
    {
        public int ValidCalls;
        public int UnknownCalls;
        public int RuleFailCalls;
        public int ThrowCalls;

        public bool TryGet(string cardId, out HeroPowerFact fact)
        {
            fact = null;
            if (cardId == "VALID")
            {
                Interlocked.Increment(ref ValidCalls);
                fact = CreateFact(cardId);
                return true;
            }
            if (cardId == "UNKNOWN")
            {
                Interlocked.Increment(ref UnknownCalls);
                return false;
            }
            if (cardId == "RULE_FAIL")
            {
                Interlocked.Increment(ref RuleFailCalls);
                fact = CreateFact(cardId);
                return true;
            }
            if (cardId == "FACT_THROW")
            {
                Interlocked.Increment(ref ThrowCalls);
                throw new InvalidOperationException("fact failure");
            }
            return false;
        }

        private static HeroPowerFact CreateFact(string cardId)
        {
            return new HeroPowerFact
            {
                RequestedCardId = cardId,
                HeroCardId = "HERO",
                PowerCardId = "POWER",
                HeroArmor = 10,
                PowerCost = 1,
                TextZhCn = "有效技能文本。",
                ScriptData = new int[6],
            };
        }
    }

    private sealed class CountingRuleEvaluator : IHeroStrategyRuleEvaluator
    {
        public int ValidCalls;
        public int UnknownCalls;
        public int RuleFailCalls;

        public bool TryEvaluate(HeroPowerFact fact, out HeroStrategy strategy)
        {
            strategy = null;
            if (fact.RequestedCardId == "RULE_FAIL")
            {
                Interlocked.Increment(ref RuleFailCalls);
                return false;
            }
            if (fact.RequestedCardId != "VALID")
            {
                Interlocked.Increment(ref UnknownCalls);
                return false;
            }
            Interlocked.Increment(ref ValidCalls);
            strategy = new HeroStrategy
            {
                HeroCardId = fact.HeroCardId,
                HeroName = "",
                PowerType = HeroPowerType.Active,
                Archetype = HeroArchetype.General,
                PowerCost = 1,
                PowerHint = "",
                SpecialRule = "VALID",
                TribeAffinity = new Dictionary<string, float>(StringComparer.Ordinal)
                {
                    { "DRAGON", 0.20f },
                },
                SynergyTags = new HashSet<string>(new[] { "DRAGON" }, StringComparer.Ordinal),
            };
            return true;
        }
    }

    private static void AssertTrue(bool value, string label)
    {
        _assertions++;
        if (!value) throw new InvalidOperationException(label);
    }

    private static void AssertEqual<T>(T expected, T actual, string label)
    {
        _assertions++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(label + ": expected=" + expected + " actual=" + actual);
    }
}
