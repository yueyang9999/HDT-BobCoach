"use strict";

const assert = require("assert");
const fs = require("fs");
const path = require("path");

const root = path.resolve(__dirname, "..");
const read = (relativePath) => fs.readFileSync(path.join(root, relativePath), "utf8");
const chineseReadme = read("README.md");
const englishReadme = read("README.en.md");
const installGuide = read("docs/user/INSTALL.md");
const docsIndex = read("docs/README.md");
const dataSources = read("DATA_SOURCES.md");
const notice = read("NOTICE");
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

assert.ok(chineseReadme.includes("[文档目录](docs/README.md)"), "Chinese README must expose the documentation index");
assert.ok(englishReadme.includes("[Documentation index](docs/README.md)"), "English README must expose the documentation index");

for (const heading of ["普通用户", "维护者", "贡献者", "政策与合规", "设计与实施历史", "包内文档模板"]) {
    assert.ok(docsIndex.includes(`## ${heading}`), `documentation index must contain ${heading}`);
}

for (const link of [
    "user/INSTALL.md",
    "maintainer/BUILD.md",
    "../CONTRIBUTING.md",
    "../DATA_SOURCES.md",
    "design/公开产品仓库治理设计_2026-07-20.md",
    "superpowers/plans/2026-07-21-chinese-download-guidance.md",
    "../tools/release/README_OFFLINE.md",
]) {
    assert.ok(docsIndex.includes(`](${link})`), `documentation index must link ${link}`);
}

for (const text of [
    "https://static.zerotoheroes.com/api/bgs/trinket-stats/last-patch/overview-from-hourly.gz.json",
    "https://api.hearthstonejson.com/v1/<build>/enUS/cards.json",
    "公开应用许可待书面确认",
    "当前不接入、不请求、不抓取、不打包、不再分发 HSReplay 数据",
    "2026-07-21",
]) {
    assert.ok(dataSources.includes(text), `data source registry must contain ${text}`);
}

assert.ok(notice.includes("v0.2.0-beta.1"), "NOTICE must acknowledge the existing public release");
assert.ok(notice.includes("separate explicit owner authorization"), "NOTICE must require authorization for each future release");
assert.ok(!notice.includes("GitHub Release publication is not authorized"), "NOTICE must not retain the obsolete release statement");

console.log("PASS public documentation contract");
