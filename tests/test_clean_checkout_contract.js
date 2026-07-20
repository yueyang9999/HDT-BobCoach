"use strict";

const assert = require("assert");
const fs = require("fs");
const path = require("path");

const root = path.resolve(__dirname, "..");
const requiredFiles = [
    "AGENTS.md",
    "README.md",
    "CHANGELOG.md",
    "CONTRIBUTING.md",
    "SECURITY.md",
    "CODE_OF_CONDUCT.md",
    "package.json",
    ".gitignore",
    ".gitattributes",
    "release_identity.json",
    "src/BobCoach/BobCoach.csproj",
    "tools/build/build_release.ps1",
    "tools/release/build_offline_package.ps1",
    "tools/build/validate_repository.ps1",
    "docs/user/INSTALL.md",
    "docs/user/UPGRADE.md",
    "docs/user/ROLLBACK.md",
    "docs/user/UNINSTALL.md",
    "docs/user/TROUBLESHOOTING.md",
    "docs/maintainer/ARCHITECTURE.md",
    "docs/maintainer/BUILD.md",
    "docs/maintainer/DEPENDENCIES.md",
    "docs/maintainer/RELEASE.md",
    ".github/workflows/ci.yml",
    ".github/ISSUE_TEMPLATE/bug_report.yml",
    ".github/ISSUE_TEMPLATE/feature_request.yml",
    ".github/pull_request_template.md",
    ".github/dependabot.yml",
];

for (const relativePath of requiredFiles) {
    assert.ok(fs.existsSync(path.join(root, relativePath)), `missing ${relativePath}`);
}

const packageJson = JSON.parse(fs.readFileSync(path.join(root, "package.json"), "utf8"));
assert.strictEqual(packageJson.private, true, "npm publication must remain disabled");
assert.deepStrictEqual(packageJson.dependencies || {}, {}, "runtime npm dependencies are forbidden");
assert.deepStrictEqual(packageJson.devDependencies || {}, {}, "development npm dependencies are forbidden");
for (const forbiddenScript of ["start", "start:win", "test:decision", "test:combat", "export-data"]) {
    assert.ok(!packageJson.scripts[forbiddenScript], `legacy script remains: ${forbiddenScript}`);
}
for (const requiredScript of ["test", "test:contracts", "test:package", "build:hdt", "package:offline"]) {
    assert.ok(packageJson.scripts[requiredScript], `missing npm script: ${requiredScript}`);
}

for (const forbiddenDirectory of ["data", "_authority", "simulation", "electron", "replays", "artifacts", "eval"]) {
    assert.ok(!fs.existsSync(path.join(root, forbiddenDirectory)), `forbidden directory: ${forbiddenDirectory}`);
}

assert.ok(!fs.existsSync(path.join(root, "package-lock.json")), "dependency lockfile is unnecessary");
console.log("PASS clean public checkout contract");
