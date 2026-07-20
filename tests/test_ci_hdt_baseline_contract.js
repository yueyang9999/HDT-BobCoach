"use strict";

const assert = require("assert");
const fs = require("fs");
const path = require("path");

const workflowPath = path.resolve(__dirname, "..", ".github", "workflows", "ci.yml");
const workflow = fs.readFileSync(workflowPath, "utf8");

assert.match(
    workflow,
    /Get-ChildItem[^\r\n]*-Filter\s+'Hearthstone Deck Tracker\.exe'/,
    "CI must enumerate the exact executable name used by the pinned official HDT ZIP",
);
assert.match(
    workflow,
    /\$buildReference\s*=\s*Join-Path\s+\$hdtDirectory\s+'HearthstoneDeckTracker\.exe'/,
    "CI must build the normalized reference path inside the discovered HDT directory",
);
assert.match(
    workflow,
    /Copy-Item\s+-LiteralPath\s+\$executables\[0\]\.FullName\s+-Destination\s+\$buildReference/,
    "CI must copy the discovered official executable to the normalized build reference",
);
assert.doesNotMatch(
    workflow,
    /Get-ChildItem[^\r\n]*-Filter\s+'HearthstoneDeckTracker\.exe'/,
    "CI must not enumerate the normalized alias before it has been created",
);

console.log("PASS CI HDT baseline normalization contract");
