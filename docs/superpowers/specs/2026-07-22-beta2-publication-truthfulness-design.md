# Beta 2 发布事实一致性设计

日期：2026-07-22

## 问题

仓库代码版本已经推进到 `0.2.0-beta.2` 候选状态，但 GitHub 当前唯一公开 Release 是 `v0.2.0-beta.1`。部分公开文档把 beta.2 写成当前公开版本，并给出不存在的 beta.2 Release 下载地址。这会让匿名用户遇到 404，也会把尚未完成最终 Windows 实机 smoke 的本地候选包误表述为已发布软件。

## 已核实的公开事实

- 仓库 `yueyang9999/HDT-BobCoach` 为公开仓库，GitHub API 返回 `private=false`、`visibility=public`。
- 默认分支为 `main`。
- 当前公开 Release 只有 `v0.2.0-beta.1`，其 ZIP 与 `.sha256` 资产可匿名访问。
- `v0.2.0-beta.2` Release 和 `BobCoach-0.2.0-beta.2-win-x64.zip` 公开资产不存在。
- 本地 beta.2 ZIP 是 release candidate，不是 GitHub Release 资产。

## 状态模型

公开文档同时表达两个不同状态：

1. `v0.2.0-beta.1`：当前最新公开版本，下载、校验和安装链接必须指向真实存在的 GitHub Release 资产。
2. `0.2.0-beta.2`：当前源码和本地候选包版本，尚未公开发布；不得提供虚构的 Release URL，不得声称已完成最终 Windows 11 24H2 实机 smoke。

候选包通过自动化测试、Release 构建和离线生命周期验证，只能证明候选产物可进入人工 smoke，不能替代真实 HDT/炉石环境的最终验收。

## 文档合同

`tests/test_public_documentation_contract.js` 必须检查：

- README 和安装指南把 beta.1 标为当前公开下载版本，并包含真实 beta.1 ZIP URL。
- 公开文档明确 beta.2 仅为本地 release candidate，尚未公开发布。
- 公开文档不得包含 beta.2 GitHub Release 下载 URL。
- Windows 11 24H2 对 beta.2 的最终实机 smoke 状态必须是待完成，不能写成已完成。
- `NOTICE` 同时承认 beta.1 已公开和 beta.2 未公开，未来公开发布仍需所有者逐次明确授权。
- GitHub 仓库内容免责声明继续覆盖代码、文档、规则快照、Issue、PR、镜像、fork、评论与外链，并明确这些内容不等于第三方官方、完整或最新数据。

## 同步范围

- 用户入口：`README.md`、`README.en.md`、`docs/user/INSTALL.md`。
- 数据与法律边界：`DATA_SOURCES.md`、`PRIVACY.md`、`NOTICE`。
- 维护者文档：`docs/maintainer/ARCHITECTURE.md`、`DEPENDENCIES.md`、`UPDATE_VALIDATION.md`。
- 候选包内说明：`tools/release/README_OFFLINE.md`。
- 文档合同测试：`tests/test_public_documentation_contract.js`。

不修改运行时端点、缓存、解析器或自动重试；不读取用户历史缓存；不修改 `.env`、密钥、token 或 CI/CD。

## 产物与验证

beta.2 候选包重新构建到仓库外的版本化目录，记录 ZIP 和 DLL 的 bytes、SHA-256、源提交。验证包括：

- 文档合同先红后绿。
- `npm test`（显式使用隔离的 HDT 基线）。
- 饰品行为测试和 Release 构建。
- `validate_repository.ps1`。
- 离线包构建及生命周期/内容验证。
- 发布白名单、网络端点和敏感信息审计。
- `git diff --check`。
- GitHub API 与匿名 HTTP 对仓库、README、Release 和资产的访问核验。

## 发布边界与残余风险

本轮不创建 PR、Tag、GitHub Release，不公开上传 beta.2 ZIP。即使全部自动化验证通过，beta.2 仍保留以下发布阻断项：真实 Windows 11 24H2 + HDT 1.53.5 + 炉石酒馆战棋的最终人工 smoke，以及所有者对具体 Release/Tag/资产上传操作的单独授权。
