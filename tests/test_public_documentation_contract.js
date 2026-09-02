"use strict";

const assert = require("assert");
const fs = require("fs");
const path = require("path");

const root = path.resolve(__dirname, "..");
const read = (relativePath) => fs.readFileSync(path.join(root, relativePath), "utf8");
const chineseReadme = read("README.md");
const englishReadme = read("README.en.md");
const installGuide = read("docs/user/INSTALL.md");
const featuresGuide = read("docs/user/FEATURES.md");
const featuresHtml = read("docs/user/FEATURES.html");
const docsIndex = read("docs/README.md");
const dataSources = read("DATA_SOURCES.md");
const privacy = read("PRIVACY.md");
const notice = read("NOTICE");
const architecture = read("docs/maintainer/ARCHITECTURE.md");
const dependencies = read("docs/maintainer/DEPENDENCIES.md");
const bilingualDisclosureDesign = read("docs/design/双语自述与第三方声明设计_2026-07-21.md");
const packageName = "BobCoach-1.2.0-win-x64.zip";
const releasesUrl = "https://github.com/yueyang9999/HDT-BobCoach/releases";
const retiredPackageUrl = "https://github.com/yueyang9999/HDT-BobCoach/releases/download/v0.2.0-beta.1/";
const stablePackageUrl = "https://github.com/yueyang9999/HDT-BobCoach/releases/download/v1.2.0/";

for (const [name, content] of [
    ["README.md", chineseReadme],
    ["README.en.md", englishReadme],
    ["docs/user/INSTALL.md", installGuide],
]) {
    assert.ok(content.includes(packageName), `${name} must name the current 1.1.0 package`);
    assert.ok(content.includes(releasesUrl), `${name} must direct users to the Releases page`);
    assert.ok(!content.includes(retiredPackageUrl), `${name} must not link to the retired beta.1 package`);
    assert.ok(content.includes(`${stablePackageUrl}${packageName}`), `${name} must link the official 1.1.0 installer`);
    assert.ok(content.includes(`${stablePackageUrl}${packageName}.sha256`), `${name} must link the official 1.1.0 checksum`);
    assert.ok(content.includes("Windows 11 24H2 x64"), `${name} must identify Windows 11`);
    assert.ok(content.includes("Windows 10 22H2 x64"), `${name} must identify Windows 10`);
    assert.ok(content.includes("Source code (zip)"), `${name} must reject the generated ZIP source archive`);
    assert.ok(content.includes("Source code (tar.gz)"), `${name} must reject the generated tar source archive`);
}

assert.ok(
    chineseReadme.includes("[中文安装教程（新手从这里开始）](docs/user/INSTALL.md)"),
    "Chinese README must expose the beginner installation guide",
);
assert.ok(chineseReadme.includes("候选"), "Chinese README must identify v1.2.0 as a release candidate");
assert.ok(chineseReadme.includes("尚未完成专用实机验收"), "Chinese README must preserve Win10 limitation");
assert.ok(
    englishReadme.includes("release candidate"),
    "English README must identify v1.2.0 as a release candidate",
);
assert.ok(
    englishReadme.includes("dedicated physical validation has not been completed"),
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
    "2026-07-22",
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
    ["docs/design/双语自述与第三方声明设计_2026-07-21.md", bilingualDisclosureDesign, "不请求、不缓存、不使用 Firestone/Zero to Heroes 饰品统计"],
]) {
    assert.ok(content.includes(statement), `${name} must document the retired Firestone runtime boundary`);
}

for (const [name, content, statements] of [
    [
        "README.md",
        chineseReadme,
        ["GitHub 仓库中的代码、文档、规则快照、议题、PR、镜像、fork、评论和外部链接不等于官方数据、完整数据或最新数据"],
    ],
    [
        "README.en.md",
        englishReadme,
        ["GitHub repository code, documentation, rule snapshots, issues, pull requests, mirrors, forks, comments, and external links are not official, complete, or current third-party data"],
    ],
    ["DATA_SOURCES.md", dataSources, ["GitHub 仓库数据免责声明"]],
    ["PRIVACY.md", privacy, ["GitHub 仓库中的内容不代表运行时会读取用户 GitHub 账户、私有仓库或历史缓存"]],
    ["NOTICE", notice, ["GitHub repository contents are not official third-party data"]],
]) {
    for (const statement of statements) {
        assert.ok(content.includes(statement), `${name} must state: ${statement}`);
    }
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

assert.ok(notice.includes("v1.0.0"), "NOTICE must preserve the historical stable release disclosure");
assert.ok(notice.includes("v1.2.0"), "NOTICE must identify the current release candidate");
assert.ok(notice.includes("not published"), "NOTICE must not claim the v1.2.0 candidate is published");
assert.ok(notice.includes("Final physical smoke validation on Windows 11 24H2 x64 was not"), "NOTICE must disclose the skipped final physical smoke validation");
assert.ok(notice.includes("separate explicit owner authorization"), "NOTICE must require authorization for each future release");

for (const [name, content, statement] of [
    ["README.md", chineseReadme, "releases/download/v1.2.0/BobCoach-1.2.0-win-x64.zip"],
    ["README.en.md", englishReadme, "releases/download/v1.2.0/BobCoach-1.2.0-win-x64.zip"],
    ["docs/user/INSTALL.md", installGuide, "releases/download/v1.2.0/BobCoach-1.2.0-win-x64.zip"],
]) {
    assert.ok(content.includes(statement), `${name} must link the official 1.1.0 installer`);
}

for (const [name, content] of [["docs/user/FEATURES.md", featuresGuide], ["docs/user/FEATURES.html", featuresHtml]]) {
    for (const token of ["选项 > 插件 > Bob教练设置", "字体大小", "解锁状态面板", "Hearthstone 窗口", "鼠标穿透", "恢复默认"]) {
        assert.ok(content.includes(token), `${name} must document status-strip workflow token: ${token}`);
    }
}

console.log("PASS public documentation contract");
