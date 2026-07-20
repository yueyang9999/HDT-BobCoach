using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BobCoach.Engine;

internal static class PrizeSpellFactSourceBehavior
{
    private static int _assertions;

    private static int Main(string[] args)
    {
        if (args.Length != 1 || !Directory.Exists(args[0]))
        {
            Console.Error.WriteLine("FAIL usage: behavior <hdtDir>");
            return 1;
        }
        string hdtDir = args[0];
        AppDomain.CurrentDomain.AssemblyResolve += (sender, eventArgs) =>
        {
            string candidate = Path.Combine(hdtDir,
                new System.Reflection.AssemblyName(eventArgs.Name).Name + ".dll");
            return File.Exists(candidate)
                ? System.Reflection.Assembly.LoadFrom(candidate)
                : null;
        };

        try
        {
            ConcurrentSuccessIsDerivedOnce();
            UnknownAndRuleFailureAreNegativeCached();
            InvalidDerivedIdentityFailsClosed();
            PrizeSpellFactSourceHearthDbBehavior.Run();
            Console.WriteLine("PASS prize spell source concurrent cache and exact local facts assertions="
                + _assertions);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL " + ex.Message);
            return 1;
        }
    }

    private static void ConcurrentSuccessIsDerivedOnce()
    {
        var facts = new CountingFacts();
        var rules = new CountingRules();
        var source = new CachedPrizeSpellSource(facts, rules);
        Parallel.For(0, 32, index =>
        {
            PrizeSpellPolicy policy;
            if (!source.TryGet("VALID", out policy)) throw new Exception("missing cached policy");
            Equal("VALID", policy.CardId, "cached CardId");
            Equal(2, policy.PrizeTier, "cached tier");
            Equal(PrizeSpellRole.Discover, policy.Role, "cached role");
        });
        Equal(1, facts.ValidCalls, "fact calls");
        Equal(1, rules.Calls, "rule calls");

        facts.ValidFact.TextZhCn = "changed after first query";
        PrizeSpellPolicy again;
        True(source.TryGet("VALID", out again), "cached second query");
        Equal(PrizeSpellRole.Discover, again.Role, "cached snapshot role");
        Equal(1, facts.ValidCalls, "fact calls after mutation");
    }

    private static void UnknownAndRuleFailureAreNegativeCached()
    {
        var facts = new CountingFacts();
        var rules = new CountingRules();
        var source = new CachedPrizeSpellSource(facts, rules);
        PrizeSpellPolicy ignored;
        False(source.TryGet("UNKNOWN", out ignored), "unknown first");
        False(source.TryGet("UNKNOWN", out ignored), "unknown second");
        Equal(1, facts.UnknownCalls, "unknown fact calls");

        False(source.TryGet("RULE_FAIL", out ignored), "rule failure first");
        False(source.TryGet("RULE_FAIL", out ignored), "rule failure second");
        Equal(1, facts.RuleFailCalls, "rule-fail fact calls");
        Equal(1, rules.RuleFailCalls, "rule-fail evaluator calls");
        False(source.TryGet("", out ignored), "empty id");
    }

    private static void InvalidDerivedIdentityFailsClosed()
    {
        var source = new CachedPrizeSpellSource(new CountingFacts(), new InvalidRules());
        PrizeSpellPolicy ignored;
        False(source.TryGet("VALID", out ignored), "mismatched derived CardId");
        False(source.TryGet("VALID", out ignored), "mismatched result cached");
    }

    private static void True(bool value, string label)
    {
        if (!value) throw new Exception(label + " expected true");
        Interlocked.Increment(ref _assertions);
    }

    private static void False(bool value, string label)
    {
        if (value) throw new Exception(label + " expected false");
        Interlocked.Increment(ref _assertions);
    }

    internal static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception(label + " expected=" + expected + " actual=" + actual);
        Interlocked.Increment(ref _assertions);
    }

    private sealed class CountingFacts : IPrizeSpellFactSource
    {
        public int ValidCalls;
        public int UnknownCalls;
        public int RuleFailCalls;
        public readonly PrizeSpellFact ValidFact = NewFact("VALID", "发现一个随从。", 2);

        public bool TryGet(string cardId, out PrizeSpellFact fact)
        {
            fact = null;
            if (cardId == "VALID")
            {
                ValidCalls++;
                fact = ValidFact;
                return true;
            }
            if (cardId == "RULE_FAIL")
            {
                RuleFailCalls++;
                fact = NewFact(cardId, "unmatched", 1);
                return true;
            }
            UnknownCalls++;
            return false;
        }
    }

    private sealed class CountingRules : IPrizeSpellRuleEvaluator
    {
        public int Calls;
        public int RuleFailCalls;

        public bool TryEvaluate(PrizeSpellFact fact, out PrizeSpellPolicy policy)
        {
            policy = null;
            if (fact.CardId == "RULE_FAIL")
            {
                RuleFailCalls++;
                return false;
            }
            Calls++;
            policy = new PrizeSpellPolicy(fact.CardId, fact.PrizeTier, PrizeSpellRole.Discover);
            return true;
        }
    }

    private sealed class InvalidRules : IPrizeSpellRuleEvaluator
    {
        public bool TryEvaluate(PrizeSpellFact fact, out PrizeSpellPolicy policy)
        {
            policy = new PrizeSpellPolicy("OTHER", fact.PrizeTier, PrizeSpellRole.Discover);
            return true;
        }
    }

    private static PrizeSpellFact NewFact(string cardId, string text, int tier)
    {
        return new PrizeSpellFact
        {
            CardId = cardId,
            CardType = PrizeSpellCardType.Spell,
            PrizeTier = tier,
            TechLevel = tier,
            TextZhCn = text,
            ScriptData = new[] { 0, 0, 0, 0, 0, 0 },
        };
    }
}
