# Beta 2 发布事实一致性实施计划

> 按 `writing-plans` 形成；实施遵循测试先行和验证后再提交。

**目标：** 让公开文档、源码版本、本地候选包和 GitHub 实际公开状态一致，消除 beta.2 虚构下载链接与错误验收声明。

**约束：** 不创建 PR、Tag 或 GitHub Release；不上传 beta.2 资产；不读取用户缓存；不使用 `-RemoveUserData`；不恢复 Firestone/Zero to Heroes 请求、缓存、专用解析或自动重试；不修改密钥或 CI/CD。

## 1. 固化发布事实合同

- 修改 `tests/test_public_documentation_contract.js`：要求 beta.1 公开链接、beta.2 候选声明、禁止 beta.2 Release URL、要求 beta.2 Windows smoke 待完成。
- 运行该测试并确认在旧文档上失败，记录失败原因来自公开状态不一致。

## 2. 同步公开文档

- 更新中英文 README 和安装指南，区分“当前公开版本 beta.1”与“源码/本地候选 beta.2”。
- 更新 `DATA_SOURCES.md`、`PRIVACY.md`、`NOTICE` 的 GitHub 数据免责声明和发布事实。
- 更新架构、依赖、更新验证与离线包说明，删除虚构 beta.2 下载路径和已完成 smoke 声明。
- 运行文档合同测试至通过。

## 3. 重建本地候选包

- 使用 Release 构建脚本，以隔离 HDT 1.53.5.7354 基线构建 DLL。
- 使用离线包脚本在仓库外生成 beta.2 ZIP。
- 验证 manifest、文件白名单、哈希、生命周期和 CurrentSeasonPreview DLL identity。
- 记录 ZIP/DLL bytes、SHA-256 和源提交。

## 4. 完整验证

- 设置 `BOBCOACH_HDT_DIR` 为隔离基线后 fresh 运行 `npm test`。
- 单独 fresh 运行饰品行为测试和 Release 构建。
- 运行 `tools/build/validate_repository.ps1`。
- 运行发布白名单、端点、敏感信息审计和 `git diff --check`。
- 失败时先定位根因；不注释测试，不加绕过标记。

## 5. GitHub 公开可访问性核验

- 用 GitHub API 核验仓库公开状态、默认分支和 Release 列表。
- 用匿名 HTTP 核验仓库主页、默认分支 README、beta.1 Release 页面、ZIP 和 checksum 均可访问。
- 反向核验 beta.2 Release/资产仍不存在，确保文档没有指向它们。

## 6. 提交与同步

- 审查完整 diff、版本字段和文档链接。
- 创建范围明确的提交。
- 在已有明确授权范围内普通 push 当前分支；禁止 force push。
- 不把未完成最终人工 smoke 的 beta.2 描述成正式或已公开版本。
