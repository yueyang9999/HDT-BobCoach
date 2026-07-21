using System;
using System.Collections.Generic;
using System.Linq;

namespace BobCoach.Engine
{
    /// <summary>
    /// 从 GameState 提取 20 维特征向量，供 ValueFunction 线性加权评估。
    /// 特征设计对齐需求规格 §3.2。
    /// </summary>
    public class FeatureExtractor
    {
        private ProbabilityCalculator _probCalc;

        public void SetProbabilityCalculator(ProbabilityCalculator calc)
        {
            _probCalc = calc;
        }

        private ICardSemanticSource _semanticSource;
        private SemanticSynergyEvaluator _semanticSynergy;

        internal void SetCardSemanticSource(ICardSemanticSource source)
        {
            _semanticSource = source;
            _semanticSynergy = source == null ? null : new SemanticSynergyEvaluator(source);
        }

        public CardSemanticsData GetCardSemantics(string cardId)
        {
            if (_semanticSource == null || string.IsNullOrEmpty(cardId)) return null;
            CardSemanticsData semantics;
            try
            {
                return _semanticSource.TryGet(cardId, out semantics) ? semantics : null;
            }
            catch { return null; }
        }

        // 特征索引常量
        public const int F_LEVEL = 0;
        public const int F_HEALTH = 1;
        public const int F_GOLD = 2;
        public const int F_TURN = 3;
        public const int F_BOARD_POWER = 4;
        public const int F_HAND_POWER = 5;
        public const int F_HAS_TRIPLE = 6;
        public const int F_TRIPLE_REWARD = 7;
        public const int F_BEST_TAVERN = 8;
        public const int F_CAN_UPGRADE = 9;
        public const int F_AVG_OPPONENT = 10;
        public const int F_FROZEN_VALUE = 11;
        public const int F_FREE_REFRESH = 12;
        public const int F_BOARD_SIZE = 13;
        public const int F_TRIBE_SYNERGY = 14;
        public const int F_HEALTH_PRESSURE = 15;
        public const int F_GOLD_EFFICIENCY = 16;
        public const int F_TRIPLE_PROGRESS = 17;
        public const int F_LEVEL_DIFF = 18;
        public const int F_TAVERN_VALUE = 19;
        public const int F_SEMANTIC_SYNERGY = 20;
        public const int F_POSITIONING = 21;
        public const int FeatureCount = 22;

        // 星级基础战力（与 decision_tables.json board_power_estimation 对齐）
        public static readonly Dictionary<int, double> TierPower = new Dictionary<int, double>
        {
            { 1, 0.3 }, { 2, 0.5 }, { 3, 0.8 }, { 4, 1.2 }, { 5, 1.8 }, { 6, 2.5 }, { 7, 3.5 }
        };
        private const double GoldenMultiplier = 1.5;

        // ── 数据驱动阈值 (来源: 438局V3.2回放分析) ──
        /// <summary>升本安全边际 P25 (覆盖75%前4玩家)</summary>
        public static readonly Dictionary<int, double> LevelUpSafePower = new Dictionary<int, double>
        {
            { 2, 0.9 }, { 3, 0.9 }, { 4, 1.6 }, { 5, 1.8 }, { 6, 4.0 }
        };
        /// <summary>升本激进边际 P10 (覆盖90%前4玩家)</summary>
        public static readonly Dictionary<int, double> LevelUpAggressivePower = new Dictionary<int, double>
        {
            { 2, 0.9 }, { 3, 0.6 }, { 4, 0.9 }, { 5, 1.3 }, { 6, 2.5 }
        };
        /// <summary>升本安全板面数 P25</summary>
        public static readonly Dictionary<int, int> LevelUpSafeBoardSize = new Dictionary<int, int>
        {
            { 2, 3 }, { 3, 3 }, { 4, 4 }, { 5, 4 }, { 6, 5 }
        };
        /// <summary>升本激进板面数 P10</summary>
        public static readonly Dictionary<int, int> LevelUpAggressiveBoardSize = new Dictionary<int, int>
        {
            { 2, 3 }, { 3, 2 }, { 4, 2 }, { 5, 2 }, { 6, 3 }
        };
        /// <summary>标准升本回合 (22局0604-0606实测Top4: 3本T3.7, 4本T4.9, 6本T8.7)</summary>
        public static readonly Dictionary<int, int> StandardLevelTurn = new Dictionary<int, int>
        {
            { 2, 2 }, { 3, 3 }, { 4, 5 }, { 5, 7 }, { 6, 9 }
        };
        /// <summary>淘汰预警: 战力比<此值连续2回合→高概率淘汰</summary>
        public const double EliminationPowerRatio = 0.7;
        /// <summary>绝望差距: 板面<对手此比例→无法通过常规买牌追赶</summary>
        public const double DesperatePowerRatio = 0.5;
        /// <summary>翻盘窗口: 落后发生后需在此回合数内完成换阵</summary>
        public const int ComebackWindowTurns = 3;
        /// <summary>换阵触发: 板面数≥此值才考虑卖弱换强</summary>
        public const int PivotMinBoardSize = 5;
        /// <summary>换阵安全: 换阵回合不同时升本(仅11%换阵+升本)</summary>
        public const double PivotWithLevelRate = 0.11;

