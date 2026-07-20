using System;
using System.Collections.Generic;

namespace BobCoach.Engine
{
    /// <summary>卡牌对局用途分类</summary>
    public enum CardPurpose { Combat, Economy, Core }

    /// <summary>S/A/B质量评级</summary>
    public enum QualityTier { None, S, A, B }

    /// <summary>
    /// 卡牌质量评估：融合价值评分+抓取率，输出S/A/B评级。
    /// S=高分高抓取率 A=高分一般抓取 B=一般分高抓取
    /// </summary>
    public static class CardQuality
    {
        // CardId → pick rate (0.0-1.0), 从外部数据加载
        private static Dictionary<string, float> _pickRates = new Dictionary<string, float>();
        private static bool _loaded;

        /// <summary>从JSON加载抓取率数据。支持两种格式: {"cardId": 0.85} 或 {"cards": {"cardId": 0.85}}</summary>
        public static void LoadPickRates(string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                _pickRates.Clear();

                // 检测Firestone格式: {"cards": {"BG19_010": 0.852, ...}}
                int cardsIdx = json.IndexOf("\"cards\"");
                if (cardsIdx >= 0)
                {
                    int objStart = json.IndexOf('{', cardsIdx + 7);
                    if (objStart >= 0)
                    {
                        int pos = objStart + 1;
                        while (pos < json.Length && json[pos] != '}')
                        {
                            pos = SkipWhitespace(json, pos);
                            if (pos >= json.Length || json[pos] == '}') break;
                            if (json[pos] != '"') { pos++; continue; }
                            var cardId = ReadString(json, ref pos);
                            pos = SkipWhitespace(json, pos);
                            if (pos < json.Length && json[pos] == ':') pos++;
                            pos = SkipWhitespace(json, pos);
                            var numStr = ReadNumber(json, ref pos);
                            float rate;
                            if (float.TryParse(numStr, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out rate))
                            {
                                _pickRates[cardId] = Math.Max(0, Math.Min(1, rate));
                            }
                            pos = SkipWhitespace(json, pos);
                            if (pos < json.Length && json[pos] == ',') pos++;
                        }
                    }
                }
                else
                {
                    // 旧格式: {"cardId": 0.85, ...}
                    int pos = 0;
                    while (pos < json.Length)
                    {
                        pos = SkipTo(json, '"', pos);
                        if (pos < 0) break;
                        var cardId = ReadString(json, ref pos);
                        pos = SkipTo(json, ':', pos);
                        if (pos < 0) break;
                        pos++;
                        pos = SkipWhitespace(json, pos);
                        var numStr = ReadNumber(json, ref pos);
                        float rate;
                        if (float.TryParse(numStr, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out rate))
                        {
                            _pickRates[cardId] = Math.Max(0, Math.Min(1, rate));
                        }
                    }
                }
                _loaded = _pickRates.Count > 0;
            }
            catch { }
        }

        /// <summary>获取卡牌抓取率，未知卡牌返回默认0.5</summary>
        public static float GetPickRate(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return 0.5f;
            float rate;
            return _pickRates.TryGetValue(cardId, out rate) ? rate : 0.5f;
        }

        /// <summary>获取所有pick rates (用于TimewarpSpellRegistry评分)</summary>
        public static Dictionary<string, double> GetPickRates()
        {
            var result = new Dictionary<string, double>();
            foreach (var kv in _pickRates) result[kv.Key] = kv.Value;
            return result;
        }

        /// <summary>综合评分+抓取率计算质量等级</summary>
        public static QualityTier ComputeTier(double score, float pickRate)
        {
            // score: 价值函数归一化分(0-1), pickRate: 抓取率(0-1)
            double combined = score * 0.6 + pickRate * 0.4;
            if (combined >= 0.72 && pickRate >= 0.6 && score >= 0.60) return QualityTier.S;
            if (combined >= 0.52) return QualityTier.A;
            if (combined >= 0.35) return QualityTier.B;
            return QualityTier.None;
        }

        /// <summary>根据卡牌分类和经济/战力/成长值判断用途</summary>
        public static CardPurpose ClassifyPurpose(
            CardClassifier.CardClassification? cls, bool isSpell,
            string cardName, string tribe, int tier)
        {
            if (!cls.HasValue)
                return CardPurpose.Combat;

            var c = cls.Value;

            if (c.IsCoreCombo) return CardPurpose.Core;

            // 经济判定: 经济型法术/随从, 法术来源
            if (c.PrimaryRole == CardClassifier.CardRole.Economy ||
                c.PrimaryRole == CardClassifier.CardRole.SpellSource ||
                c.PrimaryRole == CardClassifier.CardRole.SpellCopier ||
                c.PrimaryRole == CardClassifier.CardRole.ValueGenerator)
                return CardPurpose.Economy;

            // 经济价值>0.5且战力值低 → 经济
            if (c.EconomyValue > 0.5f && c.CombatValue < 0.3f) return CardPurpose.Economy;

            // 法术默认偏经济/辅助
            if (isSpell && c.EconomyValue > 0.2f) return CardPurpose.Economy;

            // 其他 → 战力
            return CardPurpose.Combat;
        }

        // ── JSON parsing helpers ──

        private static int SkipTo(string json, char target, int pos)
        {
            while (pos < json.Length && json[pos] != target) pos++;
            return pos < json.Length ? pos : -1;
        }

        private static string ReadString(string json, ref int pos)
        {
            if (pos >= json.Length || json[pos] != '"') return "";
            int start = ++pos;
            while (pos < json.Length && json[pos] != '"')
            { if (json[pos] == '\\') pos++; pos++; }
            var s = json.Substring(start, pos - start);
            if (pos < json.Length) pos++;
            return s;
        }

        private static string ReadNumber(string json, ref int pos)
        {
            int start = pos;
            while (pos < json.Length && (char.IsDigit(json[pos]) || json[pos] == '.' || json[pos] == '-'))
                pos++;
            return json.Substring(start, pos - start);
        }

        private static int SkipWhitespace(string json, int pos)
        {
            while (pos < json.Length && (json[pos] == ' ' || json[pos] == '\t' || json[pos] == '\n' || json[pos] == '\r'))
                pos++;
            return pos;
        }
    }
}
