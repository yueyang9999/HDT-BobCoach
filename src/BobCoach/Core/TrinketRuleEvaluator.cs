using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace BobCoach.Engine
{
    internal sealed class TrinketEvaluation
    {
        public bool IsValid;
        public bool IsLesser;
        public bool IsRated;
        public int RuleScore;
        public string DisplayName;
        public List<string> MatchedRuleIds = new List<string>();
        public List<string> MatchedTribes = new List<string>();
    }

    internal sealed class TrinketRuleEvaluator
    {
        private sealed class ParsedRules
        {
            public string NormalizedText;
            public bool IsRated;
            public int RuleScore;
            public readonly List<string> MatchedRuleIds = new List<string>();
            public readonly List<string> MatchedTribes = new List<string>();
        }

        private readonly object _cacheSync = new object();
        private readonly Dictionary<string, ParsedRules> _cache
            = new Dictionary<string, ParsedRules>(StringComparer.Ordinal);

        public TrinketEvaluation Evaluate(TrinketFact fact, string dominantTribe)
        {
            if (string.IsNullOrEmpty(fact.CardId))
                return new TrinketEvaluation();

            var result = new TrinketEvaluation
            {
                IsValid = true,
                IsLesser = fact.IsLesser,
                DisplayName = !string.IsNullOrEmpty(fact.NameZhCn)
                    ? fact.NameZhCn
                    : !string.IsNullOrEmpty(fact.NameEnUs) ? fact.NameEnUs : fact.CardId,
            };
            string text = Normalize(fact.TextZhCn) + " " + Normalize(fact.TextEnUs);
            ParsedRules parsed = GetOrParse(fact.CardId, text);
            result.IsRated = parsed.IsRated;
            result.RuleScore = parsed.RuleScore;
            result.MatchedRuleIds.AddRange(parsed.MatchedRuleIds);
            result.MatchedTribes.AddRange(parsed.MatchedTribes);
            if (result.IsRated && !string.IsNullOrEmpty(dominantTribe)
                && result.MatchedTribes.Contains(dominantTribe))
            {
                result.MatchedRuleIds.Add("dominant_tribe");
                result.RuleScore++;
            }
            return result;
        }

        private ParsedRules GetOrParse(string cardId, string text)
        {
            lock (_cacheSync)
            {
                ParsedRules cached;
                if (_cache.TryGetValue(cardId, out cached)
                    && string.Equals(cached.NormalizedText, text, StringComparison.Ordinal))
                    return cached;

                ParsedRules parsed = Parse(text);
                parsed.NormalizedText = text;
                _cache[cardId] = parsed;
                return parsed;
            }
        }

        private static ParsedRules Parse(string text)
        {
            var parsed = new ParsedRules();
            if (Regex.IsMatch(text,
                "永久|每当|每回合|每[0-9一二三四五六七八九十]+个回合"
                + "|在(?:你的|每个)回合(?:开始|结束)时"
                + "|permanently|whenever|each turn|every [0-9]+ turns?"
                + "|at the (?:start|end) of (?:your|each|every) turn",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                parsed.MatchedRuleIds.Add("scaling");
                parsed.RuleScore++;
            }
            if (Regex.IsMatch(text,
                "铸币|金币|刷新|\\b(?:Gold|Coin|Refresh)\\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                parsed.MatchedRuleIds.Add("economy");
                parsed.RuleScore++;
            }
            if (Regex.IsMatch(text,
                "圣盾|剧毒|烈毒|Divine Shield|Poisonous|Venomous",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                parsed.MatchedRuleIds.Add("protection");
                parsed.RuleScore++;
            }
            if (Regex.IsMatch(text,
                "免费|发现|获取|获得一张|选择一个|\\b(?:Free|Discover|Get|Choose)\\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                parsed.MatchedRuleIds.Add("generation");
                parsed.RuleScore++;
            }
            int positiveCount = parsed.MatchedRuleIds.Count;
            if (Regex.IsMatch(text, "替换本饰品|replace this",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                parsed.MatchedRuleIds.Add("replacement");
                parsed.RuleScore--;
            }
            if (Regex.IsMatch(text, "复仇|\\bAvenge\\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                parsed.MatchedRuleIds.Add("avenge");
            if (Regex.IsMatch(text, "金色|\\bGolden\\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                parsed.MatchedRuleIds.Add("golden");
            parsed.IsRated = positiveCount > 0;
            AddMatchedTribes(text, parsed.MatchedTribes);
            return parsed;
        }

        private static void AddMatchedTribes(string text, List<string> matches)
        {
            var tribes = new[]
            {
                new[] { "野兽", "\\bBeasts?\\b" },
                new[] { "机械", "\\bMechs?\\b" },
                new[] { "恶魔", "\\bDemons?\\b" },
                new[] { "龙", "\\bDragons?\\b" },
                new[] { "元素", "\\bElementals?\\b" },
                new[] { "亡灵", "\\bUndead\\b" },
                new[] { "海盗", "\\bPirates?\\b" },
                new[] { "野猪人", "\\bQuilboars?\\b" },
                new[] { "纳迦", "\\bNagas?\\b" },
                new[] { "鱼人", "\\bMurlocs?\\b" },
            };
            foreach (string[] tribe in tribes)
            {
                if (text.Contains(tribe[0]) || Regex.IsMatch(text, tribe[1],
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    matches.Add(tribe[0]);
            }
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            string normalized = Regex.Replace(value, "<[^>]*>", " ");
            normalized = normalized.Replace("&nbsp;", " ");
            return Regex.Replace(normalized, "\\s+", " ").Trim();
        }
    }
}
