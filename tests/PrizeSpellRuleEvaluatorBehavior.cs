using System;
using System.Collections.Generic;
using BobCoach.Engine;

internal static class PrizeSpellRuleEvaluatorBehavior
{
    private static int _assertions;

    private static int Main()
    {
        try
        {
            var rules = new PrizeSpellRuleEvaluator();
            AssertRole(rules, "获取2张香蕉果盘。", 1,
                PrizeSpellCardType.Spell, PrizeSpellRole.Economy);
            AssertRole(rules, "获得1枚铸币。在回合结束时，将本牌移回你的手牌。", 2,
                PrizeSpellCardType.Spell, PrizeSpellRole.Economy);
            AssertRole(rules, "<b>发现</b>一个新的英雄技能。", 3,
                PrizeSpellCardType.Spell, PrizeSpellRole.Utility);
            AssertRole(rules, "使酒馆中的随从在本局对战中获得+{0}/+{1}。", 1,
                PrizeSpellCardType.Spell, PrizeSpellRole.Scaling,
                new[] { 1, 1, 0, 0, 0, 0 });
            AssertRole(rules, "发现一张\r\n你当前等级的酒馆法术牌。", 2,
                PrizeSpellCardType.Spell, PrizeSpellRole.Discover);
            AssertRole(rules, "将酒馆中的所有牌替换为高一级的牌。", 2,
                PrizeSpellCardType.Spell, PrizeSpellRole.Tempo);
            AssertRole(rules, "战斗开始时：获得最左攻击力和最右生命值。", 4,
                PrizeSpellCardType.Minion, PrizeSpellRole.Minion);
            AssertRole(rules, "获得{0}枚铸币。", 4,
                PrizeSpellCardType.Spell, PrizeSpellRole.Economy,
                new[] { 0, 0, 0, 0, 0, 0 });

            AssertFails(rules, Fact("获得{6}枚铸币。", 1, PrizeSpellCardType.Spell,
                new[] { 1, 1, 1, 1, 1, 1 }));
            AssertFails(rules, Fact("获得{0}枚铸币。", 1, PrizeSpellCardType.Spell,
                new[] { 1, 1, 1, 1, 1 }));
            var tierMismatch = Fact("获得1枚铸币。", 1, PrizeSpellCardType.Spell);
            tierMismatch.TechLevel = 2;
            AssertFails(rules, tierMismatch);
            AssertFails(rules, Fact("", 1, PrizeSpellCardType.Spell));
            AssertFails(rules, Fact("获得1枚铸币。", 1, PrizeSpellCardType.Unknown));

            AssertScore(2.0, Policy(1, PrizeSpellRole.Economy), 2, 4, 30);
            AssertScore(3.0, Policy(2, PrizeSpellRole.Tempo), 10, 2, 10);
            AssertScore(1.5, Policy(3, PrizeSpellRole.Scaling), 10, 5, 30);
            AssertScore(1.0, Policy(4, PrizeSpellRole.Discover), 10, 4, 30);
            AssertScore(0.0, Policy(3, PrizeSpellRole.Discover), 10, 4, 30);
            AssertScore(0.0, null, 2, 2, 10);

            Console.WriteLine("PASS prize spell roles and current context scores assertions=" + _assertions);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL " + ex.Message);
            return 1;
        }
    }

    private static PrizeSpellFact Fact(
        string text,
        int tier,
        PrizeSpellCardType cardType,
        IReadOnlyList<int> scriptData = null)
    {
        return new PrizeSpellFact
        {
            CardId = "TEST_PRIZE",
            CardType = cardType,
            PrizeTier = tier,
            TechLevel = tier,
            TextZhCn = text,
            ScriptData = scriptData ?? new[] { 0, 0, 0, 0, 0, 0 },
        };
    }

    private static PrizeSpellPolicy Policy(int tier, PrizeSpellRole role)
    {
        return new PrizeSpellPolicy("TEST_PRIZE", tier, role);
    }

    private static void AssertRole(
        PrizeSpellRuleEvaluator rules,
        string text,
        int tier,
        PrizeSpellCardType cardType,
        PrizeSpellRole expected,
        IReadOnlyList<int> scriptData = null)
    {
        PrizeSpellPolicy actual;
        if (!rules.TryEvaluate(Fact(text, tier, cardType, scriptData), out actual))
            throw new Exception("expected role evaluation success: " + expected);
        Equal(expected, actual.Role, "role");
        Equal(tier, actual.PrizeTier, "tier");
        Equal("TEST_PRIZE", actual.CardId, "CardId");
    }

    private static void AssertFails(PrizeSpellRuleEvaluator rules, PrizeSpellFact fact)
    {
        PrizeSpellPolicy policy;
        if (rules.TryEvaluate(fact, out policy)) throw new Exception("expected rule failure");
        if (policy != null) throw new Exception("failed rule returned a policy");
        _assertions++;
    }

    private static void AssertScore(
        double expected,
        PrizeSpellPolicy policy,
        int gold,
        int boardCount,
        int hitPoints)
    {
        double actual = PrizeSpellScorer.Score(policy, gold, boardCount, hitPoints);
        if (Math.Abs(expected - actual) > 0.000001)
            throw new Exception("score expected=" + expected + " actual=" + actual);
        _assertions++;
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception(label + " expected=" + expected + " actual=" + actual);
        _assertions++;
    }
}
