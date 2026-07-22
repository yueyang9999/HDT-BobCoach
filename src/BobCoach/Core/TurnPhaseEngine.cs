using System;

namespace BobCoach.Engine
{
    public enum GamePhase { Early, Mid, Late }
    public enum TurnStrategy { Econ, Level, Build, Survive, Spike }

    public class PhaseResult
    {
        public GamePhase Phase;
        public TurnStrategy Strategy;
        public float LevelUrgency;
        public float BuildUrgency;
        public float SurvivalUrgency;
        public bool IsPowerTurn;
        public string Description = "";
    }

    /// <summary>
    /// 回合阶段引擎。分类当前游戏阶段，输出策略建议 + 动态权重调整。
    /// 替代 DecisionEngine 中硬编码的 turn 判断和固定阈值。
    /// </summary>
    public class TurnPhaseEngine
    {
        // 阶段分界
        private const int EARLY_END = 4;
        private const int MID_END = 8;

        // 关键回合（金币爆发点）
        private static readonly int[] PowerTurns = { 3, 5, 7, 9 };

        /// <summary>
        /// 根据场面状态评估回合阶段与策略。
        /// </summary>
        public PhaseResult Evaluate(GameState state)
        {
            var result = new PhaseResult();
            if (state == null) return result;

            int turn = state.Turn;
            int gold = state.Gold;
            int tier = state.TavernTier;
            int health = state.Health;
            int boardSize = state.BoardMinions != null ? state.BoardMinions.Count : 0;

            // 阶段判定
            if (turn <= EARLY_END)
                result.Phase = GamePhase.Early;
            else if (turn <= MID_END)
                result.Phase = GamePhase.Mid;
            else
                result.Phase = GamePhase.Late;

            // 紧迫度计算
            result.LevelUrgency = ComputeLevelUrgency(state);
            result.BuildUrgency = ComputeBuildUrgency(boardSize, tier, turn, health);
            result.SurvivalUrgency = ComputeSurvivalUrgency(health, state.MaxHealth);

            // 是否关键回合
            result.IsPowerTurn = IsPowerTurn(turn, gold);

            // 策略选择
            result.Strategy = SelectStrategy(result, tier, boardSize, health, gold);
            result.Description = FormatDescription(result);

            return result;
        }

        private float ComputeLevelUrgency(GameState state)
        {
            int turn = state.Turn;
            int tier = state.TavernTier;
            int gold = state.Gold;
            int health = state.Health;
            if (tier >= 6) return 0f;

            float urgency = 0.5f;
            // 早期：升本很紧迫
            if (turn <= 4 && tier <= 2) urgency += 0.3f;
            // 中期：升本节奏
            if (turn >= 5 && turn <= 8 && tier <= 4) urgency += 0.15f;
            // 金币充裕
            EffectiveGameRules rules = state.EffectiveRules ?? EffectiveGameRules.Default;
            int? upCost = GameRuleEvaluator.GetUpgradeCost(state, rules);
            if (upCost.HasValue && gold >= upCost.Value + 3) urgency += 0.1f;
            // 低血量降低升本冲动
            if (health < 15) urgency -= 0.2f;
            if (health < 10) urgency -= 0.3f;

            return Clamp(urgency);
        }

        private float ComputeBuildUrgency(int boardSize, int tier, int turn, int health)
        {
            float urgency = 0.4f;
            // 场面空 → 急需补
            if (boardSize <= 2 && turn >= 5) urgency += 0.3f;
            // 满场 → 不需补
            if (boardSize >= 7) urgency -= 0.3f;
            // 后期高本 → 需要找核心
            if (tier >= 4 && boardSize < 6) urgency += 0.15f;
            // 低血量 → 急需战力
            if (health < 15) urgency += 0.2f;

            return Clamp(urgency);
        }

        private float ComputeSurvivalUrgency(int health, int maxHealth)
        {
            if (maxHealth <= 0) maxHealth = 30;
            float ratio = (float)health / maxHealth;
            if (ratio <= 0.25f) return 1.0f;
            if (ratio <= 0.4f) return 0.75f;
            if (ratio <= 0.6f) return 0.4f;
            return 0.1f;
        }

        private bool IsPowerTurn(int turn, int gold)
        {
            foreach (var pt in PowerTurns)
                if (turn == pt && gold >= 7) return true;
            return false;
        }

