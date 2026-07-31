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
assert.ok(compileItems.includes("Core\\Decision\\UnifiedRankingPolicy.cs"),
    "unified ranking policy must be compiled into BobCoach.dll");
assert.strictEqual(compileItems.length, 191, "unexpected compile item count");
assert.strictEqual(compileDigest,
    "f9c8a29f7b32aea7d8336c963660d6473527dc4b175f9ae89253ecb20316d8cd",
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
