"use strict";

const fs = require("fs");
const path = require("path");

const root = path.resolve(__dirname, "..");
const plugin = fs.readFileSync(path.join(root, "src", "BobCoach", "BobCoachPlugin.cs"), "utf8");
const project = fs.readFileSync(path.join(root, "src", "BobCoach", "BobCoach.csproj"), "utf8");
const updater = fs.readFileSync(path.join(root, "src", "BobCoach", "Core", "TrinketStatsUpdater.cs"), "utf8");
const fetcher = fs.readFileSync(path.join(root, "src", "BobCoach", "Core", "TrinketStatsFetcher.cs"), "utf8");
const extractor = fs.readFileSync(path.join(root, "src", "BobCoach", "GameStateExtractor.cs"), "utf8");
const policyFactory = fs.readFileSync(path.join(root, "src", "BobCoach", "Core", "Decision", "DecisionPolicyFactory.cs"), "utf8");
const unifiedPolicy = fs.readFileSync(path.join(root, "src", "BobCoach", "Core", "Decision", "UnifiedRankingPolicy.cs"), "utf8");

const forbiddenPluginPaths = [
    "new NodeEngineBridge(",
    "_trinketStatsUpdater",
    "new Engine.TrinketStatsUpdater(",
    "SetCurrentBuild(build)",
];
for (const token of forbiddenPluginPaths) {
    if (plugin.includes(token)) {
        console.error(`FAIL production plugin still reaches offline-forbidden path: ${token}`);
        process.exit(1);
    }
}

const forbiddenBuildSources = ["Core\\NodeEngineBridge.cs"];
const requiredValidationSources = [
    "Core\\TrinketStatsModels.cs",
    "Core\\TrinketStatsVerifier.cs",
    "Core\\TrinketStatsFetcher.cs",
    "Core\\TrinketStatsStore.cs",
    "Core\\TrinketStatsUpdater.cs",
];
for (const source of forbiddenBuildSources) {
    if (project.includes(`<Compile Include="${source}"`)) {
        console.error(`FAIL production DLL still compiles offline-forbidden source: ${source}`);
        process.exit(1);
    }
}

for (const source of requiredValidationSources) {
    if (!project.includes(`<Compile Include="${source}"`)) {
        console.error(`FAIL production DLL omits required external validation source: ${source}`);
        process.exit(1);
    }
}
if (!plugin.includes("private const bool TrinketRecommendationsVisible = false;")
    || !plugin.includes("bool trinketShouldDisplay = TrinketRecommendationsVisible && trinketShouldShow;")
    || !plugin.includes("if (trinketShouldDisplay)")) {
    console.error("FAIL trinket recommendations must use one testable default-off display gate");
    process.exit(1);
}
if (plugin.includes("_trinketStatsUpdater.Active") || plugin.includes("TrinketStatRecord")) {
    console.error("FAIL external validation data must remain isolated from production scoring and UI");
    process.exit(1);
}

for (const token of [
    "static.zerotoheroes.com",
    "overview-from-hourly",
    "FirestoneUrl",
    "ParseFirestone",
    "RequestCheck(",
    "ScheduleRetry(",
    "new Timer(",
    "Task.Run(",
    "new TrinketStatsStore(",
    "new TrinketStatsFetcher(",
]) {
    if (updater.includes(token)) {
        console.error(`FAIL disabled statistics coordinator still contains runtime I/O path: ${token}`);
        process.exit(1);
    }
}
if (!updater.includes("TrinketStatsStatus.SourceUnavailable")
    || !updater.includes('"no-authorized-external-source"')) {
    console.error("FAIL disabled statistics coordinator must expose the no-authorized-source state");
    process.exit(1);
}
if (fetcher.includes("static.zerotoheroes.com")
    || fetcher.includes("trinket-stats-readonly")) {
    console.error("FAIL generic fetcher still embeds the retired Firestone host or identity");
    process.exit(1);
}

console.log("PASS installable DLL preserves isolated validation with no external-stat runtime path");

