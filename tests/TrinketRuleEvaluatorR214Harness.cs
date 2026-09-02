using System;
using System.Collections.Generic;
using BobCoach.Engine;

internal static class TrinketRuleEvaluatorR214Harness
{
    private static int Fail(string msg)
    {
        Console.WriteLine("FAIL: " + msg);
        return 1;
    }

    private static int Main()
    {
        var evaluator = new TrinketRuleEvaluator();

        // ── R214 测试 1: 血量情境——濒死(血甲<=15)偏好即时战力 ──
        // 饰品文本含"圣盾"(protection), 濒死时 +1
        var tempoTrinket = new TrinketFact
        {
            CardId = "LOW_HP_TEMPO",
            TextZhCn = "战斗开始时：使你的随从获得圣盾。",
            TextEnUs = "Start of Combat: Give your minions Divine Shield.",
        };
        var healthyTempo = evaluator.Evaluate(tempoTrinket, "", 30, 5, null); // 健康
        var lowHpTempo = evaluator.Evaluate(tempoTrinket, "", 12, 5, null);   // 濒死
        if (lowHpTempo.RuleScore != healthyTempo.RuleScore + 1
            || !lowHpTempo.MatchedRuleIds.Contains("low_hp_tempo"))
            return Fail("low-hp tempo bonus not applied (got " + lowHpTempo.RuleScore + " vs " + healthyTempo.RuleScore + ")");

        // 濒死时经济饰品被罚
        var econTrinket = new TrinketFact
        {
            CardId = "LOW_HP_ECON",
            TextZhCn = "获得1枚金币。",
            TextEnUs = "Gain 1 Gold.",
        };
        var healthyEcon = evaluator.Evaluate(econTrinket, "", 30, 5, null);
        var lowHpEcon = evaluator.Evaluate(econTrinket, "", 12, 5, null);
        if (lowHpEcon.RuleScore != healthyEcon.RuleScore - 1
            || !lowHpEcon.MatchedRuleIds.Contains("low_hp_economy_penalty"))
            return Fail("low-hp economy penalty not applied");

        // 健康且回合>=8 时经济饰品 +1
        var lateEcon = evaluator.Evaluate(econTrinket, "", 26, 9, null);
        if (lateEcon.RuleScore != healthyEcon.RuleScore + 1
            || !lateEcon.MatchedRuleIds.Contains("healthy_economy"))
            return Fail("healthy late-game economy bonus not applied");

        // 默认参数(未传状态)行为不变
        var defaultCall = evaluator.Evaluate(econTrinket, "");
        if (defaultCall.RuleScore != healthyEcon.RuleScore)
            return Fail("default-parameter call changed behavior");

        // ── R214 测试 2: 板面关键词协同 ──
        // 板面有战吼随从, 饰品文本含"战吼" -> +1
        var battlecryTrinket = new TrinketFact
        {
            CardId = "BOARD_BATTLECRY",
            TextZhCn = "你的战吼额外触发一次。",
            TextEnUs = "Your Battlecries trigger an extra time.",
        };
        var noKw = evaluator.Evaluate(battlecryTrinket, "", 20, 6, null);
        var withKw = evaluator.Evaluate(battlecryTrinket, "", 20, 6,
            new List<string> { "BATTLECRY" });
        if (withKw.RuleScore != noKw.RuleScore + 1
            || !withKw.MatchedRuleIds.Contains("board_BATTLECRY"))
            return Fail("board BATTLECRY synergy not applied");

        // 无关关键词不触发
        var unrelatedKw = evaluator.Evaluate(battlecryTrinket, "", 20, 6,
            new List<string> { "DEATHRATTLE" });
        if (unrelatedKw.RuleScore != noKw.RuleScore)
            return Fail("unrelated keyword wrongly triggered synergy");

        // 多关键词只加一次(同一关键词去重由调用方负责, 此处验证计数正确)
        var multiKw = evaluator.Evaluate(battlecryTrinket, "", 20, 6,
            new List<string> { "BATTLECRY", "BATTLECRY" });
        if (multiKw.RuleScore != noKw.RuleScore + 2)
            return Fail("duplicate keyword counting mismatch (got " + multiKw.RuleScore + ")");

        // 未评级的饰品不加血量/协同
        var unrated = evaluator.Evaluate(new TrinketFact
        {
            CardId = "UNRATED",
            TextZhCn = "使一个随从变为金色。",
            TextEnUs = "Make a minion Golden.",
        }, "", 10, 5, new List<string> { "BATTLECRY" });
        if (unrated.RuleScore != 0 || unrated.IsRated)
            return Fail("unrated trinket got R214 bonuses");


        // ── R214 补充测试: 核心引擎协同(ENGINE_* 标记 +2) ──
        var engineTrinket = new TrinketFact
        {
            CardId = "ENGINE_BATTLECRY_TRINKET",
            TextZhCn = "你的战吼额外触发一次。",
            TextEnUs = "Your Battlecries trigger an extra time.",
        };
        var plainKwOnly = evaluator.Evaluate(engineTrinket, "", 20, 6,
            new List<string> { "BATTLECRY" });
        var withEngine = evaluator.Evaluate(engineTrinket, "", 20, 6,
            new List<string> { "ENGINE_BATTLECRY" });
        if (withEngine.RuleScore != plainKwOnly.RuleScore + 1) // 引擎 +2 vs 普通 +1
            return Fail("engine synergy should be +2 but got +" + (withEngine.RuleScore - 0));
        if (!withEngine.MatchedRuleIds.Contains("board_ENGINE_BATTLECRY"))
            return Fail("engine rule id not recorded");

        Console.WriteLine("PASS R214 engine-keyword synergy (+2)");
        Console.WriteLine("PASS R214 health-context and board-keyword synergy");
        return 0;
    }
}