        public float[] Extract(GameState state)
        {
            if (state == null) return new float[FeatureCount];
            var f = new float[FeatureCount];

            f[F_LEVEL] = state.TavernTier;
            f[F_HEALTH] = state.Health / (float)Math.Max(1, state.MaxHealth);
            f[F_GOLD] = state.Gold / 10f;
            f[F_TURN] = state.Turn / 20f;
            // 前期(T1-T2): 真人更看重基础攻血属性, 关键词占比过高会误导值函数
            // 416局数据: T1 avgBoardSize=1.1, T2 avgBoardSize=2.0 — 前期随从少, 身材优先
            if (state.Turn <= 2 && state.BoardMinions != null && state.BoardMinions.Count > 0)
                f[F_BOARD_POWER] = ComputeBoardPowerEarly(state.BoardMinions);
            else
                f[F_BOARD_POWER] = ComputeBoardPower(state.BoardMinions);
            f[F_BOARD_POWER] += (float)Math.Min(1.0,
                (state.ActiveTrinketContext ?? ActiveTrinketContext.Empty)
                    .GetBoardSynergyScore(state.BoardMinions));
            f[F_HAND_POWER] = ComputeBoardPower(state.HandMinions) * 0.6f;  // 手牌不在场上, 战力打折
            f[F_HAS_TRIPLE] = HasTripleReady(state) ? 1f : 0f;
            f[F_TRIPLE_REWARD] = ComputeTripleProbability(state);
            f[F_BEST_TAVERN] = GetBestTavernScore(state.ShopMinions);
            f[F_CAN_UPGRADE] = CanAffordUpgrade(state) ? 1f : 0f;
            f[F_AVG_OPPONENT] = ComputeAvgOpponentPower(state.Opponents);
            f[F_FROZEN_VALUE] = state.FrozenShop ? EstimateFrozenValue(state) : 0f;
            f[F_FREE_REFRESH] = state.FreeRefreshCount / 3f;
            f[F_BOARD_SIZE] = state.BoardMinions.Count / 7f;
            f[F_TRIBE_SYNERGY] = ComputeTribeSynergy(state.BoardMinions);
            f[F_HEALTH_PRESSURE] = 1f - (state.Health / (float)Math.Max(1, state.MaxHealth));
            f[F_GOLD_EFFICIENCY] = state.Gold / (float)Math.Max(1, state.MaxGold);
            f[F_TRIPLE_PROGRESS] = ComputeTripleProgress(state);
            f[F_LEVEL_DIFF] = ComputeLevelDiff(state);
            f[F_TAVERN_VALUE] = ComputeTavernAvgScore(state.ShopMinions);
            f[F_SEMANTIC_SYNERGY] = ComputeSemanticSynergy(state);
            f[F_POSITIONING] = ComputePositioningScore(state.BoardMinions);

            return f;
        }

        private static int DictGet(Dictionary<string, int> dict, string key)
        {
            int val;
            dict.TryGetValue(key, out val);
            return val;
        }

        // ── 特征计算 ──

