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

        public TrinketEvaluation Evaluate(TrinketFact fact, string dominantTribe,
            int healthArmor = -1, int turn = 0, IList<string> boardKeywords = null,
            int boardSize = -1)
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
            // R214 插件侧同步(对齐模拟器 R205 血量情境):
            //   血甲<=15(濒死)偏好即时战力(protection/avenge), 血甲>=16 且回合>=8 偏好经济
            if (result.IsRated && healthArmor >= 0)
            {
                if (healthArmor <= 15)
                {
                    bool hasTempo = result.MatchedRuleIds.Contains("protection")
                        || result.MatchedRuleIds.Contains("avenge");
                    bool hasEconomy = result.MatchedRuleIds.Contains("economy");
                    if (hasTempo) { result.RuleScore++; result.MatchedRuleIds.Add("low_hp_tempo"); }
                    if (hasEconomy) { result.RuleScore--; result.MatchedRuleIds.Add("low_hp_economy_penalty"); }
                }
                else if (healthArmor >= 16 && turn >= 8
                    && result.MatchedRuleIds.Contains("economy"))
                {
                    result.RuleScore++;
                    result.MatchedRuleIds.Add("healthy_economy");
                }
            }
            // P4 2026-08(用户实测: 回合6板面空仍推荐慢速经济饰品): 板面规模情境——
            //   板面随从<4(场面空虚)时偏好即时战力, 慢速经济/每N回合类减分(撑不到收益期=连败);
            //   板面满(>=6)且健康时慢速经济可接受(保持原逻辑)。
            if (result.IsRated && boardSize >= 0)
            {
                bool slowGain = result.MatchedRuleIds.Contains("economy")
                    || result.MatchedRuleIds.Contains("generation")
                    || (fact.TextZhCn ?? "").Contains("每两个回合")
                    || System.Text.RegularExpressions.Regex.IsMatch(fact.TextZhCn ?? "", "每[0-9一二三四五六七八九十]+个回合");
                if (boardSize < 4)
                {
                    bool hasTempo = result.MatchedRuleIds.Contains("protection")
                        || result.MatchedRuleIds.Contains("avenge")
                        || result.MatchedRuleIds.Contains("combat");
                    if (hasTempo) { result.RuleScore++; result.MatchedRuleIds.Add("empty_board_tempo"); }
                    if (slowGain) { result.RuleScore--; result.MatchedRuleIds.Add("empty_board_slow_penalty"); }
                }
            }
            // R214 插件侧同步(对齐模拟器 R204 板面关键词协同):
            //   boardKeywords 为机制枚举值(BATTLECRY/DEATHRATTLE/...), 饰品文本含对应中文 -> +1
            if (boardKeywords != null && boardKeywords.Count > 0)
            {
                ApplyBoardKeywordSynergy(text, boardKeywords, result);
            }
            return result;
        }

        private static void ApplyBoardKeywordSynergy(
            string text, IList<string> boardKeywords, TrinketEvaluation result)
        {
            var kwMap = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "BATTLECRY", "战吼" }, { "DEATHRATTLE", "亡语" },
                { "DIVINE_SHIELD", "圣盾" }, { "REBORN", "复生" },
                { "WINDFURY", "风怒" }, { "TAUNT", "嘲讽" },
                { "VENOMOUS", "剧毒" }, { "MAGNETIC", "磁力" },
                { "POISONOUS", "剧毒" }, { "START_OF_COMBAT", "战斗开始" },
                { "AURA", "光环" }, { "END_OF_TURN_TRIGGER", "回合结束" },
                { "BACON_RALLY", "进击" }, { "BACON_SPELLCRAFT_ID", "塑造法术" },
                { "AVENGE", "复仇" },
            };
            // R214 补充: 核心引擎标记(ENGINE_*)——铜须/瑞文类引擎加分更强(+2)
            // 对齐模拟器 R210: 板面有引擎时对应饰品加分
            var engineMap = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "ENGINE_BATTLECRY", "战吼" }, { "ENGINE_DEATHRATTLE", "亡语" },
                { "ENGINE_MAGNET", "磁力" }, { "ENGINE_END_OF_TURN", "回合结束" },
            };
            foreach (string eng in boardKeywords)
            {
                string engCn = null;
                if (engineMap.TryGetValue(eng, out engCn) && engCn != null
                    && text.IndexOf(engCn, StringComparison.Ordinal) >= 0)
                {
                    if (!result.IsRated)
                    {
                        result.IsRated = true;
                        result.MatchedRuleIds.Add("board_keyword_rated");
                    }
                    result.RuleScore += 2;
                    result.MatchedRuleIds.Add("board_" + eng);
                }
            }

            foreach (string kw in boardKeywords)
            {
                if (string.IsNullOrEmpty(kw)) continue;
                string cn = null;
                if (kwMap.TryGetValue(kw, out cn) && cn != null
                    && text.IndexOf(cn, StringComparison.Ordinal) >= 0)
                {
                    // R214: 板面关键词协同命中时提升评级——含战吼/亡语等协同文本的
                    // 饰品不匹配基础规则类别(scaling/economy/...), 原 IsRated=false 永不推荐
                    // (对齐模拟器 R204 修复: 枚举值映射使协同评分真正生效)
                    if (!result.IsRated)
                    {
                        result.IsRated = true;
                        result.MatchedRuleIds.Add("board_keyword_rated");
                    }
                    result.RuleScore++;
                    result.MatchedRuleIds.Add("board_" + kw);
                }
            }
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
                "永久|每当|每[个]?回合|每[0-9一二三四五六七八九十]+个回合"
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

            // P-大帝 2026-08(用户实测: 魔化血刀'施放酒馆法术→酒馆+1/+1'完全不识别, 未选为恶魔阵容做准备):
            // 酒馆法术协同规则——'施放酒馆法术'类饰品(配合时空干扰'法术额外施放'/恶魔阵容高价值)。
            if (Regex.IsMatch(text,
                "酒馆法术|施放.*法术|cast.*spell|Tavern spell|spell.*cast",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                parsed.MatchedRuleIds.Add("spell_synergy");
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
            // R214+ 跨回合价值识别(对齐模拟器 R209, 已全量验证不劣化):
            //   储备型(每回合获得/增长) / 变现型(卖出获取) / 杠杆型(发现高本/三连)
            //   仅作为 RuleScore 加分, 不计入评级门控(IsRated 由基础规则决定)
            int crossTurn = 0;
            if (Regex.IsMatch(text,
                "每回合[^。]*?(?:获得|铸币|金币|获取|增长)|每回合结束时[^。]*?(?:获得|铸币|金币|获取|增长)"
                + "|at the (?:start|end) of (?:your|each|every) turn[^.]*?(?:gain|gold|coin|get|grow)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                crossTurn += 1;
            if (Regex.IsMatch(text,
                "出售[^。]*?(?:获得|获取)|卖出[^。]*?(?:获得|获取)"
                + "|when (?:sold|you sell)[^.]*?(?:gain|get|obtain)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                crossTurn += 1;
            if (Regex.IsMatch(text,
                "发现[^。]*?等级[一二三四五六七0-9]|三连奖品"
                + "|discover[^.]*?(?:Tier|higher|Triple)|triple reward",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                crossTurn += 1;
            if (crossTurn > 0)
            {
                parsed.RuleScore += Math.Min(crossTurn, 2);
                parsed.MatchedRuleIds.Add("cross_turn");
            }
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
