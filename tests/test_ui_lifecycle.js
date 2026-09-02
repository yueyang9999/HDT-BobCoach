/**
 * UI 生命周期测试护栏 v1
 *
 * 验证 OverlayRenderer + BobCoachPlugin 的渲染管线规则。
 * 不编译C#，直接对源码做静态验证 + 对JS可模拟部分做逻辑测试。
 *
 * 覆盖:
 *   [1] 战斗守卫 — 13个 public Show* 方法第一行必是 if (IsCombat()) return;
 *   [2] Clear 规范 — Show*+DispatchRender 的 Clear 调用模式
 *   [3] 布局初始化 — GameLayoutCalculator 构造时调用 SetTier(1)
 *   [4] 面板生命周期 — 饰品/发现 panels 完整状态路径
 *   [5] 空商店守卫 — ShopMinions.Count==0 → ClearSuggestions
 */

"use strict";

const fs = require("fs");
const path = require("path");

const ROOT = path.resolve(__dirname, "..");
// __dirname = bob-coach/test/  →  ROOT = bob-coach/
// C# 源码路径: src/BobCoach/...  (从 ROOT 算)
function readFile(rel) {
    // .cs 源码按 .gitattributes 以 CRLF 落盘; 断言只关心结构, 统一归一化为 LF 避免行尾误报
    const raw = fs.readFileSync(path.join(ROOT, rel), "utf-8");
    return rel.endsWith(".cs") ? raw.replace(/\r\n/g, "\n") : raw;
}

function countOccurrences(text, pattern) {
    const re = typeof pattern === "string" ? new RegExp(pattern.replace(/[.*+?^${}()|[\]\\]/g, "\\$&"), "g") : pattern;
    return (text.match(re) || []).length;
}

function findShowMethods(source) {
    // Extract all "public void ShowXxx(...)" method names
    const re = /public void (Show\w+)\(/g;
    const names = [];
    let m;
    while ((m = re.exec(source)) !== null) names.push(m[1]);
    return names;
}

function getMethodFirstNonBlankLines(source, methodName, count) {
    // Find method body and return first N non-blank, non-comment lines
    const idx = source.indexOf("public void " + methodName + "(");
    if (idx < 0) return [];
    const after = source.slice(idx);
    const braceIdx = after.indexOf("{");
    if (braceIdx < 0) return [];
    const bodyLines = after.slice(braceIdx + 1).split("\n");
    const result = [];
    for (const line of bodyLines) {
        const trimmed = line.trim();
        if (trimmed && !trimmed.startsWith("//") && !trimmed.startsWith("/*")) {
            result.push(trimmed);
            if (result.length >= count) break;
        }
    }
    return result;
}

// ============================================================================
// Test runner
// ============================================================================

let passed = 0, failed = 0, warnings = 0;
const diag = [];

function test(name, fn) {
    try {
        const r = fn();
        if (r === true || r === undefined) {
            passed++;
            diag.push(`✅ ${name}`);
        } else {
            failed++;
            diag.push(`❌ ${name}: ${r}`);
        }
    } catch (e) {
        failed++;
        diag.push(`💥 ${name}: ${e.message}`);
    }
}

function warn(name, msg) {
    warnings++;
    diag.push(`⚠️  ${name}: ${msg}`);
}

// ============================================================================
// [1] 战斗守卫: 13 Show* 方法
// ============================================================================
{
    const overlaySrc = readFile("src/BobCoach/OverlayRenderer.cs");
    const showMethods = findShowMethods(overlaySrc);

    test("发现 " + showMethods.length + " 个 Show* 方法 (期望 ≥13)", () => {
        if (showMethods.length < 13) return `只有 ${showMethods.length} 个`;
    });

    for (const name of showMethods) {
        test(`[CombatGuard] ${name}: 有 IsCombat 守卫 (正文或委托)`, () => {
            const lines = getMethodFirstNonBlankLines(overlaySrc, name, 2);
            if (lines.length === 0) return "无法解析方法体";
            const combined = lines.join(" ");
            // ShowTargetPulse 委托到 ShowTargetPulses，后者有守卫
            if (name === "ShowTargetPulse" && combined.includes("ShowTargetPulses")) return;
            if (!combined.includes("IsCombat()")) return `前2行未找到守卫: ${lines[0]}`;
        });
    }
}

// ============================================================================
// [2] Clear 规范
// ============================================================================
{
    const overlaySrc = readFile("src/BobCoach/OverlayRenderer.cs");

    test("[StatusStrip] ShowStatusStrip 复用稳定根元素", () => {
        const idx = overlaySrc.indexOf("public void ShowStatusStrip");
        const block = overlaySrc.slice(idx, idx + 1600);
        if (!block.includes("EnsureStatusStripRoot")) return "状态面板未复用稳定根元素";
        if (block.includes('ClearTaggedElements("status_strip")'))
            return "状态刷新仍会删除根元素并中断拖动";
    });

    test("[ClearSpec] ShowLevelUpGlow 调用了 ClearTaggedElements", () => {
        const idx = overlaySrc.indexOf("public void ShowLevelUpGlow");
        const block = overlaySrc.slice(idx, idx + 500);
        if (!block.includes("ClearTaggedElements")) return "未找到 ClearTaggedElements 调用";
    });

    test("[ClearSpec] ShowRefreshGlow 调用了 ClearTaggedElements", () => {
        const idx = overlaySrc.indexOf("public void ShowRefreshGlow");
        const block = overlaySrc.slice(idx, idx + 500);
        if (!block.includes("ClearTaggedElements")) return "未找到 ClearTaggedElements 调用";
    });

    test("[ClearSpec] ShowFreezeGlow 调用了 ClearTaggedElements", () => {
        const idx = overlaySrc.indexOf("public void ShowFreezeGlow");
        const block = overlaySrc.slice(idx, idx + 500);
        if (!block.includes("ClearTaggedElements")) return "未找到 ClearTaggedElements 调用";
    });

    // ClearNonPanel 在 DispatchRender 回调内调用
    const pluginSrc = readFile("src/BobCoach/BobCoachPlugin.cs");
    test("[ClearSpec] DispatchRender 回调内调用 ClearNonPanel", () => {
        if (!pluginSrc.includes("ClearNonPanel")) return "BobCoachPlugin.cs 中未找到 ClearNonPanel 调用";
    });

    // 全局 Clear() 不在战斗阶段每帧调用
    test("[ClearSpec] 不应在 OnUpdate 内 per-frame Clear (已修复 3153→0)", () => {
        // Pass if we can't find patterns like "OnUpdate" + "Clear()" in the same method
        // 这个检查是启发式的
        const onUpdateMatch = pluginSrc.match(/void OnUpdate[^}]*\{([^}]*)\}/s);
        if (onUpdateMatch && onUpdateMatch[1].includes("_renderer.Clear()")) {
            warn("[ClearSpec] OnUpdate 仍包含 Clear 调用，检查是否仅战斗开始时清除", "");
        }
    });
}

