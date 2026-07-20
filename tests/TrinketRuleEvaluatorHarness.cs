using System;
using BobCoach.Engine;

internal static class TrinketRuleEvaluatorHarness
{
    private static int Main()
    {
        var evaluator = new TrinketRuleEvaluator();
        var keywordName = evaluator.Evaluate(new TrinketFact
        {
            CardId = "NAME_KEYWORDS",
            IsLesser = true,
            Cost = 0,
            NameZhCn = "金币刷新获取圣盾",
            NameEnUs = "Gold Refresh Get Divine Shield",
            TextZhCn = "",
            TextEnUs = "",
        }, "");
        var plainName = evaluator.Evaluate(new TrinketFact
        {
            CardId = "PLAIN_NAME",
            IsLesser = true,
            NameZhCn = "普通名称",
            NameEnUs = "Plain Name",
            TextZhCn = "",
            TextEnUs = "",
        }, "");

        if (keywordName.IsRated || plainName.IsRated
            || keywordName.RuleScore != 0 || plainName.RuleScore != 0)
            return Fail("display names changed the rule evaluation");

        var bilingualGeneration = evaluator.Evaluate(new TrinketFact
        {
            CardId = "BILINGUAL_GENERATION",
            IsLesser = true,
            TextZhCn = "<b>获取</b>一张随从牌。",
            TextEnUs = "<b>Get</b> a minion.",
        }, "");
        if (!bilingualGeneration.IsRated || bilingualGeneration.RuleScore != 1
            || bilingualGeneration.MatchedRuleIds.Count != 1
            || bilingualGeneration.MatchedRuleIds[0] != "generation")
            return Fail("bilingual generation evidence was not counted exactly once");

        var combatStart = evaluator.Evaluate(new TrinketFact
        {
            CardId = "COMBAT_START_ONLY",
            TextZhCn = "战斗开始时：召唤一个随从。",
            TextEnUs = "Start of Combat: Summon a minion.",
        }, "");
        var recurringTurn = evaluator.Evaluate(new TrinketFact
        {
            CardId = "RECURRING_TURN",
            TextZhCn = "在每个回合结束时，使一个随从获得+1/+1。",
            TextEnUs = "At the end of every turn, give a minion +1/+1.",
        }, "");
        if (combatStart.IsRated || combatStart.RuleScore != 0
            || !recurringTurn.IsRated || recurringTurn.RuleScore != 1
            || recurringTurn.MatchedRuleIds.Count != 1
            || recurringTurn.MatchedRuleIds[0] != "scaling")
            return Fail("turn scaling and start-of-combat timing were not distinguished");

        var goldenOnly = evaluator.Evaluate(new TrinketFact
        {
            CardId = "GOLDEN_ONLY",
            TextEnUs = "Make a minion Golden.",
        }, "");
        var economy = evaluator.Evaluate(new TrinketFact
        {
            CardId = "ECONOMY",
            TextZhCn = "获得1枚金币并刷新酒馆。",
            TextEnUs = "Gain 1 Gold and Refresh the Tavern.",
        }, "");
        if (goldenOnly.IsRated || goldenOnly.RuleScore != 0
            || !economy.IsRated || economy.RuleScore != 1
            || economy.MatchedRuleIds.Count != 1
            || economy.MatchedRuleIds[0] != "economy")
            return Fail("economy evidence or the Gold/Golden word boundary was incorrect");

        var protection = evaluator.Evaluate(new TrinketFact
        {
            CardId = "PROTECTION",
            TextZhCn = "使一个随从获得圣盾和烈毒。",
            TextEnUs = "Give a minion Divine Shield and Venomous.",
        }, "");
        if (!protection.IsRated || protection.RuleScore != 1
            || protection.MatchedRuleIds.Count != 1
            || protection.MatchedRuleIds[0] != "protection")
            return Fail("bilingual protection evidence was not counted exactly once");

        var generationThenReplacement = evaluator.Evaluate(new TrinketFact
        {
            CardId = "GENERATION_REPLACEMENT",
            TextEnUs = "Choose a greater Trinket and replace this.",
        }, "");
        var replacementOnly = evaluator.Evaluate(new TrinketFact
        {
            CardId = "REPLACEMENT_ONLY",
            TextZhCn = "替换本饰品。",
        }, "");
        if (!generationThenReplacement.IsRated || generationThenReplacement.RuleScore != 0
            || generationThenReplacement.MatchedRuleIds.Count != 2
            || !generationThenReplacement.MatchedRuleIds.Contains("replacement")
            || replacementOnly.IsRated || replacementOnly.RuleScore != -1
            || replacementOnly.MatchedRuleIds.Count != 1
            || replacementOnly.MatchedRuleIds[0] != "replacement")
            return Fail("replacement penalty changed rated status or was not exactly -1");

        var explanationOnly = evaluator.Evaluate(new TrinketFact
        {
            CardId = "EXPLANATION_ONLY",
            TextZhCn = "复仇（3）：使一个随从变为金色。",
            TextEnUs = "Avenge (3): Make a minion Golden.",
        }, "");
        if (explanationOnly.IsRated || explanationOnly.RuleScore != 0
            || explanationOnly.MatchedRuleIds.Count != 2
            || !explanationOnly.MatchedRuleIds.Contains("avenge")
            || !explanationOnly.MatchedRuleIds.Contains("golden"))
            return Fail("Avenge/Golden explanation tags incorrectly changed the score or rated gate");

        var ratedBeast = new TrinketFact
        {
            CardId = "RATED_BEAST",
            TextZhCn = "获取一张野兽牌。",
            TextEnUs = "Get a Beast.",
        };
        var exactTribe = evaluator.Evaluate(ratedBeast, "野兽");
        var substringTribe = evaluator.Evaluate(ratedBeast, "野");
        var tribeOnly = evaluator.Evaluate(new TrinketFact
        {
            CardId = "TRIBE_ONLY",
            TextZhCn = "你的野兽拥有嘲讽。",
            TextEnUs = "Your Beasts have Taunt.",
        }, "野兽");
        if (!exactTribe.IsRated || exactTribe.RuleScore != 2
            || exactTribe.MatchedTribes.Count != 1 || exactTribe.MatchedTribes[0] != "野兽"
            || substringTribe.RuleScore != 1
            || tribeOnly.IsRated || tribeOnly.RuleScore != 0
            || tribeOnly.MatchedTribes.Count != 1)
            return Fail("exact dominant-tribe bonus or the unrated tribe gate was incorrect");

        exactTribe.MatchedRuleIds.Clear();
        exactTribe.MatchedTribes.Clear();
        var repeatedWithoutContext = evaluator.Evaluate(ratedBeast, "");
        var repeatedWithContext = evaluator.Evaluate(ratedBeast, "野兽");
        if (repeatedWithoutContext.RuleScore != 1
            || repeatedWithoutContext.MatchedRuleIds.Count != 1
            || repeatedWithoutContext.MatchedRuleIds[0] != "generation"
            || repeatedWithoutContext.MatchedTribes.Count != 1
            || repeatedWithContext.RuleScore != 2
            || !repeatedWithContext.MatchedRuleIds.Contains("dominant_tribe")
            || repeatedWithContext.MatchedTribes.Count != 1)
            return Fail("repeated CardId evaluation leaked context or mutable result state");

        Console.WriteLine("PASS original rule weights through exact dominant-tribe context");
        return 0;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine("FAIL " + message);
        return 1;
    }
}
