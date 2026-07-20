"use strict";

const assert = require("assert");
const childProcess = require("child_process");
const fs = require("fs");
const os = require("os");
const path = require("path");

const repositoryRoot = path.resolve(__dirname, "..");
const validatorPath = path.join(repositoryRoot, "tools", "build", "validate_repository.ps1");
const tempPrefix = "bobcoach-validator-test-";

function createRepository() {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), tempPrefix));
    childProcess.execFileSync("git", ["init", "-q", root], { stdio: "pipe" });
    return root;
}

function removeRepository(root) {
    const tempRoot = path.resolve(os.tmpdir()) + path.sep;
    const resolved = path.resolve(root);
    assert.ok(resolved.startsWith(tempRoot), `unsafe temporary cleanup path: ${resolved}`);
    assert.ok(path.basename(resolved).startsWith(tempPrefix), `unexpected temporary directory: ${resolved}`);
    fs.rmSync(resolved, { recursive: true, force: true });
}

function writeFile(root, relativePath, content) {
    const fullPath = path.join(root, relativePath);
    fs.mkdirSync(path.dirname(fullPath), { recursive: true });
    fs.writeFileSync(fullPath, content);
}

function trackFile(root, relativePath) {
    childProcess.execFileSync("git", ["-C", root, "add", "-f", "--", relativePath], { stdio: "pipe" });
}

function runValidator(root) {
    return childProcess.spawnSync(
        "powershell.exe",
        ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", validatorPath, "-RepositoryRoot", root],
        { encoding: "utf8" },
    );
}

function assertRejected(relativePath, category, content, tracked) {
    const root = createRepository();
    try {
        writeFile(root, relativePath, content);
        if (tracked) {
            trackFile(root, relativePath);
        }
        const result = runValidator(root);
        const output = `${result.stdout}\n${result.stderr}`;
        assert.notStrictEqual(result.status, 0, `validator accepted ${relativePath}: ${output}`);
        assert.match(output, new RegExp(`\\[${category}\\].*${relativePath.replace(/[.*+?^${}()|[\\]\\\\]/g, "\\$&")}`, "i"));
    } finally {
        removeRepository(root);
    }
}

{
    const root = createRepository();
    try {
        writeFile(root, "src/allowed.txt", "Reference location: C:\\Program Files\\HDT\\HearthstoneDeckTracker.exe\n");
        trackFile(root, "src/allowed.txt");
        const result = runValidator(root);
        assert.strictEqual(result.status, 0, `${result.stdout}\n${result.stderr}`);
        assert.match(result.stdout, /PASS repository validation/i);
    } finally {
        removeRepository(root);
    }
}

const token = "gh" + "p_" + "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMN";
assertRejected("src/config.txt", `secret-or-token`, `credential=${token}\n`, false);
assertRejected("src/settings.txt", "personal-absolute-path", "source=C:" + "\\" + "Users" + "\\" + "Example" + "\\" + "project\n", false);
assertRejected("captures/match.replay", "replay-or-log-data", "synthetic replay\n", false);
assertRejected("logs/runtime.log", "replay-or-log-data", "synthetic log\n", true);
assertRejected(".env", "sensitive-file", "MODE=test\n", true);
assertRejected("plugins/BobCoach.dll", "forbidden-binary-or-image", Buffer.from([0, 1, 2]), false);
assertRejected("plugins/HearthstoneDeckTracker.exe", "forbidden-binary-or-image", Buffer.from([0, 1, 2]), false);
assertRejected("archives/release.zip", "forbidden-binary-or-image", Buffer.from([0, 1, 2]), false);
assertRejected("evidence/screenshot.png", "forbidden-binary-or-image", Buffer.from([0x89, 0x50, 0x4e, 0x47]), false);
assertRejected("vm/snapshot.vhd", "forbidden-directory", Buffer.from([0, 1, 2]), false);
assertRejected("images/base.iso", "forbidden-binary-or-image", Buffer.from([0, 1, 2]), false);
assertRejected("local-data/cache.txt", "forbidden-directory", "synthetic cache\n", false);
const utf16Token = Buffer.concat([
    Buffer.from([0xff, 0xfe]),
    Buffer.from(`credential=${token}\n`, "utf16le"),
]);
assertRejected("src/utf16-config.txt", "secret-or-token", utf16Token, false);
assertRejected("src/large.txt", "large-file", Buffer.alloc(5 * 1024 * 1024 + 1, 0x61), false);

console.log("PASS repository validator contract");
