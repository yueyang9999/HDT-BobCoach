using System;
using System.Collections.Generic;
using System.Linq;
using BobCoach.Engine;

internal static class AnomalyRuleEvaluatorScheduledBehavior
{
    private static int _assertions;

    private static int Main()
    {
        try
        {
            var evaluator = new AnomalyRuleEvaluator();

            AssertRule(evaluator, Fact("在第7回合，发现一个等级5的金色随从。"),
                Expected("scheduled_golden_minion_discover", turn: 7, tier: 5, count: 1));

            var grant = Fact("每3个回合，获取一张法术牌。");
            grant.EvolutionCardId = "TEST_GRANT";
            grant.EvolutionCardType = "battleground_spell";
            AssertRule(evaluator, grant,
                Expected("scheduled_card_grant", everyTurns: 3, cardId: "TEST_GRANT",
                    cardType: "battleground_spell", count: 1));

            AssertRule(evaluator,
                Fact("对战开始时，发现等级6、4和2的随从各一个，当你达到对应等级时才可使用。"),
                Expected("tier_locked_minion_discovers", countEach: 1, tiers: new[] { 6, 4, 2 }));

            var secondPower = Fact("对战开始时，将一项技能变为你的第二英雄技能。");
            secondPower.EvolutionCardId = "TEST_HERO_POWER";
            secondPower.EvolutionCardType = "hero_power";
            AssertRule(evaluator, secondPower,
                Expected("second_hero_power", cardId: "TEST_HERO_POWER", cardType: "hero_power"));

            var lockedPower = Fact("对战开始时，将一项技能变为你的第二英雄技能，该技能在第5回合解锁。");
            lockedPower.EvolutionCardId = "TEST_LOCKED_POWER";
            lockedPower.EvolutionCardType = "hero_power";
            AssertRule(evaluator, lockedPower,
                Expected("second_hero_power", cardId: "TEST_LOCKED_POWER", cardType: "hero_power",
                    unlockTurn: 5));

            var lesserCopyPower = Fact("对战开始时，将复制你购买的小型饰品的技能变为你的第二英雄技能。");
            lesserCopyPower.EvolutionCardId = "TEST_LESSER_COPY_POWER";
            lesserCopyPower.EvolutionCardType = "hero_power";
            AssertRule(evaluator, lesserCopyPower,
                Expected("second_hero_power", cardId: "TEST_LESSER_COPY_POWER", cardType: "hero_power",
                    copiesPurchasedTrinket: "lesser"));

            AssertRule(evaluator, Fact("对战开始时，发现一项第二英雄技能。"),
                Expected("discover_second_hero_power_at_game_start", count: 1));

            var allHeroes = Fact("所有英雄均为同一个英雄。");
            allHeroes.OverrideHeroCardId = "TEST_OVERRIDE_HERO";
            AssertRule(evaluator, allHeroes,
                Expected("all_heroes_override", stringValue: "manager_marin",
                    heroCardId: "TEST_OVERRIDE_HERO"));

            var portal = Fact("在每个回合开始时，获取一张瓶中传送门。", duos: true);
            portal.EvolutionCardId = "TEST_PORTAL";
            portal.EvolutionCardType = "battleground_spell";
            AssertRule(evaluator, portal,
                Expected("portal_in_bottle_at_turn_start", cardId: "TEST_PORTAL",
                    cardType: "battleground_spell", count: 1), "duo");

            var transfer = Fact("每回合中，你可以向你的队友发送最多2枚铸币。", duos: true);
            transfer.EvolutionCardId = "TEST_TRANSFER";
            transfer.EvolutionCardType = "battleground_spell";
            AssertRule(evaluator, transfer,
                Expected("send_gold_to_teammate", cardId: "TEST_TRANSFER",
                    cardType: "battleground_spell", goldPerUse: 1, maxPerTurn: 2), "duo");

            var englishGrant = Fact("", "Every 3 turns, get a spell card.");
            englishGrant.EvolutionCardId = "TEST_EN_GRANT";
            englishGrant.EvolutionCardType = "spell";
            AssertRule(evaluator, englishGrant,
                Expected("scheduled_card_grant", everyTurns: 3, cardId: "TEST_EN_GRANT",
                    cardType: "spell", count: 1));

            var missingPower = Fact("对战开始时，将一项技能变为你的第二英雄技能。");
            AssertFails(evaluator, missingPower);

            var wrongPower = Fact("对战开始时，将一项技能变为你的第二英雄技能。");
            wrongPower.EvolutionCardId = "TEST_WRONG_POWER";
            wrongPower.EvolutionCardType = "spell";
            AssertFails(evaluator, wrongPower);

            var wrongGrant = Fact("每3个回合，获取一张法术牌。");
            wrongGrant.EvolutionCardId = "TEST_WRONG_GRANT";
            wrongGrant.EvolutionCardType = "minion";
            AssertFails(evaluator, wrongGrant);

            var soloPortal = Fact("在每个回合开始时，获取一张瓶中传送门。");
            soloPortal.EvolutionCardId = "TEST_PORTAL";
            soloPortal.EvolutionCardType = "battleground_spell";
            AssertFails(evaluator, soloPortal);

            var soloTransfer = Fact("每回合中，你可以向你的队友发送最多2枚铸币。");
            soloTransfer.EvolutionCardId = "TEST_TRANSFER";
            soloTransfer.EvolutionCardType = "battleground_spell";
            AssertFails(evaluator, soloTransfer);

            AssertFails(evaluator,
                Fact("对战开始时，发现等级6、6和2的随从各一个，当你达到对应等级时才可使用。"));
            AssertFails(evaluator, Fact("在第0回合，发现一个等级5的金色随从。"));

            var conflictingGrant = Fact("每3个回合，获取一张法术牌。", "Every 4 turns, get a spell card.");
            conflictingGrant.EvolutionCardId = "TEST_CONFLICT_GRANT";
            conflictingGrant.EvolutionCardType = "spell";
            AssertFails(evaluator, conflictingGrant);

            Console.WriteLine("PASS anomaly scheduled/hero/duo evaluator assertions=" + _assertions);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL " + ex.Message);
            return 1;
        }
    }

