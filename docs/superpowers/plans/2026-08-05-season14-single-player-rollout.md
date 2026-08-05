# BobCoach Season14 单人模式推进执行计划

> 状态：2026-08-05；T0-T9 完成，Season14 规则/攻略/归档已完成；本地可调用抓取/半自动校验工具已落地，2 个别名已确认并已 apply；T10/T11 本地部分已实施（refresh wrapper + CompGuideCatalog + SeasonDataSelector + selector 运行时接入），CI/CD、插件网络抓取、发布未实施
> 分支：`codex/season14-algorithm-iteration`
> HEAD：`9380a77`
> 数据根：`E:\HDT-BobCoach-evidence\season14\`
> 交接文档：`G:\software\Codex\Handover document\BobCoach_Season14_单人模式_T4_2026-08-05.md`

## 数据基线

- `season=season14`
- `gameBuild=36.2.248348`
- `cardPoolIdentity=0bcc979ae21df891`
- 审计快照：`E:\HDT-BobCoach-evidence\season14\generated\audit_season_snapshot.36.2.248348.v2.json`
- 审计 sha256：`3fdb5a3e59594115e1e0673af72d109225b2646a9c7322f26d34b5ddf513348b`
- 最新原始数据：`raw/cards.enUS.latest.json`、`raw/cards.zhCN.latest.json`
- 旧赛季原始数据：`raw/cards.enUS.35.6.2.244584.json`、`raw/cards.zhCN.35.6.2.244584.json`

## 任务拆分

| 任务 | 内容 | 交付物 | 完成条件 |
| --- | --- | --- | --- |
| T0 [x] | 冻结当前状态，写执行计划和 status | 本文档 | 固定数据基线；不改代码 |
| T1 [x] | 移除卡牌归档 | `E:\HDT-BobCoach-evidence\season14\archive\removed_cards\manifest.json` | 只归档不删除；minion 移除 97、spell 移除 6 与审计一致 |
| T2 [x] | 新机制注册表生成 | `generated\registries\trinket_registry.json`、`dark_gift_registry.json`、`activate_registry.json`、`lockbox_registry.json`、`fishbait_registry.json` | 全部带契约头，计数可复现；无 `BGDUO` |
| T3 [x] | 卡牌注册表生成 | `generated\registries\minion_registry.json`、`hero_registry.json`、`tavern_spell_registry.json` | 数据源为最新快照，无 `BGDUO`，字段与契约一致 |
| T4 [x] | C# 注册表/FactSource 层 | 新增 5 个机制 Registry 和对应 FactSource；更新 `.csproj` | 定向编译与加载测试通过 |
| T5 [x] | 规则评估器与旧机制下线 | Activate/Lockbox/Fishbait/Dark Gifts 规则接入；Legacy Anomaly 不再作为当前激活机制；更新 Trinket 生命周期 | 定向机制测试通过 |
| T6 [x] | 测试环境数据切换 | 用 season14 临时 `BOB_COACH_DATA_ROOT` 更新 harness 和测试 | 测试覆盖新机制，不碰 APPDATA，无 BGDUO |
| T7 [x] | 全量验证与交接 | `npm test`、`validate_repository.ps1`、`git diff --check` 全绿；更新 8/5 交接文档 | 交接文档写明当前完成度、下一步命令、遗留风险 |
| T8 [x] | 更新卡牌与规则规则化、补充流派攻略、旧数据归档、season selector | `season14_rule_catalog.json`、`generated/registries/season14_comp_guides.json`、`archive/guides/`、`archive/raw/35.6.2.244584/`、`E:\HDT-BobCoach-evidence\season_selector.json` | 规则目录、流派攻略、关键词注册表校验通过；旧 raw/攻略归档不覆盖；selector 可校验和切换当前/旧赛季 |
| T9 [x] | 本地攻略抓取/半自动校验工具 | `E:\HDT-BobCoach-evidence\season14\tools\fetch_season14_comp_guides.js`、`tests\test_season14_guide_fetch.js` | 测试通过；现有 23 条源 dry-run 校验 207 项并阻断 2 个待确认别名 |

依赖顺序：`T0 -> T1 -> T2/T3 -> T4 -> T5 -> T6 -> T7 -> T8 -> T9`。T2 和 T3 必须作为单独步骤执行，不合并。

## 本会话范围

- 完成：T0-T9；Season14 规则化、攻略数据修正、旧数据归档、selector、本地攻略抓取/半自动校验工具已完成；selector 已接入插件侧只读 `CompGuideCatalog`
- T7 全量验证：设置 `BOBCOACH_HDT_DIR` 后 `npm test` 全绿，仓库/校验工具全部通过
- T8 规则化与归档：生成 season14 规则目录与关键词注册表，补充 23 条流派攻略，归档旧 raw/旧攻略，提供 season selector
- T9 本地抓取/半自动校验：可消费本地 `season14_guide_source.json` 或 hsreplay URL；dry-run 只写 `fetched_guides/`，`--apply` 仅在名称校验全绿时写正式 registry 和权重参考
- 调研：自动收集流派攻略的数据源与可复用工具（见下文）；T10/T11 本地部分已实施，selector 已由插件侧只读层直接读取，CI/CD、插件网络抓取、发布仍需用户确认

## T7/T8 完成证据

全量验证前必须设置 HDT 基线环境变量，否则 `npm test` 会因找不到 HDT 目录失败：

```powershell
$env:BOBCOACH_HDT_DIR='E:\HDT-BobCoach-evidence\dependencies\HDT-1.54.0\build-reference\Hearthstone Deck Tracker'
npm test
tools/build/validate_repository.ps1
git diff --check
```

本轮结果：

- `npm test`：`EXIT=0`；日志 `E:\HDT-BobCoach-evidence\season14\generated\t7_npm_test_2026-08-05_final.log`
- `tools/build/validate_repository.ps1`：`EXIT=0`，`PASS repository validation: 472 ...`
- `git diff --check`：`EXIT=0`，仅换行警告
- `tests/test_season14_registries.ps1`：`PASS season14 registries assertions=173`
- `tools/validate_season14_registries.js`：`ALL_PASS=true`
- `tools/validate_season14_rule_catalog.js`：`ALL_PASS=true failures=0`
- `tools/validate_season14_guides.js`：`ALL_PASS=true`，`season14_comp_guides` guides=23、`keyword_registry` keywords=2
- 规则化修正：烈毒按最新文本注册，cleave 改为主/相邻目标批量结算且烈毒只消耗一次
- `tools/archive_season_data.js`：`EXIT=0`，files=2，bytes=19415604，manifest sha256=`e4dfd5d95151dcd8c1b1ebc3d47f3855e079d3229c6630b31ad710a3115af4a7`
- `tools/select_season_data.js list/verify`：默认 `season14`，两个 season 均 `ok=true`
- 流派攻略：`season14_guide_source.json` -> `generated/registries/season14_comp_guides.json`（guides=23）
- 旧攻略归档：`archive/guides/manifest.json`（files=3，bytes=16888290）
- 关键词注册表：`generated/registries/keyword_registry.json`（keywords=2）
- 攻略数据口径：最新 Excel 已同步；两张相同随从不是重复，生成器不去重，`coreCards` 固定 6 个槽位并原样保留重复项，当前以 2 张为核心数量参考；例如海盗钩牙宝箱 2 张“上进的逃兵”、机械火花机 2 张“偏折机器人”、机械星元机 2 张“坎格尔的学徒”、元素雷暴 2 张“无缰雷暴”、元素小粉 2 张“狂放的法力涌流”
- 机制启用确认：发动机制（Activate）与黑暗之赐（Dark Gift）在测试环境已启用；T5/T6 harness 覆盖真实 lookup、Dark Gift offering tier turn 3-10 边界、`BG36_Anomaly_002` 排除
- 关键词最新文本：`VENOMOUS`（烈毒）= 每场战斗首次造成伤害时消灭目标；命中圣盾会消耗烈毒。；`POISONOUS`（剧毒）= 造成伤害即消灭。

## 本地抓取/半自动校验工具（T9）

工具：`E:\HDT-BobCoach-evidence\season14\tools\fetch_season14_comp_guides.js`

本地 dry-run（不会写正式 registry）：

```powershell
node "E:\HDT-BobCoach-evidence\season14\tools\fetch_season14_comp_guides.js" `
  --local "E:\HDT-BobCoach-evidence\season14\generated\season14_guide_source.json" `
  --source-name local-season14-guide-source
