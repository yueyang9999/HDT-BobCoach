using System;
using System.Collections.Generic;
using System.Text;

namespace BobCoach.Engine
{
    internal sealed class CardSemanticRuleEvaluator
    {
        public CardSemanticsData Evaluate(CardSemanticFact fact)
        {
            var mechanics = new HashSet<string>(StringComparer.Ordinal);
            if (fact != null && fact.Mechanics != null)
            {
                foreach (string raw in fact.Mechanics)
                {
                    string normalized = NormalizeMechanic(raw);
                    if (!string.IsNullOrEmpty(normalized)) mechanics.Add(normalized);
                }
            }

            string textZhCn = StripMarkup(fact == null ? "" : fact.TextZhCn);
            string textEnUs = StripMarkup(fact == null ? "" : fact.TextEnUs).ToLowerInvariant();
            bool isSummonAmplifier = (textZhCn.Contains("你的召唤随从的卡牌")
                    && (textZhCn.Contains("数量翻倍") || textZhCn.Contains("两倍")))
                || (textEnUs.Contains("your cards that summon minions")
                    && (textEnUs.Contains("twice as many") || textEnUs.Contains("double")));
            if (textZhCn.Contains("战吼：") || textZhCn.Contains("战吼:")
                || textEnUs.Contains("battlecry:"))
                mechanics.Add("BATTLECRY");
            if (textZhCn.Contains("亡语：") || textZhCn.Contains("亡语:")
                || textEnUs.Contains("deathrattle:"))
                mechanics.Add("DEATHRATTLE");
            if (textZhCn.Contains("在你的回合结束时")
                || textZhCn.Contains("在回合结束时")
                || textEnUs.Contains("at the end of your turn")
                || textEnUs.Contains("at the end of every"))
                mechanics.Add("END_OF_TURN");
            if (textZhCn.Contains("失去圣盾")
                || textEnUs.Contains("loses divine shield")
                || textEnUs.Contains("lost divine shield"))
                mechanics.Add("DIVINE_SHIELD_LOST");
            if (!isSummonAmplifier && HasActiveSummonEvidence(textZhCn, textEnUs))
                mechanics.Add("SUMMON");

            var provides = new HashSet<string>(StringComparer.Ordinal);
            bool hasTriggerAmplification = HasTriggerAmplificationEvidence(textZhCn, textEnUs);
            if (((textZhCn.Contains("你的亡语")
                    || (textZhCn.Contains("你的战吼") && textZhCn.Contains("亡语")))
                    && textZhCn.Contains("触发") && hasTriggerAmplification)
                || ((textEnUs.Contains("your deathrattles")
                    || (textEnUs.Contains("your battlecries") && textEnUs.Contains("deathrattles")))
                    && textEnUs.Contains("trigger") && hasTriggerAmplification))
                provides.Add("TRIGGER_DEATHRATTLE_EXTRA");
            if ((textZhCn.Contains("你的战吼") && textZhCn.Contains("触发") && hasTriggerAmplification)
                || (textEnUs.Contains("your battlecries") && textEnUs.Contains("trigger")
                    && hasTriggerAmplification))
                provides.Add("TRIGGER_BATTLECRY_EXTRA");
            if ((textZhCn.Contains("你的回合结束效果") && textZhCn.Contains("触发")
                    && hasTriggerAmplification)
                || (textEnUs.Contains("your end of turn effects") && textEnUs.Contains("trigger")
                    && hasTriggerAmplification))
                provides.Add("TRIGGER_END_OF_TURN_EXTRA");
            if (isSummonAmplifier)
                provides.Add("SUMMON_EXTRA");
            if ((textZhCn.Contains("触发你") && textZhCn.Contains("亡语"))
                || (textEnUs.Contains("trigger your") && textEnUs.Contains("deathrattle")))
                provides.Add("COPY_DEATHRATTLE");
            if (textZhCn.Contains("重新获得圣盾")
                || textZhCn.Contains("再次获得圣盾")
                || textZhCn.Contains("恢复圣盾")
                || textEnUs.Contains("regain divine shield")
                || textEnUs.Contains("regains divine shield"))
                provides.Add("DIVINE_SHIELD_REFRESH");

            var combos = new List<CardSemanticCombo>();
            if (mechanics.Contains("DEATHRATTLE"))
            {
                combos.Add(new CardSemanticCombo("TRIGGER_DEATHRATTLE_EXTRA", 3.0));
                combos.Add(new CardSemanticCombo("COPY_DEATHRATTLE", 2.5));
            }
            if (mechanics.Contains("BATTLECRY"))
                combos.Add(new CardSemanticCombo("TRIGGER_BATTLECRY_EXTRA", 3.0));
            if (mechanics.Contains("END_OF_TURN"))
                combos.Add(new CardSemanticCombo("TRIGGER_END_OF_TURN_EXTRA", 3.0));
            if (mechanics.Contains("DIVINE_SHIELD_LOST"))
                combos.Add(new CardSemanticCombo("DIVINE_SHIELD_REFRESH", 2.5));
            if (mechanics.Contains("SUMMON"))
                combos.Add(new CardSemanticCombo("SUMMON_EXTRA", 2.0));

            return new CardSemanticsData(mechanics, combos, provides);
        }

