using System;
using System.Collections.Generic;
using System.Linq;

namespace BobCoach.Engine
{
    /// <summary>
    /// 卡池采样器。为刷新模拟提供真实卡牌采样能力。
    /// 从用户本机HearthDb读取注册成员事实，按tier索引，支持种族过滤。
    /// </summary>
    public static class CardPoolSampler
    {
        // tier → list of (cardId, tier, tribe_cn, isSpell)
        private static Dictionary<int, List<CardPoolEntry>> _cardsByTier
            = new Dictionary<int, List<CardPoolEntry>>();
        private static Dictionary<string, string> _timewarpKindByCardId
            = new Dictionary<string, string>();
        private static Dictionary<string, Dictionary<int, List<CardPoolEntry>>> _timewarpCardsByKind
            = new Dictionary<string, Dictionary<int, List<CardPoolEntry>>>();
        private static Dictionary<string, BuddyPoolMember> _buddyMembersByCardId
            = new Dictionary<string, BuddyPoolMember>();
        private static HashSet<string> _shopPoolCardIds = new HashSet<string>();
        private static HashSet<string> _buddyGoldenCardIds = new HashSet<string>();
        private static Dictionary<int, List<CardPoolEntry>> _buddyCardsByTier
            = new Dictionary<int, List<CardPoolEntry>>();

        // 商店tier分布 (从ProbabilityCalculator同步)
        private static readonly Dictionary<int, Dictionary<int, double>> ShopTierPct
            = new Dictionary<int, Dictionary<int, double>>
        {
            { 1, new Dictionary<int, double> { { 1, 1.00 } } },
            { 2, new Dictionary<int, double> { { 1, 0.56 }, { 2, 0.44 } } },
            { 3, new Dictionary<int, double> { { 1, 0.38 }, { 2, 0.49 }, { 3, 0.12 } } },
            { 4, new Dictionary<int, double> { { 1, 0.22 }, { 2, 0.32 }, { 3, 0.23 }, { 4, 0.21 }, { 5, 0.02 } } },
            { 5, new Dictionary<int, double> { { 1, 0.18 }, { 2, 0.19 }, { 3, 0.18 }, { 4, 0.23 }, { 5, 0.19 }, { 6, 0.03 } } },
            { 6, new Dictionary<int, double> { { 1, 0.20 }, { 2, 0.13 }, { 3, 0.18 }, { 4, 0.19 }, { 5, 0.20 }, { 6, 0.11 } } },
        };

        private static readonly Dictionary<int, int> ShopSlots = new Dictionary<int, int>
        {
            { 1, 3 }, { 2, 4 }, { 3, 4 }, { 4, 5 }, { 5, 5 }, { 6, 6 }
        };

        private static Random _rng = new Random();
        private static bool _initialized;
        private static HashSet<string> _lastAvailableTribesCn;
#if BOBCOACH_LEGACY_CARD_POOL_REGISTRY
        private static int _timewarpRegistryBuild;
        private static int _buddyRegistryBuild;
        private static int _shopPoolRegistryBuild;
        private static int _currentBuild;
#endif
        private static bool _usesLocalMembershipSnapshot;

        // 预计算的tier→card池(按可用种族过滤后)
        private static Dictionary<int, List<CardPoolEntry>> _filteredPool
            = new Dictionary<int, List<CardPoolEntry>>();
        private static Dictionary<string, Dictionary<int, List<CardPoolEntry>>> _filteredTimewarpPool
            = new Dictionary<string, Dictionary<int, List<CardPoolEntry>>>();
        private static Dictionary<int, List<CardPoolEntry>> _filteredBuddyPool
            = new Dictionary<int, List<CardPoolEntry>>();

        // 卡牌属性缓存: cardId → (attack, health, golden, keywords, tribe_cn)
        private static Dictionary<string, CachedCardStats> _cardStats
            = new Dictionary<string, CachedCardStats>();