```

远程抓取（校验全绿才允许 apply）：

```powershell
node "E:\HDT-BobCoach-evidence\season14\tools\fetch_season14_comp_guides.js" `
  --source "https://hsreplay.net/api/v1/battlegrounds/comp_guides/" `
  --apply
```

规则：

- 支持 `guides` 数组、纯数组、现有 `sheets` 攻略源三种输入。
- 默认只写 `E:\HDT-BobCoach-evidence\season14\generated\fetched_guides\`：snapshot、draft、validation report、aliases、fetch report。
- `rawCount=0` 或 `guides=0` 时阻断，原因 `empty_result`。
- 存在 `pendingAlias`、`ambiguous`、`unmatched` 或 `matched + applied !== totalEntries` 时阻断，原因 `name_validation`；即使带 `--apply` 也不写正式 registry。
- 只有校验全绿且带 `--apply` 时，才写 `generated/registries/season14_comp_guides.json` 并串联 `season14_guide_weight_reference.json`。
- 确认别名从 alias JSON 的 `confirmed` 读取；`pending` 不自动生效。

当前真实源校验/apply 结果：

- rawCount=23，guides=23
- totalEntries=207，matched=205，pendingAlias=0，ambiguous=0，unmatched=0，applied=2
- 已确认别名：
  - `回响标签` -> `回收标签` / `BG32_MagicItem_888`
  - `热球手肖像` -> `投球手肖像` / `BG35_MagicItem_861`

测试：`node tests\test_season14_guide_fetch.js`，已加入 `tests\run_behavior_tests.ps1`。

规则目录 `E:\HDT-BobCoach-evidence\season14\generated\season14_rule_catalog.json`：

- sha256：`825f3eded1923246c38ff3501769f97347bb919be0544ca73e403f9263e62f22`
- mechanisms=5，rules=6，mechanismCards=532
- cardChanges：minions=343 / spells=81 / trinkets=379
- minionsByChange：retained=141 / new=94 / removed=97 / changed=11
- spellsByChange：retained=65 / removed=6 / changed=1 / new=9
- trinketsByChange：retained=332 / new=47
- officialUpdates：minions=182 / spells=20 / trinkets=271 / excludedDuosRows=1 / unresolvedOfficialRows=10

旧 raw 归档 `E:\HDT-BobCoach-evidence\season14\archive\raw\35.6.2.244584\`：

- `cards.enUS.35.6.2.244584.json`
- `cards.zhCN.35.6.2.244584.json`
- `manifest.json`

season selector `E:\HDT-BobCoach-evidence\season_selector.json` 当前默认 `season14`，旧数据标识 `legacy_35_6_2_244584`。快速切换命令：

```powershell
node "E:\HDT-BobCoach-evidence\tools\select_season_data.js" set legacy_35_6_2_244584
node "E:\HDT-BobCoach-evidence\tools\select_season_data.js" set season14
```

注意：selector 已接入插件侧只读 `CompGuideCatalog`，数据根按 `BOB_COACH_SEASON_SELECTOR`、显式数据根父级、evidence 默认顺序解析；插件不写 selector 状态。切换旧赛季后必须重新运行注册表/行为测试回归。

## 自动收集流派攻略调研（本地工具已落地，T10/T11 本地部分已实施）

结论：本地抓取/半自动校验已可调用；T10/T11 本地部分已实施，全自动流程仍需要 CI/CD 或插件调度方案，不直接覆盖当前人工复核环节。

主源候选：

- HearthSim/hsreplay 免费接口 `GET https://hsreplay.net/api/v1/battlegrounds/comp_guides/`，字段包含 `name`、`how_to_play`、`when_to_commit`、`common_enablers`、`summary`
- HDT 客户端已有免费入口 `ApiWrapper.GetCompsGuides(gameLanguage)`；Premium 入口为 `GetPremiumCompsGuides(token, gameLanguage, races)`
- HSTracker 使用的接口 `https://hsreplay.net/api/v1/battlegrounds/first_place_comps/`，字段包含 `id`、`name`、`popularity`、`key_minions_top3`、`avg_final_placement`、`first_place_comps_lobby_races`
- HearthSim i18n 仓库已有 `extract_comp_guides.py`，且 `zh_Hans` 翻译文件存在

