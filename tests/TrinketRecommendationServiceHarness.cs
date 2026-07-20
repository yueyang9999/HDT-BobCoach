using System;
using System.Collections.Generic;
using System.Linq;
using BobCoach.Engine;

internal static class TrinketRecommendationServiceHarness
{
    private static int Main()
    {
        var countingSource = new CountingTrinketFactSource();
        countingSource.Add(new TrinketFact
        {
            CardId = "CACHE_ME",
            NameZhCn = "缓存饰品",
            TextEnUs = "Get a minion.",
        });
        var cachedSource = new CachedTrinketFactSource(countingSource);
        TrinketFact cachedFact;
        if (!cachedSource.TryGet("CACHE_ME", out cachedFact)
            || !cachedSource.TryGet("CACHE_ME", out cachedFact)
            || cachedFact.CardId != "CACHE_ME"
            || countingSource.QueryCount != 1)
            return Fail("repeated CardId lookup bypassed the process-memory fact cache");

        var source = new InMemoryTrinketFactSource();
        source.Add(new TrinketFact
        {
            CardId = "RATED",
            IsLesser = true,
            Cost = 0,
            NameZhCn = "已评分",
            NameEnUs = "Rated",
            TextZhCn = "获取一张随从牌。",
            TextEnUs = "Get a minion.",
        });
        var service = new TrinketRecommendationService(
            source, new TrinketRuleEvaluator());
        var results = service.Evaluate(new List<TrinketOption>
        {
            new TrinketOption { CardId = "__TRINKET_PENDING_LESSER", TrinketName = "加载中" },
            new TrinketOption { CardId = "UNKNOWN", TrinketName = "未知候选" },
            new TrinketOption { CardId = "RATED", TrinketName = "旧显示名" },
        }, "");

        if (results.Count != 2
            || results[0].CardId != "RATED" || results[0].Index != 2 || results[0].IsUnrated
            || results[0].DisplayName != "已评分"
            || results[1].CardId != "UNKNOWN" || results[1].Index != 1 || !results[1].IsUnrated)
            return Fail("pending placeholders were not ignored while real indices stayed stable");

        source.Add(new TrinketFact
        {
            CardId = "HIGH_Z", TextEnUs = "Get 1 Gold.",
        });
        source.Add(new TrinketFact
        {
            CardId = "TIE_B", TextEnUs = "Get a minion.",
        });
        source.Add(new TrinketFact
        {
            CardId = "TIE_A", TextEnUs = "Get a minion.",
        });
        var ordered = service.Evaluate(new List<TrinketOption>
        {
            new TrinketOption { CardId = "TIE_B" },
            new TrinketOption { CardId = "HIGH_Z" },
            new TrinketOption { CardId = "TIE_A" },
        }, "");
        if (ordered.Count != 3 || ordered[0].CardId != "HIGH_Z"
            || ordered[1].CardId != "TIE_A" || ordered[2].CardId != "TIE_B"
            || ordered[0].RuleScore != 2)
            return Fail("score/CardId/Index ordering was not deterministic");

        var throwing = new TrinketRecommendationService(
            new ThrowingTrinketFactSource(), new TrinketRuleEvaluator());
        var failedClosed = throwing.Evaluate(new List<TrinketOption>
        {
            new TrinketOption { CardId = "THROWS", TrinketName = "保留名称" },
        }, "野兽");
        if (failedClosed.Count != 1 || !failedClosed[0].IsUnrated
            || failedClosed[0].RuleScore != 0
            || failedClosed[0].MatchedRuleIds.Count != 0
            || failedClosed[0].DisplayName != "保留名称")
            return Fail("fact-source exception did not fail closed to an unrated candidate");

        var mismatched = new TrinketRecommendationService(
            new MismatchedTrinketFactSource(), new TrinketRuleEvaluator());
        var mismatchResult = mismatched.Evaluate(new List<TrinketOption>
        {
            new TrinketOption { CardId = "REQUESTED", TrinketName = "报价名称" },
        }, "野兽");
        if (mismatchResult.Count != 1 || !mismatchResult[0].IsUnrated
            || mismatchResult[0].RuleScore != 0
            || mismatchResult[0].MatchedRuleIds.Count != 0
            || mismatchResult[0].DisplayName != "报价名称")
            return Fail("mismatched local CardId was accepted as a rated candidate");

        var allUnratedSource = new InMemoryTrinketFactSource();
        allUnratedSource.Add(new TrinketFact { CardId = "UNRATED_Z", NameZhCn = "未知Z" });
        allUnratedSource.Add(new TrinketFact { CardId = "UNRATED_A", NameZhCn = "未知A" });
        var allUnrated = new TrinketRecommendationService(
            allUnratedSource, new TrinketRuleEvaluator()).Evaluate(
            new List<TrinketOption>
            {
                new TrinketOption { CardId = "UNRATED_Z" },
                new TrinketOption { CardId = "UNRATED_A" },
            }, "野兽");
        if (allUnrated.Count != 2 || allUnrated[0].CardId != "UNRATED_A"
            || allUnrated[1].CardId != "UNRATED_Z"
            || allUnrated.Any(x => !x.IsUnrated || x.RuleScore != 0
                || x.MatchedRuleIds.Count != 0))
            return Fail("all-unrated offers did not remain unknown in stable full order");

        var duplicateIds = service.Evaluate(new List<TrinketOption>
        {
            new TrinketOption { CardId = "RATED", TrinketName = "第一个" },
            new TrinketOption { CardId = "RATED", TrinketName = "第二个" },
        }, "");
        if (duplicateIds.Count != 2 || duplicateIds[0].Index != 0
            || duplicateIds[1].Index != 1)
            return Fail("duplicate CardId offers were not ordered by original index");

        Console.WriteLine("PASS cached facts, failure closure, and deterministic full-order contracts");
        return 0;
    }

    private sealed class InMemoryTrinketFactSource : ITrinketFactSource
    {
        private readonly Dictionary<string, TrinketFact> _facts
            = new Dictionary<string, TrinketFact>(StringComparer.Ordinal);

        public void Add(TrinketFact fact) { _facts[fact.CardId] = fact; }

        public bool TryGet(string cardId, out TrinketFact fact)
        {
            return _facts.TryGetValue(cardId, out fact);
        }
    }

    private sealed class CountingTrinketFactSource : ITrinketFactSource
    {
        private readonly Dictionary<string, TrinketFact> _facts
            = new Dictionary<string, TrinketFact>(StringComparer.Ordinal);

        public int QueryCount { get; private set; }

        public void Add(TrinketFact fact) { _facts[fact.CardId] = fact; }

        public bool TryGet(string cardId, out TrinketFact fact)
        {
            QueryCount++;
            return _facts.TryGetValue(cardId, out fact);
        }
    }

    private sealed class ThrowingTrinketFactSource : ITrinketFactSource
    {
        public bool TryGet(string cardId, out TrinketFact fact)
        {
            fact = new TrinketFact();
            throw new InvalidOperationException("simulated local-source failure");
        }
    }

    private sealed class MismatchedTrinketFactSource : ITrinketFactSource
    {
        public bool TryGet(string cardId, out TrinketFact fact)
        {
            fact = new TrinketFact
            {
                CardId = "OTHER",
                NameZhCn = "错误事实",
                TextZhCn = "获取一张野兽牌。",
            };
            return true;
        }
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine("FAIL " + message);
        return 1;
    }
}