        public struct CardPoolEntry
        {
            public string CardId;
            public int Tier;
            public string TribeCn;     // 逗号分隔或空
            public bool IsSpell;
            public int Cost;
        }

        public struct CachedCardStats
        {
            public int Attack;
            public int Health;
            public string TribeCn;
            public int Tier;
            public bool Golden;
            public bool Taunt;
            public bool DivineShield;
            public bool Reborn;
            public bool Poisonous;
            public bool Venomous;
            public bool Windfury;
        }

        private struct BuddyPoolMember
        {
            public string GoldenCardId;
            public int Tier;
            public int InitialPoolCopies;
        }

        public static bool IsInitialized { get { return _initialized; } }

        internal static void Initialize(
            ICardPoolFactSource source, HashSet<string> availableTribesCn)
        {
            ClearLocalFactPools();
            if (source == null || _shopPoolCardIds.Count == 0) return;
            var requiredIds = new HashSet<string>(_shopPoolCardIds, StringComparer.Ordinal);
            foreach (string cardId in _buddyMembersByCardId.Keys)
                if (!requiredIds.Add(cardId)) return;
            foreach (string cardId in _timewarpKindByCardId.Keys)
                if (!requiredIds.Add(cardId)) return;

            var cardsByTier = new Dictionary<int, List<CardPoolEntry>>();
            var cardStats = new Dictionary<string, CachedCardStats>();
            _buddyCardsByTier.Clear();
            _timewarpCardsByKind.Clear();
            for (int tier = 1; tier <= 6; tier++)
            {
                cardsByTier[tier] = new List<CardPoolEntry>();
                _buddyCardsByTier[tier] = new List<CardPoolEntry>();
            }
            foreach (string kind in new[] { "lesser", "greater" })
            {
                _timewarpCardsByKind[kind] = new Dictionary<int, List<CardPoolEntry>>();
                for (int tier = 1; tier <= 6; tier++)
                    _timewarpCardsByKind[kind][tier] = new List<CardPoolEntry>();
            }

            foreach (string cardId in _shopPoolCardIds)
            {
                CardPoolEntry entry;
                CachedCardStats stats;
                if (!TryReadLocalFact(source, cardId, out entry, out stats))
                {
                    ClearLocalFactPools();
                    return;
                }
                if (entry.Tier >= 1 && entry.Tier <= 6)
                    cardsByTier[entry.Tier].Add(entry);
                cardStats[cardId] = stats;
            }
            foreach (string cardId in _buddyMembersByCardId.Keys)
            {
                CardPoolEntry entry;
                CachedCardStats stats;
                if (!TryReadLocalFact(source, cardId, out entry, out stats))
                {
                    ClearLocalFactPools();
                    return;
                }
                if (entry.Tier >= 1 && entry.Tier <= 6)
                    _buddyCardsByTier[entry.Tier].Add(entry);
                cardStats[cardId] = stats;
            }
            foreach (var pair in _timewarpKindByCardId)
            {
                CardPoolEntry entry;
                CachedCardStats stats;
                if (!TryReadLocalFact(source, pair.Key, out entry, out stats))
                {
                    ClearLocalFactPools();
                    return;
                }
                if (entry.Tier >= 1 && entry.Tier <= 6)
                    _timewarpCardsByKind[pair.Value][entry.Tier].Add(entry);
                cardStats[pair.Key] = stats;
            }

            _cardsByTier = cardsByTier;
            _cardStats = cardStats;
            _lastAvailableTribesCn = availableTribesCn == null
                ? null : new HashSet<string>(availableTribesCn);
            RebuildFilteredPool(_lastAvailableTribesCn);
            _initialized = true;
        }

        internal static void Initialize(
            ILocalCardPoolMembershipSource membershipSource,
            ICardPoolFactSource factSource,
            HashSet<string> availableTribesCn)
        {
            ClearAllCardPoolState();
            LocalCardPoolMembershipSnapshot snapshot;
            if (!LocalCardPoolMembershipSnapshot.TryCreate(membershipSource, out snapshot))
                return;

            _shopPoolCardIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var member in snapshot.ShopMembers)
                _shopPoolCardIds.Add(member.CardId);

