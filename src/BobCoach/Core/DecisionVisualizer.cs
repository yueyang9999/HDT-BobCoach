using System;
using System.Collections.Generic;
using System.Linq;

namespace BobCoach.Engine
{
    /// <summary>
    /// 决策→视觉转换器（GLM 5.1 精华）：将 DecisionEngine 输出转为去文字化 VisualPlan。
    /// 决策认知止于此层，Renderer 只消费 VisualPlan + 5种图元。
    /// </summary>
    public class DecisionVisualizer
    {
        private HeroPowerEngine _heroPower;
        private FeatureExtractor _fe;
        private List<HandMarker> _sellCandidates = new List<HandMarker>();
        private string _lockedComp = null;      // 流派滞后锁
        private int _compLockTurn = 0;          // 锁定时的回合号
        private int _compLockedFor = 0;          // 已锁定持续回合数（跨评估次数）
        private static readonly HashSet<string> _reportedRawIds = new HashSet<string>();  // cardId泄露去重(防每帧重复IO)
        private int _compDirDiagCount = 0;  // 流派方向诊断计数(0611 06111357 问题D龙族误判定位)

        /// <summary>
        /// 从决策引擎结果创建 VisualPlan
        /// </summary>
        public void Reset()
        {
            _lockedComp = null;
            _compLockTurn = 0;
            _compLockedFor = 0;
            _sellCandidates.Clear();
            _reportedRawIds.Clear();  // 每局重置: 同一缺数据卡每局可重新记录一次, 避免静态集合跨会话无界增长
        }

        private int GetBaseMinionCost(GameState state)
        {
            if (state == null) return 3;
            return GameRuleEvaluator.GetPurchaseCost(
                state, new MinionData { Tier = 1 }, state.HeroCardId,
                state.EffectiveRules ?? EffectiveGameRules.Default);
        }

        private bool CanAffordAfterOneSell(GameState state, int cost)
        {
            if (state == null) return false;
            if (state.Turn < 3) return false;
            int liveGold = state.LiveGold >= 0 ? state.LiveGold : state.Gold;
            if (liveGold >= cost) return true;
            if (cost > liveGold + 1) return false;
            return FindWeakestSellCandidateIndex(state) >= 0
                || HasEarlyTripleSellTarget(state);
        }

        private bool HasEarlyTripleSellTarget(GameState state)
        {
            if (state == null || state.Turn < 3 || state.Turn > 4
                || state.BoardMinions == null || state.BoardMinions.Count < 4
                || state.ShopMinions == null || state.ShopMinions.Count == 0)
                return false;
            var rules = state.EffectiveRules ?? EffectiveGameRules.Default;
            return state.ShopMinions.Any(shop =>
                shop != null && !shop.IsSpell
                    && TripleRuleEvaluator.CompletesGolden(
                        state, shop.CardId, rules))
                && state.BoardMinions.Any(m => m != null && !m.Golden);
        }

        private bool HasAffordableSellTarget(GameState state, VisualPlan plan)
        {
            if (state == null || plan == null) return false;
            if (plan.UpgradeHint != null
                && plan.UpgradeHint.Cost > 0
                && CanAffordAfterOneSell(state, plan.UpgradeHint.Cost))
                return true;
            if (state.ShopMinions == null || plan.ShopMarkers == null)
                return false;
            var rules = state.EffectiveRules ?? EffectiveGameRules.Default;
            foreach (var marker in plan.ShopMarkers)
            {
                if (marker == null) continue;
                if (marker.Level != DecisionLevel.Critical
                    && marker.Level != DecisionLevel.Major)
                    continue;
                MinionData card = null;
                if (marker.Index >= 0 && marker.Index < state.ShopMinions.Count)
                    card = state.ShopMinions[marker.Index];
                if (card == null && marker.EntityId > 0)
                    card = state.ShopMinions.FirstOrDefault(
                        m => m != null && m.EntityId == marker.EntityId);
                if (card == null) continue;
                int cost = GameRuleEvaluator.GetPurchaseCost(
                    state, card, state.HeroCardId, rules);
                if (CanAffordAfterOneSell(state, cost))
                    return true;
            }
            return false;
        }

        private int FindWeakestSellCandidateIndex(GameState state)
        {
            if (state == null || state.BoardMinions == null || state.BoardMinions.Count == 0)
                return -1;
            int bestIdx = -1;
            double bestScore = double.MaxValue;
            for (int i = 0; i < state.BoardMinions.Count; i++)
            {
                var m = state.BoardMinions[i];
                if (m == null || m.Golden) continue;
                if (state.RecentlyBoughtEntityIds != null
                    && state.RecentlyBoughtEntityIds.Contains(m.EntityId))
                    continue;
                if (IsVisualProtectedSellMinion(state, i, m)) continue;
                if (m.Tier >= 3 && (m.DivineShield || m.Poisonous || m.Venomous)) continue;
                if (m.Tier >= 4 && m.Reborn) continue;
                if (m.Taunt)
                {
                    int tauntCount = state.BoardMinions.Count(
                        other => other != null && other.Taunt);
                    if (tauntCount <= 1) continue;
                }
                if (IsUniqueTribeVisual(state, i)) continue;
                if (m.Tier >= 4 && (m.DivineShield || m.Poisonous || m.Venomous || m.Reborn))
                    continue;
                double score = m.Tier * 8.0 + m.Attack * 0.5 + m.Health * 0.3;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestIdx = i;
                }
            }
            return bestIdx;
        }

        private bool IsSellWorthwhileForVisual(
            GameState state,
            VisualPlan plan,
            List<ShopCardScore> cardScores,
            MinionData sold,
            int sellIndex)
        {
            if (state == null || plan == null || sold == null
                || sellIndex < 0 || sellIndex >= state.BoardMinions.Count)
                return false;
            if (sold.Golden) return false;
            if (IsVisualSelfReplacingEconomySell(state, sold)) return true;
            if (IsVisualEarlyTripleSell(state, sold)) return true;
            if (state.RecentlyBoughtEntityIds != null
                && state.RecentlyBoughtEntityIds.Contains(sold.EntityId))
                return false;
            if (IsVisualProtectedSellMinion(state, sellIndex, sold)) return false;
            // 2026-08-19 实测修复(B12): 对子保护——板面同名>=2(对子/三连机会)不推荐卖。
            // 卖对子去买低价值牌=破坏三连机会, 越算越亏(用户艾萨拉回合5反馈)
            if (state.BoardMinions != null)
            {
                int sameNameCount = 0;
                foreach (var m in state.BoardMinions)
                    if (m != null && m.CardId == sold.CardId && !m.Golden)
                        sameNameCount++;
                if (sameNameCount >= 2) return false;
            }

            int gold = state.LiveGold >= 0 ? state.LiveGold : state.Gold;
            if (state.BoardMinions.Count >= state.MaxBoardSlots)
            {
                var hand = state.HandMinions != null
                    ? state.HandMinions.FirstOrDefault(card => card != null && !card.IsSpell)
                    : null;
                if (hand != null && IsVisualHandReplacementBetter(state, sold, hand))
                    return true;
            }
            if (HasVisualShopTargetAfterSell(state, plan, cardScores, sold, gold))
                return true;

            if (plan.UpgradeHint != null
                && plan.UpgradeHint.Cost > 0
                && CanAffordAfterOneSell(state, plan.UpgradeHint.Cost)
                && state.BoardMinions.Count >= 3
                && IsVisualDeadWeight(state, sellIndex, sold))
                return true;
            return false;
        }

        private bool IsVisualEarlyTripleSell(
            GameState state, MinionData sold)
        {
            if (state == null || sold == null || state.Turn < 3 || state.Turn > 4
                || state.BoardMinions == null || state.BoardMinions.Count < 4
                || state.ShopMinions == null || state.ShopMinions.Count == 0)
                return false;
            if (sold.Golden || sold.Tier > Math.Max(1, state.TavernTier))
                return false;
            var rules = state.EffectiveRules ?? EffectiveGameRules.Default;
            bool tripleInShop = false;
            foreach (var shop in state.ShopMinions)
            {
                if (shop == null || shop.IsSpell) continue;
                if (!TripleRuleEvaluator.CompletesGolden(
                    state, shop.CardId, rules))
                    continue;
                tripleInShop = true;
                if (string.Equals(shop.CardId, sold.CardId,
                    StringComparison.Ordinal)
                    && TripleRuleEvaluator.CountOwnedCopies(
                        state, sold.CardId) >= 2)
                    return false;
            }
            if (!tripleInShop) return false;
            double soldPower = VisualPower(sold);
            double total = 0;
            foreach (var m in state.BoardMinions)
                if (m != null) total += VisualPower(m);
            double avg = state.BoardMinions.Count > 0
                ? total / state.BoardMinions.Count : 0;
            return avg > 0 && soldPower <= avg * 1.05;
        }

