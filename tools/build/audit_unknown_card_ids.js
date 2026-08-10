"use strict";

const fs = require("fs");
const path = require("path");

const root = path.resolve(__dirname, "..", "..");
const sourceRoot = path.join(root, "src", "BobCoach");
const forbiddenPatterns = [
    /CardName\s*\?\?\s*CardId/,
    /CardName\s*\?\?\s*"随从"/,
    /CardName\s*\?\?\s*"\?"/,
];

function walk(dir, results) {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
        const full = path.join(dir, entry.name);
        if (entry.isDirectory()) {
            walk(full, results);
        } else if (entry.name.endsWith(".cs") || entry.name.endsWith(".js")
            || entry.name.endsWith(".ts")) {
            const text = fs.readFileSync(full, "utf8");
            for (const pattern of forbiddenPatterns) {
                if (pattern.test(text)) {
                    results.push(`${path.relative(root, full)} matches ${pattern}`);
                }
            }
        }
    }
}

const cardNameService = fs.readFileSync(
    path.join(sourceRoot, "Core", "CardNameService.cs"), "utf8");
if (!cardNameService.includes('return "unknown";')) {
    console.error("FAIL CardNameService must return unknown for unresolved card IDs");
    process.exit(1);
}

const results = [];
walk(sourceRoot, results);
if (results.length > 0) {
    console.error("FAIL unknown card ID fallback audit:");
    for (const line of results) console.error("  " + line);
    process.exit(1);
}

console.log("PASS unknown card ID fallback audit");