            _buddyMembersByCardId
                = new Dictionary<string, BuddyPoolMember>(StringComparer.Ordinal);
            _buddyGoldenCardIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var member in snapshot.BuddyMembers)
            {
                _buddyMembersByCardId.Add(member.CardId, new BuddyPoolMember
                {
                    GoldenCardId = member.GoldenCardId,
                    Tier = member.Tier,
                    InitialPoolCopies = member.InitialPoolCopies,
                });
                _buddyGoldenCardIds.Add(member.GoldenCardId);
            }

            _timewarpKindByCardId
                = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var member in snapshot.TimewarpMembers)
                _timewarpKindByCardId.Add(member.CardId, member.Kind);

            _usesLocalMembershipSnapshot = true;
            Initialize(factSource, availableTribesCn);
            if (!_initialized) ClearAllCardPoolState();
        }

        private static void ClearAllCardPoolState()
        {
            ClearLocalFactPools();
            _shopPoolCardIds.Clear();
            _buddyMembersByCardId.Clear();
            _buddyGoldenCardIds.Clear();
            _timewarpKindByCardId.Clear();
#if BOBCOACH_LEGACY_CARD_POOL_REGISTRY
            _shopPoolRegistryBuild = 0;
            _buddyRegistryBuild = 0;
            _timewarpRegistryBuild = 0;
#endif
            _usesLocalMembershipSnapshot = false;
        }

        private static void ClearLocalFactPools()
        {
            _initialized = false;
            _lastAvailableTribesCn = null;
            _cardsByTier.Clear();
            _buddyCardsByTier.Clear();
            _timewarpCardsByKind.Clear();
            _cardStats.Clear();
            _filteredPool.Clear();
            _filteredBuddyPool.Clear();
            _filteredTimewarpPool.Clear();
        }

        private static bool TryReadLocalFact(
            ICardPoolFactSource source,
            string cardId,
            out CardPoolEntry entry,
            out CachedCardStats stats)
        {
            entry = new CardPoolEntry();
            stats = new CachedCardStats();
            CardPoolFact fact;
            try
            {
                if (source == null || !source.TryGetCard(cardId, out fact)
                    || !string.Equals(fact.CardId, cardId, StringComparison.Ordinal)
                    || fact.Tier < 1 || fact.Tier > 7
                    || (fact.IsSpell ? fact.Cost < 0 : fact.Cost != -1)
                    || fact.Attack < 0 || fact.Health < 0
                    || string.IsNullOrEmpty(cardId))
                    return false;
            }
            catch
            {
                return false;
            }
            entry = new CardPoolEntry
            {
                CardId = cardId,
                Tier = fact.Tier,
                TribeCn = fact.TribeCn ?? "",
                IsSpell = fact.IsSpell,
                Cost = fact.Cost,
            };
            stats = new CachedCardStats
            {
                Attack = fact.Attack,
                Health = fact.Health,
                TribeCn = fact.TribeCn ?? "",
                Tier = fact.Tier,
            };
            return true;
        }