不推荐依赖 Tier7 私有接口：需要客户端 token，实测 401，不适合长期数据源。

落地建议：

1. 定时抓取免费接口 -> 规范化 -> 生成 registry -> 校验 -> 生成人工复审 PR，不直接覆盖当前数据；本地已具备除定时/PR 调度外的核心链路。
2. 免费 zhCN 接口可能返回 `[]`，必须做空结果兜底，并保留人工校对入口。
3. GitHub Actions schedule + manual dispatch 可作为后续全自动方案，但涉及 CI/CD，需用户确认后再实施。
4. 候选参考仓库：`HearthSim/Hearthstone-Deck-Tracker`、`HearthSim/HSTracker`、`LearnDude/hsdt_plus`；其余候选偏数据统计或榜单，不是现成流派攻略主源。

## T10/T11 落地状态与后续规划

目标：把当前本地半自动链路升级为“可自动抓取 + 可校验 + 可人工兜底”的流程，并在插件内提供攻略数据刷新与算法权重校准入口。本轮已完成本地 refresh wrapper 和插件侧只读攻略目录；CI/CD、插件网络请求、发布策略仍需用户确认后再实施。

### 本轮已完成（本地落地）

- `E:\HDT-BobCoach\tools\refresh\season14_guide_refresh.js`：复用 `fetch_season14_comp_guides.js`，支持 `--local` / `--source`；默认 dry-run，显式 `--apply` 才写正式 registry 和权重参考；递归拦截 BGDUO；空结果、名称校验失败、BGDUO 均 fail closed 并写 refresh report。
- `E:\HDT-BobCoach\tests\test_season14_guide_refresh.js`：覆盖 dry-run、apply、empty_result、name_validation、duo_forbidden，已加入 `tests\run_behavior_tests.ps1`。
- `src\BobCoach\Core\CompGuideCatalog.cs`：只读加载 `generated\registries\season14_comp_guides.json` 与 `generated\season14_guide_weight_reference.json`；数据根经 `SeasonDataSelector.ResolveConfiguredRoot()` 从 `BOB_COACH_SEASON_SELECTOR`、显式 `BOB_COACH_DATA_ROOT` 父级 `season_selector.json` 或默认 evidence selector 解析；缺文件、坏 JSON、空产物、BGDUO 均 fail closed；不 fallback APPDATA；真实数据为 guides=23、coreEntries=138、trinketEntries=69。
- `src\BobCoach\Core\SeasonDataSelector.cs`：插件侧只读解析当前 season，只设置只读属性、不写 selector 状态；缺文件、坏 JSON、BGDUO、未知档均 fail closed。
- `tests\test_release_build_contract.js`：`CompGuideCatalog.cs` 与 `SeasonDataSelector.cs` 已纳入 release 编译集合，compile=212，digest=`f84d15ed944357a5a5170a6661928eb0de9c2f18a0971149164c680551d521a1`。

