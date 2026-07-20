using System;
using System.Collections.Generic;
using System.Linq;
using BobCoach.Engine;

internal static class HeroStrategyRuleEvaluatorBehavior
{
    private static int _assertions;

    private static int Main()
    {
        try
        {
            var evaluator = new HeroStrategyRuleEvaluator();
            AssertFails(evaluator, null, "null fact");
            AssertFails(evaluator, Fact(text: ""), "empty text");
            AssertFails(evaluator, Fact(heroArmor: -1), "negative armor");
            AssertFails(evaluator, Fact(powerCost: 21), "cost above boundary");
            AssertFails(evaluator, Fact(heroCardId: ""), "empty hero identity");
            AssertFails(evaluator, Fact(powerCardId: ""), "empty power identity");

            AssertType(evaluator, Fact(powerCost: 3, hideCost: true),
                HeroPowerType.Passive, 0, "HIDE_COST precedence");
            AssertType(evaluator, Fact(powerCost: 2, activated: true),
                HeroPowerType.Passive, 0, "activated precedence");
            AssertType(evaluator, Fact(powerCost: 2),
                HeroPowerType.Active, 2, "positive cost active");
            AssertType(evaluator, Fact(powerCost: 0, text: "战斗开始时：获得效果。"),
                HeroPowerType.Passive, 0, "start-of-combat passive");
            AssertType(evaluator, Fact(powerCost: 0,
                    text: "选择一个随从。在战斗开始时，使其获得效果。"),
                HeroPowerType.Active, 0, "nested trigger remains active");
            AssertType(evaluator, Fact(powerCost: 0, text: "每局一次，使一个随从获得效果。"),
                HeroPowerType.Active, 0, "per-game allowance remains active");
            AssertType(evaluator, Fact(powerCost: 0, text: "在每个回合开始时获得效果。"),
                HeroPowerType.Passive, 0, "recurring turn passive");

            HeroStrategy discover;
            AssertTrue(evaluator.TryEvaluate(Fact(
                text: " <b>发现</b> 一张\r\n牌。", powerCost: 1), out discover),
                "HTML and whitespace normalize");
            AssertEqual(HeroArchetype.Greed, discover.Archetype, "discover archetype");
            AssertEqual(HeroUsePurpose.Resource, discover.UsePurpose, "discover purpose");
            AssertTrue(discover.HasDiscover, "discover flag");
            AssertTrue(discover.SynergyTags.SetEquals(new[] { "DISCOVER" }), "discover tag");
            AssertEqual("", discover.HeroName, "hero name excluded");
            AssertEqual("", discover.PowerHint, "power text excluded");

            HeroStrategy adjacentMarkup;
            AssertTrue(evaluator.TryEvaluate(Fact(
                text: "发现一个<b>英雄技能</b>。"), out adjacentMarkup),
                "adjacent HTML markup evaluates");
            AssertEqual("FINLEY", adjacentMarkup.SpecialRule,
                "HTML removal preserves adjacent rule tokens");

            HeroStrategy scripted;
            AssertTrue(evaluator.TryEvaluate(Fact(
                text: "使一个随从获得+{0}/+{1}。", scriptData: new[] { 2, 3, 0, 0, 0, 0 }),
                out scripted), "script placeholders expand");
            AssertEqual(HeroArchetype.Tempo, scripted.Archetype, "scripted buff archetype");
            AssertEqual(HeroUsePurpose.Buff, scripted.UsePurpose, "scripted buff purpose");

            HeroStrategy unlock;
            AssertTrue(evaluator.TryEvaluate(Fact(
                text: "第7回合解锁。等级4时解锁。发现一张牌。", powerCost: 1), out unlock),
                "unlock facts evaluate");
            AssertEqual(7, unlock.UnlockTurn, "unlock turn");
            AssertEqual(4, unlock.UnlockTier, "unlock tier");

            HeroStrategy afk;
            AssertTrue(evaluator.TryEvaluate(Fact(
                text: "跳过你的前两个回合。之后获得效果。"), out afk),
                "AFK rule evaluates");
            AssertEqual(3, afk.UnlockTurn, "AFK local unlock");
            AssertEqual("AFK", afk.SpecialRule, "AFK special rule");
            AssertEqual(0.70f, afk.LevelAggression, "AFK aggression");

            HeroStrategy economy;
            AssertTrue(evaluator.TryEvaluate(Fact(
                text: "每当你出售一个随从，下一回合获得一枚铸币。"), out economy),
                "economy rule evaluates");
            AssertEqual(HeroArchetype.Econ, economy.Archetype, "economy archetype");
            AssertEqual("GALLYWIX", economy.SpecialRule, "economy special rule");
            AssertEqual(1.15f, economy.LevelAggression, "economy aggression");
            AssertEqual(0.04f, economy.BuyValueBias, "economy buy bias");

            HeroStrategy tribe;
            AssertTrue(evaluator.TryEvaluate(Fact(
                text: "使一个亡灵获得+1/+1和亡语。"), out tribe),
                "tribe rule evaluates");
            AssertEqual(0.20f, tribe.TribeAffinity["UNDEAD"], "undead affinity");
            AssertTrue(tribe.SynergyTags.Contains("UNDEAD"), "undead tag");
            AssertTrue(tribe.SynergyTags.Contains("DEATH"), "death tag");

            var mutableScripts = new[] { 2, 3, 0, 0, 0, 0 };
            var mutable = Fact(text: "使一个随从获得+{0}/+{1}。", scriptData: mutableScripts);
            HeroStrategy first;
            AssertTrue(evaluator.TryEvaluate(mutable, out first), "mutable input evaluates");
            mutable.TextZhCn = "";
            mutableScripts[0] = 99;
            AssertEqual(HeroArchetype.Tempo, first.Archetype, "returned strategy detached from fact");
            AssertEqual("", first.PowerHint, "returned strategy stores no fact text");

            Console.WriteLine("PASS hero strategy pure rules and fail-closed matrix assertions="
                + _assertions);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL " + ex.Message);
            return 1;
        }
    }

    private static HeroPowerFact Fact(
        string requestedCardId = "REQUESTED",
        string heroCardId = "HERO",
        string powerCardId = "POWER",
        int heroArmor = 10,
        int powerCost = 0,
        bool hideCost = false,
        bool activated = false,
        string text = "有效技能文本。",
        int[] scriptData = null)
    {
        return new HeroPowerFact
        {
            RequestedCardId = requestedCardId,
            HeroCardId = heroCardId,
            PowerCardId = powerCardId,
            HeroArmor = heroArmor,
            PowerCost = powerCost,
            HideCost = hideCost,
            BaconHeroPowerActivated = activated,
            TextZhCn = text,
            ScriptData = scriptData ?? new int[6],
        };
    }

    private static void AssertType(
        IHeroStrategyRuleEvaluator evaluator,
        HeroPowerFact fact,
        HeroPowerType expectedType,
        int expectedCost,
        string label)
    {
        HeroStrategy strategy;
        AssertTrue(evaluator.TryEvaluate(fact, out strategy), label + " evaluates");
        AssertEqual(expectedType, strategy.PowerType, label + " type");
        AssertEqual(expectedCost, strategy.PowerCost, label + " cost");
    }

    private static void AssertFails(
        IHeroStrategyRuleEvaluator evaluator,
        HeroPowerFact fact,
        string label)
    {
        HeroStrategy strategy;
        AssertTrue(!evaluator.TryEvaluate(fact, out strategy), label + " fails closed");
        AssertTrue(strategy == null, label + " returns null");
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
