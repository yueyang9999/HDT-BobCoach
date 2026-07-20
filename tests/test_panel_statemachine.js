/**
 * PanelState 状态机逻辑测试 (C2)
 *
 * 用 JS 镜像 C# PanelStateMachine.Advance 的状态转换表, 驱动序列验证不变量:
 *   - offer 出现 → Active
 *   - offer 消失 → Fading → (滞回到时) → Expired → Idle
 *   - "选完饰品"序列: offer 2→0 后, ≤滞回时长内必到 Expired ("消不掉"回归测试)
 *   - 闪烁测试: offer 在 Active 期间抖动 1 帧, 不得离开 Active (滞回生效)
 *
 * 注: JS 镜像须与 C# Core/PanelStateMachine.cs 手工对齐。
 * test_ui_lifecycle.js 的 [StateMachine] 静态断言负责锁住 C# 侧签名(ref/字段/调用顺序)。
 */

"use strict";

const IDLE = "Idle", ACTIVE = "Active", FADING = "Fading", EXPIRED = "Expired";
const MAX_ACTIVE_MS = 8000; // C# MaxActiveTicks = 8s

// ── JS 镜像: PanelStateMachine.Advance ──
// ps = { phase, phaseEnteredMs, createdTurn, hysteresisMs }
// 用 nowMs 注入时间, 避免依赖真实时钟
function advance(ps, active, turn, enforceMaxActive, nowMs) {
    switch (ps.phase) {
        case IDLE:
            if (active) { transition(ps, ACTIVE, nowMs); ps.createdTurn = turn; }
            break;
        case ACTIVE:
            if (!active) {
                transition(ps, FADING, nowMs);
            } else if (enforceMaxActive && (nowMs - ps.phaseEnteredMs) > MAX_ACTIVE_MS) {
                transition(ps, FADING, nowMs);
            }
            break;
        case FADING:
            if (active) {
                transition(ps, ACTIVE, nowMs);
            } else if ((nowMs - ps.phaseEnteredMs) >= ps.hysteresisMs) {
                transition(ps, EXPIRED, nowMs);
            }
            break;
        case EXPIRED:
            // Expired 自闭环: 下一帧回 Idle, 并立即据 active 决定是否重入 Active
            // (与 C# Core/PanelStateMachine.cs Expired 分支对齐)
            transition(ps, IDLE, nowMs);
            if (active) { transition(ps, ACTIVE, nowMs); ps.createdTurn = turn; }
            break;
    }
    return ps;
}

function transition(ps, newPhase, nowMs) {
    ps.phase = newPhase;
    ps.phaseEnteredMs = nowMs;
}

function newPanel(hysteresisMs, nowMs) {
    return { phase: IDLE, phaseEnteredMs: nowMs, createdTurn: 1, hysteresisMs };
}

