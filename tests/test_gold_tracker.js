/**
 * GoldTracker 金币自追踪逻辑测试
 *
 * 用 JS 镜像 C# Core/GoldTracker.Advance 的纯计算逻辑, 驱动帧序列验证:
 *   - maxGold 公式 Min(10, 2+turn) (锁住已修的 off-by-one)
 *   - 买随从扣 minionCost / 卖回 +1 / 刷新 -1 / 免费刷新不扣
 *   - 升本折扣回归 (bug2): 用 GoldTracker 自维护的 lastUpgradeTurn 算折扣, 不依赖外部写回
 *   - bonusGold 回归 (bug1): 金钱大战开局 +10 且只加一次(跨回合不重复)
 *   - firstBuyFree / freeCards 畸变路径
 *
 * 注: JS 镜像须与 C# Core/GoldTracker.cs 手工对齐。
 * test_ui_lifecycle.js 的 [GoldTracker] 静态断言负责锁住 C# 侧签名(Advance/Reset/字段/无Core.Game耦合)。
 */

"use strict";

// ── C# ActionEnumerator.GetUpgradeCost 镜像 ──
// 基础费: 1→2:5, 2→3:7, 3→4:9, 4→5:11, 5→6:13; 折扣 = max(0, turn - lastUpgradeTurn)
function getUpgradeCost(currentTier, turn, lastUpgradeTurn) {
    const baseCosts = [0, 5, 7, 9, 11, 13];
    const baseCost = currentTier < baseCosts.length ? baseCosts[currentTier] : 99;
    const discount = turn > lastUpgradeTurn ? turn - lastUpgradeTurn : 0;
    return Math.max(0, baseCost - discount);
}

// ── JS 镜像: GoldTracker (实例状态 + Advance/Reset) ──
function newTracker() {
    return {
        selfTrackedGold: -1, selfTrackedTurn: -1,
        prevShopCount: -1, prevBoardCount: -1, prevHandCount: -1, prevTavernTier: -1,
        prevShopEntityIds: [],
        freeCardsUsedThisTurn: 0, freeRefreshUsed: false, firstBuyUsed: false,
        bonusGoldApplied: false, trackedLastUpgradeTurn: 1,
        pendingStaleBuyCharges: 0,
    };
}

function sameShopEntitySet(a, b) {
    if (!a || !b || a.length === 0 || b.length === 0) return false;
    if (a.length !== b.length) return false;
    const aa = [...a].sort((x, y) => x - y), bb = [...b].sort((x, y) => x - y);
    return aa.every((v, i) => v === bb[i]);
}

function applyBuyCost(g, p, minionCost) {
    if ((p.freeCards || 0) > 0 && g.freeCardsUsedThisTurn < p.freeCards) {
        g.freeCardsUsedThisTurn++;
    } else if (p.firstBuyFree && !g.firstBuyUsed) {
        g.firstBuyUsed = true;
    } else {
        g.selfTrackedGold = Math.max(0, g.selfTrackedGold - minionCost);
    }
}

