using System;
using System.Collections.Generic;

namespace BobCoach.Engine
{
    /// <summary>伙伴(Buddy)注册表: 加载buddy_registry.json, 119个伙伴随从</summary>
    public class BuddyRegistry
    {
        public class BuddyEntry
        {
            public string HeroId;
            public string HeroName;
            public string BuddyId;
            public string BuddyName;
            public int Tier;
            public int Atk;
            public int Hp;
            public int GoldPerTurn;
            public int GoldOneTime;
            public int GoldPerBuy;
            public int StatPerTurn;
            public int StatPerBuy;
            public int StatCombat;
            public int StatOnSell;
            public int FreeCard;
            public int FreeDiscover;
            public int FreeRefresh;
            public int GoldenCreate;
            public int ExtraCopy;
            public int CostReduction;
            public string Special;
        }

        private Dictionary<string, BuddyEntry> _byHeroId = new Dictionary<string, BuddyEntry>();
        private Dictionary<string, BuddyEntry> _byBuddyId = new Dictionary<string, BuddyEntry>();
        private bool _loaded;

        public void LoadFromJson(string json)
        {
            _byHeroId.Clear(); _byBuddyId.Clear();
            try
            {
                int pos = 0;
                while (pos < json.Length)
                {
                    int keyIdx = json.IndexOf("\"TB_BaconShop_HERO_", pos);
                    if (keyIdx < 0) break;
                    int keyEnd = json.IndexOf("\"", keyIdx + 1);
                    int colonIdx = json.IndexOf(":", keyEnd);
                    if (colonIdx < 0) break;
                    int objStart = json.IndexOf("{", colonIdx);
                    if (objStart < 0) break;
                    int objEnd = FindBrace(json, objStart);
                    if (objEnd < 0) break;
                    string obj = json.Substring(objStart, objEnd - objStart + 1);
                    var b = ParseBuddy(obj);
                    if (b != null && !string.IsNullOrEmpty(b.HeroId))
                    {
                        _byHeroId[b.HeroId] = b;
                        if (!string.IsNullOrEmpty(b.BuddyId))
                            _byBuddyId[b.BuddyId] = b;
                    }
                    pos = objEnd + 1;
                }
                _loaded = true;
            }
            catch { _loaded = false; }
        }

        private BuddyEntry ParseBuddy(string obj)
        {
            var b = new BuddyEntry();
            b.HeroId = ExStr(obj, "heroId");
            if (string.IsNullOrEmpty(b.HeroId)) return null;
            b.HeroName = ExStr(obj, "heroName");
            b.BuddyId = ExStr(obj, "buddyId");
            b.BuddyName = ExStr(obj, "buddyName");
            b.Tier = ExInt(obj, "tier");
            b.Atk = ExInt(obj, "atk");
            b.Hp = ExInt(obj, "hp");
            b.GoldPerTurn = ExInt(obj, "goldPerTurn");
            b.GoldOneTime = ExInt(obj, "goldOneTime");
            b.GoldPerBuy = ExInt(obj, "goldPerBuy");
            b.StatPerTurn = ExInt(obj, "statPerTurn");
            b.StatPerBuy = ExInt(obj, "statPerBuy");
            b.StatCombat = ExInt(obj, "statCombat");
            b.StatOnSell = ExInt(obj, "statOnSell");
            b.FreeCard = ExInt(obj, "freeCard");
            b.FreeDiscover = ExInt(obj, "freeDiscover");
            b.FreeRefresh = ExInt(obj, "freeRefresh");
            b.GoldenCreate = ExInt(obj, "goldenCreate");
            b.ExtraCopy = ExInt(obj, "extraCopy");
            b.CostReduction = ExInt(obj, "costReduction");
            b.Special = ExStr(obj, "special");
            return b;
        }

        private static string ExStr(string j, string k) { string s = "\"" + k + "\""; int i = j.IndexOf(s); if (i < 0) return ""; i = j.IndexOf("\"", i + s.Length); if (i < 0) return ""; int e = j.IndexOf("\"", i + 1); if (e < 0) return ""; return j.Substring(i + 1, e - i - 1); }
        private static int ExInt(string j, string k) { int r; int.TryParse(ExStr(j, k), out r); return r; }
        private static int FindBrace(string s, int st) { int d = 0; for (int i = st; i < s.Length; i++) { if (s[i] == '{') d++; else if (s[i] == '}') { d--; if (d == 0) return i; } } return -1; }

        public bool IsLoaded => _loaded;
        public int Count => _byHeroId.Count;

        public BuddyEntry GetByHero(string heroId) { _byHeroId.TryGetValue(heroId ?? "", out var b); return b; }
        public BuddyEntry GetByBuddy(string buddyId) { _byBuddyId.TryGetValue(buddyId ?? "", out var b); return b; }

        /// <summary>伙伴评分(1-10): 基于经济和战力增益</summary>
        public int ScoreBuddy(BuddyEntry b)
        {
            if (b == null) return 3;
            int s = 3;
            s += b.GoldPerTurn * 2 + b.GoldOneTime + b.GoldPerBuy;
            s += b.StatPerTurn / 3 + b.StatPerBuy / 3 + b.StatCombat / 2 + b.StatOnSell / 5;
            if (b.FreeCard > 0) s += 3;
            if (b.FreeDiscover > 0) s += 3;
            if (b.FreeRefresh > 0) s += 2;
            if (b.GoldenCreate > 0) s += 3;
            if (b.ExtraCopy > 0) s += 3;
            if (b.CostReduction > 0) s += 2;
            if (!string.IsNullOrEmpty(b.Special) && b.Special != "none") s += 2;
            return Math.Min(10, s);
        }

        public List<BuddyEntry> GetAll() => new List<BuddyEntry>(_byHeroId.Values);
    }
}
