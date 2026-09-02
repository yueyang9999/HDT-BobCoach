# 数据资产清单

> 全部数据资产的位置索引(不入库, 在 E:/HDT-BobCoach-evidence/)。

## 训练数据

| 资产 | 路径 | 内容 |
|---|---|---|
| 英雄节奏画像 | evidence/playbook/hero_ops_rhythm.json | 97英雄: 买/卖/刷/三连/升级节奏 |
| 英雄策略参数 | evidence/playbook/hero_strategy_params.json | 97英雄: levelAgg/buyBias/refBias |
| 运营画像 | evidence/playbook/ops_profiles.json | 1281局逐局画像(含买卡列表) |
| 对局清单 | evidence/playbook/season_failure_games.json | 279局(124失败+155成功) |
| 升级安全线 | evidence/playbook/upgrade_safety_training.json | 升本板面阈值训练集 |
| 板面战力 | evidence/playbook/board_power_training.json | 战力估算训练集 |

## 模拟测试报告

| 版本 | 路径 | 说明 |
|---|---|---|
| R240 | evidence/simulation-season14/reconstruct-r240 | 基线(对手cost bug未修) |
| R243base | evidence/simulation-season14/reconstruct-r243base | 基线(对手已修) |
| R246 | evidence/simulation-season14/reconstruct-r246 | 首升门槛0.6实验(否决) |
| R254 | evidence/simulation-season14/reconstruct-r254 | 最新基线 |
| R257 | evidence/simulation-season14/reconstruct-r257 | 法术槽修复后: 名次3.418 前四72.4% |

## 回放数据

| 资产 | 路径 | 说明 |
|---|---|---|
| Firestone XML | evidence/season14/raw/firestone-replays/xml | 1742局HSReplay XML |
| 真实曲线 | evidence/playbook/user_replay_curves.json | 25局逐回合战力曲线 |
| HDT对局 | AppData/Roaming/HearthstoneDeckTracker/BgsLastGames.xml | 66局最近记录 |

## 知识库(本目录)

| 分类 | 文件 |
|---|---|
| 01-hero-training | hero-rank-gap-20260821.md |
| 02-mechanism-fidelity | fidelity-deviations.md |
| 03-strategy-patterns | player-patterns.md |
| 04-experiments | 见 OPTIMIZATION_LOG.md(权威) |
| 05-data-assets | 本文件 |

_更新: 新数据资产生成后登记。_