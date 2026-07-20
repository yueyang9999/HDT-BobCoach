using System;
using System.Collections.Generic;
using System.Linq;

namespace BobCoach.Engine
{
    /// <summary>
    /// 金币自追踪器 — 纯计算，无任何 HDT (Core.Game) 依赖。
    ///
    /// 设计要点：
    /// - 从 GameStateExtractor.TrackGold 抽离，所有输入由调用方注入（商店/板面/等级/畸变参数/HDT基准值），
    ///   故可被 JS 镜像测试 (test/test_gold_tracker.js) 完整覆盖，不再是"无护栏的金币雷区"。
    /// - 状态跨帧存活（实例字段），由 GameStateExtractor 持久持有一个实例。
    /// - HDT 的 RESOURCES 标签在战棋中不实时更新，仅在回合切换时作基准校准用（hdtResourceTag，-1=不可用）。
    ///
    /// 历史 bug（本次抽离时定点修复，有测试兜底）：
    /// - bonusGold 曾是死变量（读了不用）→ 现按"开局一次性加金"语义正确加入基准（金钱大战 +10）。
    /// - lastUpgradeTurn 曾依赖 extractor→plugin 跨方法写回，TrackGold 早于写回执行恒读到默认值 1
    ///   → 现由本类自维护 _trackedLastUpgradeTurn，彻底摆脱时序耦合。
    /// </summary>
    public class GoldTracker
    {
        private int _selfTrackedGold = -1;
        private int _selfTrackedTurn = -1;
        private int _prevShopCount = -1;
        private int _prevBoardCount = -1;
        private int _prevHandCount = -1;
        private int _prevTavernTier = -1;
        private int _prevTavernUpgradeCost = -1;
        private List<int> _prevShopEntityIds = new List<int>();  // 上帧商店实体ID(检测刷新)
        private int _freeCardsUsedThisTurn = 0;
        private bool _freeRefreshUsed = false;
        private bool _firstBuyUsed = false;
        private bool _firstPurchaseUsed = false;

        // 本次抽离新增（修 bug 用）
        private bool _bonusGoldApplied = false;       // 开局一次性加金标志(金钱大战等)
        private int _trackedLastUpgradeTurn = 1;      // 自维护的升本回合(不依赖外部写回)

        // 07061713 修复: stale-buy 双扣去重
        private int _pendingStaleBuyCharges = 0;      // 已按stale路径扣费、商店数尚未减少的购买数

        /// <summary>新游戏开局重置。对应旧 GameStateExtractor reset 的 _selfTrackedGold=-1; _selfTrackedTurn=-1;</summary>
        public void Reset()
        {
            _selfTrackedGold = -1;
            _selfTrackedTurn = -1;
            _prevShopCount = -1;
            _prevBoardCount = -1;
            _prevHandCount = -1;
            _prevTavernTier = -1;
            _prevTavernUpgradeCost = -1;
            _prevShopEntityIds.Clear();
            _freeCardsUsedThisTurn = 0;
            _freeRefreshUsed = false;
            _firstBuyUsed = false;
            _firstPurchaseUsed = false;
            _bonusGoldApplied = false;
            _trackedLastUpgradeTurn = 1;
            _pendingStaleBuyCharges = 0;
        }