#if BOBCOACH_LEGACY_CARD_POOL_REGISTRY
        public static void LoadShopPoolRegistry(string json)
        {
            _shopPoolCardIds.Clear();
            _shopPoolRegistryBuild = 0;
            if (string.IsNullOrEmpty(json)) return;

            int build = ReadJsonInt(json, "build");
            int cardsKey = json.IndexOf("\"cards\"", StringComparison.Ordinal);
            int arrayStart = cardsKey >= 0 ? json.IndexOf('[', cardsKey) : -1;
            int arrayEnd = arrayStart >= 0 ? json.IndexOf(']', arrayStart) : -1;
            if (build <= 0 || arrayStart < 0 || arrayEnd < 0) return;

            var parsed = new HashSet<string>();
            int pos = arrayStart + 1;
            while (pos < arrayEnd)
            {
                while (pos < arrayEnd && (char.IsWhiteSpace(json[pos]) || json[pos] == ','))
                    pos++;
                if (pos >= arrayEnd) break;
                if (json[pos] != '"') return;
                int valueEnd = json.IndexOf('"', pos + 1);
                if (valueEnd < 0 || valueEnd > arrayEnd) return;
                string cardId = json.Substring(pos + 1, valueEnd - pos - 1);
                if (string.IsNullOrEmpty(cardId) || !parsed.Add(cardId)) return;
                pos = valueEnd + 1;
            }

            if (parsed.Count == 0) return;
            _shopPoolRegistryBuild = build;
            _shopPoolCardIds = parsed;
        }

        public static void LoadBuddyMinionRegistry(string json)
        {
            _buddyMembersByCardId.Clear();
            _buddyGoldenCardIds.Clear();
            _buddyRegistryBuild = 0;
            if (string.IsNullOrEmpty(json)) return;
            _buddyRegistryBuild = ReadJsonInt(json, "build");
            int pos = 0;
            while (pos < json.Length)
            {
                int cardKey = json.IndexOf("\"cardId\"", pos, StringComparison.Ordinal);
                if (cardKey < 0) break;
                int objectStart = json.LastIndexOf('{', cardKey);
                int objectEnd = objectStart >= 0 ? json.IndexOf('}', cardKey) : -1;
                if (objectStart < 0 || objectEnd < 0) break;
                string obj = json.Substring(objectStart, objectEnd - objectStart + 1);
                string cardId = ReadJsonString(obj, "cardId");
                string goldenCardId = ReadJsonString(obj, "goldenCardId");
                int tier = ReadJsonInt(obj, "tier");
                int initialPoolCopies = ReadJsonInt(obj, "initialPoolCopies");
                if (!string.IsNullOrEmpty(cardId) && !string.IsNullOrEmpty(goldenCardId)
                    && tier >= 1 && tier <= 6 && initialPoolCopies > 0)
                {
                    _buddyMembersByCardId[cardId] = new BuddyPoolMember
                    {
                        GoldenCardId = goldenCardId,
                        Tier = tier,
                        InitialPoolCopies = initialPoolCopies,
                    };
                    _buddyGoldenCardIds.Add(goldenCardId);
                }
                pos = objectEnd + 1;
            }
        }

        public static void LoadTimewarpMinionRegistry(string json)
        {
            _timewarpKindByCardId.Clear();
            _timewarpRegistryBuild = 0;
            if (string.IsNullOrEmpty(json)) return;
            _timewarpRegistryBuild = ReadJsonInt(json, "build");
            int pos = 0;
            while (pos < json.Length)
            {
                int cardKey = json.IndexOf("\"cardId\"", pos, StringComparison.Ordinal);
                if (cardKey < 0) break;
                int objectStart = json.LastIndexOf('{', cardKey);
                int objectEnd = objectStart >= 0 ? json.IndexOf('}', cardKey) : -1;
                if (objectStart < 0 || objectEnd < 0) break;
                string obj = json.Substring(objectStart, objectEnd - objectStart + 1);
                string cardId = ReadJsonString(obj, "cardId");
                string kind = ReadJsonString(obj, "kind");
                if (!string.IsNullOrEmpty(cardId) && (kind == "lesser" || kind == "greater"))
                    _timewarpKindByCardId[cardId] = kind;
                pos = objectEnd + 1;
            }
        }

        public static void SetCurrentBuild(int build)
        {
            _currentBuild = build > 0 ? build : 0;
        }
