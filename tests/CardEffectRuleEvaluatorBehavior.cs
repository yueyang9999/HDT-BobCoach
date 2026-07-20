using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BobCoach.Engine;

internal static class CardEffectRuleEvaluatorBehavior
{
    private static int Main()
    {
        try
        {
            AssertSignature("战吼：获得1枚铸币。", "generate_gold=1/once");
            AssertSignature("在你的回合结束时，获得1枚铸币。", "generate_gold=1/turn");
            AssertSignature("发现一张鱼人牌。", "discover=2.5/once@鱼人");
            AssertSignature("出售本随从时，获取一张法术牌。", "sell_generate=1.5/once");
            AssertSignature("获取一张酒馆法术牌。", "generate_spell=1.2/once");
            AssertSignature("获取一张随从牌。", "generate_card=2/once");
            AssertSignature("你的铸币上限提高。", "gold_cap=1/permanent");
            AssertSignature("抉择：使一个鱼人获得+1/+1。", "tribe_buff=1/once@鱼人");
            AssertSignature("召唤一个随从，仅限本场战斗。", "combat_summon=0.5/once");
            AssertSignature("亡语：消灭击杀本随从的随从。", "combat_removal=1/once");
            AssertSignature("你的战吼额外触发一次。", "amplifier=0/");
            AssertSignature("花掉铸币后获得+1/+1。", "");
            AssertSignature("使一个随从获得+2/+2。", "");
            AssertSignature("", "");
            AssertSignature("仅限本场，发现一张鱼人牌。", "discover=1.25/once@鱼人");
            AssertSignature(
                "获得1枚铸币。额外免费刷新。发现一张鱼人牌。你的战吼额外触发一次。",
                "generate_gold=1/once;free_refresh=1/once;discover=2.5/once@鱼人;amplifier=0/");

            AssertSignature(
                "在每{0}次刷新后，获得一次免费刷新。",
                "free_refresh=2/once",
                new[] { 3, 0, 0, 0, 0, 0 });
            AssertSignature(
                "在每{0}次刷新后，获得一次免费刷新。",
                "free_refresh=1/once",
                new[] { 0, 0, 0, 0, 0, 0 });
            AssertSignature(
                "当你出售本随从时，使你的随从获得+{0}/+{1}。",
                "sell_generate=1.5/once",
                new[] { 1, 0, 0, 0, 0, 0 });
            AssertEvaluationFails(
                "获取{6}张牌。",
                new[] { 1, 1, 1, 1, 1, 1 });
            AssertEvaluationFails("任意文本", new[] { 1, 2 });
            AssertUnsupportedTypeFails();

            Console.WriteLine("PASS card effect rule evaluator preserves the 11 approved effect contracts");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL " + ex.Message);
            return 1;
        }
    }

    private static void AssertSignature(
        string text, string expected, IReadOnlyList<int> scriptData = null)
    {
        var fact = new CardEffectFact
        {
            CardId = "TEST_CARD",
            CardType = CardEffectCardType.Minion,
            TextZhCn = text,
            ScriptData = scriptData ?? new[] { 0, 0, 0, 0, 0, 0 },
            Attack = 1,
            Health = 1,
            Tier = 1,
        };
        IReadOnlyList<CardEffectDefinition> effects;
        if (!new CardEffectRuleEvaluator().TryEvaluate(fact, out effects))
            throw new InvalidOperationException("evaluation unexpectedly failed for: " + text);
        string actual = Signature(effects);
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            throw new InvalidOperationException("signature mismatch expected=" + expected + " actual=" + actual);
    }

    private static void AssertEvaluationFails(string text, IReadOnlyList<int> scriptData)
    {
        var fact = new CardEffectFact
        {
            CardId = "TEST_CARD",
            CardType = CardEffectCardType.Minion,
            TextZhCn = text,
            ScriptData = scriptData,
            Attack = 1,
            Health = 1,
            Tier = 1,
        };
        IReadOnlyList<CardEffectDefinition> effects;
        if (new CardEffectRuleEvaluator().TryEvaluate(fact, out effects))
            throw new InvalidOperationException("invalid dynamic text did not fail closed: " + text);
    }

    private static void AssertUnsupportedTypeFails()
    {
        var fact = new CardEffectFact
        {
            CardId = "TEST_HERO",
            CardType = CardEffectCardType.Unknown,
            TextZhCn = "发现一张牌。",
            ScriptData = new[] { 0, 0, 0, 0, 0, 0 },
        };
        IReadOnlyList<CardEffectDefinition> effects;
        if (new CardEffectRuleEvaluator().TryEvaluate(fact, out effects))
            throw new InvalidOperationException("unsupported card type did not fail closed");
    }

    private static string Signature(IEnumerable<CardEffectDefinition> effects)
    {
        return string.Join(";", (effects ?? new CardEffectDefinition[0]).Select(effect =>
            effect.Type + "="
            + effect.ValueGold.ToString("0.###############", CultureInfo.InvariantCulture)
            + "/" + effect.Per
            + (string.IsNullOrEmpty(effect.Tribe) ? "" : "@" + effect.Tribe)));
    }
}
