using System;
using System.Collections.Generic;

namespace BobCoach.Engine
{
    /// <summary>
    /// 精确概率计算器。基于超几何分布计算刷新看到特定卡、凑三连、升本后高本卡的概率。
    /// 依赖 CardPoolTracker 提供卡池剩余数量。
    /// </summary>
    public class ProbabilityCalculator
    {
        private CardPoolTracker _pool;

        // 各等级酒馆刷新数量
        private static readonly Dictionary<int, int> RefreshSlots = new Dictionary<int, int>
        {
            { 1, 3 }, { 2, 4 }, { 3, 4 }, { 4, 5 }, { 5, 5 }, { 6, 6 }
        };

        // ── 商店Tier分布 (2026-05 replay数据, 2840个商店样本) ──
        // shopTierPct[tavernTier][cardTier] = 出现概率
        private static readonly Dictionary<int, Dictionary<int, double>> ShopTierPct = new Dictionary<int, Dictionary<int, double>>
        {
            { 1, new Dictionary<int, double> { { 1, 1.00 } } },
            { 2, new Dictionary<int, double> { { 1, 0.56 }, { 2, 0.44 } } },
            { 3, new Dictionary<int, double> { { 1, 0.38 }, { 2, 0.49 }, { 3, 0.12 } } },
            { 4, new Dictionary<int, double> { { 1, 0.22 }, { 2, 0.32 }, { 3, 0.23 }, { 4, 0.21 }, { 5, 0.02 } } },
            { 5, new Dictionary<int, double> { { 1, 0.18 }, { 2, 0.19 }, { 3, 0.18 }, { 4, 0.23 }, { 5, 0.19 }, { 6, 0.03 } } },
            { 6, new Dictionary<int, double> { { 1, 0.20 }, { 2, 0.13 }, { 3, 0.18 }, { 4, 0.19 }, { 5, 0.20 }, { 6, 0.11 } } },
        };

        // 星级基础战力（与 FeatureExtractor.TierPower 对齐）
        private static readonly Dictionary<int, double> TierValue = new Dictionary<int, double>
        {
            { 1, 0.3 }, { 2, 0.5 }, { 3, 0.8 }, { 4, 1.2 }, { 5, 1.8 }, { 6, 2.5 }, { 7, 3.5 }
        };

        public ProbabilityCalculator(CardPoolTracker pool)
        {
            _pool = pool;
        }

        /// <summary>
        /// 下一次刷新看到特定随从的概率（超几何分布）。
        /// P = 1 - C(N-K, n) / C(N, n)
        /// </summary>
        public double ProbFindCardInNextRefresh(string cardId, int tavernTier)
        {
            if (_pool == null || string.IsNullOrEmpty(cardId)) return 0;

            int K = _pool.GetRemaining(cardId);   // 目标卡剩余数
            if (K <= 0) return 0;

            int N = _pool.GetRemainingForTier(tavernTier);  // 该等级总剩余
            if (N <= 0) return 0;

            int n = RefreshSlots.ContainsKey(tavernTier) ? RefreshSlots[tavernTier] : 4;
            if (n > N) n = N;

            // P(X >= 1) = 1 - C(N-K, n) / C(N, n)
            double totalComb = Combination(N, n);
            if (totalComb <= 0) return 0;
            double failComb = Combination(N - K, n);
            return 1.0 - failComb / totalComb;
        }

        /// <summary>
        /// 本回合凑成三连的概率（简化：有对子时，下次刷新看到第三张的概率）。
        /// </summary>
        public double ProbCompleteTripleThisTurn(GameState state, string pairedCardId, int refreshBudget)
        {
            if (_pool == null || state == null || string.IsNullOrEmpty(pairedCardId))
                return 0;

            int K = _pool.GetRemaining(pairedCardId);
            if (K <= 0) return 0;

            int N = _pool.GetRemainingForTier(state.TavernTier);
            if (N <= 0) return 0;

            int slots = RefreshSlots.ContainsKey(state.TavernTier) ? RefreshSlots[state.TavernTier] : 4;
            double probPerRefresh = 1.0 - Combination(N - K, slots) / Combination(N, slots);

            // 有 refreshBudget 次刷新机会
            double prob = 1.0 - Math.Pow(1.0 - probPerRefresh, refreshBudget);
            return prob;
        }