        private static bool IsVisualSelfReplacingEconomySell(
            GameState state, MinionData sold)
        {
            if (state == null || sold == null
                || (sold.CardId != "BG33_140" && sold.CardId != "BG33_140_G"))
                return false;
            int gold = state.LiveGold >= 0 ? state.LiveGold : state.Gold;
            var rules = state.EffectiveRules ?? EffectiveGameRules.Default;
            foreach (var shop in state.ShopMinions ?? new List<MinionData>())
            {
                if (shop == null || !shop.IsSpell) continue;
                int cost = GameRuleEvaluator.GetPurchaseCost(
                    state, shop, state.HeroCardId, rules);
                if (gold >= cost) continue;
                if (gold + 1 < cost) continue;
                if (shop.CardId == "BG28_810"
                    || (shop.CardName != null
                        && (shop.CardName.Contains("铸币")
                            || shop.CardName.Contains("金币")
                            || shop.CardName.Contains("币"))))
                    return true;
            }
            return false;
        }

        private bool IsVisualProtectedSellMinion(
            GameState state, int index, MinionData m)
        {
            if (state == null || m == null) return true;
            if (m.Golden || IsVisualKnownEngine(m.CardId)) return true;
            if (OpponentTechAdvisor.IsSituationalTechCard(m.CardId)
                && OpponentTechAdvisor.IsTechCardWorthKeeping(
                    m.CardId, state.Opponents))
                return true;
            if (m.Tier >= 4 && (m.DivineShield || m.Poisonous || m.Venomous || m.Reborn))
                return true;
            if (m.Tier >= 3 && (m.DivineShield || m.Poisonous || m.Venomous))
                return true;
            if (m.Taunt)
            {
                int tauntCount = state.BoardMinions.Count(
                    other => other != null && other.Taunt);
                if (tauntCount <= 1) return true;
            }
            return IsUniqueTribeVisual(state, index);
        }

        private bool IsVisualDeadWeight(
            GameState state, int index, MinionData sold)
        {
            if (state == null || state.BoardMinions == null
                || state.BoardMinions.Count < 3 || sold == null)
                return false;
            double soldPower = VisualPower(sold);
            double total = 0;
            foreach (var m in state.BoardMinions)
                if (m != null) total += VisualPower(m);
            double avg = total / state.BoardMinions.Count;
            if (soldPower <= 0 || avg <= 0) return false;
            return soldPower <= avg * 0.6
                && sold.Tier <= Math.Max(1, state.TavernTier - 1);
        }

        private bool IsVisualHandReplacementBetter(
            GameState state, MinionData sold, MinionData hand)
        {
            if (state == null || sold == null || hand == null) return false;
            if (hand.Golden) return true;
            double soldPower = VisualPower(sold);
            double handPower = VisualPower(hand);
            return handPower >= soldPower * 1.15;
        }

        private bool HasVisualShopTargetAfterSell(
            GameState state,
            VisualPlan plan,
            List<ShopCardScore> cardScores,
            MinionData sold,
            int gold)
        {
            if (state == null || plan == null || state.ShopMinions == null
                || sold == null)
                return false;
            var rules = state.EffectiveRules ?? EffectiveGameRules.Default;

            foreach (var marker in plan.ShopMarkers)
            {
                if (marker == null
                    || (marker.Level != DecisionLevel.Critical
                        && marker.Level != DecisionLevel.Major))
                    continue;
                var shop = ResolveShopMarkerCard(state, marker);
                if (shop == null) continue;
                int cost = GameRuleEvaluator.GetPurchaseCost(
                    state, shop, state.HeroCardId, rules);
                if (gold >= cost) continue;
                if (gold + 1 < cost) continue;
                if (IsVisualShopTargetBetter(state, marker, shop, sold, cardScores))
                    return true;
            }
            return false;
        }

        private bool IsVisualShopTargetBetter(
            GameState state,
            ShopMarker marker,
            MinionData shop,
            MinionData sold,
            List<ShopCardScore> cardScores)
        {
            if (state == null || marker == null || shop == null || sold == null)
                return false;
            if (marker.IsTriple) return true;
            var rules = state.EffectiveRules ?? EffectiveGameRules.Default;
            if (TripleRuleEvaluator.CompletesGolden(state, shop.CardId, rules))
                return true;

            if (OpponentTechAdvisor.IsSituationalTechCard(shop.CardId)
                && OpponentTechAdvisor.IsTechCardWorthKeeping(
                    shop.CardId, state.Opponents))
                return true;

            double soldPower = VisualPower(sold);
            double shopPower = VisualPower(shop);
            var score = FindShopScore(cardScores, marker);
            bool resourceOrCore = !shop.IsSpell && score != null
                && (score.Value.IsCoreCombo
                    || score.Value.EconomyValue >= 0.35f
                    || IsVisualResourceRole(score.Value.PrimaryRole));
            if (resourceOrCore)
            {
                if (shopPower >= soldPower * 1.05)
                    return true;
                double boardAvg = state.BoardMinions != null
                    && state.BoardMinions.Count > 0
                        ? state.BoardMinions
                            .Where(minion => minion != null)
                            .Sum(VisualPower)
                            / state.BoardMinions.Count
                        : 0;
                return soldPower <= boardAvg * 0.65;
            }
            double threshold = shop.Tier >= sold.Tier ? 1.20 : 1.50;
            return shopPower >= soldPower * threshold;
        }

        private MinionData ResolveShopMarkerCard(
            GameState state, ShopMarker marker)
        {
            if (state == null || marker == null) return null;
            if (marker.Index >= 0 && marker.Index < state.ShopMinions.Count)
                return state.ShopMinions[marker.Index];
            if (marker.EntityId > 0)
                return state.ShopMinions.FirstOrDefault(
                    card => card != null && card.EntityId == marker.EntityId);
            return null;
        }

        private static ShopCardScore? FindShopScore(
            List<ShopCardScore> cardScores, ShopMarker marker)
        {
            if (cardScores == null || marker == null) return null;
            foreach (var score in cardScores)
            {
                if (score.Index == marker.Index
                    || (marker.EntityId > 0 && score.EntityId == marker.EntityId))
                    return score;
            }
            return null;
        }

        private static bool IsVisualResourceRole(CardClassifier.CardRole role)
        {
            return role == CardClassifier.CardRole.Economy
                || role == CardClassifier.CardRole.ValueGenerator
                || role == CardClassifier.CardRole.SpellSource
                || role == CardClassifier.CardRole.SpellCopier;
        }

        private bool IsUniqueTribeVisual(GameState state, int index)
        {
            if (state == null || state.BoardMinions == null
                || index < 0 || index >= state.BoardMinions.Count)
                return false;
            var target = state.BoardMinions[index];
            if (target == null || string.IsNullOrEmpty(target.Tribe)) return false;
            foreach (var tribe in MinionData.GetTribesArray(target.Tribe))
            {
                int count = state.BoardMinions.Count(
                    other => other != null && other != target
                        && !string.IsNullOrEmpty(other.Tribe)
                        && MinionData.TribeMatches(other.Tribe, tribe));
                if (count == 0) return true;
            }
            return false;
        }

        private static double VisualPower(MinionData m)
        {
            if (m == null) return 0;
            return m.Attack * 0.6 + m.Health * 0.4 + m.Tier * 2.0;
        }

        private static bool IsVisualKnownEngine(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return false;
            return VisualKnownEngineCards.Contains(cardId);
        }

