using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace BobCoach.Engine
{
    internal interface ICardEffectRuleEvaluator
    {
        bool TryEvaluate(
            CardEffectFact fact,
            out IReadOnlyList<CardEffectDefinition> effects);
    }

    internal sealed class CardEffectRuleEvaluator : ICardEffectRuleEvaluator
    {
        private static readonly IReadOnlyList<CardEffectDefinition> Empty
            = Array.AsReadOnly(new CardEffectDefinition[0]);

        private static readonly string[] Tribes =
        {
            "鱼人", "野兽", "机械", "恶魔", "龙", "元素", "纳迦", "亡灵", "海盗", "鸡", "微技",
        };

        public bool TryEvaluate(
            CardEffectFact fact,
            out IReadOnlyList<CardEffectDefinition> effects)
        {
            effects = Empty;
            if (fact == null
                || string.IsNullOrEmpty(fact.CardId)
                || (fact.CardType != CardEffectCardType.Minion
                    && fact.CardType != CardEffectCardType.TavernSpell)
                || fact.ScriptData == null
                || fact.ScriptData.Count != 6)
                return false;

            string text;
            if (!TryExpandText(fact, out text)) return false;
            effects = Array.AsReadOnly(DetectEffects(text).ToArray());
            return true;
        }

        private static bool TryExpandText(CardEffectFact fact, out string text)
        {
            text = Regex.Replace(fact.TextZhCn ?? "", "<[^>]+>", "");
            for (int i = 0; i < 6; i++)
            {
                string token = "{" + i + "}";
                if (!text.Contains(token)) continue;
                text = text.Replace(
                    token,
                    fact.ScriptData[i].ToString(CultureInfo.InvariantCulture));
            }
            return !Regex.IsMatch(text, @"\{\d+\}");
        }

        private static List<CardEffectDefinition> DetectEffects(string text)
        {
            string per = IsMatch(text, "回合结束|你的回合开始|每个回合|每回合|回合开始时")
                ? "turn" : "once";
            double harsh = IsMatch(text, "仅限本场|奇数|偶数") ? 0.5 : 1.0;
            string tribe = "";
            Match tribeMatch = Regex.Match(text,
                "(发现|获取|获得|抽取|召唤).{0,8}(" + string.Join("|", Tribes) + ")");
            if (tribeMatch.Success) tribe = tribeMatch.Groups[2].Value;
            bool sellTrigger = IsMatch(text, "出售本随从");
            var effects = new List<CardEffectDefinition>();

            if (IsMatch(text, "铸币.{0,4}上限|上限.{0,4}铸币"))
                effects.Add(new CardEffectDefinition("gold_cap", 1.0, "permanent"));
            else if (IsMatch(text, "铸币")
                && IsMatch(text, "获得|增加|额外|多.{0,2}枚")
                && !IsMatch(text, "花掉|花费|支付"))
                effects.Add(new CardEffectDefinition("generate_gold", 1.0, per));

            if (IsMatch(text, "免费.{0,6}刷新|额外.{0,4}免费.{0,4}刷新"))
            {
                Match refresh = Regex.Match(text, "(\\d+)\\s*次");
                int count = refresh.Success
                    ? int.Parse(refresh.Groups[1].Value, CultureInfo.InvariantCulture)
                    : 1;
                effects.Add(new CardEffectDefinition(
                    "free_refresh", Math.Min(Math.Max(count, 1), 2), "once"));
            }

            bool spellToken = IsMatch(text, "法术牌|弹幕|黏黏盾|宝石|酒馆法术");
            CardEffectDefinition generated = null;
            if (sellTrigger && IsMatch(text, "获取|发现|获得"))
                generated = new CardEffectDefinition("sell_generate", 1.5 * harsh, per, tribe);
            else if (IsMatch(text, "发现"))
                generated = new CardEffectDefinition("discover", 2.5 * harsh, per, tribe);
            else if (IsMatch(text, "获取|抽取") && spellToken)
                generated = new CardEffectDefinition("generate_spell", 1.2 * harsh, per, tribe);
            else if (IsMatch(text, "(获取|抽取).{0,10}随从")
                && !IsMatch(text, "使.{0,10}随从|随从.{0,4}获得\\s*\\+|召唤"))
                generated = new CardEffectDefinition("generate_card", 2.0 * harsh, per, tribe);
            if (generated != null) effects.Add(generated);

            if (IsMatch(text, "抉择")
                && IsMatch(text, "获得\\s*\\+|复生|风怒")
                && Tribes.Any(value => IsMatch(text, Regex.Escape(value)))
                && !IsMatch(text, "酒馆法术"))
            {
                string buffTribe = Tribes.First(value => IsMatch(text, Regex.Escape(value)));
                effects.Add(new CardEffectDefinition(
                    "tribe_buff", 1.0 * harsh, "once", buffTribe));
            }

            if (IsMatch(text, "召唤.{0,24}(仅限本场|本场战斗)|仅限本场战斗"))
                effects.Add(new CardEffectDefinition("combat_summon", 1.0 * harsh, "once"));
            if (IsMatch(text, "亡语.{0,10}消灭|消灭击杀"))
                effects.Add(new CardEffectDefinition("combat_removal", 1.0, "once"));
            if (IsMatch(text, "触发两次|施放两次|额外.{0,2}触发"))
                effects.Add(new CardEffectDefinition("amplifier", 0.0, ""));
            return effects;
        }

        private static bool IsMatch(string text, string pattern)
        {
            return Regex.IsMatch(text ?? "", pattern);
        }
    }
}