// ============================================================================
// [3] 布局初始化: CardW > 0 保证
// ============================================================================
{
    const calcSrc = readFile("src/BobCoach/Core/GameLayoutCalculator.cs");

    test("[Layout] GameLayoutCalculator 构造时调用 SetTier(1)", () => {
        // Check constructor
        const ctorIdx = calcSrc.indexOf("public GameLayoutCalculator(");
        if (ctorIdx < 0) return "未找到构造函数";
        const afterCtor = calcSrc.slice(ctorIdx);
        const closeBrace = afterCtor.indexOf("{");
        const ctorBody = afterCtor.slice(closeBrace, afterCtor.indexOf("}", closeBrace + 500));
        if (!ctorBody.includes("SetTier(1)")) return "构造函数未调用 SetTier(1)";
    });

    test("[Layout] CardW 基于 _clientRect.Width + _activeCardWidthPct (非 ShopMinions)", () => {
        if (!calcSrc.includes("_activeCardWidthPct")) return "CardW 未使用 _activeCardWidthPct";
        const cardWLine = calcSrc.match(/public double CardW\s*=>\s*(.+);/);
        if (!cardWLine) return "无法解析 CardW 表达式";
        const expr = cardWLine[1];
        if (expr.includes("ShopMinions") || expr.includes("Count")) {
            warn("[Layout] CardW 表达式可能依赖 ShopMinions", `表达式: ${expr}`);
        }
    });

    test("[Layout] SetTier 接受 1-6 范围限制", () => {
        if (!calcSrc.includes("Math.Max(1, Math.Min(6, tier))")) return "SetTier 未做范围限制";
    });

    test("[Layout] GetShopCardRects 有缓存机制 (_shopCache)", () => {
        if (!calcSrc.includes("_shopCache")) return "缺少 _shopCache 缓存";
    });

    const configSrc = readFile("src/BobCoach/Core/LayoutConfig.cs");
    const overlaySrc = readFile("src/BobCoach/OverlayRenderer.cs");
    const calibSrc = readFile("src/BobCoach/Core/CalibrationOverlay.cs");

    test("[LayoutResolution] LayoutConfig records calibration canvas size", () => {
        if (!configSrc.includes("Version { get; set; } = 11"))
            return "LayoutConfig version must bump to v11 for status strip settings";
        if (!configSrc.includes("CalibrationWidth") || !configSrc.includes("CalibrationHeight"))
            return "missing CalibrationWidth/CalibrationHeight";
        if (!configSrc.includes("ScaleX(") || !configSrc.includes("ScaleY("))
            return "missing persisted-offset scaling helpers";
        if (!configSrc.includes("RebasePixelValues"))
            return "SetCalibrationSize must rebase pixel fields so saving at 4K does not shrink default gaps";
    });

    test("[LayoutResolution] Calibration save writes current canvas size", () => {
        if (!calibSrc.includes("SetCalibrationSize"))
            return "CalibrationOverlay must record the current canvas size before Save";
    });

    test("[Calibration] Shop preview uses manual preview card count", () => {
        if (!calibSrc.includes('_selectedZone == "Shop" ? _previewCardCount : GetShopLayoutCardCount(_activeTier)'))
            return "Shop calibration preview must honor [] preview card count";
    });

    test("[LayoutResolution] GameLayoutCalculator scales config pixel offsets", () => {
        if (!calcSrc.includes("_config.ScaleX") || !calcSrc.includes("_config.ScaleY"))
            return "GameLayoutCalculator still applies persisted pixel offsets without resolution scaling";
    });

    test("[ShopLayout][0723] 2048宽5卡默认购买标签与卡牌中轴重合", () => {
        const defaultOffsetMatch = configSrc.match(/public\s+const\s+double\s+DefaultShopOffsetX\s*=\s*(-?\d+(?:\.\d+)?)/);
        if (!defaultOffsetMatch) return "无法读取 DefaultShopOffsetX";

        const canvasWidth = 2048;
        const defaultOffsetAtCanvasWidth = Number(defaultOffsetMatch[1])
            * canvasWidth / 1920;
        const shopGroupCenter = canvasWidth / 2 + defaultOffsetAtCanvasWidth;
        if (Math.abs(shopGroupCenter - canvasWidth / 2) > 0.01)
            return `默认商店中轴偏移 ${defaultOffsetAtCanvasWidth.toFixed(1)}px`;

        const ratingIdx = overlaySrc.indexOf("public void ShowShopCardRating");
        const nextMethodIdx = overlaySrc.indexOf("internal void ShowTimewarpPurchaseRating", ratingIdx);
        const ratingBlock = overlaySrc.slice(ratingIdx, nextMethodIdx);
        if (!ratingBlock.includes("left + (cw - barW) / 2 + labelOffX"))
            return "购买标签条没有按卡牌矩形居中";
        if (!ratingBlock.includes("cx - arrowW / 2"))
            return "购买箭头没有按卡牌中心定位";
    });

    test("[LayoutResolution] status strip position is resolution-scaled", () => {
        const idx = calcSrc.indexOf("public LayoutPoint GetStatusStripPosition");
        const block = calcSrc.slice(idx, idx + 260);
        if (block.includes("new LayoutPoint(8, 105)"))
            return "status strip still uses raw 1080p coordinates";
        if (!block.includes("_config.StatusOffsetX") || !block.includes("_config.StatusOffsetY"))
            return "status strip position should use persisted config offsets";
    });

    test("[StatusStrip] renderer and calibration share one LayoutConfig", () => {
        if (!overlaySrc.includes("OverlayRenderer(Engine.LayoutConfig config)"))
            return "renderer missing shared LayoutConfig constructor";
        if (!overlaySrc.includes("new Engine.CalibrationOverlay(_config)"))
            return "calibration overlay does not use renderer config";
        const refreshIdx = overlaySrc.indexOf("public void RefreshLayout");
        const refreshBlock = overlaySrc.slice(refreshIdx, refreshIdx + 420);
        if (refreshBlock.includes("LayoutConfig.Load()"))
            return "RefreshLayout replaces the shared config instance";
    });

    test("[StatusStrip] F10 Status updates position and reset preserves font size", () => {
        const statusApply = calibSrc.indexOf('case "Status":');
        const applyBlock = calibSrc.slice(statusApply, statusApply + 260);
        if (!applyBlock.includes("StatusOffsetX") || !applyBlock.includes("StatusOffsetY"))
            return "F10 Status arrow keys do not update status offsets";
        const resetIdx = calibSrc.indexOf("private void ResetCurrentZone");
        const resetBlock = calibSrc.slice(resetIdx, resetIdx + 2200);
        const statusReset = resetBlock.indexOf('case "Status":');
        if (statusReset < 0) return "F10 Status reset is missing";
        const statusResetBlock = resetBlock.slice(statusReset, statusReset + 260);
        if (!statusResetBlock.includes("StatusOffsetX") || !statusResetBlock.includes("StatusOffsetY"))
            return "F10 Status reset does not restore position";
        if (statusResetBlock.includes("StatusFontSize"))
            return "F10 Status reset must preserve the player font size";
    });

    test("[StatusStrip] drag lifecycle preserves the stable root during routine refresh", () => {
        if (!overlaySrc.includes("CaptureMouse()") || !overlaySrc.includes("ReleaseMouseCapture()")
            || !overlaySrc.includes("LostMouseCapture"))
            return "status drag must capture the mouse and handle lost capture";
        const clearIdx = overlaySrc.indexOf("public void Clear(");
        const clearBlock = overlaySrc.slice(clearIdx, clearIdx + 900);
        const nonPanelIdx = overlaySrc.indexOf("public void ClearNonPanel");
        const nonPanelBlock = overlaySrc.slice(nonPanelIdx, nonPanelIdx + 900);
        if (!clearBlock.includes("DetachStatusStrip"))
            return "full clear must remove status handlers and restore lock";
        if (nonPanelBlock.includes("DetachStatusStrip"))
            return "routine redraw must preserve the status root and an active drag";
        if (!nonPanelBlock.includes('tag == "status_strip"'))
            return "routine redraw must retain the status root registration";
        const fadeIdx = overlaySrc.indexOf("public void FadeOutActiveElements");
        const fadeBlock = overlaySrc.slice(fadeIdx, fadeIdx + 900);
        if (fadeBlock.includes("DetachStatusStrip") || !fadeBlock.includes("ReferenceEquals(el, _statusStripRoot)"))
            return "routine fade must preserve the status root and an active drag";
        const mouseDownIdx = overlaySrc.indexOf("private void StatusStripMouseLeftButtonDown");
        const mouseDownBlock = overlaySrc.slice(mouseDownIdx, mouseDownIdx + 900);
        if (!/if\s*\(\s*!_statusStripRoot\.CaptureMouse\(\)\s*\)\s*return;[\s\S]*?_statusDragging\s*=\s*true;/.test(mouseDownBlock))
            return "status drag must start only after mouse capture succeeds";
    });

    test("[LayoutResolution] top-right suggestion badge stays inside the canvas", () => {
        const idx = overlaySrc.indexOf("public void ShowSuggestionBadge");
        const block = overlaySrc.slice(idx, idx + 1100);
        if (!block.includes("tb.Measure("))
            return "suggestion badge must measure its rendered text width before positioning";
        if (!block.includes("tb.DesiredSize.Width"))
            return "suggestion badge right anchor does not use measured text width";
        if (!/Math\.Max\(0,\s*w\s*-\s*tb\.DesiredSize\.Width\s*-/.test(block))
            return "suggestion badge must clamp its left edge while preserving a right margin";
    });

    test("[LayoutResolution] trinket/discover panels do not use raw 1080p coordinates", () => {
        const trinketIdx = overlaySrc.indexOf("public void ShowTrinketHints");
        const discoverIdx = overlaySrc.indexOf("public void ShowDiscoverHints");
        const trinketBlock = overlaySrc.slice(trinketIdx, discoverIdx);
        const discoverBlock = overlaySrc.slice(discoverIdx, overlaySrc.indexOf("public void ClearTrinketHints", discoverIdx));
        if (/double\s+x\s*=\s*12\s*;/.test(trinketBlock) || /double\s+y\s*=\s*220\s*;/.test(trinketBlock))
            return "trinket panel still uses raw x=12/y=220";
        if (/double\s+y\s*=\s*380\s*;/.test(discoverBlock) || /double\s+x\s*=\s*12\s*;/.test(discoverBlock))
            return "discover panel still uses raw x=12/y=380";
        // 07071644: 面板位置改由 Config.ScaleX/ScaleY(变量)驱动, 宽度仍 ScaleX(数字); 均为缩放坐标(非raw 1080p)
        if (!/ScaleX\(/.test(trinketBlock) || !/ScaleY\(/.test(trinketBlock))
            return "trinket panel should use scaled coordinates";
        if (!/ScaleX\(/.test(discoverBlock) || !/ScaleY\(/.test(discoverBlock))
            return "discover panel should use scaled coordinates";
        if (!/panelTextWidth = _calc\.ScaleX\(\d+\)/.test(trinketBlock) || !/panelTextWidth = _calc\.ScaleX\(\d+\)/.test(discoverBlock))
            return "panel text widths should scale with resolution";
    });
}

// ============================================================================
// [4] Panel lifecycle: 饰品/发现面板
// ============================================================================
{
    const extractorSrc = readFile("src/BobCoach/GameStateExtractor.cs");

    test("[Lifecycle] 饰品 offer 检测有 NewRound 回合切换", () => {
        if (!extractorSrc.includes("_lastTrinketOfferTurn")) return "缺少 _lastTrinketOfferTurn 回合跟踪";
        if (!extractorSrc.includes("turn != _lastTrinketOfferTurn")) return "缺少 NewRound 回合切换检测";
    });

    test("[ShopPos] ExtractShopMinions 保留原始槽位 (raw slot, 不dense压缩)", () => {
        // V4.1.1: ShopPosition 保留 ZONE_POSITION-1, 标签按 raw slot + 理论槽位布局。
        if (!extractorSrc.includes("Position = slotIndex"))
            return "missing raw slot assignment";
        if (extractorSrc.includes("result[i].Position = i"))
            return "raw slot is still compressed into dense index";
    });

    test("[DiscoverGate] 裸 zone6 不得自触发 Discover gate", () => {
        if (extractorSrc.includes("DiscoverTriggerActive || (zone6Active && turn >= 3)"))
            return "zone6 still self-opens discover gate";
        if (!extractorSrc.includes("bool shouldExtract = DiscoverTriggerActive"))
            return "discover gate is not explicit-trigger-only";
    });

    test("[DiscoverGate] hand-play discover may open gate only with fresh zone6", () => {
        const pluginSrc = readFile("src/BobCoach/BobCoachPlugin.cs");
        const idx = pluginSrc.indexOf("private bool UpdateDiscoverTriggerWindow");
        const _mEnd = pluginSrc.indexOf("\n        private ", idx + 1);
        const block = pluginSrc.slice(idx, _mEnd > idx ? _mEnd : idx + 9000);
        if (!block.includes("HandDecreasedThisFrame") || !block.includes("Zone6FreshThisFrame"))
            return "hand-play discover trigger must require both hand decrease and fresh zone6";
        if (!block.includes("Zone6EntityCountThisFrame > 0"))
            return "hand-play discover trigger must require zone6 candidates";
        if (!block.includes('"battlecry"'))
            return "hand-play discover source should be tagged separately from naked zone6";
    });

    test("[DiscoverGate] only standard triple reward opens heuristic gate with fresh zone6", () => {
        const pluginSrc = readFile("src/BobCoach/BobCoachPlugin.cs");
        const idx = pluginSrc.indexOf("private bool UpdateDiscoverTriggerWindow");
        const _mEnd = pluginSrc.indexOf("\n        private ", idx + 1);
        const block = pluginSrc.slice(idx, _mEnd > idx ? _mEnd : idx + 9000);
        if (!pluginSrc.includes("_prevGoldenHandCount"))
            return "missing previous hand golden count tracker";
        if (!block.includes("handTripleJustHappened"))
            return "hand golden triple trigger is not detected";
        if (!block.includes("curGoldenHand > _prevGoldenHandCount"))
            return "hand triple trigger must require increased golden hand count";
        if (!block.includes("Zone6FreshThisFrame") || !block.includes("Zone6EntityCountThisFrame > 0"))
            return "hand triple trigger must require fresh zone6 candidates";
        if (!block.includes("TripleRuleEvaluator.GrantsStandardDiscover"))
            return "golden-count heuristic must be disabled when anomaly replaces the standard discover reward";
        if (!block.includes('"DiscoverTrigger: source=handTriple'))
            return "missing handTriple diagnostic log";
    });

    test("[AnomalyRules] shop golden opportunity tag uses effective copy threshold", () => {
        const pluginSrc = readFile("src/BobCoach/BobCoachPlugin.cs");
        const projectSrc = readFile("src/BobCoach/BobCoach.csproj");
        const idx = pluginSrc.indexOf("private string GetPairTag");
        const end = pluginSrc.indexOf("\n        private ", idx + 1);
        const block = pluginSrc.slice(idx, end > idx ? end : idx + 1800);
        if (!block.includes("TripleRuleEvaluator.CompletesGolden"))
            return "shop tag still hardcodes ordinary three-copy completion";
        if (!block.includes("GoldenCopyRequirement"))
            return "shop tag does not distinguish completion from ordinary pair progress";
        if (!projectSrc.includes('Compile Include="Core\\TripleRuleEvaluator.cs"'))
            return "checked-in HDT project omits TripleRuleEvaluator.cs";
    });

    test("[07071121] LogConfig 强制 [Power] Verbose=True (EntityChoices 开关)", () => {
        const src = readFile("src/BobCoach/Core/LogConfigEnsurer.cs");
        const pi = src.indexOf('["Power"]');
        const zi = src.indexOf('["Zone"]');
        const powerBlock = src.slice(pi, zi > pi ? zi : pi + 800);
        if (!powerBlock.includes('["Verbose"] = "True"'))
            return "Power 段 Verbose 必须为 True, 否则游戏不写 GameState.DebugPrintEntityChoices()";
        if (!src.includes("EnsureKeyInSection") || !src.includes("EnsureFunctionalKey"))
            return "PatchConfig 需就地改值(EnsureKeyInSection), 不能只追加缺失整段(已存在段的错值修不掉)";
    });

    test("[07071121] 发现门控: Power.log 活跃时 zone6 启发式静默", () => {
        const src = readFile("src/BobCoach/BobCoachPlugin.cs");
        const idx = src.indexOf("private bool UpdateDiscoverTriggerWindow");
        const end = src.indexOf("\n        private ", idx + 1);
        const block = src.slice(idx, end > idx ? end : idx + 9000);
        if (!block.includes("powerLogOwnsDiscover"))
            return "缺少 Power.log 独占发现门的短路";
        if (!block.includes("IsChoiceListActive"))
            return "短路条件需含 IsChoiceListActive (路径A活跃判定)";
        const scIdx = block.indexOf("powerLogOwnsDiscover");
        const trigIdx = block.indexOf("hasRealTrigger");
        if (scIdx < 0 || trigIdx < 0 || scIdx > trigIdx)
            return "短路必须在 hasRealTrigger 计算之前(A活跃时不跑9启发式)";
        if (!block.includes("_prevBoardHadDiscoverSource = BoardHasDiscoverSource"))
            return "短路分支需同步 _prev* 跟踪, 防A结束后B用跨A状态差误触发";
    });

    test("[07071121] PowerLogWatcher 输出 PL census + 暴露 IsChoiceListActive", () => {
        const src = readFile("src/BobCoach/Core/PowerLogWatcher.cs");
        if (!src.includes("PL census"))
            return "StopWatching 需打 PL census 行(仲裁 Verbose 修复)";
        if (!src.includes("_censusEntityChoices") || !src.includes("DebugPrintEntityChoices"))
            return "census 需统计 DebugPrintEntityChoices 行数(输入源仲裁)";
        if (!src.includes("IsChoiceListActive"))
            return "watcher 需暴露 IsChoiceListActive 供发现门控查询";
    });

    test("[0711][PowerLog] 选择事件主动请求UI评估且Dispatcher定时器消费请求", () => {
        const pluginSrc = readFile("src/BobCoach/BobCoachPlugin.cs");
        const discoverStart = pluginSrc.indexOf("private void OnPowerLogDiscoverOffered");
        const trinketStart = pluginSrc.indexOf("private void OnPowerLogTrinketChoiceActive", discoverStart);
        const completeStart = pluginSrc.indexOf("private void OnPowerLogChoiceCompleted", trinketStart);
        const stateChangedStart = pluginSrc.indexOf("private void OnPowerLogStateChanged", completeStart);
        const discoverBlock = pluginSrc.slice(discoverStart, trinketStart);
        const trinketBlock = pluginSrc.slice(trinketStart, completeStart);
        const completeBlock = pluginSrc.slice(completeStart, stateChangedStart);
        if (!discoverBlock.includes("OnPowerLogStateChanged();"))
            return "DiscoverOffered 只更新字段但不请求评估 — 静止选择界面无OnUpdate时面板不会显示";
        if (!trinketBlock.includes("OnPowerLogStateChanged();"))
            return "TrinketChoiceActive 须主动请求评估";
        if (!completeBlock.includes("OnPowerLogStateChanged();"))
            return "ChoiceCompleted 须主动请求评估以即时清面板";
        const timerStart = pluginSrc.indexOf("_combatWatchTimer.Tick +=");
        const timerStartMarker = "_combatWatchTimer.Start();";
        const timerEnd = pluginSrc.indexOf(timerStartMarker, timerStart);
        const timerBlock = pluginSrc.slice(timerStart, timerEnd > timerStart ? timerEnd + timerStartMarker.length : timerStart + 2400);
        if (!timerBlock.includes("_eventEvalRequested") || !timerBlock.includes("EvaluateAndRender();"))
            return "Dispatcher定时器未消费Power.log评估请求 — 标志仍只能等待不保证到来的HDT OnUpdate";
    });

    test("[0713][Deploy] 正式DLL保留隔离校验但不创建外部统计运行路径", () => {
        const build = readFile("tools/build/build_release.ps1");
        const project = readFile("src/BobCoach/BobCoach.csproj");
        const plugin = readFile("src/BobCoach/BobCoachPlugin.cs");
        const updater = readFile("src/BobCoach/Core/TrinketStatsUpdater.cs");
        for (const name of ["TrinketStatsModels.cs", "TrinketStatsVerifier.cs", "TrinketStatsFetcher.cs", "TrinketStatsStore.cs", "TrinketStatsUpdater.cs"]) {
            if (!project.includes(`<Compile Include="Core\\${name}"`))
                return "正式DLL漏编译外部校验模块: " + name;
        }
        if (plugin.includes("_trinketStatsUpdater")
            || plugin.includes("new Engine.TrinketStatsUpdater(")
            || plugin.includes("SetCurrentBuild(build)"))
            return "生产插件仍创建或驱动外部饰品统计更新器";
        if (plugin.includes("_trinketStatsUpdater.Active") || plugin.includes("TrinketStatRecord"))
            return "外部校验数据已越界接入生产评分/UI";
        for (const token of ["FirestoneUrl", "ParseFirestone", "RequestCheck(", "ScheduleRetry(", "new Timer(", "Task.Run(", "new TrinketStatsStore(", "new TrinketStatsFetcher("]) {
            if (updater.includes(token))
                return "禁用协调器仍包含联网、缓存、解析或重试入口: " + token;
        }
        for (const token of ["hdt-bridge.js", 'Join-Path $repoRoot "simulation"', 'Join-Path $repoRoot "data"']) {
            if (build.includes(token))
                return "正式构建仍部署非DLL运行内容: " + token;
        }
    });

    test("[0721][TrinketUI] 饰品本地链路保留但显示经单一默认关闭开关", () => {
        const plugin = readFile("src/BobCoach/BobCoachPlugin.cs");
        if (!plugin.includes("private const bool TrinketRecommendationsVisible = true;"))
            return "饰品推荐显示开关必须已开启";
        if (!plugin.includes("_engine.EvaluateTrinkets(state)"))
            return "饰品本地评估链被删除";
        if (!plugin.includes("OnPowerLogTrinketChoiceActive"))
            return "饰品候选识别生命周期被删除";
        if (!plugin.includes("bool trinketShouldDisplay = TrinketRecommendationsVisible && trinketShouldShow;")
            || !plugin.includes("if (trinketShouldDisplay)"))
            return "最终饰品 UI 输出未使用默认关闭开关";
        if (!plugin.includes("_renderer.ShowTrinketHints(plan.TrinketHints)")
            || !plugin.includes("_renderer.ClearTrinketHints()"))
            return "饰品渲染实现未完整保留";
    });

    test("[07071603] EntityChoices 分发用原始 line + 入口正则去前缀 (ExtractContent 剥前缀根因)", () => {
        const src = readFile("src/BobCoach/Core/PowerLogParser.cs");
        if (!src.includes('else if (line.Contains("DebugPrintEntityChoices()"))'))
            return "EntityChoices 分发必须用原始 line.Contains (content 已被 ExtractContent 剥掉标识前缀 → content.Contains 恒 false)";
        const idx = src.indexOf("private PLEvent ParseEntityChoices");
        const block = src.slice(idx, idx + 1400);
        if (block.includes('DebugPrintEntityChoices\\(\\) - id='))
            return "入口正则不得含 'DebugPrintEntityChoices() - ' 前缀 (content 已剥离该前缀, 会恒不匹配 → _currentEntityChoice 永不设置)";
        if (!block.includes('@"^id=(\\d+) Player='))
            return "入口正则应以 ^id= 起匹配剥离后的 content";
    });

    test("[DiscoverGate] zone6 fallback 不得依赖已清空的 DiscoverOptions", () => {
        const bad = /zone6Active\s*&&\s*!DiscoverTriggerActive\s*&&\s*state\.DiscoverOptions\.Count\s*>\s*0/.test(extractorSrc);
        if (bad) return "DiscoverZone6Triggered 条件不可达: gate关闭时 DiscoverOptions 已被置空";
    });

    test("[DiscoverGate] zone6 fresh is based on entity changes, not turn rollover", () => {
        if (extractorSrc.includes("_lastZone6CheckTurn") || extractorSrc.includes("turn!=_lastZone6CheckTurn"))
            return "zone6 fresh must not be refreshed just because the turn changed";
        if (!extractorSrc.includes("Zone6FreshWindowSeconds = 1.5"))
            return "zone6 fresh window should be narrowed to 1.5 seconds";
        if (extractorSrc.includes("TotalSeconds < 4"))
            return "old 4s zone6 fresh window is still present";
    });

    test("[DiscoverGate] second hero power discover must be selected from HP entities", () => {
        if (!extractorSrc.includes("SelectHeroPowerEntities")
            || !extractorSrc.includes("SelectPrimaryHeroPowerEntity"))
            return "extractor must collect all HP entities and identify the primary separately";
        if (extractorSrc.includes("var hpEntity = entities.Find(e => e.CardId != null"))
            return "raw first hero power Find can miss second hero power / pick opponent HP";
        if (!extractorSrc.includes("HeroPowerTextHasDiscover"))
            return "copied anomaly/trinket hero powers must detect Discover from actual HP text";
        if (!extractorSrc.includes("HeroPowerStateResolver.Resolve")
            || !extractorSrc.includes("state.HeroPowers"))
            return "all observed hero powers must enter the explicit per-power state model";
        if (!extractorSrc.includes("ctrl != _localPlayerCtrl"))
            return "hero power selection must filter opponent controller";
    });

    test("[ShopPos] maxGold 公式 = Min(10, 2+turn) (off-by-one修复)", () => {
        // 原 3+Max(0,turn-2) 从T2起每回合少算1金 → gold提前归零误推荐(06111155问题2)
        if (extractorSrc.includes("3 + Math.Max(0, turn - 2)"))
            return "maxGold 仍是旧 off-by-one 公式";
        if (!extractorSrc.includes("Math.Min(10, 2 + turn)"))
            return "缺少修正后的 Min(10, 2+turn) 公式";
    });

    test("[GoldTracker] early-turn fallback must not overwrite real 0 gold", () => {
        if (!extractorSrc.includes("trackedGold < 0 && state.NormalRecruitAvailable")
            || !extractorSrc.includes("state.Gold <= 0"))
            return "T1/T2 gold fallback must only run before tracker initializes";
        const oldFallback = /state\.Gold\s*<=\s*0\s*&&\s*state\.ShopMinions\.Count\s*>\s*0\s*&&\s*turn\s*<=\s*2\)\s*state\.Gold\s*=\s*3/.test(extractorSrc);
        if (oldFallback)
            return "old early-turn fallback can still overwrite real 0 gold with 3";
    });

    test("[Lifecycle] 饰品 offer 数量 <2 时返回空 (抑制单残留实体)", () => {
        // 最终返回前有数量不足检查 (新轮次状态机保留此守卫)
        if (!extractorSrc.includes("result.Count < 2"))
            return "缺少数量不足返回空检查";
    });

    test("[TrinketRound] 双过滤: 已装备cardId过滤残留 (0611重构,替代脆弱回合抑制)", () => {
        // 问题3+6根因: 旧 cross-turn/firstTurn+5 硬兜底误杀T9大饰品。
        // 新机制: 源头双过滤 — 已装备(inPlay)cardId + 已完成轮次cardId 滤掉残留实体。
        if (!extractorSrc.includes("_equippedTrinketIds"))
            return "缺少 _equippedTrinketIds 已装备饰品过滤";
        if (!extractorSrc.includes("_equippedTrinketIds.Contains(e.CardId)"))
            return "缺少已装备cardId残留过滤";
    });

    test("[TrinketRound] 已删除脆弱回合抑制 (防 T9 大饰品误杀回归)", () => {
        // 旧 cross-turn suppress / firstTurn+5 hard suppress 是问题6(T9大饰品缺失)根因, 必须已删除
        if (extractorSrc.includes("cross-turn suppress"))
            return "旧 cross-turn suppress 未删除 (会误杀T9大饰品)";
        if (extractorSrc.includes("_trinketOfferFirstTurn + 5"))
            return "旧 firstTurn+5 硬兜底未删除 (会误杀T9大饰品)";
    });

    test("[TrinketRound] 每次选取都累积 completed (修T7小饰品兄弟残留)", () => {
        // 问题3根因: 旧逻辑仅 owned>=2 才记completed, T6选第1个小饰品时兄弟实体未记→T7残留。
        // 新逻辑: owned增加即把当前result(兄弟实体)累积进completed。
        if (!extractorSrc.includes("round resolved, completed+="))
            return "缺少每次选取累积completed的逻辑";
    });

    test("[TrinketRound] 神秘魔方同CardId offer 不应被已装备过滤误杀", () => {
        if (!extractorSrc.includes("IsReplaceableTrinketOfferDuplicate"))
            return "缺少替换型饰品重复offer例外";
        if (!extractorSrc.includes('cardId == "BG30_MagicItem_703"'))
            return "神秘魔方 BG30_MagicItem_703 未纳入重复offer例外";
        if (!extractorSrc.includes("_equippedTrinketIds.Contains(e.CardId) && !IsReplaceableTrinketOfferDuplicate(e.CardId)"))
            return "已装备过滤仍会误杀神秘魔方同CardId offer";
    });

    test("[TrinketRound] 小饰品已选后、大饰品回合前 suppress 预刷残留", () => {
        if (!extractorSrc.includes("waitingForGreaterTrinketTurn"))
            return "缺少 T7/T8 pre-greater residue 门控";
        if (!extractorSrc.includes("pre-greater residue suppressed"))
            return "缺少 pre-greater residue 抑制日志";
        if (!extractorSrc.includes('!hasRepeatingReplacementTrinket'))
            return "神秘魔方每回合替换机制不能被 pre-greater 门控误杀";
        if (!extractorSrc.includes("_trinketLesserTurn = 6") || !extractorSrc.includes("_trinketGreaterTurn = 9"))
            return "普通饰品回合必须在跨局 Reset 时恢复为 T6/T9";
    });

    test("[TrinketRound] 时空扭曲随从池回合不得改写饰品回合", () => {
        if (extractorSrc.includes("TimewarpLesserTurn>0)_trinketLesserTurn")
            || extractorSrc.includes("TimewarpGreaterTurn>0)_trinketGreaterTurn")
            || extractorSrc.includes("TimewarpLesserTurn > 0) _trinketLesserTurn")
            || extractorSrc.includes("TimewarpGreaterTurn > 0) _trinketGreaterTurn"))
            return "timewarp pool turns are minion-pool timing, not trinket choice timing";
    });

    test("[TrinketRound] offerCount 每帧更新, 不只在回合切换时更新", () => {
        const updateIdx = extractorSrc.indexOf("_prevOfferCount = offerCount");
        if (updateIdx < 0) return "缺少 _prevOfferCount 更新";
        const newRoundIdx = extractorSrc.indexOf("if (turn != _lastTrinketOfferTurn)");
        const blockEnd = extractorSrc.indexOf("_lastTrinketOfferTurn = turn", newRoundIdx);
        if (newRoundIdx >= 0 && blockEnd >= 0 && updateIdx > newRoundIdx && updateIdx < blockEnd)
            return "_prevOfferCount 仍只在回合切换分支内更新, 同回合延迟 offer 无法触发动态清理";
        if (extractorSrc.includes("_lastPickedOfferIds = null;\n                _lastPickedOfferIds = null;"))
            return "_lastPickedOfferIds 重复清空, 疑似笔误";
    });

    test("[TrinketRound] 重复同CardId候选应视为残留而非真实offer", () => {
        if (!extractorSrc.includes("duplicate-only residue suppressed"))
            return "缺少重复同CardId饰品候选抑制日志";
        if (!extractorSrc.includes("uniqueOfferIds.Count < 2"))
            return "缺少不同CardId数量守卫, 典狱长标签x2 残留会复活面板";
        if (!extractorSrc.includes("_completedTrinketIds.Add(cid)"))
            return "重复残留未写入 completed, 下一帧仍会复活";
    });

    test("[TrinketRound] polluted offers are salvaged only on scheduled choice turns", () => {
        const factSource = readFile("src/BobCoach/Core/HearthDbTrinketFactSource.cs");
        if (!extractorSrc.includes("HasLocalTrinketFact")
            || !extractorSrc.includes("TryGetLocalTrinketFact"))
            return "missing local trinket fact filter";
        if (!factSource.includes("CardType.BATTLEGROUND_TRINKET"))
            return "local fact source must reject anomaly/non-trinket card types";
        if (!extractorSrc.includes("polluted offer suppressed"))
            return "polluted trinket batches must be suppressed";
        if (!extractorSrc.includes("polluted offer salvaged") || !extractorSrc.includes("scheduledTrinketChoiceTurn"))
            return "scheduled trinket choice turns should salvage a coherent batch from polluted residues";
        if (extractorSrc.includes("Take(4)"))
            return "polluted trinket batches must not be disguised by Take(4)";
        const pollutedIdx = extractorSrc.indexOf("polluted offer suppressed");
        const pollutedBlock = extractorSrc.slice(Math.max(0, pollutedIdx - 500), pollutedIdx + 500);
        if (pollutedBlock.includes("_completedTrinketIds.Add(cid)"))
            return "polluted batches must not poison completed ids; current valid offers can be mixed with residue";
    });

    test("[TrinketRound] owned>=2 post-choice residue is suppressed even on scheduled turns", () => {
        const idx = extractorSrc.indexOf("post-choice residue suppressed");
        if (idx < 0) return "missing post-choice residue suppression";
        const block = extractorSrc.slice(Math.max(0, idx - 500), idx + 500);
        if (!block.includes("ownedRealTrinketCount >= 2"))
            return "post-choice suppression must require owned>=2";
        if (block.includes("!scheduledTrinketChoiceTurn"))
            return "scheduled trinket turns must still suppress residue after the player already owns 2 trinkets";
    });

    test("[TrinketRound] lesser/greater classification must use local HearthDb facts", () => {
        const pluginSrc = readFile("src/BobCoach/BobCoachPlugin.cs");
        if (!extractorSrc.includes("SetTrinketFactSource")
            || !extractorSrc.includes("TryGetLocalTrinketFact"))
            return "extractor must receive the local trinket fact source";
        if (!extractorSrc.includes("IsLesser = localFact.IsLesser"))
            return "trinket type classification must use the local SpellSchool fact";
        if (!pluginSrc.includes("_extractor.SetTrinketFactSource(_engine.TrinketFactSource)"))
            return "plugin must wire DecisionEngine local trinket facts into extractor";
        if (extractorSrc.includes('cardId.Contains("MagicItem_8")')
            || extractorSrc.includes('cardId.Contains("MagicItem_9")'))
            return "lesser/greater classification must not use CardId suffix fallbacks";
    });

    test("[TrinketRound] scheduled salvage filters mismatched lesser/greater batches", () => {
        if (!extractorSrc.includes("type-filtered T{0} expected={1}"))
            return "scheduled trinket turns must log and apply type filtering";
        if (!extractorSrc.includes("batch pending T{1}"))
            return "T9 must suppress lesser-only polluted batches until greater options arrive";
        if (!extractorSrc.includes("t.IsLesser == expectLesser"))
            return "scheduled trinket filtering must compare option type to expected round type";
        if (extractorSrc.includes("turn == _trinketLesserTurn && ownedRealTrinketCount == 0")
            || extractorSrc.includes("turn == _trinketGreaterTurn || ownedRealTrinketCount >= 1"))
            return "scheduled trinket round type must not be polluted by owned residue count";
    });

    test("[TrinketRound] scheduled turns show placeholder before entities arrive", () => {
        const pluginSrc = readFile("src/BobCoach/BobCoachPlugin.cs");
        if (!pluginSrc.includes("ApplyScheduledTrinketPlaceholder"))
            return "missing scheduled trinket placeholder fallback";
        if (!pluginSrc.includes("__TRINKET_PENDING_LESSER_1") || !pluginSrc.includes("__TRINKET_PENDING_GREATER_1"))
            return "placeholder must distinguish lesser and greater rounds";
        // 07062107: 判定条件从 owned 绝对计数改为"计划回合+本回合未完成选取" —
        // 英雄机制额外饰品(费林 equipped=3)会破坏 owned==0/1 判定
        if (!pluginSrc.includes("state.Turn == 6 && !roundResolvedThisTurn") || !pluginSrc.includes("state.Turn == 9 && !roundResolvedThisTurn"))
            return "placeholder must be constrained to unresolved T6/T9 trinket choice turns";
        if (!pluginSrc.includes("DIAG TrinketPlaceholder"))
            return "placeholder path must be diagnosable in real logs";
        if (!pluginSrc.includes("IsScheduledTrinketPlaceholder") || !pluginSrc.includes("!IsScheduledTrinketPlaceholder(state.TrinketOffer)"))
            return "scheduled placeholder must bypass UiTargetStateMachine confirmation clearing";
    });

    test("[T35][Trinket] mystery cube activation is not treated as scheduled greater trinket", () => {
        const lifecycle = readFile("src/BobCoach/Core/TrinketChoiceBatchLifecycle.cs");
        if (!lifecycle.includes("MysteryCubeActivation"))
            return "missing MysteryCubeActivation context";
        if (!lifecycle.includes("BG30_MagicItem_703t"))
            return "mystery cube source is not explicitly recognized";
    });

    test("[T35][Trinket] mystery cube suppresses scheduled turn filtering", () => {
        const extractor = readFile("src/BobCoach/GameStateExtractor.cs");
        if (!extractor.includes("CurrentTrinketChoiceContext"))
            return "extractor does not receive the lifecycle context";
        if (!extractor.includes("mysteryCubeActivation"))
            return "mystery cube does not bypass scheduled trinket filtering";
    });

    test("[T35][Trinket] mystery cube panel uses authoritative Power.log candidates", () => {
        const plugin = readFile("src/BobCoach/BobCoachPlugin.cs");
        if (!plugin.includes("MysteryCubeActivation"))
            return "plugin does not branch on mystery cube activation";
        if (!plugin.includes("UiTargetSource.PowerLog"))
            return "plugin does not require the authoritative candidate source";
    });

    test("[TrinketRound] scheduled placeholder is loading state, not fake recommendation", () => {
        const pluginSrc = readFile("src/BobCoach/BobCoachPlugin.cs");
        const overlaySrc = readFile("src/BobCoach/OverlayRenderer.cs");
        const engineSrc = readFile("src/BobCoach/Core/DecisionEngine.cs");
        const visualizerSrc = readFile("src/BobCoach/Core/DecisionVisualizer.cs");
        if (!pluginSrc.includes("HasScheduledTrinketPlaceholder(state)") || !pluginSrc.includes("ShowTrinketLoading"))
            return "scheduled placeholder must render a loading panel instead of numbered candidate hints";
        if (!overlaySrc.includes("public void ShowTrinketLoading") || !overlaySrc.includes("ShowTrinketLoading:"))
            return "renderer must expose diagnosable trinket loading UI";
        if (engineSrc.includes("results.Add(new TrinketScore { Index = i, CardId = t.CardId, Name = t.TrinketName, Score = 0"))
            return "pending trinket placeholders must not enter structured trinket scoring";
        if (!visualizerSrc.includes("pendingTrinketOnly") || !visualizerSrc.includes("!pendingTrinketOnly"))
            return "visualizer must not turn pending placeholders into ranked trinket hints";
    });

    test("[GoldTracker] stale shop entity buy must still spend gold", () => {
        const goldSrc = readFile("src/BobCoach/Core/GoldTracker.cs");
        if (!goldSrc.includes("int boardCount, int handCount, int tavernTier"))
            return "GoldTracker must receive hand count to detect buy animation windows";
        if (!goldSrc.includes("SameShopEntitySet(curShopIds, _prevShopEntityIds)"))
            return "stale shop entity set should be detected explicitly";
        if (!goldSrc.includes("curHand > _prevHandCount"))
            return "hand growth with unchanged shop entities must spend buy cost";
        if (!goldSrc.includes("ApplyBuyCost(freeCards, firstBuyFree, minionCost)"))
            return "stale-shop fallback must share free-card / first-buy-free cost handling";
    });

    test("[DiscoverGate] board leave discover sources open gate", () => {
        const pluginSrc = readFile("src/BobCoach/BobCoachPlugin.cs");
        if (!pluginSrc.includes("boardDiscoverSourceLeft"))
            return "selling/leaving-board discover sources must be a real discover trigger";
        if (!pluginSrc.includes("BoardHasDiscoverSource"))
            return "plugin must remember whether previous board had discover text";
        if (!pluginSrc.includes("DiscoverTrigger: source=boardLeave"))
            return "board leave discover trigger must be diagnosable in logs";
    });

    // ── 07061713 修复护栏 ──

    test("[5B.3H][Spell] IsUIEntity 使用实体与本机HearthDb精确事实", () => {
        const extractorSrc = readFile("src/BobCoach/GameStateExtractor.cs");
        if (!extractorSrc.includes("TryResolveShopItem(e, out purchaseFact)"))
            return "IsUIEntity must use the shared entity/local purchase-fact resolver";
        if (!extractorSrc.includes("ShopItemFactResolver.TryCreateObservation")
            || !extractorSrc.includes("entity.HasTag(GameTag.COST)"))
            return "entity kind and COST presence must be normalized without treating zero as missing";
        if (extractorSrc.includes("IsSpellCard") || extractorSrc.includes("spell_costs.json"))
            return "legacy spell-ID and spell_costs fallbacks must stay removed";
    });

    test("[07061713][Gold] 升本扣费只在 GoldTracker 内部一处, TrackGold 不得二次扣", () => {
        const extractorSrc = readFile("src/BobCoach/GameStateExtractor.cs");
        const trackGoldIdx = extractorSrc.indexOf("private int TrackGold(");
        if (trackGoldIdx < 0) return "TrackGold not found";
        const trackGoldBody = extractorSrc.slice(trackGoldIdx, extractorSrc.indexOf("private int _goldDiagCount"));
        if (trackGoldBody.includes("GetUpgradeCost"))
            return "TrackGold must not deduct upgrade cost again — GoldTracker.Advance already does (07061713: double-charge drove gold to 0)";
    });

    test("[07061713][Gold] stale-buy 双扣去重 + 07062107 来源判定/无回合内HDT自愈", () => {
        const goldSrc = readFile("src/BobCoach/Core/GoldTracker.cs");
        if (!goldSrc.includes("_pendingStaleBuyCharges"))
            return "GoldTracker must track pending stale-buy charges to avoid double-charging when shop count later decreases";
        if (!goldSrc.includes("handGainedFromShop"))
            return "stale-buy must require hand-gained-from-shop evidence — trinket/discover card gains also raise hand count without a purchase (07062107)";
        if (goldSrc.includes("ResyncedLastFrame") || goldSrc.includes("_stableShopFrames"))
            return "in-turn HDT resync must stay removed — HDT RESOURCES keeps the turn-start value during recruit, resync overwrites correct self-tracked gold (07062107: 27 wrong resyncs)";
        const extractorSrc = readFile("src/BobCoach/GameStateExtractor.cs");
        if (!extractorSrc.includes("_prevShopIdsForGold") || !extractorSrc.includes("_prevHandIdsForGold"))
            return "extractor must compute handGainedFromShop from prev shop/hand entity id sets";
    });

    test("[07061713][Discover] secondHeroPower 触发源(畸变第二技能发现)", () => {
        const pluginSrc = readFile("src/BobCoach/BobCoachPlugin.cs");
        if (!pluginSrc.includes("secondHeroPowerDiscoverUsed"))
            return "second hero power (anomaly secondHp e.g. 小型水晶球) must be a discover trigger source";
        if (!pluginSrc.includes("DiscoverTrigger: source=secondHeroPower"))
            return "secondHeroPower trigger must be diagnosable in logs";
        if (!pluginSrc.includes("_prevExhaustedHeroPowerEntityIds")
            || !pluginSrc.includes("newlyExhaustedDiscoverPower"))
            return "plugin must track concrete exhausted discover-power entities across frames";
        const stateSrc = readFile("src/BobCoach/Core/GameState.cs");
        if (!stateSrc.includes("List<HeroPowerState> HeroPowers"))
            return "GameState must expose per-power hero state";
        const simSrc = readFile("src/BobCoach/Core/Simulator.cs");
        if (!simSrc.includes("src.HeroPowers.Select(power => power.Copy())"))
            return "ShallowCopy must deep-copy hero-power states";
    });

    test("[07061713][Discover] zone6 批次兜底源(触发源枚举不全的结构性保险)", () => {
        const pluginSrc = readFile("src/BobCoach/BobCoachPlugin.cs");
        const extractorSrc = readFile("src/BobCoach/GameStateExtractor.cs");
        if (!extractorSrc.includes("Zone6NewEntityCountThisFrame"))
            return "extractor must expose per-frame NEW zone6 entity count (total count 30 is noise)";
        if (!pluginSrc.includes("zone6BatchFallback"))
            return "plugin must open discover gate on fresh zone6 batch of 3-4 new entities";
        if (!pluginSrc.includes("state.Turn != 6 && state.Turn != 9"))
            return "zone6 fallback must exclude scheduled trinket turns (T6/T9 batches also live in zone6)";
        if (!pluginSrc.includes("DiscoverTrigger: source=zone6Fallback"))
            return "zone6 fallback trigger must be diagnosable in logs";
    });

    test("[07061713][Trinket] 真实候选首见延迟可诊断(实体晚到 vs 门控延迟归因)", () => {
        const extractorSrc = readFile("src/BobCoach/GameStateExtractor.cs");
        if (!extractorSrc.includes("DIAG TrinketFirstSeen"))
            return "type-filtered trinket batch must log per-entity zone6 first-seen age";
    });

    // ── 07062107 修复护栏 ──

    test("[07062107][Render] 渲染被拦截后 planHash 必须清空重试", () => {
        const pluginSrc = readFile("src/BobCoach/BobCoachPlugin.cs");
        const idx = pluginSrc.indexOf("private void DispatchRender");
        const nextMethod = pluginSrc.indexOf("\n        private ", idx + "private void DispatchRender".length);
        const block = pluginSrc.slice(idx, nextMethod >= 0 ? nextMethod : pluginSrc.length);
        const resetCount = (block.match(/_lastRenderedPlanHash = null/g) || []).length;
        if (resetCount < 4)
            return `combat-lag/version/shopGone return paths must reset planHash (found ${resetCount}/4+ resets) — T2 LEVEL_UP suggestion was swallowed by hash-dedup after combat-lag return`;
    });

    test("[07062107][Trinket] picked 后同回合残留按回合压制(不依赖owned绝对计数)", () => {
        const extractorSrc = readFile("src/BobCoach/GameStateExtractor.cs");
        if (!extractorSrc.includes("_trinketRoundResolvedTurn"))
            return "extractor must track the turn when a trinket round resolved";
        if (!extractorSrc.includes("same-turn post-resolve residue suppressed"))
            return "same-turn residue after pick must be suppressed regardless of owned count (T6 owned=1 leaked 2min of stale hints)";
        if (!extractorSrc.includes("TrinketRoundResolvedTurn"))
            return "extractor must expose TrinketRoundResolvedTurn for plugin placeholder gating";
    });

    test("[07062107][Trinket] placeholder 判定不依赖 owned 绝对计数", () => {
        const pluginSrc = readFile("src/BobCoach/BobCoachPlugin.cs");
        if (pluginSrc.includes("state.Turn == 6 && owned == 0") || pluginSrc.includes("state.Turn == 9 && owned == 1"))
            return "placeholder must not gate on absolute owned count — hero-granted extra trinkets (费林 equipped=3) break it (07062107: T9 panel missing)";
        if (!pluginSrc.includes("roundResolvedThisTurn"))
            return "placeholder must gate on scheduled-turn + round-not-yet-resolved";
        if (!pluginSrc.includes("_lastTrinketHideTurn == state.Turn"))
            return "choice完成后事件驱动评估早于extractor resolved更新时，同回合不得重新注入候选读取中";
    });

    test("[07062107][Trinket] 计划回合批次首帧确认(0.95置信通道)", () => {
        const smSrc = readFile("src/BobCoach/Core/UiTargetStateMachine.cs");
        if (smSrc.includes("snapshot.Source == UiTargetSource.PowerLog && snapshot.Confidence"))
            return "IsStableEnough must accept any >=0.95 confidence source, not only PowerLog — event-driven extraction can idle 8s between frames (07062107: T9 panel 8s late)";
        if (!smSrc.includes("snapshot.Confidence >= 0.95"))
            return "IsStableEnough must fast-confirm high-confidence batches";
        const pluginSrc = readFile("src/BobCoach/BobCoachPlugin.cs");
        if (!pluginSrc.includes("(state.Turn == 6 || state.Turn == 9) ? 0.95"))
            return "scheduled trinket turns must produce 0.95-confidence snapshots";
    });

    test("[07062107][Discover] zone6 兜底源按非饰品实体计数(MagicItem噪声过滤)", () => {
        const extractorSrc = readFile("src/BobCoach/GameStateExtractor.cs");
        if (!extractorSrc.includes("Zone6NewNonTrinketCountThisFrame"))
            return "extractor must expose non-trinket new zone6 count — trinket pool floods zone6 (+11 at T3) and masks the 3-4 discover window";
        if (!extractorSrc.includes("newIds=["))
            return "DiscoverGate SKIP diagnostic must list new entity cardIds for attribution";
        // 07062242: fallback 判定升级为去重候选数(Zone6NewDistinctCandidateCount), 见对应护栏
    });

    // ── 07062157 修复护栏 ──

    test("[07062157][Discover] boardLeave 用3秒等待窗口而非同帧zone6要求", () => {
        const pluginSrc = readFile("src/BobCoach/BobCoachPlugin.cs");
        if (!pluginSrc.includes("_boardLeaveDiscoverPendingUntil"))
            return "board-leave discover must open a pending window — discover candidates enter zone6 1-2 frames after the sell, same-frame check always misses";
        if (!pluginSrc.includes("boardShrankWithSource"))
            return "board shrink with discover source must arm the pending window";
        if (!pluginSrc.includes("DIAG BoardLeaveCheck"))
            return "board shrink frames must log unconditional attribution diagnostics";
    });

    test("[07062157][Discover] 出售发现源 CardId 白名单 + SKIP诊断每回合重置", () => {
        const pluginSrc = readFile("src/BobCoach/BobCoachPlugin.cs");
        const extractorSrc = readFile("src/BobCoach/GameStateExtractor.cs");
        if (!pluginSrc.includes("DiscoverSourceCardWhitelist") || !pluginSrc.includes("BG24_715"))
            return "BoardHasDiscoverSource must whitelist known sell-to-discover cards (耐心的侦查员 BG24_715)";
        if (!extractorSrc.includes("_zone6DiagTurn"))
            return "DiscoverGate SKIP diagnostic budget must reset per turn — 8-cap exhausted at T3 left the sell moment blind";
    });

    // ── 07062242 修复护栏 ──

    test("[07062242][Discover] 手牌减少(法术/战吼发现)使用3秒等待窗口", () => {
        const pluginSrc = readFile("src/BobCoach/BobCoachPlugin.cs");
        if (!pluginSrc.includes("_handDiscoverPendingUntil"))
            return "hand-decrease discover must open a pending window — spell discover candidates enter zone6 1-2 frames late (搜寻时光/三连奖励法术 zero-trigger root cause)";
    });

    test("[07062242][Discover] fallback 按去重候选cardId数判定(每候选2实体)", () => {
        const extractorSrc = readFile("src/BobCoach/GameStateExtractor.cs");
        const pluginSrc = readFile("src/BobCoach/BobCoachPlugin.cs");
        if (!extractorSrc.includes("Zone6NewDistinctCandidateCount"))
            return "extractor must expose distinct candidate cardId count — real discover batch was 3 candidates × 2 entities = 6 new entities, entity-count window 3-4 missed it";
        if (!pluginSrc.includes("Zone6NewDistinctCandidateCount >= 3"))
            return "zone6 fallback must gate on distinct candidate count";
    });

    test("[07071644][Panel] 饰品/发现推荐面板移至屏幕左侧(避开中央三选一交互区)", () => {
        const overlaySrc = readFile("src/BobCoach/OverlayRenderer.cs");
        const centered = (overlaySrc.match(/\(canvasW - panelTextWidth\) \/ 2/g) || []).length;
        if (centered > 0)
            return `面板不应再水平居中(07071644: 中央与游戏三选一交互区重叠遮挡), 发现 ${centered} 处居中 → 改屏幕左侧`;
        // 三个面板位置改由 F10 校准配置驱动(Config.PanelOffsetX/Y), 不再硬编码
        const cfgDriven = (overlaySrc.match(/_calc\.Config\.PanelOffsetX/g) || []).length;
        if (cfgDriven < 4)
            return `三个选择面板须从 Config.PanelOffsetX 读位置(F10可校准) (found ${cfgDriven}/3)`;
    });

    test("[07071644][Calib] 推荐面板位置/缩放可 F10 校准[9]", () => {
        const cfg = readFile("src/BobCoach/Core/LayoutConfig.cs");
        if (!cfg.includes("PanelOffsetX") || !cfg.includes("PanelOffsetY") || !cfg.includes("PanelScale"))
            return "LayoutConfig 须有 PanelOffsetX/Y/Scale 字段";
        const overlaySrc = readFile("src/BobCoach/OverlayRenderer.cs");
        if ((overlaySrc.match(/new ScaleTransform\(sc, sc\)/g) || []).length < 4)
            return "三个面板须应用 PanelScale 的 ScaleTransform";
        const calib = readFile("src/BobCoach/Core/CalibrationOverlay.cs");
        if (!calib.includes('_selectedZone = "Panel"'))
            return "校准模式须支持 [9] Panel 目标";
        if (!calib.includes("_config.PanelOffsetX += dx") || !calib.includes("_config.PanelScale"))
            return "校准须支持面板位置(方向键)+缩放(+/-)";
        if (!calib.includes("case Key.G") || !/_step\s*=\s*_step\s*>=\s*4\.0/.test(calib))
            return "校准须支持 G 键切换微调步长(4/2/1/0.5px)以精确对齐卡槽";
        // 07071644: 校准按键须在 BobCoachPlugin 的 poll 白名单里分发, 否则 HandleKey 的 case 收不到(D9/G曾漏)
        const plugin = readFile("src/BobCoach/BobCoachPlugin.cs");
        if (!/IsKeyDown\(System\.Windows\.Input\.Key\.D9\)/.test(plugin))
            return "BobCoachPlugin 校准按键轮询须分发 D9(否则按9选面板无效)";
        if (!/IsKeyDown\(System\.Windows\.Input\.Key\.G\)/.test(plugin))
            return "BobCoachPlugin 校准按键轮询须分发 G(否则步长切换无效)";
    });

    test("[07071806] EntityChoices 选择完成靠 SendChoices 信号(面板即时清除)", () => {
        const src = readFile("src/BobCoach/Core/PowerLogParser.cs");
        if (!src.includes('!line.Contains("SendChoices")'))
            return "行过滤须放行 SendChoices(否则选择完成信号被丢弃, chosen=0, 面板残留数秒与后续面板重叠)";
        if (!src.includes('line.Contains("SendChoices()")'))
            return "ParseLine 须分发 SendChoices";
        const si = src.indexOf('else if (line.Contains("SendChoices()"))');
        const sEnd = src.indexOf("\n        private PLEvent ParseTagChange(", si + 10);
        const sblock = si >= 0 ? src.slice(si, sEnd > si ? sEnd : si + 2800) : "";
        if (!sblock.includes("m_chosenEntities") || !sblock.includes("PowerLogChoiceCompletion"))
            return "SendChoices 分支须解析 choiceId + chosen entity 为 typed completion";
        if (!sblock.includes("ChoiceCompleted?.Invoke(completion)"))
            return "SendChoices 分支须 raise typed ChoiceCompleted(按choiceId即时清匹配面板)";
    });

    test("[07072158] 卡宽/间隙改全局(调一次全等级生效) + 发现面板不强制超时", () => {
        const calib = readFile("src/BobCoach/Core/CalibrationOverlay.cs");
        // 07072158: 卡宽/间隙改全局(游戏卡尺寸不随等级变, 分档要调6次且默认档偏差), 调一次全tier生效, 清分档
        if (!calib.includes("_config.ShopCardWidthPct = Math.Max"))
            return "卡宽须调全局 ShopCardWidthPct(不再分档, 否则T6等未调档偏差)";
        if (!calib.includes("_config.ShopCardGap = Math.Max"))
            return "间隙须调全局 ShopCardGap(不再分档)";
        if (!/TierCardWidthPct\[k\] = 0/.test(calib) || !/TierCardGap\[k\] = 0/.test(calib))
            return "调全局卡宽/间隙时须清分档数组避免覆盖";
        const plugin = readFile("src/BobCoach/BobCoachPlugin.cs");
        // R234/R239(8/23): 恒 false 演进为权威区分——Power.log 权威发现(choiceId+来源卡)不强制超时
        // (防阮大师等慢选消失), 陈旧复发批次才 8s 超时兜底; 意图仍为"权威发现不强制超时靠选完关闭"
        if (!/_discoverPanelState[\s\S]{0,200}enforceMaxActive: !discoverHasAuthority/.test(plugin))
            return "发现面板应 enforceMaxActive:!discoverHasAuthority(Power.log权威发现不强制超时, 陈旧批次8s兜底)";
        if (!/bool discoverHasAuthority = _discoverBatchFromLog != null/.test(plugin)
            || !/_discoverBatchFromLog\.ChoiceId >= 0/.test(plugin)
            || !/!string\.IsNullOrEmpty\(_discoverBatchFromLog\.SourceCardId\)/.test(plugin))
            return "discoverHasAuthority 须由 choiceId>=0 + 来源卡非空判定(权威=不超时, 陈旧=8s兜底)";
    });

    test("[5B.3H] 法术购买费用接受实时0费并对未知费用失败关闭", () => {
        const ae = readFile("src/BobCoach/Core/ActionEnumerator.cs");
        const rules = readFile("src/BobCoach/Core/GameRuleEvaluator.cs");
        if (!ae.includes("GameRuleEvaluator.GetPurchaseCost")
            || !/card\.Cost\s*>=\s*0\s*\?\s*card\.Cost\s*:\s*int\.MaxValue/.test(rules))
            return "法术公共费用入口必须接受0费，并让负值未知费用不可购买";
        const ext = readFile("src/BobCoach/GameStateExtractor.cs");
        if (!ext.includes("int cost = purchaseFact.Cost"))
            return "ExtractShopMinions must preserve the resolved realtime/local cost";
        if (ext.includes("DIAG ShopSpell"))
            return "resolved local spell facts must not be written to diagnostic logs";
    });

    test("[07072158][#1] 正常商店与购买后按实际卡数居中; 补货瞬态保留理论槽位", () => {
        const p = readFile("src/BobCoach/BobCoachPlugin.cs");
        if (!p.includes("_shopLayoutSlotPolicy.Resolve(")
            || !p.includes("int slotCountForLayout = layoutDecision.LayoutCount"))
            return "初始商店渲染必须消费布局策略决策";
        const o = readFile("src/BobCoach/OverlayRenderer.cs");
        if (!o.includes("int layoutCount = layoutDecision.LayoutCount"))
            return "SyncShopTagPositions 必须复用初始渲染的布局决策";
    });

    test("[07062242][Refresh] planHash 含金币归零边界(残留光晕清除)", () => {
        const pluginSrc = readFile("src/BobCoach/BobCoachPlugin.cs");
        if (!pluginSrc.includes("\"G0\" : \"G+\""))
            return "planHash must include gold-zero boundary — gold spent to 0 with otherwise-identical plan never re-dispatched, stale refresh glow lingered (T8 末段)";
    });

    test("[ShopTag] 低金币保护按单卡可支付过滤, 不清空全部评分", () => {
        const pluginSrc = readFile("src/BobCoach/BobCoachPlugin.cs");
        const actionSrc = readFile("src/BobCoach/Core/ActionEnumerator.cs");
        const rulesSrc = readFile("src/BobCoach/Core/GameRuleEvaluator.cs");
        if (pluginSrc.includes("if (state.Gold < 3)\n            {\n                cardScores?.Clear();"))
            return "gold<3 must not clear all shop scores; low-cost spells and first-buy-free need per-card filtering";
        if (!pluginSrc.includes("FilterAffordableShopScores") || !pluginSrc.includes("IsShopMarkerAffordable"))
            return "missing per-card affordability filter for shop scores and rendered markers";
        if (!pluginSrc.includes("GameRuleEvaluator.GetPurchaseCost")
            || !rulesSrc.includes("FirstMinionPurchaseCost") || !rulesSrc.includes("card.Cost"))
            return "affordability filter must account for first-buy-free and spell/card costs";
        if (!actionSrc.includes("Type = isSpell ? ActionType.BuySpell : ActionType.BuyMinion"))
            return "ActionEnumerator must preserve BuySpell action type";
    });

    test("[Discover] 暗月奖品周期回合显式打开发现扫描门控", () => {
        const pluginSrc = readFile("src/BobCoach/BobCoachPlugin.cs");
        if (!pluginSrc.includes("GetScheduledDiscoverKind")
            || !pluginSrc.includes("ScheduledGrantEvaluator.GetDue"))
            return "missing unified scheduled prize discover trigger";
        if (pluginSrc.includes("IsScheduledPrizeDiscoverTurn")
            || pluginSrc.includes("PrizeEveryNTurns"))
            return "Darkmoon prize gate still reads legacy ActiveAnomaly schedule fields";
        if (!pluginSrc.includes('DiscoverTrigger: source=prize'))
            return "prize trigger should be logged distinctly from triple discover";
    });

    test("[LocalIdentity] 本地Controller跨战斗保持稳定", () => {
        if (!extractorSrc.includes("FindKnownLocalHero")
            || !/if \(_localPlayerCtrl <= 0 && playerHero != null\)/.test(extractorSrc))
            return "extractor can replace the local controller with the first in-play combat hero";
        if (!extractorSrc.includes("return null")
            || !extractorSrc.includes("_localPlayerCtrl > 0"))
            return "missing fail-closed path when the known local hero is temporarily unavailable";
    });

    test("[UpgradePrize] 权威Choice每批最多claim一次", () => {
        const pluginSrc = readFile("src/BobCoach/BobCoachPlugin.cs");
        if (!pluginSrc.includes("_claimedUpgradePrizeChoiceIds")
            || !pluginSrc.includes("batch.SourceCardId"))
            return "upgrade-prize claim is not bound to the authoritative source choice";
        if (!pluginSrc.includes("_claimedUpgradePrizeChoiceIds.Add(batch.ChoiceId)"))
            return "the same upgrade-prize choice can claim multiple pending occurrences";
    });

    test("[ShopTag] 补牌型商店使用实际可见槽位", () => {
        const pluginSrc = readFile("src/BobCoach/BobCoachPlugin.cs");
        const gameStateSrc = readFile("src/BobCoach/Core/GameState.cs");
        const simulatorSrc = readFile("src/BobCoach/Core/Simulator.cs");
        if (!gameStateSrc.includes("ReplenishingShopActive"))
            return "GameState must expose replenishing shop state";
        if (!extractorSrc.includes('BG30_MagicItem_841') || !extractorSrc.includes("state.ReplenishingShopActive"))
            return "extractor must detect Glowing Gauntlet / 辐光护手";
        if (!extractorSrc.includes('BG34_Treasure_917') || !extractorSrc.includes("UpdateTimewarpedNewRecruitState"))
            return "extractor must detect Timewarped New Recruit / 时空扭曲物色新人";
        if (!simulatorSrc.includes("ReplenishingShopActive = src.ReplenishingShopActive"))
            return "Simulator.ShallowCopy must preserve replenishing shop state";
        const slotPolicySrc = readFile("src/BobCoach/Core/ShopLayoutSlotPolicy.cs");
        if (!pluginSrc.includes("state.ReplenishingShopActive")
            || !slotPolicySrc.includes("if (replenishingShopActive)")
            || !slotPolicySrc.includes("new ShopLayoutDecision(Math.Max(1, visibleCount), true)"))
            return "shop render must use actual visible count for replenishing shops";
        if (!pluginSrc.includes("renderShopPosition"))
            return "shop render must remap raw slot to dense visible slot for replenishing shops";
    });

    test("[Lifecycle] 发现面板有 token/衍生卡过滤", () => {
        // Check filters in ExtractDiscoverOptionsStrict
        if (!extractorSrc.includes("_t") || !extractorSrc.includes("t2")) return "缺少 token 过滤";
        if (!extractorSrc.includes("PREMIUM")) return "缺少金色卡过滤";
    });

    test("[Lifecycle] 发现面板有战斗衍生物防护", () => {
        // Check combat cooldown
        if (!extractorSrc.includes("combat") && !extractorSrc.includes("Combat")) {
            warn("[Lifecycle] ExtractDiscoverOptionsStrict 可能缺战斗衍生物防护", "");
        }
    });

    test("[Lifecycle] 饰品面板有 InPlay 过滤 (不限ctrl)", () => {
        if (!extractorSrc.includes("IsInPlay")) return "缺少 IsInPlay 过滤";
        // The comment says "不限ctrl" — verify the filter doesn't check controller
        const inPlaySection = extractorSrc.slice(
            extractorSrc.indexOf("IsInPlay") - 200,
            extractorSrc.indexOf("IsInPlay") + 100
        );
        // Should filter on IsInPlay directly, not ctrl==localplayer
        if (inPlaySection.includes("ctrl ==") || inPlaySection.includes("CONTROLLER")) {
            warn("[Lifecycle] InPlay 过滤仍包含 ctrl 检查，可能过滤过度", "");
        }
    });

    test("[DiscoverLifecycle] strict zone6 extraction rejects upgrade/dark gift control IDs", () => {
        const start = extractorSrc.indexOf("private List<Engine.TrinketOption> ExtractDiscoverOptionsStrict(");
        const end = extractorSrc.indexOf("private List<Engine.TrinketOption> ExtractDiscoverOptions(");
        const strictBody = extractorSrc.slice(start, end);
        const required = [
            'e.CardId.Contains("_Button")',
            'e.CardId.StartsWith("TB_BaconShopTechUp"',
            'e.CardId.StartsWith("BG36_MidGameEffect_"',
            'e.CardId.Contains("DarkGift")',
            'e.CardId.StartsWith("BG36_Synthetic"'
        ];
        const missing = required.filter((pattern) => !strictBody.includes(pattern));
        if (missing.length > 0) {
            return "strict discover is missing filters: " + missing.join(", ");
        }
    });
}

// ============================================================================
// [5.5] PanelState 状态机复活 — 锁死 struct 拷贝 bug 不回归 (2026-06-10)
// ============================================================================
{
    const extractorSrc = readFile("src/BobCoach/GameStateExtractor.cs");
    const pluginSrc = readFile("src/BobCoach/BobCoachPlugin.cs");
    const gameStateSrc = readFile("src/BobCoach/Core/GameState.cs");
    const smPath = path.join(ROOT, "src/BobCoach/Core/PanelStateMachine.cs");
    const targetSmPath = path.join(ROOT, "src/BobCoach/Core/UiTargetStateMachine.cs");

    test("[StateMachine] PanelStateMachine.cs 存在", () => {
        if (!fs.existsSync(smPath)) return "缺少 Core/PanelStateMachine.cs";
    });

    test("[TargetStateMachine] UiTargetStateMachine.cs 存在并由插件持有", () => {
        if (!fs.existsSync(targetSmPath)) return "缺少 Core/UiTargetStateMachine.cs";
        if (!pluginSrc.includes("_discoverTargetState") || !pluginSrc.includes("_trinketTargetState"))
            return "BobCoachPlugin 未持有目标对象状态机";
        if (!pluginSrc.includes("AdvanceUiTargets"))
            return "BobCoachPlugin 未在面板状态机前推进目标状态机";
    });

    test("[StateMachine] Advance 用 ref PanelState 写回 (struct 副本 bug 根治)", () => {
        const smSrc = fs.readFileSync(smPath, "utf-8");
        if (!/Advance\s*\(\s*ref\s+PanelState/.test(smSrc))
            return "Advance 未用 ref PanelState — struct 拷贝不会写回";
    });

    test("[StateMachine] 死方法已删除 (防 struct 拷贝模式复活)", () => {
        if (extractorSrc.includes("UpdateTrinketPanelState"))
            return "GameStateExtractor 仍有 UpdateTrinketPanelState (死代码复活)";
        if (extractorSrc.includes("UpdateDiscoverPanelState"))
            return "GameStateExtractor 仍有 UpdateDiscoverPanelState (死代码复活)";
    });

    test("[StateMachine] GameState 已删每帧重建的面板字段", () => {
        if (/public\s+PanelState\s+TrinketPanel/.test(gameStateSrc))
            return "GameState 仍有 TrinketPanel 字段 (每帧重建会重置状态机)";
        if (/public\s+PanelState\s+DiscoverPanel/.test(gameStateSrc))
            return "GameState 仍有 DiscoverPanel 字段";
    });

    test("[StateMachine] 状态机由插件持久持有 (跨帧存活)", () => {
        if (!/_trinketPanelState/.test(pluginSrc)) return "缺少 _trinketPanelState 持久字段";
        if (!/_discoverPanelState/.test(pluginSrc)) return "缺少 _discoverPanelState 持久字段";
    });

    test("[StateMachine] Expired 自闭环回 Idle (防卡死: 不依赖消费方写回)", () => {
        const smSrc = fs.readFileSync(smPath, "utf-8");
        // 定位 Expired 分支, 必须 Transition 到 Idle, 而非裸 break(否则进战斗到 Expired 永久卡死)
        const idx = smSrc.indexOf("case PanelPhase.Expired:");
        if (idx < 0) return "找不到 Expired 分支";
        const block = smSrc.slice(idx, idx + 400);
        if (!/Transition\(PanelPhase\.Idle\)/.test(block))
            return "Expired 分支未 Transition 回 Idle — 消费方被早返回跳过时状态机会永久卡死";
    });

    test("[StateMachine] 消费端不写回状态机 (防 BeginInvoke stale-clobber)", () => {
        // DispatchRender 是延迟回调, 写回 new PanelState 会抹掉新一帧已推进的状态
        if (/_trinketPanelState\s*=\s*new\s+PanelState/.test(pluginSrc))
            return "消费端仍写回 _trinketPanelState (stale-clobber 风险)";
        if (/_discoverPanelState\s*=\s*new\s+PanelState/.test(pluginSrc))
            return "消费端仍写回 _discoverPanelState";
    });

    test("[StateMachine] 消费端读插件字段而非 state.TrinketPanel", () => {
        if (pluginSrc.includes("state.TrinketPanel")) return "消费端仍读 state.TrinketPanel (已删字段)";
        if (pluginSrc.includes("state.DiscoverPanel")) return "消费端仍读 state.DiscoverPanel";
    });

    test("[StateMachine] 每帧 Advance 在 planHash 门控之前", () => {
        const advIdx = pluginSrc.indexOf("PanelStateMachine.Advance");
        const hashIdx = pluginSrc.indexOf("if (planHash == _lastRenderedPlanHash)");
        if (advIdx < 0) return "未调用 PanelStateMachine.Advance";
        if (hashIdx >= 0 && advIdx > hashIdx)
            return "Advance 在 planHash 门控之后 — Fading→Expired 计时不走";
    });

    test("[StateMachine] 跨局重置面板状态机回 Idle", () => {
        // 重置点应 new PanelState(...Idle...) 两个状态机
        const resetCount = (pluginSrc.match(/_trinketPanelState\s*=\s*new\s+Engine\.PanelState/g) || []).length;
        if (resetCount < 1) return "缺少跨局重置 _trinketPanelState";
    });

    test("[StateMachine] planHash 含面板 Phase (相位变化触发重绘)", () => {
        const hashSection = pluginSrc.slice(
            pluginSrc.indexOf("string planHash ="),
            pluginSrc.indexOf("string planHash =") + 2200
        );
        if (!hashSection.includes("_trinketPanelState.Phase") && !hashSection.includes("_discoverPanelState.Phase"))
            return "planHash 未含面板 Phase — 相位变化不触发重绘";
    });

    test("[ScheduledGrant] 定时随从发现兜底读取统一有效规则", () => {
        const pluginSrc = readFile("src/BobCoach/BobCoachPlugin.cs");
        if (!pluginSrc.includes("ScheduledGrantEvaluator.GetDue"))
            return "scheduled discover gate still ignores EffectiveGameRules schedules";
        if (!pluginSrc.includes('"golden_minion_discover"'))
            return "Treasure Hoard due occurrence is not recognized as a discover source";
        if (!pluginSrc.includes('"tier_locked_minion_discover"'))
            return "Anomalous Journey opening choices are not recognized as scheduled discovers";
        if (!pluginSrc.includes("_triggeredScheduledDiscoverOccurrenceIds"))
            return "scheduled discover occurrences are not guarded against duplicate heuristic triggers";
        if (!pluginSrc.includes("MarkDueScheduledDiscoversTriggered(state)"))
            return "Power.log-owned scheduled choices can reopen through the heuristic after completion";
        if (pluginSrc.includes("_lastScheduledDiscoverTriggerAt"))
            return "scheduled discover gate still rearms the same occurrence on a timer";
        if (!pluginSrc.includes('source=scheduled'))
            return "scheduled golden discover should not be mislabeled as Darkmoon prize";
    });

    test("[0711][StateMachine] planHash 含发现/饰品批次身份 (连续同尺寸选择必须重绘)", () => {
        const hashSection = pluginSrc.slice(
            pluginSrc.indexOf("string planHash ="),
            pluginSrc.indexOf("string planHash =") + 2200
        );
        if (!hashSection.includes("_discoverTargetState.State.BatchId"))
            return "planHash 未含 discover BatchId — 连续两次3选1且面板保持Active时会吞掉第二批提示";
        if (!hashSection.includes("_trinketTargetState.State.BatchId"))
            return "planHash 未含 trinket BatchId — 连续同尺寸饰品选择可能复用旧提示";
    });

    test("[DiscoverGate] 显式触发同帧打开 gate 后必须重提取", () => {
        const evalIdx = pluginSrc.indexOf("private void EvaluateAndRender");
        const applyIdx = pluginSrc.indexOf("ApplyPowerLogDiscoverCandidates", evalIdx);
        const block = pluginSrc.slice(evalIdx, applyIdx);
        if (!block.includes("UpdateDiscoverTriggerWindow(state)"))
            return "EvaluateAndRender 未在同帧检测 Discover 显式触发";
        if (!block.includes("openedDiscoverGate") || !block.includes("_extractor.DiscoverTriggerActive = true"))
            return "显式触发后未同帧打开 DiscoverTriggerActive";
        if (countOccurrences(block, "state = _extractor.Extract();") < 1)
            return "打开 gate 后未重提取 state, Discover 仍会滞后一帧";
    });

    test("[DiscoverGate] Power.log candidates do not depend on zone6 gate", () => {
        const idx = pluginSrc.indexOf("private Engine.UiTargetSource ApplyPowerLogDiscoverCandidates");
        const block = pluginSrc.slice(idx, idx + 1200);
        if (!block.includes("_discoverBatchFromLog?.Candidates")
            || !block.includes("candidates.Count >= 2 && _discoverPanelActive"))
            return "Power.log discover candidates should be accepted from the Power.log panel flag";
        if (block.includes("candidates.Count <= 4"))
            return "typed Power.log discover still rejects five-option timewarp choices";
        if (block.includes("state.DiscoverGatePassed"))
            return "Power.log discover candidates are still blocked by zone6 gate";
    });

    test("[DiscoverLifecycle] explicit Power.log choice survives combat-to-recruit transition", () => {
        const idx = pluginSrc.indexOf("private Engine.UiTargetSource ApplyPowerLogDiscoverCandidates");
        const block = pluginSrc.slice(idx, idx + 1200);
        if (block.includes("state.Turn != _discoverLogTurn"))
            return "Power.log discover is still discarded when HDT advances the cached turn";
        const timeoutIdx = pluginSrc.indexOf("Discover flag: timeout after 8s");
        if (timeoutIdx >= 0)
            return "explicit choice flag still expires before combat-end treasure choices become visible";
    });

    test("[PowerLogDiscover] five-option batch can reach target state machine", () => {
        const idx = pluginSrc.indexOf("private Engine.UiTargetSnapshot BuildTargetSnapshot");
        const block = pluginSrc.slice(idx, idx + 2200);
        if (!block.includes("type == Engine.UiTargetType.Discover")
            || !block.includes("source == Engine.UiTargetSource.PowerLog")
            || !block.includes("? 5 : 4"))
            return "BuildTargetSnapshot still rejects the authoritative five-option timewarp discover batch";
        if (/int\s+maxOptions\s*=\s*4\s*;/.test(block))
            return "shared four-option cap still clears timewarp discover after its fifth candidate arrives";
        const scoreIdx = pluginSrc.indexOf("private List<Engine.TrinketHint> ScoreDiscoverOptions");
        const scoreBlock = pluginSrc.slice(scoreIdx, scoreIdx + 5000);
        if (scoreBlock.includes("Math.Min(4, scored.Count)"))
            return "timewarp discover scoring still hides the fifth candidate";
    });

    test("[PowerLogDiscover] combat overlap only uses authoritative discover path", () => {
        const policySrc = readFile("src/BobCoach/Core/CombatChoiceRenderPolicy.cs");
        const discoverRendererSrc = readFile("src/BobCoach/OverlayRenderer.cs");
        if (!policySrc.includes("batch.ChoiceId >= 0") || !policySrc.includes("batch.Candidates.Count >= 2"))
            return "combat discover policy does not require valid choiceId and at least two candidates";
        if (!pluginSrc.includes("CanRenderDiscoverDuringCombat"))
            return "plugin does not consume authoritative combat discover policy";
        if (!pluginSrc.includes("ShowAuthoritativeDiscoverHints"))
            return "plugin has no isolated authoritative discover render path";
        const publicDiscoverIdx = discoverRendererSrc.indexOf("public void ShowDiscoverHints");
        const internalDiscoverIdx = discoverRendererSrc.indexOf("internal void ShowAuthoritativeDiscoverHints");
        if (publicDiscoverIdx < 0 || internalDiscoverIdx < 0)
            return "discover renderer entry points are incomplete";
        const publicBlock = discoverRendererSrc.slice(publicDiscoverIdx, internalDiscoverIdx);
        if (!publicBlock.includes("if (IsCombat()) return;"))
            return "ordinary discover renderer lost its combat guard";
    });

    test("[TimewarpPurchase] separate event renders one compact affordable marker", () => {
        const watcherSrc = readFile("src/BobCoach/Core/PowerLogWatcher.cs");
        const rendererSrc = readFile("src/BobCoach/OverlayRenderer.cs");
        if (!watcherSrc.includes("TimewarpPurchaseOffered"))
            return "PowerLogWatcher does not forward the typed timewarp purchase batch";
        if (!pluginSrc.includes("OnPowerLogTimewarpPurchaseOffered")
            || !pluginSrc.includes("_timewarpPurchaseBatchFromLog"))
            return "plugin does not own a separate timewarp purchase lifecycle";
        const renderIdx = pluginSrc.indexOf("private void RenderTimewarpPurchaseHint");
        const renderBlock = pluginSrc.slice(renderIdx, renderIdx + 5000);
        if (renderIdx < 0 || !renderBlock.includes("SelectAffordableIndices"))
            return "timewarp purchase does not select affordable candidates";
        if (!renderBlock.includes("ShowTimewarpPurchaseRatings")
            || renderBlock.includes("ShowDiscoverHints"))
            return "timewarp purchase still uses the discover panel";
        const overlayIdx = rendererSrc.indexOf("internal void ShowTimewarpPurchaseRatings");
        const overlayBlock = rendererSrc.slice(overlayIdx, overlayIdx + 5000);
        if (overlayIdx < 0 || !overlayBlock.includes("GetChoiceCardRect"))
            return "timewarp purchase marker must use the central choice-card layout";
        if (!overlayBlock.includes("placeAbove"))
            return "timewarp purchase marker must move above the choice card to avoid game UI";
        if (!overlayBlock.includes('Tag = "timewarp_purchase"'))
            return "timewarp purchase marker is not isolated from ordinary ShopTag elements";
        const completionIdx = pluginSrc.indexOf("private void OnPowerLogChoiceCompleted");
        const completionBlock = pluginSrc.slice(completionIdx, completionIdx + 3000);
        if (!completionBlock.includes("_timewarpPurchaseBatchFromLog.ChoiceId == completion.ChoiceId")
            || !completionBlock.includes("_timewarpPurchaseClearRequested = true"))
            return "matching choice completion does not clear the timewarp purchase marker";
        const timerIdx = pluginSrc.indexOf("_combatWatchTimer.Tick +=");
        const timerBlock = pluginSrc.slice(timerIdx, timerIdx + 2200);
        if (!timerBlock.includes("_timewarpPurchaseClearRequested")
            || !timerBlock.includes("ClearTimewarpPurchaseHint"))
            return "timewarp purchase clear request is not consumed on the UI thread";
    });

    test("[PersistentUI] Dispatcher 回调内二次检查 combat", () => {
        const methodIdx = pluginSrc.indexOf("private void RefreshPersistentUI");
        const callbackIdx = pluginSrc.indexOf("new Action", methodIdx);
        const showIdx = pluginSrc.indexOf("_renderer.ShowStatusStrip", callbackIdx);
        if (methodIdx < 0 || callbackIdx < 0 || showIdx < 0)
            return "无法定位 RefreshPersistentUI Dispatcher 回调";
        const callbackGuard = pluginSrc.slice(callbackIdx, showIdx);
        if (!callbackGuard.includes("IsBattlegroundsCombatPhase"))
            return "RefreshPersistentUI 回调内缺少 combat 二次检查";
    });
}

// ============================================================================
// [5.51] 冻结提示严格遵守 UI面板需求 V2.0
// ============================================================================
{
    const visualizerSrc = readFile("src/BobCoach/Core/DecisionVisualizer.cs");

    test("[FreezeSpec] UI 冻结提示 T1-T3 不显示", () => {
        const idx = visualizerSrc.indexOf("冻结提示");
        const block = visualizerSrc.slice(idx, idx + 1800);
        if (!block.includes("state.Turn > 3"))
            return "冻结 UI 仍可能在 T3 显示, 与 V2.0 T1-T3 冻结分 x0.01 不一致";
    });

    test("[FreezeSpec] 禁止升本三连增强绕过缺钱条件", () => {
        if (visualizerSrc.includes("冻结→下回合合金"))
            return "仍存在升本+三连冻结增强文案, 会绕过 V2.0 仅缺钱冻结条件";
        if (visualizerSrc.includes("hasTripleCardInShop && isLevelUpAction"))
            return "仍存在升本+三连冻结增强逻辑";
    });
}

// ============================================================================
// [5.55] GoldTracker 金币追踪抽离 — 锁死纯类签名/无HDT耦合 (2026-06-11)
//   金币纯计算从 GameStateExtractor.TrackGold 抽离为 Core/GoldTracker.cs,
//   配合 test_gold_tracker.js 的序列测试. 此处静态断言锁住 C# 侧契约不回归.
// ============================================================================
{
    const gtPath = path.join(ROOT, "src/BobCoach/Core/GoldTracker.cs");
    const extractorSrc = readFile("src/BobCoach/GameStateExtractor.cs");

    test("[GoldTracker] Core/GoldTracker.cs 存在", () => {
        if (!fs.existsSync(gtPath)) return "缺少 Core/GoldTracker.cs";
    });

    test("[GoldTracker] 暴露 Advance(...) 纯方法", () => {
        const src = fs.readFileSync(gtPath, "utf-8");
        if (!src.includes("public int Advance(")) return "缺少 public int Advance(";
    });

    test("[GoldTracker] 暴露 Reset() (跨局重置)", () => {
        const src = fs.readFileSync(gtPath, "utf-8");
        if (!src.includes("public void Reset()")) return "缺少 public void Reset()";
    });

    test("[GoldTracker] 无 HDT (Core.Game) 耦合 (保持可测纯类)", () => {
        const src = fs.readFileSync(gtPath, "utf-8");
        // 剥离 /// 与 // 注释行后再查, 避免文档注释里的 "Core.Game" 字样误报
        const code = src.split("\n").filter(l => !l.trim().startsWith("//")).join("\n");
        if (code.includes("Core.Game")) return "GoldTracker 不应引用 Core.Game (HDT耦合应留在 extractor 薄封装层)";
    });

    test("[GoldTracker] bonusGold 一次性加金标志存在 (bug1: 死变量修复)", () => {
        const src = fs.readFileSync(gtPath, "utf-8");
        if (!src.includes("_bonusGoldApplied")) return "缺少 _bonusGoldApplied 一次性标志";
    });

    test("[GoldTracker] 自维护 lastUpgradeTurn (bug2: 摆脱外部写回时序)", () => {
        const src = fs.readFileSync(gtPath, "utf-8");
        if (!src.includes("_trackedLastUpgradeTurn")) return "缺少 _trackedLastUpgradeTurn 自维护字段";
        // 升本折扣应读自维护值, 不读外部 state.LastUpgradeTurn
        if (src.includes("state.LastUpgradeTurn")) return "GoldTracker 不应读 state.LastUpgradeTurn (时序bug根源)";
    });

    test("[GoldTracker] maxGold 公式 = Min(10, 2+turn) (off-by-one)", () => {
        const src = fs.readFileSync(gtPath, "utf-8");
        if (src.includes("3 + Math.Max(0, turn - 2)")) return "maxGold 仍是旧 off-by-one 公式";
        if (!src.includes("Math.Min(10, 2 + turn)")) return "缺少修正后的 Min(10, 2+turn) 公式";
    });

    test("[GoldTracker] GameStateExtractor 持有 GoldTracker 实例 (薄封装)", () => {
        if (!extractorSrc.includes("GoldTracker")) return "extractor 未引用 GoldTracker";
        if (!extractorSrc.includes("_goldTracker.Advance(")) return "TrackGold 未委托 _goldTracker.Advance";
    });

    test("[GoldTracker] 旧金币状态字段已迁出 extractor (防双份状态)", () => {
        // 这些字段已迁入 GoldTracker, extractor 不应再持有
        if (extractorSrc.includes("private int _selfTrackedGold"))
            return "extractor 仍持有 _selfTrackedGold (应迁入 GoldTracker)";
        if (extractorSrc.includes("private bool _freeRefreshUsed"))
            return "extractor 仍持有 _freeRefreshUsed (应迁入 GoldTracker)";
    });
}

// ============================================================================
// [5.6] 购买标签逐帧跟随 (2026-06-10)
// ============================================================================
{
    const overlaySrc = readFile("src/BobCoach/OverlayRenderer.cs");
    const pluginSrc = readFile("src/BobCoach/BobCoachPlugin.cs");

    test("[ShopTag] OverlayRenderer 有 SyncShopTagPositions 方法", () => {
        if (!/(?:public|internal) void SyncShopTagPositions/.test(overlaySrc))
            return "缺少 SyncShopTagPositions 方法";
    });

    test("[ShopTag] 标签元素绑定 entityId (ShopTagElement)", () => {
        if (!overlaySrc.includes("_shopTagElements")) return "缺少 _shopTagElements 注册列表";
        if (!overlaySrc.includes("class ShopTagElement")) return "缺少 ShopTagElement 结构";
    });

    test("[ShopTag] SyncShopTagPositions 离店淡出 (不再zonePos平移/不重建)", () => {
        const idx = overlaySrc.indexOf("void SyncShopTagPositions");
        const block = overlaySrc.slice(idx, idx + 3600);
        // 新设计(0611 06111155 问题1+4修复): zonePos买卖后跳号→平移会右偏/越界飘左上,
        // 已废除 zonePos 漂移跟随。位置由 planHash 全量重绘按密集名次处理, 此方法仅保留离店淡出。
        if (!block.includes("AnimateFadeOut")) return "应保留离店淡出";
        if (block.includes("canvas.Children.Add")) return "SyncShopTagPositions 不应重建元素";
        // 反向断言: 不应再用 ZONE_POSITION-1 做位置漂移(zPos - 1 平移是旧根因)
        if (!block.includes("List<Engine.MinionData> liveShopMinions"))
            return "SyncShopTagPositions 应接收 live ShopMinions 快照";
        if (!block.includes("GetShopCardRect(rawSlot, layoutCount)"))
            return "未按原始 rawSlot + layoutCount 计算目标槽位";
        if (!block.includes("Canvas.SetLeft") || !block.includes("Canvas.SetTop"))
            return "缺少逐帧平移";
    });

    test("[ShopTag] OnUpdate 调用 SyncShopTagPositions", () => {
        if (!pluginSrc.includes("SyncShopTagPositions(_cachedState.ShopMinions, _shopLayoutDecision)"))
            return "OnUpdate 未调用 SyncShopTagPositions";
    });

    test("[ShopTag] SyncShopTagPositions 不在渲染层读取 Core.Game.Entities 猜补牌机制", () => {
        const idx = overlaySrc.indexOf("void SyncShopTagPositions");
        const block = overlaySrc.slice(idx, idx + 1800);
        if (!block.includes("Engine.ShopLayoutDecision layoutDecision"))
            return "SyncShopTagPositions should accept the shared layout decision from the plugin";
        if (block.includes("Core.Game.Entities"))
            return "renderer should not inspect HDT entity dictionary while syncing tag positions";
    });

    test("[OfflineEngine] C#评分链保留 ShopPosition/EntityId/IsSpell", () => {
        const engineSrc = readFile("src/BobCoach/Core/DecisionEngine.cs");
        const visualizerSrc = readFile("src/BobCoach/Core/DecisionVisualizer.cs");
        if (!engineSrc.includes("ShopPosition = shopCard.Position") || !engineSrc.includes("EntityId = shopCard.EntityId"))
            return "C# shop scoring must preserve raw slot and entity id for UI labels";
        if (!engineSrc.includes("IsSpell = shopCard.IsSpell") && !engineSrc.includes("IsSpell = isSpell"))
            return "C# shop scoring must preserve spell flag";
        if (!visualizerSrc.includes("ShopPosition = cs.ShopPosition") || !visualizerSrc.includes("EntityId = cs.EntityId"))
            return "C# visual plan must carry shop slot and entity id to renderer";
    });

    test("[ShopTag] shop change Clear is cooldown-guarded", () => {
        if (!pluginSrc.includes("ShopUiClearCooldownMs"))
            return "shop UI clear cooldown is missing";
        if (!pluginSrc.includes("RequestShopUiRefresh(DateTime.Now)"))
            return "shop change path should go through RequestShopUiRefresh";
        const idx = pluginSrc.indexOf("private void RequestShopUiRefresh");
        const block = pluginSrc.slice(idx, idx + 900);
        if (!block.includes("_lastRenderedPlanHash = null"))
            return "shop refresh must still force next render";
        if (!block.includes("TotalMilliseconds < ShopUiClearCooldownMs"))
            return "shop clear cooldown guard is missing";
        if (!block.includes("_renderer.ClearNonPanel()") || !block.includes("_lastShopUiClearAt = now"))
            return "shop refresh helper must clear non-panel elements and record cooldown time";
        if (block.includes("_renderer.Clear()"))
            return "shop refresh must preserve the status root instead of performing a full clear";
    });

    test("[ShopTag] layout uses shop refresh tier, not post-upgrade tier", () => {
        if (!pluginSrc.includes("_shopLayoutSlotPolicy.LastRefreshTier"))
            return "shop tag layout should use the tier from the last shop refresh";
        if (!pluginSrc.includes("layoutTier") || !pluginSrc.includes("_renderer.SetCalcTier(layoutTier)"))
            return "shop render should pass layoutTier into the renderer";
    });

    test("[ShopTag] Clear 清空 _shopTagElements (防悬空)", () => {
        const idx = overlaySrc.indexOf("public void Clear(");
        const block = overlaySrc.slice(idx, idx + 700);
        if (!block.includes("_shopTagElements.Clear()"))
            return "Clear() 未清空 _shopTagElements";
    });
}

// ============================================================================
// [5] entity.Values.ToList() 安全性
// ============================================================================
{
    test("[ThreadSafe] OverlayRenderer.cs: game.Entities.Values 遍历前调用 ToList()", () => {
        const overlaySrc = readFile("src/BobCoach/OverlayRenderer.cs");
        const entitiesRefs = overlaySrc.match(/Entities\.Values/g) || [];
        const toListRefs = overlaySrc.match(/Entities\.Values\.ToList\(\)/g) || [];
        if (entitiesRefs.length > toListRefs.length) {
            warn("[ThreadSafe] " + (entitiesRefs.length - toListRefs.length) + " 处 Entities.Values 遍历可能未 ToList", "");
        }
    });

    test("[ThreadSafe] BobCoachPlugin.cs: game.Entities.Values 遍历前调用 ToList()", () => {
        const pluginSrc = readFile("src/BobCoach/BobCoachPlugin.cs");
        const entitiesRefs = pluginSrc.match(/Entities\.Values/g) || [];
        const toListRefs = pluginSrc.match(/Entities\.Values\.ToList\(\)/g) || [];
        if (entitiesRefs.length > toListRefs.length) {
            warn("[ThreadSafe] " + (entitiesRefs.length - toListRefs.length) + " 处 Entities.Values 遍历可能未 ToList", "");
        }
    });

    test("[ThreadSafe] GameStateExtractor.cs: game.Entities.Values 遍历前调用 ToList()", () => {
        const extractorSrc = readFile("src/BobCoach/GameStateExtractor.cs");
        const entitiesRefs = extractorSrc.match(/Entities\.Values/g) || [];
        const toListRefs = extractorSrc.match(/Entities\.Values\.ToList\(\)/g) || [];
        if (entitiesRefs.length > toListRefs.length) {
            warn("[ThreadSafe] " + (entitiesRefs.length - toListRefs.length) + " 处 Entities.Values 遍历可能未 ToList", "");
        }
    });
}

// ============================================================================
// [6] ShallowCopy 字段完备性
// ============================================================================
{
    const simulatorSrc = readFile("src/BobCoach/Core/Simulator.cs");
    const gameStateSrc = readFile("src/BobCoach/Core/GameState.cs");

    // 检查 _nodeHandIdx/_nodeHandReason/_nodeHeroPower 三个NodeBridge字段在ShallowCopy中出现
    test("[ShallowCopy] _nodeHandIdx 在 ShallowCopy 中赋值 (已修复)", () => {
        if (!simulatorSrc.includes("_nodeHandIdx")) return "ShallowCopy 中未找到 _nodeHandIdx";
    });
    test("[ShallowCopy] _nodeHandReason 在 ShallowCopy 中赋值 (已修复)", () => {
        if (!simulatorSrc.includes("_nodeHandReason")) return "ShallowCopy 中未找到 _nodeHandReason";
    });
    test("[ShallowCopy] _nodeHeroPower 在 ShallowCopy 中赋值 (已修复)", () => {
        if (!simulatorSrc.includes("_nodeHeroPower")) return "ShallowCopy 中未找到 _nodeHeroPower";
    });
}

// ============================================================================
// [7] ParseBrush 线程安全 — BrushHelper 统一实现
const brushHelperSrc = readFile("src/BobCoach/Core/BrushHelper.cs");
const overlaySrc = readFile("src/BobCoach/OverlayRenderer.cs");
const calibSrc   = readFile("src/BobCoach/Core/CalibrationOverlay.cs");

{
    test("[ParseBrush] BrushHelper.cs 存在且含线程锁", () => {
        if (!brushHelperSrc.includes("private static readonly object _lock")) return "缺少 _lock 字段";
        if (!brushHelperSrc.includes("lock (_lock)")) return "缺少 lock(_lock) 保护";
    });

    test("[ParseBrush] BrushHelper 有缓存上限 BRUSH_CACHE_MAX", () => {
        if (!brushHelperSrc.includes("BRUSH_CACHE_MAX")) return "缺少 BRUSH_CACHE_MAX";
        if (!brushHelperSrc.includes("_brushCache.Count >= BRUSH_CACHE_MAX")) return "未使用 BRUSH_CACHE_MAX 限制";
    });

    test("[ParseBrush] 空字符串返回 null (不缓存)", () => {
        if (!brushHelperSrc.includes("string.IsNullOrEmpty(hex)") || !brushHelperSrc.includes("return null"))
            return "空字符串未正确处理";
    });

    test("[ParseBrush] OverlayRenderer 调用 BrushHelper.ParseBrush", () => {
        // OverlayRenderer 不再含独立 _brushCache 定义，应委托给 BrushHelper
        if (overlaySrc.includes("private static readonly Dictionary<string, SolidColorBrush> _brushCache"))
            return "OverlayRenderer 仍有独立 _brushCache，未委托 BrushHelper";
        if (!overlaySrc.includes("Engine.BrushHelper.ParseBrush"))
            return "OverlayRenderer 未调用 BrushHelper.ParseBrush";
    });

    test("[ParseBrush] CalibrationOverlay 调用 BrushHelper.ParseBrush", () => {
        if (calibSrc.includes("private static readonly Dictionary<string, SolidColorBrush> _brushCache"))
            return "CalibrationOverlay 仍有独立缓存，未委托 BrushHelper";
        if (!calibSrc.includes("BrushHelper.ParseBrush"))
            return "CalibrationOverlay 未调用 BrushHelper.ParseBrush";
    });
}

{
    const pluginSrcDense = readFile("src/BobCoach/BobCoachPlugin.cs");
    const overlaySrcDense = readFile("src/BobCoach/OverlayRenderer.cs");

    test("[ShopTag][07072158] 初始渲染与逐帧跟随共享布局决策", () => {
        const renderIdx = pluginSrcDense.indexOf("int slotCountForLayout");
        const renderBlock = pluginSrcDense.slice(Math.max(0, renderIdx - 600), renderIdx + 900);
        if (!renderBlock.includes("layoutDecision.LayoutCount")
            || !renderBlock.includes("layoutDecision.UseDenseSlots"))
            return "shop render 须使用布局策略给出的槽位数量和dense模式";
        const syncIdx = overlaySrcDense.indexOf("void SyncShopTagPositions");
        const syncBlock = overlaySrcDense.slice(syncIdx, syncIdx + 1400);
        if (!syncBlock.includes("layoutDecision.LayoutCount")
            || !syncBlock.includes("layoutDecision.UseDenseSlots"))
            return "shop tag follow 须与初始渲染共享布局决策";
    });
}

// ============================================================================
// [0708] T21 UI fixes
// ============================================================================
{
    const pluginSrc = readFile("src/BobCoach/BobCoachPlugin.cs");

    test("[0708][DiscoverRender] Fading + no options must not SHOW discover panel", () => {
        if (!pluginSrc.includes("var authoritativeDiscover = _discoverPanelActive"))
            return "discover render must use the latest power-log batch";
        if (!pluginSrc.includes("(discoverPs.IsVisible\n                            && (state.DiscoverOptions?.Count ?? 0) >= 2)"))
            return "discover render must require visible panel and >=2 current options; Fading with empty candidates otherwise leaves stale tavern/recruit hints";
    });

    test("[0708][Gold] Dispatcher liveGold must not be overridden by HDT RESOURCES", () => {
        const idx = pluginSrc.indexOf("private void DispatchRender");
        const nextIdx = pluginSrc.indexOf("\n        private ", idx + "private void DispatchRender".length);
        const block = pluginSrc.slice(idx, nextIdx >= 0 ? nextIdx : pluginSrc.length);
        if (!block.includes("int liveGold = GetLiveGold(state);"))
            return "DispatchRender must read live gold from self-tracked live gold";
        if (block.includes("GameTag.RESOURCES"))
            return "DispatchRender still trusts HDT RESOURCES, which stays at turn-start during recruit (0708: 0 gold still suggested 3-cost HP/buy)";
    });

    test("[0708][Gold] ComputeStateHash must use cached self-tracked gold, not HDT RESOURCES", () => {
        const idx = pluginSrc.indexOf("private string ComputeStateHash()");
        const nextIdx = pluginSrc.indexOf("\n        private ", idx + "private string ComputeStateHash".length);
        const block = pluginSrc.slice(idx, nextIdx >= 0 ? nextIdx : pluginSrc.length);
        if (!block.includes("_cachedState?.Gold ?? 3"))
            return "ComputeStateHash must use _cachedState.Gold fallback so gold=0 remains part of the state hash";
        if (block.includes("GameTag.RESOURCES"))
            return "ComputeStateHash still reads HDT RESOURCES; in-turn recruit gold is unreliable";
    });
}

// ============================================================================
// [0809] Dark gift candidate panel + early-turn upgrade affordability
// ============================================================================
{
    const overlaySrc = readFile("src/BobCoach/OverlayRenderer.cs");
    const pluginSrc = readFile("src/BobCoach/BobCoachPlugin.cs");

    test("[0809][DarkGift] renderer exposes a candidate panel with combat guard", () => {
        const showIdx = overlaySrc.indexOf("internal void ShowDarkGiftCandidates(");
        if (showIdx < 0)
            return "OverlayRenderer must expose ShowDarkGiftCandidates";
        const block = overlaySrc.slice(showIdx, showIdx + 2600);
        if (!block.includes("if (IsCombat() && !renderDuringCombat) return;"))
            return "ShowDarkGiftCandidates must keep the combat guard while allowing dark gift explicit bypass";
        if (!block.includes('Tag = "dark_gift_panel"'))
            return "dark gift candidates must use an independent dark_gift_panel tag";
        if (!block.includes("_calc.Config.PanelOffsetX") || !block.includes("new ScaleTransform(sc, sc)"))
            return "dark gift candidate panel must use F10 calibration position and scale";
        if (!overlaySrc.includes("h.CardName ?? h.CardId"))
            return "dark gift candidate rows must show the parsed card name";
        if (!block.includes("List<Engine.TrinketHint> hints = null"))
            return "ShowDarkGiftCandidates must accept scored dark gift hints";
        if (block.includes("hint.Reason"))
            return "ShowDarkGiftCandidates must hide internal dark gift reason text";
    });

    test("[0809][DarkGift] clear lifecycle is independent and survives ClearNonPanel", () => {
        const clearIdx = overlaySrc.indexOf("internal void ClearDarkGiftCandidates(");
        if (clearIdx < 0)
            return "OverlayRenderer must expose ClearDarkGiftCandidates";
        const clearBlock = overlaySrc.slice(clearIdx, clearIdx + 1200);
        if (!clearBlock.includes('ClearTaggedElements("dark_gift_panel")'))
            return "ClearDarkGiftCandidates must clear only dark_gift_panel elements";
        if (!clearBlock.includes("_darkGiftPanelSig = null"))
            return "ClearDarkGiftCandidates must reset the dark gift panel signature";
        const nonPanelIdx = overlaySrc.indexOf("public void ClearNonPanel");
        const nonPanelBlock = overlaySrc.slice(nonPanelIdx, nonPanelIdx + 1200);
        if (!nonPanelBlock.includes('tag == "dark_gift_panel"'))
            return "ClearNonPanel must preserve the active dark gift candidate panel";
        const clearAllIdx = overlaySrc.indexOf("public void Clear(");
        const clearAllBlock = overlaySrc.slice(clearAllIdx, clearAllIdx + 1000);
        if (!clearAllBlock.includes("_darkGiftPanelSig = null"))
            return "full Clear must reset the dark gift panel signature";
    });

    test("[0809][DarkGift] plugin renders and clears candidates with dark gift status", () => {
        if (!pluginSrc.includes("_renderer.ShowDarkGiftCandidates("))
            return "plugin must render dark gift candidates while a dark gift choice is active";
        if (!pluginSrc.includes("_renderer.ClearDarkGiftCandidates()"))
            return "plugin must clear dark gift candidates when the dark gift choice ends";
    });

    test("[0809][DarkGift] candidate panel supports explicit render during combat", () => {
        const showIdx = overlaySrc.indexOf("internal void ShowDarkGiftCandidates(");
        if (showIdx < 0)
            return "OverlayRenderer must expose ShowDarkGiftCandidates";
        const block = overlaySrc.slice(showIdx, showIdx + 600);
        if (!block.includes("bool renderDuringCombat = false"))
            return "ShowDarkGiftCandidates must expose renderDuringCombat=false default";
        if (!block.includes("if (IsCombat() && !renderDuringCombat) return;"))
            return "ShowDarkGiftCandidates must skip only when combat guard is not explicitly bypassed";
    });

    test("[0809][DarkGift] plugin passes renderDuringCombat=true for dark gift candidates", () => {
        const renders = countOccurrences(pluginSrc, "_renderer.ShowDarkGiftCandidates(");
        const scored = countOccurrences(pluginSrc, "ScoreDarkGiftCandidates()");
        if (renders < 3)
            return `expected at least 3 dark gift candidate renders, found ${renders}`;
        if (scored < 3)
            return `expected each dark gift candidate render to pass scored hints, found ${scored}`;
    });

    test("[0809][DarkGift] plugin builds dark gift context from cached GameState", () => {
        const helperIdx = pluginSrc.indexOf("private Engine.DarkGiftTipContext BuildDarkGiftTipContext()");
        if (helperIdx < 0)
            return "BuildDarkGiftTipContext helper not found";
        const braceIdx = pluginSrc.indexOf("{", helperIdx);
        const closeIdx = pluginSrc.indexOf("\n        }\n", braceIdx);
        const block = pluginSrc.slice(helperIdx, closeIdx + 5);
        const required = [
            "var state = _cachedState",
            "if (state == null)",
            "StateAvailable = false",
            "GameActive = state.GameActive",
            'Phase = inCombat ? "combat" : (state.Phase ?? "")',
            "Turn = state.Turn",
            "TavernTier = state.TavernTier",
            "Gold = liveGold",
            "GetLiveGold(state)",
            "_powerLogPhase",
            "BoardMinionCount = state.BoardMinions?.Count ?? 0",
            "ShopMinionCount = state.ShopMinions?.Count ?? 0",
            "HandMinionCount = state.HandMinions?.Count ?? 0",
        ];
        const missing = required.filter(x => !block.includes(x));
        if (missing.length > 0)
            return "BuildDarkGiftTipContext missing: " + missing.join(", ");
    });

    test("[0809][DarkGift] all render paths pass dark gift context to status", () => {
        const expected = "_darkGiftTipCoordinator.GetStatus(DateTime.Now, BuildDarkGiftTipContext())";
        const actual = countOccurrences(pluginSrc, expected);
        if (actual < 3)
            return `expected 3 context-aware GetStatus calls, found ${actual}`;
        if (countOccurrences(pluginSrc, "_darkGiftTipCoordinator.GetStatus(DateTime.Now)") > 0)
            return "render paths must not call GetStatus without BuildDarkGiftTipContext";
    });

    test("[0809][DarkGift] ordinary discover/trinket guards remain unconditional", () => {
        const ordinaryIdx = overlaySrc.indexOf("public void ShowDiscoverHints(");
        if (ordinaryIdx < 0)
            return "ShowDiscoverHints not found";
        const ordinaryBlock = overlaySrc.slice(ordinaryIdx, ordinaryIdx + 900);
        if (!ordinaryBlock.includes("if (IsCombat()) return;"))
            return "ordinary discover renderer must keep an unconditional combat guard";
        const trinketIdx = overlaySrc.indexOf("public void ShowTrinketHints(");
        if (trinketIdx < 0)
            return "ShowTrinketHints not found";
        const trinketBlock = overlaySrc.slice(trinketIdx, trinketIdx + 900);
        if (!trinketBlock.includes("if (IsCombat()) return;"))
            return "trinket renderer must keep an unconditional combat guard";
    });

    test("[0809][Gold] levelUrgent requires affordable upgrade cost", () => {
        const levelUrgentIdx = pluginSrc.indexOf("bool levelUrgent =");
        if (levelUrgentIdx < 0)
            return "levelUrgent assignment not found";
        const semicolonIdx = pluginSrc.indexOf(";", levelUrgentIdx);
        const levelUrgentBlock = pluginSrc.slice(levelUrgentIdx, semicolonIdx);
        if (!levelUrgentBlock.includes("liveGold >= plan.UpgradeHint.Cost"))
            return "levelUrgent must require liveGold >= plan.UpgradeHint.Cost; critical urgency alone must not drive upgrade text at turn 1 with 0 gold";
    });

    test("[0809][Gold] justLeveled must not fire on turn 1 from the default LastUpgradeTurn", () => {
        const visualizerSrc = readFile("src/BobCoach/Core/DecisionVisualizer.cs");
        const justLeveledIdx = visualizerSrc.indexOf("bool justLeveled = state.Turn > 1 && state.LastUpgradeTurn == state.Turn;");
        if (justLeveledIdx < 0)
            return "justLeveled must require state.Turn > 1 so the default LastUpgradeTurn=1 cannot force Sprint at turn 1";
    });

    test("[0809][Gold] ShowLevelUpGlow must be guarded by liveGold >= upgrade cost", () => {
        const glowIdx = pluginSrc.indexOf("_renderer.ShowLevelUpGlow(");
        if (glowIdx < 0)
            return "_renderer.ShowLevelUpGlow call not found";
        const glowBlock = pluginSrc.slice(Math.max(0, glowIdx - 400), glowIdx + 500);
        if (!glowBlock.includes("if (plan.UpgradeHint != null && liveGold >= plan.UpgradeHint.Cost)"))
            return "ShowLevelUpGlow must render only when UpgradeHint exists and liveGold can cover its cost";
    });

    test("[0809][Gold] unaffordable upgrade must not render a shortage reason", () => {
        const glowIdx = pluginSrc.indexOf("_renderer.ShowLevelUpGlow(");
        if (glowIdx < 0)
            return "_renderer.ShowLevelUpGlow call not found";
        const glowBlock = pluginSrc.slice(Math.max(0, glowIdx - 600), glowIdx + 500);
        if (glowBlock.includes("需{0}费(差{1})"))
            return "unaffordable upgrade text must be removed; shortage guidance must not appear when liveGold < upgrade cost";
        if (glowBlock.includes("canAfford = liveGold >= plan.UpgradeHint.Cost"))
            return "ShowLevelUpGlow must not keep a canAfford branch that renders shortage reasons";
    });
}

// ============================================================================
// T25 Faelin passive choice + 0-coin/no-recruit gates
// ============================================================================
{
    const gameStateSrc = readFile("src/BobCoach/Core/GameState.cs");
    const simulatorSrc = readFile("src/BobCoach/Core/Simulator.cs");
    const goldSrc = readFile("src/BobCoach/Core/GoldTracker.cs");
    const extractorSrc = readFile("src/BobCoach/GameStateExtractor.cs");
    const pluginSrc = readFile("src/BobCoach/BobCoachPlugin.cs");
    const parserSrc = readFile("src/BobCoach/Core/PowerLogParser.cs");
    const watcherSrc = readFile("src/BobCoach/Core/PowerLogWatcher.cs");

    test("[T25] GameState exposes passive/no-recruit flags and Simulator copies them", () => {
        if (!gameStateSrc.includes("PassiveChoiceActive"))
            return "GameState must expose PassiveChoiceActive";
        if (!gameStateSrc.includes("NormalRecruitAvailable"))
            return "GameState must expose NormalRecruitAvailable";
        if (!simulatorSrc.includes("PassiveChoiceActive = src.PassiveChoiceActive"))
            return "Simulator.DetachedCopy must preserve PassiveChoiceActive";
        if (!simulatorSrc.includes("NormalRecruitAvailable = src.NormalRecruitAvailable"))
            return "Simulator.DetachedCopy must preserve NormalRecruitAvailable";
    });

    test("[T25] GoldTracker and TrackGold carry NormalRecruitAvailable fail closed", () => {
        if (!goldSrc.includes("bool normalRecruitAvailable = true"))
            return "GoldTracker.Advance must accept normalRecruitAvailable=true default";
        if (!goldSrc.includes("if (!normalRecruitAvailable)")
            || !goldSrc.includes("return _selfTrackedGold;"))
            return "GoldTracker.Advance must preserve known gold or unknown -1 outside normal recruit";
        const trackIdx = extractorSrc.indexOf("private int TrackGold(");
        const trackEnd = extractorSrc.indexOf("private int _goldDiagCount", trackIdx);
        const trackBody = trackIdx >= 0 ? extractorSrc.slice(trackIdx, trackEnd > trackIdx ? trackEnd : trackIdx + 4000) : "";
        if (!trackBody.includes("state.NormalRecruitAvailable"))
            return "TrackGold must pass state.NormalRecruitAvailable into GoldTracker";
        if (!extractorSrc.includes("trackedGold < 0 && state.NormalRecruitAvailable"))
            return "early-turn fallback must be gated by NormalRecruitAvailable";
    });

    test("[T25] ReadGold must not fallback to 3", () => {
        const readGoldIdx = extractorSrc.indexOf("private static int ReadGold()");
        const readGoldBlock = readGoldIdx >= 0 ? extractorSrc.slice(readGoldIdx, readGoldIdx + 500) : "";
        if (readGoldBlock.includes("return 3;"))
            return "ReadGold fallback must not return 3";
        if (!readGoldBlock.includes("return -1;"))
            return "ReadGold fallback must be explicit unknown/-1";
    });

    test("[T25] refresh affordability uses liveGold", () => {
        const idx = pluginSrc.indexOf("bool canAffordRefresh");
        const block = idx >= 0 ? pluginSrc.slice(idx, idx + 240) : "";
        if (!block.includes("liveGold >= liveRefreshCost"))
            return "refresh affordability must use liveGold";
        if (block.includes("state.Gold >= liveRefreshCost"))
            return "refresh affordability must not use stale state.Gold";
    });

    test("[ZeroGold][Refresh] refresh glow must fail closed at 0 gold without free refresh", () => {
        const idx = pluginSrc.indexOf("bool refreshAffordableByState");
        const block = idx >= 0 ? pluginSrc.slice(idx, idx + 260) : "";
        if (!block.includes("renderGold >= liveRefreshCost"))
            return "refresh UI must re-check captured state gold";
        if (!block.includes("state.FreeRefreshCount > 0"))
            return "refresh UI must allow free refresh";
        if (!pluginSrc.includes("!_lastLiveSafetySuppressRefresh"))
            return "refresh UI must honor LiveSafetyGate suppression";
    });

    test("[Phase2][Safety] live safety gate is wired into visible decisions", () => {
        if (!pluginSrc.includes("LiveSafetyGate.Evaluate"))
            return "BobCoachPlugin must invoke LiveSafetyGate";
        if (!pluginSrc.includes("liveSafety.SuppressUpgrade"))
            return "upgrade suppression must be applied to visible plan";
        if (!pluginSrc.includes("liveSafety.SuppressRefresh"))
            return "refresh suppression must be applied to visible plan";
        if (!pluginSrc.includes("liveSafety.SuppressBuy"))
            return "buy suppression must be applied to visible plan";
    });

    test("[T35][Refresh] disabled refresh gate clears the previous glow", () => {
        const plugin = readFile("src/BobCoach/BobCoachPlugin.cs");
        const block = plugin.slice(
            plugin.indexOf("bool showRefresh ="),
            plugin.indexOf("// 冻结提示")
        );
        if (!block.includes("ClearRefreshGlow"))
            return "showRefresh=false path does not clear refresh glow";
    });

    test("[T35][Refresh] refresh reason text shares the refresh tag", () => {
        const overlay = readFile("src/BobCoach/OverlayRenderer.cs");
        const block = overlay.slice(
            overlay.indexOf("public void ShowRefreshGlow"),
            overlay.indexOf("public void ShowFreezeGlow")
        );
        const reasonIndex = block.indexOf("reasonLabel");
        if (reasonIndex < 0)
            return "reason label missing";
        const reasonBlock = block.slice(reasonIndex, reasonIndex + 700);
        if (!reasonBlock.includes('Tag = "refresh_glow"'))
            return "refresh reason label is not cleared with the glow";
    });

    test("[Phase2][HeroSkill] visible hero suggestion uses data-driven decision service", () => {
        if (!pluginSrc.includes("HeroSkillDecisionService.Decide"))
            return "BobCoachPlugin must use HeroSkillDecisionService";
        if (!pluginSrc.includes("_heroSkillCatalog.TryGet"))
            return "BobCoachPlugin must query HeroSkillCatalog";
    });

    test("[T35][HeroPlaybook] visible hero guidance consumes the versioned playbook", () => {
        const engine = readFile("src/BobCoach/Core/DecisionEngine.cs");
        if (!pluginSrc.includes("HeroPlaybookDecisionService.Decide"))
            return "BobCoachPlugin does not consume the hero playbook service";
        if (!pluginSrc.includes("_heroPlaybook.GetProfileForPower"))
            return "BobCoachPlugin does not resolve playbook profiles by hero power";
        if (!engine.includes("_heroPlaybook.GetProfileForPower"))
            return "DecisionEngine does not score hero powers through the playbook";
    });

    test("[Phase2][Activate] activate hint reaches visible UI", () => {
        if (!pluginSrc.includes("plan.ActivateHint"))
            return "BobCoachPlugin must consume ActivateHint";
        if (!pluginSrc.includes("_renderer.ShowTargetPulse"))
            return "activate hint must render a target pulse";
        if (!pluginSrc.includes("liveGold >= plan.ActivateHint.Cost"))
            return "activate hint must be gated by liveGold";
    });

    test("[Phase3][Plan] recommendation plan is derived from final visual plan", () => {
        if (!pluginSrc.includes("RecommendationPlan.FromVisualPlan"))
            return "BobCoachPlugin must derive RecommendationPlan from final VisualPlan";
        if (!pluginSrc.includes("recommendationErrors.Count > 0"))
            return "BobCoachPlugin must validate RecommendationPlan consistency";
        if (!pluginSrc.includes("plan.Recommendation = recommendationPlan"))
            return "BobCoachPlugin must attach RecommendationPlan to final VisualPlan";
    });

    test("[T25] noActionNeeded covers passive/no-recruit and does not hide by passive hero power", () => {
        const idx = pluginSrc.indexOf("bool noActionNeeded = ");
        const block = idx >= 0 ? pluginSrc.slice(idx, idx + 600) : "";
        if (!block.includes("state.PassiveChoiceActive"))
            return "noActionNeeded must cover passive choice period";
        if (!block.includes("!state.NormalRecruitAvailable"))
            return "noActionNeeded must cover no normal recruit period";
        if (block.includes('HeroPowerType == "Passive"'))
            return "noActionNeeded must not hide guidance globally just because HeroPowerType is Passive";
    });

    test("[T25] parser/watcher/plugin expose passive choice lifecycle", () => {
        if (!parserSrc.includes("public event Action<PowerLogChoiceBatch> PassiveChoiceActive;")
            || !parserSrc.includes("public event Action<PowerLogChoiceCompletion> PassiveChoiceCompleted;")
            || !parserSrc.includes("BG22_HERO_201p"))
            return "PowerLogParser must expose and recognize the Faelin passive choice lifecycle";
        if (!watcherSrc.includes("public event Action<PowerLogChoiceBatch> PassiveChoiceActive;")
            || !watcherSrc.includes("public event Action<PowerLogChoiceCompletion> PassiveChoiceCompleted;")
            || !watcherSrc.includes("OnPassiveChoiceActive")
            || !watcherSrc.includes("OnPassiveChoiceCompleted"))
            return "PowerLogWatcher must forward passive choice lifecycle events";
        if (!pluginSrc.includes("_passiveChoiceActive")
            || !pluginSrc.includes("OnPowerLogPassiveChoiceActive")
            || !pluginSrc.includes("OnPowerLogPassiveChoiceCompleted"))
            return "BobCoachPlugin must own and handle passive choice lifecycle state";
    });
}

// ============================================================================
// [SellGold] Projected gold after one sell must feed buy/refresh/upgrade paths
// ============================================================================
{
    const pluginSrc = readFile("src/BobCoach/BobCoachPlugin.cs");
    const actionSrc = readFile("src/BobCoach/Core/ActionEnumerator.cs");
    const hardSrc = readFile("src/BobCoach/Core/Decision/HardConstraintFilter.cs");
    const engineSrc = readFile("src/BobCoach/Core/DecisionEngine.cs");
    const visualizerSrc = readFile("src/BobCoach/Core/DecisionVisualizer.cs");

    test("[SellGold] board-full buy remains legal when hand has space", () => {
        if (!actionSrc.includes("state.BoardMinions.Count < state.MaxBoardSlots")
            || !actionSrc.includes("state.HandMinions.Count < 10"))
            return "ActionEnumerator must allow buying a minion into hand when board is full";
        if (!hardSrc.includes("state.BoardMinions.Count >= state.MaxBoardSlots")
            || !hardSrc.includes("state.HandMinions.Count >= 10"))
            return "HardConstraintFilter must reject full-board buy only when hand is also full";
    });

    test("[SellGold] sell action is generated when one sale can fund a shop buy", () => {
        if (!actionSrc.includes("state.ShopMinions.Count > 0")
            || !actionSrc.includes("hasSellableMinion")
            || !actionSrc.includes("liveGold + 1 >= minBuyCost"))
            return "ActionEnumerator must generate sell candidates when shop exists and one sale funds a buy";
    });

    test("[SellGold] shop scoring projects a sell before an unaffordable buy", () => {
        if (!engineSrc.includes("CanAffordAfterOneSell"))
            return "DecisionEngine must expose projected one-sell affordability";
        if (!engineSrc.includes("simState = _sim.Simulate(state, new GameAction")
            || !engineSrc.includes("Type = ActionType.SellMinion"))
            return "DecisionEngine must simulate sell before scoring an after-sell buy";
    });

    test("[SellGold] visualizer keeps a sell marker for sell-to-buy paths", () => {
        if (!visualizerSrc.includes("HasAffordableSellTarget"))
            return "DecisionVisualizer must keep a sell marker only for an affordable sell-to-buy/upgrade target";
        if (!visualizerSrc.includes("FindWeakestSellCandidateIndex(state)"))
            return "DecisionVisualizer must fall back to a protected weakest sell candidate";
    });

    test("[SellGold] dispatcher lets sellPath drive buy/refresh/upgrade affordability", () => {
        if (!pluginSrc.includes("bool sellPath = plan.SellMarkers.Count > 0"))
            return "DispatchRender must recognize an existing sell marker as a sellPath";
        if (!pluginSrc.includes("levelOkViaSell"))
            return "DispatchRender must support upgrade affordability after one sell";
        if (!pluginSrc.includes("IsShopMarkerAffordableAfterSell"))
            return "DispatchRender must keep after-sell affordable shop markers";
        const sellTextIdx = pluginSrc.indexOf("&& (canBuyMinion || levelOk))");
        if (sellTextIdx < 0)
            return "sell text must render when projected buy/upgrade is possible";
        const sellTextBlock = pluginSrc.slice(sellTextIdx, sellTextIdx + 80);
        if (sellTextBlock.includes("liveGold >= 1"))
            return "sell text must not require current gold >= 1 when one sale funds the action";
    });

    test("[SellGold] FilterAffordableShopScores uses liveGold plus one-sell projection", () => {
        const filterIdx = pluginSrc.indexOf("private List<ShopCardScore> FilterAffordableShopScores");
        const filterBlock = pluginSrc.slice(filterIdx, filterIdx + 700);
        if (!filterBlock.includes("GetLiveGold(state)"))
            return "FilterAffordableShopScores must use liveGold";
        if (!filterBlock.includes("IsShopScoreAffordableAfterSell(state, score, liveGold)"))
            return "FilterAffordableShopScores must keep cards affordable after one sell";
        if (!pluginSrc.includes("IsShopScoreWorthAfterSell")
            || !pluginSrc.includes("FindFilterSellCandidateIndex"))
            return "FilterAffordableShopScores must require an actual worthwhile sell target";
    });

    test("[SellGold] zero gold never funds refresh by selling", () => {
        const idx = pluginSrc.indexOf("bool canRefresh = liveGold >= 1");
        const block = idx >= 0 ? pluginSrc.slice(idx, idx + 120) : "";
        if (!block.includes("state.FreeRefreshCount > 0"))
            return "canRefresh must still allow free refresh";
        if (block.includes("CanAffordAfterOneSell"))
            return "zero-gold refresh must not become affordable through a sell projection";
    });

    test("[SellGold] zero gold clears stale non-panel UI when no action exists", () => {
        if (!pluginSrc.includes("ZeroGoldClear: no spendable action; cleared non-panel UI"))
            return "zero-gold guard must clear stale buy/refresh UI when no actionable plan exists";
    });

    test("[SellGold] after-sell buy path requires an explicit shop marker", () => {
        const idx = pluginSrc.indexOf("bool canBuyMinion = liveGold >= minBuyCost");
        const block = idx >= 0 ? pluginSrc.slice(idx, idx + 180) : "";
        if (!block.includes("plan.ShopMarkers.Count > 0"))
            return "after-sell buy must not render unless a shop marker exists";
    });

    test("[SellGold][Professor] BG25_HERO_100p is a trusted ordinary discover source", () => {
        const parserSrc = readFile("src/BobCoach/Core/PowerLogParser.cs");
        const harnessSrc = readFile("tests/PowerLogChoiceBatchHarness.cs");
        if (!parserSrc.includes('"BG25_HERO_100p"'))
            return "PowerLogParser must trust professor discover source BG25_HERO_100p";
        if (!harnessSrc.includes('cardId=BG25_HERO_100p'))
            return "PowerLog harness must cover professor discover source BG25_HERO_100p";
    });

    test("[SellGold][Professor] resource discover skills do not require board target", () => {
        const skillSrc = readFile("src/BobCoach/Core/HeroSkillDecisionService.cs");
        if (skillSrc.includes('usePurpose == "Resource" && boardSize >= 7'))
            return "resource discover must not be blocked by full board";
        if (!skillSrc.includes('targetOk = hasValidTarget || usePurpose == "Economy"'))
            return "resource discover must be included in target-free purposes";
        if (!pluginSrc.includes('heroSkill.UsePurpose == "Resource"'))
            return "plugin must treat resource discover as target-free for hero skill suggestions";
    });

    test("[SellGold][EarlyTriple] turn-3 sell-to-triple survives the dispatcher gold gate", () => {
        const canAffordIdx = pluginSrc.indexOf("private bool CanAffordAfterOneSell");
        const canAffordBlock = pluginSrc.slice(canAffordIdx, canAffordIdx + 420);
        if (canAffordBlock.includes("state.Turn < 4"))
            return "turn-3 triple sells must not be blocked by a blanket turn-4 gate";
        if (!canAffordBlock.includes("HasPotentialSellAfterTurn(state)"))
            return "CanAffordAfterOneSell must route through the early-triple-aware sell projection";
        if (!pluginSrc.includes("HasEarlyTripleSellOpportunity"))
            return "plugin must recognize early triple shop opportunities for after-sell buys";
        const markerIdx = pluginSrc.indexOf("private bool IsShopMarkerWorthAfterSell");
        const markerBlock = pluginSrc.slice(markerIdx, markerIdx + 220);
        if (markerBlock.includes("state.Turn < 4"))
            return "IsShopMarkerWorthAfterSell must allow turn-3 triple targets";
    });

    test("[SellGold][EarlyTriple] visualizer keeps a turn-3 triple sell marker", () => {
        const affordIdx = visualizerSrc.indexOf("private bool CanAffordAfterOneSell");
        const affordBlock = visualizerSrc.slice(affordIdx, affordIdx + 420);
        if (affordBlock.includes("state.Turn < 4"))
            return "visualizer must not blanket-block turn-3 after-sell targets";
        if (!visualizerSrc.includes("HasEarlyTripleSellTarget"))
            return "visualizer must project an early triple sell target";
        if (!visualizerSrc.includes("IsVisualEarlyTripleSell"))
            return "visualizer must accept the actual early triple sell candidate";
    });

    test("[SellUI][StatusConsistency] status strip is rendered after plan pruning and hash includes final-ish status", () => {
        const statusIdx = pluginSrc.lastIndexOf("_renderer.ShowStatusStrip(status);");
        const noActionIdx = pluginSrc.indexOf("bool noActionNeeded = ");
        if (statusIdx < 0 || statusIdx < noActionIdx)
            return "status strip must be rendered after noActionNeeded/pruning, not before";
        const hashSection = pluginSrc.slice(
            pluginSrc.indexOf("string planHash ="),
            pluginSrc.indexOf("string planHash =") + 2200);
        if (!hashSection.includes("plan.Status.HintLine"))
            return "planHash must include status HintLine so stale sell/play text triggers redraw";
        if (!hashSection.includes("plan.SellMarkers.Count"))
            return "planHash must include sell marker count so marker clear forces redraw";
    });

    test("[Mainline] strategy planner is wired into decision engine and guides buy/level/sell", () => {
        const plannerSrc = readFile("src/BobCoach/Core/MainlineStrategyPlanner.cs");
        if (!plannerSrc.includes("class MainlineStrategyPlanner"))
            return "MainlineStrategyPlanner must exist";
        if (!plannerSrc.includes("Analyze(")
            || !plannerSrc.includes("Adjust(")
            || !plannerSrc.includes("ShouldSkipUpgrade("))
            return "mainline planner must expose analyze/adjust/upgrade-gate APIs";
        if (!plannerSrc.includes("ShouldBuyCoreFirst")
            || !plannerSrc.includes("MissingCoreCardNames")
            || !plannerSrc.includes("HasTripleInShop"))
            return "mainline planner must track core nodes, missing cores and triple nodes";
        if (!engineSrc.includes("_mainline = new MainlineStrategyPlanner"))
            return "DecisionEngine must own the mainline strategy planner";
        if (!engineSrc.includes("MainlineBlocksUpgrade()"))
            return "DecisionEngine must let mainline nodes override forced upgrade timing";
        if (!engineSrc.includes("value = _mainline.Adjust(value, state, action, mainlinePlan)"))
            return "DecisionEngine must apply mainline adjustment in action scoring";
        if (!engineSrc.includes("state.Gold + 1 >= tripleCost"))
            return "DecisionEngine must allow sell-to-triple when current gold is one short";
    });

    test("[Meta] environment baseline overrides guide tier and core card ids", () => {
        const baselineSrc = readFile("src/BobCoach/Core/SeasonMetaBaseline.cs");
        const catalogSrc = readFile("src/BobCoach/Core/CompGuideCatalog.cs");
        const plannerSrc = readFile("src/BobCoach/Core/MainlineStrategyPlanner.cs");
        if (!baselineSrc.includes("TryGetTier")
            || !baselineSrc.includes("TryGetCoreCardIds"))
            return "SeasonMetaBaseline must expose tier and core-id lookups";
        if (!catalogSrc.includes("season14_meta_baseline.json")
            || !catalogSrc.includes("ApplyMetaBaseline"))
            return "CompGuideCatalog must load and apply the season meta baseline";
        if (!plannerSrc.includes("MetaSource")
            || !plannerSrc.includes("Tier = \"\""))
            return "MainlineStrategyPlanner must carry meta tier and source into replay/audit data";
    });

    test("[Dynamic] mainline planner exposes stage/mode/pivot/counter fields and adjusts", () => {
        const plannerSrc = readFile("src/BobCoach/Core/MainlineStrategyPlanner.cs");
        const pluginSrc = readFile("src/BobCoach/BobCoachPlugin.cs");
        for (const token of [
            "ComputeStage(",
            "ComputePathScoresAndPivot(",
            "ObservePivotCandidates(",
            "DecideDynamicMode(",
            "BuildCounterPlan(",
            "hasCurrentDirection",
            "HasGuideEvidence",
            "GetAlternativeCoreIds",
            "IsGenericPathEvidenceCard",
            "distinctEvidence.Count >= 2",
            "plan.ShouldBuyCoreFirst",
            "plan.ShouldHoldForNextTurn",
            "plan.PivotScore >= 0.10 && RecentPivotObservation()",
            "state.Turn < 7",
            "ShouldConsiderPivot",
            "ShouldHedge",
            "AlternativeCoreCardIds",
            "PivotObservationCount",
            "NeedTradeUp",
        ]) {
            if (!plannerSrc.includes(token))
                return `MainlineStrategyPlanner missing dynamic planning token ${token}`;
        }
        const commitMode = plannerSrc.indexOf("if (plan.ShouldBuyCoreFirst)");
        const economyMode = plannerSrc.indexOf("else if (plan.ShouldHoldForNextTurn)");
        const pivotMode = plannerSrc.indexOf("else if (plan.ShouldConsiderPivot");
        if (commitMode < 0 || economyMode < 0 || pivotMode < 0
            || commitMode > economyMode || economyMode > pivotMode)
            return "dynamic planner must prioritize Commit/Economy over Pivot/Hedge";
        const priorityCall = plannerSrc.indexOf("DecidePriority(state, plan);");
        const pathCall = plannerSrc.indexOf("ComputePathScoresAndPivot(state, catalog");
        if (priorityCall < 0 || pathCall < 0 || priorityCall > pathCall)
            return "dynamic planner must decide resource priority before path scoring";
        if (!plannerSrc.includes("plan.Mode == \"Pivot\" || plan.Mode == \"Hedge\""))
            return "dynamic planner must boost alternative core purchases during hedge/pivot";
        if (!plannerSrc.includes("plan.NeedTradeUp"))
            return "dynamic planner must feed late-game trade-up into buy scoring";
        if (!pluginSrc.includes("currentPathScore")
            || !pluginSrc.includes("counterPlan")
            || !pluginSrc.includes("pivotScore"))
            return "mainlinePlan replay payload must include dynamic planning fields";
    });

    test("[Mainline][LateCommit] visible buy scoring penalizes off-path cards after the core is established", () => {
        const plannerSrc = readFile("src/BobCoach/Core/MainlineStrategyPlanner.cs");
        if (!plannerSrc.includes("latePathCommitment"))
            return "mainline planner must detect a late-game committed path";
        if (!plannerSrc.includes("0.62")
            || !plannerSrc.includes("0.70"))
            return "late committed path must penalize off-tribe and generic neutral buys";
    });

    test("[Spell][WinnersBread] early winner's bread is prioritized when a board target exists", () => {
        const engineSrc = readFile("src/BobCoach/Core/DecisionEngine.cs");
        if (!engineSrc.includes("BG36_883"))
            return "winner's bread must have an explicit card-id rule";
        if (!engineSrc.includes("赢家的面包"))
            return "winner's bread must have a name fallback";
    });

    test("[Discover][Authoritative] power-log discover batches render from the event batch", () => {
        const pluginSrc = readFile("src/BobCoach/BobCoachPlugin.cs");
        if (!pluginSrc.includes("var authoritativeDiscover = _discoverPanelActive"))
            return "dispatch render must read the latest power-log discover batch";
        if (!pluginSrc.includes("ShowAuthoritativeDiscoverHints"))
            return "authoritative discover batches must bypass delayed state extraction";
    });

    test("[StrategicMemory] decision engine owns memory and exposes replay snapshot", () => {
        const engineSrc = readFile("src/BobCoach/Core/DecisionEngine.cs");
        if (!engineSrc.includes("_strategyMemory = new StrategicMemory()"))
            return "DecisionEngine must own StrategicMemory";
        if (!engineSrc.includes("StrategicMemorySnapshot =>"))
            return "DecisionEngine must expose defensive memory snapshot";
        if (!engineSrc.includes("_strategySequencePlanner.Build"))
            return "DecisionEngine must invoke sequence planner";
        const pluginSrc = readFile("src/BobCoach/BobCoachPlugin.cs");
        if (!engineSrc.includes("ResetStrategicMemory()"))
            return "DecisionEngine must expose cross-game strategic reset";
        if (!pluginSrc.includes("_engine?.ResetStrategicMemory()"))
            return "new game lifecycle must clear strategic memory";
        if (!engineSrc.includes("Shadow memory must never break the visible legacy path"))
            return "strategic memory failures must stay isolated from legacy policy";
    });

    test("[StrategicPlan] sequence planner expands simulated action chains", () => {
        const plannerSrc = readFile("src/BobCoach/Core/Decision/StrategySequencePlanner.cs");
        if (!plannerSrc.includes("SequenceSearchPlanner.Search"))
            return "game adapter must use pure sequence search";
        if (!plannerSrc.includes("_simulator.Simulate"))
            return "game adapter must simulate candidate steps";
        if (!plannerSrc.includes("ExpandSequence"))
            return "game adapter must expand from the accumulated simulated state";
        if (!plannerSrc.includes("TurnProjector.Advance"))
            return "game adapter must score future gold";
        if (!plannerSrc.includes("OpponentTechAdvisor.AnalyzeOpponentBoard"))
            return "game adapter must account for opponent tech pressure";
        if (!plannerSrc.includes("MemoryEvidenceScore"))
            return "game adapter must consume cross-turn memory in scoring";
    });

    test("[StrategicPlan][Shadow] planner must not mutate visible legacy decision", () => {
        const engineSrc = readFile("src/BobCoach/Core/DecisionEngine.cs");
        const visibleReturn = engineSrc.indexOf("return bestAction;");
        const rankedMethod = engineSrc.indexOf(
            "public List<BobCoach.Engine.Decision.RankedAction> GetRankedCandidates");
        if (visibleReturn < 0 || rankedMethod < 0 || visibleReturn >= rankedMethod)
            return "sequence planner must remain isolated to Shadow ranking";
    });

    test("[StrategicPlan][Search] pure search rejects invalid finite-score inputs", () => {
        const searchSrc = readFile("src/BobCoach/Core/Decision/SequenceSearchPlanner.cs");
        if (!searchSrc.includes("step.Action != null"))
            return "search must reject roots without actions";
        if (!searchSrc.includes("double.IsNaN"))
            return "search must normalize non-finite scores";
    });

    test("[Adversarial][Sell] current core and high-tier resource cards are protected", () => {
        const engineSrc = readFile("src/BobCoach/Core/DecisionEngine.cs");
        if (!engineSrc.includes("_lastMainlinePlan.CoreCardIds.Contains(m.CardId)"))
            return "sell protection must include current mainline core cards";
        if (!engineSrc.includes("IsLikelyResourceText"))
            return "sell protection must include high-tier resource text fallback";
        if (!engineSrc.includes("始终为金色"))
            return "always-golden minions must receive an explicit early-game value bonus";
    });

    test("[Buy][Tempo] wherever-scaling low-tier minions get mid-game transition bonus", () => {
        const engineSrc = readFile("src/BobCoach/Core/DecisionEngine.cs");
        if (!engineSrc.includes("WhereverScalingTempoBonus"))
            return "wherever-scaling tempo bonus method must exist";
        if (!engineSrc.includes("无论本随从在哪"))
            return "wherever-scaling tempo bonus must use stable card text";
        if (!engineSrc.includes("card.CardName.Contains(\"星元自动机\")"))
            return "Star Automaton must be explicitly covered as transition tempo";
    });

    test("[Replay][Action] power-log buy/sell events request immediate UI refresh", () => {
        const pluginSrc = readFile("src/BobCoach/BobCoachPlugin.cs");
        const sellBlock = pluginSrc.indexOf("oldZone == \"PLAY\" && newZone == \"SETASIDE\"");
        const sellSlice = pluginSrc.slice(
            sellBlock,
            pluginSrc.indexOf("private static bool IsTavernSpellCard", sellBlock)
        );
        if (!sellSlice.includes("OnPowerLogStateChanged()"))
            return "sell zone event must request UI refresh immediately";
        const actionBlock = pluginSrc.indexOf("private void RecordPowerActionBlock");
        const actionSlice = pluginSrc.slice(
            actionBlock,
            pluginSrc.indexOf("private bool IsLocalPowerEvent", actionBlock)
        );
        if (!actionSlice.includes("OnPowerLogStateChanged()"))
            return "buy/sell/refresh/level power actions must request UI refresh";
    });

    test("[Adversarial][Discover] race matching must use structured tribes, not only names", () => {
        const extractorSrc = readFile("src/BobCoach/GameStateExtractor.cs");
        const scoreIdx = pluginSrc.indexOf("private List<Engine.TrinketHint> ScoreDiscoverOptions");
        const scoreBlock = pluginSrc.slice(scoreIdx, scoreIdx + 4500);
        if (!extractorSrc.includes("Tribes = GetTribes(e)"))
            return "GameStateExtractor must populate discover option tribes";
        if (!scoreBlock.includes("optionTribes.Any"))
            return "ScoreDiscoverOptions must use structured option tribes";
        if (!scoreBlock.includes("ResolveCardTribes"))
            return "ScoreDiscoverOptions must fall back to a card database tribe resolver";
    });

    test("[Adversarial][Gold] full board must not create a play-hand recommendation without room", () => {
        const visualizerSrc = readFile("src/BobCoach/Core/DecisionVisualizer.cs");
        const pairIdx = visualizerSrc.indexOf("else if (isPair && !hm.IsSpell");
        const pairBlock = visualizerSrc.slice(pairIdx, pairIdx + 90);
        if (!pairBlock.includes("!boardFull"))
            return "pair hand marker must not recommend a minion onto a full board";
        if (!visualizerSrc.includes("state.BoardMinions.Count < state.MaxBoardSlots"))
            return "fallback play-hand recommendation must require a free board slot";
    });

    test("[Adversarial][HeroPower] resource skills may be usable on full board", () => {
        const heroSrc = readFile("src/BobCoach/Core/HeroPowerEngine.cs");
        const resourceIdx = heroSrc.indexOf("strategy.UsePurpose == HeroUsePurpose.Resource");
        const resourceBlock = heroSrc.slice(resourceIdx, resourceIdx + 180);
        if (resourceBlock.includes("boardFull ?"))
            return "HeroPowerEngine must not suppress Resource skills on a full board";
    });

    test("[Adversarial][MCTS] second-step search must not hard-skip full board or zero gold", () => {
        const mctsSrc = readFile("src/BobCoach/Core/MCTSSearch.cs");
        if (mctsSrc.includes("state1.Gold < 1 || state1.BoardMinions.Count >= 7"))
            return "MCTS must let the action enumerator decide after first-step actions";
    });

    test("[Adversarial][DarkGift] pressable tip can be refreshed while still pressable", () => {
        const gateSrc = readFile("src/BobCoach/Core/DarkGiftTipGate.cs");
        if (!gateSrc.includes("public void RefreshPressable"))
            return "DarkGiftTipRenderCoordinator must support refreshing a still-pressable button";
        if (!pluginSrc.includes("_darkGiftTipCoordinator.RefreshPressable(DateTime.Now)"))
            return "plugin timer must refresh dark gift pressable state";
    });

    test("[Adversarial][DarkGift] candidate hints are scored from board comp and stay fail-closed", () => {
        const advisorSrc = readFile("src/BobCoach/Core/DarkGiftCandidateAdvisor.cs");
        if (!advisorSrc.includes("public static List<TrinketHint> Evaluate"))
            return "DarkGiftCandidateAdvisor must expose a scored hint API";
        if (!advisorSrc.includes("IsUnrated"))
            return "DarkGiftCandidateAdvisor must mark unknown candidates unrated";
        if (!advisorSrc.includes("匹配流派"))
            return "DarkGiftCandidateAdvisor must reward comp-matched candidates";
        if (!pluginSrc.includes("private List<Engine.TrinketHint> ScoreDarkGiftCandidates"))
            return "plugin must own a dark gift candidate scoring path";
    });

    test("[Adversarial][DarkGift] tip must use live gold and block combat phase", () => {
        const idx = pluginSrc.indexOf("private Engine.DarkGiftTipContext BuildDarkGiftTipContext");
        const block = pluginSrc.slice(idx, idx + 2200);
        if (!block.includes("int liveGold = GetLiveGold(state)"))
            return "dark gift tip context must use tracked live gold";
        if (!block.includes("MAIN_COMBAT"))
            return "dark gift tip context must treat MAIN_COMBAT as combat";
        if (!block.includes("Phase = inCombat ? \"combat\" : (state.Phase ?? \"\")"))
            return "dark gift tip context must suppress non-shop phases";
    });

    test("[Adversarial][DarkGift] top pick candidate font is highlighted", () => {
        const overlayIdx = overlaySrc.indexOf("internal void ShowDarkGiftCandidates(");
        const block = overlaySrc.slice(overlayIdx, overlayIdx + 4000);
        if (!block.includes("isTopPick"))
            return "ShowDarkGiftCandidates must know the top pick";
        if (!block.includes("FontWeights.Bold"))
            return "ShowDarkGiftCandidates must bold the recommended candidate";
    });

    test("[Adversarial][Sell] sell markers require an actual affordable after-sell target", () => {
        const visualizerSrc = readFile("src/BobCoach/Core/DecisionVisualizer.cs");
        const engineSrc = readFile("src/BobCoach/Core/DecisionEngine.cs");
        const gameStateSrc = readFile("src/BobCoach/Core/GameState.cs");
        if (!visualizerSrc.includes("IsSellWorthwhileForVisual")
            || !visualizerSrc.includes("IsVisualProtectedSellMinion"))
            return "DecisionVisualizer must gate sells by protected cards and actual improvement";
        if (!engineSrc.includes("IsSellWorthwhile")
            || !engineSrc.includes("RecentlyBoughtEntityIds"))
            return "DecisionEngine must gate sells by recent buys and actual improvement";
        if (!gameStateSrc.includes("RecentlyBoughtEntityIds"))
            return "GameState must track recently bought entity ids";
        if (!pluginSrc.includes("handWaitingOnFullBoard")
            || !pluginSrc.includes("hasUpgradeAfterSell"))
            return "DispatchRender must preserve sell only for hand-swap or affordable upgrade";
        if (!pluginSrc.includes("!handWaitingOnFullBoard && !hasUpgradeAfterSell"))
            return "DispatchRender must clear sell markers when no hand/upgrade/shop target is executable";
    });

    test("[Adversarial][AFK] early skip heroes disable normal recruit and actions", () => {
        const extractorSrc = readFile("src/BobCoach/GameStateExtractor.cs");
        const enumeratorSrc = readFile("src/BobCoach/Core/ActionEnumerator.cs");
        const engineSrc = readFile("src/BobCoach/Core/DecisionEngine.cs");
        const gateSrc = readFile("src/BobCoach/Core/EarlyTurnRecruitGate.cs");
        if (!extractorSrc.includes("afkSkipsEarlyTurns"))
            return "GameStateExtractor must model AFK early-skip turns";
        if (!extractorSrc.includes("BlocksEarlyRecruit")
            || !extractorSrc.includes("_earlyTurnRecruitGate.Observe")
            || !extractorSrc.includes("_earlyTurnRecruitGate.Reset"))
            return "GameStateExtractor must persist and reset confirmed AFK signals";
        if (!gateSrc.includes("TB_BaconShop_HP_044")
            || !gateSrc.includes("TB_BaconShop_HERO_KelThuzad"))
            return "EarlyTurnRecruitGate must recognize current and legacy AFK identities";
        if (!extractorSrc.includes("!afkSkipsEarlyTurns"))
            return "GameStateExtractor must close normal recruit during AFK early turns";
        if (!enumeratorSrc.includes("if (!state.NormalRecruitAvailable) return actions;"))
            return "ActionEnumerator must emit no actions outside normal recruit";
        if (!engineSrc.includes("!state.NormalRecruitAvailable"))
            return "DecisionEngine shop scoring must fail closed outside normal recruit";
        if (!engineSrc.includes("!state.NormalRecruitAvailable")
            || !engineSrc.includes("当前不可招募"))
            return "DecisionEngine level-up suggestion must fail closed outside normal recruit";
    });

    test("[Adversarial][HeroSkin] strategy and target rules use current hero power", () => {
        const engineSrc = readFile("src/BobCoach/Core/HeroPowerEngine.cs");
        const stateSrc = readFile("src/BobCoach/Core/HeroPowerState.cs");
        const extractorSrc = readFile("src/BobCoach/GameStateExtractor.cs");
        const enumeratorSrc = readFile("src/BobCoach/Core/ActionEnumerator.cs");
        if (!engineSrc.includes("GetStrategyForPower"))
            return "HeroPowerEngine must resolve strategy from the current hero power";
        if (!stateSrc.includes("BaseHeroCardId"))
            return "HeroPowerState must carry the resolved base hero identity";
        if (!extractorSrc.includes("BaseHeroCardId"))
            return "GameStateExtractor must populate base hero identity from hero power";
        if (!enumeratorSrc.includes("power.BaseHeroCardId"))
            return "ActionEnumerator must use base hero identity for skin-independent target rules";
    });

    test("[Adversarial][Discover] spell sources fall back to HearthDb discover text", () => {
        const parserSrc = readFile("src/BobCoach/Core/PowerLogParser.cs");
        const extractorSrc = readFile("src/BobCoach/GameStateExtractor.cs");
        if (!parserSrc.includes("GetLocText(HearthDb.Enums.Locale.zhCN)")
            || !parserSrc.includes("text.Contains(\"发现\")"))
            return "PowerLogParser must authorize discover sources from local card text";
        if (extractorSrc.includes("resolvedName == \"新生幼苗\""))
            return "GameStateExtractor must not filter the Newborn Sapling tavern spell as a token";
    });

    test("[Adversarial][Discover] scoring weighs core and early-mid resource cards", () => {
        const pluginSrcNow = readFile("src/BobCoach/BobCoachPlugin.cs");
        const engineSrc = readFile("src/BobCoach/Core/DecisionEngine.cs");
        const advisorSrc = readFile("src/BobCoach/Core/DarkGiftCandidateAdvisor.cs");
        if (!engineSrc.includes("public bool IsResourceCard")
            || !engineSrc.includes("public bool IsCoreComboCard"))
            return "DecisionEngine must expose resource and core-card classification";
        if (!pluginSrcNow.includes("IsResourceCard(d.CardId)")
            || !pluginSrcNow.includes("IsCoreComboCard(d.CardId)"))
            return "ScoreDiscoverOptions must consume resource and core-card classification";
        if (!pluginSrcNow.includes("state.Turn <= 5 ? 5.5"))
            return "ScoreDiscoverOptions must give early resource cards the highest bonus";
        if (!pluginSrcNow.includes("_engine.IsResourceCard")
            || !pluginSrcNow.includes("_engine.IsCoreComboCard"))
            return "Dark gift scoring must receive resource and core-card classification";
        if (!advisorSrc.includes("isResourceCard != null")
            || !advisorSrc.includes("isCoreCard != null"))
            return "DarkGiftCandidateAdvisor must consume resource and core-card classification";
    });

    test("[Adversarial][ShopSlots] special tavern slot rules are modeled", () => {
        const gameStateSrc = readFile("src/BobCoach/Core/GameState.cs");
        const extractorSrc = readFile("src/BobCoach/GameStateExtractor.cs");
        const pluginSrcNow = readFile("src/BobCoach/BobCoachPlugin.cs");
        if (!gameStateSrc.includes("HasAlwaysSevenTavernCards"))
            return "GameState must model always-seven tavern card effects";
        if (!gameStateSrc.includes("BG30_MagicItem_423"))
            return "GameState must model Innkeeper's Stein extra refresh minion";
        if (!gameStateSrc.includes("HERO_27"))
            return "GameState must model Sindragosa one-fewer tavern slot";
        if (!extractorSrc.includes("BG30_MagicItem_991"))
            return "GameStateExtractor must include Felbat Portrait in always-seven replenishment";
        if (!pluginSrcNow.includes("state.ShopSlotCount"))
            return "DispatchRender must use state-aware shop slot count";
        if (!pluginSrcNow.includes("GetShopSlotCountForTier(layoutTier)"))
            return "DispatchRender must use the last real refresh tier for shop slot count";
    });

    test("[Adversarial][UpgradeUI] upgrade hint is primary unless buy then upgrade", () => {
        const pluginSrcNow = readFile("src/BobCoach/BobCoachPlugin.cs");
        if (!pluginSrcNow.includes("hasBuyThenUpgrade")
            || !pluginSrcNow.includes("upgradePrimary"))
            return "DispatchRender must prioritize upgrade and only keep buy-then-upgrade cards";
        if (!pluginSrcNow.includes("liveGold - buyCost >= plan.UpgradeHint.Cost"))
            return "buy-then-upgrade must require enough dynamic gold after buying";
    });

    test("[Adversarial][CardValue] spellcraft attached resources are valued", () => {
        const engineSrc = readFile("src/BobCoach/Core/DecisionEngine.cs");
        if (!engineSrc.includes("AttachedResourceBonus"))
            return "DecisionEngine must add attached resource value";
        if (!engineSrc.includes("BACON_SPELLCRAFT_ID")
            || !engineSrc.includes("塑造法术"))
            return "AttachedResourceBonus must recognize Spellcraft minions";
        if (!engineSrc.includes("immediateCoin")
            || !engineSrc.includes("return 1.5;"))
            return "immediate coin minions must receive a stronger resource bonus than spellcraft";
    });

    test("[Adversarial][SpellHint] buy spell status must include the spell name", () => {
        const visualizerSrc = readFile("src/BobCoach/Core/DecisionVisualizer.cs");
        if (visualizerSrc.includes('parts.Add("法术"); break;'))
            return "BuySpell must not render a bare spell label";
        const buySpellIdx = visualizerSrc.indexOf("case ActionType.BuySpell:");
        const buySpellBlock = visualizerSrc.slice(buySpellIdx, buySpellIdx + 800);
        if (!buySpellBlock.includes('parts.Add("买→" + spellName)'))
            return "BuySpell status must include the targeted spell name";
        if (!buySpellBlock.includes("spell.CardName"))
            return "BuySpell status must fall back to the observed spell card name";
        if (!visualizerSrc.includes("case ActionType.BuySpell:"))
            return "second-step status must also support BuySpell";
    });

    test("[Adversarial][TavernCoin] one leftover gold must still recommend BG28_810", () => {
        const engineSrc = readFile("src/BobCoach/Core/DecisionEngine.cs");
        if (!engineSrc.includes("cid.Contains(\"BG28_810\")"))
            return "DecisionEngine must recognize the Tavern Coin spell";
        // 2026-08-19 B11 修复: 回合1(3金)买酒馆币=花1得1净零, 却抢走买3费随从节奏
        // (实测: 艾萨拉回合1"囤币被荐使用"根因)。新条件: 买后仍买得起3费随从
        // (Gold-cost>=3) 或 仅剩1金不买浪费 (Gold==cost)。Gold==cost 分支保留。
        if (!engineSrc.includes("state.Gold == spellCost || state.Gold - spellCost >= 3"))
            return "Tavern Coin must be buyable when gold exactly equals its cost";
        if (!engineSrc.includes('sc.CardName.Contains("币")'))
            return "economy spell fallback must recognize Tavern Coin by name";
    });

    test("[Adversarial][Busker] T1 must hold next-turn gold battlecry minions", () => {
        const visualizerSrc = readFile("src/BobCoach/Core/DecisionVisualizer.cs");
        const enumeratorSrc = readFile("src/BobCoach/Core/ActionEnumerator.cs");
        if (!visualizerSrc.includes("IsNextTurnGoldBattlecryCard"))
            return "DecisionVisualizer must recognize next-turn gold battlecry minions";
        if (!visualizerSrc.includes("state.Turn == 1 && IsNextTurnGoldBattlecryCard(hm.CardId)"))
            return "T1 must suppress the play marker for Southsea Busker";
        if (!visualizerSrc.includes("BG26_135")
            || !visualizerSrc.includes("BG26_135_G"))
            return "Southsea Busker and its golden copy must be covered";
        if (!enumeratorSrc.includes("ShouldHoldNextTurnGoldBattlecry"))
            return "ActionEnumerator must also hold turn-one busker in hand";
    });

    test("[Adversarial][TrinketPanel] panel must not expose internal scoring attributes", () => {
        const overlaySrc = readFile("src/BobCoach/OverlayRenderer.cs");
        const start = overlaySrc.indexOf("public void ShowTrinketHints");
        const end = overlaySrc.indexOf("public void ShowTrinketLoading", start);
        const block = overlaySrc.slice(start, end >= 0 ? end : start + 4000);
        if (!block.includes('if (h.IsTopPick) line += " ★首选";'))
            return "trinket panel must keep a top-pick marker";
        if (block.includes('line += "  [" + h.Reason + "]"'))
            return "trinket panel must not display internal scoring attributes";
    });

    test("[Adversarial][TrinketPanel] discover panel avoids trinket overlap", () => {
        const overlaySrc = readFile("src/BobCoach/OverlayRenderer.cs");
        if (!overlaySrc.includes("bool trinketPanelPresent"))
            return "discover render must detect the trinket panel";
        if (!overlaySrc.includes("x += panelTextWidth * sc + _calc.ScaleX(16)"))
            return "discover panel must shift away when the trinket panel is present";
    });

    test("[Adversarial][TrinketPanel] Power.log candidates fall back when local facts fail", () => {
        const pluginSrcNow = readFile("src/BobCoach/BobCoachPlugin.cs");
        const start = pluginSrcNow.indexOf("private Engine.UiTargetSource ApplyPowerLogTrinketCandidates");
        const end = pluginSrcNow.indexOf("private Engine.UiTargetSource ApplyScheduledTrinketPlaceholder", start);
        const block = pluginSrcNow.slice(start, end >= 0 ? end : start + 5000);
        if (!block.includes("Engine.TrinketFact fact = default(Engine.TrinketFact)"))
            return "Power.log trinket candidate handling must tolerate missing local facts";
        if (!block.includes("Engine.DisplayNameResolver.Resolve"))
            return "Power.log trinket candidates must fall back to resolved display names";
        if (!block.includes("localOffers.Add(new Engine.TrinketOption"))
            return "Power.log trinket candidates must still inject offers after fact failure";
    });

    test("[Adversarial][ShopSlots] purchase exits awaiting-complete even during partial snapshot", () => {
        const pluginSrcNow = readFile("src/BobCoach/BobCoachPlugin.cs");
        if (pluginSrcNow.includes("else if (!_shopLayoutSlotPolicy.AwaitingCompleteShop)"))
            return "purchase handling must not skip ObservePurchase while waiting for a full shop";
        if (!pluginSrcNow.includes("_shopLayoutSlotPolicy.ObservePurchase();"))
            return "purchase detection must confirm through the shared slot policy";
    });

    test("[Adversarial][MultiStep] sell generation includes cheap spell targets", () => {
        const actionSrc = readFile("src/BobCoach/Core/ActionEnumerator.cs");
        const blockStart = actionSrc.indexOf("int minBuyCost = GameRuleEvaluator.GetPurchaseCost");
        const block = actionSrc.slice(blockStart, blockStart + 500);
        if (!block.includes("if (cost >= 0 && cost < minBuyCost)"))
            return "sell enumeration must consider cheaper tavern spell costs";
        if (!block.includes("minBuyCost = cost"))
            return "sell enumeration must lower the after-sell funding target for spells";
    });

    test("[Adversarial][MultiStep] low-gold two-step search can include sell follow-ups", () => {
        const engineSrc = readFile("src/BobCoach/Core/DecisionEngine.cs");
        if (!engineSrc.includes("bool canExploreTwoStep = !ShouldPreserveHeroPlaybookAction")
            || !engineSrc.includes("state.Gold >= 1"))
            return "two-step search must activate with low gold";
        if (!engineSrc.includes("actions.Any(a => a.Type == ActionType.SellMinion"))
            return "two-step search must recognize sell-first chains";
    });

    test("[Adversarial][MultiStep] River Skipper sell-to-economy chain is modeled", () => {
        const engineSrc = readFile("src/BobCoach/Core/DecisionEngine.cs");
        const visualizerSrc = readFile("src/BobCoach/Core/DecisionVisualizer.cs");
        const simulatorSrc = readFile("src/BobCoach/Core/Simulator.cs");
        const samplerSrc = readFile("src/BobCoach/Core/CardPoolSampler.cs");
        if (!engineSrc.includes("IsSelfReplacingEconomySell"))
            return "DecisionEngine must recognize River Skipper economy sell";
        if (!visualizerSrc.includes("IsVisualSelfReplacingEconomySell"))
            return "DecisionVisualizer must mirror River Skipper economy sell";
        if (!simulatorSrc.includes("sold.CardId == \"BG33_140\""))
            return "Simulator must handle River Skipper sell replacement";
        if (!samplerSrc.includes("public static MinionData SampleTierOneReplacement"))
            return "CardPoolSampler must expose tier-one replacement sampling";
    });

    test("[Adversarial][UpgradeRisk] lethal level-up risk blocks upgrade", () => {
        const engineSrc = readFile("src/BobCoach/Core/DecisionEngine.cs");
        if (!engineSrc.includes("bool upgradeLethal = OpponentModel.IsLevelUpLethal"))
            return "DecisionEngine must compute lethal level-up risk";
        if (!engineSrc.includes("&& !upgradeLethal"))
            return "lethal level-up risk must block upgrade actions";
        if (!engineSrc.includes("当前血量过低，升本被斩杀风险过大"))
            return "level-up suggestion must report lethal risk";
    });

    test("[Adversarial][ResourceValue] next-turn gold battlecries are valued", () => {
        const engineSrc = readFile("src/BobCoach/Core/DecisionEngine.cs");
        if (!engineSrc.includes("nextTurnGold"))
            return "DecisionEngine must recognize next-turn gold resource cards";
        if (!engineSrc.includes("text.Contains(\"下回合获得\")"))
            return "next-turn gold bonus must use Chinese text rules";
        if (!engineSrc.includes("text.Contains(\"next turn\")"))
            return "next-turn gold bonus must use English text rules";
    });

    test("[Adversarial][UnifiedRanking] all actions share a ranked candidate surface", () => {
        const engineSrc = readFile("src/BobCoach/Core/DecisionEngine.cs");
        if (!engineSrc.includes("GetRankedCandidates"))
            return "DecisionEngine must expose a unified ranked candidate surface";
        if (!engineSrc.includes("ActionDescriptor.CreateKey(action)"))
            return "ranked candidates must carry stable action keys";
        if (!engineSrc.includes("OpponentModel.IsLevelUpLethal"))
            return "ranked candidates must apply lethal upgrade safety";
    });

    test("[Adversarial][UnifiedRanking] ranked fallback fills legacy no-action holes", () => {
        const pluginSrcNow = readFile("src/BobCoach/BobCoachPlugin.cs");
        if (!pluginSrcNow.includes("TryResolveRankedFallback"))
            return "plugin must resolve legacy no-action through ranked candidates";
        if (!pluginSrcNow.includes("ranked-fallback"))
            return "ranked fallback must carry a stable policy id";
    });

    test("[Adversarial][CrossTurn] TurnProjector models next-turn gold and state reset", () => {
        const projectorSrc = readFile("src/BobCoach/Core/TurnProjector.cs");
        if (!projectorSrc.includes("public static GameState Advance"))
            return "TurnProjector must expose next-turn state projection";
        if (!projectorSrc.includes("int baseMaxGold = Math.Min(10, 2 + next.Turn)"))
            return "TurnProjector must reset next-turn max gold";
        if (!projectorSrc.includes("carriedGoldCapBonus"))
            return "TurnProjector must preserve permanent max-gold cap bonuses";
        if (!projectorSrc.includes("IsNextTurnGoldCard"))
            return "TurnProjector must model next-turn gold cards";
    });

    test("[Adversarial][CrossTurn] DecisionEngine exposes cross-turn planning", () => {
        const engineSrc = readFile("src/BobCoach/Core/DecisionEngine.cs");
        if (!engineSrc.includes("GetCrossTurnPlan"))
            return "DecisionEngine must expose cross-turn planning";
        if (!engineSrc.includes("TurnProjector.Advance(afterFirst)"))
            return "cross-turn planning must project the next turn after the first action";
        if (!engineSrc.includes("0.1 * nextTurnFeatures[FeatureExtractor.F_GOLD]"))
            return "ranked candidates must value next-turn gold in unified scoring";
    });

    test("[Adversarial][HeroPower] utility power entities never enter action enumeration", () => {
        const extractorSrc = readFile("src/BobCoach/GameStateExtractor.cs");
        const enumeratorSrc = readFile("src/BobCoach/Core/ActionEnumerator.cs");
        const catalogSrc = readFile("src/BobCoach/Core/HeroPowerRuleCatalog.cs");
        if (!catalogSrc.includes("TB_BaconShop_UpdateDmgCap"))
            return "hero power catalog must recognize UpdateDmgCap as utility";
        if (!extractorSrc.includes("IsUtilityPowerCardId(e.CardId)"))
            return "extractor must exclude utility hero power entities";
        if (!enumeratorSrc.includes("HeroPowerRuleCatalog.IsUtilityPowerCardId(power.CardId)"))
            return "action enumerator must exclude utility hero powers";
    });

    test("[Adversarial][HeroPower] non-targeted hero powers render without board targets", () => {
        const enumeratorSrc = readFile("src/BobCoach/Core/ActionEnumerator.cs");
        const visualizerSrc = readFile("src/BobCoach/Core/DecisionVisualizer.cs");
        if (!enumeratorSrc.includes("RequiresTarget"))
            return "hero power actions must carry target requirements";
        if (!visualizerSrc.includes("bestAction.RequiresTarget"))
            return "visualizer must not attach board targets to non-targeted hero powers";
    });

    test("[Adversarial][Upgrade] LEVEL_UP suppresses conflicting hero skill guidance", () => {
        const pluginSrcNow = readFile("src/BobCoach/BobCoachPlugin.cs");
        if (!pluginSrcNow.includes("heroUseSuggestion = \"\""))
            return "plugin must clear hero skill suggestion when level-up is primary";
        if (!pluginSrcNow.includes("plan.Status.HintLine = \"\""))
            return "plugin must clear stale status hint when level-up is primary";
    });

    test("[Adversarial][UI] discover and dark gift panels hide internal scoring reasons", () => {
        const overlaySrcNow = readFile("src/BobCoach/OverlayRenderer.cs");
        const discoverStart = overlaySrcNow.indexOf("private void RenderDiscoverHints");
        const discoverBlock = overlaySrcNow.slice(discoverStart, discoverStart + 1200);
        const darkStart = overlaySrcNow.indexOf("internal void ShowDarkGiftCandidates");
        const darkBlock = overlaySrcNow.slice(darkStart, darkStart + 1200);
        if (discoverBlock.includes('"  [" + h.Reason + "]"'))
            return "discover panel must not render internal reason text";
        if (darkBlock.includes("hint.Reason"))
            return "dark gift panel must not render internal reason text";
    });

    test("[Adversarial][Tech] train crew keep and buy value uses opponent board context", () => {
        const modelSrc = readFile("src/BobCoach/Core/OpponentModel.cs");
        const engineSrcNow = readFile("src/BobCoach/Core/DecisionEngine.cs");
        const visualizerSrcNow = readFile("src/BobCoach/Core/DecisionVisualizer.cs");
        if (!modelSrc.includes("IsSituationalTechCard")
            || !modelSrc.includes("IsTechCardWorthKeeping"))
            return "OpponentModel must expose situational tech evaluation";
        if (
            !engineSrcNow.includes("GetSituationalTechMultiplier(shopCard.CardId")
            || !engineSrcNow.includes("OpponentTechAdvisor.IsTechCardWorthKeeping(")
            || !engineSrcNow.includes("state.Opponents")
        ) {
            return "DecisionEngine must use opponent context for tech buy and sell";
        }
        if (
            !visualizerSrcNow.includes("OpponentTechAdvisor.IsTechCardWorthKeeping(")
            || !visualizerSrcNow.includes("state.Opponents")
        ) {
            return "DecisionVisualizer must use opponent context for tech sell and swap";
        }
    });

    test("[Eudora][HandCombat] visual fallback must not suggest playing hand-combat scouts", () => {
        const visualizerSrc = readFile("src/BobCoach/Core/DecisionVisualizer.cs");
        if (!visualizerSrc.includes("if (Engine.HandCombatCardPolicy.ShouldKeepInHand(hm.CardId)) continue;"))
            return "hand play fallback must skip hand-combat cards such as BG32_330";
        if (!visualizerSrc.includes("state.Turn == 1 && IsNextTurnGoldBattlecryCard(hm.CardId)) continue;"))
            return "hand play fallback must skip next-turn gold cards";
    });

    test("[Eudora][Freeze] early freeze keeps one gold for the dig power", () => {
        const engineSrc = readFile("src/BobCoach/Core/DecisionEngine.cs");
        const visualizerSrc = readFile("src/BobCoach/Core/DecisionVisualizer.cs");
        if (!engineSrc.includes("HasEudoraWorthFreezing"))
            return "DecisionEngine must evaluate Eudora early freeze targets";
        if (!engineSrc.includes("IsEudoraHero(")
            || !engineSrc.includes("state.HeroCardId"))
            return "DecisionEngine must recognize Eudora hero ids including skins";
        if (!visualizerSrc.includes("eudoraEarly"))
            return "DecisionVisualizer must expose the Eudora early freeze hint path";
        if (!visualizerSrc.includes("\u7559\u0031\u91d1\u6316\u5b9d"))
            return "Eudora freeze hint must explain reserving one gold for digging";
    });

    test("[DarkGift][Gold] stale choice and re-armed pressable windows stay hidden at low gold", () => {
        const gateSrc = readFile("src/BobCoach/Core/DarkGiftTipGate.cs");
        if (!gateSrc.includes("context.Gold >= 0 && context.Gold < 3"))
            return "dark gift coordinator must hide stale active choice text when gold is below cost";
        if (!gateSrc.includes("_gate.ShouldShow(now)"))
            return "dark gift gate must not endlessly re-arm the pressable window";
    });

    test("[Gold][Shop] live-gold pruning removes unaffordable purchase markers", () => {
        const pluginSrc = readFile("src/BobCoach/BobCoachPlugin.cs");
        if (!pluginSrc.includes("PruneShopMarkersByLiveGold"))
            return "plugin must prune purchase markers with live gold before render";
        if (!pluginSrc.includes("plan.ShopMarkers.RemoveAll(marker =>"))
            return "pruning must use explicit per-marker affordability";
    });

    test("[T35][Cenarius] simulator uses bounded permanent-gold-cap helper", () => {
        const simulator = readFile("src/BobCoach/Core/Simulator.cs");
        if (!simulator.includes("HeroPowerRuleCatalog.AdvancePermanentGoldCap"))
            return "Cenarius simulation still hard-codes its max-gold cap";
    });

    test("[Sell][Safety] sell-to-refresh is removed and upgrade sell requires survival margin", () => {
        const engineSrc = readFile("src/BobCoach/Core/DecisionEngine.cs");
        if (engineSrc.includes("gold + 1 >= refreshCost"))
            return "sell should not be recommended merely to fund a refresh";
        if (!engineSrc.includes("state.Health > 15"))
            return "upgrade sell must require a survival margin";
        if (!engineSrc.includes("!OpponentModel.IsLevelUpLethal(state.Health, state.Turn)"))
            return "upgrade sell must reject lethal level-up risk";
    });

    test("[T35][Sell] final sell must be revalidated after ranked selection", () => {
        const pluginSrc = readFile("src/BobCoach/BobCoachPlugin.cs");
        if (!pluginSrc.includes("IsSellActionValid"))
            return "final ranked sell action is not revalidated";
    });

    test("[T35][Sell] resource/core tier alone cannot justify selling a board minion", () => {
        const engineSrc = readFile("src/BobCoach/Core/DecisionEngine.cs");
        const block = engineSrc.slice(
            engineSrc.indexOf("private bool IsShopMinionBetter"),
            engineSrc.indexOf("private int MaxShopTier")
        );
        if (block.includes("|| shop.Tier >= sold.Tier"))
            return "resource/core branch still allows tier-only replacement";
    });

    test("[T35][Sell] plugin after-sell paths must not allow tier-only resource/core replacement", () => {
        const pluginSrc = readFile("src/BobCoach/BobCoachPlugin.cs");
        const scoreIdx = pluginSrc.indexOf("private bool IsShopScoreWorthAfterSell");
        const scoreBlock = pluginSrc.slice(scoreIdx, scoreIdx + 2200);
        const markerIdx = pluginSrc.indexOf("private bool IsShopMarkerWorthAfterSell");
        const markerBlock = pluginSrc.slice(markerIdx, markerIdx + 2400);
        if (scoreBlock.includes("shopCard.Tier >= sold.Tier"))
            return "score after-sell path still allows tier-only replacement";
        if (markerBlock.includes("shop.Tier >= sold.Tier"))
            return "marker after-sell path still allows tier-only replacement";
        if (!scoreBlock.includes("shopPower >= soldPower * 1.05")
            || !markerBlock.includes("shopPower >= soldPower * 1.05"))
            return "plugin after-sell paths must require a clear board-power improvement";
    });

    test("[ReplayRegression] global replay states are replayable decision inputs", () => {
        const recorderSrc = readFile("src/BobCoach/Core/GlobalReplayRecorder.cs");
        const pluginSrc = readFile("src/BobCoach/BobCoachPlugin.cs");
        const engineSrc = readFile("src/BobCoach/Core/DecisionEngine.cs");
        const runnerSrc = readFile("tools/evaluate/ReplayDecisionRunner.cs");
        if (!recorderSrc.includes("public GameState ToGameState()"))
            return "GlobalReplayState must be convertible back to GameState";
        if (!recorderSrc.includes("public static GlobalReplayState FromJson"))
            return "GlobalReplayState must support JSON round-trip parsing";
        if (!recorderSrc.includes("public List<GlobalHeroPowerRef> HeroPowers")
            || !recorderSrc.includes("public List<GlobalTrinketRef> TrinketDetails"))
            return "replay state must capture hero powers and trinket details";
        if (!pluginSrc.includes("HeroPowers = state.HeroPowers")
            || !pluginSrc.includes("ActiveTrinkets = state.ActiveTrinkets"))
            return "plugin must fill replayable decision inputs";
        if (!engineSrc.includes("public void HydrateReplayRules"))
            return "DecisionEngine must restore anomaly and trinket rules for replay";
        if (!runnerSrc.includes("engine.GetRankedCandidates"))
            return "offline replay runner must re-decide with the current engine";
    });

    test("[T36][Nuoboton] hero power requires a last used tavern spell", () => {
        const enumeratorSrc = readFile("src/BobCoach/Core/ActionEnumerator.cs");
        const pluginSrc = readFile("src/BobCoach/BobCoachPlugin.cs");
        const recorderSrc = readFile("src/BobCoach/Core/GlobalReplayRecorder.cs");
        if (!enumeratorSrc.includes("return !string.IsNullOrEmpty(state.LastUsedTavernSpellCardId)"))
            return "Nuoboton hero power must be unavailable before any tavern spell is used";
        if (!pluginSrc.includes("_lastUsedTavernSpellCardId")
            || !pluginSrc.includes("LastUsedTavernSpellCardId = _lastUsedTavernSpellCardId"))
            return "plugin must track and inject the last used tavern spell";
        if (!recorderSrc.includes("LastUsedTavernSpellCardId")
            || !recorderSrc.includes("LastUsedTavernSpellName"))
            return "replay state must record the last used tavern spell";
    });

    test("[T36][Sell] already affordable shop targets must not trigger sell-to-buy", () => {
        const engineSrc = readFile("src/BobCoach/Core/DecisionEngine.cs");
        const visualizerSrc = readFile("src/BobCoach/Core/DecisionVisualizer.cs");
        const engineBlock = engineSrc.slice(
            engineSrc.indexOf("private bool HasBetterShopAfterSell"),
            engineSrc.indexOf("private bool IsShopMinionBetter")
        );
        const visualBlock = visualizerSrc.slice(
            visualizerSrc.indexOf("private bool HasVisualShopTargetAfterSell"),
            visualizerSrc.indexOf("private bool IsVisualShopTargetBetter")
        );
        if (!engineBlock.includes("if (gold >= cost) continue;")
            || !visualBlock.includes("if (gold >= cost) continue;"))
            return "sell-to-buy must skip shop cards already affordable without selling";
    });

    test("[T36][Refresh] refresh must leave a meaningful budget or free refresh", () => {
        const pluginSrc = readFile("src/BobCoach/BobCoachPlugin.cs");
        if (!pluginSrc.includes("CanRefreshLeaveMeaningfulBudget"))
            return "plugin must gate refresh by post-refresh budget";
        if (!pluginSrc.includes("CanRefreshLeaveMeaningfulBudget(state, liveGold)"))
            return "visible refresh glow must use the post-refresh budget gate";
    });

    test("[T36][SpellUI] non-targeted tavern spell text says direct use", () => {
        const visualizerSrc = readFile("src/BobCoach/Core/DecisionVisualizer.cs");
        if (!visualizerSrc.includes("→直接使用"))
            return "non-targeted spell recommendations must say direct use";
    });

    test("[Chromie][HeroPower] free hero suggestion survives affordable upgrade", () => {
        const pluginSrc = readFile("src/BobCoach/BobCoachPlugin.cs");
        if (!pluginSrc.includes("goldAfterUpgrade < heroPowerCost"))
            return "plugin must only clear hero suggestion when upgrade leaves too little gold";
    });

    test("[Chromie][Spell] spell shop gets score boost and more visible markers", () => {
        const engineSrc = readFile("src/BobCoach/Core/DecisionEngine.cs");
        const visualizerSrc = readFile("src/BobCoach/Core/DecisionVisualizer.cs");
        if (!engineSrc.includes("chromieSpellHero"))
            return "DecisionEngine must recognize Chromie spell-centric scoring";
        if (!visualizerSrc.includes("spellShopCount >= 3"))
            return "DecisionVisualizer must show more spell markers after Chromie refresh";
    });

    test("[FreeRefresh] stored free refreshes survive turn projection and drive freeze", () => {
        const projectorSrc = readFile("src/BobCoach/Core/TurnProjector.cs");
        const engineSrc = readFile("src/BobCoach/Core/DecisionEngine.cs");
        if (!projectorSrc.includes("next.FreeRefreshCount = state.FreeRefreshCount;"))
            return "TurnProjector must preserve stored free refresh count";
        if (!engineSrc.includes("preserveFreeRefresh"))
            return "DecisionEngine must preserve free refresh when shop is worth freezing";
    });

    test("[Replay] global action inference uses entity identity and records hero powers", () => {
        const pluginSrc = readFile("src/BobCoach/BobCoachPlugin.cs");
        if (!pluginSrc.includes("prevShopEntities.Contains(hm.EntityId)"))
            return "global buy inference must use shop entity identity";
        if (!pluginSrc.includes("actions.Add(\"heroPower:\" + (state.HeroPowerCardId ?? \"\"))"))
            return "global replay must record active hero power usage";
    });
}

// ============================================================================
// Summary
// ============================================================================

console.log(`\n=== UI 生命周期测试结果 ===`);
console.log(`通过: ${passed}  失败: ${failed}  警告: ${warnings}`);
diag.forEach(l => console.log(l));

if (failed > 0) {
    console.log(`\n${failed} 项测试失败 — 修复后再提交`);
    process.exit(1);
} else {
    console.log(`\n全部 ${passed} 项通过 (${warnings} 警告)`);
    process.exit(0);
}
