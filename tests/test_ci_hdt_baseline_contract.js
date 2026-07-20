"use strict";

const assert = require("assert");
const fs = require("fs");
const path = require("path");

const workflowPath = path.resolve(__dirname, "..", ".github", "workflows", "ci.yml");
const workflow = fs.readFileSync(workflowPath, "utf8");
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

console.log("PASS CI HDT baseline normalization contract");
