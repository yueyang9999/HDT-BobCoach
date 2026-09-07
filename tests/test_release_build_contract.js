"use strict";

const assert = require("assert");
const crypto = require("crypto");
const fs = require("fs");
const path = require("path");

const root = path.resolve(__dirname, "..");
const projectPath = path.join(root, "src", "BobCoach", "BobCoach.csproj");
const builderPath = path.join(root, "tools", "build", "build_release.ps1");
const project = fs.readFileSync(projectPath, "utf8");
const builder = fs.readFileSync(builderPath, "utf8");

function requireMatch(source, pattern, label) {
    assert.match(source, pattern, `missing ${label}`);
}

function requireAbsent(source, pattern, label) {
    assert.doesNotMatch(source, pattern, `forbidden ${label}`);
}

requireMatch(project, /<PlatformTarget>x64<\/PlatformTarget>/, "PlatformTarget=x64");
requireMatch(project, /<Prefer32Bit>false<\/Prefer32Bit>/, "Prefer32Bit=false");
requireMatch(project, /<TargetFrameworkVersion>v4\.7\.2<\/TargetFrameworkVersion>/,
    "TargetFrameworkVersion=v4.7.2");
requireMatch(project, /<HdtDirectory\s+Condition="[^"]+">[^<]+<\/HdtDirectory>/,
    "conditional HdtDirectory");
requireMatch(project, /<Deterministic>true<\/Deterministic>/, "deterministic compiler output");
requireAbsent(project, /AnyCPU/i, "AnyCPU target");
requireAbsent(project, /<PlatformTarget>x86<\/PlatformTarget>/i, "x86 target");

for (const reference of ["HearthstoneDeckTracker.exe", "HearthDb.dll", "Newtonsoft.Json.dll"]) {
    const escaped = reference.replace(".", "\\.");
    requireMatch(project,
        new RegExp(`<Reference Include="[^"]+">[\\s\\S]*?<HintPath>\\$\\(HdtDirectory\\)\\\\${escaped}<\\/HintPath>[\\s\\S]*?<Private>false<\\/Private>[\\s\\S]*?<\\/Reference>`),
        `${reference} reference with Private=false`);
}

const compileItems = [...project.matchAll(/<Compile Include="([^"]+)"/g)]
    .map(match => match[1].replace(/[\\/]+/g, "\\"))
    .sort();
const compileDigest = crypto.createHash("sha256")
    .update(`${compileItems.join("\n")}\n`, "utf8")
    .digest("hex");
assert.ok(compileItems.includes("Core\\PluginIntegrityVerifier.cs"),
    "runtime integrity verifier must be compiled into BobCoach.dll");
assert.ok(compileItems.includes("Core\\Decision\\ActionFeatureSchema.cs"),
    "versioned action feature schema must be compiled into BobCoach.dll");
assert.ok(compileItems.includes("Core\\Decision\\ActionFeatureExtractor.cs"),
    "action feature extractor must be compiled into BobCoach.dll");
assert.ok(compileItems.includes("Core\\Decision\\LinearPairwiseRanker.cs"),
    "linear pairwise ranker must be compiled into BobCoach.dll");
assert.ok(compileItems.includes("Core\\Decision\\DecisionConfidenceEvaluator.cs"),
    "decision confidence evaluator must be compiled into BobCoach.dll");
assert.ok(compileItems.includes("Core\\Decision\\ExpectedShopEstimator.cs"),
    "expected shop estimator must be compiled into BobCoach.dll");
assert.ok(compileItems.includes("Core\\Decision\\BeamPlanner.cs"),
    "beam planner must be compiled into BobCoach.dll");
assert.ok(compileItems.includes("Core\\Decision\\UnifiedRankingPolicy.cs"),
    "unified ranking policy must be compiled into BobCoach.dll");
assert.ok(compileItems.includes("Core\\Decision\\ExplanationBuilder.cs"),
    "contribution explanation builder must be compiled into BobCoach.dll");
assert.ok(compileItems.includes("Core\\Decision\\DecisionPolicyFactory.cs"),
    "decision policy factory must be compiled into BobCoach.dll");
assert.ok(compileItems.includes("Core\\Decision\\ProductionSeasonFeatureExtension.cs"),
    "production season approval provider must be compiled into BobCoach.dll");
assert.ok(compileItems.includes("Core\\Decision\\ObservedActionInference.cs"),
    "observed player actions must use the evidence-based inference helper");
assert.ok(compileItems.includes("Core\\TrinketDefinitionRegistry.cs"),
    "trinket definition registry must be compiled into BobCoach.dll");
assert.ok(compileItems.includes("Core\\CompGuideCatalog.cs"),
    "comp guide catalog must be compiled into BobCoach.dll");
assert.ok(compileItems.includes("Core\\SeasonMetaBaseline.cs"),
    "season meta baseline must be compiled into BobCoach.dll");
assert.ok(compileItems.includes("Core\\SeasonDataSelector.cs"),
    "season data selector must be compiled into BobCoach.dll");
assert.ok(compileItems.includes("Core\\DisplayNameResolver.cs"),
    "display name resolver must be compiled into BobCoach.dll");
assert.ok(compileItems.includes("Core\\DarkGiftCandidateAdvisor.cs"),
    "dark gift candidate advisor must be compiled into BobCoach.dll");
    assert.ok(compileItems.includes("Core\\TurnProjector.cs"),
        "cross-turn projector must be compiled into BobCoach.dll");
    assert.ok(compileItems.includes("Core\\Decision\\StrategicPlan.cs"),
        "strategic plan contract must be compiled into BobCoach.dll");
    assert.ok(compileItems.includes("Core\\Decision\\StrategicMemory.cs"),
        "strategic memory must be compiled into BobCoach.dll");
    assert.ok(compileItems.includes("Core\\Decision\\SequenceSearchPlanner.cs"),
        "sequence search planner must be compiled into BobCoach.dll");
    assert.ok(compileItems.includes("Core\\Decision\\StrategySequencePlanner.cs"),
        "strategy sequence planner must be compiled into BobCoach.dll");
    assert.ok(compileItems.includes("Core\\HeroPlaybookProfile.cs"),
        "hero playbook profile must be compiled into BobCoach.dll");
    assert.ok(compileItems.includes("Core\\HeroPlaybookRegistry.cs"),
        "hero playbook registry must be compiled into BobCoach.dll");
    assert.ok(compileItems.includes("Core\\HeroPlaybookDecisionService.cs"),
        "hero playbook decision service must be compiled into BobCoach.dll");
    assert.ok(compileItems.includes("Core\\DeterministicRng.cs"),
        "deterministic rng for decision pipeline must be compiled into BobCoach.dll");
    assert.strictEqual(compileItems.length, 248, "unexpected compile item count");
    assert.strictEqual(compileDigest,
        "cc3bb959c5021ffcc18cb4379f8d5a5c62ecf030b79e0bb1f94e5f5a65559d3e",
        "compile set changed");

for (const token of [
    "release_identity.json",
    "BOBCOACH_MSBUILD",
    "vswhere.exe",
    "/p:Configuration=Release",
    "GetAssemblyName",
    "GetPEKind",
    "PE32Plus",
    "AMD64",
]) {
    assert.ok(builder.includes(token), `build_release.ps1 missing ${token}`);
}
requireAbsent(builder, /D:\\\\Program Files.*Visual Studio/i, "fixed D-drive Visual Studio path");

console.log(`PASS release build contract compile=${compileItems.length} sha256=${compileDigest}`);