const extractActiveIndex = extractor.indexOf("ExtractActiveTrinkets(entities, state);");
const trackGoldIndex = extractor.indexOf("int trackedGold = TrackGold(state, entities);");
if (extractActiveIndex < 0 || trackGoldIndex < 0 || extractActiveIndex > trackGoldIndex
    || !extractor.includes("state.ActiveTrinketContext = TrinketEffectResolver.Resolve(")
    || !extractor.includes("state.EffectiveRules = state.ActiveTrinketContext.ApplyTo(")) {
    console.error("FAIL equipped-trinket hard rules must be resolved and merged before gold tracking");
    process.exit(1);
}
if (!extractor.includes("e.IsBattlegroundsTrinket && e.IsInPlay && !string.IsNullOrEmpty(e.CardId)")
    || !extractor.includes("UnknownTrinketDiagnosticTracker")
    || extractor.includes("StatPairPattern")
    || extractor.includes("private static bool GrantsStats(")) {
    console.error("FAIL active trinkets and stat spells must use exact fail-closed local registries with diagnostics");
    process.exit(1);
}

console.log("PASS equipped-trinket hard rules are active before resource accounting");

const watcher = fs.readFileSync(path.join(root, "src", "BobCoach", "Core", "PowerLogWatcher.cs"), "utf8");
const logConfigEnsurer = fs.readFileSync(path.join(root, "src", "BobCoach", "Core", "LogConfigEnsurer.cs"), "utf8");
const forbiddenAutomaticWriters = [
    "public static LogConfigStatus Ensure()",
    "private static LogConfigStatus CreateConfig()",
    "private static LogConfigStatus PatchConfig()",
];
for (const token of forbiddenAutomaticWriters) {
    if (logConfigEnsurer.includes(token)) {
        console.error(`FAIL log.config still exposes a write path without an inspected plan: ${token}`);
        process.exit(1);
    }
}

const startBegin = watcher.indexOf("public void StartWatching()");
const startEnd = watcher.indexOf("private void RetryStartWatching(", startBegin);
const startBody = watcher.slice(startBegin, startEnd);
if (!startBody.includes("StartWatchingWithConfigInspector(LogConfigEnsurer.Inspect)")
    || !startBody.includes("internal void StartWatchingWithConfigInspector(")
    || startBody.includes("LogConfigEnsurer.Ensure()")
    || startBody.includes("LogConfigEnsurer.Apply(")) {
    console.error("FAIL game start must inspect log.config without writing");
    process.exit(1);
}

const buttonBegin = plugin.indexOf("public void OnButtonPress()");
const buttonEnd = plugin.indexOf("public void OnUpdate()", buttonBegin);
const buttonBody = plugin.slice(buttonBegin, buttonEnd);
const yesIndex = buttonBody.indexOf("MessageBoxResult.Yes");
const applyIndex = buttonBody.indexOf("LogConfigEnsurer.Apply(plan)");
if (!buttonBody.includes("LogConfigEnsurer.Inspect()")
    || !buttonBody.includes("plan.ProposedContent")
    || yesIndex < 0 || applyIndex < 0 || yesIndex > applyIndex) {
    console.error("FAIL plugin button must show the plan and require Yes before applying log.config");
    process.exit(1);
}
if (!plugin.includes("private bool _decisionEventRecordingOptedIn;")
    || !buttonBody.includes("MessageBoxButton.YesNo")
    || !buttonBody.includes("_decisionEventRecordingOptedIn = true")
    || !buttonBody.includes("_decisionEventRecordingOptedIn = false")) {
    console.error("FAIL local decision-event recording must default off and require explicit Yes/No consent");
    process.exit(1);
}
const configOkIndex = buttonBody.indexOf("plan.Status == Engine.LogConfigStatus.OK");
const configOkReturnIndex = buttonBody.indexOf("return;", configOkIndex);
const recordingConsentIndex = buttonBody.indexOf("MessageBoxButton.YesNo", configOkIndex);
if (configOkIndex < 0 || recordingConsentIndex < 0
    || (configOkReturnIndex >= 0 && configOkReturnIndex < recordingConsentIndex)) {
    console.error("FAIL an already-valid log.config must not bypass recording consent");
    process.exit(1);
}
if (/Settings\s*\.\s*\w+\s*=\s*_decisionEventRecordingOptedIn/.test(plugin)
    || /Profile\w*\s*\.\s*\w+\s*=\s*_decisionEventRecordingOptedIn/.test(plugin)) {
    console.error("FAIL recording consent must remain in memory instead of adding a permanent setting/profile field");
    process.exit(1);
}