#endif

        /// <summary>重新按可用种族过滤卡池。</summary>
        public static void RebuildFilteredPool(HashSet<string> availableTribesCn)
        {
            _filteredPool.Clear();
            _filteredTimewarpPool.Clear();
            _filteredBuddyPool.Clear();
            foreach (var kind in new[] { "lesser", "greater" })
            {
                _filteredTimewarpPool[kind] = new Dictionary<int, List<CardPoolEntry>>();
                for (int t = 1; t <= 6; t++)
                    _filteredTimewarpPool[kind][t] = new List<CardPoolEntry>();
            }
            for (int t = 1; t <= 6; t++)
            {
                _filteredPool[t] = new List<CardPoolEntry>();
                _filteredBuddyPool[t] = new List<CardPoolEntry>();
                if (!_cardsByTier.ContainsKey(t)) continue;

                foreach (var entry in _cardsByTier[t])
                {
                    if (MatchesAvailableTribes(entry, availableTribesCn))
                        _filteredPool[t].Add(entry);
                }
                foreach (var kind in new[] { "lesser", "greater" })
                {
                    foreach (var entry in _timewarpCardsByKind[kind][t])
                        if (MatchesAvailableTribes(entry, availableTribesCn))
                            _filteredTimewarpPool[kind][t].Add(entry);
                }
                foreach (var entry in _buddyCardsByTier[t])
                    if (MatchesAvailableTribes(entry, availableTribesCn))
                        _filteredBuddyPool[t].Add(entry);
            }
        }

        private static bool MatchesAvailableTribes(
            CardPoolEntry entry, HashSet<string> availableTribesCn)
        {
            if (string.IsNullOrEmpty(entry.TribeCn)
                || availableTribesCn == null || availableTribesCn.Count == 0) return true;
            return entry.TribeCn.Split(',').Any(tribe =>
            {
                string value = tribe.Trim();
                return value == "中立" || value == "全部" || value == "ALL"
                    || (!string.IsNullOrEmpty(value) && availableTribesCn.Contains(value));
            });
        }

        /// <summary>
        /// 模拟一次刷新: 按tier分布抽取真实卡牌ID, 构造MinionData列表。
        /// </summary>
        public static List<MinionData> SampleShop(int tavernTier, HashSet<string> availableTribesCn)
        {
            return SampleShop(tavernTier, availableTribesCn, EffectiveGameRules.Default, 1);
        }

        public static List<MinionData> SampleShop(
            int tavernTier,
            HashSet<string> availableTribesCn,
            EffectiveGameRules rules,
            int turn)
        {
            if (availableTribesCn != null && availableTribesCn.Count >= 3
                && (_lastAvailableTribesCn == null
                    || !_lastAvailableTribesCn.SetEquals(availableTribesCn)))
            {
                _lastAvailableTribesCn = new HashSet<string>(availableTribesCn);
                RebuildFilteredPool(_lastAvailableTribesCn);
            }
            var result = new List<MinionData>();
            if (!_initialized || !IsShopPoolRegistryCompatible())
                return GenerateFallback(tavernTier);

            int slots = ShopSlots.ContainsKey(tavernTier) ? ShopSlots[tavernTier] : 3;
            var tierDist = ShopTierPct.ContainsKey(tavernTier) ? ShopTierPct[tavernTier] : ShopTierPct[1];

            for (int i = 0; i < slots; i++)
            {
                // 按tier分布权重选择tier
                double roll = _rng.NextDouble();
                double cum = 0;
                int chosenTier = 1;
                foreach (var kv in tierDist)
                {
                    cum += kv.Value;
                    if (roll <= cum) { chosenTier = kv.Key; break; }
                }
                chosenTier = Math.Min(chosenTier, tavernTier);
                chosenTier = Math.Max(1, chosenTier);

                // 从对应tier的过滤池中随机选一张卡
                var pool = GetEligiblePool(chosenTier, rules, turn);
                if ((pool == null || pool.Count == 0) && chosenTier != 1)
                    pool = GetEligiblePool(1, rules, turn);

                if (pool != null && pool.Count > 0)
                {
                    var entry = pool[_rng.Next(pool.Count)];
                    var minion = BuildMinionData(entry);
                    result.Add(minion);
                }
                else
                {
                    result.Add(FallbackMinion(chosenTier, i));
                }
            }

            return result;
        }

        public static bool IsCardEligibleForShop(
            string cardId, EffectiveGameRules rules, int turn)
        {
            if (string.IsNullOrEmpty(cardId) || !_initialized) return false;
            BuddyPoolMember buddyMember;
            if (_buddyMembersByCardId.TryGetValue(cardId, out buddyMember))
                return IsBuddyRegistryCompatible()
                    && BuddyCardPoolEvaluator.IsEnabled(rules, turn)
                    && _filteredBuddyPool.ContainsKey(buddyMember.Tier)
                    && _filteredBuddyPool[buddyMember.Tier]
                        .Any(entry => entry.CardId == cardId);
            if (_buddyGoldenCardIds.Contains(cardId)) return false;
            string kind;
            if (!_timewarpKindByCardId.TryGetValue(cardId, out kind))
                return IsShopPoolRegistryCompatible()
                    && _filteredPool.Values.Any(pool => pool.Any(entry => entry.CardId == cardId));
            if (!IsTimewarpRegistryCompatible()) return false;
            if (!TimewarpCardPoolEvaluator.IsMerged(rules, kind, turn)) return false;
            return _filteredTimewarpPool[kind].Values
                .Any(pool => pool.Any(entry => entry.CardId == cardId));
        }

        private static List<CardPoolEntry> GetEligiblePool(
            int tier, EffectiveGameRules rules, int turn)
        {
            var result = _filteredPool.ContainsKey(tier)
                ? new List<CardPoolEntry>(_filteredPool[tier])
                : new List<CardPoolEntry>();
            foreach (var kind in new[] { "lesser", "greater" })
            {
                if (IsTimewarpRegistryCompatible()
                    && TimewarpCardPoolEvaluator.IsMerged(rules, kind, turn)
                    && _filteredTimewarpPool.ContainsKey(kind)
                    && _filteredTimewarpPool[kind].ContainsKey(tier))
                    result.AddRange(_filteredTimewarpPool[kind][tier]);
            }
            if (IsBuddyRegistryCompatible()
                && BuddyCardPoolEvaluator.IsEnabled(rules, turn)
                && _filteredBuddyPool.ContainsKey(tier))
                result.AddRange(_filteredBuddyPool[tier]);
            return result;
        }

        private static bool IsTimewarpRegistryCompatible()
        {
            if (_usesLocalMembershipSnapshot) return _initialized;
#if BOBCOACH_LEGACY_CARD_POOL_REGISTRY
            return _currentBuild > 0 && _timewarpRegistryBuild == _currentBuild;
#else
            return false;
#endif
        }

        public static bool IsShopPoolRegistryCompatible()
        {
            if (_usesLocalMembershipSnapshot) return _initialized;
#if BOBCOACH_LEGACY_CARD_POOL_REGISTRY
            return _currentBuild > 0 && _shopPoolRegistryBuild == _currentBuild;
#else
            return false;
#endif
        }

        public static bool IsKnownBuddyCard(string cardId)
        {
            return !string.IsNullOrEmpty(cardId)
                && _buddyMembersByCardId.ContainsKey(cardId);
        }

        public static bool IsBuddyRegistryCompatible()
        {
            if (_usesLocalMembershipSnapshot) return _initialized;
#if BOBCOACH_LEGACY_CARD_POOL_REGISTRY
            return _currentBuild > 0 && _buddyRegistryBuild == _currentBuild;
#else
            return false;
#endif
        }

        public static int GetBuddyInitialPoolCopies(string cardId)
        {
            BuddyPoolMember member;
            return !string.IsNullOrEmpty(cardId)
                && _buddyMembersByCardId.TryGetValue(cardId, out member)
                ? member.InitialPoolCopies : 0;
        }

        internal static Dictionary<string, CardPoolFact> GetCardMetaSnapshot()
        {
            var result = new Dictionary<string, CardPoolFact>(StringComparer.Ordinal);
            if (!_initialized) return result;
            foreach (var byTier in _cardsByTier.Values)
                foreach (var entry in byTier) AddSnapshotFact(result, entry);
            foreach (var byTier in _buddyCardsByTier.Values)
                foreach (var entry in byTier) AddSnapshotFact(result, entry);
            foreach (var byKind in _timewarpCardsByKind.Values)
                foreach (var byTier in byKind.Values)
                    foreach (var entry in byTier) AddSnapshotFact(result, entry);
            return result;
        }

        private static void AddSnapshotFact(
            Dictionary<string, CardPoolFact> result, CardPoolEntry entry)
        {
            CachedCardStats stats;
            _cardStats.TryGetValue(entry.CardId, out stats);
            result[entry.CardId] = new CardPoolFact
            {
                CardId = entry.CardId,
                Tier = entry.Tier,
                TribeCn = entry.TribeCn ?? "",
                IsSpell = entry.IsSpell,
                Cost = entry.Cost,
                Attack = stats.Attack,
                Health = stats.Health,
            };
        }

