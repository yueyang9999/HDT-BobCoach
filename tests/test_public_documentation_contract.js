"use strict";

const assert = require("assert");
const fs = require("fs");
const path = require("path");

const root = path.resolve(__dirname, "..");
const read = (relativePath) => fs.readFileSync(path.join(root, relativePath), "utf8");
const chineseReadme = read("README.md");
const englishReadme = read("README.en.md");
const installGuide = read("docs/user/INSTALL.md");
const packageName = "BobCoach-0.2.0-beta.1-win-x64.zip";
const packageUrl = `https://github.com/yueyang9999/HDT-BobCoach/releases/download/v0.2.0-beta.1/${packageName}`;

for (const [name, content] of [
    ["README.md", chineseReadme],
    ["README.en.md", englishReadme],
    ["docs/user/INSTALL.md", installGuide],
]) {
    assert.ok(content.includes(packageName), `${name} must name the official package`);
    assert.ok(content.includes(packageUrl), `${name} must link directly to the official package`);
    assert.ok(content.includes("Windows 11 24H2 x64"), `${name} must identify Windows 11`);
    assert.ok(content.includes("Windows 10 22H2 x64"), `${name} must identify Windows 10`);
    assert.ok(content.includes("Source code (zip)"), `${name} must reject the generated ZIP source archive`);
    assert.ok(content.includes("Source code (tar.gz)"), `${name} must reject the generated tar source archive`);
}

assert.ok(
    chineseReadme.includes("[中文安装教程（新手从这里开始）](docs/user/INSTALL.md)"),
    "Chinese README must expose the beginner installation guide",
);
assert.ok(chineseReadme.includes("已完成实机验收"), "Chinese README must preserve Win11 verification status");
assert.ok(chineseReadme.includes("尚未完成专用实机验收"), "Chinese README must preserve Win10 limitation");
assert.ok(englishReadme.includes("physically verified"), "English README must preserve Win11 verification status");
assert.ok(
    englishReadme.includes("not completed dedicated physical validation"),
    "English README must preserve Win10 limitation",
);
assert.ok(installGuide.startsWith("# Bob Coach 中文安装教程\n"), "install guide must have a recognizable Chinese title");

console.log("PASS public documentation contract");
