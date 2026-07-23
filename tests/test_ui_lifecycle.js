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
    return fs.readFileSync(path.join(ROOT, rel), "utf-8");
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

    // ClearTaggedElements 在 Show* 方法体内（可能在入口后几行）
    test("[ClearSpec] ShowStatusStrip 调用了 ClearTaggedElements", () => {
        const idx = overlaySrc.indexOf("public void ShowStatusStrip");
        const block = overlaySrc.slice(idx, idx + 800);
        if (!block.includes("ClearTaggedElements")) return "未找到 ClearTaggedElements 调用";
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
        if (!configSrc.includes("Version { get; set; } = 10"))
            return "LayoutConfig version must bump to v10 so the legacy default shop offset can be migrated";
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
        if (!block.includes("ScaleX(8)") || !block.includes("ScaleY(105)"))
            return "status strip position should use ScaleX/ScaleY";
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
        const timerBlock = pluginSrc.slice(timerStart, timerStart + 2400);
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
        if (!plugin.includes("private const bool TrinketRecommendationsVisible = false;"))
            return "缺少单一、可测试且默认关闭的饰品推荐显示开关";
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
        if (!extractorSrc.includes("trackedGold < 0 && state.Gold <= 0"))
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
        const block = pluginSrc.slice(idx, idx + 4000);
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
        if (cfgDriven < 3)
            return `三个选择面板须从 Config.PanelOffsetX 读位置(F10可校准) (found ${cfgDriven}/3)`;
    });

    test("[07071644][Calib] 推荐面板位置/缩放可 F10 校准[9]", () => {
        const cfg = readFile("src/BobCoach/Core/LayoutConfig.cs");
        if (!cfg.includes("PanelOffsetX") || !cfg.includes("PanelOffsetY") || !cfg.includes("PanelScale"))
            return "LayoutConfig 须有 PanelOffsetX/Y/Scale 字段";
        const overlaySrc = readFile("src/BobCoach/OverlayRenderer.cs");
        if ((overlaySrc.match(/new ScaleTransform\(sc, sc\)/g) || []).length < 3)
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
        const sblock = si >= 0 ? src.slice(si, si + 2800) : "";
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
        if (!/_discoverPanelState[\s\S]{0,160}enforceMaxActive: false/.test(plugin))
            return "发现面板应 enforceMaxActive:false(靠SendChoices选完关闭, 防慢选时提前超时消失)";
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

    test("[07072158][#1] 商店按实际卡数居中(确认B:买卡向心靠拢); 设备差异可由ShopOffsetX校准", () => {
        const p = readFile("src/BobCoach/BobCoachPlugin.cs");
        // 用户确认B: 游戏买卡后向中心轴靠拢=始终按实际卡数居中。默认基准是客户区中轴, 设备差异才由ShopOffsetX校准。
        if (!/slotCountForLayout = Math\.Max\(1, Math\.Min\(7, state\.ShopMinions\.Count\)\)/.test(p))
            return "商店须按实际在场卡数居中(实测B: 买卡后向心靠拢)";
        const o = readFile("src/BobCoach/OverlayRenderer.cs");
        if (!/layoutCount = Math\.Max\(1, Math\.Min\(7, liveShopMinions\.Count\)\)/.test(o))
            return "SyncShopTagPositions 须按实际在场卡数居中(与ShopRender一致)";
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
        if (!pluginSrc.includes("denseReplenishingShop") || !pluginSrc.includes("state.ShopMinions.Count"))
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
            pluginSrc.indexOf("string planHash =") + 700
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
            pluginSrc.indexOf("string planHash =") + 1000
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
        if (renderIdx < 0 || !renderBlock.includes("SelectBestAffordableIndex"))
            return "timewarp purchase does not select the best affordable candidate";
        if (!renderBlock.includes("ShowTimewarpPurchaseRating")
            || renderBlock.includes("ShowDiscoverHints"))
            return "timewarp purchase still uses the discover panel";
        const overlayIdx = rendererSrc.indexOf("internal void ShowTimewarpPurchaseRating");
        const overlayBlock = rendererSrc.slice(overlayIdx, overlayIdx + 5000);
        if (overlayIdx < 0 || !overlayBlock.includes("GetShopCardRect"))
            return "timewarp purchase marker does not reuse purchase-card layout";
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
        if (!overlaySrc.includes("public void SyncShopTagPositions"))
            return "缺少 SyncShopTagPositions 方法";
    });

    test("[ShopTag] 标签元素绑定 entityId (ShopTagElement)", () => {
        if (!overlaySrc.includes("_shopTagElements")) return "缺少 _shopTagElements 注册列表";
        if (!overlaySrc.includes("class ShopTagElement")) return "缺少 ShopTagElement 结构";
    });

    test("[ShopTag] SyncShopTagPositions 离店淡出 (不再zonePos平移/不重建)", () => {
        const idx = overlaySrc.indexOf("public void SyncShopTagPositions");
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
        if (!pluginSrc.includes("SyncShopTagPositions(_cachedState.ShopMinions, layoutTier, _cachedState.ReplenishingShopActive)"))
            return "OnUpdate 未调用 SyncShopTagPositions";
    });

    test("[ShopTag] SyncShopTagPositions 不在渲染层读取 Core.Game.Entities 猜补牌机制", () => {
        const idx = overlaySrc.indexOf("public void SyncShopTagPositions");
        const block = overlaySrc.slice(idx, idx + 1800);
        if (!block.includes("bool denseReplenishingShop"))
            return "SyncShopTagPositions should accept replenishing state from extractor/plugin";
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
        if (!block.includes("_renderer.Clear()") || !block.includes("_lastShopUiClearAt = now"))
            return "shop clear helper must still clear and record cooldown time";
    });

    test("[ShopTag] layout uses shop refresh tier, not post-upgrade tier", () => {
        if (!pluginSrc.includes("_lastShopRefreshTier > 0 ? _lastShopRefreshTier"))
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

    test("[ShopTag][07072158] 商店标签按实际在场卡数居中(确认B); 设备差异由ShopOffsetX校准", () => {
        // 反复历程(以实测为准): 07072052全dense→截图疑似右偏 → 07072158回退raw → 用户确认B(买卡向心靠拢=居中)→恢复dense。
        // 结论: 居中逻辑使用实际卡数; 默认基准为客户区中轴, ShopOffsetX仅保留给设备差异校准。
        const renderIdx = pluginSrcDense.indexOf("int slotCountForLayout");
        const renderBlock = pluginSrcDense.slice(Math.max(0, renderIdx - 600), renderIdx + 900);
        if (!renderBlock.includes("Math.Max(1, Math.Min(7, state.ShopMinions.Count))"))
            return "shop render 须按实际在场卡数居中(state.ShopMinions.Count)";
        const syncIdx = overlaySrcDense.indexOf("public void SyncShopTagPositions");
        const syncBlock = overlaySrcDense.slice(syncIdx, syncIdx + 1400);
        if (!syncBlock.includes("Math.Max(1, Math.Min(7, liveShopMinions.Count))"))
            return "shop tag follow 须与初始渲染一致, 按实际在场卡数居中";
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
