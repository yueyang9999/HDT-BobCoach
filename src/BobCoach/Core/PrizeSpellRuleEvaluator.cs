using System.Globalization;
using System.Text.RegularExpressions;

namespace BobCoach.Engine
{
    internal interface IPrizeSpellRuleEvaluator
    {
        bool TryEvaluate(PrizeSpellFact fact, out PrizeSpellPolicy policy);
    }

    internal sealed class PrizeSpellRuleEvaluator : IPrizeSpellRuleEvaluator
    {
        public bool TryEvaluate(PrizeSpellFact fact, out PrizeSpellPolicy policy)
        {
            policy = null;
            if (fact == null
                || string.IsNullOrEmpty(fact.CardId)
                || (fact.CardType != PrizeSpellCardType.Spell
                    && fact.CardType != PrizeSpellCardType.Minion)
                || fact.PrizeTier < 1
                || fact.PrizeTier > 4
                || fact.TechLevel != fact.PrizeTier
                || fact.ScriptData == null
                || fact.ScriptData.Count != 6)
                return false;

            string text;
            if (!TryExpandText(fact, out text) || string.IsNullOrEmpty(text)) return false;

            PrizeSpellRole role;
            if (fact.CardType == PrizeSpellCardType.Minion)
                role = PrizeSpellRole.Minion;
            else if (IsEconomy(text))
                role = PrizeSpellRole.Economy;
            else if (IsUtility(text))
                role = PrizeSpellRole.Utility;
            else if (IsScaling(text))
                role = PrizeSpellRole.Scaling;
            else if (IsDiscover(text))
                role = PrizeSpellRole.Discover;
            else if (IsTempo(text))
                role = PrizeSpellRole.Tempo;
            else
                return false;

            policy = new PrizeSpellPolicy(fact.CardId, fact.PrizeTier, role);
            return true;
        }

        private static bool TryExpandText(PrizeSpellFact fact, out string text)
        {
            text = Regex.Replace(fact.TextZhCn ?? "", "<[^>]+>", "");
            for (int i = 0; i < 6; i++)
            {
                string token = "{" + i + "}";
                if (!text.Contains(token)) continue;
                text = text.Replace(token,
                    fact.ScriptData[i].ToString(CultureInfo.InvariantCulture));
            }
            if (Regex.IsMatch(text, @"\{\d+\}")) return false;
            text = Regex.Replace(text, @"\s+", " ").Trim();
            return true;
        }

        private static bool IsEconomy(string text)
        {
            return IsMatch(text, "铸币")
                || IsMatch(text, "免费.{0,6}刷新")
                || IsMatch(text, "酒馆法术.{0,12}消耗.{0,8}减少")
                || IsMatch(text, "获取.{0,6}香蕉|香蕉果盘.{0,8}填满.{0,4}手牌")
                || IsMatch(text, "酒馆.{0,6}额外提供.{0,12}随从")
                || IsMatch(text, "酒馆中随从.{0,8}铸币消耗.{0,8}2")
                || IsMatch(text, "随机.{0,8}酒馆法术.{0,8}填满.{0,4}手牌");
        }

        private static bool IsUtility(string text)
        {
            return IsMatch(text, "新的英雄技能")
                || IsMatch(text, "非金色.{0,12}随从.{0,12}移回.{0,8}手牌")
                || IsMatch(text, "随从.{0,8}变为金色.{0,16}移回.{0,8}手牌");
        }

        private static bool IsScaling(string text)
        {
            return IsMatch(text, "酒馆法术.{0,8}使随从.{0,8}额外获得")
                || IsMatch(text, "酒馆中.{0,4}随从.{0,12}本局对战中.{0,8}获得")
                || IsMatch(text, "回合结束时.{0,12}获得\\+");
        }

        private static bool IsDiscover(string text)
        {
            return IsMatch(text, "发现")
                || IsMatch(text, "随机获取.{0,16}酒馆法术");
        }

        private static bool IsTempo(string text)
        {
            return IsMatch(text,
                "替换为高一级|攻击力翻倍|嘲讽.{0,12}生命值翻倍|"
                + "战吼.{0,12}额外触发|圣盾|变为金色|偷取酒馆");
        }

        private static bool IsMatch(string text, string pattern)
        {
            return Regex.IsMatch(text ?? "", pattern, RegexOptions.IgnoreCase);
        }
    }
}