        private static readonly HashSet<string> VisualKnownEngineCards =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "BG_LOE_077",
                "BG26_ICC_901",
                "BG35_883",
                "BG33_825",
                "BGS_018",
                "BG_DAL_775",
                "BG35_155",
            };

        public VisualPlan CreateVisualPlan(
            GameState state,
            GameAction bestAction,
            LevelUpSuggestion? levelResult,
            List<ShopCardScore> cardScores,
            HeroPowerEngine heroPower = null,
            FeatureExtractor fe = null,
            CompGuidance compGuidance = null,
            GameAction secondStep = null,
            GameState secondStepState = null,
            List<DecisionEngine.TrinketScore> trinketScores = null)
        {
            _heroPower = heroPower;
            _fe = fe;
            var plan = new VisualPlan();
            plan.RecommendedActionType = bestAction != null ? bestAction.Type.ToString() : "";

            // ── 状态条 ──
            plan.Status = BuildStatus(state, bestAction, levelResult, compGuidance);

            // ── 升本宝石: LEVEL_UP/CAN_LEVEL/STABILIZE时显示, NO_GOLD/DANGER/MAX时隐藏 ──
            bool showLevelHint = levelResult.HasValue
                && levelResult.Value.Suggestion != "NO_GOLD"
                && levelResult.Value.Suggestion != "DANGER"
                && levelResult.Value.Suggestion != "MAX";
            if (showLevelHint)
            {
                bool urgent = levelResult.Value.Suggestion == "LEVEL_UP";
                plan.UpgradeHint = new UpgradeHint
                {
                    Level = urgent ? DecisionLevel.Critical : DecisionLevel.Major,
                    Reason = levelResult.Value.Reason,
                    Cost = GameRuleEvaluator.GetUpgradeCost(
                        state, state.EffectiveRules ?? EffectiveGameRules.Default) ?? -1,
                    CurrentTier = state.TavernTier,
                };
            }

            int maxRecos = Math.Max(2, state.ShopMinions.Count - 2);
            // ── 商店卡牌标记: 用途分类+质量评级+数量限制 ──
            if (cardScores != null && cardScores.Count > 0)
            {
                // 归一化
                double maxS = cardScores.Max(cs => cs.Score);
                double minS = cardScores.Min(cs => cs.Score);
                double range = maxS - minS;
                if (range < 0.001) range = 1;

                // 米尔豪斯(买怪2费)等经济英雄 → 可以多推荐1张
                if (_heroPower != null && !string.IsNullOrEmpty(state.HeroCardId))
                {
                    var hs = _heroPower.GetStrategyForPower(
                        state.HeroCardId, state.HeroPowerCardId);
                    if (hs.SpecialRule == "MILLHOUSE") maxRecos = Math.Min(maxRecos + 1, state.ShopMinions.Count);
                }
                if (HeroPowerRuleCatalog.IsChromieHero(state.HeroCardId))
                {
                    int spellShopCount = state.ShopMinions.Count(
                        m => m != null && m.IsSpell);
                    if (spellShopCount >= 3)
                        maxRecos = Math.Min(maxRecos + 2, state.ShopMinions.Count);
                }

                // 降低阈值确保有足够推荐: 后期放宽, 高本好牌不因归一化被埋没
                float threshold;
                if (state.Turn == 1) threshold = 0.06f;
                else if (state.Turn == 2) threshold = 0.10f;
                else if (state.Turn <= 5) threshold = 0.16f;
                else if (state.Turn <= 8) threshold = 0.12f;  // T6-T8: 核心牌开始出现
                else threshold = 0.08f;  // T9+: 高质量牌密集, 竭力避免漏推荐

                // 先收集所有通过阈值的标记, 计算质量等级用于排序
                var candidates = new List<ShopMarker>();
                foreach (var cs in cardScores)
                {
                    double normScore = (cs.Score - minS) / range;
                    if (normScore < threshold) continue;

                    DecisionLevel level;
                    if (normScore >= 0.65) level = DecisionLevel.Major;
                    else level = DecisionLevel.Minor;

                    // 用途分类
                    var purpose = ClassifyCardPurpose(cs);

                    // 质量评级
                    var quality = CardQuality.ComputeTier(normScore, cs.PickRate);

                    candidates.Add(new ShopMarker
                    {
                        Index = cs.Index,
                        ShopPosition = cs.ShopPosition,
                        EntityId = cs.EntityId,
                        Level = level,
                        Score = normScore,
                        CardName = cs.CardName,
                        Tribe = cs.Tribe,
                        Tier = cs.Tier,
                        Reason = cs.Reason ?? "",
                        IsSpell = cs.IsSpell,
                        IsTriple = cs.IsTriple,
                        Purpose = purpose,
                        Quality = quality,
                        PickRate = cs.PickRate,
                        Pulse = (quality == QualityTier.S),
                    });
                }

                // 按质量+评分排序: S>A>B, 同级别内按分数降序
                candidates.Sort((a, b) =>
                {
                    int qCmp = b.Quality.CompareTo(a.Quality);
                    if (qCmp != 0) return qCmp;
                    return b.Score.CompareTo(a.Score);
                });

                // 截断到推荐数量上限
                for (int i = 0; i < candidates.Count && i < maxRecos; i++)
                    plan.ShopMarkers.Add(candidates[i]);
            }

            // ── 兜底：无标记时强制标记最优卡, 全回合生效(不限制T8) ──
            if (plan.ShopMarkers.Count == 0 && cardScores != null && cardScores.Count > 0
                && state.ShopMinions.Count > 0)
            {
                var sorted = cardScores.OrderByDescending(cs => cs.Score).ToList();
                int fallbackCount = state.Turn <= 2 ? Math.Min(2, sorted.Count)
                    : state.Turn <= 5 ? Math.Min(2, sorted.Count)
                    : Math.Min(3, sorted.Count);  // T6+: 至少标记2-3张, 更多选择
                for (int i = 0; i < fallbackCount; i++)
                {
                    plan.ShopMarkers.Add(new ShopMarker
                    {
                        Index = sorted[i].Index,
                        ShopPosition = sorted[i].ShopPosition,
                        EntityId = sorted[i].EntityId,
                        Level = i == 0 ? DecisionLevel.Major : DecisionLevel.Minor,
                        Score = 0.85 - i * 0.15,
                        CardName = sorted[i].CardName,
                        Tribe = sorted[i].Tribe,
                        Tier = sorted[i].Tier,
                        Reason = sorted[i].Reason ?? "",
                        IsSpell = sorted[i].IsSpell,
                        IsTriple = sorted[i].IsTriple,
                        Purpose = ClassifyCardPurpose(sorted[i]),
                        Quality = i == 0 ? QualityTier.A : QualityTier.B,
                        PickRate = sorted[i].PickRate,
                    });
                }
            }

            PromoteBestShopAction(state, bestAction, cardScores, plan, maxRecos);

            // ── 卖牌标记: 仅在有明确升级场景时显示 (V1.1core: 匹配率从1.5%提升) ──
            if (bestAction != null && bestAction.Type == ActionType.SellMinion
                && bestAction.TargetIndex >= 0
                && bestAction.TargetIndex < state.BoardMinions.Count)
            {
                var targetMinion = state.BoardMinions[bestAction.TargetIndex];
                if (IsSellWorthwhileForVisual(
                    state, plan, cardScores, targetMinion, bestAction.TargetIndex))
                    plan.SellMarkers.Add(new SellMarker { BoardIndex = bestAction.TargetIndex });
            }

            // ── 发动标记：仅在明确 Activate 动作且目标随从仍存在时显示 ──
            if (bestAction != null && bestAction.Type == ActionType.Activate
                && bestAction.TargetIndex >= 0
                && bestAction.TargetIndex < state.BoardMinions.Count)
            {
                var targetMinion = state.BoardMinions[bestAction.TargetIndex];
                plan.ActivateHint = new ActivateHint
                {
                    BoardIndex = bestAction.TargetIndex,
                    CardId = targetMinion.CardId,
                    CardName = !string.IsNullOrEmpty(targetMinion.CardName)
                        ? targetMinion.CardName
                        : "unknown",
                    Cost = bestAction.Cost,
                    Reason = "发动",
                };
            }

            // I-20260907-23: Activate 目标在视觉构建时失效(引擎缓存帧 vs 当前板面竞态, 随从
            // 死亡/售出/买入致 TargetIndex 越界) → ActivateHint 未建。此时不得保留
            // RecommendedActionType=Activate: 否则 Validate 报 "activate action requires
            // activate hint"(122 处/09-06 晚至今) 且状态条退化为无目标裸"发动"(A2 反馈同族)。
            // 降级为空 → UI 不显示无意义的发动建议, 下一帧新评估会重建。
            if (plan.RecommendedActionType == "Activate" && plan.ActivateHint == null)
                plan.RecommendedActionType = "";

            // ── 手牌标记：流派匹配 + 打出售卖评估 ──
            EvaluateHandCards(state, bestAction, plan);

            // 兜底：无标记但手上有可提升战力的卡→提示打出
            if (plan.HandMarker == null && state.HandMinions.Count > 0
                && state.BoardMinions.Count > 0
                && state.BoardMinions.Count < state.MaxBoardSlots)
            {
                // 找板面最弱随从的战力 (attack+health, 简单评估)
                int weakestPower = int.MaxValue;
                foreach (var bm in state.BoardMinions)
                {
                    int pwr = bm.Attack + bm.Health + (bm.Golden ? 5 : 0)
                        + (bm.Taunt ? 3 : 0) + (bm.DivineShield ? 5 : 0);
                    if (pwr < weakestPower) weakestPower = pwr;
                }
                for (int i = 0; i < state.HandMinions.Count; i++)
                {
                    var hm = state.HandMinions[i];
                    if (hm.IsFrozen || hm.IsSpell) continue;
                    if (Engine.HandCombatCardPolicy.ShouldKeepInHand(hm.CardId)) continue;
                    if (state.Turn == 1 && IsNextTurnGoldBattlecryCard(hm.CardId)) continue;
                    int handPwr = hm.Attack + hm.Health + (hm.Golden ? 5 : 0)
                        + (hm.Taunt ? 3 : 0) + (hm.DivineShield ? 5 : 0);
                    // 手牌战力必须 > 板面最弱的1.1倍才推荐打出 (以强换弱)
                    if (handPwr > weakestPower * 1.1 || hm.Golden)
                    {
                        plan.HandMarker = new HandMarker
                        { Index = i, CardName = hm.CardName, Reason = hm.Golden ? "金卡" : "增强" };
                        break;
                    }
                }
            }

            // ── 目标提示: 任意手牌推荐卡都需要目标提示(法术/战吼/磁力等) ──
            MinionData bestHandCard = null;
            if (bestAction != null
                && (bestAction.Type == ActionType.PlayMinion
                    || bestAction.Type == ActionType.UseSpell)
                && bestAction.TargetIndex >= 0
                && bestAction.TargetIndex < state.HandMinions.Count)
            {
                bestHandCard = state.HandMinions[bestAction.TargetIndex];
            }
            bool isMethodicalMadness = bestHandCard != null
                && bestHandCard.CardId == "BG36_880";

            bool handIsTargeted = plan.HandMarker != null &&
                plan.HandMarker.Index < state.HandMinions.Count &&
                IsTargetedCard(state.HandMinions[plan.HandMarker.Index]);
            bool isMagnetic = handIsTargeted &&
                CardNameService.GetName(state.HandMinions[plan.HandMarker.Index].CardId).Contains("磁力");
            bool heroTargeted = bestAction != null && bestAction.Type == ActionType.UseHeroPower
                && bestAction.RequiresTarget;
            if (handIsTargeted || heroTargeted)
            {
                if (isMethodicalMadness)
                {
                    int tgtIdx = -1;
                    double tgtScore = double.MinValue;
                    for (int bi = 0; bi < state.BoardMinions.Count; bi++)
                    {
                        var bm = state.BoardMinions[bi];
                        if (bm == null || string.IsNullOrEmpty(bm.Tribe)
                            || !MinionData.TribeMatches(bm.Tribe, "恶魔"))
                            continue;
                        double sc = bm.Attack * 1.5 + bm.Health * 0.5
                            + bm.Tier * 3.0;
                        if (!string.IsNullOrEmpty(plan.Status.CompDir)
                            && MinionData.TribeMatches(
                                bm.Tribe, plan.Status.CompDir))
                            sc *= 1.5;
                        if (bm.Golden) sc -= 2.0;
                        if (sc > tgtScore)
                        {
                            tgtScore = sc;
                            tgtIdx = bi;
                        }
                    }
                    if (tgtIdx >= 0)
                    {
                        plan.TargetHints.Add(new TargetHint
                        {
                            BoardIndex = tgtIdx,
                            Reason = "目标恶魔",
                            Level = DecisionLevel.Major,
                        });
                    }
                }
                else if (isMagnetic)
                {
                    // 磁力: 所有场上非金机械都是合法目标
                    for (int bi = 0; bi < state.BoardMinions.Count; bi++)
                    {
                        var bm = state.BoardMinions[bi];
                        if (bm.Golden) continue;
                        if (string.IsNullOrEmpty(bm.Tribe) || !MinionData.TribeMatches(bm.Tribe, "机械")) continue;
                        plan.TargetHints.Add(new TargetHint
                        {
                            BoardIndex = bi,
                            Reason = "贴磁力",
                            Level = DecisionLevel.Major,
                        });
                    }
                }
                else
                {
                    // 非法术/非法人: 找场上最适合buff的随从: 流派匹配+非金+高攻>高血
                    int tgtIdx = -1; double tgtScore = -1;
                    for (int bi = 0; bi < state.BoardMinions.Count; bi++)
                    {
                        var bm = state.BoardMinions[bi];
                        if (bm.Golden) continue;
                        double sc = (bm.Attack * 1.5 + bm.Health * 0.5 + bm.Tier * 3);
                        if (!string.IsNullOrEmpty(bm.Tribe) && MinionData.TribeMatches(bm.Tribe, plan.Status.CompDir)) sc *= 1.5;
                        if (sc > tgtScore) { tgtScore = sc; tgtIdx = bi; }
                    }
                    if (tgtIdx >= 0)
                    {
                        plan.TargetHints.Add(new TargetHint
                        {
                            BoardIndex = tgtIdx,
                            Reason = "目标",
                            Level = DecisionLevel.Major,
                        });
                    }
                }
            }

            // ── 行4 饰品状态栏摘要移至方法末尾(TrinketHints 填充之后)计算, 修 PickLine 读空列表死代码 ──

            // ── 行3: 动作序列 (浅黄) ──
            {
                var parts = new List<string>();
                if (bestAction != null)
                {
                    switch (bestAction.Type)
                    {
                        case ActionType.BuyMinion:
                            if (bestAction.TargetIndex < state.ShopMinions.Count)
                                parts.Add("买→" + CardNameService.GetName(
                                    state.ShopMinions[bestAction.TargetIndex].CardId));
                            else parts.Add("买");
                            break;
                        case ActionType.Upgrade: parts.Add("升本→" + (state.TavernTier + 1) + "本"); break;
                        // 2026-08-19 实测反馈: 刷新后面不要跟随从名(刷新后商店内容会变,
                        // 指向当前商店随从无意义) → 显示"刷新酒馆"
                        case ActionType.Refresh: parts.Add("刷新酒馆"); break;
                        case ActionType.FreezeShop: parts.Add("冻结"); break;
                        case ActionType.BuySpell:
                            if (bestAction.TargetIndex < state.ShopMinions.Count)
                            {
                                var spell = state.ShopMinions[bestAction.TargetIndex];
                                string spellName = !string.IsNullOrEmpty(spell.CardName)
                                    ? spell.CardName
                                    : CardNameService.GetName(spell.CardId);
                                parts.Add("买→" + spellName);
                            }
                            else parts.Add("买法术");
                            break;
                        case ActionType.SellMinion:
                            if (bestAction.TargetIndex >= 0 && bestAction.TargetIndex < state.BoardMinions.Count)
                                parts.Add("卖→" + CardNameService.GetName(
                                    state.BoardMinions[bestAction.TargetIndex].CardId));
                            else parts.Add("卖");
                            break;
                        // 2026-08-19 A2: 发动机制提示——发动类动作无文字提示,
                        // UI 只有高亮没有说明, 玩家不知道要做什么(用户实测反馈)
                        case ActionType.Activate:
                            if (bestAction.TargetIndex >= 0 && bestAction.TargetIndex < state.BoardMinions.Count)
                                parts.Add("发动→" + CardNameService.GetName(
                                    state.BoardMinions[bestAction.TargetIndex].CardId));
                            else parts.Add("发动");
                            break;
                    }
                }
                bool handActionAdded = false;
                if (plan.HandMarker != null && plan.HandMarker.Index < state.HandMinions.Count)
                {
                    var hName = CardNameService.GetName(
                        state.HandMinions[plan.HandMarker.Index].CardId);
                    bool handIsSpell = state.HandMinions[plan.HandMarker.Index].IsSpell;
                    string handPrefix = handIsSpell ? "使用→" : "打→";
                    if (bestAction != null
                        && bestAction.TargetIndex == plan.HandMarker.Index
                        && bestAction.Type == ActionType.UseSpell)
                        handPrefix = "使用→";
                    if (parts.Count == 0 || !parts[parts.Count - 1].EndsWith(hName))
                        parts.Add(handPrefix + hName);
                    if (bestAction != null
                        && bestAction.TargetIndex == plan.HandMarker.Index)
                        handActionAdded = true;
                }
                // 第一步: 英雄技能
                if (bestAction != null && bestAction.Type == ActionType.UseHeroPower)
                {
                    if (bestAction.RequiresTarget
                        && plan.TargetHints.Count > 0
                        && plan.TargetHints[0].BoardIndex < state.BoardMinions.Count)
                        parts.Add("技→" + CardNameService.GetName(
                            state.BoardMinions[plan.TargetHints[0].BoardIndex].CardId));
                    else parts.Add("技");
                }
                // 第二步(二步前瞻): 买→技 / 买→买等, 带目标对象
                // 2026-08-19 实测反馈: 第一步为"刷新酒馆"时抑制二步随从指向——
                // 刷新后商店内容变化, 指向当前商店的随从/法术可能不存在
                // 1.2.2 T1反馈: 动态铸币复核 — 第一步买完后预算不足时不显示需花钱的第二步,
                // 避免状态条 "买A\n买B" 两行误导; 免费打出动作在第一步为购买时也不显示。
                bool step1Bought = bestAction != null
                    && (bestAction.Type == ActionType.BuyMinion || bestAction.Type == ActionType.BuySpell);
                bool step2NeedsGold = secondStep != null
                    && (secondStep.Type == ActionType.BuyMinion
                        || secondStep.Type == ActionType.BuySpell
                        || secondStep.Type == ActionType.Upgrade
                        || secondStep.Type == ActionType.Refresh
                        || secondStep.Type == ActionType.SellMinion);
                bool noBudgetForStep2 = step1Bought && secondStepState != null
                    && (secondStepState.Gold < 3 || step2NeedsGold);
                bool freeStep2AfterBuy = step1Bought && secondStep != null
                    && (secondStep.Type == ActionType.PlayMinion || secondStep.Type == ActionType.UseSpell);
                if (secondStep != null
                    && !(bestAction != null && bestAction.Type == ActionType.Refresh)
                    && !noBudgetForStep2
                    && !freeStep2AfterBuy)
                {
                    switch (secondStep.Type)
                    {
                        case ActionType.UseHeroPower:
                            if (secondStep.RequiresTarget
                                && secondStepState != null && secondStep.TargetIndex >= 0
                                && secondStep.TargetIndex < secondStepState.BoardMinions.Count)
                                parts.Add("技→" + CardNameService.GetName(
                                    secondStepState.BoardMinions[secondStep.TargetIndex].CardId));
                            else
                                parts.Add("技");
                            break;
                        case ActionType.BuyMinion:
                            if (secondStepState != null && secondStep.TargetIndex >= 0
                                && secondStep.TargetIndex < secondStepState.ShopMinions.Count)
                                parts.Add("买→" + CardNameService.GetName(
                                    secondStepState.ShopMinions[secondStep.TargetIndex].CardId));
                            else
                                parts.Add("再买");
                            break;
                        case ActionType.BuySpell:
                            if (secondStepState != null && secondStep.TargetIndex >= 0
                                && secondStep.TargetIndex < secondStepState.ShopMinions.Count)
                            {
                                var spell = secondStepState.ShopMinions[secondStep.TargetIndex];
                                string spellName = !string.IsNullOrEmpty(spell.CardName)
                                    ? spell.CardName
                                    : CardNameService.GetName(spell.CardId);
                                parts.Add("买→" + spellName);
                            }
                            else
                                parts.Add("再买法术");
                            break;
                        case ActionType.SellMinion:
                            if (secondStepState != null && secondStep.TargetIndex >= 0
                                && secondStep.TargetIndex < secondStepState.BoardMinions.Count)
                                parts.Add("卖→" + CardNameService.GetName(
                                    secondStepState.BoardMinions[secondStep.TargetIndex].CardId));
                            else
                                parts.Add("卖");
                            break;
                        case ActionType.Refresh:
                            parts.Add("刷新"); break;
                        case ActionType.Upgrade:
                            parts.Add("升"); break;
                        // 2026-08-19 A2: 二步发动提示
                        case ActionType.Activate:
                            if (secondStepState != null && secondStep.TargetIndex >= 0
                                && secondStep.TargetIndex < secondStepState.BoardMinions.Count)
                                parts.Add("发动→" + CardNameService.GetName(
                                    secondStepState.BoardMinions[secondStep.TargetIndex].CardId));
                            else
                                parts.Add("发动");
                            break;
                    }
                }
                if (bestAction != null
                    && (bestAction.Type == ActionType.PlayMinion || bestAction.Type == ActionType.UseSpell))
                {
                    string handName = bestAction.TargetIndex >= 0
                        && bestAction.TargetIndex < state.HandMinions.Count
                        ? CardNameService.GetName(state.HandMinions[bestAction.TargetIndex].CardId)
                        : "";
                    if (!handActionAdded)
                    {
                        string prefix = bestAction.Type == ActionType.UseSpell
                            ? "使用→" : "打出→";
                        string suffix = "";
                        if (bestAction.Type == ActionType.UseSpell
                            && bestAction.TargetIndex >= 0
                            && bestAction.TargetIndex < state.HandMinions.Count
                            && IsTargetedCard(state.HandMinions[bestAction.TargetIndex])
                            && plan.TargetHints.Count > 0
                            && plan.TargetHints[0].BoardIndex >= 0
                            && plan.TargetHints[0].BoardIndex < state.BoardMinions.Count)
                        {
                            suffix = "→" + CardNameService.GetName(
                                state.BoardMinions[plan.TargetHints[0].BoardIndex].CardId);
                        }
                        else if (bestAction.Type == ActionType.UseSpell
                            && IsTargetedCard(state.HandMinions[bestAction.TargetIndex]))
                        {
                            // 2026-08-19 实测修复(A3): 指向性法术但无可用目标——
                            // 不显示误导的"直接使用", 标注缺目标(该法术当前不可用)
                            suffix = "→缺目标";
                        }
                        else if (bestAction.Type == ActionType.UseSpell)
                        {
                            suffix = "→直接使用";
                        }
                        parts.Add(prefix + handName + suffix);
                    }
                    else if (bestAction.Type == ActionType.UseSpell
                        && plan.TargetHints.Count > 0
                        && plan.TargetHints[0].BoardIndex >= 0
                        && plan.TargetHints[0].BoardIndex < state.BoardMinions.Count
                        && parts.Count > 0)
                    {
                        string target = CardNameService.GetName(
                            state.BoardMinions[plan.TargetHints[0].BoardIndex].CardId);
                        parts[parts.Count - 1] += "→" + target;
                    }
                    else if (bestAction.Type == ActionType.UseSpell
                        && parts.Count > 0)
                    {
                        parts[parts.Count - 1] += "→直接使用";
                    }
                }
                if (parts.Count > 0)
                {
                    // 每个动作-目标对独立成行, 完整保留卡牌名不截断
                    plan.Status.HintLine = string.Join("\n", parts);
                    // 诊断: 检测裸 cardId 泄露到状态栏(GetName 三级缓存全 miss 兜底返回了 cardId)。
                    // 同一卡只记一次(_reportedRawIds 去重), 避免 HintLine 每帧重算导致同步 IO 拖累 UI 线程。
                    foreach (var p in parts)
                        if (LooksLikeRawCardId(p) && _reportedRawIds.Add(p))
                            VisualizerLog("DIAG CardNameMiss: status leaked rawId in '" + p + "'");
                }
            }

            // ── 冻结提示: 只保留可由当前局面确认的三连缺钱条件 ──
            // 禁止以"高本好牌/高星级/好牌缺钱"为由冻结(规格明令)。T1-T3 不显示冻结提示。
            int liveGoldForFreeze = state.LiveGold >= 0 ? state.LiveGold : state.Gold;
            bool eudoraEarly = HeroPowerRuleCatalog.IsEudoraHero(state.HeroCardId)
                && state.Turn <= 6 && liveGoldForFreeze < 3;
            if (!state.FrozenShop && state.ShopMinions.Count > 0
                && (state.Turn > 3 || eudoraEarly
                    || (state.FreeRefreshCount > 0 && liveGoldForFreeze < 3
                        && cardScores != null && cardScores.Any(cs =>
                            cs.IsTriple || cs.IsCoreCombo
                            || cs.EconomyValue >= 0.35f
                            || (!cs.IsSpell && cs.Tier >= 2)))))
            {
                int tripleCnt = cardScores != null ? cardScores.Count(cs => cs.IsTriple) : 0;
                bool shortOnGold = liveGoldForFreeze < 3;
                bool preserveFreeRefresh = state.FreeRefreshCount > 0 && shortOnGold
                    && cardScores != null && cardScores.Any(cs =>
                        cs.IsTriple || cs.IsCoreCombo
                        || cs.EconomyValue >= 0.35f
                        || (!cs.IsSpell && cs.Tier >= 2));

                if (eudoraEarly)
                {
                    bool hasWorthFreeze = tripleCnt >= 1
                        || (cardScores != null && cardScores.Any(cs =>
                            cs.IsTriple || cs.IsCoreCombo
                            || cs.EconomyValue >= 0.35f
                            || (!cs.IsSpell && cs.Tier >= 2)));
                    if (hasWorthFreeze)
                    {
                        plan.FreezeHint = new FreezeHint
                        {
                            Active = true,
                            Urgent = false,
                            Reason = "留1金挖宝",
                        };
                    }
                }
                else if (preserveFreeRefresh)
                {
                    plan.FreezeHint = new FreezeHint
                    {
                        Active = true,
                        Urgent = false,
                        Reason = "留免费刷新",
                    };
                }
                else if (tripleCnt >= 1 && shortOnGold)
                {
                    plan.FreezeHint = new FreezeHint
                    {
                        Active = true,
                        Urgent = false,
                        Reason = "三连缺钱",
                    };
                }

            }

            // ── 饰品推荐提示 (S13: 优先使用DecisionEngine结构化评分) ──
            bool pendingTrinketOnly = state.TrinketOffer != null
                && state.TrinketOffer.Count > 0
                && state.TrinketOffer.All(t => t != null
                    && !string.IsNullOrEmpty(t.CardId)
                    && t.CardId.StartsWith("__TRINKET_PENDING", StringComparison.Ordinal));
            if (state.TrinketOffer != null && state.TrinketOffer.Count > 0 && !pendingTrinketOnly)
            {
                // 只消费本机事实与原创规则的统一评分入口。
                if (trinketScores != null && trinketScores.Count > 0)
                {
                    for (int tsi = 0; tsi < Math.Min(4, trinketScores.Count); tsi++)
                    {
                        var ts = trinketScores[tsi];
                        string reason = TrinketReasonFormatter.Format(
                            ts.IsUnrated, ts.MatchedRuleIds);
                        plan.TrinketHints.Add(new TrinketHint
                        {
                            Index = ts.Index,
                            Name = ts.Name,
                            Score = ts.Score,
                            Reason = reason,
                            IsTopPick = (tsi == 0 && !ts.IsUnrated),
                            IsUnrated = ts.IsUnrated,
                        });
                    }
                }
            }

            // ── 行4: 饰品状态栏摘要(金色) — 必须在 TrinketHints 填充之后计算(修 PickLine 死代码) ──
            if (plan.TrinketHints.Count > 0)
            {
                var topHint = plan.TrinketHints[0];
                plan.Status.PickLine = "饰品: " + topHint.Name
                    + (topHint.IsUnrated ? "（未知）" : (topHint.IsTopPick ? " ★" : ""));
            }
            else { plan.Status.PickLine = ""; }

            return plan;
        }

        private static void PromoteBestShopAction(
            GameState state,
            GameAction bestAction,
            List<ShopCardScore> cardScores,
            VisualPlan plan,
            int maxRecos)
        {
            if (state == null || plan == null || bestAction == null
                || state.ShopMinions == null
                || bestAction.TargetIndex < 0
                || bestAction.TargetIndex >= state.ShopMinions.Count)
                return;
            if (bestAction.Type != ActionType.BuyMinion
                && bestAction.Type != ActionType.BuySpell)
                return;

            var shop = state.ShopMinions[bestAction.TargetIndex];
            if (shop == null) return;

            ShopCardScore selected = default(ShopCardScore);
            bool found = false;
            if (cardScores != null)
            {
                foreach (var score in cardScores)
                {
                    if (score.Index != bestAction.TargetIndex
                        && (shop.EntityId <= 0 || score.EntityId != shop.EntityId))
                        continue;
                    selected = score;
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                selected = new ShopCardScore
                {
                    Index = bestAction.TargetIndex,
                    ShopPosition = shop.Position,
                    EntityId = shop.EntityId,
                    Score = 1.0,
                    CardName = shop.CardName,
                    Tribe = shop.Tribe,
                    Tier = shop.Tier,
                    IsSpell = shop.IsSpell,
                };
            }

            var existing = plan.ShopMarkers.FirstOrDefault(marker =>
                marker != null
                && (marker.Index == selected.Index
                    || (selected.EntityId > 0 && marker.EntityId == selected.EntityId)));
            if (existing != null)
                plan.ShopMarkers.Remove(existing);

            plan.ShopMarkers.Insert(0, new ShopMarker
            {
                Index = selected.Index,
                ShopPosition = selected.ShopPosition,
                EntityId = selected.EntityId,
                Level = DecisionLevel.Major,
                Score = existing != null ? existing.Score : 0.85,
                CardName = selected.CardName,
                Tribe = selected.Tribe,
                Tier = selected.Tier,
                Reason = selected.Reason ?? "推荐动作",
                IsSpell = selected.IsSpell,
                IsTriple = selected.IsTriple,
                Purpose = ClassifyCardPurpose(selected),
                Quality = existing != null
                    ? existing.Quality : QualityTier.A,
                PickRate = selected.PickRate,
                Pulse = true,
            });

            while (plan.ShopMarkers.Count > maxRecos)
                plan.ShopMarkers.RemoveAt(plan.ShopMarkers.Count - 1);
        }

        private static CardPurpose ClassifyCardPurpose(ShopCardScore score)
        {
            var classification = ToClassification(score);
            return CardQuality.ClassifyPurpose(
                classification, score.IsSpell,
                score.CardName, score.Tribe, score.Tier);
        }

        private static CardClassifier.CardClassification? ToClassification(
            ShopCardScore score)
        {
            if (!score.HasClassification) return null;
            return new CardClassifier.CardClassification
            {
                PrimaryRole = score.PrimaryRole,
                EconomyValue = score.EconomyValue,
                CombatValue = score.CombatValue,
                GrowthValue = score.GrowthValue,
                IsCoreCombo = score.IsCoreCombo,
            };
        }

        /// <summary>简化版（仅状态条，无决策）</summary>
        public VisualPlan CreateWaitingPlan(GameState state)
        {
            return new VisualPlan
            {
                Status = BuildStatus(state, null, null),
            };
        }

        // ── 内部方法 ──

        /// <summary>评估手牌：标记打出售卖/保留</summary>
        private void EvaluateHandCards(GameState state, GameAction bestAction, VisualPlan plan)
        {
            if (state.HandMinions.Count == 0) return;

            string compDir = plan.Status.CompDir;
            bool boardFull = state.BoardMinions.Count >= state.MaxBoardSlots;
            bool early = state.Turn <= 3;
            int bestIdx = -1; string bestReason = ""; double bestPri = -1;

            // 对子检测: 统计场上+手牌每种CardId的出现次数
            var cardCounts = new Dictionary<string, int>();
            foreach (var bm in state.BoardMinions)
                if (!string.IsNullOrEmpty(bm.CardId))
                    cardCounts[bm.CardId] = cardCounts.ContainsKey(bm.CardId) ? cardCounts[bm.CardId] + 1 : 1;
            foreach (var hm in state.HandMinions)
                if (!string.IsNullOrEmpty(hm.CardId))
                    cardCounts[hm.CardId] = cardCounts.ContainsKey(hm.CardId) ? cardCounts[hm.CardId] + 1 : 1;

            for (int i = 0; i < state.HandMinions.Count; i++)
            {
                var hm = state.HandMinions[i];
                if (hm.IsFrozen) continue;
                if (Engine.HandCombatCardPolicy.ShouldKeepInHand(hm.CardId))
                    continue;
                // 下回合铸币战吼（南海卖艺者等）T1只拿不直接打，T2打出让T3有6币双买。
                if (state.Turn == 1 && IsNextTurnGoldBattlecryCard(hm.CardId))
                    continue;
                double pri = 0;
                string reason = "";

                bool matchTribe = !string.IsNullOrEmpty(compDir)
                    && !string.IsNullOrEmpty(hm.Tribe) && MinionData.TribeMatches(hm.Tribe, compDir);

                // 对子/三连检测: 场上+手牌已有≥2张同CardId → 保留凑三连，不建议打出
                int totalCopies;
                cardCounts.TryGetValue(hm.CardId, out totalCopies);
                bool isPair = totalCopies >= 2;

                // 优先级: Combo就绪 > 流派匹配有空间 > 法术(非对子) > 金卡战力 > 战吼 > 过渡
                if (hm.IsSpell && IsComboSpell(hm.CardId) && BoardHasCardId(state, ComboPieceFor(hm.CardId)))
                { pri = 100; reason = "Combo"; }
                else if (matchTribe && !boardFull && !hm.IsSpell)
                { pri = 80; reason = TribeCn(compDir); }
                else if (hm.IsSpell && hm.Tier >= 2 && !isPair)
                { pri = 60; reason = "法术"; }
                else if (hm.Golden && !boardFull)
                { pri = 50; reason = "金卡"; }
                else if (!hm.IsSpell && IsBattlecryCard(hm, _fe) && !boardFull)
                { pri = 40; reason = "战吼"; }
                else if (isPair && !hm.IsSpell && !boardFull)
                { pri = 35; reason = "对子(保留)"; }
                else if (!matchTribe && !hm.IsSpell && !hm.Golden && !boardFull && hm.Tier >= 3)
                { pri = 20; reason = "过渡"; }
                else if (early && !boardFull && !hm.IsSpell)
                { pri = 25; reason = "早期"; }
                else if (!hm.IsSpell && !boardFull)
                { pri = 10; reason = "打出"; }
                // 法术后补: 对子状态下的法术降为低优先(保留)
                else if (hm.IsSpell && isPair)
                { pri = 5; reason = "对子保留"; }

                if (pri > bestPri) { bestPri = pri; bestIdx = i; bestReason = reason; }
            }

            if (bestIdx >= 0 && bestPri > 0)
            {
                plan.HandMarker = new HandMarker
                {
                    Index = bestIdx,
                    CardName = state.HandMinions[bestIdx].CardName,
                    Reason = bestReason,
                };
            }
        }

        private static bool IsNextTurnGoldBattlecryCard(string cardId)
        {
            return cardId == "BG26_135"
                || cardId == "BG26_135_G"
                || cardId == "BG34_Giant_001"
                || cardId == "BG34_Giant_001_G";
        }

        private StatusInfo BuildStatus(GameState state, GameAction bestAction, LevelUpSuggestion? levelResult, CompGuidance guidance = null)
        {
            float hpRatio = state.MaxHealth > 0 ? (float)state.Health / state.MaxHealth : 1f;
            int? resolvedUpCost = GameRuleEvaluator.GetUpgradeCost(
                state, state.EffectiveRules ?? EffectiveGameRules.Default);
            int upCost = resolvedUpCost ?? int.MaxValue;
            // 欧穆: 升本后得2币 → 有效门槛低2
            int effectiveUpCost = upCost;
            if (_heroPower != null)
            {
                var hs = _heroPower.GetStrategyForPower(
                    state.HeroCardId, state.HeroPowerCardId);
                if (hs.SpecialRule == "OMU") effectiveUpCost = Math.Max(0, upCost - 2);
            }
            int maxTavernTier = state.EffectiveRules != null
                ? state.EffectiveRules.MaxTavernTier : 6;
            bool canLevel = resolvedUpCost.HasValue && state.Gold >= effectiveUpCost
                && state.TavernTier < maxTavernTier && state.Turn >= 2;
            // Pace: T1固定稳, T2+根据血量和升本能力 — 能升本+血多=冲, 能升本+血少=赌, 不能升=稳, 血极低=守
            string pace;
            if (hpRatio >= 0.70f && canLevel) pace = "Sprint";
            else if (hpRatio >= 0.50f && canLevel) pace = "Cruise";
            else if (hpRatio < 0.25f) pace = "Conserve";
            else if (hpRatio < 0.40f && canLevel) pace = "AllIn";
            else pace = "Cruise";
            // 如果本回合刚升完本 → 设为 Sprint (升本是最激进的操作)
            bool justLeveled = state.Turn > 1 && state.LastUpgradeTurn == state.Turn;
            if (justLeveled && pace != "Sprint") pace = "Sprint";
            // Phase: 直接用回合数
            string phase = state.Turn.ToString();

            // 可用种族过滤 (仅当检测到>=3个种族时生效)
            bool hasAvailableTribes = state.AvailableTribes != null && state.AvailableTribes.Count >= 3;

            string compDir = "";
            var tribeCount = new Dictionary<string, int>();
            foreach (var m in state.BoardMinions)
            {
                if (!string.IsNullOrEmpty(m.Tribe))
                {
                    // 多部落支持: 每个部落独立计数, 有可用种族时过滤
                    bool anyMatch = false;
                    foreach (var t in MinionData.GetTribesArray(m.Tribe))
                    {
                        if (hasAvailableTribes && !state.AvailableTribes.Contains(t)) continue;
                        anyMatch = true;
                        int c; tribeCount.TryGetValue(t, out c);
                        tribeCount[t] = c + 1;
                    }
                    if (!anyMatch && hasAvailableTribes) continue;
                }
            }
            int maxT = 0; string maxTribe = "";
            foreach (var kv in tribeCount)
                if (kv.Value > maxT) { maxT = kv.Value; maxTribe = kv.Key; }

            // 流派滞后锁: 锁定后2-3回合内不随意切换(除非原方向崩盘)
            // P1修复: 缩短锁定期, 新方向更容易切换
            if (!string.IsNullOrEmpty(_lockedComp))
            {
                int lockedCount = tribeCount.ContainsKey(_lockedComp) ? tribeCount[_lockedComp] : 0;
                // 原方向仍有核心(≥2随从)且未显著落后 → 保持
                if (lockedCount >= 2 && lockedCount >= maxT - 1)
                {
                    compDir = _lockedComp;
                    _compLockedFor++;
                }
                // 新方向≥3随从且领先≥1即可切换 (原阈值≥4+领先≥2过于保守)
                else if (maxT >= lockedCount + 1 && maxT >= 3)
                {
                    compDir = maxTribe;
                    _lockedComp = maxTribe;
                    _compLockTurn = state.Turn;
                    _compLockedFor = 1;
                }
                // 锁定超过4回合且原方向不占优 → 强制解锁
                else if (_compLockedFor >= 4 && lockedCount < maxT)
                {
                    compDir = maxTribe;
                    _lockedComp = maxTribe;
                    _compLockTurn = state.Turn;
                    _compLockedFor = 1;
                }
                else if (lockedCount == 0)
                {
                    // 原方向完全瓦解
                    _lockedComp = null;
                    _compLockTurn = 0;
                    _compLockedFor = 0;
                    if (maxT >= 2) { compDir = maxTribe; _lockedComp = maxTribe; _compLockTurn = state.Turn; _compLockedFor = 1; }
                }
                else
                {
                    // 过渡状态: 仍显示原方向
                    compDir = _lockedComp;
                    _compLockedFor++;
                }
            }
            else
            {
                // 未锁定: 首次锁定需要同族≥2 (从≥3降低)
                if (maxT >= 2) { compDir = maxTribe; _lockedComp = maxTribe; _compLockTurn = state.Turn; _compLockedFor = 1; }
                else compDir = "";
            }

            // Early-game fallback: show hero affinity direction
            if (string.IsNullOrEmpty(compDir) && state.Turn <= 5
                && !string.IsNullOrEmpty(state.HeroCardId) && _heroPower != null)
            {
                string heroDir = GetHeroAffinityDirection(state);
                if (!string.IsNullOrEmpty(heroDir))
                    compDir = TribeCn(heroDir) + "?";
            }

            // Translate compDir to Chinese for display
            if (!string.IsNullOrEmpty(compDir) && !compDir.EndsWith("?"))
                compDir = TribeCn(compDir);

            // 诊断: 流派方向判定(0611 06111357 问题D: 场上中立却提示龙族)。
            // 打印 board各tribe计数 + 锁定流派 + 最终compDir, 下局定位是Tribe误判还是滞后锁卡住。
            // 0611 06110521改进: 只在board非空时计数(避免开局空board帧耗尽配额, 漏掉中后期决策帧),
            // 且compDir非空(真有流派推荐)时强制输出, 确保覆盖"5元素却推野兽"那一刻。
            bool boardNonEmpty = state.BoardMinions != null && state.BoardMinions.Count > 0;
            bool hasCompRec = !string.IsNullOrEmpty(compDir);
            if (boardNonEmpty && (hasCompRec || _compDirDiagCount < 30))
            {
                var tcStr = string.Join(",", tribeCount.Select(kv => kv.Key + ":" + kv.Value));
                VisualizerLog(string.Format("DIAG CompDir: turn={0} board={1} tribeCounts=[{2}] maxTribe={3}({4}) locked={5} lockTurn={6} → compDir={7}",
                    state.Turn, state.BoardMinions.Count,
                    tcStr, maxTribe, maxT, _lockedComp ?? "null", _compLockTurn, compDir));
                _compDirDiagCount++;
            }

            // 检测绝望战力鸿沟（需要tech pivot）
            // 2026-08-19 实测修复(B7): ①oppAvg 原为 oppStats/oppCount*7(对手存活少时
            // 放大2-3倍, 高估对手战力——米罗克9.8k圣盾被误判"战力不足"的根因);
            // 改为对手平均战力(去掉*7); ②myStats 加圣盾/烈毒交换价值
            bool isDesperate = false;
            if (state.Turn >= 8 && state.Health > 15)
            {
                float myStats = 0f;
                if (state.BoardMinions != null)
                    foreach (var m in state.BoardMinions)
                    {
                        myStats += m.Attack * 0.7f + m.Health * 0.3f;
                        if (m.DivineShield) myStats += 1.5f;
                        if (m.Venomous) myStats += 1.5f;
                    }
                float oppStats = 0f; int oppCount = 0;
                if (state.Opponents != null)
                    foreach (var o in state.Opponents)
                    {
                        if (!o.Alive) continue;
                        foreach (var m in o.BoardMinions)
                        { oppStats += m.Attack * 0.7f + m.Health * 0.3f; oppCount++; }
                    }
                float oppAvg = oppCount > 0 ? oppStats / oppCount : 0f;
                if (oppAvg > 0 && myStats < oppAvg * 0.15f)
                    isDesperate = true;
            }

            string lockIcon = "";
            if (guidance != null)
                lockIcon = guidance.LockIcon;

            return new StatusInfo
            {
                Health = state.Health,
                MaxHealth = state.MaxHealth,
                IsDesperate = isDesperate,
                Gold = state.Gold,
                MaxGold = state.MaxGold,
                ShowLevelUpDot = bestAction != null && bestAction.Type == ActionType.Upgrade
                    && levelResult.HasValue && levelResult.Value.Suggestion == "LEVEL_UP",
                ShowBuyDot = bestAction != null && bestAction.Type == ActionType.BuyMinion,
                Phase = phase,
                Pace = pace,
                CompDir = compDir,
                LockIcon = lockIcon,
            };
        }

        private DecisionLevel MapLevelUpToDecisionLevel(LevelUpSuggestion suggestion)
        {
            switch (suggestion.Suggestion)
            {
                case "LEVEL_UP": return DecisionLevel.Critical;
                case "STABILIZE": return DecisionLevel.Major;
                default: return DecisionLevel.Minor;
            }
        }

        private static readonly Dictionary<string, string> TribeCnName = new Dictionary<string, string>
        {
            { "BEAST", "野兽" }, { "MECHANICAL", "机械" }, { "DRAGON", "龙" },
            { "ELEMENTAL", "元素" }, { "MURLOC", "鱼人" }, { "PIRATE", "海盗" },
            { "UNDEAD", "亡灵" }, { "QUILBOAR", "野猪人" }, { "NAGA", "纳迦" },
            { "DEMON", "恶魔" }, { "ALL", "全" },
        };

        private static string TribeCn(string code)
        {
            if (string.IsNullOrEmpty(code)) return "";
            string cn;
            return TribeCnName.TryGetValue(code, out cn) ? cn : code;
        }

        private string GetHeroAffinityDirection(GameState state)
        {
            if (_heroPower == null) return "";
            var strat = _heroPower.GetStrategyForPower(
                state.HeroCardId, state.HeroPowerCardId);
            if (strat.TribeAffinity == null || strat.TribeAffinity.Count == 0) return "";

            string bestTribe = "";
            float bestAffinity = 0f;
            foreach (var kv in strat.TribeAffinity)
            {
                if (kv.Value > bestAffinity)
                {
                    bestAffinity = kv.Value;
                    bestTribe = kv.Key;
                }
            }
            return bestAffinity >= 0.20f ? bestTribe : "";
        }

        // ── Combo 检测辅助方法 ──

        /// <summary>2026-08-19 法术指向性判定(按卡面文本): 需要随从目标的法术才标指向性。</summary>
        private static bool SpellNeedsTarget(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (text.Contains("使一个") || text.Contains("对一个") || text.Contains("选择一个")
                || text.Contains("使目标") || text.Contains("对一个随从") || text.Contains("为你的一个"))
                return true;
            return false;
        }

        /// <summary>已知 combo 法术卡映射：法术CardId → 所需组件的CardId</summary>
        private static readonly Dictionary<string, string> ComboMap = new Dictionary<string, string>
        {
            { "BG35_952", "BG35_123" },  // 背靠背 → 灾变先锋
        };

        /// <summary>判断手牌是否指向性卡牌(需要选择目标): 法术/战吼/磁力/塑造法术等</summary>
        private static bool IsTargetedCard(MinionData card)
        {
            if (card == null) return false;
            // 2026-08-19 实测修复(A3/B15): 法术不能一律视为指向性——
            // 慎重投资(下回合2币)/铸币类/全局/随机类无需目标, 误标"指向随从"误导;
            // 香蕉果盘(使一个随从+2/+2)必须带目标, 无目标时标"缺目标"而非"直接使用"
            if (card.IsSpell) return SpellNeedsTarget(card.CardText ?? "");
            var name = card.CardName ?? "";
            var cardId = card.CardId ?? "";
            // 磁力: 经典指向性机制
            if (name.Contains("磁力") || cardId.Contains("Magnetic")) return true;
            // 战吼/指向性随从: 卡名关键词
            if (name.Contains("使") || name.Contains("吞噬") || name.Contains("塑造")
                || name.Contains("造成") || name.Contains("对一个") || name.Contains("选择一个")) return true;
            // 已知指向性随从类型: 通过cardId前缀检测
            // 战吼类: 大多数战吼随从需要目标选择(除非是自身buff)
            if (name.Contains("战吼") && !name.Contains("随机")) return true;
            return false;
        }

        private static bool IsComboSpell(string cardId)
        {
            return !string.IsNullOrEmpty(cardId) && ComboMap.ContainsKey(cardId);
        }

        private static string ComboPieceFor(string cardId)
        {
            string piece;
            return ComboMap.TryGetValue(cardId, out piece) ? piece : "";
        }

        private static bool BoardHasCardId(GameState state, string cardId)
        {
            if (state.BoardMinions == null || string.IsNullOrEmpty(cardId)) return false;
            foreach (var m in state.BoardMinions)
                if (m.CardId == cardId)
                    return true;
            return false;
        }

        /// <summary>检查卡牌是否有战吼关键词（通过FeatureExtractor的本机派生语义）</summary>
        private static bool IsBattlecryCard(MinionData m, FeatureExtractor fe)
        {
            if (m == null || string.IsNullOrEmpty(m.CardId)) return false;
            // 直接检查常见战吼卡模式
            if (fe != null)
            {
                try
                {
                    var semantics = fe.GetCardSemantics(m.CardId);
                    if (semantics != null && semantics.HasMechanic("BATTLECRY"))
                        return true;
                }
                catch { }
            }
            return false;
        }

        // Token/生成类卡牌 (血宝石、三连奖励等), 不应标记为待售
        private static bool IsTokenCard(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return true;
            // 血宝石
            if (cardId.Contains("GEM") || cardId.Contains("Gem")) return true;
            // 三连奖励token
            if (cardId.Contains("Triples") || cardId.Contains("triples")) return true;
            // Coin/铸币token
            if (cardId.Contains("Coin") || cardId.Contains("TheCoin")) return true;
            return false;
        }

        // 通用强卡 (铜须/瑞文/达卡莱等), 不应被标记为待售
        private static bool IsUniversalKeepCard(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return false;
            // 铜须 BG_LOE_077
            if (cardId == "BG_LOE_077") return true;
            // 瑞文/提图斯 BG25_354, BG25_354g, BG25_354_G
            if (cardId.StartsWith("BG25_354")) return true;
            // 达卡莱附魔师 BG26_ICC_901
            if (cardId == "BG26_ICC_901") return true;
            // 布莱恩·铜须 (小铜须)
            if (cardId == "TB_BaconUps_045") return true;
            return false;
        }

        /// <summary>检测文本是否含裸 cardId(GetName 三级缓存全 miss 兜底泄露)。
        /// 匹配 BG##_/TB_/MagicItem 等卡牌代码前缀, 仅用于诊断日志。</summary>
        private static bool LooksLikeRawCardId(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            // 取 "→" 之后的卡名部分(动作前缀如"买→"不参与判断)
            int arrow = text.LastIndexOf('→');
            string name = arrow >= 0 ? text.Substring(arrow + 1) : text;
            name = name.Trim();
            if (name.Length < 4) return false;
            // 卡牌代码特征: 含下划线 + 全为 ASCII 字母数字下划线(无中文)
            if (name.IndexOf('_') < 0) return false;
            foreach (char c in name)
                if (c > 127) return false;  // 含中文 → 是正常卡名
            return true;
        }

        private static void VisualizerLog(string msg)
        {
            try
            {
                var bobDir = BobCoachDataPaths.Root;
                System.IO.Directory.CreateDirectory(bobDir);
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(bobDir, "bob_coach.log"),
                    string.Format("[{0:O}] [Visualizer] {1}\n", DateTime.UtcNow, msg),
                    System.Text.Encoding.UTF8);
            }
            catch { }
        }
    }
}
