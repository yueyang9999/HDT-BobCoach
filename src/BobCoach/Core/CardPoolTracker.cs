using System.Collections.Generic;

namespace BobCoach.Engine
{
    /// <summary>
    /// 卡池追踪器。维护每张卡在公共卡池中的剩余份数。
    /// V1.1: 基于玩家操作 + 对手人数衰减估算。
    /// </summary>
    public class CardPoolTracker
    {
        // 各星级初始每卡份数 (BG 赛季标准值)
        private static readonly Dictionary<int, int> InitialCopiesPerTier = new Dictionary<int, int>
        {
            { 1, 16 }, { 2, 15 }, { 3, 13 }, { 4, 11 }, { 5, 9 }, { 6, 7 }
        };

        // cardId → (total copies, remaining copies)
        private Dictionary<string, int> _totalCopies = new Dictionary<string, int>();
        private Dictionary<string, int> _remainingCopies = new Dictionary<string, int>();

        // 本回合对手已购买计数（每个对手每回合买 1-3 张）
        private int _opponentsAlive = 7;
        private int _turnNumber = 1;

        public void Initialize(Dictionary<string, int> cardTiers)
        {
            Initialize(cardTiers, EffectiveGameRules.Default);
        }

        public void Initialize(
            Dictionary<string, int> cardTiers, EffectiveGameRules rules)
        {
            _totalCopies.Clear();
            _remainingCopies.Clear();

            foreach (var kv in cardTiers)
            {
                string cardId = kv.Key;
                int tier = kv.Value;
                if (tier < 1 || tier > 6) continue;

                bool isKnownBuddy = CardPoolSampler.IsKnownBuddyCard(cardId);
                if (isKnownBuddy && (!CardPoolSampler.IsBuddyRegistryCompatible()
                    || !BuddyCardPoolEvaluator.IsEnabled(rules, 1)))
                    continue;
                if (!isKnownBuddy && cardId.Contains("Buddy")) continue;

                int copies = isKnownBuddy
                    ? CardPoolSampler.GetBuddyInitialPoolCopies(cardId)
                    : (InitialCopiesPerTier.ContainsKey(tier)
                        ? InitialCopiesPerTier[tier] : 10);
                if (copies <= 0) continue;
                _totalCopies[cardId] = copies;
                _remainingCopies[cardId] = copies;
            }
        }

        /// <summary>按当局可用种族过滤卡池: 移除不在当前种族池中的随从和受限法术</summary>
        public void FilterByAvailableTribes(HashSet<string> availableTribes,
            Dictionary<string, (int tier, string tribe, bool isSpell)> cardMeta)
        {
            if (availableTribes == null || availableTribes.Count == 0 || cardMeta == null) return;

            var toRemove = new List<string>();
            foreach (var kv in _remainingCopies)
            {
                if (cardMeta.TryGetValue(kv.Key, out var meta))
                {
                    if (!CardPoolFilter.IsCardInPool(kv.Key, availableTribes, meta.tribe, meta.isSpell))
                        toRemove.Add(kv.Key);
                }
            }
            foreach (var cid in toRemove)
            {
                _totalCopies.Remove(cid);
                _remainingCopies.Remove(cid);
            }
        }

        /// <summary>通知回合变更，应用对手衰减。</summary>
        public void OnNewTurn(int turn, int opponentsAlive)
        {
            _turnNumber = turn;
            _opponentsAlive = opponentsAlive;
        }

        /// <summary>通知回合结束，估算对手购买。</summary>
        public void OnTurnEnd()
        {
            // 每个存活对手每回合大约购买 1-2 张随从
            // 对每张卡按比例衰减 (对手不买我们也看不到具体买了什么)
            // MVP 简化：不衰减，仅追踪自己操作
        }

        /// <summary>玩家购买了某张卡。</summary>
        public void OnBuyCard(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return;
            if (_remainingCopies.ContainsKey(cardId))
                _remainingCopies[cardId] = System.Math.Max(0, _remainingCopies[cardId] - 1);
        }

        /// <summary>玩家出售了某张卡（返回卡池）。</summary>
        public void OnSellCard(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return;
            if (_remainingCopies.ContainsKey(cardId) && _totalCopies.ContainsKey(cardId))
                _remainingCopies[cardId] = System.Math.Min(_totalCopies[cardId], _remainingCopies[cardId] + 1);
        }

        /// <summary>三连：移除 3 张普通卡（已在之前购买时各减 1，此处再补减 2）。</summary>
        public void OnTripleCard(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return;
            if (_remainingCopies.ContainsKey(cardId))
                _remainingCopies[cardId] = System.Math.Max(0, _remainingCopies[cardId] - 2);
        }

        /// <summary>获取某张卡的剩余份数。</summary>
        public int GetRemaining(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return 0;
            int result;
            _remainingCopies.TryGetValue(cardId, out result);
            return result;
        }

        /// <summary>获取某张卡的总份数。</summary>
        public int GetTotal(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return 0;
            int result;
            _totalCopies.TryGetValue(cardId, out result);
            return result;
        }

        /// <summary>获取某星级的剩余总份数（估算）。</summary>
        public int GetRemainingForTier(int tier)
        {
            int sum = 0;
            foreach (var kv in _remainingCopies)
            {
                int cardTier = 0;
                // 通过 total 反推 tier (MVP 简化)
                int total;
                if (_totalCopies.TryGetValue(kv.Key, out total))
                {
                    foreach (var t in InitialCopiesPerTier)
                    {
                        if (t.Value == total) { cardTier = t.Key; break; }
                    }
                }
                if (cardTier == tier) sum += kv.Value;
            }
            return sum;
        }

        /// <summary>估算某星级剩余比例 (0.0-1.0)。</summary>
        public double GetRemainingRatio(int tier)
        {
            int remaining = GetRemainingForTier(tier);
            int total = 0;
            foreach (var kv in _totalCopies)
            {
                int ct = 0;
                foreach (var t in InitialCopiesPerTier)
                {
                    if (t.Value == kv.Value) { ct = t.Key; break; }
                }
                if (ct == tier) total += kv.Value;
            }
            return total > 0 ? (double)remaining / total : 1.0;
        }
    }
}
