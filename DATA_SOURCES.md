# Bob Coach 数据来源登记

治理登记复核日期：2026-07-22；离线饰品规则复核日期：2026-07-22

本登记只描述已核对的技术行为和公开资料，不构成法律意见，也不代表已经取得所有第三方许可。免责声明不能替代权利人要求的授权。

## 总体原则

根目录 MIT `LICENSE` 只覆盖原创 Bob Coach 代码和贡献者有权许可的原创项目材料。MIT 不重新授权第三方软件、数据、聚合统计、游戏内容、图片、名称或商标。

公开仓库和离线包不包含 HDT 二进制、HearthDb DLL、原始第三方响应、卡牌图片、用户日志、回放、账号数据或从用户机器导出的游戏数据。任何未来运行时外部来源只允许 HTTPS、固定域名和只读 GET 请求，不发送请求体，并必须先通过本登记规定的审批。

每个新增数据源、网络端点或嵌入资源必须登记：所有者与参考、精确端点、用途、请求与发送内容、响应使用、本地缓存与保留、是否进入 Git/DLL/ZIP、许可或权限依据、署名要求、授权状态、失败行为和最后复核日期。缺少明确分发依据的第三方内容不得进入公开仓库或离线包。

## GitHub 仓库数据免责声明

GitHub 仓库中的代码、文档、规则快照、议题、PR、镜像、fork、评论和外部链接不等于官方数据、完整数据或最新数据。仓库内容可能过期、缺失、被修改或暂时不可用，也可能与用户本机的 HDT/HearthDb build 不一致；它们不自动成为 Bob Coach 的运行时输入、第三方原始响应、授权依据或任何权利人的背书。GitHub 仓库、议题、PR、镜像、fork、评论和外部链接也不自动构成数据源登记中的获批端点。仓库公开可访问只说明 GitHub 权限状态，不证明其中任何第三方数据已获授权、完整或适合当前版本。

发布包应以对应版本的 `manifest.json`、外部 ZIP SHA-256 校验文件和本登记中的版本化数据源声明为准，而不是以仓库页面、GitHub 自动生成的源码压缩包、CI 工件或第三方附件为准。Bob Coach 不会自动读取用户的 GitHub 账户、私有仓库、token、议题/PR 内容或本地历史缓存；仓库中的示例、维护者笔记和合成测试数据不会改变这一运行时边界。

当前公开版本事实（2026-07-22 核验）：GitHub 仓库为 public，默认分支为 `main`，唯一公开 Release 为 `v0.2.0-beta.1`。仓库源码的 `0.2.0-beta.2` 仅是本地 release candidate；未创建 beta.2 Release，beta.2 ZIP 和 checksum 不应被写成公开下载地址。

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

## 已装备饰品的本地规则

- **来源与输入：** HDT 已知的本机对局状态提供 `GameState.ActiveTrinkets`（精确 `CardId` 列表）；已知饰品只按精确、版本化的本地 `CardId` 规则匹配，不使用报价统计、名称匹配或模糊文本推断。
- **规则事实版本：** `hdt-1.53.5-hearthdb-2026-07-22-r4`，覆盖 17 个精确、大小写敏感的 `CardId`，依据维护者本机 HDT 1.53.5 / HearthDb build 245258 快照审计；仓库和发布包不复制该快照。新增的 `BG30_MagicItem_972` 只在当前引擎可完整表达的范围内，通过所属方受控召唤路径复制开战饰品阶段的最左随从。
- **当前精确覆盖：** `BG30_MagicItem_439`、`BG35_MagicItem_921`、`BG30_MagicItem_403`、`BG30_MagicItem_540`、`BG30_MagicItem_542`、`BG35_MagicItem_702`、`BG30_MagicItem_441`、`BG35_MagicItem_754`、`BG30_MagicItem_301`、`BG30_MagicItem_310`、`BG30_MagicItem_902`、`BG30_MagicItem_962`、`BG30_MagicItem_970`、`BG30_MagicItem_970t`、`BG32_MagicItem_360`、`BG30_MagicItem_705`（Bartend-o-Tron 的 Oilcan）、`BG30_MagicItem_972`（Karazhan Chess Set）。
- **第三阶段硬规则：** Oilcan 使酒馆升本费用减少 3，费用下限为 0。HDT 已提供的非负实时升本按钮费用视为已经包含游戏内折扣，不重复扣减；只有实时费用缺失时才对本地确定性 fallback 应用该折扣。
- **第二阶段战斗规则：** Eternal Portrait 和 Rivendare Portrait 只按精确随从 ID 处理；Holy Mallet、Training Certificate、Valorous Medallion、Greater Valorous Medallion 与 Baleful Incense 按所属方战场顺序和修改前属性确定目标。生命变化同步当前生命与最大生命。
- **处理边界：** `GameState.ActiveTrinkets` 经本地注册表解析后进入 `EffectiveGameRules`、`FeatureExtractor`、`ActionScoring` 和 `CombatSimulator`，用于确定装备后的费用、合法性、卡牌与阵容价值以及战斗效果。
- **战斗隔离：** 战斗开始效果只读取对应一方的战团与手牌快照；攻击、生命、类型和位置选择不会跨到对手上下文。
- **与报价推荐的关系：** 首发不显示饰品报价选择提示，也不让该提示抢占其他推荐；显示开关只控制渲染，不禁用已装备饰品识别和效果计算。
- **未知效果：** 未知 ID 保守忽略其效果并记录诊断，不根据名称或模糊文本改变规则、费用或评分。
- **数据与测试：** 这条本地链路不请求或使用 Firestone/Zero to Heroes 统计，不读取历史缓存；规则测试只使用合成状态和本地固定 ID。
- **是否进入 Git/DLL/ZIP：** Bob Coach 原创规则代码可按发布白名单进入 Git 和 DLL；HDT 状态、用户对局数据和诊断不进入仓库或发布 ZIP。

