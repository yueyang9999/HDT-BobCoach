# 决策事件采集与离线聚合

> 状态：本地决策事件录制为 Shadow 采集链路；真实 Season14 样本不足时，效果状态保持 `blocked-by-season-data`，`metrics: null`。
> 关联文档：`AGENTS.md` 的 Next-Season Algorithm Data Boundary、`docs/maintainer/SEASON_DATA_CONTRACT.md`、`docs/maintainer/BUILD.md`。

## 目的

本流程用于从真实 Season14 对战采集决策事件，并在仓库外完成校验、聚合和 baseline/candidate 对比。采集到的玩家操作只作为观察事件，不作为正确性标签，也不作为训练标签。

## 数据根要求

录制只接受显式配置的 `BOB_COACH_DATA_ROOT`：

- 必须是绝对路径；
- 必须已经存在；
- 不能在系统盘；
- 不能在仓库内，也不能包含仓库路径；
- 不能位于 `%APPDATA%\bob-coach`；
- 路径上不能包含 junction/symlink/reparse point。

录制输出固定为 `<data-root>\decision-events.jsonl`。该文件是真实对战证据，禁止复制进仓库，禁止提交到 Git。

推荐在启动 HDT 前先校验数据根：

```powershell
node tools\model\validate_decision_event_capture.js --data-root <data-root>
```

校验器不会创建目录或文件，只确认录制目标可用并输出下一步 ingest 命令。

## 插件端操作

1. 在 HDT 的插件列表中点击“Bob教练”按钮。
2. 出现 Power.log 配置/本地决策录制弹窗：
   - 只有点击“是”才会写入或修改 `log.config`，并在本次插件运行期间启用本地决策事件录制；
   - 点击“否”不会写入、不会启用。
3. 如果弹窗提示 `log.config` 已新建或修改，必须完全关闭炉石并重新启动后才会生效。
4. 如果弹窗提示配置已完整并允许录制，本地决策事件录制将从下一局开始生效。
5. 对局结束后检查 `<data-root>\decision-events.jsonl` 是否生成了真实事件。

录制只在当前插件运行期间启用；插件退出后再次启动需要重新走上述确认流程。

## 离线聚合

数据根下建议使用以下目录布局：

```text
<data-root>/
  decision-events.jsonl
  reports/
    decision-events-aggregate.json
```

聚合命令：

```powershell
node tools\model\ingest_decision_events.js `
  --data-root <data-root> `
  --input decision-events.jsonl `
  --output reports\decision-events-aggregate.json
```

聚合结果包含事件类型计数、决策数、身份字段去重值、trinket offer 决策数和 `PickTrinket` 观察操作数，可用于判断样本是否真实覆盖当前赛季身份和饰品决策面。

## baseline/candidate 对比

拿到 baseline 和 candidate 两份真实 decision-events 样本后运行：

```powershell
node tools\model\compare_decision_events.js `
  --baseline <baseline-decision-events.jsonl> `
  --candidate <candidate-decision-events.jsonl>
```

在满足至少 300 个真实、可信、版本匹配的专家标注场景，以及代表性质 Shadow 对比和人工 severe-error review 之前，对比结果不能转换为胜率、名次或策略质量提升声明。

## 约束

- 不接网络抓取真实对局；本流程只采集本机已确认启用的 Shadow 事件。
- 不读写 `%APPDATA%\bob-coach\data\`。
- 不把决策事件、聚合报告或任何真实样本写入仓库。
- 不虚构 `decision-events.jsonl` 作为真实 Season14 样本。
- 无足够真实样本时继续报告 `blocked-by-season-data` 和 `metrics: null`。
- `unresolvedOfficialRows=10` 保持人工跟踪，不因本流程落地而标记解决。
