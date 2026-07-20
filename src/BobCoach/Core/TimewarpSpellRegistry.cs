using System;
using System.Collections.Generic;

namespace BobCoach.Engine
{
    /// <summary>时空法术注册表: 30张时空法术, 用pick_rates大数据评分</summary>
    public class TimewarpSpellRegistry
    {
        public class TimewarpEntry
        {
            public string CardId;
            public string Name;
            public int Tier;
            public int Cost;
            public string Text;
            public int GoldEquiv;       // 等效金币价值
            public int GoldBonus;
            public int StatBonus;
            public int DiscoverCount;
            public int FreeCard;
            public int GoldenCreate;
            public int CopyCreate;
            public int ArmorGain;
        }

        private Dictionary<string, TimewarpEntry> _byId = new Dictionary<string, TimewarpEntry>();
        private bool _loaded;

        // 卡牌pick rates (从card_pick_rates.json加载, 用于大数据评分)
        private Dictionary<string, double> _pickRates = new Dictionary<string, double>();

        public void LoadFromJson(string json)
        {
            _byId.Clear();
            try
            {
                int pos = 0;
                while (pos < json.Length)
                {
                    int keyIdx = json.IndexOf("\"BG34_Treasure_", pos);
                    if (keyIdx < 0) keyIdx = json.IndexOf("\"BG28_Treasure_", pos);
                    if (keyIdx < 0) keyIdx = json.IndexOf("\"EBG_Treasure_", pos);
                    if (keyIdx < 0) break;
                    int keyEnd = json.IndexOf("\"", keyIdx + 1);
                    int colonIdx = json.IndexOf(":", keyEnd);
                    if (colonIdx < 0) break;
                    int objStart = json.IndexOf("{", colonIdx);
                    if (objStart < 0) break;
                    int objEnd = FindBrace(json, objStart);
                    if (objEnd < 0) break;
                    string obj = json.Substring(objStart, objEnd - objStart + 1);
                    var t = Parse(obj);
                    if (t != null && !string.IsNullOrEmpty(t.CardId))
                        _byId[t.CardId] = t;
                    pos = objEnd + 1;
                }
                _loaded = true;
            }
            catch { _loaded = false; }
        }

        /// <summary>注入卡牌pick rates用于大数据评分</summary>
        public void SetPickRates(Dictionary<string, double> rates)
        {
            _pickRates = rates ?? new Dictionary<string, double>();
        }

        private TimewarpEntry Parse(string obj)
        {
            var t = new TimewarpEntry();
            t.CardId = ExStr(obj, "name");  // timewarp_registry用"name"作为cardId
            if (string.IsNullOrEmpty(t.CardId)) return null;
            t.Name = ExStr(obj, "name");
            t.Tier = ExInt(obj, "tier");
            t.Cost = ExInt(obj, "cost");
            t.Text = ExStr(obj, "text");
            t.GoldEquiv = ExInt(obj, "goldEquiv");
            t.GoldBonus = ExInt(obj, "goldBonus");
            t.StatBonus = ExInt(obj, "statBonus");
            t.DiscoverCount = ExInt(obj, "discoverCount");
            t.FreeCard = ExInt(obj, "freeCard");
            t.GoldenCreate = ExInt(obj, "goldenCreate");
            t.CopyCreate = ExInt(obj, "copyCreate");
            t.ArmorGain = ExInt(obj, "armorGain");
            return t;
        }

        private static string ExStr(string j, string k) { string s = "\"" + k + "\""; int i = j.IndexOf(s); if (i < 0) return ""; i = j.IndexOf("\"", i + s.Length); if (i < 0) return ""; int e = j.IndexOf("\"", i + 1); if (e < 0) return ""; return j.Substring(i + 1, e - i - 1); }
        private static int ExInt(string j, string k) { int r; int.TryParse(ExStr(j, k), out r); return r; }
        private static int FindBrace(string s, int st) { int d = 0; for (int i = st; i < s.Length; i++) { if (s[i] == '{') d++; else if (s[i] == '}') { d--; if (d == 0) return i; } } return -1; }

        public bool IsLoaded => _loaded;
        public int Count => _byId.Count;

        public TimewarpEntry GetById(string cardId) { _byId.TryGetValue(cardId ?? "", out var t); return t; }

        /// <summary>大数据评分: pickRate权重60% + 等效价值40%</summary>
        public double ScoreTimewarp(TimewarpEntry t)
        {
            if (t == null) return 0;
            // 等效金币价值
            double valScore = t.GoldEquiv * 0.5 + t.GoldBonus * 0.8 + t.StatBonus * 0.1
                             + t.DiscoverCount * 3 + t.FreeCard * 4 + t.GoldenCreate * 5
                             + t.CopyCreate * 3 + t.ArmorGain * 0.3;
            valScore = Math.Min(10, valScore);

            // pick rate评分
            double pickScore = 3;
            if (_pickRates.TryGetValue(t.CardId, out var pr))
                pickScore = pr * 10;  // pick rate 0-1 → 0-10

            return valScore * 0.4 + pickScore * 0.6;
        }

        public List<TimewarpEntry> GetAll() => new List<TimewarpEntry>(_byId.Values);
    }
}
