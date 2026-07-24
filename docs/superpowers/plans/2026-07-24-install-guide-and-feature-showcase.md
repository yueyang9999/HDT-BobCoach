# BobCoach Install Guide And Feature Showcase Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 更新 BobCoach 图文安装教程，并新增适合炉石玩家浏览的五项功能展示页。

**Architecture:** 安装教程和功能展示均提供 Markdown 与纯静态 HTML 两种入口，共用仓库内经脱敏压缩的本地图片。文档合同测试负责检查步骤、图片引用、离线资源和公开内容边界，现有发布构建继续验证安装包内容。

**Tech Stack:** Markdown、静态 HTML/CSS、PowerShell `System.Drawing`、Node.js 文档合同测试、.NET Release 构建

## Global Constraints

- 不修改插件运行时代码、CI/CD、密钥或公开 Release。
- 原始截图不进入 Git；最终功能截图必须裁剪、脱敏、压缩，最长边不超过 1600 像素。
- HTML 不使用网络资源，Markdown 与 HTML 的操作事实必须一致。
- `BobCoach.dll` 和 `BobCoach.dll.sha256` 必须一起复制并保持相邻。

---

### Task 1: 固化公开图片目录规则和文档合同

**Files:**
- Modify: `AGENTS.md`
- Modify: `NOTICE`
- Modify: `tests/docs/install-guide.test.js`
- Create or modify: `tests/docs/feature-showcase.test.js`

**Interfaces:**
- Consumes: `docs/user/images/install/` 和新的 `docs/user/images/features/` 目录约定。
- Produces: 对安装步骤、五项功能、图片尺寸、图片引用和离线 HTML 的自动验证。

- [ ] **Step 1: 更新失败的文档合同测试**

测试必须断言安装教程包含“打开插件文件夹”、`BobCoach.dll.sha256` 和 BobCoach 开关说明；功能页必须包含五个功能名、免责声明、五个本地图片引用，且 HTML 不出现 `http://`、`https://`、`//cdn` 等远程资源。

- [ ] **Step 2: 运行文档合同并确认失败**

Run: `npm test -- --runInBand`

Expected: FAIL，原因是功能页和新图片尚不存在，或安装教程仍使用旧入口。

- [ ] **Step 3: 更新公开内容规则与第三方说明**

在 `AGENTS.md` 中只额外允许 `docs/user/images/features/` 下的脱敏压缩成品图；在 `NOTICE` 中声明截图中的游戏、HDT 和第三方内容归各自权利人所有。

### Task 2: 生成安装与功能展示成品图

**Files:**
- Modify: `docs/user/images/install/install-02-open-plugins-folder.png`
- Modify: `docs/user/images/install/install-04-enable-bobcoach.png`
- Create: `docs/user/images/features/feature-01-buy.jpg`
- Create: `docs/user/images/features/feature-02-upgrade.jpg`
- Create: `docs/user/images/features/feature-03-hero-power.jpg`
- Create: `docs/user/images/features/feature-04-trinket.jpg`
- Create: `docs/user/images/features/feature-05-discover.jpg`

**Interfaces:**
- Consumes: 用户提供的六张本地原始截图。
- Produces: 页面可直接引用的隐私清理图片。

- [ ] **Step 1: 在仓库外生成预览图**

使用 PowerShell `System.Drawing` 读取原图，裁取所需界面，遮挡昵称与分数，按比例缩放。不得覆盖原图。

- [ ] **Step 2: 人工检查预览图**

确认安装图能看清 BobCoach 开关和“打开插件文件夹”，五张功能图能看清推荐标识，并且没有昵称、分数、个人路径或无关桌面内容。

- [ ] **Step 3: 输出最终图片并检查尺寸**

用 `System.Drawing.Image.Width`、`Height` 和文件 bytes 验证所有功能图最长边不超过 1600 像素，且没有原始大图进入仓库。

### Task 3: 更新教程并建立功能展示页

**Files:**
- Modify: `docs/user/INSTALL.md`
- Modify: `docs/user/INSTALL.html`
- Create: `docs/user/FEATURES.md`
- Create: `docs/user/FEATURES.html`
- Modify: `README.md`
- Modify: `docs/README.md`

**Interfaces:**
- Consumes: Task 2 的稳定图片文件名。
- Produces: GitHub 阅读入口和可离线打开的独立 HTML。

- [ ] **Step 1: 更新 Markdown 安装教程**

将打开目录方式改为 HDT“选项 > 插件 > 打开插件文件夹”，正文明确复制 DLL 和相邻 SHA 后返回 HDT 开启 BobCoach。

- [ ] **Step 2: 同步独立 HTML 安装教程**

保持与 Markdown 相同的事实和图片，正文容器最大宽度约 960 像素，移动端单列显示。

- [ ] **Step 3: 创建 Markdown 与 HTML 功能展示页**

按购买、升本、技能、饰品、发现的顺序书写，每项包含一句玩家可理解的说明和一张实战图；页面说明插件只提供建议、不自动操作。

- [ ] **Step 4: 更新仓库导航**

在 `README.md` 和 `docs/README.md` 中增加功能展示入口及一句摘要，不在首页重复嵌入五张图片。

- [ ] **Step 5: 运行文档合同测试**

Run: `npm test`

Expected: PASS，所有图片引用和文档合同均通过。

### Task 4: 完整验证、提交与已授权推送

**Files:**
- Verify all changed files.

**Interfaces:**
- Consumes: Tasks 1-3 的全部文档和图片。
- Produces: 可推送到 `origin/main` 的已验证提交。

- [ ] **Step 1: 按维护文档运行完整验证**

Run: 按 `docs/maintainer/BUILD.md` 执行 clean restore、Release build、`npm test`、发布身份和 allowlist 检查、敏感信息/个人路径/大文件扫描。

Expected: 所有检查 PASS；允许记录既有且不影响构建的编译警告。

- [ ] **Step 2: 检查变更边界**

Run: `git diff --check`、`git status --short`、`git diff --stat`。

Expected: 无空白错误、无原始截图、无 DLL/ZIP/构建产物进入提交。

- [ ] **Step 3: 创建文档提交**

Run: `git add` 仅暂存本计划列出的文档、测试和成品图片，然后 `git commit -m "docs: add feature showcase and refresh install guide"`。

Expected: 新提交位于既有 `a9acc6e` 之后。

- [ ] **Step 4: 推送并监控 CI**

Run: `git push origin main`，随后查看该提交触发的 GitHub Actions。

Expected: `origin/main` 包含 `a9acc6e` 和本次文档提交，CI 全部成功。公开 Release 仍不创建。
