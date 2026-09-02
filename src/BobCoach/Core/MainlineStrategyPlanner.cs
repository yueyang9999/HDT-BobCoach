using System;
using System.Collections.Generic;
using System.Linq;

namespace BobCoach.Engine
{
    internal enum MainlineNodePriority
    {
        Normal,
        CoreNode,
        TripleNode,
        ResourceNode,
        Survival,
    }

    internal sealed class MainlinePlan
    {
        internal string Archetype = "";
        internal string GuideId = "";
        internal string CompDir = "";
        internal string Strategy = "";
        internal string Reason = "";
        internal string Tier = "";
        internal string MetaSource = "";
        internal List<string> CoreCardIds = new List<string>();
        internal List<string> CoreCardNames = new List<string>();
        internal List<string> MissingCoreCardNames = new List<string>();
        internal int BoardCoreCount;
        internal int HandCoreCount;
        internal int ShopCoreCount;
        internal bool HasTripleInShop;
        internal bool HasCoreInShop;
        internal bool ShouldBuyCoreFirst;
        internal bool ShouldHoldForNextTurn;
        internal MainlineNodePriority NodePriority = MainlineNodePriority.Normal;
        internal string Stage = "";
        internal string Mode = "";
        internal double CurrentPathScore;
        internal double BestAlternativeScore;
        internal double PivotScore;
        internal bool ShouldConsiderPivot;
        internal bool ShouldHedge;
        internal int PivotObservationCount;
        internal HashSet<string> AlternativeCoreCardIds =
            new HashSet<string>(StringComparer.Ordinal);
        internal string CounterPlan = "";
        internal bool NeedTradeUp;
    }

    internal sealed class MainlineStrategyPlanner
    {
        private sealed class PivotObservation
        {
            internal string GuideId;
            internal int Turn;
            internal bool Triple;
        }

        private readonly List<PivotObservation> _pivotObservations =
            new List<PivotObservation>();
        private int _lastObservationTurn = -1;

        private sealed class TribeSignal
        {
            internal double Score;
            internal int CoreCount;
            internal int CardCount;
        }

