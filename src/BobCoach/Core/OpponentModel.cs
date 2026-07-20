using System;
using System.Collections.Generic;

namespace BobCoach.Engine
{
    /// <summary>
    /// 对手启发式模型 v2.0。
    /// 基于438局回放+hero_rhythm.json统计校准的曲线，
    /// 在无HDT全量对手数据时估算典型对手状态。
    /// </summary>
    public static class OpponentModel
    {
        // ── 对手升本节奏 (turn → expected tavern tier, 回放统计中位值) ──
        private static readonly Dictionary<int, double> ExpectedOpponentTier = new Dictionary<int, double>
        {
            { 1, 1.0 }, { 2, 1.2 }, { 3, 1.8 }, { 4, 2.1 }, { 5, 2.8 },
            { 6, 3.1 }, { 7, 3.7 }, { 8, 4.2 }, { 9, 4.6 }, { 10, 5.0 },
            { 11, 5.2 }, { 12, 5.5 }, { 13, 5.7 }, { 14, 5.9 }, { 15, 6.0 },
        };

        // ── 对手板面战力 (turn → expected board power, 回放统计) ──
        private static readonly Dictionary<int, double> ExpectedOpponentPower = new Dictionary<int, double>
        {
            { 1, 0.3 }, { 2, 0.8 }, { 3, 1.5 }, { 4, 2.5 },
            { 5, 3.8 }, { 6, 5.0 }, { 7, 6.5 }, { 8, 8.0 },
            { 9, 9.5 }, { 10, 11.5 }, { 11, 13.5 }, { 12, 15.5 },
            { 13, 17.5 }, { 14, 19.5 }, { 15, 21.5 },
        };

        // ── 对手板面标准差 (turn → stddev, 体现对手差异) ──
        private static readonly Dictionary<int, double> OpponentPowerStdDev = new Dictionary<int, double>
        {
            { 1, 0.1 }, { 2, 0.3 }, { 3, 0.6 }, { 4, 1.0 },
            { 5, 1.5 }, { 6, 2.0 }, { 7, 2.5 }, { 8, 3.0 },
            { 9, 3.5 }, { 10, 4.0 }, { 11, 4.5 }, { 12, 5.0 },
            { 13, 5.5 }, { 14, 6.0 }, { 15, 6.5 },
        };

        private static Random _rng = new Random();

        /// <summary>
        /// 估算典型对手在当前回合的酒馆等级 (浮点, 含插值)。
        /// </summary>
        public static double EstimateOpponentTierFloat(int turn)
        {
            if (ExpectedOpponentTier.TryGetValue(turn, out double tier))
                return tier;
            return turn > 15 ? 6.0 : 1.0;
        }

        /// <summary>
        /// 估算典型对手在当前回合的酒馆等级 (整数, 向下取整)。
        /// </summary>
        public static int EstimateOpponentTier(int turn)
        {
            return (int)Math.Floor(EstimateOpponentTierFloat(turn));
        }

        /// <summary>
        /// 估算典型对手在当前回合的板面战力。
        /// </summary>
        public static double EstimateOpponentPower(int turn)
        {
            if (ExpectedOpponentPower.TryGetValue(turn, out double power))
                return power;
            return 21.5 + (turn - 15) * 2.0;
        }

        /// <summary>
        /// 带方差的对手战力估计（用于模拟不同对手强度）。
        /// </summary>
        public static double EstimateOpponentPowerWithVariance(int turn, int opponentIndex)
        {
            double basePower = EstimateOpponentPower(turn);
            double stddev = OpponentPowerStdDev.ContainsKey(turn) ? OpponentPowerStdDev[turn] : 5.0;
            // 用对手索引做种子, 保证同一对手在模拟中一致
            var seededRng = new Random(opponentIndex * 1000 + turn);
            double variance = (seededRng.NextDouble() - 0.5) * 2.0 * stddev; // [-stddev, +stddev]
            return Math.Max(0.1, basePower + variance);
        }

        /// <summary>
        /// 估算对手造成的伤害。伤害 = 对手酒馆等级 + 存活随从星级之和
        /// </summary>
        public static int EstimateDamage(int turn, double opponentPowerMultiplier = 1.0)
        {
            int tier = (int)Math.Ceiling(EstimateOpponentTierFloat(turn));
            double power = EstimateOpponentPower(turn) * opponentPowerMultiplier;
            int starsFromMinions = (int)Math.Ceiling(power / 1.2);
            return tier + starsFromMinions;
        }

        /// <summary>
        /// 升本致命风险评估。
        /// </summary>
        public static bool IsLevelUpLethal(int currentHealth, int turn, int nextTurnDamage = 0)
        {
            if (currentHealth <= 0) return true;
            int expectedDmg = EstimateDamage(turn);
            if (nextTurnDamage > 0) expectedDmg = Math.Max(expectedDmg, nextTurnDamage);
            return currentHealth <= expectedDmg + 2;
        }

        /// <summary>
        /// 混合对手评估: HDT数据可用时使用真实板面战力, 否则回退到启发式。
        /// </summary>
        public static double GetBestOpponentPower(List<OpponentData> hdtOpponents, int turn)
        {
            // v2.0: 优先使用HDT真实对手数据
            if (hdtOpponents != null && hdtOpponents.Count > 0)
            {
                double maxPower = 0;
                int aliveCount = 0;
                foreach (var o in hdtOpponents)
                {
                    if (!o.Alive) continue;
                    aliveCount++;
                    // 有板面数据时基于实际随从估算战力
                    if (o.BoardMinions != null && o.BoardMinions.Count > 0)
                    {
                        double oppBoardPower = 0;
                        foreach (var m in o.BoardMinions)
                        {
                            double mp = 0.3;
                            if (m.Tier >= 1 && m.Tier <= 6)
                                mp = new double[] { 0, 0.3, 0.5, 0.8, 1.2, 1.8, 2.5 }[m.Tier];
                            if (m.Golden) mp *= 1.5;
                            if (m.DivineShield) mp *= 1.15;
                            if (m.Reborn) mp *= 1.1;
                            if (m.Poisonous || m.Venomous) mp += 0.5;
                            oppBoardPower += mp;
                        }
                        if (oppBoardPower > maxPower) maxPower = oppBoardPower;
                    }
                }
                if (aliveCount > 0 && maxPower > 0) return maxPower;
            }

            // 回退到启发式
            return EstimateOpponentPower(turn);
        }

        /// <summary>
        /// 获取对手平均战力 (HDT数据优先, 含方差)。
        /// </summary>
        public static double GetAvgOpponentPower(List<OpponentData> hdtOpponents, int turn)
        {
            if (hdtOpponents != null && hdtOpponents.Count > 0)
            {
                double sum = 0;
                int count = 0;
                foreach (var o in hdtOpponents)
                {
                    if (!o.Alive) continue;
                    if (o.BoardMinions != null && o.BoardMinions.Count > 0)
                    {
                        foreach (var m in o.BoardMinions)
                        {
                            double mp = 0.3;
                            if (m.Tier >= 1 && m.Tier <= 6)
                                mp = new double[] { 0, 0.3, 0.5, 0.8, 1.2, 1.8, 2.5 }[m.Tier];
                            if (m.Golden) mp *= 1.5;
                            if (m.DivineShield) mp *= 1.15;
                            if (m.Reborn) mp *= 1.1;
                            sum += mp;
                        }
                        count++;
                    }
                }
                if (count > 0) return sum / count;
            }
            return EstimateOpponentPower(turn);
        }
    }
}