### T10 全自动抓取流水线（未实施）

1. 固定主源为 `GET https://hsreplay.net/api/v1/battlegrounds/comp_guides/`，复用现有 `fetch_season14_comp_guides.js` 的规范化、名称校验、dry-run、`--apply` 逻辑。
2. 新增定时任务：
   - 定时抓取 -> 规范化 -> 生成 draft registry 与校验报告 -> 不再直接覆盖正式 registry。
   - 校验全绿时生成“人工复审 PR”，由人在 GitHub 确认后合并。
   - 校验未全绿、接口返回空、或出现新 pending 别名时生成告警摘要，不写正式数据。
3. 半自动校验保留为人工兜底：新攻略来源名称与官方名称的差异进入 `guide_name_aliases.json`，只有人工确认后才能通过 `name_validation`。
4. 空结果兜底：免费 zhCN 接口可能返回 `[]`，视为“无更新”，保留上一份正式数据并记录日志。
5. 自动流程只输出路径、条数、校验摘要、sha256 和错误摘要；完整 JSON 留在 evidence/CI artifact，不进对话上下文。

### T11 插件内攻略抓取与算法权重校准（部分落地）

1. 未实施：插件侧新增“攻略数据刷新”入口，优先复用 HDT `ApiWrapper.GetCompsGuides(gameLanguage)` 免费入口；失败时回退到插件内置或本地 registry 的最后可用版本。
2. 已落地：插件侧新增 `CompGuideCatalog` 只读层，经 `SeasonDataSelector` 读取当前赛季数据根下的 `season14_comp_guides.json` 与 `season14_guide_weight_reference.json`，不把攻略权重硬编码到算法中。
3. 每次算法优化前运行一个校准步骤：
   - 用最新攻略生成 `season14_guide_weight_reference.json`。
   - 当前口径：`T0=3, T1=2, T2=1, T3=0`；同一流派重复核心牌按数量保留，并计算 duplicate bonus。
   - 优化后跑行为测试与离线回放，确认权重变化没有破坏既有选择。
4. 插件内抓取需明确交互边界：启动时自动刷新、手动按钮刷新，还是只允许在用户点击时发起网络请求；该项与是否允许插件直接访问外部接口一起确认。

### 实施顺序

