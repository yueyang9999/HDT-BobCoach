# 仓库文档与数据来源治理设计

日期：2026-07-21
状态：已实施并验证
范围：`E:\HDT-BobCoach` 的公开文档结构、文档权威边界和数据来源声明

## 目标

在不移动或删除现有文件、不破坏已有链接的前提下，为普通用户、维护者和贡献者建立清晰的文档入口；同时把运行时数据来源、使用方式、权限依据和待确认风险记录为可持续维护的登记信息。

本次工作不能承诺消除全部法律风险。目标是如实披露来源和使用边界、避免错误授权声明，并把需要权利人书面确认的事项明确标记出来。

## 方案选择

采用“保留位置、增加导航、建立权威层级”的低风险方案：

- 新增统一文档目录，不搬迁、不删除现有文档。
- README 继续承担普通用户的下载和安装入口，不复制完整维护细节。
- `docs/README.md` 承担全仓库文档导航，并按受众和用途分类。
- 根目录政策文档继续作为公开政策的权威来源。
- 历史设计和实施计划可以在总目录中逐项查阅，但必须与当前操作指南分开，并标明其历史属性。
- 安装包内 README 模板只说明包内行为，不作为仓库当前状态或发布授权的权威来源。

不采用整体搬迁方案，因为它会破坏 README、Release、外部引用和历史记录中的路径。也不采用仅补目录的方案，因为现有 Release 状态和数据授权表述存在需要同步修正的冲突。

## 文档信息架构

### 普通用户

普通用户首先从根目录 `README.md` 进入，随后按需要访问：

- `docs/user/INSTALL.md`
- `docs/user/UPGRADE.md`
- `docs/user/ROLLBACK.md`
- `docs/user/UNINSTALL.md`
- `docs/user/TROUBLESHOOTING.md`
- `PRIVACY.md`
- `DATA_SOURCES.md`
- `SUPPORT.md`

README 必须保持中文优先，明确 Windows 10 和 Windows 11 使用同一安装包，同时区分“Windows 11 已实机验收”和“Windows 10 技术兼容、尚未完成专用实机验收”。

### 维护者

维护者从 `docs/README.md` 进入 `docs/maintainer/`，其中构建、依赖、架构、发布和快速更新验证各自只保留一个权威文件。README 只提供入口，不复制容易过期的命令和流程。

### 贡献者

贡献者入口包括 `CONTRIBUTING.md`、`SECURITY.md`、`AGENTS.md` 和维护者构建文档。`AGENTS.md` 继续定义仓库目录契约和工程规则，不承担用户教程职责。

### 政策与合规

根目录文件保持权威：

- `LICENSE`：仅许可项目原创代码及项目有权许可的原创材料。
- `NOTICE`：第三方关系、商标边界和发布状态。
- `DATA_SOURCES.md`：数据来源、用途、处理、权限依据和授权状态。
- `PRIVACY.md`：本地数据、网络请求和隐私边界。
- `SUPPORT.md`：支持与自愿赞助边界。

文档之间允许互相链接，但不得各自维护相互冲突的数据来源或发布状态。

### 设计与实施历史

`docs/design/` 和 `docs/superpowers/plans/` 中的文件在 `docs/README.md` 中逐项列出，并明确说明它们记录当时的设计或实施过程，不代表当前用户操作步骤。当前行为应以 README、用户文档、维护者文档和政策文件为准。

### 包内文档模板

`tools/release/README_OFFLINE.md` 是构建过程使用的包内模板。统一目录只提供一个维护者入口，并说明模板中的条件块可能同时服务预览包和正式包。旧预览措辞本轮只做权威边界说明；是否退役预览构建路径需要单独评估脚本和测试，不在本次范围内。

## 数据来源登记设计

`DATA_SOURCES.md` 改为中文数据来源登记表。每个来源至少记录：

- 来源名称、所有者和官方参考地址；
- 精确运行时端点或端点模板；
- 在 Bob Coach 中的用途；
- 请求方法、发送字段和 User-Agent；
- 实际使用的响应内容；
- 本地缓存路径、保留和回退行为；
- 是否进入 Git、DLL 或发布 ZIP；
- 已知许可或权限依据；
- 署名要求；
- 当前授权状态；
- 请求失败或数据验证失败时的行为；
- 最后复核日期。

### Firestone / Zero to Heroes

登记运行时端点：

`https://static.zerotoheroes.com/api/bgs/trinket-stats/last-patch/overview-from-hourly.gz.json`

当前用途是只读获取聚合饰品统计候选数据。插件不上传日志、回放、账号、对手或设备标识，不把原始响应提交到 Git、嵌入 DLL 或放入发布 ZIP。

