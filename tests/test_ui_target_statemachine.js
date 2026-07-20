"use strict";

const SEARCHING = "Searching";
const CANDIDATE = "Candidate";
const STABLE = "Stable";
const BOUND = "Bound";
const LOST = "Lost";
const EXPIRED = "Expired";

function newMachine(stableMs = 120, lostMs = 1000, stableFrames = 2) {
    return {
        stableMs,
        lostMs,
        stableFrames,
        state: {
            phase: SEARCHING,
            type: null,
            source: "None",
            batchId: "",
            turn: 0,
            entityIds: [],
            firstSeenMs: 0,
            lastSeenMs: 0,
            lostAtMs: 0,
            confidence: 0,
            expireReason: "None",
            stableFrames: 0,
        },
        completedBatchId: "",
        completedType: null,
        completedTurn: 0,
    };
}

function snapshot(type, source, batchId, turn, ids, confidence = 0.8) {
    return {
        type,
        source,
        batchId,
        turn,
        entityIds: ids || [],
        optionCount: ids ? ids.length : 0,
        confidence,
        valid: !!batchId && ids && ids.length >= 2,
    };
}

function sameIds(a, b) {
    const aa = (a || []).slice().sort((x, y) => x - y);
    const bb = (b || []).slice().sort((x, y) => x - y);
    return aa.length === bb.length && aa.every((v, i) => v === bb[i]);
}

function sameBatch(state, snap) {
    return state.type === snap.type
        && state.turn === snap.turn
        && state.batchId === snap.batchId
        && sameIds(state.entityIds, snap.entityIds);
}

function isStableEnough(machine, snap, nowMs) {
    if (snap.source === "PowerLog" && snap.confidence >= 0.95) return true;
    return machine.state.stableFrames >= machine.stableFrames
        || (nowMs - machine.state.firstSeenMs) >= machine.stableMs;
}

function startCandidate(machine, snap, nowMs) {
    if (!isCompletedBatch(machine, snap)) {
        machine.completedBatchId = "";
        machine.completedTurn = 0;
    }
    machine.state = {
        phase: CANDIDATE,
        type: snap.type,
        source: snap.source,
        batchId: snap.batchId,
        turn: snap.turn,
        entityIds: snap.entityIds.slice(),
        firstSeenMs: nowMs,
        lastSeenMs: nowMs,
        lostAtMs: 0,
        confidence: snap.confidence,
        expireReason: "None",
        stableFrames: 1,
    };
    if (isStableEnough(machine, snap, nowMs)) machine.state.phase = STABLE;
}

function expire(machine, reason, nowMs) {
    machine.state.phase = EXPIRED;
    machine.state.lastSeenMs = nowMs;
    machine.state.expireReason = reason;
}

function advance(machine, snap, nowMs) {
    const s = machine.state;
    if (!snap || !snap.valid) {
        if (s.phase === CANDIDATE) expire(machine, "InvalidBatch", nowMs);
        else if (s.phase === STABLE || s.phase === BOUND) {
            s.phase = LOST;
            s.lostAtMs = nowMs;
            s.expireReason = "MissingSignal";
        } else if (s.phase === LOST && nowMs - s.lostAtMs >= machine.lostMs) {
            expire(machine, "Timeout", nowMs);
        } else if (s.phase === EXPIRED) {
            machine.state = newMachine(machine.stableMs, machine.lostMs, machine.stableFrames).state;
        }
        return machine.state;
    }

    if (isCompletedBatch(machine, snap)) {
        expire(machine, "ChoiceCompleted", nowMs);
        return machine.state;
    }

    if (s.phase === SEARCHING || s.phase === EXPIRED || !sameBatch(s, snap)) {
        startCandidate(machine, snap, nowMs);
        return machine.state;
    }

    s.lastSeenMs = nowMs;
    s.confidence = Math.max(s.confidence, snap.confidence);
    s.stableFrames++;
    s.expireReason = "None";
    if (s.phase === CANDIDATE && isStableEnough(machine, snap, nowMs)) s.phase = STABLE;
    else if (s.phase === STABLE) s.phase = BOUND;
    else if (s.phase === LOST) {
        s.phase = BOUND;
        s.lostAtMs = 0;
    }
    return s;
}

function completeChoice(machine, nowMs) {
    if (machine.state.batchId) {
        machine.completedBatchId = machine.state.batchId;
        machine.completedType = machine.state.type;
        machine.completedTurn = machine.state.turn;
    }
    expire(machine, "ChoiceCompleted", nowMs);
}

