"use strict";

const assert = require("assert");
const fs = require("fs");
const path = require("path");

const scriptPath = path.join(__dirname, "test_powerlog_choice_batch_behavior.js");
const source = fs.readFileSync(scriptPath, "utf8");

assert.ok(
    source.includes('require("os")'),
    "PowerLog behavior test must use the operating-system temporary directory",
);
assert.ok(
    source.includes("fs.mkdtempSync(path.join(os.tmpdir()"),
    "PowerLog behavior test must allocate a unique temporary output directory",
);
assert.ok(
    source.includes("fs.rmSync(outputDir, { recursive: true, force: true })"),
    "PowerLog behavior test must clean its temporary output directory",
);
assert.ok(
    !source.includes('path.join(root, "eval")'),
    "PowerLog behavior test must not write generated files into the repository",
);

console.log("PASS behavior tests keep generated output outside the repository");