    private static AnomalyFact Fact(string zhCn, string enUs = null, bool duos = false)
    {
        return new AnomalyFact
        {
            RequestedCardId = "TEST_ANOMALY",
            AnomalyCardId = "TEST_ANOMALY",
            IsDuosExclusive = duos,
            ScriptData = new[] { 0, 0, 0, 0, 0, 0 },
            TextZhCn = zhCn,
            TextEnUs = enUs ?? zhCn,
        };
    }

    private static ExpectedRule Expected(
        string type,
        string stringValue = null,
        int turn = 0,
        int everyTurns = 0,
        int tier = 0,
        string cardId = null,
        string heroCardId = null,
        string cardType = null,
        int count = 0,
        int countEach = 0,
        int goldPerUse = 0,
        int maxPerTurn = 0,
        int? unlockTurn = null,
        string copiesPurchasedTrinket = null,
        IReadOnlyList<int> tiers = null)
    {
        return new ExpectedRule
        {
            Type = type,
            StringValue = stringValue,
            Turn = turn,
            EveryTurns = everyTurns,
            Tier = tier,
            CardId = cardId,
            HeroCardId = heroCardId,
            CardType = cardType,
            Count = count,
            CountEach = countEach,
            GoldPerUse = goldPerUse,
            MaxPerTurn = maxPerTurn,
            UnlockTurn = unlockTurn,
            CopiesPurchasedTrinket = copiesPurchasedTrinket,
            Tiers = tiers ?? new int[0],
        };
    }

    private static void AssertRule(
        AnomalyRuleEvaluator evaluator,
        AnomalyFact fact,
        ExpectedRule expected,
        string expectedScope = "solo")
    {
        AnomalyDefinition definition;
        if (!evaluator.TryEvaluate(fact, out definition))
            throw new Exception("expected evaluation success for " + fact.TextZhCn + fact.TextEnUs);
        Equal("primary", definition.Lifecycle, "lifecycle");
        Equal(expectedScope, definition.Scope, "scope");
        Equal(1, definition.Rules.Count, "rule count");
        var actual = definition.Rules[0];
        Equal(expected.Type, actual.Type, "type");
        Equal(expected.StringValue, actual.StringValue, "string value");
        Equal(expected.Turn, actual.Turn, "turn");
        Equal(expected.EveryTurns, actual.EveryTurns, "everyTurns");
        Equal(expected.Tier, actual.Tier, "tier");
        Equal(expected.CardId, actual.CardId, "cardId");
        Equal(expected.HeroCardId, actual.HeroCardId, "heroCardId");
        Equal(expected.CardType, actual.CardType, "cardType");
        Equal(expected.Count, actual.Count, "count");
        Equal(expected.CountEach, actual.CountEach, "countEach");
        Equal(expected.GoldPerUse, actual.GoldPerUse, "goldPerUse");
        Equal(expected.MaxPerTurn, actual.MaxPerTurn, "maxPerTurn");
        Equal(expected.UnlockTurn, actual.UnlockTurn, "unlockTurn");
        Equal(expected.CopiesPurchasedTrinket, actual.CopiesPurchasedTrinket, "copiesPurchasedTrinket");
        Equal(string.Join(",", expected.Tiers), string.Join(",", actual.Tiers), "tiers");
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
        public string StringValue;
        public int Turn;
        public int EveryTurns;
        public int Tier;
        public string CardId;
        public string HeroCardId;
        public string CardType;
        public int Count;
        public int CountEach;
        public int GoldPerUse;
        public int MaxPerTurn;
        public int? UnlockTurn;
        public string CopiesPurchasedTrinket;
        public IReadOnlyList<int> Tiers;
    }
}
