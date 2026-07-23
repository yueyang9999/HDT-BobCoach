# BobCoach 新手图文安装教程实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让不熟悉终端的 Windows 玩家在 GitHub 和离线 ZIP 中都能按四步中文图文教程安装并启用 BobCoach。

**Architecture:** 仓库以 `docs/user/INSTALL.md` 作为 GitHub 可渲染的中文权威教程，以 `docs/user/INSTALL.html` 作为可下载和离线复用的单文件入口，四张经过裁切和去隐私处理的成品图统一存放在 `docs/user/images/install/`。打包器仅复制精确列出的 HTML 和图片相对路径，并继续由 manifest、SHA-256 与 ZIP entry 合同锁定文件集合；`INSTALL.ps1` 保留为可选高级安装入口。

**Tech Stack:** Markdown、静态 HTML/CSS、PNG、Node.js 合同测试、PowerShell 离线包构建与生命周期测试。

## Global Constraints

- 默认安装流程必须是退出 HDT、打开 `%AppData%\HearthstoneDeckTracker\Plugins`、复制 `BobCoach.dll` 到该目录根部、启动 HDT 并启用 BobCoach。
- 不要求普通玩家运行 PowerShell；`INSTALL.ps1` 只作为可选高级校验、自动备份和回退入口。
- 每一步必须先有完整文字，再紧跟对应图片；图片失效时仍可仅靠文字完成安装。
- HTML 的图片、脚本、样式和字体只允许使用 ZIP 内本地相对路径，不得从 GitHub、CDN 或其他网络端点自动加载；面向用户的官方下载超链接可以保留。
- 只允许提交 `docs/user/images/install/` 下经过裁切、去隐私处理的安装教程成品截图；原始截图和验收证据不得入库。
- 发行包继续使用精确文件白名单，不允许递归通配复制。
- 不读取用户历史缓存，不使用 `-RemoveUserData`，不恢复 Firestone/Zero to Heroes 请求、缓存、专用解析或自动重试。
- 不修改 `.env`、密钥、token、CI/CD，不创建 Tag、GitHub Release 或公开上传发行包。

---

### Task 1: 建立教程与图片公开合同

**Files:**
- Modify: `AGENTS.md`
- Modify: `tests/test_release_package_contract.js`
- Create: `tests/test_install_guide_contract.js`
- Modify: `tests/run_contract_tests.ps1`

**Interfaces:**
- Consumes: 已批准的 `docs/superpowers/specs/2026-07-23-beginner-friendly-manual-install-design.md`。
- Produces: `expectedGuideImages` 四项稳定相对路径、教程内容断言、离线资源断言和仓库截图精确例外。

- [x] **Step 1: 先更新仓库规则**

将 `AGENTS.md` 的截图禁令改成精确例外：仅允许 `docs/user/images/install/` 下已裁切、去隐私的安装教程成品图，其他截图和原始证据仍禁止提交。

- [x] **Step 2: 写失败的安装教程合同测试**

创建 `tests/test_install_guide_contract.js`，至少断言：

```js
const expectedGuideImages = [
  'docs/user/images/install/install-01-exit-hdt.png',
  'docs/user/images/install/install-02-open-plugins-folder.png',
  'docs/user/images/install/install-03-copy-bobcoach-dll.png',
  'docs/user/images/install/install-04-enable-bobcoach.png',
];
```

测试必须检查 `INSTALL.md` 直接引用四图且有非空替代文本、`INSTALL.html` 只引用本地四图且不存在 `http(s)://` 资源、中文默认流程包含 DLL 根目录复制和 HDT 启用、PowerShell 明确标为可选高级方式、README 中英文与离线说明不再把脚本列为默认必需步骤。

- [x] **Step 3: 把合同测试接入默认测试入口**

在 `tests/run_contract_tests.ps1` 中执行：

```powershell
node (Join-Path $PSScriptRoot "test_install_guide_contract.js")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
```

- [x] **Step 4: 运行测试并确认 RED**

Run: `node tests/test_install_guide_contract.js`

Expected: FAIL，原因是 `INSTALL.html` 和四张图片尚不存在，且现有教程仍把 `INSTALL.ps1` 作为默认步骤。

### Task 2: 实现 GitHub 与离线图文教程源文件

**Files:**
- Modify: `README.md`
- Modify: `README.en.md`
- Modify: `docs/user/INSTALL.md`
- Create: `docs/user/INSTALL.html`
- Create: `docs/user/images/install/install-01-exit-hdt.png`
- Create: `docs/user/images/install/install-02-open-plugins-folder.png`
- Create: `docs/user/images/install/install-03-copy-bobcoach-dll.png`
- Create: `docs/user/images/install/install-04-enable-bobcoach.png`
- Modify: `tools/release/README_OFFLINE.md`

**Interfaces:**
- Consumes: Task 1 的四项图片路径和教程合同。
- Produces: GitHub 可直接阅读的 Markdown 教程、可下载/离线双击打开的 HTML 教程和四张稳定 PNG 资源。

- [x] **Step 1: 制作四张去隐私成品图**

使用真实 Windows/HDT 界面素材制作 1600 px 以内的 PNG，统一使用红色圆形编号；裁掉用户名、个人路径、账号、对局、历史记录、其他插件和无关桌面内容。图中只表达退出 HDT、运行窗口打开插件目录、复制 DLL 到 `Plugins` 根目录、在 HDT 插件页启用四个动作。

- [x] **Step 2: 重写中文 Markdown 主教程**

`docs/user/INSTALL.md` 顶部直接给出四步流程，每个步骤按“动作文字、注意事项、图片、纯文字图注”排列，并链接 `INSTALL.html`。后续章节仅保留升级、三项高频故障处理、可选高级脚本及卸载/回退链接。