const loadBegin = plugin.indexOf("public void OnLoad()");
const loadEnd = plugin.indexOf("public void OnUnload()", loadBegin);
if (plugin.slice(loadBegin, loadEnd).includes("MessageBox.Show")) {
    console.error("FAIL log.config consent must not use a startup modal");
    process.exit(1);
}

console.log("PASS log.config writes require explicit plugin-button consent");

const shadowModeToken = "DecisionRuntimeMode.Shadow";
if (!plugin.includes(shadowModeToken)
    || plugin.includes("DecisionRuntimeMode.LegacyOnly")
    || plugin.includes("DecisionRuntimeMode.Unified")) {
    console.error("FAIL ranking orchestration mode must be hard-coded Shadow with no alternate activation path");
    process.exit(1);
}
if (!plugin.includes("BobCoach.DecisionModels.linear-v1.envelope.json")
    || !plugin.includes("GetManifestResourceStream")
    || !plugin.includes("DecisionPolicyFactory.Create")) {
    console.error("FAIL plugin must compose policies from the optional checksum-validated model resource");
    process.exit(1);
}
const pluginLoadStart = plugin.indexOf("public void OnLoad()");
const pluginLoadEnd = plugin.indexOf("public void OnUnload()", pluginLoadStart);
const pluginLoadBody = plugin.slice(pluginLoadStart, pluginLoadEnd);
if (!pluginLoadBody.includes("new RankingModelRuntimeContext(")
    || !pluginLoadBody.includes("DecisionPolicyFactory.Create")) {
    console.error("FAIL production policy composition must provide an explicit runtime approval context");
    process.exit(1);
}
if (!policyFactory.includes("RankingModelRuntimeContext runtimeContext")
    || !policyFactory.includes("RankingModelLoader.LoadForRuntime(modelArtifact, runtimeContext)")
    || policyFactory.includes("RankingModelLoader.Load(modelArtifact)")) {
    console.error("FAIL production policy factory must use the strict runtime model loader");
    process.exit(1);
}
const publicProductionEntries = unifiedPolicy.match(/public\s+static\s+UnifiedRankingPolicy\s+CreateProduction\s*\(/g) || [];
const internalProductionEntries = unifiedPolicy.match(/internal\s+static\s+UnifiedRankingPolicy\s+CreateProduction\s*\(/g) || [];
if (publicProductionEntries.length !== 0 || internalProductionEntries.length !== 4) {
    console.error("FAIL factory must be the only public production-composition entry point");
    process.exit(1);
}
if (!project.includes('Core\\Decision\\ExplanationBuilder.cs')
    || !project.includes('Core\\Decision\\DecisionPolicyFactory.cs')
    || project.includes("DecisionModels\\linear-v1.envelope.json")) {
    console.error("FAIL build must include Task 11 composition code without embedding a production model");
    process.exit(1);
}
for (const token of [
    "_engine.GetLevelUpSuggestion(state)",
    "_engine.GetShopCardScores(state)",
    "_engine.GetCompGuidance(state)",
]) {
    if (!plugin.includes(token)) {
        console.error(`FAIL legacy non-ranking API was removed: ${token}`);
        process.exit(1);
    }
}
if (plugin.includes('_engine.GetBestAction(state), "GetBestAction"')) {
    console.error("FAIL main visible recommendation still bypasses the Shadow orchestrator");
    process.exit(1);
}

const evaluationStart = plugin.indexOf("private void EvaluateAndRender(");
const evaluationEnd = plugin.indexOf("private ", evaluationStart + 20);
const evaluationBody = plugin.slice(evaluationStart, evaluationEnd);
const observedIndex = evaluationBody.indexOf("InferObservedActionKeys(_prevState, state)");
const attachObservedIndex = evaluationBody.indexOf("_decisionLifecycle.AttachObservedActions");
const generateIndex = evaluationBody.indexOf("_decisionPolicies.Candidates.Generate(state)");
const beginIndex = evaluationBody.indexOf("_decisionLifecycle.BeginOrReuse(state, candidates)");
const decideWithIdIndex = evaluationBody.indexOf("_decisionOrchestrator.Decide(state, context.DecisionId)");
const recommendationIndex = evaluationBody.indexOf("_decisionEventRecorder.AttachRecommendation");
if ([observedIndex, attachObservedIndex, generateIndex, beginIndex, decideWithIdIndex, recommendationIndex]
        .some(index => index < 0)
    || !(observedIndex < attachObservedIndex
        && attachObservedIndex < generateIndex
        && generateIndex < beginIndex
        && beginIndex < decideWithIdIndex
        && decideWithIdIndex < recommendationIndex)) {
    console.error("FAIL active recording order must be observation -> creation -> Shadow comparison -> recommendation");
    process.exit(1);
}
if (!evaluationBody.includes("NoOpShadowComparisonSink.Instance")
    || !evaluationBody.includes("_decisionOrchestrator.Decide(state)")) {
    console.error("FAIL inactive recording must use the no-op sink and ID-free orchestrator call");
    process.exit(1);
}
if (!plugin.includes('"unknown"')
    || !plugin.includes("_currentGameRecord.Timestamp")
    || !plugin.includes("Guid.NewGuid()")) {
    console.error("FAIL unavailable identities and per-game local recording IDs must remain explicit");
    process.exit(1);
}

const gameStartStart = plugin.indexOf("private void OnGameStart()");
const gameStartEnd = plugin.indexOf("private void OnGameEnd()", gameStartStart);
const gameStartBody = plugin.slice(gameStartStart, gameStartEnd);
const startWatchingIndex = gameStartBody.indexOf("_powerLogWatcher.StartWatching()");
const createRecorderIndex = gameStartBody.indexOf("DecisionEventRecorder.CreateForLocalReplay(");
const recordingGateIndex = gameStartBody.indexOf("_powerLogWatcher.ConfigStatus == LogConfigStatus.OK");
if ([startWatchingIndex, createRecorderIndex, recordingGateIndex].some(index => index < 0)
    || !(startWatchingIndex < recordingGateIndex && recordingGateIndex < createRecorderIndex)
    || !gameStartBody.includes("_decisionEventRecordingOptedIn")
    || !gameStartBody.includes("(_powerLogWatcher.IsWatching || _powerLogWatcher.IsStarting)")
    || !/else\s*\{\s*ResetDecisionRecording\(\);\s*\}/.test(gameStartBody)) {
    console.error("FAIL decision recording must start only after the watcher is active with valid config");
    process.exit(1);
}
if (!gameStartBody.includes("_decisionPolicies.ModelId")
    || !gameStartBody.includes("_decisionPolicies.ModelPayloadSha256")) {
    console.error("FAIL decision-event recording must carry the exact composed model identity");
    process.exit(1);
}

const gameEndStart = plugin.indexOf("private void OnGameEnd()");
const gameEndEnd = plugin.indexOf("private void ResetDecisionRecording()", gameEndStart);
const gameEndBody = plugin.slice(gameEndStart, gameEndEnd);
const extractPlacementIndex = gameEndBody.indexOf("ExtractFinalPlacement()");
const attachOutcomeIndex = gameEndBody.indexOf("_decisionLifecycle.AttachCombatOutcome");
const resetRecordingIndex = gameEndBody.indexOf("ResetDecisionRecording()");
if ([extractPlacementIndex, attachOutcomeIndex, resetRecordingIndex].some(index => index < 0)
    || !(extractPlacementIndex < attachOutcomeIndex && attachOutcomeIndex < resetRecordingIndex)
    || !gameEndBody.includes('"placement:" + placement')) {
    console.error("FAIL game end must attach an observed placement before clearing decision recording");
    process.exit(1);
}

console.log("PASS ranking stays Shadow with ordered local lifecycle and legacy-visible fallback");
