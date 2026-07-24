# HDT-BobCoach 文档目录

本页是仓库文档的统一入口。普通用户应优先阅读安装、升级和故障排查文档；`docs/design/` 与 `docs/superpowers/` 记录历史设计和实施过程，不替代当前操作指南。

当前信息的权威顺序如下：用户操作以根目录 `README.md` 和 `docs/user/` 为准；构建与发布以 `docs/maintainer/` 为准；许可、数据来源、隐私与第三方权利以根目录政策文件为准。历史文档或包内模板与这些文件冲突时，以当前权威文件为准。

## 普通用户

- [中文项目说明与下载入口](../README.md)
- [English README](../README.en.md)
- [中文安装教程](user/INSTALL.md)
- [功能展示](user/FEATURES.md)：查看购买、升本、技能、饰品和发现提示的实战画面
- [功能展示离线 HTML](user/FEATURES.html)
- [升级教程](user/UPGRADE.md)
- [回退教程](user/ROLLBACK.md)
- [卸载教程](user/UNINSTALL.md)
- [故障排查](user/TROUBLESHOOTING.md)
- [版本变更记录](../CHANGELOG.md)

## 维护者

- [架构说明](maintainer/ARCHITECTURE.md)
- [构建说明](maintainer/BUILD.md)
- [依赖说明](maintainer/DEPENDENCIES.md)
- [发布流程](maintainer/RELEASE.md)
- [快速更新验证流程](maintainer/UPDATE_VALIDATION.md)
- [迁移工具说明](../tools/migrate/README.md)

## 贡献者

- [贡献指南](../CONTRIBUTING.md)
- [行为准则](../CODE_OF_CONDUCT.md)
- [安全问题报告](../SECURITY.md)
- [仓库工程规则](../AGENTS.md)
- [Pull Request 模板](../.github/pull_request_template.md)

## 政策与合规

- [MIT License](../LICENSE)
- [第三方权利与商标声明](../NOTICE)
- [数据来源登记](../DATA_SOURCES.md)
- [隐私边界](../PRIVACY.md)
- [支持、问题反馈与自愿赞赏边界](../SUPPORT.md)
- [安全政策](../SECURITY.md)

## 设计与实施历史

以下文件记录当时的设计判断或实施步骤，便于审计和追溯；它们不是当前用户操作或发布授权依据。

- [公开产品仓库治理设计（2026-07-20）](design/公开产品仓库治理设计_2026-07-20.md)
- [双语自述与第三方声明设计（2026-07-21）](design/双语自述与第三方声明设计_2026-07-21.md)
- [仓库文档与数据来源治理设计规格（2026-07-21）](superpowers/specs/2026-07-21-repository-documentation-and-data-source-governance-design.md)
- [HDT-BobCoach 公开仓库实施计划（2026-07-20）](superpowers/plans/2026-07-20-hdt-bobcoach-public-repository.md)
- [双语 README 实施计划（2026-07-21）](superpowers/plans/2026-07-21-bilingual-readme.md)
- [中文下载指引实施计划（2026-07-21）](superpowers/plans/2026-07-21-chinese-download-guidance.md)
- [仓库文档与数据来源治理实施计划（2026-07-21）](superpowers/plans/2026-07-21-repository-documentation-and-data-source-governance.md)

## 包内文档模板

- [离线安装包 README 模板](../tools/release/README_OFFLINE.md)：由构建脚本按预览包或正式包条件生成，只说明包内操作，不代表仓库当前发布状态，也不构成发布授权。