function isCompletedBatch(machine, snap) {
    return !!machine.completedBatchId
        && machine.completedType === snap.type
        && machine.completedTurn === snap.turn
        && machine.completedBatchId === snap.batchId;
}

let passed = 0;
let failed = 0;
const diag = [];

function test(name, fn) {
    try {
        const result = fn();
        if (result === true || result === undefined) {
            passed++;
            diag.push("PASS " + name);
        } else {
            failed++;
            diag.push("FAIL " + name + ": " + result);
        }
    } catch (e) {
        failed++;
        diag.push("FAIL " + name + ": " + e.message);
    }
}

test("[TargetSM] zone6 candidate needs a stable second frame", () => {
    const sm = newMachine();
    let t = 0;
    advance(sm, snapshot("Discover", "Zone6", "D|7|a,b", 7, [11, 12], 0.75), t);
    if (sm.state.phase !== CANDIDATE) return "expected Candidate, got " + sm.state.phase;
    t += 50;
    advance(sm, snapshot("Discover", "Zone6", "D|7|a,b", 7, [11, 12], 0.75), t);
    if (sm.state.phase !== STABLE) return "expected Stable after repeated batch, got " + sm.state.phase;
});

test("[TargetSM] Power.log batch can confirm immediately", () => {
    const sm = newMachine();
    advance(sm, snapshot("Discover", "PowerLog", "D|8|x,y,z", 8, [21, 22, 23], 0.98), 0);
    if (sm.state.phase !== STABLE) return "expected Stable, got " + sm.state.phase;
});

test("[TargetSM] confirmed batch enters Lost before Expired", () => {
    const sm = newMachine();
    advance(sm, snapshot("Trinket", "PowerLog", "T|6|a,b", 6, [31, 32], 0.98), 0);
    advance(sm, null, 100);
    if (sm.state.phase !== LOST) return "expected Lost, got " + sm.state.phase;
    advance(sm, null, 900);
    if (sm.state.phase !== LOST) return "lost hysteresis expired too early";
    advance(sm, null, 1101);
    if (sm.state.phase !== EXPIRED) return "expected Expired, got " + sm.state.phase;
});

test("[TargetSM] same batch recovering during Lost rebinds", () => {
    const sm = newMachine();
    const snap = snapshot("Discover", "PowerLog", "D|9|a,b", 9, [41, 42], 0.98);
    advance(sm, snap, 0);
    advance(sm, null, 100);
    advance(sm, snap, 200);
    if (sm.state.phase !== BOUND) return "expected Bound, got " + sm.state.phase;
});

test("[TargetSM] new batch replaces old batch", () => {
    const sm = newMachine();
    advance(sm, snapshot("Discover", "PowerLog", "D|9|a,b", 9, [41, 42], 0.98), 0);
    advance(sm, snapshot("Discover", "PowerLog", "D|9|c,d", 9, [43, 44], 0.98), 50);
    if (sm.state.batchId !== "D|9|c,d") return "old batch retained";
    if (sm.state.phase !== STABLE) return "new high-confidence batch should be Stable";
});

test("[TargetSM] invalid discover count does not bind", () => {
    const sm = newMachine();
    advance(sm, snapshot("Discover", "PowerLog", "D|10|single", 10, [51], 0.98), 0);
    if (sm.state.phase !== SEARCHING) return "single option should not leave Searching";
});

test("[TargetSM] choice completion blocks same stale batch until a new batch arrives", () => {
    const sm = newMachine();
    const stale = snapshot("Trinket", "PowerLog", "T|6|a,b", 6, [61, 62], 0.98);
    advance(sm, stale, 0);
    completeChoice(sm, 10);
    if (sm.state.phase !== EXPIRED || sm.state.expireReason !== "ChoiceCompleted")
        return "choice completion did not expire target";
    advance(sm, null, 20);
    if (sm.state.phase !== SEARCHING) return "expired target should close to Searching";
    advance(sm, stale, 25);
    if (sm.state.phase !== EXPIRED) return "same completed batch rebound";
    advance(sm, snapshot("Trinket", "PowerLog", "T|9|c,d", 9, [63, 64], 0.98), 30);
    if (sm.state.batchId !== "T|9|c,d" || sm.state.phase !== STABLE)
        return "new trinket batch did not bind";
});

console.log("\n=== UiTargetStateMachine mirror tests ===");
console.log(`passed: ${passed} failed: ${failed}`);
diag.forEach(line => console.log(line));

if (failed > 0) process.exit(1);
