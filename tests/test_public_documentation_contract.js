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
const privacy = read("PRIVACY.md");
const notice = read("NOTICE");
const architecture = read("docs/maintainer/ARCHITECTURE.md");
const dependencies = read("docs/maintainer/DEPENDENCIES.md");
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
    "https://api.hearthstonejson.com/v1/<build>/enUS/cards.json",
    "历史评估背景",
    "不请求、不缓存、不展示 Firestone/Zero to Heroes 饰品统计",
    "不读取、不迁移、不删除既有历史缓存",
    "重新设计并单独审批",
    "当前不接入、不请求、不抓取、不打包、不再分发 HSReplay 数据",
    "2026-07-21",
]) {
    assert.ok(dataSources.includes(text), `data source registry must contain ${text}`);
}
for (const [name, content] of [
    ["README.md", chineseReadme],
    ["README.en.md", englishReadme],
    ["DATA_SOURCES.md", dataSources],
    ["PRIVACY.md", privacy],
    ["NOTICE", notice],
    ["docs/maintainer/ARCHITECTURE.md", architecture],
    ["docs/maintainer/DEPENDENCIES.md", dependencies],
]) {
    for (const retiredEndpoint of [
        "static.zerotoheroes.com",
        "/api/bgs/trinket-stats/last-patch/overview-from-hourly.gz.json",
    ]) {
        assert.ok(!content.includes(retiredEndpoint), `${name} must not publish retired Firestone endpoint ${retiredEndpoint}`);
    }
}

for (const [name, content, statement] of [
    ["README.md", chineseReadme, "不请求、不缓存、不展示 Firestone/Zero to Heroes 饰品统计"],
    ["README.en.md", englishReadme, "does not request, cache, or display Firestone/Zero to Heroes trinket statistics"],
    ["PRIVACY.md", privacy, "不请求、不缓存、不展示 Firestone/Zero to Heroes 饰品统计"],
    ["NOTICE", notice, "historical evaluation context"],
    ["docs/maintainer/ARCHITECTURE.md", architecture, "不请求、不缓存、不展示 Firestone/Zero to Heroes 饰品统计"],
    ["docs/maintainer/DEPENDENCIES.md", dependencies, "不请求、不缓存、不展示 Firestone/Zero to Heroes 饰品统计"],
]) {
    assert.ok(content.includes(statement), `${name} must document the retired Firestone runtime boundary`);
}

for (const [name, content, statements] of [
    [
        "README.md",
        chineseReadme,
        ["不显示饰品报价选择提示", "显示开关只控制渲染", "版本化本地 `CardId` 规则"],
    ],
    [
        "README.en.md",
        englishReadme,
        ["does not display trinket-offer choice prompts", "display switch controls rendering only", "versioned local `CardId` rules"],
    ],
    [
        "DATA_SOURCES.md",
        dataSources,
        ["GameState.ActiveTrinkets", "EffectiveGameRules", "FeatureExtractor", "ActionScoring", "CombatSimulator", "未知 ID", "合成状态"],
    ],
    [
        "PRIVACY.md",
        privacy,
        ["`GameState.ActiveTrinkets`", "版本化本地 `CardId` 规则", "只控制渲染"],
    ],
    [
        "NOTICE",
        notice,
        ["does not display or prioritize trinket-offer choice", "equipped-trinket effects", "versioned local CardId rules"],
    ],
    [
        "docs/maintainer/ARCHITECTURE.md",
        architecture,
        ["GameState.ActiveTrinkets", "TrinketEffectRegistry", "ActiveTrinketContext", "EffectiveGameRules", "FeatureExtractor", "ActionScoring", "CombatSimulator"],
    ],
    [
        "docs/maintainer/DEPENDENCIES.md",
        dependencies,
        ["版本化本地 `CardId` 规则", "未知 ID", "合成状态"],
    ],
]) {
    for (const statement of statements) {
        assert.ok(content.includes(statement), `${name} must document independent equipped-trinket behavior: ${statement}`);
    }
}

assert.ok(notice.includes("v0.2.0-beta.1"), "NOTICE must acknowledge the existing public release");
assert.ok(notice.includes("separate explicit owner authorization"), "NOTICE must require authorization for each future release");
assert.ok(!notice.includes("GitHub Release publication is not authorized"), "NOTICE must not retain the obsolete release statement");

console.log("PASS public documentation contract");