// p = { turn, shopCount, shopEntityIds, boardCount, handCount, tavernTier,
//       bonusGold, goldPerTurn, minionCost, freeRefresh, freeCards, firstBuyFree, hdtTag,
//       handGainedFromShop }
function advance(g, p) {
    const turn = p.turn;
    let maxGold = Math.min(10, 2 + turn);
    maxGold = Math.min(20, maxGold + (p.goldPerTurn || 0));
    const effBonus = g.bonusGoldApplied ? 0 : (p.bonusGold || 0);
    const maxGoldWithBonus = Math.min(20, maxGold + effBonus);

    const curShopIds = p.shopEntityIds || [];
    const observedCosts = p.observedPurchaseCosts || [];
    const observedPurchaseCount = observedCosts.length;
    const hasUnknownPurchaseCost = observedCosts.some(cost => cost < 0
        || cost === Number.MAX_SAFE_INTEGER);
    const exactPurchaseCosts = observedCosts.filter(cost => cost >= 0
        && cost !== Number.MAX_SAFE_INTEGER);

    if (turn !== g.selfTrackedTurn) {
        g.selfTrackedTurn = turn;
        g.prevShopCount = -1; g.prevBoardCount = -1; g.prevHandCount = -1; g.prevTavernTier = -1;
        g.prevShopEntityIds = [];
        g.pendingStaleBuyCharges = 0;
        let hdtBase = (p.hdtTag == null || p.hdtTag < 0 || p.hdtTag > 20) ? maxGold : p.hdtTag;
        if (hdtBase < maxGold) hdtBase = maxGold;
        g.selfTrackedGold = Math.max(0, Math.min(maxGoldWithBonus, hdtBase + effBonus));
        if (effBonus > 0) g.bonusGoldApplied = true;
        g.freeCardsUsedThisTurn = 0;
        g.freeRefreshUsed = false;
        g.firstBuyUsed = false;
    }

    const curShop = p.shopCount;
    const curBoard = p.boardCount;
    const curHand = p.handCount != null ? p.handCount : 0;
    const handGainedFromShop = p.handGainedFromShop || 0;
    const minionCost = p.minionCost != null ? p.minionCost : 3;

    // 刷新: 商店ID完全变化
    const shopRefreshed = g.prevShopEntityIds.length > 0 && curShopIds.length > 0
        && !curShopIds.some(id => g.prevShopEntityIds.includes(id));
    if (shopRefreshed) {
        g.pendingStaleBuyCharges = 0; // 整店换血, pending离店实体已不存在
        if ((p.freeRefresh || 0) > 0 && !g.freeRefreshUsed) {
            g.freeRefreshUsed = true;
        } else {
            g.selfTrackedGold = Math.max(0, g.selfTrackedGold - 1);
        }
    }

    if (observedPurchaseCount > 0) {
        for (const cost of exactPurchaseCosts)
            g.selfTrackedGold = Math.max(0, g.selfTrackedGold - cost);
        if (hasUnknownPurchaseCost) g.selfTrackedGold = 0;
        if (!shopRefreshed) g.pendingStaleBuyCharges += observedPurchaseCount;
    }

    // 购买: 商店数量减少 (先吸收stale路径已扣费的pending数, 防同一次购买双扣)
    if (g.prevShopCount >= 0 && curShop < g.prevShopCount) {
        const bought = g.prevShopCount - curShop;
        const absorbed = observedPurchaseCount > 0
            ? bought : Math.min(bought, g.pendingStaleBuyCharges);
        g.pendingStaleBuyCharges = Math.max(0, g.pendingStaleBuyCharges - absorbed);
        for (let i = 0; i < bought - absorbed && observedPurchaseCount === 0; i++)
            applyBuyCost(g, p, minionCost);
    } else if (observedPurchaseCount === 0
        && g.prevShopCount >= 0 && g.prevHandCount >= 0 && g.prevBoardCount >= 0
        && curShop === g.prevShopCount
        && curBoard === g.prevBoardCount
        && curHand > g.prevHandCount
        && handGainedFromShop > 0
        && sameShopEntitySet(curShopIds, g.prevShopEntityIds)) {
        // stale-shop buy: 只有新增手牌实体确实来自上帧商店集合时才算购买(07062107)
        const bought = Math.min(handGainedFromShop, Math.max(1, curShop));
        for (let i = 0; i < bought; i++)
            applyBuyCost(g, p, minionCost);
        g.pendingStaleBuyCharges += bought;
    }

    // 售出: 板面减少 → +1每张
    if (g.prevBoardCount >= 0 && curBoard < g.prevBoardCount)
        g.selfTrackedGold = Math.min(maxGoldWithBonus, g.selfTrackedGold + (g.prevBoardCount - curBoard));

    // 升本: 用自维护的 trackedLastUpgradeTurn 算折扣
    if (g.prevTavernTier >= 1 && p.tavernTier > g.prevTavernTier) {
        g.selfTrackedGold = Math.max(0,
            g.selfTrackedGold - getUpgradeCost(g.prevTavernTier, turn, g.trackedLastUpgradeTurn));
        g.trackedLastUpgradeTurn = turn;
    }

    // 07062107: 回合内不做HDT自愈校准 — HDT RESOURCES 在招募阶段保持回合起始满金不减,
    // 校准会把正确的self(花过钱)改成错误的满金值。仅回合切换时校准。

    g.prevShopCount = curShop;
    g.prevBoardCount = curBoard;
    g.prevHandCount = curHand;
    g.prevTavernTier = p.tavernTier;
    g.prevShopEntityIds = curShopIds;

    return Math.max(0, Math.min(maxGoldWithBonus, g.selfTrackedGold));
}

