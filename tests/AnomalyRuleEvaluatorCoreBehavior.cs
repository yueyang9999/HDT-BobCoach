using System;
using System.Collections.Generic;
using System.Linq;
using BobCoach.Engine;

internal static class AnomalyRuleEvaluatorCoreBehavior
{
    private static int _assertions;

    private static int Main()
    {
        try
        {
            var evaluator = new AnomalyRuleEvaluator();

            AssertRules(evaluator, Fact("对战开始时即有10枚铸币。"),
                Rule("start_gold_override", intValue: 10));
            AssertRules(evaluator, Fact("随从消耗<b>2</b>枚铸币。你不能刷新酒馆，购买后自行刷新。"),
                Rule("minion_cost_override", intValue: 2),
                Rule("manual_refresh_allowed", boolValue: false),
                Rule("refresh_after_purchase", boolValue: true));
            AssertRules(evaluator, Fact("每回合你购买的第一个随从免费。"),
                Rule("first_minion_purchase_cost", intValue: 0, period: "turn"));
            AssertRules(evaluator, Fact("每回合中，在你第一次购买卡牌时，获取一张额外复制。"),
                Rule("first_purchase_extra_copy", intValue: 1, cardType: "card"));
            AssertRules(evaluator, Fact("你只需2个复制即可将随从变为金色。使用金色随从不会获取三连奖励，改为获取酒馆币。"),
                Rule("golden_copy_requirement", intValue: 2),
                Rule("golden_reward_override", stringValue: "tavern_coin"));

            var startCard = Fact("开局时拥有一张法术。");
            startCard.EvolutionCardId = "TEST_START_CARD";
            startCard.EvolutionCardType = "spell";
            AssertRules(evaluator, startCard,
                Rule("start_with_card", cardId: "TEST_START_CARD", cardType: "spell", count: 1));

            var boardMinion = Fact("对战开始时场上有一个金色的随从。");
            boardMinion.EvolutionCardId = "TEST_BOARD_MINION";
            boardMinion.EvolutionCardType = "minion";
            boardMinion.EvolutionIsGolden = true;
            AssertRules(evaluator, boardMinion,
                Rule("start_with_board_minion", cardId: "TEST_BOARD_MINION", cardType: "minion",
                    count: 1, golden: true));

            AssertRules(evaluator, Fact("存在酒馆等级7。对战开始时额外拥有10点护甲值。"),
                Rule("max_tavern_tier", intValue: 7),
                Rule("start_armor_delta", intValue: 10));
            AssertRules(evaluator, Fact("酒馆中会出现伙伴。"),
                Rule("include_buddies_in_tavern", boolValue: true));
            AssertRules(evaluator, Fact("在你升级酒馆后，发现一个等级1的暗月奖品。（3回合后提升！）"),
                Rule("discover_prize_after_upgrade", initialTier: 1, improvesEveryTurns: 3));
            AssertRules(evaluator, Fact("[x]每4个回合，发现一个暗月奖品。（还剩@回合！）"),
                Rule("scheduled_prize_discover", everyTurns: 4, count: 1));
            AssertRules(evaluator, Fact("在每个回合开始时，所有玩家转动相同的尤格-萨隆的命运之轮。"),
                Rule("shared_yogg_wheel_at_turn_start", boolValue: true));
            AssertRules(evaluator, Fact("在每个回合开始时，由一个玩家选择一张牌，回合结束时所有玩家都会获取这张牌。"),
                Rule("shared_card_vote_each_turn", grantAt: "turn_end"));

            AssertRules(evaluator,
                Fact("随从消耗2枚铸币。", "Minions cost 2 Gold."),
                Rule("minion_cost_override", intValue: 2));
            AssertRules(evaluator,
                Fact("每回合你购买的第一个随从\u3000免费。", ""),
                Rule("first_minion_purchase_cost", intValue: 0, period: "turn"));

            AssertFails(evaluator, null);
            AssertFails(evaluator, Fact("", ""));
            AssertFails(evaluator, Fact("随从消耗2枚铸币。", "Minions cost 3 Gold."));
            AssertFails(evaluator, Fact("对战开始时即有99枚铸币。"));
            AssertFails(evaluator, Fact("尚未支持的机制。"));

            var missingRelation = Fact("开局时拥有一张法术。");
            AssertFails(evaluator, missingRelation);

            var wrongRelation = Fact("对战开始时场上有一个金色的随从。");
            wrongRelation.EvolutionCardId = "TEST_WRONG";
            wrongRelation.EvolutionCardType = "spell";
            wrongRelation.EvolutionIsGolden = true;
            AssertFails(evaluator, wrongRelation);

            var invalidScriptData = Fact("随从消耗2枚铸币。");
            invalidScriptData.ScriptData = new[] { 0, 0, 0 };
            AssertFails(evaluator, invalidScriptData);

            Console.WriteLine("PASS anomaly core evaluator assertions=" + _assertions);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL " + ex.Message);
            return 1;
        }
    }