        public float ComputeBoardPower(List<MinionData> minions)
        {
            if (minions == null || minions.Count == 0) return 0f;
            double power = 0;
            foreach (var m in minions)
            {
                if (m == null) continue;
                double p = TierPower.ContainsKey(m.Tier) ? TierPower[m.Tier] : 0.3;
                // 金色卡牌: 身材翻倍(GoldenMultiplier=1.5x)
                // 金色不改变关键词规则(复生仍是1血, 圣盾仍是1次, 剧毒不穿透圣盾)
                if (m.Golden) p *= GoldenMultiplier;
                // 关键词加成
                if (m.Poisonous || m.Venomous) p += 1.5;  // 剧毒: 造成伤害即消灭(不穿透圣盾)
                if (m.DivineShield) p *= 1.35;              // 圣盾: 抵挡1次伤害
                if (m.Reborn) p *= 1.45;                     // 复生: 死亡后1血(金色满血)复活
                if (m.Windfury) p *= 1.25;                   // 风怒: 额外攻击1次
                if (m.Taunt) p *= 1.05;                      // 嘲讽: 保护后排
                power += p;
            }
            return (float)power;
        }

        // 前期战力评估(T1-T2): attack+health 属性占50%, TierPower占50%
        // 真人数据: T1-T2 boardSize 1-2, 关键词少, 身材决定交换优劣势
        private const double MaxEarlyStats = 12.0; // 早期随从 atk+hp 理论最大值 (如2/4=6)
        public float ComputeBoardPowerEarly(List<MinionData> minions)
        {
            if (minions == null || minions.Count == 0) return 0f;
            double power = 0;
            foreach (var m in minions)
            {
                if (m == null) continue;
                double rawStats = (m.Attack + m.Health) / MaxEarlyStats;
                double tierBase = TierPower.ContainsKey(m.Tier) ? TierPower[m.Tier] : 0.3;
                power += rawStats * 0.5 + tierBase * 0.5;
            }
            return (float)power;
        }

        // ── 站位评分 (36局真人数据) ──
        // 高分玩家模板: [风怒/剧毒] [圣盾] [高攻] [高攻] [中] [嘲讽] [引擎]
        // pos=0最左, pos=6最右
        public float ComputePositioningScore(List<MinionData> board)
        {
            if (board == null || board.Count <= 1) return 0.5f; // 0-1随从无站位可言, 中性分
            int n = board.Count;
            float score = 1.0f;
            float avgAtk = (float)board.Average(m => m.Attack);

            for (int i = 0; i < n; i++)
            {
                var m = board[i];
                float posRatio = n > 1 ? (float)i / (n - 1) : 0.5f; // 0=最左, 1=最右

                // 嘲讽: 理想在右侧(posRatio >= 0.7)
                if (m.Taunt)
                {
                    if (posRatio < 0.5f) score -= 0.12f;  // 嘲讽在左半区→扣分
                    else if (posRatio >= 0.7f) score += 0.06f; // 嘲讽在右端→加分
                }

                // 风怒/剧毒: 理想在左侧(posRatio <= 0.3), 先手破盾/换怪
                if (m.Windfury || m.Poisonous || m.Venomous)
                {
                    if (posRatio <= 0.25f) score += 0.05f;
                    else if (posRatio >= 0.7f) score -= 0.08f; // 风怒/剧毒放右边浪费先手
                }

                // 引擎卡(铜须/瑞文/男爵): 理想在极右(posRatio >= 0.8)
                // 检测: 低攻(低于均值60%) 或 已知引擎卡名
                bool isEngine = (m.Attack < avgAtk * 0.6f && m.Health > 0)
                    || (m.CardName != null && (m.CardName.Contains("铜须") || m.CardName.Contains("瑞文")
                        || m.CardName.Contains("男爵") || m.CardName.Contains("达卡莱")));
                if (isEngine && !m.Taunt)
                {
                    if (posRatio >= 0.8f) score += 0.06f;
                    else if (posRatio < 0.4f) score -= 0.10f; // 引擎暴露在最左很危险
                }

                // 圣盾: 理想在左侧(posRatio <= 0.4), 先手吸收伤害
                if (m.DivineShield)
                {
                    if (posRatio <= 0.35f) score += 0.04f;
                    else if (posRatio >= 0.7f) score -= 0.04f;
                }

                // 高攻(>=1.3倍均值): 避免扎堆在最左(暴露), 应在中间或圣盾后面
                if (m.Attack >= avgAtk * 1.3f && !m.Taunt && !m.Windfury)
                {
                    if (posRatio <= 0.15f && n >= 3) score -= 0.07f;
                    else if (posRatio >= 0.3f && posRatio <= 0.7f) score += 0.03f;
                }
            }

            return Math.Max(0.1f, Math.Min(1.0f, score));
        }

