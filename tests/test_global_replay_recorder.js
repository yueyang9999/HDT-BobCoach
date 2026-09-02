"use strict";

const fs = require("fs");
const os = require("os");
const path = require("path");
const { spawnSync } = require("child_process");
const {
    findRoslynCompiler,
    findFrameworkAssembly,
    skipOrFail,
} = require("./dotnet_test_env");

const root = path.resolve(__dirname, "..");
const recorderSource = path.join(root, "src", "BobCoach", "Core", "GlobalReplayRecorder.cs");
const dataPathsSource = path.join(root, "src", "BobCoach", "Core", "BobCoachDataPaths.cs");
const harnessSource = path.join(__dirname, "GlobalReplayRecorderBehavior.cs");
const outputDir = fs.mkdtempSync(path.join(os.tmpdir(), "bobcoach-global-replay-"));
const outputExe = path.join(outputDir, "GlobalReplayRecorderBehavior.exe");
const compiler = findRoslynCompiler();
const netstandard = findFrameworkAssembly("netstandard.dll");

if (!compiler || !netstandard) {
    skipOrFail("Global replay recorder behavior: Roslyn compiler/netstandard unavailable; set BOBCOACH_CSC");
}

let exitCode = 1;
try {
    const compile = spawnSync(compiler, [
        "/nologo",
        "/langversion:7.3",
        "/define:BOBCOACH_REPLAY_CORE_ONLY",
        "/target:exe",
        `/out:${outputExe}`,
        `/r:${netstandard}`,
        dataPathsSource,
        recorderSource,
        harnessSource,
    ], { encoding: "utf8" });

    if (compile.status !== 0) {
        process.stderr.write(compile.stdout || "");
        process.stderr.write(compile.stderr || "");
        exitCode = compile.status || 1;
    } else {
        const run = spawnSync(outputExe, { encoding: "utf8" });
        process.stdout.write(run.stdout || "");
        process.stderr.write(run.stderr || "");
        exitCode = run.status || 0;
    }
} finally {
    fs.rmSync(outputDir, { recursive: true, force: true });
}

process.exit(exitCode);