    private static AnomalyFact Fact(string zhCn, string enUs = null)
    {
        return new AnomalyFact
        {
            RequestedCardId = "TEST_ANOMALY",
            AnomalyCardId = "TEST_ANOMALY",
            ScriptData = new[] { 0, 0, 0, 0, 0, 0 },
            TextZhCn = zhCn,
            TextEnUs = enUs ?? zhCn,
        };
    }

    private static ExpectedRule Rule(
        string type,
        int? intValue = null,
        bool? boolValue = null,
        string stringValue = null,
        int everyTurns = 0,
        int initialTier = 0,
        int improvesEveryTurns = 0,
        string cardId = null,
        string cardType = null,
        int count = 0,
        bool? golden = null,
        string period = null,
        string grantAt = null)
    {
        return new ExpectedRule
        {
            Type = type,
            IntValue = intValue,
            BoolValue = boolValue,
            StringValue = stringValue,
            EveryTurns = everyTurns,
            InitialTier = initialTier,
            ImprovesEveryTurns = improvesEveryTurns,
            CardId = cardId,
            CardType = cardType,
            Count = count,
            Golden = golden,
            Period = period,
            GrantAt = grantAt,
        };
    }

    private static void AssertRules(
        AnomalyRuleEvaluator evaluator,
        AnomalyFact fact,
        params ExpectedRule[] expected)
    {
        AnomalyDefinition definition;
        if (!evaluator.TryEvaluate(fact, out definition))
            throw new Exception("expected evaluation success for: " + (fact == null ? "<null>" : fact.TextZhCn));
        Equal("TEST_ANOMALY", definition.AnomalyCardId, "anomaly CardId");
        Equal("primary", definition.Lifecycle, "lifecycle");
        Equal("solo", definition.Scope, "scope");
        Equal(expected.Length, definition.Rules.Count, "rule count");

        var actual = definition.Rules.OrderBy(rule => rule.Type, StringComparer.Ordinal).ToArray();
        var orderedExpected = expected.OrderBy(rule => rule.Type, StringComparer.Ordinal).ToArray();
        for (int i = 0; i < actual.Length; i++)
            AssertRule(orderedExpected[i], actual[i]);
    }

    private static void AssertRule(ExpectedRule expected, AnomalyRegistry.TypedRule actual)
    {
        Equal(expected.Type, actual.Type, "rule type");
        Equal(expected.IntValue, actual.IntValue, expected.Type + " int value");
        Equal(expected.BoolValue, actual.BoolValue, expected.Type + " bool value");
        Equal(expected.StringValue, actual.StringValue, expected.Type + " string value");
        Equal(expected.EveryTurns, actual.EveryTurns, expected.Type + " everyTurns");
        Equal(expected.InitialTier, actual.InitialTier, expected.Type + " initialTier");
        Equal(expected.ImprovesEveryTurns, actual.ImprovesEveryTurns, expected.Type + " improvesEveryTurns");
        Equal(expected.CardId, actual.CardId, expected.Type + " cardId");
        Equal(expected.CardType, actual.CardType, expected.Type + " cardType");
        Equal(expected.Count, actual.Count, expected.Type + " count");
        Equal(expected.Golden, actual.Golden, expected.Type + " golden");
        Equal(expected.Period, actual.Period, expected.Type + " period");
        Equal(expected.GrantAt, actual.GrantAt, expected.Type + " grantAt");
    }

    private static void AssertFails(AnomalyRuleEvaluator evaluator, AnomalyFact fact)
    {
        AnomalyDefinition definition;
        if (evaluator.TryEvaluate(fact, out definition))
            throw new Exception("expected evaluation failure");
        if (definition != null) throw new Exception("failed evaluation returned a definition");
        _assertions++;
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception(label + " expected=" + expected + " actual=" + actual);
        _assertions++;
    }

    private sealed class ExpectedRule
    {
        public string Type;
        public int? IntValue;
        public bool? BoolValue;
        public string StringValue;
        public int EveryTurns;
        public int InitialTier;
        public int ImprovesEveryTurns;
        public string CardId;
        public string CardType;
        public int Count;
        public bool? Golden;
        public string Period;
        public string GrantAt;
    }
}