        private static readonly Dictionary<string, string> TribeEnToCn =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "BEAST", "野兽" },
                { "MECHANICAL", "机械" },
                { "DEMON", "恶魔" },
                { "DRAGON", "龙" },
                { "ELEMENTAL", "元素" },
                { "MURLOC", "鱼人" },
                { "PIRATE", "海盗" },
                { "UNDEAD", "亡灵" },
                { "QUILBOAR", "野猪人" },
                { "NAGA", "纳迦" },
                { "ALL", "全" },
            };

        internal MainlinePlan Analyze(
            GameState state,
            CompGuideCatalog catalog,
            CompLockResult compLock)
        {
            var plan = new MainlinePlan();
            if (state == null) return plan;

            string compDir = ResolveCompDir(state, compLock, catalog);
            plan.CompDir = compDir;

            var guide = FindGuide(catalog, compDir);
            if (guide != null)
            {
                plan.Archetype = guide.Name;
                plan.GuideId = guide.Id;
                plan.Tier = guide.Tier ?? "";
                plan.Strategy = guide.Strategy ?? "";
                plan.MetaSource = catalog != null ? catalog.MetaBaselineSource : "";
                plan.CoreCardIds = catalog != null
                    ? catalog.GetCoreCardIdsForGuide(guide)
                    : new List<string>();
                plan.CoreCardNames = plan.CoreCardIds
                    .Where(id => !string.IsNullOrEmpty(id))
                    .Select(ResolveCardName)
                    .Where(name => !string.IsNullOrEmpty(name))
                    .ToList();
            }
            if (string.IsNullOrEmpty(plan.Archetype))
                plan.Archetype = compDir;

            if (!string.IsNullOrEmpty(compDir))
            {
                CountOwnedCores(state, plan);
                BuildMissingCores(state, plan);
                ScanShop(state, plan);
            }

            ResetPivotMemoryIfNewGame(state);
            ComputeStage(state, plan);
            DecidePriority(state, plan);
            ComputePathScoresAndPivot(state, catalog, compLock, plan);
            ObservePivotCandidates(state, catalog, plan);
            BuildCounterPlan(state, plan);
            DecideDynamicMode(state, plan);
            return plan;
        }

        internal bool ShouldSkipUpgrade(GameState state, MainlinePlan plan)
        {
            if (state == null || plan == null || !plan.ShouldBuyCoreFirst)
                return false;
            return HasAffordableMainlineNode(state, plan, true);
        }

        internal double Adjust(
            double value,
            GameState state,
            GameAction action,
            MainlinePlan plan)
        {
            if (state == null || action == null || plan == null
                || string.IsNullOrEmpty(plan.CompDir))
                return value;

            switch (action.Type)
            {
                case ActionType.BuyMinion:
                    return AdjustBuyMinion(value, state, action, plan);
                case ActionType.BuySpell:
                    return AdjustBuySpell(value, state, action, plan);
                case ActionType.Refresh:
                    return AdjustRefresh(value, state, plan);
                case ActionType.Upgrade:
                    return AdjustUpgrade(value, state, plan);
                case ActionType.SellMinion:
                    return AdjustSell(value, state, action, plan);
                case ActionType.FreezeShop:
                    return AdjustFreeze(value, state, plan);
                default:
                    return value;
            }
        }

        private double AdjustBuyMinion(
            double value,
            GameState state,
            GameAction action,
            MainlinePlan plan)
        {
            if (action.TargetIndex < 0 || action.TargetIndex >= state.ShopMinions.Count)
                return value;
            var shop = state.ShopMinions[action.TargetIndex];
            if (shop == null) return value;
            if (IsCoreCard(shop.CardId, plan.CoreCardIds))
            {
                value += plan.BoardCoreCount == 0 ? 0.75 : 0.45;
                if (plan.NodePriority == MainlineNodePriority.TripleNode)
                    value += 0.30;
            }
            else if (MatchesComp(shop.Tribe, plan.CompDir))
            {
                value += 0.08;
            }
            bool latePathCommitment = state.Turn >= 9
                && plan.BoardCoreCount + plan.HandCoreCount >= 3;
            bool isAlternativeCore = IsCoreCard(
                shop.CardId, plan.AlternativeCoreCardIds);
            bool isTradeTech = plan.NeedTradeUp
                && (shop.Venomous || shop.Poisonous
                    || shop.DivineShield || shop.Reborn);
            if (latePathCommitment
                && !IsCoreCard(shop.CardId, plan.CoreCardIds)
                && !MatchesComp(shop.Tribe, plan.CompDir)
                && !isAlternativeCore
                && !isTradeTech)
            {
                value *= string.IsNullOrEmpty(shop.Tribe) ? 0.70 : 0.62;
            }
            if (IsCoreCard(shop.CardId, plan.AlternativeCoreCardIds)
                && (plan.Mode == "Pivot" || plan.Mode == "Hedge"))
                value += 0.25;
            if (plan.NeedTradeUp
                && (shop.Venomous || shop.Poisonous
                    || shop.DivineShield || shop.Reborn))
                value += 0.15;
            return value;
        }

        private double AdjustBuySpell(
            double value,
            GameState state,
            GameAction action,
            MainlinePlan plan)
        {
            if (state.Turn > 6 || string.IsNullOrEmpty(action.CardId))
                return value;
            string name = ResolveCardName(action.CardId) ?? action.CardId ?? "";
            if (name.Contains("铸币") || name.Contains("金币")
                || name.Contains("投资") || name.Contains("发掘")
                || name.Contains("甄选") || name.Contains("刷新"))
            {
                value += 0.20;
            }
            return value;
        }

        private double AdjustRefresh(double value, GameState state, MainlinePlan plan)
        {
            if (plan.ShouldBuyCoreFirst && !plan.HasCoreInShop)
                value *= 1.12;
            else if (plan.MissingCoreCardNames.Count > 0
                && state.FreeRefreshCount > 0
                && !plan.HasCoreInShop)
                value *= 1.08;
            if ((plan.Mode == "Pivot" || plan.Mode == "Hedge")
                && plan.MissingCoreCardNames.Count > 0)
                value *= 1.15;
            return value;
        }

        private double AdjustUpgrade(double value, GameState state, MainlinePlan plan)
        {
            if (plan.ShouldBuyCoreFirst && HasAffordableMainlineNode(state, plan, true))
                return value * 0.12;
            if (plan.ShouldHoldForNextTurn && state.Turn <= 5
                && GetLiveGold(state) < 3)
                return value * 0.75;
            if (plan.Mode == "Survival" || plan.Mode == "Economy"
                || plan.Mode == "Pivot")
                value *= 0.75;
            return value;
        }

        private double AdjustSell(
            double value,
            GameState state,
            GameAction action,
            MainlinePlan plan)
        {
            if (action.TargetIndex < 0 || action.TargetIndex >= state.BoardMinions.Count)
                return value;
            var sold = state.BoardMinions[action.TargetIndex];
            if (sold == null) return value;
            if (IsCoreCard(sold.CardId, plan.CoreCardIds))
                return value * 0.05;
            if (plan.ShouldBuyCoreFirst && !plan.HasCoreInShop)
                value *= 0.90;
            if (plan.Mode == "Pivot" && sold.Tier <= 3)
                value += 0.05;
            return value;
        }

        private double AdjustFreeze(double value, GameState state, MainlinePlan plan)
        {
            int gold = GetLiveGold(state);
            bool coreUnaffordable = (plan.HasCoreInShop || plan.HasTripleInShop)
                && gold < 3;
            if (coreUnaffordable)
                value *= 1.35;
            if (plan.AlternativeCoreCardIds.Count > 0 && gold < 3)
                value *= 1.20;
            return value;
        }

        private bool HasAffordableMainlineNode(
            GameState state,
            MainlinePlan plan,
            bool allowAfterSell)
        {
            if (state.ShopMinions == null || state.ShopMinions.Count == 0)
                return false;
            int gold = GetLiveGold(state);
            bool sellable = allowAfterSell
                && state.BoardMinions != null
                && state.BoardMinions.Any(m => m != null && !m.Golden);
            var rules = state.EffectiveRules ?? EffectiveGameRules.Default;
            foreach (var shop in state.ShopMinions)
            {
                if (shop == null || shop.IsSpell) continue;
                if (!IsCoreCard(shop.CardId, plan.CoreCardIds)
                    && !TripleRuleEvaluator.CompletesGolden(
                        state, shop.CardId, rules))
                    continue;
                int cost = GameRuleEvaluator.GetPurchaseCost(
                    state, shop, state.HeroCardId, rules);
                if (gold >= cost || (sellable && gold + 1 >= cost))
                    return true;
            }
            return false;
        }

        private void CountOwnedCores(GameState state, MainlinePlan plan)
        {
            foreach (var m in state.BoardMinions)
            {
                if (m != null && IsCoreCard(m.CardId, plan.CoreCardIds))
                    plan.BoardCoreCount++;
            }
            foreach (var m in state.HandMinions)
            {
                if (m != null && IsCoreCard(m.CardId, plan.CoreCardIds))
                    plan.HandCoreCount++;
            }
        }

        private void BuildMissingCores(GameState state, MainlinePlan plan)
        {
            var owned = new HashSet<string>(StringComparer.Ordinal);
            foreach (var m in state.BoardMinions)
                if (m != null) owned.Add(m.CardId ?? "");
            foreach (var m in state.HandMinions)
                if (m != null) owned.Add(m.CardId ?? "");
            foreach (var id in plan.CoreCardIds)
            {
                if (string.IsNullOrEmpty(id)) continue;
                bool has = owned.Any(c => IsCoreCard(c, new List<string> { id }));
                if (!has)
                {
                    string name = ResolveCardName(id);
                    if (!string.IsNullOrEmpty(name))
                        plan.MissingCoreCardNames.Add(name);
                }
            }
        }

        private void ScanShop(GameState state, MainlinePlan plan)
        {
            if (state.ShopMinions == null) return;
            var rules = state.EffectiveRules ?? EffectiveGameRules.Default;
            foreach (var shop in state.ShopMinions)
            {
                if (shop == null || shop.IsSpell) continue;
                if (IsCoreCard(shop.CardId, plan.CoreCardIds))
                    plan.ShopCoreCount++;
                if (TripleRuleEvaluator.CompletesGolden(
                    state, shop.CardId, rules))
                    plan.HasTripleInShop = true;
            }
            plan.HasCoreInShop = plan.ShopCoreCount > 0;
        }

        private void DecidePriority(GameState state, MainlinePlan plan)
        {
            int gold = GetLiveGold(state);
            int nextGold = Math.Min(10, 2 + state.Turn + 1);
            bool healthSafe = state.Health + state.Armor > 18 || state.Turn <= 6;
            bool canAffordCoreNow = HasAffordableMainlineNode(state, plan, false);
            bool canAffordCoreAfterSell = HasAffordableMainlineNode(state, plan, true);

            if ((plan.HasTripleInShop || plan.HasCoreInShop)
                && (canAffordCoreNow || canAffordCoreAfterSell)
                && plan.MissingCoreCardNames.Count > 0)
            {
                plan.ShouldBuyCoreFirst = true;
                plan.NodePriority = plan.HasTripleInShop
                    ? MainlineNodePriority.TripleNode
                    : MainlineNodePriority.CoreNode;
                plan.Reason = "主线大节点优先";
            }
            else if (plan.MissingCoreCardNames.Count > 0
                && gold < 3
                && nextGold >= 3
                && healthSafe)
            {
                plan.ShouldHoldForNextTurn = true;
                plan.NodePriority = MainlineNodePriority.ResourceNode;
                plan.Reason = "核心缺费，留钱下一节点";
            }
            else
            {
                plan.NodePriority = MainlineNodePriority.Normal;
            }
        }

        private void ResetPivotMemoryIfNewGame(GameState state)
        {
            if (state.Turn < _lastObservationTurn)
            {
                _pivotObservations.Clear();
                _lastObservationTurn = state.Turn;
            }
        }

        private void ComputeStage(GameState state, MainlinePlan plan)
        {
            if (state.Turn <= 5 || state.TavernTier <= 3)
                plan.Stage = "Early";
            else if (state.Turn <= 9 || state.TavernTier <= 5)
                plan.Stage = "Mid";
            else
                plan.Stage = "Late";
        }

        private void ComputePathScoresAndPivot(
            GameState state,
            CompGuideCatalog catalog,
            CompLockResult compLock,
            MainlinePlan plan)
        {
            if (catalog == null || catalog.Guides == null || catalog.Guides.Count == 0)
                return;

            double bestScore = double.NegativeInfinity;
            CompGuide bestAlternative = null;
            plan.BestAlternativeScore = double.NegativeInfinity;
            plan.PivotScore = double.NegativeInfinity;
            plan.ShouldHedge = false;
            plan.ShouldConsiderPivot = false;
            foreach (var guide in catalog.Guides)
            {
                if (guide == null || !IsGuideAvailable(guide, state.AvailableTribes))
                    continue;

                double score = GetGuideScore(state, catalog, guide, plan);
                if (guide.Id == plan.GuideId)
                {
                    plan.CurrentPathScore = score;
                    continue;
                }

                // 替代流派必须已有实际核心/三连证据，不能只靠环境分领先就触发转型。
                if (!HasGuideEvidence(state, catalog, guide, plan))
                    continue;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestAlternative = guide;
                }
            }

            plan.BestAlternativeScore = bestScore;
            plan.PivotScore = bestScore - plan.CurrentPathScore;

            if (bestAlternative != null)
            {
                plan.AlternativeCoreCardIds =
                    new HashSet<string>(
                        GetAlternativeCoreIds(catalog, bestAlternative, plan),
                        StringComparer.Ordinal);

                double hedgeThreshold = plan.Stage == "Early" ? 0.15
                    : plan.Stage == "Mid" ? 0.25 : 0.35;
                double pivotThreshold = plan.Stage == "Early" ? 0.40
                    : plan.Stage == "Mid" ? 0.55 : 0.70;

                bool hasCurrentDirection = !string.IsNullOrEmpty(plan.GuideId)
                    || plan.BoardCoreCount + plan.HandCoreCount > 0
                    || (compLock != null && compLock.State != LockState.None);
                plan.ShouldHedge = hasCurrentDirection
                    && (plan.PivotScore >= hedgeThreshold
                        || (plan.PivotScore >= 0.10 && RecentPivotObservation()));
                plan.ShouldConsiderPivot = hasCurrentDirection
                    && plan.PivotScore >= pivotThreshold
                    && plan.MissingCoreCardNames.Count > 0
                    && state.Health + state.Armor > 12
                    && (compLock == null || compLock.State != LockState.Hard);
                if (plan.ShouldBuyCoreFirst || plan.ShouldHoldForNextTurn)
                {
                    plan.ShouldConsiderPivot = false;
                    plan.ShouldHedge = false;
                }
            }
            else
            {
                plan.AlternativeCoreCardIds.Clear();
            }
        }

        private bool HasGuideEvidence(
            GameState state,
            CompGuideCatalog catalog,
            CompGuide guide,
            MainlinePlan plan)
        {
            var coreIds = GetAlternativeCoreIds(catalog, guide, plan);
            if (coreIds.Count == 0)
                return false;

            var ownedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var m in state.BoardMinions)
                if (m != null)
                    AddEvidenceId(ownedIds, m.CardId, coreIds);
            foreach (var m in state.HandMinions)
                if (m != null)
                    AddEvidenceId(ownedIds, m.CardId, coreIds);

            var rules = state.EffectiveRules ?? EffectiveGameRules.Default;
            var shopIds = new HashSet<string>(StringComparer.Ordinal);
            bool triple = false;
            foreach (var shop in state.ShopMinions)
            {
                if (shop == null || shop.IsSpell) continue;
                AddEvidenceId(shopIds, shop.CardId, coreIds);
                if (TripleRuleEvaluator.CompletesGolden(state, shop.CardId, rules)
                    && IsCoreCard(shop.CardId, coreIds))
                    triple = true;
            }

            if (triple) return true;
            var distinctEvidence = new HashSet<string>(ownedIds, StringComparer.Ordinal);
            foreach (var id in shopIds)
                if (!string.IsNullOrEmpty(id))
                    distinctEvidence.Add(id);
            return distinctEvidence.Count >= 2;
        }

        private static void AddEvidenceId(
            HashSet<string> target,
            string cardId,
            ICollection<string> coreIds)
        {
            if (string.IsNullOrEmpty(cardId) || !IsCoreCard(cardId, coreIds))
                return;
            string baseId = cardId.EndsWith("_G", StringComparison.Ordinal)
                ? cardId.Substring(0, cardId.Length - 2)
                : cardId;
            if (!string.IsNullOrEmpty(baseId))
                target.Add(baseId);
        }

        private List<string> GetAlternativeCoreIds(
            CompGuideCatalog catalog,
            CompGuide guide,
            MainlinePlan plan)
        {
            var all = catalog.GetCoreCardIdsForGuide(guide);
            if (all == null || all.Count == 0)
                return new List<string>();
            return all
                .Where(id => !string.IsNullOrEmpty(id)
                    && !IsCoreCard(id, plan.CoreCardIds)
                    && !IsGenericPathEvidenceCard(id))
                .ToList();
        }

        private static bool IsGenericPathEvidenceCard(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return false;
            string name = ResolveCardName(cardId);
            if (!string.IsNullOrEmpty(name)
                && (name.Contains("布莱恩·铜须")
                    || name.Contains("达卡莱附魔师")
                    || name.Contains("提图斯·瑞文戴尔")
                    || name.Contains("巴琳达·斯通赫尔斯")
                    || name.Contains("星元自动机")
                    || name.Contains("吵吵模组")
                    || name.Contains("愤怒编织者")
                    || name.Contains("机鳍鱼人")))
                return true;

            return cardId == "BG36_764"
                || cardId == "BG36_764_G"
                || cardId == "BG_TTN_401"
                || cardId == "BG_TTN_401_G"
                || cardId == "BG_BOT_911"
                || cardId == "BG_BOT_911_G"
                || cardId == "BGS_004"
                || cardId == "BGS_004_G"
                || cardId == "BG_LOE_077"
                || cardId == "BG_LOE_077_G"
                || cardId == "BG26_ICC_901"
                || cardId == "BG26_ICC_901_G"
                || cardId == "BG25_354"
                || cardId == "BG25_354_G"
                || cardId == "BG35_883"
                || cardId == "BG35_883_G";
        }

        private double GetGuideScore(
            GameState state,
            CompGuideCatalog catalog,
            CompGuide guide,
            MainlinePlan plan)
        {
            int tierWeight;
            double meta = catalog.TryGetTierWeight(guide.Tier, out tierWeight)
                ? tierWeight : 0;
            var coreIds = catalog.GetCoreCardIdsForGuide(guide);
            var coreSet = new HashSet<string>(coreIds, StringComparer.Ordinal);

            int ownedCore = 0;
            foreach (var m in state.BoardMinions)
                if (m != null && IsCoreCard(m.CardId, coreIds))
                    ownedCore++;
            foreach (var m in state.HandMinions)
                if (m != null && IsCoreCard(m.CardId, coreIds))
                    ownedCore++;

            int shopCore = 0;
            bool triple = false;
            var rules = state.EffectiveRules ?? EffectiveGameRules.Default;
            foreach (var shop in state.ShopMinions)
            {
                if (shop == null || shop.IsSpell) continue;
                if (IsCoreCard(shop.CardId, coreIds))
                    shopCore++;
                if (TripleRuleEvaluator.CompletesGolden(state, shop.CardId, rules)
                    && IsCoreCard(shop.CardId, coreIds))
                    triple = true;
            }

            int tribeCommitment = 0;
            foreach (var m in state.BoardMinions)
                if (m != null && MatchesGuideTribe(m.Tribe, guide))
                    tribeCommitment++;
            foreach (var m in state.HandMinions)
                if (m != null && MatchesGuideTribe(m.Tribe, guide))
                    tribeCommitment++;

            double metaWeight = plan.Stage == "Early" ? 0.40
                : plan.Stage == "Mid" ? 0.30 : 0.20;
            double coreWeight = plan.Stage == "Early" ? 0.20
                : plan.Stage == "Mid" ? 0.30 : 0.40;
            double shopWeight = plan.Stage == "Early" ? 0.25
                : plan.Stage == "Mid" ? 0.25 : 0.15;
            double tribeWeight = plan.Stage == "Early" ? 0.15
                : plan.Stage == "Mid" ? 0.15 : 0.25;

            return meta * metaWeight
                + Math.Min(ownedCore, 6) * coreWeight
                + Math.Min(shopCore, 3) * shopWeight
                + Math.Min(tribeCommitment, 7) * tribeWeight
                + (triple ? 0.30 : 0.0);
        }

        private void ObservePivotCandidates(
            GameState state,
            CompGuideCatalog catalog,
            MainlinePlan plan)
        {
            _lastObservationTurn = state.Turn;
            if (catalog == null || catalog.Guides == null) return;

            bool hasCurrentDirection = !string.IsNullOrEmpty(plan.CompDir)
                || !string.IsNullOrEmpty(plan.GuideId)
                || plan.BoardCoreCount + plan.HandCoreCount > 0;
            if (!hasCurrentDirection)
            {
                _pivotObservations.Clear();
                plan.PivotObservationCount = 0;
                plan.AlternativeCoreCardIds.Clear();
                return;
            }

            var rules = state.EffectiveRules ?? EffectiveGameRules.Default;
            foreach (var guide in catalog.Guides)
            {
                if (guide == null || guide.Id == plan.GuideId
                    || !IsGuideAvailable(guide, state.AvailableTribes)
                    || !HasGuideEvidence(state, catalog, guide, plan))
                    continue;

                var coreIds = GetAlternativeCoreIds(catalog, guide, plan);
                if (coreIds.Count == 0)
                    continue;
                bool sawCore = false;
                bool sawTriple = false;
                foreach (var shop in state.ShopMinions)
                {
                    if (shop == null || shop.IsSpell) continue;
                    if (IsCoreCard(shop.CardId, coreIds))
                        sawCore = true;
                    if (TripleRuleEvaluator.CompletesGolden(state, shop.CardId, rules)
                        && IsCoreCard(shop.CardId, coreIds))
                        sawTriple = true;
                }

                if (!sawCore && !sawTriple) continue;
                if (!_pivotObservations.Any(o =>
                    o.GuideId == guide.Id
                    && o.Turn == state.Turn
                    && o.Triple == sawTriple))
                {
                    _pivotObservations.Add(new PivotObservation
                    {
                        GuideId = guide.Id,
                        Turn = state.Turn,
                        Triple = sawTriple,
                    });
                }

                if (plan.AlternativeCoreCardIds.Count == 0)
                {
                    plan.AlternativeCoreCardIds =
                        new HashSet<string>(coreIds, StringComparer.Ordinal);
                }
                else
                {
                    foreach (var id in coreIds)
                        if (!string.IsNullOrEmpty(id))
                            plan.AlternativeCoreCardIds.Add(id);
                }
            }

            _pivotObservations.RemoveAll(o => o.Turn < state.Turn - 2);
            plan.PivotObservationCount =
                _pivotObservations.Count(o => o.Turn >= state.Turn - 2);
        }

        private bool RecentPivotObservation()
        {
            return _pivotObservations.Any(o => o.Turn >= _lastObservationTurn - 1
                && o.Triple)
                || _pivotObservations.Count(
                    o => o.Turn >= _lastObservationTurn - 2) >= 2;
        }

        private void BuildCounterPlan(GameState state, MainlinePlan plan)
        {
            plan.CounterPlan = "";
            plan.NeedTradeUp = false;
            if (state.Turn < 7) return;

            var profile = OpponentTechAdvisor.AnalyzeOpponentBoard(state.Opponents);
            if (!profile.HasBoardData) return;

            if (profile.BigThreatCount >= 2
                && (profile.MaxHealth >= 15 || profile.MaxStats >= 20))
            {
                plan.CounterPlan = "big-stats";
            }
            else if (profile.WideFrequencyCount >= 3)
            {
                plan.CounterPlan = "frequency";
            }

            if (!string.IsNullOrEmpty(plan.CounterPlan)
                && GetPowerRatio(state) < 0.8)
                plan.NeedTradeUp = true;
        }

        private void DecideDynamicMode(GameState state, MainlinePlan plan)
        {
            bool underPressure = state.Health + state.Armor <= 12
                || GetPowerRatio(state) < 0.65;

            if (plan.ShouldBuyCoreFirst)
            {
                plan.Mode = "Commit";
                plan.AlternativeCoreCardIds.Clear();
                plan.ShouldConsiderPivot = false;
                plan.ShouldHedge = false;
            }
            else if (plan.ShouldHoldForNextTurn)
            {
                plan.Mode = "Economy";
                plan.AlternativeCoreCardIds.Clear();
                plan.ShouldConsiderPivot = false;
                plan.ShouldHedge = false;
            }
            else if (plan.ShouldConsiderPivot && !underPressure)
                plan.Mode = "Pivot";
            else if (plan.ShouldHedge && !underPressure)
                plan.Mode = "Hedge";
            else if (plan.NeedTradeUp)
                plan.Mode = "TradeUp";
            else if (underPressure)
                plan.Mode = "Survival";
            else
                plan.Mode = "Flexible";
        }

        private static bool IsGuideAvailable(
            CompGuide guide,
            HashSet<string> availableTribes)
        {
            if (guide == null || guide.Tribes == null || guide.Tribes.Count == 0)
                return true;
            if (guide.Tribes.Any(t =>
                !string.IsNullOrEmpty(t)
                && (t.Contains("混合") || t.Contains("全"))))
                return true;
            if (availableTribes == null || availableTribes.Count < 3)
                return true;

            foreach (var guideTribe in guide.Tribes)
            {
                if (string.IsNullOrEmpty(guideTribe)) continue;
                foreach (var available in availableTribes)
                {
                    string availableCn = ToCn(available);
                    if (guideTribe.Contains(availableCn)
                        || availableCn.Contains(guideTribe))
                        return true;
                }
            }
            return false;
        }

        private static bool MatchesGuideTribe(string tribeField, CompGuide guide)
        {
            if (string.IsNullOrEmpty(tribeField) || guide == null
                || guide.Tribes == null)
                return false;

            foreach (var guideTribe in guide.Tribes)
            {
                if (string.IsNullOrEmpty(guideTribe)) continue;
                if (guideTribe.Contains("混合") || guideTribe.Contains("全"))
                    return true;
                foreach (var tribe in MinionData.GetTribesArray(tribeField))
                {
                    if (tribe.Equals(guideTribe, StringComparison.OrdinalIgnoreCase)
                        || tribe.Contains(guideTribe)
                        || guideTribe.Contains(tribe))
                        return true;
                }
            }
            return false;
        }

        private static double GetPowerRatio(GameState state)
        {
            double own = EstimateBoardPower(state.BoardMinions);
            double opp = OpponentModel.GetAvgOpponentPower(
                state.Opponents, state.Turn);
            return opp > 0 ? own / opp : 1.0;
        }

        private static double EstimateBoardPower(IEnumerable<MinionData> board)
        {
            double power = 0;
            foreach (var m in board)
            {
                if (m == null) continue;
                power += Math.Max(0, m.Attack) + Math.Max(0, m.Health)
                    + Math.Max(0, m.Tier) * 0.4;
                if (m.DivineShield) power += 1.2;
                if (m.Reborn) power += 0.8;
                if (m.Poisonous || m.Venomous) power += 1.0;
            }
            return power;
        }

        private string ResolveCompDir(
            GameState state,
            CompLockResult compLock,
            CompGuideCatalog catalog)
        {
            if (compLock != null && compLock.State == LockState.Hard
                && !string.IsNullOrEmpty(compLock.DominantTribe))
                return compLock.DominantTribe;

            var coreTribeSignals = new Dictionary<string, TribeSignal>(
                StringComparer.OrdinalIgnoreCase);

            if (catalog != null && catalog.Guides != null)
            {
                foreach (var guide in catalog.Guides)
                {
                    if (guide == null || guide.Tribes == null
                        || guide.Tribes.Count == 0)
                        continue;
                    var coreIds = catalog.GetCoreCardIdsForGuide(guide);
                    if (coreIds == null || coreIds.Count == 0)
                        continue;
                    foreach (var tribe in guide.Tribes)
                    {
                        string en = ToEn(tribe);
                        if (string.IsNullOrEmpty(en) || en == "ALL") continue;
                        if (!coreTribeSignals.ContainsKey(en))
                            coreTribeSignals[en] = new TribeSignal();
                        foreach (var board in state.BoardMinions)
                        {
                            if (board != null
                                && IsCoreCard(board.CardId, coreIds))
                            {
                                coreTribeSignals[en].Score += 1.55;
                                coreTribeSignals[en].CoreCount++;
                            }
                        }
                        foreach (var hand in state.HandMinions)
                        {
                            if (hand != null
                                && IsCoreCard(hand.CardId, coreIds))
                            {
                                coreTribeSignals[en].Score += 1.05;
                                coreTribeSignals[en].CoreCount++;
                            }
                        }
                    }
                }
            }

            foreach (var board in state.BoardMinions)
            {
                if (board == null || string.IsNullOrEmpty(board.Tribe))
                    continue;
                foreach (var tribe in MinionData.GetTribesArray(board.Tribe))
                {
                    string en = ToEn(tribe);
                    if (string.IsNullOrEmpty(en) || en == "ALL") continue;
                    if (!coreTribeSignals.ContainsKey(en))
                        coreTribeSignals[en] = new TribeSignal();
                    coreTribeSignals[en].Score += 1.0 + Math.Max(0, board.Tier - 1) * 0.20;
                    coreTribeSignals[en].CardCount++;
                }
            }
            foreach (var hand in state.HandMinions)
            {
                if (hand == null || hand.IsSpell
                    || string.IsNullOrEmpty(hand.Tribe))
                    continue;
                foreach (var tribe in MinionData.GetTribesArray(hand.Tribe))
                {
                    string en = ToEn(tribe);
                    if (string.IsNullOrEmpty(en) || en == "ALL") continue;
                    if (!coreTribeSignals.ContainsKey(en))
                        coreTribeSignals[en] = new TribeSignal();
                    coreTribeSignals[en].Score +=
                        (1.0 + Math.Max(0, hand.Tier - 1) * 0.20) * 0.55;
                    coreTribeSignals[en].CardCount++;
                }
            }

            var coreDirection = coreTribeSignals
                .Where(kv => kv.Value != null && kv.Value.CoreCount > 0)
                .OrderByDescending(kv => kv.Value.Score)
                .Select(kv => kv.Key)
                .FirstOrDefault();
            if (!string.IsNullOrEmpty(coreDirection))
                return coreDirection;
            if (compLock != null && !string.IsNullOrEmpty(compLock.DominantTribe))
                return compLock.DominantTribe;
            return coreTribeSignals
                .Where(kv => kv.Value != null && kv.Value.CardCount > 0)
                .OrderByDescending(kv => kv.Value.Score)
                .Select(kv => kv.Key)
                .FirstOrDefault() ?? "";
        }

        private static string ToEn(string tribe)
        {
            if (string.IsNullOrEmpty(tribe)) return "";
            foreach (var pair in TribeEnToCn)
            {
                if (pair.Value == tribe)
                    return pair.Key;
            }
            return tribe;
        }

        private CompGuide FindGuide(CompGuideCatalog catalog, string compDir)
        {
            if (catalog == null || catalog.Guides == null || catalog.Guides.Count == 0)
                return null;
            string compCn = ToCn(compDir);
            return catalog.Guides
                .Where(g => g != null && g.Tribes != null
                    && g.Tribes.Any(t => MatchesTribe(t, compDir, compCn)))
                .OrderByDescending(g =>
                {
                    int w;
                    return catalog.TryGetTierWeight(g.Tier, out w) ? w : 0;
                })
                .ThenByDescending(g => g.CoreCards == null ? 0 : g.CoreCards.Count)
                .FirstOrDefault();
        }

        private static bool MatchesTribe(
            string guideTribe,
            string compDir,
            string compCn)
        {
            if (string.IsNullOrEmpty(guideTribe)) return false;
            if (guideTribe.Equals(compDir, StringComparison.OrdinalIgnoreCase))
                return true;
            if (!string.IsNullOrEmpty(compCn)
                && guideTribe.Contains(compCn))
                return true;
            return false;
        }

        private static string ToCn(string tribe)
        {
            if (string.IsNullOrEmpty(tribe)) return "";
            string cn;
            return TribeEnToCn.TryGetValue(tribe, out cn) ? cn : tribe;
        }

        private static bool MatchesComp(string tribeField, string compDir)
        {
            if (string.IsNullOrEmpty(tribeField) || string.IsNullOrEmpty(compDir))
                return false;
            string compCn = ToCn(compDir);
            foreach (var tribe in MinionData.GetTribesArray(tribeField))
            {
                if (tribe.Equals(compDir, StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrEmpty(compCn) && tribe.Contains(compCn)))
                    return true;
            }
            return false;
        }

        private static bool IsCoreCard(string cardId, ICollection<string> coreIds)
        {
            if (string.IsNullOrEmpty(cardId) || coreIds == null || coreIds.Count == 0)
                return false;
            if (coreIds.Contains(cardId)) return true;
            if (cardId.EndsWith("_G", StringComparison.Ordinal)
                && coreIds.Contains(cardId.Substring(0, cardId.Length - 2)))
                return true;
            return false;
        }

        private static int GetLiveGold(GameState state)
        {
            return state != null && state.LiveGold >= 0
                ? state.LiveGold : (state != null ? state.Gold : 0);
        }

        private static string ResolveCardName(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return "";
            return CardNameService.GetName(cardId);
        }
    }
}