        public float GetBestTavernScore(List<MinionData> shop)
        {
            if (shop == null || shop.Count == 0) return 0f;
            float best = 0f;
            foreach (var m in shop)
            {
                double p = TierPower.ContainsKey(m.Tier) ? TierPower[m.Tier] : 0.3;
                if (m.Golden) p *= GoldenMultiplier;
                if (p > best) best = (float)p;
            }
            return best;
        }

        private float ComputeTavernAvgScore(List<MinionData> shop)
        {
            if (shop == null || shop.Count == 0) return 0f;
            float sum = 0f;
            foreach (var m in shop)
            {
                double p = TierPower.ContainsKey(m.Tier) ? TierPower[m.Tier] : 0.3;
                sum += (float)p;
            }
            return sum / shop.Count;
        }

        private bool HasTripleReady(GameState state)
        {
            if (state.BoardMinions == null || state.HandMinions == null) return false;
            var rules = state.EffectiveRules ?? EffectiveGameRules.Default;
            foreach (var cardId in GetOwnedNormalCardIds(state))
                if (TripleRuleEvaluator.CountOwnedCopies(state, cardId) >=
                    GetGoldenCopyRequirement(state, cardId, rules) - 1)
                    return true;
            return false;
        }

        private bool CanAffordUpgrade(GameState state)
        {
            int? cost = GameRuleEvaluator.GetUpgradeCost(
                state, state.EffectiveRules ?? EffectiveGameRules.Default);
            return cost.HasValue && state.Gold >= cost.Value;
        }

        public float ComputeAvgOpponentPower(List<OpponentData> opponents)
        {
            if (opponents == null || opponents.Count == 0) return 0f;
            float sum = 0f;
            int count = 0;
            foreach (var o in opponents)
            {
                if (!o.Alive) continue;
                if (o.BoardMinions == null) continue;
                float p = ComputeBoardPower(o.BoardMinions);
                sum += p;
                count++;
            }
            return count > 0 ? sum / count : 0f;
        }

        private float EstimateFrozenValue(GameState state)
        {
            if (state.ShopMinions == null || state.ShopMinions.Count == 0) return 0f;
            float val = 0f;
            int pairBonus = 0;
            var rules = state.EffectiveRules ?? EffectiveGameRules.Default;
            // 收集板面种族用于协同检测
            var boardTribes = new HashSet<string>();
            if (state.BoardMinions != null)
                foreach (var m in state.BoardMinions)
                    if (!string.IsNullOrEmpty(m.Tribe))
                        foreach (var t in MinionData.GetTribesArray(m.Tribe))
                            boardTribes.Add(t);

            foreach (var m in state.ShopMinions)
            {
                double p = TierPower.ContainsKey(m.Tier) ? TierPower[m.Tier] : 0.3;
                // 合金检测: 冻结店里的牌按当前规则阈值可完成金色随从
                if (!m.IsSpell && TripleRuleEvaluator.CompletesGolden(
                    state, m.CardId, rules))
                {
                    pairBonus++;
                    p *= 1.6; // 即将三连的牌价值更高
                }
                // 种族协同: 冻结店里的牌与场上种族匹配
                if (!string.IsNullOrEmpty(m.Tribe) && boardTribes.Count > 0)
                {
                    foreach (var t in MinionData.GetTribesArray(m.Tribe))
                        if (boardTribes.Contains(t)) { p *= 1.15; break; }
                }
                val += (float)p;
            }
            // 对子加成: 每个可完成的对子+0.15, 上限+0.3
            val += Math.Min(pairBonus * 0.15f, 0.3f);
            return Math.Min(1f, val / 5f);
        }

        private float ComputeTribeSynergy(List<MinionData> minions)
        {
            if (minions == null || minions.Count < 2) return 0f;
            var counts = new Dictionary<string, int>();
            foreach (var m in minions)
            {
                if (string.IsNullOrEmpty(m.Tribe)) continue;
                foreach (var t in MinionData.GetTribesArray(m.Tribe))
                    counts[t] = DictGet(counts, t) + 1;
            }
            int maxSame = 0;
            foreach (var kv in counts)
            {
                if (kv.Value > maxSame) maxSame = kv.Value;
            }
            return maxSame / (float)Math.Max(1, minions.Count);
        }