- [x] **Step 3: 创建离线 HTML 教程**

`docs/user/INSTALL.html` 使用内嵌 CSS 和以下相对图片路径：

```html
<img src="images/install/install-01-exit-hdt.png" alt="完全退出 Hearthstone Deck Tracker">
```

页面不使用 JavaScript、远程字体、远程样式、远程图片或分析脚本；四步在窄屏和桌面均为清晰单列布局。

- [x] **Step 4: 同步仓库入口与包内说明**

`README.md` 将四步无终端流程放进安装区并同时链接 Markdown 与 HTML；`README.en.md` 保持相同事实；`tools/release/README_OFFLINE.md` 首屏说明双击 `安装教程.html`，并把 `INSTALL.ps1` 移到可选高级章节。

- [x] **Step 5: 运行教程合同并确认 GREEN**

Run: `node tests/test_install_guide_contract.js`

Expected: `PASS beginner-friendly install guide ...`，且四个图片引用均可解析。

### Task 3: 扩展精确离线包白名单

**Files:**
- Modify: `tools/release/build_offline_package.ps1`
- Modify: `tools/release/INSTALL.ps1`
- Modify: `tests/test_release_package_contract.js`
- Modify: `tests/test_offline_package_builder.ps1`

**Interfaces:**
- Consumes: `docs/user/INSTALL.html` 与 `docs/user/images/install/*.png`。
- Produces: ZIP 根部 `安装教程.html`、`images/install/*.png`、同步 manifest 文件列表与 SHA-256 清单。

- [x] **Step 1: 先扩展失败的包合同**

将精确相对路径加入静态合同与 PowerShell 构建测试：

```text
安装教程.html
images/install/install-01-exit-hdt.png
images/install/install-02-open-plugins-folder.png
images/install/install-03-copy-bobcoach-dll.png
images/install/install-04-enable-bobcoach.png
```

构建测试必须递归枚举相对文件路径，验证 ZIP entry、manifest 和 `SHA256SUMS.txt` 精确一致，并验证 HTML 的本地图片在解压目录中全部存在。

- [x] **Step 2: 运行包合同并确认 RED**

Run: `node tests/test_release_package_contract.js`

Expected: FAIL，原因是 builder 和 installer 的 `$PackageFiles` 尚未包含新教程资源。

- [x] **Step 3: 最小实现精确相对路径打包**

在 builder 中为每个白名单相对路径显式创建父目录并复制来源；`docs/user/INSTALL.html` 在包内重命名为 `安装教程.html`，四图保持 `images/install/` 相对结构。安装器只校验这些包文件，不把教程或图片复制进 HDT `Plugins`。

- [x] **Step 4: 更新 manifest、哈希与 ZIP entry 生成逻辑**

所有文件枚举统一使用 `/` 规范化相对路径；`SHA256SUMS.txt` 包含除其自身外的每个文件，校验器按相对路径解析并拒绝逃逸路径或额外文件。

- [x] **Step 5: 运行目标包测试并确认 GREEN**

Run: `node tests/test_release_package_contract.js`

Run: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/test_offline_package_builder.ps1`

Expected: 两者退出码均为 0，精确白名单数量为 16，SHA-256 行数为 15。

### Task 4: 完整验证、候选 ZIP 与提交

**Files:**
- Verify: repository-wide files and generated package outside Git

**Interfaces:**
- Consumes: Tasks 1-3 的教程、合同和打包逻辑。
- Produces: 新鲜构建日志、ZIP bytes/SHA-256、干净或明确列出的 Git 工作区状态。

- [x] **Step 1: 运行全部自动化测试**

Run: `npm test`

Run: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/run_behavior_tests.ps1`

Expected: 所有合同、饰品行为和离线包测试通过。

- [x] **Step 2: 新鲜 Release 构建与仓库验证**

Run: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/build/build_release.ps1 -HdtDirectory $env:BOBCOACH_HDT_DIR -OutputDirectory "$env:TEMP\bobcoach-build-install-guide" -Force`

Run: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/build/validate_repository.ps1`

Expected: Release 构建和仓库内容/敏感信息/个人路径/大文件/身份/白名单检查均退出 0。

- [x] **Step 3: 构建新离线 ZIP 并记录身份**

Run: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/release/build_offline_package.ps1 -HdtDirectory $env:BOBCOACH_HDT_DIR -OutputDirectory "$env:TEMP\bobcoach-package-install-guide" -Force`

记录 ZIP 绝对路径、bytes 与外部 `.sha256` 内容；解压核对 `安装教程.html` 和四图存在。

- [x] **Step 4: 执行发布边界审计**

运行 `docs/maintainer/BUILD.md` 和 `RELEASE.md` 规定的白名单、端点、敏感信息审计，以及 `git diff --check`；确认没有 `.env`、密钥、token、用户缓存、HDT DLL 或生成 ZIP 被纳入提交。

- [x] **Step 5: 提交已验证改动**

```powershell
git add AGENTS.md README.md README.en.md docs/user/INSTALL.md docs/user/INSTALL.html docs/user/images/install tools/release/README_OFFLINE.md tools/release/build_offline_package.ps1 tools/release/INSTALL.ps1 tests/test_install_guide_contract.js tests/test_release_package_contract.js tests/test_offline_package_builder.ps1 tests/run_contract_tests.ps1 docs/superpowers/plans/2026-07-23-beginner-friendly-illustrated-install.md
git commit -m "docs: add beginner-friendly illustrated install guide"
```

提交后重新运行 `git status --short --branch`，只在已获得明确授权时快进推送 `main`，不创建 Tag、Release 或上传 ZIP。
