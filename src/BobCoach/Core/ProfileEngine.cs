using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BobCoach.Engine
{
    /// <summary>
    /// 单局游戏记录。
    /// </summary>
    public class GameRecord
    {
        public string HeroId = "";
        public string HeroName = "";
        public int FinalRank;           // 1-8, 1 = 第一
        public int TurnCount;
        public int LevelUpTurn4 = 1;    // 升到 4 本的回合
        public int LevelUpTurn5 = 1;    // 升到 5 本的回合
        public int LevelUpTurn6 = 1;    // 升到 6 本的回合
        public Dictionary<string, int> TribeCounts = new Dictionary<string, int>();
        public Dictionary<string, int> KeywordCounts = new Dictionary<string, int>();
        public int TripleCount;
        public double BoardPowerPeak;
        public DateTime Timestamp = DateTime.UtcNow;
        public List<TurnSnapshot> Turns = new List<TurnSnapshot>();  // 逐回合回放

        /// <summary>种族归一化比例</summary>
        public Dictionary<string, float> GetTribeRatios()
        {
            var result = new Dictionary<string, float>();
            int total = 0;
            foreach (var kv in TribeCounts) total += kv.Value;
            if (total == 0) return result;
            foreach (var kv in TribeCounts)
                result[kv.Key] = (float)kv.Value / total;
            return result;
        }

        /// <summary>关键词归一化比例</summary>
        public Dictionary<string, float> GetKeywordRatios()
        {
            var result = new Dictionary<string, float>();
            int total = 0;
            foreach (var kv in KeywordCounts) total += kv.Value;
            if (total == 0) return result;
            foreach (var kv in KeywordCounts)
                result[kv.Key] = (float)kv.Value / total;
            return result;
        }
    }

    /// <summary>逐回合快照——回放分析的核心数据</summary>
    public class TurnSnapshot
    {
        public int Turn;
        public int Gold, MaxGold, Tier, Health, Armor;
        public int BoardSize, ShopSize, HandSize;
        public double BoardPower, OpponentPower;
        // 完整随从数据 (含属性/关键词/金色)
        public List<MinionSnapshot> Board = new List<MinionSnapshot>();
        public List<MinionSnapshot> Shop = new List<MinionSnapshot>();
        public List<MinionSnapshot> Hand = new List<MinionSnapshot>();
        // 对手信息
        public List<OpponentSnapshot> Opponents = new List<OpponentSnapshot>();
        // 算法输出
        public string AlgoRecommendation;
        public string AlgoRule;
        public string LevelUpSuggestion;
        public string CompDirection;
        // 玩家操作 (从timeline/ops提取)
        public List<string> PlayerTimeline = new List<string>();
        // 决策引擎训练数据 (v1.4+)
        public string FeatureVector;        // 22维特征向量, 逗号分隔 (供回放训练)
        public string RecommendedActionJson; // 推荐动作详情JSON: {type,targetIndex,cardId,score}
        public string ActionScoresJson;      // 所有可选动作评分JSON: [{type,targetIndex,cardId,score},...]
        public DateTime Timestamp = DateTime.UtcNow;
    }

    /// <summary>随从快照——含完整战斗属性</summary>
    public class MinionSnapshot
    {
        public string CardId;
        public string Name;
        public int Attack, Health, Tier, Position;
        public bool Golden, Taunt, DivineShield, Reborn, Poisonous, Venomous, Windfury;
        public bool MegaWindfury, Stealth, Cleave, Overkill;
        public string Tribe;
        public bool IsSpell;
        public int Cost;
    }

    /// <summary>对手快照</summary>
    public class OpponentSnapshot
    {
        public string HeroId;
        public string HeroName;
        public int Health, TavernTier;
        public bool Alive;
        public List<MinionSnapshot> Board = new List<MinionSnapshot>();
    }

    /// <summary>
    /// 玩家累积特征档案。
    /// </summary>
    public class PlayerProfile
    {
        public int TotalGames;
        public double AverageRank;
        public double RankSum;
        public Dictionary<string, float> TribeRatios = new Dictionary<string, float>();
        public Dictionary<string, float> KeywordPref = new Dictionary<string, float>();
        public double AverageTripleRate;
        public double TripleCountSum;
        public DateTime LastUpdated = DateTime.UtcNow;
    }

    /// <summary>
    /// 个性化引擎。记录每局数据，构建玩家偏好档案，提供个性化评分修正。
    /// 数据本地存储为 %AppData%/BobCoach/player_profile.json。
    /// </summary>
    public class ProfileEngine
    {
        private PlayerProfile _profile;
        private static readonly string DataDir = BobCoachDataPaths.Root;
        private static readonly string ProfilePath = Path.Combine(DataDir, "player_profile.json");

        public PlayerProfile Profile { get { return _profile; } }

        public ProfileEngine()
        {
            _profile = Load();
        }

        // ── 记录 ──

        /// <summary>
        /// 一局结束后调用，更新累积档案。
        /// </summary>
        public void RecordGame(GameRecord record)
        {
            if (record == null) return;

            _profile.TotalGames++;
            _profile.RankSum += record.FinalRank;
            _profile.AverageRank = _profile.RankSum / _profile.TotalGames;
            _profile.TripleCountSum += record.TripleCount;
            _profile.AverageTripleRate = _profile.TripleCountSum / Math.Max(1, _profile.TotalGames);

            // 更新种族偏好（指数移动平均，EMA，权重 0.2）
            var tribeRatios = record.GetTribeRatios();
            foreach (var kv in tribeRatios)
            {
                float old;
                _profile.TribeRatios.TryGetValue(kv.Key, out old);
                _profile.TribeRatios[kv.Key] = old * 0.8f + kv.Value * 0.2f;
            }

            // 更新关键词偏好
            var keywordRatios = record.GetKeywordRatios();
            foreach (var kv in keywordRatios)
            {
                float old;
                _profile.KeywordPref.TryGetValue(kv.Key, out old);
                _profile.KeywordPref[kv.Key] = old * 0.8f + kv.Value * 0.2f;
            }

            _profile.LastUpdated = DateTime.UtcNow;
            Save();
        }

        // ── 个性化评分 ──

        /// <summary>
        /// 获取个性化修正后的卡牌评分。
        /// </summary>
        public float GetPersonalizedScore(string cardId, float baseScore, string tribe, string keyword)
        {
            float score = baseScore;
            float tribeBonus = GetTribeBonus(tribe);
            float keywordBonus = GetKeywordBonus(keyword);
            return score * (1f + tribeBonus + keywordBonus);
        }

        /// <summary>种族偏好修正系数 (-0.15 ~ +0.15)</summary>
        public float GetTribeBonus(string tribe)
        {
            if (string.IsNullOrEmpty(tribe)) return 0f;
            float ratio;
            _profile.TribeRatios.TryGetValue(tribe, out ratio);
            // ratio 是历史使用比例，高于 0.25 表示偏好，映射到 +0.15
            return (ratio - 0.2f) * 0.75f;
        }

        /// <summary>关键词偏好修正系数</summary>
        public float GetKeywordBonus(string keyword)
        {
            if (string.IsNullOrEmpty(keyword)) return 0f;
            float ratio;
            _profile.KeywordPref.TryGetValue(keyword, out ratio);
            return (ratio - 0.15f) * 0.5f;
        }

        // ── 持久化 ──

        private PlayerProfile Load()
        {
            try
            {
                if (!File.Exists(ProfilePath))
                    return new PlayerProfile();

                var json = File.ReadAllText(ProfilePath, Encoding.UTF8);
                return ParseProfile(json);
            }
            catch { return new PlayerProfile(); }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                var json = SerializeProfile(_profile);
                File.WriteAllText(ProfilePath, json, Encoding.UTF8);
            }
            catch { }
        }

        // ── 简单 JSON 序列化（无外部依赖） ──

        private PlayerProfile ParseProfile(string json)
        {
            var profile = new PlayerProfile();
            try
            {
                profile.TotalGames = ReadInt(json, "TotalGames");
                profile.AverageRank = ReadDouble(json, "AverageRank");
                profile.RankSum = ReadDouble(json, "RankSum");
                profile.AverageTripleRate = ReadDouble(json, "AverageTripleRate");
                profile.TripleCountSum = ReadDouble(json, "TripleCountSum");
                profile.TribeRatios = ReadDict(json, "TribeRatios");
                profile.KeywordPref = ReadDict(json, "KeywordPref");
            }
            catch { }
            return profile;
        }

        private string SerializeProfile(PlayerProfile p)
        {
            var sb = new StringBuilder();
            sb.Append("{");
            sb.AppendFormat("\"TotalGames\":{0},", p.TotalGames);
            sb.AppendFormat("\"AverageRank\":{0:F2},", p.AverageRank);
            sb.AppendFormat("\"RankSum\":{0:F1},", p.RankSum);
            sb.AppendFormat("\"AverageTripleRate\":{0:F3},", p.AverageTripleRate);
            sb.AppendFormat("\"TripleCountSum\":{0:F1},", p.TripleCountSum);
            sb.AppendFormat("\"TribeRatios\":{0},", SerializeDict(p.TribeRatios));
            sb.AppendFormat("\"KeywordPref\":{0},", SerializeDict(p.KeywordPref));
            sb.AppendFormat("\"LastUpdated\":\"{0:O}\"", p.LastUpdated);
            sb.Append("}");
            return sb.ToString();
        }

        private string SerializeDict(Dictionary<string, float> dict)
        {
            if (dict.Count == 0) return "{}";
            var items = new List<string>();
            foreach (var kv in dict)
                items.Add(string.Format("\"{0}\":{1:F3}", Escape(kv.Key), kv.Value));
            return "{" + string.Join(",", items) + "}";
        }

        private Dictionary<string, float> ReadDict(string json, string key)
        {
            var result = new Dictionary<string, float>();
            var search = "\"" + key + "\":{";
            var idx = json.IndexOf(search);
            if (idx < 0) return result;
            idx += search.Length;
            var end = json.IndexOf('}', idx);
            if (end < 0) return result;
            var content = json.Substring(idx, end - idx);
            var pairs = content.Split(',');
            foreach (var pair in pairs)
            {
                var colonIdx = pair.IndexOf(':');
                if (colonIdx < 0) continue;
                var k = pair.Substring(0, colonIdx).Trim().Trim('"');
                float v;
                if (float.TryParse(pair.Substring(colonIdx + 1).Trim(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out v))
                {
                    if (!string.IsNullOrEmpty(k)) result[k] = v;
                }
            }
            return result;
        }

        private int ReadInt(string json, string key)
        {
            var search = "\"" + key + "\":";
            var idx = json.IndexOf(search);
            if (idx < 0) return 0;
            idx += search.Length;
            var end = idx;
            while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '-'))
                end++;
            int val;
            int.TryParse(json.Substring(idx, end - idx), out val);
            return val;
        }

        private double ReadDouble(string json, string key)
        {
            var search = "\"" + key + "\":";
            var idx = json.IndexOf(search);
            if (idx < 0) return 0;
            idx += search.Length;
            var end = idx;
            while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '.' || json[end] == '-'))
                end++;
            double val;
            double.TryParse(json.Substring(idx, end - idx),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out val);
            return val;
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
