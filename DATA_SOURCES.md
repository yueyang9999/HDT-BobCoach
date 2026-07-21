# Bob Coach 数据来源

> 范围：当前生产运行边界与公开分发限制。
> 本文件不构成对第三方内容的重新授权或法律意见。

## 原则

根目录 MIT `LICENSE` 只覆盖原创 Bob Coach 代码和有权许可的原创项目材料。它不重新授权游戏内容、第三方软件、商标、聚合统计或用户游戏数据。

公开仓库和离线包不包含 HDT 二进制、HearthDb DLL、原始第三方响应、卡牌图片、用户日志、回放、账号数据或从用户机器导出的游戏数据。

## 当前生产边界

| 来源 | 当前用途 | 分发与运行限制 |
|---|---|---|
| Bob Coach 贡献者 | 原创规则、插件代码、测试和文档 | 仅在贡献者有权许可的范围内适用 MIT |
| 用户已安装的 HDT、HearthDb 和 Hearthstone | 运行时宿主、内存中的卡牌/实体事实、本地 `Power.log` 与 `log.config` | 不打包、不上传；用户自行安装和使用 |
| Firestone / Zero to Heroes | 只读聚合饰品统计候选校验 | HTTPS 运行时请求；不把响应打包或再分发，失败不阻断本地基线 |
| HearthstoneJSON / HearthSim hsdata | 精确游戏 Build 的运行时事实校验 | HTTPS 运行时请求；不把响应打包或再分发，未验证数据不得进入生产评分或 UI 排序 |

Hearthstone、HDT、HearthDb、HearthstoneJSON、Firestone/Zero to Heroes 的名称、数据和商标受其各自权利与条款约束。项目不主张关联、赞助或背书。

## 维护要求

新增嵌入资源、外部来源、网络端点或保留字段前，必须记录来源、用途、许可/权限依据、是否进入 Git/DLL/ZIP，以及失败时的行为。缺少明确分发依据的第三方数据不得进入公开仓库或离线包。

详细本地处理和网络隐私边界见 `PRIVACY.md`；第三方声明见 `NOTICE`。
