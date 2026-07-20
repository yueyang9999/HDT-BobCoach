using System;
using System.Collections.Generic;
using System.Linq;
using BobCoach.Engine;

internal static class CardSemanticRuleEvaluatorBehavior
{
    private static int Main()
    {
        var evaluator = new CardSemanticRuleEvaluator();
        var semantics = evaluator.Evaluate(new CardSemanticFact
        {
            CardId = "TEST_BATTLECRY",
            Mechanics = new List<string> { "Battlecry" },
            TextZhCn = "",
            TextEnUs = "",
        });

        if (!semantics.HasMechanic("BATTLECRY"))
            return Fail("Battlecry mechanic was not normalized");
        if (semantics.Combos.Count != 1
            || semantics.Combos[0].Mechanic != "TRIGGER_BATTLECRY_EXTRA"
            || Math.Abs(semantics.Combos[0].Weight - 3.0) > 0.000001)
            return Fail("Battlecry did not produce the approved 3.0 relationship");

        var deathrattle = evaluator.Evaluate(new CardSemanticFact
        {
            CardId = "TEST_DEATHRATTLE",
            Mechanics = new List<string> { "Deathrattle" },
        });
        if (!deathrattle.HasMechanic("DEATHRATTLE")
            || deathrattle.Combos.Count != 2
            || deathrattle.Combos[0].Mechanic != "TRIGGER_DEATHRATTLE_EXTRA"
            || Math.Abs(deathrattle.Combos[0].Weight - 3.0) > 0.000001
            || deathrattle.Combos[1].Mechanic != "COPY_DEATHRATTLE"
            || Math.Abs(deathrattle.Combos[1].Weight - 2.5) > 0.000001)
            return Fail("Deathrattle did not produce the approved 3.0 and 2.5 relationships");

        var endOfTurn = evaluator.Evaluate(new CardSemanticFact
        {
            CardId = "TEST_END_OF_TURN",
            TextZhCn = "<b>在你的回合结束时</b>，使一个友方随从获得+1/+1。",
        });
        if (!endOfTurn.HasMechanic("END_OF_TURN")
            || endOfTurn.Combos.Count != 1
            || endOfTurn.Combos[0].Mechanic != "TRIGGER_END_OF_TURN_EXTRA"
            || Math.Abs(endOfTurn.Combos[0].Weight - 3.0) > 0.000001)
            return Fail("End-of-turn text did not produce the approved 3.0 relationship");

        var periodicEndOfTurn = evaluator.Evaluate(new CardSemanticFact
        {
            CardId = "TEST_PERIODIC_END_OF_TURN",
            TextZhCn = "每2个回合，在回合结束时，获取一张牌。",
            TextEnUs = "At the end of every 2 turns, get a card.",
        });
        if (!periodicEndOfTurn.HasMechanic("END_OF_TURN")
            || periodicEndOfTurn.Combos.Count != 1)
            return Fail("Periodic end-of-turn text did not produce the approved relationship");

        var shieldLoss = evaluator.Evaluate(new CardSemanticFact
        {
            CardId = "TEST_SHIELD_LOSS",
            TextZhCn = "在一个友方随从失去<b>圣盾</b>后，使其获得+2/+2。",
        });
        if (!shieldLoss.HasMechanic("DIVINE_SHIELD_LOST")
            || shieldLoss.Combos.Count != 1
            || shieldLoss.Combos[0].Mechanic != "DIVINE_SHIELD_REFRESH"
            || Math.Abs(shieldLoss.Combos[0].Weight - 2.5) > 0.000001)
            return Fail("Divine-shield-loss text did not produce the approved 2.5 relationship");

        var englishShieldLoss = evaluator.Evaluate(new CardSemanticFact
        {
            CardId = "TEST_ENGLISH_SHIELD_LOSS",
            TextEnUs = "After a friendly minion loses Divine Shield, give it +2/+2.",
        });
        if (!englishShieldLoss.HasMechanic("DIVINE_SHIELD_LOST")
            || englishShieldLoss.Combos.Count != 1)
            return Fail("English-only shield-loss fact did not produce the approved relationship");

        var summon = evaluator.Evaluate(new CardSemanticFact
        {
            CardId = "TEST_SUMMON",
            Mechanics = new List<string> { "Deathrattle" },
            TextEnUs = "<b>Deathrattle:</b> Summon a 1/1 Beast.",
        });
        if (!summon.HasMechanic("SUMMON")
            || summon.Combos.Count != 3
            || summon.Combos[2].Mechanic != "SUMMON_EXTRA"
            || Math.Abs(summon.Combos[2].Weight - 2.0) > 0.000001)
            return Fail("Summon text did not produce the approved 2.0 relationship");

        var summonObserver = evaluator.Evaluate(new CardSemanticFact
        {
            CardId = "TEST_SUMMON_OBSERVER",
            TextZhCn = "每当你召唤一个随从时，使其获得+1/+1。",
            TextEnUs = "Whenever you summon a minion, give it +1/+1.",
        });
        if (summonObserver.HasMechanic("SUMMON") || summonObserver.Combos.Count != 0)
            return Fail("A summon observer was mistaken for a card that actually summons");

        foreach (string observerText in new[]
        {
            "每当召唤一个随从时，使其获得+1/+1。",
            "在召唤一个随从后，使其获得+1/+1。",
        })
        {
            var implicitObserver = evaluator.Evaluate(new CardSemanticFact
            {
                CardId = "TEST_IMPLICIT_SUMMON_OBSERVER",
                TextZhCn = observerText,
            });
            if (implicitObserver.HasMechanic("SUMMON") || implicitObserver.Combos.Count != 0)
                return Fail("An implicit-subject summon observer was mistaken for an action");
        }

        var observerWithAction = evaluator.Evaluate(new CardSemanticFact
        {
            CardId = "TEST_SUMMON_OBSERVER_WITH_ACTION",
            TextZhCn = "每当你召唤一个随从时，召唤一个1/1的复制。",
            TextEnUs = "Whenever you summon a minion, summon a 1/1 copy.",
        });
        if (!observerWithAction.HasMechanic("SUMMON")
            || observerWithAction.Combos.Count != 1)
            return Fail("An explicit summon action after an observer trigger was lost");

        var titus = evaluator.Evaluate(new CardSemanticFact
        {
            CardId = "TEST_TITUS",
            TextZhCn = "你的<b>亡语</b>额外触发一次。",
            TextEnUs = "Your <b>Deathrattles</b> trigger an extra time.",
        });
        if (!titus.Provides("TRIGGER_DEATHRATTLE_EXTRA")
            || titus.ProvidesMechanics.Count != 1)
            return Fail("Titus text did not provide only deathrattle amplification");

        var moira = evaluator.Evaluate(new CardSemanticFact
        {
            CardId = "TEST_MOIRA",
            TextZhCn = "你的<b>战吼</b>和<b>亡语</b>会触发两次。",
            TextEnUs = "Your <b>Battlecries</b> and <b>Deathrattles</b> trigger twice.",
        });
        if (!moira.Provides("TRIGGER_BATTLECRY_EXTRA")
            || !moira.Provides("TRIGGER_DEATHRATTLE_EXTRA")
            || moira.ProvidesMechanics.Count != 2)
            return Fail("Moira text did not expose both approved providers");

        var drakkari = evaluator.Evaluate(new CardSemanticFact
        {
            CardId = "TEST_DRAKKARI",
            TextZhCn = "你的回合结束效果会触发两次。",
            TextEnUs = "Your end of turn effects trigger twice.",
        });
        if (!drakkari.Provides("TRIGGER_END_OF_TURN_EXTRA")
            || drakkari.ProvidesMechanics.Count != 1
            || drakkari.HasMechanic("END_OF_TURN")
            || drakkari.Combos.Count != 0)
            return Fail("Drakkari text did not remain a provider-only end-of-turn amplifier");

        var khadgar = evaluator.Evaluate(new CardSemanticFact
        {
            CardId = "TEST_KHADGAR",
            TextZhCn = "你的召唤随从的卡牌召唤数量翻倍。",
            TextEnUs = "Your cards that summon minions summon twice as many.",
        });
        if (!khadgar.Provides("SUMMON_EXTRA")
            || khadgar.ProvidesMechanics.Count != 1
            || khadgar.HasMechanic("SUMMON")
            || khadgar.Combos.Count != 0)
            return Fail("Khadgar text was not separated into a provider-only summon amplifier");

        var macaw = evaluator.Evaluate(new CardSemanticFact
        {
            CardId = "TEST_MACAW",
            Mechanics = new List<string> { "Rally" },
            TextZhCn = "进击：触发你最左边的亡语，触发两次。",
            TextEnUs = "Rally: Trigger your left-most Deathrattle twice.",
        });
        if (!macaw.HasMechanic("RALLY")
            || macaw.HasMechanic("SUMMON")
            || !macaw.Provides("COPY_DEATHRATTLE")
            || macaw.Combos.Count != 0)
            return Fail("Rally deathrattle trigger was not separated from summon semantics");

        var shieldRefresh = evaluator.Evaluate(new CardSemanticFact
        {
            CardId = "TEST_SHIELD_REFRESH",
            TextZhCn = "每当一个友方随从失去<b>圣盾</b>，使其重新获得<b>圣盾</b>。",
            TextEnUs = "After a friendly minion loses Divine Shield, it regains Divine Shield.",
        });
        if (!shieldRefresh.HasMechanic("DIVINE_SHIELD_LOST")
            || !shieldRefresh.Provides("DIVINE_SHIELD_REFRESH"))
            return Fail("Actual shield refresh text did not provide the approved capability");
        if (shieldLoss.Provides("DIVINE_SHIELD_REFRESH"))
            return Fail("Grease Bot-style stat payoff was still treated as shield refresh");

        var ordinaryDeathrattleWatcher = evaluator.Evaluate(new CardSemanticFact
        {
            CardId = "TEST_DEATHRATTLE_WATCHER",
            TextEnUs = "After your Deathrattles trigger, give your minions +1/+1.",
        });
        if (ordinaryDeathrattleWatcher.Provides("TRIGGER_DEATHRATTLE_EXTRA"))
            return Fail("Ordinary trigger observation was mistaken for extra triggering");

        var textBattlecry = evaluator.Evaluate(new CardSemanticFact
        {
            CardId = "TEST_TEXT_BATTLECRY",
            TextZhCn = "<b>战吼：</b>使一个友方随从获得+1/+1。",
            TextEnUs = "<b>Battlecry:</b> Give a friendly minion +1/+1.",
        });
        if (!textBattlecry.HasMechanic("BATTLECRY")
            || textBattlecry.Combos.Count != 1)
            return Fail("Bilingual keyword text did not recover a missing base Battlecry mechanic");

        var textDeathrattle = evaluator.Evaluate(new CardSemanticFact
        {
            CardId = "TEST_TEXT_DEATHRATTLE",
            TextZhCn = "<b>亡语：</b>召唤一个1/1的野兽。",
            TextEnUs = "<b>Deathrattle:</b> Summon a 1/1 Beast.",
        });
        if (!textDeathrattle.HasMechanic("DEATHRATTLE")
            || !textDeathrattle.HasMechanic("SUMMON")
            || textDeathrattle.Combos.Count != 3)
            return Fail("Bilingual keyword text did not recover missing Deathrattle and Summon mechanics");

        foreach (string retiredMechanic in new[] { "Reborn", "Avenge", "Casts When Bought", "Rally" })
        {
            var retired = evaluator.Evaluate(new CardSemanticFact
            {
                CardId = "TEST_RETIRED_" + retiredMechanic,
                Mechanics = new List<string> { retiredMechanic },
            });
            if (retired.Combos.Count != 0)
                return Fail(retiredMechanic + " still produced a retired semantic relationship");
        }

        Console.WriteLine("PASS approved semantics retain six relationships and retire Reborn/Avenge/OnBuy/Rally mappings");
        return 0;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine("FAIL " + message);
        return 1;
    }
}