#if BOBCOACH_LEGACY_CARD_POOL_REGISTRY
        private static string ReadJsonString(string json, string key)
        {
            string token = "\"" + key + "\"";
            int index = json.IndexOf(token, StringComparison.Ordinal);
            int colon = index >= 0 ? json.IndexOf(':', index + token.Length) : -1;
            int start = colon >= 0 ? json.IndexOf('"', colon + 1) : -1;
            int end = start >= 0 ? json.IndexOf('"', start + 1) : -1;
            return start >= 0 && end > start ? json.Substring(start + 1, end - start - 1) : "";
        }

        private static int ReadJsonInt(string json, string key)
        {
            string token = "\"" + key + "\"";
            int index = json.IndexOf(token, StringComparison.Ordinal);
            int colon = index >= 0 ? json.IndexOf(':', index + token.Length) : -1;
            int pos = colon + 1;
            while (pos > 0 && pos < json.Length && char.IsWhiteSpace(json[pos])) pos++;
            int start = pos;
            while (pos > 0 && pos < json.Length && char.IsDigit(json[pos])) pos++;
            int result;
            return start >= 0 && pos > start
                && int.TryParse(json.Substring(start, pos - start), out result) ? result : 0;
        }
#endif

        private static MinionData BuildMinionData(CardPoolEntry entry)
        {
            var m = new MinionData
            {
                CardId = entry.CardId,
                CardName = entry.CardId,
                Tier = entry.Tier,
                Tribe = entry.TribeCn ?? "",
                IsSpell = entry.IsSpell,
                Cost = entry.Cost,
                Position = 0,
            };

            // 从缓存获取战斗属性
            if (_cardStats.TryGetValue(entry.CardId, out var stats))
            {
                m.Attack = stats.Attack;
                m.Health = stats.Health;
                m.Golden = stats.Golden;
                m.Taunt = stats.Taunt;
                m.DivineShield = stats.DivineShield;
                m.Reborn = stats.Reborn;
                m.Poisonous = stats.Poisonous;
                m.Venomous = stats.Venomous;
                m.Windfury = stats.Windfury;
                m.Tier = stats.Tier > 0 ? stats.Tier : entry.Tier;
            }
            else
            {
                // 基于tier的默认属性
                int baseStats = entry.Tier * 2;
                m.Attack = baseStats;
                m.Health = baseStats;
            }

            return m;
        }

        private static List<MinionData> GenerateFallback(int tavernTier)
        {
            var result = new List<MinionData>();
            int slots = ShopSlots.ContainsKey(tavernTier) ? ShopSlots[tavernTier] : 3;
            for (int i = 0; i < slots; i++)
                result.Add(FallbackMinion(tavernTier, i));
            return result;
        }

        private static MinionData FallbackMinion(int tier, int pos)
        {
            return new MinionData
            {
                CardId = "sim_refresh",
                CardName = "刷新随从",
                Tier = tier,
                Attack = tier * 2,
                Health = tier * 2,
                Position = pos,
            };
        }

    }
}