// 便捷: 构造一帧参数(默认无畸变)
function frame(turn, shop, ids, board, tier, extra) {
    return Object.assign({
        turn, shopCount: shop, shopEntityIds: ids, boardCount: board, handCount: 0, tavernTier: tier,
        bonusGold: 0, goldPerTurn: 0, minionCost: 3, freeRefresh: 0, freeCards: 0,
        firstBuyFree: false, hdtTag: -1,
    }, extra || {});
}

// ── 测试框架 ──
let passed = 0, failed = 0;
const diag = [];
function test(name, fn) {
    try {
        const r = fn();
        if (r === true || r === undefined) { passed++; diag.push("✅ " + name); }
        else { failed++; diag.push("❌ " + name + " — " + r); }
    } catch (e) {
        failed++; diag.push("❌ " + name + " — 异常: " + e.message);
    }
}

// ============================================================================
// 测试用例
// ============================================================================

test("[GT] maxGold 公式 Min(10,2+turn): T1=3 T2=4 T6=8 T8=10 T9封顶10", () => {
    const cases = [[1, 3], [2, 4], [6, 8], [8, 10], [9, 10], [12, 10]];
    for (const [turn, exp] of cases) {
        const g = newTracker();
        const gold = advance(g, frame(turn, 5, [1, 2, 3, 4, 5], 0, 1));
        if (gold !== exp) return `T${turn} 应=${exp}, 实际 ${gold}`;
    }
});

test("[GT] 买随从扣 3: T5 起步7金, 买2张 → 1", () => {
    const g = newTracker();
    advance(g, frame(5, 4, [10, 11, 12, 13], 0, 1));   // 新回合 T5 maxGold=7
    advance(g, frame(5, 3, [11, 12, 13], 1, 1));       // 买1 → -3 = 4
    const gold = advance(g, frame(5, 2, [12, 13], 2, 1)); // 买1 → -3 = 1
    if (gold !== 1) return "应=1, 实际 " + gold;
});

test("[GT] 卖随从回 +1", () => {
    const g = newTracker();
    advance(g, frame(5, 4, [10, 11, 12, 13], 3, 1));    // 7金, 3随从
    advance(g, frame(5, 3, [11, 12, 13], 4, 1));        // 买1 → 4金, board 3→4
    const gold = advance(g, frame(5, 3, [11, 12, 13], 3, 1)); // 卖1 board 4→3 → +1 = 5
    if (gold !== 5) return "卖后应=5, 实际 " + gold;
});

test("[GT] 刷新扣 1: 商店ID完全变化", () => {
    const g = newTracker();
    advance(g, frame(5, 4, [10, 11, 12, 13], 0, 1));    // 7金
    const gold = advance(g, frame(5, 4, [20, 21, 22, 23], 0, 1)); // 全新ID → 刷新 -1 = 6
    if (gold !== 6) return "刷新后应=6, 实际 " + gold;
});

test("[GT] 免费刷新畸变: 首次刷新不扣", () => {
    const g = newTracker();
    advance(g, frame(5, 4, [10, 11, 12, 13], 0, 1, { freeRefresh: 1 }));   // 7金
    const g1 = advance(g, frame(5, 4, [20, 21, 22, 23], 0, 1, { freeRefresh: 1 })); // 免费刷新 = 7
    if (g1 !== 7) return "免费刷新后应=7, 实际 " + g1;
    const g2 = advance(g, frame(5, 4, [30, 31, 32, 33], 0, 1, { freeRefresh: 1 })); // 第二次扣1 = 6
    if (g2 !== 6) return "第二次刷新应扣1=6, 实际 " + g2;
});