        /// <summary>
        /// 升本后第一次刷新看到高本（≥targetTier）卡的概率。
        /// </summary>
        public double ProbSeeHighTierAfterUpgrade(GameState state, int targetTier)
        {
            if (_pool == null || state == null) return 0;
            int newTier = Math.Min(state.TavernTier + 1, 6);
            if (newTier < targetTier) return 0;

            int N = _pool.GetRemainingForTier(newTier);
            int totalN = 0;
            for (int t = 1; t <= newTier; t++)
                totalN += _pool.GetRemainingForTier(t);

            if (totalN <= 0 || N <= 0) return 0.1; // 保守估计

            int slots = RefreshSlots.ContainsKey(newTier) ? RefreshSlots[newTier] : 4;
            double prob = 1.0 - Math.Pow((double)(totalN - N) / totalN, slots);
            return prob;
        }

        /// <summary>
        /// 对手战力估值：根据上一轮所受伤害反推。
        /// </summary>
        public int EstimateOpponentPower(int damageTaken, int ownLevel, int ownStarsSum)
        {
            // 伤害公式: tavernTier + sum(surviving_minion_tiers)
            // 反推: sum(surviving_minion_tiers) = damageTaken - ownLevel
            int starsSum = damageTaken - ownLevel;
            if (starsSum <= 0) return 0;

            // 每颗星大约对应 0.3-0.5 战力
            return (int)(starsSum * 3.5);
        }

        /// <summary>
        /// 估计当前酒馆等级下的期望商店价值（TierPower加权和×插槽数）。
        /// 基于 replay 实测 Tier 分布。
        /// </summary>
        public static double EstimateExpectedShopValue(int tavernTier)
        {
            if (!ShopTierPct.ContainsKey(tavernTier)) return 1.0;
            var dist = ShopTierPct[tavernTier];
            int slots = RefreshSlots.ContainsKey(tavernTier) ? RefreshSlots[tavernTier] : 4;
            double evPerSlot = 0;
            foreach (var kv in dist)
            {
                int cardTier = kv.Key;
                double prob = kv.Value;
                double val = TierValue.ContainsKey(cardTier) ? TierValue[cardTier] : 0.3;
                evPerSlot += prob * val;
            }
            return evPerSlot * slots;
        }

        /// <summary>
        /// 计算当前商店的实际价值（供刷新决策对比）。
        /// </summary>
        public static double ComputeCurrentShopValue(List<MinionData> shopMinions)
        {
            if (shopMinions == null || shopMinions.Count == 0) return 0;
            double val = 0;
            foreach (var m in shopMinions)
            {
                double p = TierValue.ContainsKey(m.Tier) ? TierValue[m.Tier] : 0.3;
                if (m.Golden) p *= 1.5;
                val += p;
            }
            return val;
        }

        /// <summary>
        /// 刷新期望增益: 正数=刷新后期待更好, 负数=当前店更好。
        /// 归一化到 [-1, 1] 范围, 供DecisionEngine调整刷新权重。
        /// </summary>
        public static double EstimateRefreshGain(int tavernTier, List<MinionData> currentShop)
        {
            double expectedNew = EstimateExpectedShopValue(tavernTier);
            double current = ComputeCurrentShopValue(currentShop);
            if (expectedNew <= 0) return 0;
            // 增益 = (期望新店 - 当前店) / 期望新店 → 归一化
            return (expectedNew - current) / expectedNew;
        }

        // ── 组合数计算 ──

        /// <summary>
        /// C(N, k) = N! / (k! * (N-k)!)
        /// 使用乘法递推避免溢出: C(N, k) = prod_{i=1..k} (N - k + i) / i
        /// </summary>
        private static double Combination(int N, int k)
        {
            if (k < 0 || k > N) return 0;
            if (k == 0 || k == N) return 1;
            if (k > N / 2) k = N - k;  // 对称性

            double result = 1.0;
            for (int i = 1; i <= k; i++)
            {
                result = result * (N - k + i) / i;
            }
            return result;
        }
    }
}
