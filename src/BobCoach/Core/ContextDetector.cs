using System;

namespace BobCoach.Engine
{
    /// <summary>
    /// 游戏情境分类器。从GameState计算power_ratio/hp_pressure/tier_deficit/shop_advantage，
    /// 将当前局面分为5种情境之一，驱动后续策略调整。
    /// </summary>
    public class ContextDetector
    {
        private SituationType _previousContext = SituationType.STANDARD;
        private int _consecutiveMatchCount = 0;
        private const int HYSTERESIS_THRESHOLD = 2;

        /// <summary>
        /// 情境检测结果
        /// </summary>
        public struct ContextResult
        {
            public SituationType Type;
            public float PowerRatio;
            public float HpPressure;
            public int TierDeficit;
            public int ShopAdvantage;
            public float GoldCapacity;
        }

        /// <summary>
        /// 从GameState检测当前情境。含防抖(hysteresis)：需连续2次匹配同一情境才切换。
        /// </summary>
        public ContextResult Detect(GameState state, FeatureExtractor fe)
        {
            if (state == null || !state.GameActive)
                return new ContextResult { Type = SituationType.STANDARD };

            // ── 计算核心指标 ──
            float boardPower = fe.ComputeBoardPower(state.BoardMinions);
            float avgOppPower = fe.ComputeAvgOpponentPower(state.Opponents);
            float powerRatio = avgOppPower > 0.01f ? boardPower / avgOppPower : 99f;

            float hpPressure = state.MaxHealth > 0
                ? 1.0f - (float)state.Health / state.MaxHealth
                : 0f;

            int expectedTier = state.Turn <= 1 ? 1
                : state.Turn <= 4 ? 2
                : state.Turn <= 6 ? 3
                : state.Turn <= 8 ? 4
                : state.Turn <= 10 ? 5 : 6;
            int tierDeficit = Math.Max(0, expectedTier - state.TavernTier);

            int shopMaxTier = 0;
            if (state.ShopMinions != null)
                foreach (var m in state.ShopMinions)
                    if (m.Tier > shopMaxTier) shopMaxTier = m.Tier;
            int shopAdvantage = shopMaxTier - state.TavernTier;

            float goldCapacity = state.Gold / 3.0f;

            // ── 情境判定 ──
            SituationType detected;
            if (state.Health <= 10 || powerRatio < 0.35f)
                detected = SituationType.DESPERATE;
            else if (powerRatio < 0.7f && hpPressure >= 0.4f)
                detected = SituationType.UNDER_PRESSURE;
            else if (powerRatio >= 1.4f && hpPressure <= 0.3f && tierDeficit <= 0)
                detected = SituationType.POWER_CURVE;
            else if (goldCapacity >= 2.5f && state.BoardMinions.Count >= 5
                && hpPressure < 0.5f && tierDeficit <= 1)
                detected = SituationType.ECON_TURN;
            else
                detected = SituationType.STANDARD;

            // ── 防抖 ──
            if (detected == _previousContext)
                _consecutiveMatchCount++;
            else
            {
                _consecutiveMatchCount = 1;
                _previousContext = detected;
            }

            SituationType finalType = _consecutiveMatchCount >= HYSTERESIS_THRESHOLD
                ? detected : _previousContext;

            return new ContextResult
            {
                Type = finalType,
                PowerRatio = powerRatio,
                HpPressure = hpPressure,
                TierDeficit = tierDeficit,
                ShopAdvantage = shopAdvantage,
                GoldCapacity = goldCapacity
            };
        }

        /// <summary>重置防抖状态(新游戏开始时调用)</summary>
        public void Reset()
        {
            _previousContext = SituationType.STANDARD;
            _consecutiveMatchCount = 0;
        }
    }
}