test("[GT] 升本折扣回归(bug2): 自维护 lastUpgradeTurn, 不依赖外部写回", () => {
    // T2 升 T3: lastUpgradeTurn 默认1, turn=2 → 折扣=1, cost=7-1=6
    const g = newTracker();
    advance(g, frame(2, 4, [1, 2, 3, 4], 0, 2));        // T2 起步4金, tier=2
    const after = advance(g, frame(2, 4, [1, 2, 3, 4], 0, 3)); // tier 2→3, cost=7-(2-1)=6 → 4-6=0
    if (after !== 0) return "T2升T3扣6 → 0, 实际 " + after;
    // 关键: GoldTracker 自维护 trackedLastUpgradeTurn=2 (不读外部 state.LastUpgradeTurn)
    if (g.trackedLastUpgradeTurn !== 2) return "升本后 trackedLastUpgradeTurn 应=2, 实际 " + g.trackedLastUpgradeTurn;
});

test("[GT] 升本折扣随回合递增: T3停留到T5再升, 折扣更大", () => {
    const g = newTracker();
    advance(g, frame(3, 4, [1, 2, 3, 4], 0, 2));        // T3 tier=2
    advance(g, frame(3, 4, [1, 2, 3, 4], 0, 3));        // T3 升 T3 (tier2→3), lastUpgrade=3
    // 停到 T5 再升 T3→T4: 折扣 = 5-3 = 2, cost = 9-2 = 7
    advance(g, frame(5, 4, [5, 6, 7, 8], 0, 3));        // T5 新回合 maxGold=7, tier 仍3
    const after = advance(g, frame(5, 4, [5, 6, 7, 8], 0, 4)); // tier3→4, cost=9-2=7 → 7-7=0
    if (after !== 0) return "T5升T4扣7 → 0, 实际 " + after;
});

test("[GT] bonusGold 回归(bug1): 金钱大战开局 +10, 只加一次", () => {
    const g = newTracker();
    // T1 金钱大战: maxGold=3, +10 → 13(封顶min(20,3+10)=13), 起步基准 max(3)+10=13
    const t1 = advance(g, frame(1, 5, [1, 2, 3, 4, 5], 0, 1, { bonusGold: 10 }));
    if (t1 !== 13) return "金钱大战T1应=13, 实际 " + t1;
    if (!g.bonusGoldApplied) return "bonusGoldApplied 应置位";
    // T2: 不再重复加(铸币不跨回合, +10只在开局), maxGold=4
    const t2 = advance(g, frame(2, 5, [1, 2, 3, 4, 5], 0, 1, { bonusGold: 10 }));
    if (t2 !== 4) return "T2不应重复加bonus, 应=4, 实际 " + t2;
});

test("[GT] firstBuyFree 畸变: 首次购买不扣费", () => {
    const g = newTracker();
    advance(g, frame(5, 4, [1, 2, 3, 4], 0, 1, { firstBuyFree: true }));    // T5 7金
    const g1 = advance(g, frame(5, 3, [2, 3, 4], 1, 1, { firstBuyFree: true })); // 首购免费 = 7
    if (g1 !== 7) return "首购免费后应=7, 实际 " + g1;
    const g2 = advance(g, frame(5, 2, [3, 4], 2, 1, { firstBuyFree: true }));    // 第二购扣3 = 4
    if (g2 !== 4) return "第二购应扣3=4, 实际 " + g2;
});

test("[GT] freeCards 畸变: 前N次购买免费", () => {
    const g = newTracker();
    advance(g, frame(5, 5, [1, 2, 3, 4, 5], 0, 1, { freeCards: 2 }));   // 7金
    advance(g, frame(5, 4, [2, 3, 4, 5], 1, 1, { freeCards: 2 }));      // 免费1
    const g2 = advance(g, frame(5, 3, [3, 4, 5], 2, 1, { freeCards: 2 })); // 免费2 = 7
    if (g2 !== 7) return "2张免费后应=7, 实际 " + g2;
    const g3 = advance(g, frame(5, 2, [4, 5], 3, 1, { freeCards: 2 }));  // 第三张扣3 = 4
    if (g3 !== 4) return "第三购应扣3=4, 实际 " + g3;
});

