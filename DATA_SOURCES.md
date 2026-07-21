# Bob Coach 数据来源登记

最后复核日期：2026-07-21

本登记只描述已核对的技术行为和公开资料，不构成法律意见，也不代表已经取得所有第三方许可。免责声明不能替代权利人要求的授权。

## 总体原则

根目录 MIT `LICENSE` 只覆盖原创 Bob Coach 代码和贡献者有权许可的原创项目材料。MIT 不重新授权第三方软件、数据、聚合统计、游戏内容、图片、名称或商标。

公开仓库和离线包不包含 HDT 二进制、HearthDb DLL、原始第三方响应、卡牌图片、用户日志、回放、账号数据或从用户机器导出的游戏数据。运行时外部来源只允许 HTTPS、固定域名和只读 GET 请求，不发送请求体。

每个新增数据源、网络端点或嵌入资源必须登记：所有者与参考、精确端点、用途、请求与发送内容、响应使用、本地缓存与保留、是否进入 Git/DLL/ZIP、许可或权限依据、署名要求、授权状态、失败行为和最后复核日期。缺少明确分发依据的第三方内容不得进入公开仓库或离线包。

## Bob Coach 原创材料

- **所有者与参考：** 各贡献者；适用范围见根目录 `LICENSE` 和提交历史。
- **用途：** 原创插件代码、规则、测试、脚本和文档。
- **是否进入 Git/DLL/ZIP：** 按仓库白名单和发布清单进入相应产物。
- **许可或权限依据：** 仅在贡献者有权许可的范围内适用 MIT。
- **授权状态：** 已按根目录 MIT License 公开；不覆盖本文登记的任何第三方材料。

## 用户自行安装的 HDT、HearthDb 与 Hearthstone

- **所有者与参考：** Hearthstone 属于 Blizzard Entertainment；HDT、HearthDb 及相关组件属于各自权利人。插件开发参考 [HDT Creating Plugins](https://github.com/HearthSim/Hearthstone-Deck-Tracker/wiki/Creating-Plugins)。
- **用途：** HDT 作为运行时宿主；插件读取 HDT 已知的内存状态以及用户本机的 `Power.log`、`log.config`。
- **请求与发送内容：** Bob Coach 不向这些项目上传日志、回放、账号、对手、用户档案或设备标识。
- **是否进入 Git/DLL/ZIP：** HDT、HearthDb 和 Hearthstone 二进制均不进入仓库或发布 ZIP；由用户自行合法安装。
- **许可或权限依据：** 各组件和游戏内容继续受各自许可、使用条款和权利约束。
- **授权状态：** 仅说明兼容性和运行时关系，不主张获得第三方商标、游戏内容或发行授权。

## Firestone / Zero to Heroes

- **所有者与参考：** Firestone / Zero to Heroes；[Firestone Developer resources](https://github.com/Zero-to-Heroes/firestone/wiki/Developer-resources)。公开资料要求发布成果时署名，并要求公开应用联系作者。
- **精确端点：** `https://static.zerotoheroes.com/api/bgs/trinket-stats/last-patch/overview-from-hourly.gz.json`
- **用途：** 只读获取上个补丁的聚合饰品统计，作为候选数据进行当前 Build 校验。
- **请求与发送内容：** HTTPS GET，无请求体；User-Agent 为 `BobCoach/0.2 trinket-stats-readonly`。不发送 `Power.log`、回放、账号、对手、用户档案或设备标识。
- **限制：** 仅允许 `static.zerotoheroes.com`；禁止自动重定向；响应上限 5 MiB，超时 15 秒；常规检查间隔 6 小时，失败后依次等待 15 分钟、1 小时、6 小时。
- **响应使用：** 解析聚合饰品统计并与当前 Build 的卡牌事实交叉验证。未验证候选不得进入生产评分或 UI 排序。
- **本地缓存与保留：** 验证结果写入 `%APPDATA%\bob-coach\data\trinket-stats\` 下的 `active.json`、`previous.json`、`candidate.json` 和 `health.json`；只激活已验证快照，并保留前一已验证快照用于回退。
- **是否进入 Git/DLL/ZIP：** 原始响应和用户本地缓存均不提交 Git、不嵌入 DLL、不放入发布 ZIP。
- **署名要求：** 公开文档必须明确署名 Firestone / Zero to Heroes，并保留官方开发者资料链接。
- **授权状态：** **公开应用许可待书面确认**。现有免责声明不能代替许可；后续公开发布前应取得并归档权利人的书面确认。无法取得时，应停用该运行时来源并保留本地基线能力。
- **失败行为：** 网络、格式或验证失败不阻断本地推荐；使用同 Build 的已验证缓存，否则回退本地基线。

## HearthstoneJSON / HearthSim hsdata

- **所有者与参考：** [HearthstoneJSON](https://hearthstonejson.com) 与 [HearthSim hsdata](https://github.com/HearthSim/hsdata)。这些资料说明数据来源和生成方式，但不能自动视为 Bob Coach 获得第三方游戏数据的再许可。
- **端点模板：** `https://api.hearthstonejson.com/v1/<build>/enUS/cards.json`
- **用途：** 获取指定游戏 Build 的英文卡牌事实，仅用于校验 Firestone 候选中的饰品 ID 是否属于当前 Build。
- **请求与发送内容：** HTTPS GET，无请求体；User-Agent 为 `BobCoach/0.2 trinket-stats-readonly`。URL 只包含 HDT 已知的数字 Build，不发送日志、回放、账号、对手、用户档案或设备标识。
- **限制：** 仅允许 `api.hearthstonejson.com`；禁止自动重定向；响应上限 64 MiB，超时 45 秒。
- **响应使用：** 只在内存中提取当前 Build 的卡牌 ID 集合，用于候选校验；未验证数据不得进入生产评分或 UI 排序。
- **本地缓存与保留：** 卡牌事实保留在当前插件进程内；经两来源校验后的统计快照按上一节所列文件写入本地缓存，不单独持久化或再分发原始 `cards.json`。
- **是否进入 Git/DLL/ZIP：** 原始响应不提交 Git、不嵌入 DLL、不放入发布 ZIP。
- **许可或权限依据：** 技术来源已披露；hsdata 项目代码或工具的许可不自动重新许可其中涉及的 Blizzard 游戏数据、文本、名称或商标。
- **署名要求：** 数据来源说明保留 HearthstoneJSON 与 HearthSim hsdata 名称和官方参考链接。
- **授权状态：** 事实来源已披露，第三方数据权利仍受各自权利和条款约束；每次改变使用或分发方式前必须重新复核。
- **失败行为：** 无法取得或校验当前 Build 事实时，不激活新候选；使用同 Build 的已验证缓存，否则回退本地基线。

## HSReplay

当前不接入、不请求、不抓取、不打包、不再分发 HSReplay 数据。

列出 HSReplay 只用于明确未使用边界，不表示存在合作、许可、赞助或背书。未来如需接入，必须先单独复核适用条款、API 或数据许可、署名要求、隐私影响和分发权限，并取得所需授权后再修改代码。

## 维护与复核

变更端点、用途、发送字段、响应保留、缓存文件、发布内容或第三方条款时，必须先更新本登记和对应合同测试，再修改生产行为。发布检查必须确认原始第三方响应和用户本地缓存没有进入 Git、DLL 或 ZIP。

详细本地处理和网络隐私边界见 [PRIVACY.md](PRIVACY.md)；第三方关系和商标声明见 [NOTICE](NOTICE)。
