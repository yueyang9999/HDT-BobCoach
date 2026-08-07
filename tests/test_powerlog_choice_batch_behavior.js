"use strict";

const fs = require("fs");
const os = require("os");
const path = require("path");
const { spawnSync } = require("child_process");
const { findRoslynCompiler, skipOrFail } = require("./dotnet_test_env");

const root = path.resolve(__dirname, "..");
const dataPathsSource = path.join(root, "src", "BobCoach", "Core", "BobCoachDataPaths.cs");
const logConfigSource = path.join(root, "src", "BobCoach", "Core", "LogConfigEnsurer.cs");
const parserSource = path.join(root, "src", "BobCoach", "Core", "PowerLogParser.cs");
const buildScannerSource = path.join(root, "src", "BobCoach", "Core", "PowerLogInitialBuildScanner.cs");
const watcherSource = path.join(root, "src", "BobCoach", "Core", "PowerLogWatcher.cs");
const lifecycleSource = path.join(root, "src", "BobCoach", "Core", "TrinketChoiceBatchLifecycle.cs");
const shadowSource = path.join(root, "src", "BobCoach", "Core", "TrinketShadowCaptureSession.cs");
const combatChoicePolicySource = path.join(root, "src", "BobCoach", "Core", "CombatChoiceRenderPolicy.cs");
const timewarpAdvisorSource = path.join(root, "src", "BobCoach", "Core", "TimewarpPurchaseAdvisor.cs");
const season14JsonParserSource = path.join(root, "src", "BobCoach", "Core", "Season14RegistryJsonParser.cs");
const season14CardRegistrySource = path.join(root, "src", "BobCoach", "Core", "Season14CardRegistry.cs");
const seasonDataSelectorSource = path.join(root, "src", "BobCoach", "Core", "SeasonDataSelector.cs");
const seasonMechanicProfileSource = path.join(root, "src", "BobCoach", "Core", "SeasonMechanicProfile.cs");
const activateRegistrySource = path.join(root, "src", "BobCoach", "Core", "ActivateRegistry.cs");
const activateFactSource = path.join(root, "src", "BobCoach", "Core", "ActivateFactSource.cs");
const lockboxRegistrySource = path.join(root, "src", "BobCoach", "Core", "LockboxRegistry.cs");
const lockboxFactSource = path.join(root, "src", "BobCoach", "Core", "LockboxFactSource.cs");
const fishbaitRegistrySource = path.join(root, "src", "BobCoach", "Core", "FishbaitRegistry.cs");
const fishbaitFactSource = path.join(root, "src", "BobCoach", "Core", "FishbaitFactSource.cs");
const darkGiftRegistrySource = path.join(root, "src", "BobCoach", "Core", "DarkGiftRegistry.cs");
const darkGiftFactSource = path.join(root, "src", "BobCoach", "Core", "DarkGiftFactSource.cs");
const darkGiftTipGateSource = path.join(root, "src", "BobCoach", "Core", "DarkGiftTipGate.cs");
const displayNameResolverSource = path.join(root, "src", "BobCoach", "Core", "DisplayNameResolver.cs");
const season14RulesEvaluatorSource = path.join(root, "src", "BobCoach", "Core", "Season14RulesEvaluator.cs");
const harnessSource = path.join(__dirname, "PowerLogChoiceBatchHarness.cs");
const outputDir = fs.mkdtempSync(path.join(os.tmpdir(), "bobcoach-powerlog-choice-"));
const outputExe = path.join(outputDir, "PowerLogChoiceBatchHarness.exe");
const compiler = findRoslynCompiler();

if (!compiler) skipOrFail("PowerLog choice batch behavior: Roslyn compiler unavailable; set BOBCOACH_CSC");

let exitCode = 1;
try {
    const compile = spawnSync(compiler, [
        "/nologo",
        "/langversion:7.3",
        `/out:${outputExe}`,
        dataPathsSource,
        logConfigSource,
        parserSource,
        buildScannerSource,
        watcherSource,
        lifecycleSource,
        shadowSource,
        combatChoicePolicySource,
        timewarpAdvisorSource,
        season14JsonParserSource,
        season14CardRegistrySource,
        seasonDataSelectorSource,
        seasonMechanicProfileSource,
        activateRegistrySource,
        activateFactSource,
        lockboxRegistrySource,
        lockboxFactSource,
        fishbaitRegistrySource,
        fishbaitFactSource,
        darkGiftRegistrySource,
        darkGiftFactSource,
        darkGiftTipGateSource,
        displayNameResolverSource,
        season14RulesEvaluatorSource,
        harnessSource,
    ], { encoding: "utf8" });

    if (compile.status !== 0) {
        process.stderr.write(compile.stdout || "");
        process.stderr.write(compile.stderr || "");
        exitCode = compile.status || 1;
    } else {
        const testAppData = path.join(outputDir, "powerlog-choice-test-appdata");
        fs.mkdirSync(testAppData, { recursive: true });
        const run = spawnSync(outputExe, process.argv.slice(2), {
            encoding: "utf8",
            env: { ...process.env, APPDATA: testAppData },
        });
        process.stdout.write(run.stdout || "");
        process.stderr.write(run.stderr || "");
        exitCode = run.status || 0;
    }
} finally {
    fs.rmSync(outputDir, { recursive: true, force: true });
}

process.exit(exitCode);