## Firestone / Zero to Heroes

- **登记性质：** 仅作为历史评估背景，保留 [Firestone Developer resources](https://github.com/Zero-to-Heroes/firestone/wiki/Developer-resources) 参考；不是当前运行时数据来源。
- **当前行为：** 公开版不请求、不缓存、不展示 Firestone/Zero to Heroes 饰品统计，也不使用这些统计比较饰品报价。具体端点、专用解析、定时检查和自动重试入口已从生产路径移除。
- **历史缓存：** 当前版本不读取、不迁移、不删除既有历史缓存。历史文件不进入 Git、DLL 或发布 ZIP；卸载和升级也不得隐式处理这些文件。
- **授权状态：** 当前没有已批准的 Firestone 运行时来源。免责声明和历史署名不能代替许可。
- **未来变更：** 任何外部饰品统计适配器都必须重新设计并单独审批，明确所有者、精确端点、许可、发送内容、缓存和保留策略后，才能恢复生产请求或展示。

## HearthstoneJSON / HearthSim hsdata

- **所有者与参考：** [HearthstoneJSON](https://hearthstonejson.com) 与 [HearthSim hsdata](https://github.com/HearthSim/hsdata)。这些资料说明数据来源和生成方式，但不能自动视为 Bob Coach 获得第三方游戏数据的再许可。
- **端点模板：** `https://api.hearthstonejson.com/v1/<build>/enUS/cards.json`
- **用途：** 为未来获批来源保留指定游戏 Build 卡牌事实的受限获取能力；来源无关的饰品统计校验器可使用这些事实核对候选 ID。
- **当前行为：** 生产插件没有外部饰品统计运行路径，因此当前不会为饰品统计主动请求此端点。自动化测试只使用合成数据，不请求第三方数据。
- **请求边界：** 若未来适配器经单独审批启用，仅允许 HTTPS GET、无请求体，User-Agent 为 `BobCoach/0.2 external-validation-readonly`；URL 只可包含已知的数字 Build，不发送日志、回放、账号、对手、用户档案或设备标识。
- **限制：** 仅允许 `api.hearthstonejson.com`，禁止自动重定向；响应大小和超时必须由获批适配器显式限制。
- **响应使用：** 只允许在内存中提取当前 Build 的卡牌 ID 集合进行候选校验；未验证数据不得进入生产评分或 UI 排序。
- **本地缓存与保留：** 当前不请求、不持久化或再分发原始 `cards.json`。
- **是否进入 Git/DLL/ZIP：** 原始响应不提交 Git、不嵌入 DLL、不放入发布 ZIP。
- **许可或权限依据：** 技术来源已披露；hsdata 项目代码或工具的许可不自动重新许可其中涉及的 Blizzard 游戏数据、文本、名称或商标。
- **署名要求：** 数据来源说明保留 HearthstoneJSON 与 HearthSim hsdata 名称和官方参考链接。
- **授权状态：** 事实来源已披露，第三方数据权利仍受各自权利和条款约束；每次改变使用或分发方式前必须重新复核。
- **失败行为：** 没有获批来源时保持 `SourceUnavailable`，本地识别、规则和推荐逻辑继续运行，饰品推荐显示保持默认关闭。

## HSReplay

当前不接入、不请求、不抓取、不打包、不再分发 HSReplay 数据。

列出 HSReplay 只用于明确未使用边界，不表示存在合作、许可、赞助或背书。未来如需接入，必须先单独复核适用条款、API 或数据许可、署名要求、隐私影响和分发权限，并取得所需授权后再修改代码。

## 维护与复核

变更端点、用途、发送字段、响应保留、缓存文件、发布内容或第三方条款时，必须先更新本登记和对应合同测试，再修改生产行为。发布检查必须确认原始第三方响应和用户本地缓存没有进入 Git、DLL 或 ZIP。

详细本地处理和网络隐私边界见 [PRIVACY.md](PRIVACY.md)；第三方关系和商标声明见 [NOTICE](NOTICE)。

## 离线维护审计工具

`tools/build/audit_trinket_registry.js` 和 `tools/build/generate_trinket_stats_diff_report.js` 仅用于维护者离线校验。两者没有默认数据路径，必须由调用者显式传入全部输入文件；未提供 `--output` 时只向 stdout 输出。自动化测试只能使用仓库测试代码生成的合成数据，不得把工具指向实时用户 `%APPDATA%`、历史饰品缓存或未获分发授权的第三方原始响应。

这些工具不属于插件运行时，不发起网络请求、不创建缓存，也不改变本地饰品规则、生产评分或 UI。任何真实外部快照仍须先按本登记完成来源、授权和保留策略审核，显式输入能力本身不构成数据源授权。