1. [x] 人工确认 2 个 pending 别名并 `--apply`，生成正式权重参考。
2. [~] 本地 refresh wrapper 与测试已完成；定时任务、CI/CD、人工复审 PR 未实施。
3. [x] 插件侧 `CompGuideCatalog` 只读目录已落地，并经 `SeasonDataSelector` 读取当前赛季数据根；网络刷新入口、回退逻辑、发布未实施。

## 后续任务：算法迭代优化

- 状态：前置条件已闭环，算法本体未开始。
- 前置条件（当前基线）：
  1. [x] 数据与最新赛季同步：`season14_guide_source.json`、正式 registry、`season14_guide_weight_reference.json` 为最新且无 BGDUO。
  2. [x] 测试环境与最新赛季同步：`BOB_COACH_DATA_ROOT`、HDT 基线、season selector、行为测试套件覆盖当前赛季；`test_season14_guide_weights.js` 已纳入行为套件。
  3. [x] 各模块机制与最新赛季同步：Activate/Lockbox/Fishbait/Dark Gift/Trinket 生命周期等模块与规则目录一致；`unresolvedOfficialRows=10` 保留为人工追踪项，不算已解决。
  4. [x] 权重基准：用最新攻略重新生成 `season14_guide_weight_reference.json`；当前口径 `T0=3/T1=2/T2=1/T3=0`，真实权重无 null，合成 null 仅保留在测试边界。
- 后续入口：`CompGuideCatalog.TryGetWeight` / `TryGetTierWeight`；算法改动需跑行为测试与离线回放。
- 下一步：实现并回归 `TryGetWeight` / `TryGetTierWeight` 的算法消费路径，再进入算法迭代优化；CI/CD、插件网络抓取、发布仍需用户确认。

## 执行约束

- 证据、归档、生成产物统一写入 `E:\HDT-BobCoach-evidence\season14\`
- 不读写 `%APPDATA%\bob-coach\data\`
- 不加入任何 `BGDUO`/双人模式卡牌或机制
- 不删除文件、目录或 git 历史
- 不 `push`、不 `rebase`、不 `reset --hard`、不 force push
- 不修改 `.env`、密钥、CI/CD、数据库 schema
- 不安装全局依赖
- 大 JSON、审计结果、日志写入 evidence；终端只输出路径、条数、sha256 和错误摘要

## 定向命令

生成器：`E:\HDT-BobCoach-evidence\season14\tools\generate_season14_registries.js`

```powershell
node "E:\HDT-BobCoach-evidence\season14\tools\generate_season14_registries.js" archive `
  --old-en "E:\HDT-BobCoach-evidence\season14\raw\cards.enUS.35.6.2.244584.json" `
  --old-zh "E:\HDT-BobCoach-evidence\season14\raw\cards.zhCN.35.6.2.244584.json" `
  --new-en "E:\HDT-BobCoach-evidence\season14\raw\cards.enUS.latest.json" `
  --new-zh "E:\HDT-BobCoach-evidence\season14\raw\cards.zhCN.latest.json" `
  --audit "E:\HDT-BobCoach-evidence\season14\generated\audit_season_snapshot.36.2.248348.v2.json" `
  --output "E:\HDT-BobCoach-evidence\season14\archive\removed_cards\manifest.json"
```

T2 与 T3 使用同一生成器的 `mechanisms` 和 `cards` 子命令，分两次运行。每条命令传 `--season`、`--game-build`、`--card-pool-identity`、`--captured-at`。

## 完成定义

1. T1 manifest 中 `minions` 97 条、`spells` 6 条、`trinkets` 0 条。
2. T2/T3 共 8 个注册表全部带 `_meta`，必填字段齐全，sha256 与内容一致。
3. 所有生成结果不含 `^BGDUO` 的 cardId。
4. 交接文档标记 Season14 规则化/攻略/归档完成；T10/T11 本地部分（refresh wrapper + CompGuideCatalog + SeasonDataSelector + selector 运行时接入）已落地；CI/CD、插件网络抓取、发布未实施。
5. T6 harness 使用 season14 临时 `BOB_COACH_DATA_ROOT` 运行；断言数据根不是 APPDATA，且行为测试套件包含 `test_season14_registries.ps1`。
6. T8 规则目录校验通过，旧 raw 归档 manifest 完整，selector 可 list/verify/set，且插件侧只读 `CompGuideCatalog` 已直接读取 selector 指向的数据根。
7. T8 流派攻略/关键词注册表/旧攻略归档均校验通过；旧数据可经 selector 快速切换。
8. T9 本地抓取工具测试通过；真实 23 条源 dry-run 能产出校验报告并阻断 2 个待确认别名；未带 `--apply` 时正式 registry 不变。
