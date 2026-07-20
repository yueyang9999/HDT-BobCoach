"use strict";

const fs = require("fs");
const path = require("path");
const { spawnSync } = require("child_process");
const os = require("os");
const { findRoslynCompiler, skipOrFail } = require("./dotnet_test_env");

const root = path.resolve(__dirname, "..");
const compiler = findRoslynCompiler();
if (!compiler) skipOrFail("log.config consent: Roslyn compiler unavailable; set BOBCOACH_CSC");

const dataPathsSource = path.join(root, "src", "BobCoach", "Core", "BobCoachDataPaths.cs");
const source = path.join(root, "src", "BobCoach", "Core", "LogConfigEnsurer.cs");
const parserSource = path.join(root, "src", "BobCoach", "Core", "PowerLogParser.cs");
const scannerSource = path.join(root, "src", "BobCoach", "Core", "PowerLogInitialBuildScanner.cs");
const watcherSource = path.join(root, "src", "BobCoach", "Core", "PowerLogWatcher.cs");
const pluginSource = path.join(root, "src", "BobCoach", "BobCoachPlugin.cs");
const harness = path.join(__dirname, "LogConfigConsentHarness.cs");
const tempRoot = fs.mkdtempSync(path.join(os.tmpdir(), "bobcoach-log-config-test-"));
const output = path.join(tempRoot, "LogConfigConsentHarness.exe");
const failureLogContract = "Power.log configuration apply failed; Power.log feature remains disabled";
const pluginText = fs.readFileSync(pluginSource, "utf8");
if (!pluginText.includes(failureLogContract)) {
    console.error(`FAIL missing stable failure log contract: ${failureLogContract}`);
    process.exit(1);
}
const gameStartIndex = pluginText.indexOf("private void OnGameStart()");
const gameEndIndex = pluginText.indexOf("private void OnGameEnd()", gameStartIndex);
const initPoolIndex = pluginText.indexOf("private void InitPoolTracker()", gameStartIndex);
const gameStartBlock = pluginText.slice(gameStartIndex, initPoolIndex);
const gameEndBlock = pluginText.slice(gameEndIndex, pluginText.indexOf("\n        private ", gameEndIndex + 1));
const unloadIndex = pluginText.indexOf("public void OnUnload()");
const unloadBlock = pluginText.slice(unloadIndex, pluginText.indexOf("\n        public ", unloadIndex + 1));
if (!pluginText.includes("SubscribePowerLogWatcherEvents()")
    || !pluginText.includes("UnsubscribePowerLogWatcherEvents()")
    || !gameStartBlock.includes("SubscribePowerLogWatcherEvents();")) {
    console.error("FAIL Power.log event subscriptions are not guarded by lifecycle helpers");
    process.exit(1);
}
if (!gameEndBlock.includes("UnsubscribePowerLogWatcherEvents();")
    || !gameEndBlock.includes("_powerLogWatcher.StopWatching();")
    || !unloadBlock.includes("UnsubscribePowerLogWatcherEvents();")) {
    console.error("FAIL Power.log event handlers are not cleaned up on every end/unload path");
    process.exit(1);
}
try {
    const build = spawnSync(compiler, [
        "/nologo", "/langversion:7.3", "/target:exe", `/out:${output}`,
        dataPathsSource, source, parserSource, scannerSource, watcherSource, harness,
    ], { encoding: "utf8" });
    if (build.status !== 0) {
        console.error(build.stdout || build.stderr);
        process.exitCode = 1;
    } else {
        const run = spawnSync(output, [], { encoding: "utf8" });
        if (run.status !== 0) {
            console.error(run.stdout || run.stderr || `exit=${run.status}`);
            process.exitCode = run.status || 1;
        } else {
            console.log(run.stdout.trim());
        }
    }
} finally {
    fs.rmSync(tempRoot, { recursive: true, force: true });
}
