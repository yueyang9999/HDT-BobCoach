# 依赖

## 构建与运行依赖

| 依赖 | 用途 | 分发规则 |
|---|---|---|
| .NET Framework 4.7.2 Developer Pack | 编译目标框架 | 维护者在本机安装，不进入仓库或离线包 |
| HDT `1.53.5.0` x64 | 插件宿主与编译引用 | 用户/维护者自行安装，不重新分发 |
| HearthDb | 用户本机 HDT 提供的卡牌事实 | 不打包 DLL |
| Newtonsoft.Json | 用户本机 HDT 提供的运行时程序集 | 不打包 DLL |
| Node.js | 无第三方依赖的合同测试驱动 | 不属于插件运行时 |

项目的 `package.json` 必须保持 `private: true`，且不得引入 npm 运行时或开发依赖。

## 外部事实来源

插件可在严格边界内读取用户本机 HDT/Hearthstone 数据，并可访问两个只读 HTTPS 来源：Firestone/Zero to Heroes 聚合统计和 HearthstoneJSON 精确 Build 事实。它们不作为发布包数据源；请求失败不能影响本地基线，未验证数据不能进入生产评分或 UI 排序。

Hearthstone、HDT、HearthDb、HearthstoneJSON 与 Firestone/Zero to Heroes 的名称、数据和权利不因本仓库 MIT 许可证而重新授权。完整来源和限制见根目录 `DATA_SOURCES.md` 与 `NOTICE`。

## 变更规则

新增或升级依赖前，必须明确其用途、版本、许可、是否进入发布包及离线行为。不得以解决构建问题为由加入必需网络依赖、复制 HDT 二进制或绕过许可审查。
