# 离线回放回归测试环境

Date: 2026-08-13

## 目标

减少“每个 DLL 更新都要人工打很多局”的验证成本。核心思路是：

1. 实战时把完整决策输入写入 `global_*.jsonl`。
2. 后续每次更新后，离线批量重放这些真实状态，用新 DLL 重新决策。
3. 输出预测动作、实际动作、旧推荐、硬约束违规和差异汇总。

这不是用来证明胜率或策略质量，而是用来在几分钟内发现回归和明显错误。

## 当前赛季机制确认

测试环境使用：

- 赛季：`season14`
- 版本：`36.2.248348`
- 卡池：`0bcc979ae21df891`

`season14_mechanic_profile.json` 当前状态：

- `activate`：active
- `lockbox`：active
- `fishbait`：active
- `darkGift`：active
- `trinket`：active
- `anomaly`：inactive

离线重放水合时也会显式读取该开关。当前赛季即使旧数据残留畸变字段，也会在重放前清空并跳过畸变规则。

## 录制能力

`GlobalReplayState` 已扩展为可重放决策输入：

- 英雄、玩家、血量、铸币、星级、升级费用
- 商店、板面、手牌、发现、饰品
- 英雄技能状态与 HeroPower 实体
- 畸变、可用种族、已装备饰品、触发集合
- 对手板面与关键词
- 随从词缀、法术状态、费用、文本

新格式是旧 `global_*.jsonl` 的增量扩展；旧回放仍可分析，但没有 `heroCardId` 等字段时无法用新 DLL 完整重放。

## 离线重放

```powershell
$env:BOBCOACH_HDT_DIR = 'D:\software\game\HDT-V2.7.0\HDT'

powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\evaluate\run_replay_regression.ps1 `
  -ReplayDir "$env:APPDATA\bob-coach\replays" `
  -OutputDirectory "E:\HDT-BobCoach-evidence\season14\generated\replay-regression"
```

也可指定单文件：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\evaluate\run_replay_regression.ps1 `
  -File "$env:APPDATA\bob-coach\replays\global_xxx.jsonl" `
  -OutputDirectory "E:\HDT-BobCoach-evidence\season14\generated\replay-regression"
```

## 输出

- `replay_predictions.jsonl`：每个可重放状态的新 DLL 推荐、实际动作、费用、金币、旧推荐。
- `replay_regression_summary.json`：汇总可重放状态数、加载失败数、硬约束违规、预测/实际差异。

## 指标

当前关注：

- `statesReplayed`：可被新 DLL 重放的状态数。
- `stateLoadFailures`：回放状态无法重建或重放失败数。
- `hardConstraintCount`：预测动作费用超过可用金币、非法出售目标等。
- `mismatchCount`：新 DLL 推荐与实际动作差异，用于抽样人工复核。

长期扩展：

- 新旧 DLL 在同一批回放上的推荐差异。
- 推荐与最终成型阵容的主线偏离。
- 高影响差异（最终名次差且动作方向完全不同）。

## 边界

- 当前可用样本：本地 `global_*.jsonl` 17 个、Firestone XML 36 个；但这些旧回放没有新格式决策输入，不能直接用于新 DLL 重放。
- 首批可重放样本需要先用新 DLL 打 1-2 局；结构性问题建议积累 30 局，趋势性差异建议积累 100 局。
- 只有录制增强后的回放才能完整重放；旧回放不能回测新 DLL。
- 回放状态仍可能缺少插件没有录制的隐藏游戏信息，重放结果应按“回归信号”看待。
- 大量离线测试不能替代少量真人实测；它只能把真人实测压缩到少量高价值验收局。