        /// <summary>
        /// 推进金币追踪一帧，返回当前剩余铸币。
        /// </summary>
        /// <param name="turn">当前回合</param>
        /// <param name="shopCount">商店随从数</param>
        /// <param name="shopEntityIds">商店随从实体ID列表(检测刷新)</param>
        /// <param name="boardCount">板面随从数</param>
        /// <param name="tavernTier">酒馆等级</param>
        /// <param name="bonusGold">畸变开局一次性加金(金钱大战=10)</param>
        /// <param name="goldPerTurn">畸变每回合额外铸币上限(权威无此类畸变,通常0)</param>
        /// <param name="minionCost">购买随从费用(默认3,畸变可减)</param>
        /// <param name="freeRefresh">畸变本回合免费刷新次数</param>
        /// <param name="freeCards">畸变本回合免费购买次数</param>
        /// <param name="firstBuyFree">畸变首次购买免费</param>
        /// <param name="hdtResourceTag">HDT RESOURCES 标签值(-1=不可用),仅回合切换校准用</param>
        /// <param name="handGainedFromShop">本帧新增手牌实体中来自上帧商店实体集合的数量(stale-buy 购买证据)</param>
        public int Advance(
            int turn, int shopCount, List<int> shopEntityIds,
            int boardCount, int handCount, int tavernTier,
            int bonusGold, int goldPerTurn, int minionCost,
            int freeRefresh, int freeCards, bool firstBuyFree,
            int hdtResourceTag, int handGainedFromShop = 0,
            IList<int> observedPurchaseCosts = null,
            int observedPurchasedMinionCount = 0,
            EffectiveGameRules effectiveRules = null,
            int tavernUpgradeCost = -1,
            IList<ObservedPurchase> observedPurchases = null)
        {
            effectiveRules = effectiveRules ?? EffectiveGameRules.Default;
            int maxGold = Math.Min(10, 2 + turn); // T1=3,T2=4...T8起=10(与 GameState.MaxGold 同步)
            maxGold = Math.Min(20, maxGold + goldPerTurn); // 畸变可能超过10
            // 开局一次性加金(金钱大战等): 当回合上限拔高
            int effBonus = _bonusGoldApplied ? 0 : bonusGold;
            int maxGoldWithBonus = Math.Min(20, maxGold + effBonus);

            var curShopIds = shopEntityIds ?? new List<int>();
            var successfulPurchases = observedPurchases == null
                ? null
                : observedPurchases.Where(purchase => purchase != null
                    && purchase.Succeeded).ToList();
            var observedCosts = successfulPurchases != null
                ? successfulPurchases.Select(purchase => purchase.Cost).ToList()
                : (observedPurchaseCosts ?? new List<int>()).ToList();
            int observedPurchaseCount = observedCosts.Count;
            bool hasUnknownPurchaseCost = observedCosts.Any(cost => cost < 0
                || cost == int.MaxValue);
            var exactPurchaseCosts = observedCosts.Where(cost => cost >= 0
                && cost != int.MaxValue).ToList();
            int exactPurchasedMinionCount = successfulPurchases != null
                ? successfulPurchases.Count(purchase => !purchase.IsSpell)
                : observedPurchasedMinionCount;

            // 新回合: 重置基准。
            if (turn != _selfTrackedTurn)
            {
                _selfTrackedTurn = turn;
                _prevShopCount = -1; _prevBoardCount = -1; _prevHandCount = -1; _prevTavernTier = -1;
                _prevTavernUpgradeCost = -1;
                _prevShopEntityIds.Clear();
                _pendingStaleBuyCharges = 0;
                int hdtBase = (hdtResourceTag < 0 || hdtResourceTag > 20) ? maxGold : hdtResourceTag;
                // 回合切换时若HDT标签未更新(仍为旧回合值), 则用回合最大值为基准
                if (hdtBase < maxGold) hdtBase = maxGold;
                // 开局一次性加金: 仅首个有效回合加一次
                _selfTrackedGold = Math.Max(0, Math.Min(maxGoldWithBonus, hdtBase + effBonus));
                if (effBonus > 0) _bonusGoldApplied = true;
                _freeCardsUsedThisTurn = 0;
                _freeRefreshUsed = false;
                _firstBuyUsed = false;
                _firstPurchaseUsed = false;
            }

            int curShop = shopCount;
            int curBoard = boardCount;
            int curHand = handCount;

            // 检测刷新: 商店ID完全变化
            bool shopRefreshed = _prevShopEntityIds.Count > 0 && curShopIds.Count > 0
                && !curShopIds.Any(id => _prevShopEntityIds.Contains(id));
            if (shopRefreshed)
            {
                _pendingStaleBuyCharges = 0; // 整店换血, pending离店实体已不存在
                bool automaticAfterPurchase = effectiveRules.RefreshAfterPurchase
                    && observedPurchaseCount > 0;
                if (automaticAfterPurchase)
                {
                    // 高戈奈斯等购后自动换店不是玩家主动刷新，不扣刷新费用。
                }
                else if (freeRefresh > 0 && !_freeRefreshUsed)
                {
                    _freeRefreshUsed = true;
                }
                else
                {
                    _selfTrackedGold = Math.Max(0, _selfTrackedGold - 1);
                }
            }

            if (observedPurchaseCount > 0)
            {
                foreach (int exactCost in exactPurchaseCosts)
                    _selfTrackedGold = Math.Max(0, _selfTrackedGold - exactCost);
                if (hasUnknownPurchaseCost) _selfTrackedGold = 0;
                _firstPurchaseUsed = true;
                if (exactPurchasedMinionCount > 0) _firstBuyUsed = true;
                if (!shopRefreshed) _pendingStaleBuyCharges += observedPurchaseCount;
            }

            // 检测购买: 商店数量减少 (先吸收stale路径已扣费的pending数, 防同一次购买双扣)
            if (_prevShopCount >= 0 && curShop < _prevShopCount)
            {
                int bought = _prevShopCount - curShop;
                int absorbed = observedPurchaseCount > 0
                    ? bought
                    : Math.Min(bought, _pendingStaleBuyCharges);
                _pendingStaleBuyCharges = Math.Max(0, _pendingStaleBuyCharges - absorbed);
                for (int i = 0; i < bought - absorbed && observedPurchaseCount == 0; i++)
                {
                    ApplyBuyCost(freeCards, firstBuyFree, minionCost);
                }
            }
            else if (observedPurchaseCount == 0
                && _prevShopCount >= 0 && _prevHandCount >= 0 && _prevBoardCount >= 0
                && curShop == _prevShopCount
                && curBoard == _prevBoardCount
                && curHand > _prevHandCount
                && handGainedFromShop > 0
                && SameShopEntitySet(curShopIds, _prevShopEntityIds))
            {
                // HDT can keep bought tavern entities in the shop list during the animation window.
                // 只有当新增手牌实体确实来自上帧商店实体集合时才算购买 —
                // 饰品给牌/发现拿牌/战利品同样"手牌增加+商店不变", 但实体ID不来自商店(07062107修复)。
                // 记入pending: 动画结束商店数真正减少时不再二次扣费(07061713根因之一)。
                int bought = Math.Min(handGainedFromShop, Math.Max(1, curShop));
                for (int i = 0; i < bought; i++)
                    ApplyBuyCost(freeCards, firstBuyFree, minionCost);
                _pendingStaleBuyCharges += bought;
            }

            // 检测售出: 板面减少 → +1每张 (封顶用含开局加金的本回合上限)
            if (_prevBoardCount >= 0 && curBoard < _prevBoardCount)
                _selfTrackedGold = Math.Min(maxGoldWithBonus, _selfTrackedGold + (_prevBoardCount - curBoard));

            // 检测升本: 用自维护的 _trackedLastUpgradeTurn 算折扣(摆脱外部写回时序)
            if (_prevTavernTier >= 1 && tavernTier > _prevTavernTier)
            {
                int upgradeCost = _prevTavernUpgradeCost >= 0
                    ? _prevTavernUpgradeCost
                    : ActionEnumerator.GetUpgradeCost(_prevTavernTier, turn, _trackedLastUpgradeTurn);
                _selfTrackedGold = Math.Max(0,
                    _selfTrackedGold - upgradeCost);
                _trackedLastUpgradeTurn = turn;  // 扣费后再更新
            }

            // 手牌增加(战吼给币/发现等) — 不影响追踪, 金由操作扣除覆盖
            //
            // 07062107 复盘: 曾在此处做"稳定帧向HDT RESOURCES重校准"自愈, 已移除 —
            // HDT RESOURCES 在BG招募阶段内保持回合起始满金不随消费减少(升本/购买后仍是旧值),
            // 27次RESYNC全部把正确的self改成错误的HDT值。回合内self恒<=HDT(起始满金)属正常态。
            // 校准只在回合切换时进行(见上方新回合基准逻辑)。

            _prevShopCount = curShop;
            _prevBoardCount = curBoard;
            _prevHandCount = curHand;
            _prevTavernTier = tavernTier;
            _prevTavernUpgradeCost = tavernUpgradeCost;
            _prevShopEntityIds = curShopIds;

            return Math.Max(0, Math.Min(maxGoldWithBonus, _selfTrackedGold));
        }

        public bool IsFirstMinionPurchaseUsed(int turn)
        {
            return turn == _selfTrackedTurn && _firstBuyUsed;
        }

        public bool IsFirstPurchaseUsed(int turn)
        {
            return turn == _selfTrackedTurn && _firstPurchaseUsed;
        }

        private void ApplyBuyCost(int freeCards, bool firstBuyFree, int minionCost)
        {
            _firstPurchaseUsed = true;
            if (freeCards > 0 && _freeCardsUsedThisTurn < freeCards)
            {
                _freeCardsUsedThisTurn++;
            }
            else if (firstBuyFree && !_firstBuyUsed)
            {
                _firstBuyUsed = true;
            }
            else
            {
                _selfTrackedGold = Math.Max(0, _selfTrackedGold - minionCost);
            }
        }

        private static bool SameShopEntitySet(List<int> a, List<int> b)
        {
            if (a == null || b == null || a.Count == 0 || b.Count == 0) return false;
            if (a.Count != b.Count) return false;
            var aa = a.OrderBy(x => x).ToList();
            var bb = b.OrderBy(x => x).ToList();
            for (int i = 0; i < aa.Count; i++)
                if (aa[i] != bb[i]) return false;
            return true;
        }
    }
}
