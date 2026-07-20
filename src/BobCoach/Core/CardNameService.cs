using System;
using System.Collections.Generic;

namespace BobCoach.Engine
{
    internal interface ICardIdentitySource
    {
        bool TryGetCard(string cardId, out string zhCnName, out int tier);
    }

    /// <summary>
    /// 统一卡名解析服务 — 所有cardId→中文名的唯一入口。
    /// 解决之前三套独立实现的碎片化问题，并完整覆盖Token/金色/衍生卡。
    /// </summary>
    public static class CardNameService
    {
        private static readonly Dictionary<string, string> _nameCache = new Dictionary<string, string>();
        private static readonly Dictionary<string, int> _tierCache = new Dictionary<string, int>();
        private static readonly object Sync = new object();
        private static ICardIdentitySource _identitySource = new HearthDbCardIdentitySource();

        internal static void SetIdentitySourceForTests(ICardIdentitySource source)
        {
            lock (Sync)
            {
                _identitySource = source;
                _nameCache.Clear();
                _tierCache.Clear();
            }
        }

        internal static void ResetForTests()
        {
            lock (Sync)
            {
                _identitySource = new HearthDbCardIdentitySource();
                _nameCache.Clear();
                _tierCache.Clear();
            }
        }

        public static void Initialize()
        {
            lock (Sync)
            {
                if (_identitySource == null)
                    _identitySource = new HearthDbCardIdentitySource();
            }
        }

        /// <summary>获取卡牌中文名。Token/金色/衍生卡自动推导父卡名。</summary>
        public static string GetName(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return "";
            Initialize();
            lock (Sync)
            {

            // 1. 精确缓存
            string cn;
            if (_nameCache.TryGetValue(cardId, out cn) && !string.IsNullOrEmpty(cn))
                return cn;

            string localName;
            int localTier;
            if (_identitySource != null
                && _identitySource.TryGetCard(cardId, out localName, out localTier)
                && !string.IsNullOrEmpty(localName))
            {
                _nameCache[cardId] = localName;
                if (localTier >= 1 && localTier <= 6)
                    _tierCache[cardId] = localTier;
                return localName;
            }

            // 2. Token推导: 处理所有已知后缀模式
            string parentId = DeriveParentId(cardId);
            if (parentId != null)
            {
                // 2a. 父卡中文名缓存
                if (_nameCache.TryGetValue(parentId, out cn) && !string.IsNullOrEmpty(cn))
                    return cn + "(衍生)";
                if (_identitySource != null
                    && _identitySource.TryGetCard(parentId, out localName, out localTier)
                    && !string.IsNullOrEmpty(localName))
                {
                    _nameCache[parentId] = localName;
                    if (localTier >= 1 && localTier <= 6)
                        _tierCache[parentId] = localTier;
                    return localName + "(衍生)";
                }
                // 2b. 继续推导父卡的父卡 (嵌套Token)
                string grandParentId = DeriveParentId(parentId);
                if (grandParentId != null && _nameCache.TryGetValue(grandParentId, out cn) && !string.IsNullOrEmpty(cn))
                    return cn + "(衍生)";
            }

            // 3. 最终兜底
            return cardId;
            }
        }

        /// <summary>获取卡牌星级(1-6)，未知返回0。</summary>
        public static int GetTier(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return 0;
            Initialize();
            lock (Sync)
            {
            int tier;
            if (_tierCache.TryGetValue(cardId, out tier)) return tier;
            string localName;
            if (_identitySource != null
                && _identitySource.TryGetCard(cardId, out localName, out tier)
                && tier >= 1 && tier <= 6)
            {
                _tierCache[cardId] = tier;
                if (!string.IsNullOrEmpty(localName))
                    _nameCache[cardId] = localName;
                return tier;
            }
            // Token卡尝试父卡
            string parentId = DeriveParentId(cardId);
            if (parentId != null && _tierCache.TryGetValue(parentId, out tier)) return tier;
            if (parentId != null && _identitySource != null
                && _identitySource.TryGetCard(parentId, out localName, out tier)
                && tier >= 1 && tier <= 6)
            {
                _tierCache[parentId] = tier;
                if (!string.IsNullOrEmpty(localName))
                    _nameCache[parentId] = localName;
                return tier;
            }
            return 0;
            }
        }

        /// <summary>检查名字缓存中是否有此卡(用于UI覆盖层)。</summary>
        public static bool HasName(string cardId)
        {
            string name;
            return TryGetName(cardId, out name);
        }

        /// <summary>获取名字缓存(用于批量查询)。</summary>
        public static bool TryGetName(string cardId, out string name)
        {
            name = null;
            if (string.IsNullOrEmpty(cardId)) return false;
            string resolved = GetName(cardId);
            if (string.IsNullOrEmpty(resolved)
                || string.Equals(resolved, cardId, StringComparison.Ordinal))
                return false;
            name = resolved;
            return true;
        }

        /// <summary>获取Tier缓存(用于批量查询)。</summary>
        public static bool TryGetTier(string cardId, out int tier)
        {
            tier = GetTier(cardId);
            return tier >= 1 && tier <= 6;
        }

        // ── Token/Golden/衍生卡ID推导 ──

        private static string DeriveParentId(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return null;

            // 模式1: *_t, *_t2, *_t3 → 父卡 (Token标记)
            int tIdx = cardId.IndexOf('t');
            if (tIdx > 0)
            {
                string prefix = cardId.Substring(0, tIdx);
                // BG27_004_Gt → BG27_004_G (保留金色后缀)
                if (prefix.EndsWith("_G")) return prefix;
                // BG27_004_t → BG27_004 (去掉尾部_)
                if (prefix.EndsWith("_")) prefix = prefix.Substring(0, prefix.Length - 1);
                return prefix;
            }

            // 模式2: *_Gg, *_G → 金色版 (如 BG31_803_Gg)
            if (cardId.EndsWith("_Gg"))
            {
                return cardId.Substring(0, cardId.Length - 3);
            }
            if (cardId.EndsWith("_G") && !cardId.EndsWith("_Gg"))
            {
                return cardId.Substring(0, cardId.Length - 2);
            }

            // 模式3: *_b → Buddy
            if (cardId.EndsWith("_b"))
                return cardId.Substring(0, cardId.Length - 2);

            // 模式4: TB_BaconUps_* → 暗月奖品Token (尝试去掉数字后缀)
            if (cardId.StartsWith("TB_BaconUps_") && cardId.Length > 16)
            {
                // TB_BaconUps_045g → 尝试去掉尾部g → TB_BaconUps_045
                if (cardId.EndsWith("g"))
                {
                    string baseId = cardId.Substring(0, cardId.Length - 1);
                    if (_nameCache.ContainsKey(baseId)) return baseId;
                }
                return null;
            }

            return null;
        }

    }
}