function isVisible(ps) {
    return ps.phase === ACTIVE || ps.phase === FADING;
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

test("[SM] offer 出现 → Idle 转 Active", () => {
    let t = 0;
    const ps = newPanel(1500, t);
    advance(ps, true, 5, false, t);
    if (ps.phase !== ACTIVE) return "应进入 Active, 实际 " + ps.phase;
    if (ps.createdTurn !== 5) return "createdTurn 未记录";
});

test("[SM] offer 消失 → Active 转 Fading", () => {
    let t = 0;
    const ps = newPanel(1500, t);
    advance(ps, true, 5, false, t);   // → Active
    t += 100;
    advance(ps, false, 5, false, t);  // offer 消失 → Fading
    if (ps.phase !== FADING) return "应进入 Fading, 实际 " + ps.phase;
});

test("[SM] Fading 期间内容重现 → 回 Active (不闪)", () => {
    let t = 0;
    const ps = newPanel(1500, t);
    advance(ps, true, 5, false, t);
    t += 100; advance(ps, false, 5, false, t);  // → Fading
    t += 200; advance(ps, true, 5, false, t);   // 抖动恢复 → Active
    if (ps.phase !== ACTIVE) return "抖动恢复应回 Active, 实际 " + ps.phase;
});

test("[SM] 闪烁测试: Active 期间抖动 1 帧不离开 Active", () => {
    let t = 0;
    const ps = newPanel(1500, t);
    advance(ps, true, 5, false, t);
    // 单帧抖动: false 1 帧 → 立即 true
    t += 50; advance(ps, false, 5, false, t);  // 进 Fading
    t += 10; advance(ps, true, 5, false, t);   // 立即回 Active
    if (ps.phase !== ACTIVE) return "单帧抖动后应在 Active(滞回吸收), 实际 " + ps.phase;
    if (!isVisible(ps)) return "抖动期间面板不应隐藏";
});

test("[SM] 选完饰品: offer 2→0 后滞回到时必到 Expired (消不掉回归)", () => {
    let t = 0;
    const ps = newPanel(1500, t);
    advance(ps, true, 5, false, t);          // 显示饰品
    t += 500; advance(ps, false, 5, false, t); // 选完, offer 消失 → Fading
    // 滞回 1500ms, 推进到 1501ms 后
    t += 1501; advance(ps, false, 5, false, t);
    if (ps.phase !== EXPIRED) return "滞回到时应 Expired, 实际 " + ps.phase;
});

test("[SM] Fading 内未到滞回时长仍 Fading (不提前消)", () => {
    let t = 0;
    const ps = newPanel(1500, t);
    advance(ps, true, 5, false, t);
    t += 100; advance(ps, false, 5, false, t);  // → Fading
    t += 1000; advance(ps, false, 5, false, t); // 仅过 1000ms < 1500ms
    if (ps.phase !== FADING) return "未到滞回时长应仍 Fading, 实际 " + ps.phase;
    if (!isVisible(ps)) return "Fading 期间应仍可见";
});

test("[SM] Expired 自闭环: 下一帧自动回 Idle (无需消费方写回)", () => {
    let t = 0;
    const ps = newPanel(1500, t);
    advance(ps, true, 5, false, t);
    t += 100; advance(ps, false, 5, false, t);
    t += 1501; advance(ps, false, 5, false, t); // → Expired
    if (ps.phase !== EXPIRED) return "前置: 应到 Expired";
    // 关键回归: 即使消费方(DispatchRender)被早返回跳过、从不写回,
    // 下一帧 Advance 也必须自动把 Expired 推回 Idle, 否则状态机永久卡死
    t += 100; advance(ps, false, 5, false, t);
    if (ps.phase !== IDLE) return "Expired 下一帧应自动回 Idle, 实际 " + ps.phase;
});

test("[SM] Expired 帧若 offer 重现 → 直接重入 Active (不丢一帧)", () => {
    let t = 0;
    const ps = newPanel(1500, t);
    advance(ps, true, 5, false, t);
    t += 100; advance(ps, false, 5, false, t);
    t += 1501; advance(ps, false, 5, false, t); // → Expired
    // Expired 那帧 offer 又出现(新一轮饰品): 应直接回 Active, 不必先空一帧 Idle
    t += 50; advance(ps, true, 7, false, t);
    if (ps.phase !== ACTIVE) return "Expired+offer 应重入 Active, 实际 " + ps.phase;
    if (ps.createdTurn !== 7) return "重入应记录新 createdTurn";
});

test("[SM] 卡死回归: 进入战斗(消费方全程早返回) 不影响状态机自恢复", () => {
    let t = 0;
    const ps = newPanel(1500, t);
    advance(ps, true, 5, false, t);          // 显示
    t += 100; advance(ps, false, 5, false, t); // 选完 → Fading
    t += 1501; advance(ps, false, 5, false, t); // → Expired (此帧进战斗, 消费方早返回, 不写回)
    t += 100; advance(ps, false, 5, false, t);  // 战斗中 offer=false → Idle
    // 下一局/回合 offer 重现
    t += 5000; advance(ps, true, 6, false, t);
    if (ps.phase !== ACTIVE) return "状态机应能自恢复显示, 实际 " + ps.phase;
});

test("[SM] 完整生命周期: Idle→Active→Fading→Expired→Idle (自闭环)", () => {
    let t = 0;
    const ps = newPanel(1500, t);
    const trail = [ps.phase];
    advance(ps, true, 5, false, t); trail.push(ps.phase);            // Active
    t += 100; advance(ps, false, 5, false, t); trail.push(ps.phase); // Fading
    t += 1501; advance(ps, false, 5, false, t); trail.push(ps.phase); // Expired
    t += 100; advance(ps, false, 5, false, t); trail.push(ps.phase);  // Idle (自闭环)
    const expected = [IDLE, ACTIVE, FADING, EXPIRED, IDLE];
    if (JSON.stringify(trail) !== JSON.stringify(expected))
        return "生命周期路径错误: " + trail.join("→");
});

test("[SM] 发现面板最大驻留兜底: Active 超 8s → Fading", () => {
    let t = 0;
    const ps = newPanel(1000, t);
    advance(ps, true, 5, true, t);  // enforceMaxActive=true (发现面板)
    // offer 一直存在(zone6 残留实体), 但超过 8s
    t += 8001; advance(ps, true, 5, true, t);
    if (ps.phase !== FADING) return "最大驻留超时应 Fading, 实际 " + ps.phase;
});

test("[SM] 饰品面板最大驻留兜底: Active 超 8s → Fading", () => {
    let t = 0;
    const ps = newPanel(1500, t);
    advance(ps, true, 5, true, t);  // enforceMaxActive=true (饰品面板)
    t += 8001; advance(ps, true, 5, true, t);
    if (ps.phase !== FADING) return "饰品最大驻留超时应 Fading, 实际 " + ps.phase;
});

// ============================================================================
// Summary
// ============================================================================
console.log("\n=== PanelState 状态机逻辑测试 ===");
console.log(`通过: ${passed}  失败: ${failed}`);
diag.forEach(l => console.log(l));

if (failed > 0) {
    console.log(`\n${failed} 项失败 — 修复后再提交`);
    process.exit(1);
} else {
    console.log(`\n全部 ${passed} 项通过`);
    process.exit(0);
}
