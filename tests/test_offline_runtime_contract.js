"use strict";

const fs = require("fs");
const path = require("path");

const root = path.resolve(__dirname, "..");
const plugin = fs.readFileSync(path.join(root, "src", "BobCoach", "BobCoachPlugin.cs"), "utf8");
const project = fs.readFileSync(path.join(root, "src", "BobCoach", "BobCoach.csproj"), "utf8");
const updater = fs.readFileSync(path.join(root, "src", "BobCoach", "Core", "TrinketStatsUpdater.cs"), "utf8");
const fetcher = fs.readFileSync(path.join(root, "src", "BobCoach", "Core", "TrinketStatsFetcher.cs"), "utf8");
const extractor = fs.readFileSync(path.join(root, "src", "BobCoach", "GameStateExtractor.cs"), "utf8");

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

const loadBegin = plugin.indexOf("public void OnLoad()");
const loadEnd = plugin.indexOf("public void OnUnload()", loadBegin);
if (plugin.slice(loadBegin, loadEnd).includes("MessageBox.Show")) {
    console.error("FAIL log.config consent must not use a startup modal");
    process.exit(1);
}

console.log("PASS log.config writes require explicit plugin-button consent");