test("[GT] 回合切换重置免费额度", () => {
    const g = newTracker();
    advance(g, frame(4, 4, [1, 2, 3, 4], 0, 1, { firstBuyFree: true }));
    advance(g, frame(4, 3, [2, 3, 4], 1, 1, { firstBuyFree: true }));   // 用掉首购
    // 新回合: 首购额度重置
    advance(g, frame(5, 4, [5, 6, 7, 8], 1, 1, { firstBuyFree: true })); // T5 7金
    const g1 = advance(g, frame(5, 3, [6, 7, 8], 2, 1, { firstBuyFree: true })); // 新回合首购免费 = 7
    if (g1 !== 7) return "新回合首购应重置免费=7, 实际 " + g1;
});

test("[GT] Reset 清空所有状态", () => {
    const g = newTracker();
    advance(g, frame(5, 4, [1, 2, 3, 4], 0, 3, { bonusGold: 10 }));
    // 模拟 Reset
    const fresh = newTracker();
    Object.assign(g, fresh);
    if (g.selfTrackedGold !== -1 || g.bonusGoldApplied !== false || g.trackedLastUpgradeTurn !== 1)
        return "Reset 未清空: gold=" + g.selfTrackedGold + " bonusApplied=" + g.bonusGoldApplied;
});

test("[GT] gold 不跌破 0: 连续购买夹紧", () => {
    const g = newTracker();
    advance(g, frame(3, 5, [1, 2, 3, 4, 5], 0, 1));    // T3 5金
    advance(g, frame(3, 4, [2, 3, 4, 5], 1, 1));       // -3 = 2
    const gold = advance(g, frame(3, 3, [3, 4, 5], 2, 1)); // -3 → max(0,-1)=0
    if (gold !== 0) return "应夹紧到0, 实际 " + gold;
});

// ── 07061713 回归: stale-buy 双扣 / 升本双扣 / 回合内自愈 ──

test("[GT-07061713] stale-buy 不双扣: 动画期hand+1扣费后, 商店数随后减少不再扣", () => {
    const g = newTracker();
    advance(g, frame(5, 4, [10, 11, 12, 13], 0, 1, { handCount: 0 }));  // T5 7金
    // 动画窗口: 商店集合不变, hand 0→1 且新手牌实体来自商店 → stale-buy 扣3 = 4
    const g1 = advance(g, frame(5, 4, [10, 11, 12, 13], 0, 1, { handCount: 1, handGainedFromShop: 1 }));
    if (g1 !== 4) return "stale-buy 后应=4, 实际 " + g1;
    // 动画结束: 商店数 4→3 (同一次购买的实体真正离店) → 不应再扣
    const g2 = advance(g, frame(5, 3, [11, 12, 13], 0, 1, { handCount: 1 }));
    if (g2 !== 4) return "动画结束后不应二次扣费, 应=4, 实际 " + g2;
});

test("[GT-07061713] 正常购买(无stale窗口)仍扣费: pending不误吸收独立购买", () => {
    const g = newTracker();
    advance(g, frame(5, 4, [10, 11, 12, 13], 0, 1, { handCount: 0 }));  // 7金
    // stale-buy 1次 = 4, pending=1
    advance(g, frame(5, 4, [10, 11, 12, 13], 0, 1, { handCount: 1, handGainedFromShop: 1 }));
    // 动画结束吸收 pending: 4→3 不扣, pending=0
    advance(g, frame(5, 3, [11, 12, 13], 0, 1, { handCount: 1 }));
    // 再正常买一张(商店 3→2, 无stale前置) → 应扣3 = 1
    const gold = advance(g, frame(5, 2, [12, 13], 0, 1, { handCount: 1 }));
    if (gold !== 1) return "第二次独立购买应扣3=1, 实际 " + gold;
});

test("[GT-07061713] 刷新清pending: stale扣费后整店换血, 新店购买正常扣费", () => {
    const g = newTracker();
    advance(g, frame(6, 4, [10, 11, 12, 13], 0, 1, { handCount: 0 }));  // T6 8金
    advance(g, frame(6, 4, [10, 11, 12, 13], 0, 1, { handCount: 1, handGainedFromShop: 1 }));  // stale-buy -3 = 5, pending=1
    advance(g, frame(6, 4, [20, 21, 22, 23], 0, 1, { handCount: 1 }));  // 刷新 -1 = 4, pending清0
    const gold = advance(g, frame(6, 3, [21, 22, 23], 0, 1, { handCount: 1 })); // 新店购买 -3 = 1
    if (gold !== 1) return "刷新后购买应正常扣3=1, 实际 " + gold;
});

