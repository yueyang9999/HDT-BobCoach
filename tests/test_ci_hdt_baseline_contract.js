"use strict";

const assert = require("assert");
const fs = require("fs");
const path = require("path");

const workflowPath = path.resolve(__dirname, "..", ".github", "workflows", "ci.yml");
const resolverPath = path.resolve(__dirname, "..", "tools", "build", "prepare_latest_hdt.ps1");
const buildReleasePath = process.env.BOBCOACH_BUILD_RELEASE_SCRIPT
    ? path.resolve(process.env.BOBCOACH_BUILD_RELEASE_SCRIPT)
    : path.resolve(__dirname, "..", "tools", "build", "build_release.ps1");
const workflow = fs.readFileSync(workflowPath, "utf8");
assert.ok(fs.existsSync(resolverPath), "latest HDT resolver script must exist");
const resolver = fs.readFileSync(resolverPath, "utf8");
assert.ok(fs.existsSync(buildReleasePath), `release build script must exist: ${buildReleasePath}`);
const buildRelease = fs.readFileSync(buildReleasePath, "utf8");
const enumerateOfficialExecutablePattern =
    /^[ \t]*\$executables[ \t]*=[ \t]*@\(Get-ChildItem[^\r\n]*-Filter[ \t]+'Hearthstone Deck Tracker\.exe'\)[ \t]*$/m;
const buildReferencePattern =
    /^[ \t]*\$buildReference[ \t]*=[ \t]*Join-Path[ \t]+\$hdtDirectory[ \t]+'HearthstoneDeckTracker\.exe'[ \t]*$/m;
const copyAliasPattern =
    /^[ \t]*Copy-Item[ \t]+-LiteralPath[ \t]+\$executables\[0\]\.FullName[ \t]+-Destination[ \t]+\$buildReference[ \t]*$/m;

assert.match(
    workflow,
    enumerateOfficialExecutablePattern,
    "CI must enumerate the exact executable name used by the pinned official HDT ZIP",
);
assert.match(
    workflow,
    buildReferencePattern,
    "CI must build the normalized reference path inside the discovered HDT directory",
);
assert.match(
    workflow,
    copyAliasPattern,
    "CI must copy the discovered official executable to the normalized build reference",
);
assert.doesNotMatch(
    "Copy-Item\n-LiteralPath $executables[0].FullName\n-Destination $buildReference",
    copyAliasPattern,
    "CI contract must reject an invalid copy command split across lines",
);
assert.doesNotMatch(
    workflow,
    /Get-ChildItem[^\r\n]*-Filter\s+'HearthstoneDeckTracker\.exe'/,
    "CI must not enumerate the normalized alias before it has been created",
);

assert.match(workflow, /Download and verify HDT 1\.54\.0 baseline/, "CI must pin the validated baseline");
assert.match(
    workflow,
    /releases\/download\/v1\.54\.0\/Hearthstone\.Deck\.Tracker-v1\.54\.0\.zip/,
    "CI must use the exact validated baseline asset",
);
assert.match(
    workflow,
    /c7dd6093d28e68876ea8971659b10aa1eb379d4c6cf91eb809ea13a43ab1c673/,
    "CI must verify the validated baseline SHA-256",
);
assert.doesNotMatch(workflow, /v1\.53\.5|1\.53\.14/, "CI must not retain an obsolete HDT baseline");

const latestJobMarker = "  latest-hdt-compatibility:";
const latestJobIndex = workflow.indexOf(latestJobMarker);
assert.notStrictEqual(latestJobIndex, -1, "CI must include a latest HDT compatibility job");
assert.match(
    workflow,
    /^  schedule:\r?\n    - cron: '[^']+'$/m,
    "CI must schedule automatic latest HDT compatibility checks",
);
const baselineJob = workflow.slice(0, latestJobIndex);
assert.match(
    baselineJob,
    /^    if: github\.event_name != 'schedule'$/m,
    "scheduled runs must skip the pinned release-baseline job",
);
const latestJob = workflow.slice(latestJobIndex);
assert.match(latestJob, /prepare_latest_hdt\.ps1/, "latest job must use the shared resolver");
assert.match(latestJob, /-AllowNewerHdt/, "latest job must opt into compatibility mode");
assert.doesNotMatch(
    latestJob,
    /build_offline_package\.ps1/,
    "latest compatibility job must not create a formal release package",
);

assert.match(
    buildRelease,
    /\[switch\]\s*\$AllowNewerHdt/,
    "release build script must declare the latest-HDT opt-in switch",
);
assert.match(
    buildRelease,
    /\$baselineVersion\s*=\s*\[Version\]\s*\[string\]\s*\$identity\.hdtBaselineVersion/,
    "release build script must derive its comparison baseline from release identity",
);
assert.match(
    buildRelease,
    /if\s*\(\s*\$AllowNewerHdt\s*\)\s*\{\s*if\s*\(\s*\$hdtVersion\s*-lt\s*\$baselineVersion\s*\)/s,
    "latest-HDT opt-in must still reject versions older than the release baseline",
);
assert.match(
    buildRelease,
    /\}\s*elseif\s*\(\s*\$hdtVersion\s*-ne\s*\$baselineVersion\s*\)\s*\{/s,
    "default release builds must require the exact release baseline",
);

for (const token of [
    "api.github.com/repos/HearthSim/Hearthstone-Deck-Tracker/releases/latest",
    "prerelease",
    "draft",
    "digest",
    "Hearthstone.Deck.Tracker-v",
    "Hearthstone Deck Tracker.exe",
    "HearthstoneDeckTracker.exe",
    "HearthDb.dll",
    "Newtonsoft.Json.dll",
]) {
    assert.ok(resolver.includes(token), `latest HDT resolver missing ${token}`);
}

console.log("PASS CI pinned baseline and latest HDT compatibility contract");
