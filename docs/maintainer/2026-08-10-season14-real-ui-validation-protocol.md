# BobCoach Season14 真实 UI 验收协议

Date: 2026-08-10
Status: Ready for real-device validation

## 目的

用真实 HDT + Hearthstone Season14 对局证据，确认算法推荐、动态铸币、英雄技能、暗赐、Activate 和 UI 视觉计划一致。

## 前置条件

1. HDT 已安装 BobCoach v4：
   - AppData：`%APPDATA%\HearthstoneDeckTracker\Plugins\BobCoach.dll`
   - 程序目录：`D:\software\game\HDT-V2.7.0\HDT\Plugins\BobCoach.dll`
2. 两处 DLL SHA256 均为：
   `B0D9A1A272733D01ED9102EDDE32751155ECF39B3CAC2F4B55D4836AE15F32F3`
3. 启动 HDT，再进入 Season14 单人模式对局。

## 证据采集步骤

1. 启动 HDT，进入对局。
2. 在以下场景截图或录像：
   - 0 铸币且存在商店/刷新/升本按钮；
   - 残血弱战力但算法原本可能建议升本；
   - 被动英雄技能可点时段；
   - 主动英雄技能可用时段；
   - Activate 随从在场且铸币足够；
   - 暗赐按钮可点，T3 后且铸币 ≥3；
   - 发现/三连/技能发现面板触发；
   - 暗月奖品、时光购买、饰品选择面板。
3. 对局结束或需要收证据时，运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\release\collect_real_ui_evidence.ps1
```

脚本会校验两处 DLL、定位最新非空 `Power.log`、截取尾部 20000 行并回放。

4. 把截图/录像放入：
   `E:\HDT-BobCoach-evidence\season14\generated\ui-evidence`

## 验收标准

- 0 铸币时不再显示购买、刷新、升本、发动等不可支付 UI。
- 残血且战力比 <0.7、升本后无余钱时，不再主推升本。
- 被动英雄技能不出现“使用技能”提示。
- 主动英雄技能只在费用、解锁、目标、性价比门控通过时提示。
- Activate 随从在铸币足够且未发动时显示目标脉冲和“发动→名称”文本。
- 暗赐按钮只出现在 T3 起、每局未超过 3 次、本回合未用过、铸币 ≥3 时。
- 发现面板只由 Power.log `WaitThenShowChoices` 权威触发，普通酒馆面板不误开。
- 用户可见卡名不出现裸 CardId。
- `RecommendationPlan` 与 `VisualPlan` 最终计划一致，`BobCoachPlugin` 日志无 `RecommendationPlan consistency` 错误。

## 证据是否满足

| 证据 | 状态 |
| --- | --- |
| 源码/合成/行为/package 门禁 | 已完成 |
| 真实 Power_old.log 回放 | 已完成 |
| 最新 Power.log 尾部回放 | 已完成 |
| HDT 实机截图/录像 | 待采集 |
| BobCoach 插件运行日志 | 待采集 |