test("[GT-07062107] 非购买手牌增加(饰品/发现给牌)不扣费: 实体不来自商店", () => {
    const g = newTracker();
    advance(g, frame(9, 5, [1, 2, 3, 4, 5], 3, 4, { handCount: 2, hdtTag: 10 })); // T9 10金
    // 饰品给牌: hand 2→3, 商店/板面不变, 新实体不来自商店 → 不扣费
    const gold = advance(g, frame(9, 5, [1, 2, 3, 4, 5], 3, 4, { handCount: 3, handGainedFromShop: 0, hdtTag: 10 }));
    if (gold !== 10) return "饰品给牌不应扣费, 应=10, 实际 " + gold;
});

test("[GT-07062107] 回合内不做HDT自愈: 升本后self=0正确值不被滞后HDT拉回", () => {
    // HDT RESOURCES 在招募阶段保持回合起始满金不减(07062107实测: 升本后HDT仍=4)
    const g = newTracker();
    advance(g, frame(2, 3, [1, 2, 3], 0, 1, { hdtTag: 0 }));   // T2 4金(HDT滞后0, 用maxGold)
    // 升本 T1→T2: cost=5-(2-1)=4 → self=0 (正确)
    advance(g, frame(2, 3, [1, 2, 3], 0, 2, { hdtTag: 4 }));
    // 之后HDT一直报4(滞后满金), 连续多帧稳定 — self必须保持0, 不得被"自愈"拉回4
    advance(g, frame(2, 3, [1, 2, 3], 0, 2, { hdtTag: 4 }));
    advance(g, frame(2, 3, [1, 2, 3], 0, 2, { hdtTag: 4 }));
    advance(g, frame(2, 3, [1, 2, 3], 0, 2, { hdtTag: 4 }));
    const gold = advance(g, frame(2, 3, [1, 2, 3], 0, 2, { hdtTag: 4 }));
    if (gold !== 0) return "升本后self=0不得被滞后HDT改写, 实际 " + gold;
});

test("[GT-07061713] 升本只扣一次(GoldTracker内部), 无外部二次扣费叠加", () => {
    // C# 侧曾在 GameStateExtractor.TrackGold 对 Advance 返回值二次扣升本费(已删)。
    // 镜像锁: Advance 内部升本扣费后, 结果即最终值。
    const g = newTracker();
    advance(g, frame(4, 4, [1, 2, 3, 4], 0, 1));        // T4 6金 tier=1
    const after = advance(g, frame(4, 4, [1, 2, 3, 4], 0, 2)); // 升T2: cost=5-(4-1)=2 → 6-2=4
    if (after !== 4) return "T4升T2应扣2=4(只扣一次), 实际 " + after;
});

test("[5B.3H] 已观察购买费用未知时本回合金币保守关闭为0", () => {
    const g = newTracker();
    advance(g, frame(3, 2, [90, 91], 0, 1, { handCount: 0 }));
    const gold = advance(g, frame(3, 2, [90, 91], 0, 1, {
        handCount: 1,
        handGainedFromShop: 1,
        observedPurchaseCosts: [-1],
    }));
    if (gold !== 0) return "未知费用购买后应=0, 实际 " + gold;
    const recovered = advance(g, frame(4, 2, [100, 101], 0, 1, {
        handCount: 1, hdtTag: 6,
    }));
    if (recovered !== 6) return "下一回合应恢复新基准6, 实际 " + recovered;
});

// ============================================================================
// Summary
// ============================================================================
console.log("\n=== GoldTracker 金币自追踪逻辑测试 ===");
console.log(`通过: ${passed}  失败: ${failed}`);
diag.forEach(l => console.log(l));

if (failed > 0) {
    console.log(`\n${failed} 项失败 — 修复后再提交`);
    process.exit(1);
} else {
    console.log(`\n全部 ${passed} 项通过`);
    process.exit(0);
}