Firestone 官方开发者资料要求发布成果时署名；公开应用还要求联系作者。因此：

- 文档必须署名 Firestone / Zero to Heroes，并链接官方开发者资料。
- 授权状态必须写为“公开应用许可待书面确认”，不能写成已经获准。
- 现有免责声明不能代替权利人的许可。
- 公开上线前应取得并归档书面许可；如无法取得，应停用该运行时来源，保留本地基线能力。

### HearthstoneJSON / HearthSim hsdata

登记运行时端点模板：

`https://api.hearthstonejson.com/v1/<build>/enUS/cards.json`

当前用途是校验指定游戏 Build 的卡牌事实。原始响应不提交到 Git、不嵌入 DLL、不放入发布 ZIP。HearthstoneJSON 和 hsdata 的项目说明可以证明数据来源与生成方式，但不能自动构成 Bob Coach 对游戏数据的再许可依据。

文档必须把“事实来源”和“再许可权利”分开表述，避免用本仓库 MIT License 覆盖 Blizzard、HearthSim 或其他第三方材料。

### HSReplay

保持明确的未使用声明：当前不接入、不请求、不抓取、不打包、不再分发 HSReplay 数据。未来接入前必须单独复核条款、接口权限、署名和隐私影响。

## 运行与失败边界

数据来源声明必须与代码行为一致：

- 网络访问只允许 HTTPS 和代码允许的固定域名。
- Firestone 请求上限为 5 MiB、超时 15 秒；HearthstoneJSON 请求上限为 64 MiB、超时 45 秒。
- Firestone 按既定间隔检查，失败后按代码中的退避策略重试。
- 运行时缓存位于 Bob Coach 用户数据目录，包括 `active.json`、`previous.json`、`candidate.json` 和 `health.json`。
- 未验证数据不能进入生产评分或 UI 排序。
- 外部来源失败时回退到同 Build 的已验证缓存或本地基线，不能阻止插件离线启动和基本使用。

如果实现发生变化，代码、隐私文档和数据来源登记必须在同一变更中更新。

## 冲突修正

`NOTICE` 当前关于“GitHub Release 尚未授权”的描述已经与公开的 `v0.2.0-beta.1` 冲突。实施时改为记录当前 Release 已存在，并重申未来每个 Release、上传或公开发布仍需项目所有者单独明确授权。

旧预览包模板中的限制只适用于预览条件块，不能用来描述当前公开 Release。当前发布状态以 GitHub Release、根 README、`NOTICE` 和维护者发布文档为准。

## 仓库规则更新

更新 `AGENTS.md` 的目录契约：

- `docs/README.md` 是统一文档入口。
- `docs/superpowers/specs/` 存放日期命名的已确认设计规格。
- `docs/superpowers/plans/` 存放对应的日期命名实施计划。

不新增其他顶层目录，不改变现有构建产物和验收证据的存放规则。

## 验证设计

实施后进行以下验证，不运行或清理 VM：

1. 检查所有受版本控制的 Markdown 相对链接均可解析。
2. 检查 README、`docs/README.md`、`NOTICE`、`DATA_SOURCES.md`、`PRIVACY.md` 和 Release 文档之间没有发布状态或数据来源冲突。
3. 检查数据来源登记中的端点、超时、响应上限、缓存文件和失败行为与生产代码一致。
4. 运行 `npm test`。
5. 按 `docs/maintainer/BUILD.md` 运行 `validate_repository.ps1`。
6. 运行 `git diff --check`。
7. 复核 Git diff，确认没有删除文件、用户数据、验收证据、CI 配置或发布资产。

## 明确不在本次范围内

- 不修改插件运行逻辑或网络请求行为。
- 不修改 CI/CD 配置。
- 不删除或移动任何文件。
- 不清理 VM、验收证据、安装包或用户数据。
- 不使用 `-RemoveUserData`。
- 不提交、push、合并 PR 或创建 GitHub Release，除非项目所有者另行明确授权。
- 不宣称项目已经消除全部法律风险，也不把本文档作为法律意见。

## 完成标准

- 新用户能从 README 明确找到正确下载和中文安装教程。
- 任一仓库文档能从 `docs/README.md` 按受众或用途找到。
- 当前指南与历史记录、包内模板之间的权威关系清晰。
- 每个实际或明确未使用的数据来源都有可审计的状态记录。
- Firestone 的公开应用许可待确认事项醒目、真实且可执行。
- 文档和代码事实一致，仓库验证通过，且未触碰本次范围外内容。
