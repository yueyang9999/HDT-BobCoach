"use strict";

const assert = require("assert");
const fs = require("fs");
const os = require("os");
const path = require("path");
const { execFileSync } = require("child_process");

const root = path.resolve(__dirname, "..");
const tool = path.join(root, "tools", "evaluate", "firestone_review_extract.js");
const temp = fs.mkdtempSync(path.join(os.tmpdir(), "bobcoach-firestone-extract-"));
try {
    const input = path.join(temp, "wal.log");
    const output = path.join(temp, "review-ids.json");
    fs.writeFileSync(input, Buffer.from(
        "prefix reviewId\"$1E4C74DE-23E6-46F1-B92A-39B13F1EC984\" suffix " +
        "reviewId\"$1E4C74DE-23E6-46F1-B92A-39B13F1EC984\" duplicate",
        "latin1"));
    execFileSync(process.execPath, [tool, "--input", input, "--output", output], {
        encoding: "utf8",
        windowsHide: true,
    });
    const result = JSON.parse(fs.readFileSync(output, "utf8"));
    assert.strictEqual(result.count, 1, "review IDs are deduplicated");
    assert.deepStrictEqual(result.reviewIds,
        ["1e4c74de-23e6-46f1-b92a-39b13f1ec984"], "stable normalized review id");
    console.log("PASS firestone review extract");
} finally {
    fs.rmSync(temp, { recursive: true, force: true });
}