        private static string NormalizeMechanic(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            var result = new StringBuilder(value.Length);
            bool pendingSeparator = false;
            foreach (char c in value.Trim())
            {
                if (char.IsLetterOrDigit(c))
                {
                    if (pendingSeparator && result.Length > 0) result.Append('_');
                    result.Append(char.ToUpperInvariant(c));
                    pendingSeparator = false;
                }
                else
                {
                    pendingSeparator = true;
                }
            }
            return result.ToString();
        }

        private static string StripMarkup(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            var result = new StringBuilder(value.Length);
            bool insideTag = false;
            foreach (char c in value)
            {
                if (c == '<') { insideTag = true; continue; }
                if (c == '>') { insideTag = false; continue; }
                if (!insideTag) result.Append(c);
            }
            return result.ToString();
        }

        private static bool HasTriggerAmplificationEvidence(string textZhCn, string textEnUs)
        {
            return textZhCn.Contains("额外触发")
                || textZhCn.Contains("触发两次")
                || textZhCn.Contains("触发三次")
                || textZhCn.Contains("触发次数翻倍")
                || textEnUs.Contains("trigger an extra time")
                || textEnUs.Contains("trigger an additional time")
                || textEnUs.Contains("trigger twice")
                || textEnUs.Contains("trigger three times");
        }

        private static bool HasActiveSummonEvidence(string textZhCn, string textEnUs)
        {
            int start = 0;
            while (start < textZhCn.Length)
            {
                int index = textZhCn.IndexOf("召唤", start, StringComparison.Ordinal);
                if (index < 0) break;
                char previous = index > 0 ? textZhCn[index - 1] : '\0';
                if (previous != '你' && previous != '每' && previous != '被'
                    && previous != '当' && previous != '在')
                    return true;
                start = index + 2;
            }

            start = 0;
            while (start < textEnUs.Length)
            {
                int index = textEnUs.IndexOf("summon", start, StringComparison.Ordinal);
                if (index < 0) break;
                int after = index + 6;
                bool wordStart = index == 0 || !char.IsLetter(textEnUs[index - 1]);
                bool wordEnd = after >= textEnUs.Length || !char.IsLetter(textEnUs[after]);
                if (wordStart && wordEnd)
                {
                    string previousWord = ReadPreviousWord(textEnUs, index);
                    if (previousWord != "you" && previousWord != "that" && previousWord != "which")
                        return true;
                }
                start = after;
            }
            return false;
        }

        private static string ReadPreviousWord(string text, int index)
        {
            int end = index - 1;
            while (end >= 0 && !char.IsLetter(text[end])) end--;
            if (end < 0) return "";
            int start = end;
            while (start > 0 && char.IsLetter(text[start - 1])) start--;
            return text.Substring(start, end - start + 1);
        }
    }
}
