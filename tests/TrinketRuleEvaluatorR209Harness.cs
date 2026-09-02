using System;
using System.Collections.Generic;
using BobCoach.Engine;

internal static class TrinketRuleEvaluatorR209Harness
{
    private static int Fail(string msg)
    {
        Console.WriteLine("FAIL: " + msg);
        return 1;
    }

    private static int Main()
    {
        var evaluator = new TrinketRuleEvaluator();

        // ── 储备型: 每回合获得资源 → cross_turn +1 ──
        var stockpile = evaluator.Evaluate(new TrinketFact
        {
            CardId = "STOCKPILE",
            TextZhCn = "在每个回合开始时，获得1枚铸币。",
            TextEnUs = "At the start of each turn, gain 1 Gold.",
        }, "");
        if (stockpile.RuleScore < 1 || !stockpile.MatchedRuleIds.Contains("cross_turn"))
            return Fail("stockpile cross-turn not applied (score=" + stockpile.RuleScore + ")");

        // ── 变现型: 卖出获得 → cross_turn +1 ──
        var liquidate = evaluator.Evaluate(new TrinketFact
        {
            CardId = "LIQUIDATE",
            TextZhCn = "当你出售一个随从时，获得2枚铸币。",
            TextEnUs = "When you sell a minion, gain 2 Gold.",
        }, "");
        if (liquidate.RuleScore < 1 || !liquidate.MatchedRuleIds.Contains("cross_turn"))
            return Fail("liquidate cross-turn not applied (score=" + liquidate.RuleScore + ")");

        // ── 杠杆型: 发现高本随从 → cross_turn +1 ──
        var leverage = evaluator.Evaluate(new TrinketFact
        {
            CardId = "LEVERAGE",
            TextZhCn = "发现一个等级7的随从。",
            TextEnUs = "Discover a Tier 7 minion.",
        }, "");
        if (leverage.RuleScore < 1 || !leverage.MatchedRuleIds.Contains("cross_turn"))
            return Fail("leverage cross-turn not applied (score=" + leverage.RuleScore + ")");

        // ── 三连奖品(杠杆型) ──
        var triple = evaluator.Evaluate(new TrinketFact
        {
            CardId = "TRIPLE_REWARD",
            TextZhCn = "你的三连奖品额外触发一次。",
            TextEnUs = "Your Triple reward triggers an extra time.",
        }, "");
        if (!triple.MatchedRuleIds.Contains("cross_turn"))
            return Fail("triple reward cross-turn not applied");

        // ── 非跨回合饰品: 不加 cross_turn ──
        var plain = evaluator.Evaluate(new TrinketFact
        {
            CardId = "PLAIN_BUFF",
            TextZhCn = "使一个随从获得+2/+2。",
            TextEnUs = "Give a minion +2/+2.",
        }, "");
        if (plain.MatchedRuleIds.Contains("cross_turn"))
            return Fail("plain buff wrongly got cross_turn");

        // ── cross_turn 不影响评级门控(纯 buff 饰品仍不评级) ──
        if (plain.IsRated)
            return Fail("plain buff became rated");

        Console.WriteLine("PASS R209 cross-turn value recognition");
        return 0;
    }
}