        private float ComputeTripleProgress(GameState state)
        {
            if (state.BoardMinions == null || state.HandMinions == null) return 0f;
            var rules = state.EffectiveRules ?? EffectiveGameRules.Default;
            int candidates = 0;
            foreach (var cardId in GetOwnedNormalCardIds(state))
                if (TripleRuleEvaluator.CountOwnedCopies(state, cardId) >=
                    GetGoldenCopyRequirement(state, cardId, rules) - 1)
                    candidates++;
            return Math.Min(1f, candidates / 3f);
        }

        private float ComputeTripleProbability(GameState state)
        {
            if (_probCalc == null) return 0f;
            if (state.BoardMinions == null || state.HandMinions == null) return 0f;
            var rules = state.EffectiveRules ?? EffectiveGameRules.Default;
            // B13修复: 遍历所有差一张合金候选，取最高概率
            float bestProb = 0f;
            int budget = state.Gold;
            foreach (var cardId in GetOwnedNormalCardIds(state))
            {
                if (TripleRuleEvaluator.CountOwnedCopies(state, cardId) >=
                    GetGoldenCopyRequirement(state, cardId, rules) - 1)
                {
                    float prob = (float)_probCalc.ProbCompleteTripleThisTurn(state, cardId, budget);
                    if (prob > bestProb) bestProb = prob;
                }
            }
            return bestProb;
        }

        private static HashSet<string> GetOwnedNormalCardIds(GameState state)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            if (state == null) return ids;
            if (state.BoardMinions != null)
                foreach (var card in state.BoardMinions)
                    if (card != null && !card.Golden && !card.IsSpell
                        && !string.IsNullOrEmpty(card.CardId)) ids.Add(card.CardId);
            if (state.HandMinions != null)
                foreach (var card in state.HandMinions)
                    if (card != null && !card.Golden && !card.IsSpell
                        && !string.IsNullOrEmpty(card.CardId)) ids.Add(card.CardId);
            return ids;
        }

        private static int GetGoldenCopyRequirement(
            GameState state, string cardId, EffectiveGameRules rules)
        {
            MinionData card = FindOwnedCard(state, cardId);
            var trinkets = state.ActiveTrinketContext
                ?? rules.ActiveTrinkets ?? ActiveTrinketContext.Empty;
            return trinkets.GetGoldenCopyRequirement(card, rules.GoldenCopyRequirement);
        }

        private static MinionData FindOwnedCard(GameState state, string cardId)
        {
            foreach (var card in state.BoardMinions)
                if (card != null && card.CardId == cardId) return card;
            foreach (var card in state.HandMinions)
                if (card != null && card.CardId == cardId) return card;
            return null;
        }

        private float ComputeLevelDiff(GameState state)
        {
            if (state.Opponents == null || state.Opponents.Count == 0) return 0f;
            float sum = 0f;
            int count = 0;
            foreach (var o in state.Opponents)
            {
                if (!o.Alive) continue;
                sum += state.TavernTier - o.TavernTier;
                count++;
            }
            if (count == 0) return 0f;
            float diff = sum / count;
            return (diff + 3f) / 6f; // 归一化到 [0,1]
        }

        // ── 语义特征 f[20] ──

        /// <summary>
        /// 计算商店卡牌与场上放大器的机制匹配度。
        /// 对每个商店卡牌的 implied_combos 检查场上是否有对应放大器。
        /// 归一化到 [0, 1]。
        /// </summary>
        private float ComputeSemanticSynergy(GameState state)
        {
            if (_semanticSynergy == null || state.ShopMinions == null || state.ShopMinions.Count == 0)
                return 0f;
            if (state.BoardMinions == null || state.BoardMinions.Count == 0)
                return 0f;
            return _semanticSynergy.ComputeWeightedShopScore(
                state.BoardMinions.Where(card => card != null).Select(card => card.CardId),
                state.ShopMinions.Where(card => card != null).Select(card => card.CardId));
        }
    }
}