        private TurnStrategy SelectStrategy(PhaseResult pr, int tier, int boardSize, int health, int gold)
        {
            // 生存危机
            if (pr.SurvivalUrgency >= 0.7f)
                return TurnStrategy.Survive;
            // 战力爆发窗口
            if (pr.IsPowerTurn && tier >= 3 && boardSize >= 4)
                return TurnStrategy.Spike;
            // 升本优先
            if (pr.LevelUrgency >= 0.65f && health >= 20)
                return TurnStrategy.Level;
            // 场面建设
            if (pr.BuildUrgency >= 0.55f)
                return TurnStrategy.Build;
            // 经济积累
            if (pr.Phase == GamePhase.Early && gold >= 5)
                return TurnStrategy.Econ;

            return TurnStrategy.Build;
        }

        private string FormatDescription(PhaseResult pr)
        {
            string phaseName = pr.Phase == GamePhase.Early ? "早期" :
                               pr.Phase == GamePhase.Mid ? "中期" : "晚期";
            string stratName = pr.Strategy == TurnStrategy.Econ ? "攒钱/理财" :
                               pr.Strategy == TurnStrategy.Level ? "升本节奏" :
                               pr.Strategy == TurnStrategy.Build ? "补强场面" :
                               pr.Strategy == TurnStrategy.Survive ? "保命优先" : "战力爆发";
            return string.Format("{0}-{1}", phaseName, stratName);
        }

        /// <summary>
        /// 获取阶段调整后的V(s)权重。基于当前权重做微调，不改基向量结构。
        /// </summary>
        public float[] GetPhaseAdjustedWeights(float[] baseWeights, PhaseResult phase)
        {
            if (baseWeights == null) return null;
            var adjusted = (float[])baseWeights.Clone();

            switch (phase.Phase)
            {
                case GamePhase.Early:
                    // 早期: 升本权重大幅提升, 血量压力降低
                    // T2-T4是升本窗口期, 错过严重影响后续节奏
                    adjusted[FeatureExtractor.F_CAN_UPGRADE] += 0.10f;
                    adjusted[FeatureExtractor.F_HEALTH_PRESSURE] -= 0.10f;
                    adjusted[FeatureExtractor.F_BOARD_POWER] -= 0.02f;
                    // 早期升本后能拿到更高本卡, 升本动作本身应该更受鼓励
                    adjusted[FeatureExtractor.F_LEVEL] += 0.03f;
                    break;
                case GamePhase.Mid:
                    // 中期: 流派协同+0.05, 场面战力+0.03
                    adjusted[FeatureExtractor.F_TRIBE_SYNERGY] += 0.05f;
                    adjusted[FeatureExtractor.F_BOARD_POWER] += 0.03f;
                    adjusted[FeatureExtractor.F_TRIPLE_PROGRESS] += 0.03f;
                    break;
                case GamePhase.Late:
                    // 晚期: 血量压力+0.10, 血量+0.05, 升本-0.05
                    adjusted[FeatureExtractor.F_HEALTH_PRESSURE] += 0.10f;
                    adjusted[FeatureExtractor.F_HEALTH] += 0.05f;
                    adjusted[FeatureExtractor.F_CAN_UPGRADE] -= 0.05f;
                    adjusted[FeatureExtractor.F_BOARD_POWER] += 0.03f;
                    break;
            }

            // 生存策略覆盖：血量压力大幅提升
            if (phase.Strategy == TurnStrategy.Survive)
            {
                adjusted[FeatureExtractor.F_HEALTH_PRESSURE] += 0.12f;
                adjusted[FeatureExtractor.F_HEALTH] += 0.08f;
                adjusted[FeatureExtractor.F_CAN_UPGRADE] -= 0.10f;
                adjusted[FeatureExtractor.F_BOARD_POWER] += 0.05f;
            }

            return adjusted;
        }

        /// <summary>
        /// 根据阶段获取动态规则阈值。
        /// Early: 更容易升本, Mid: 更倾向买, Late: 更保守。
        /// </summary>
        public float GetDynamicThreshold(string thresholdName, GamePhase phase)
        {
            switch (thresholdName)
            {
                case "board_power_weak":
                    // 场面弱阈值：早期0.3, 中期0.5, 晚期0.8
                    return phase == GamePhase.Early ? 0.3f :
                           phase == GamePhase.Mid ? 0.5f : 0.8f;
                case "shop_good_min_tier_diff":
                    // 店"好"所需的最低等级差
                    return phase == GamePhase.Early ? 1 : 2;
                default:
                    return 1.0f;
            }
        }

        private static float Clamp(float v) => Math.Max(0f, Math.Min(1f, v));
    }
}
