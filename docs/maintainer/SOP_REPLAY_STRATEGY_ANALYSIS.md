# SOP: 回放策略分析工作流(固化标准操作程序)

| 项 | 值 |
|---|---|
| 版本 | v1.1(2026-08-25) |
| 维护人 | BobCoach 算法组 |
| 适用范围 | 任意赛季 Hearthstone 战棋回放 → 策略知识积累 → 算法验证 |
| 触发条件 | 新赛季数据到达 / **赛季中补丁更新** / 新回放入库 / 策略迭代前 |
| 前置 | Node.js ≥ 18, 回放数据已同步到 evidence/decision-data/replays |

---

## 0. 目的与原则(必须先读)

**目的**: 把真实玩家对局转化为可学习的策略知识, 供 BobCoach 算法决策模块参考。

**铁律(违反即返工)**:
1. 每项策略规则必须能追溯到具体对局证据(报告文件+行号)
2. 血量/护甲只信任 replay_full 的 Armor 字段与英雄实体 Damage 事件(Firestone XML 无逐回合血量)
3. 对手阵容必须以真实回放为参考(每回合对阵记录), 不得用模拟器随机对手替代
4. 批量分析前必须先做 1 局完整人工核验(提取→比对回放→确认无漏)
5. 策略规则进算法前必须过 A/B(同 seed 同池, 多 seed 平均, 只保留不劣化)
6. **环境必须标注**: 每个分析报告必须写明 赛季/补丁/build, 不同环境的数据不得混用
7. **入库前必须验证**: 环境、卡牌、回放记录三者核验通过才能写入知识库

---

## 1. 环境标注(每次分析必须做)

### 1.1 环境三维标识
每个回放/报告必须标注 **赛季 + 补丁 + build**, 三选一即可推导另两个:

| 标识 | 来源 | 示例 |
|---|---|---|
| 赛季 | firestone-meta.seasonId / 数据目录 | season14 |
| 补丁版本 | hearthstonejson / 官方公告 | 36.2.1 / 36.2.2 |
| build号 | firestone-meta.buildNumber / 回放 | 248348 / 249896 |

### 1.2 环境对照表(维护于 KNOWLEDGE_BASE)
```
season14:
  36.2.0 → build 247xxx
  36.2.1 → build 248348 (本批 firestone XML)
  36.2.2 → build 249896 (当前, hearthstonejson 官方卡池)
```
**规则**: 报告引用卡池/规则时, 必须与回放 build 匹配。build 不匹配的卡(如镀金石像鬼 is_battlegrounds_pool=false)不得当作卡池随从。

### 1.3 报告中环境标注格式(固定)
```
- 环境: season14 / 36.2.1 / build 248348
- 卡池身份: 官方 hearthstonejson-249896 + 回放高频补充
- 数据源: replay_full_20260819_112036_72.json (HDT插件记录)
```

---

## 2. 输入准备(30 秒)

- [ ] 确认回放目录: `E:/HDT-BobCoach-evidence/decision-data/replays/replay_full_*.json`
- [ ] 无 replay_full 时, 用 Firestone XML(仅能验证操作细节, 血量/护甲缺失)
- [ ] **记录回放 build/赛季**(firestone-meta 或文件名时间戳推算)

## 3. 单局提取(命令)

```bash
cd E:/HDT-BobCoach/tools/simulation
# 单局
node extract_replay_full.js <回放.json> <输出.md>
# 批量(全部)
for f in E:/HDT-BobCoach-evidence/decision-data/replays/replay_full_*.json; do
  node extract_replay_full.js "$f" "E:/HDT-BobCoach-evidence/simulation-season14/hero_audit/games/$(basename $f .json).md"
done
```

**验收**: 每局报告包含——环境标注/英雄/名次/回合数/估算血量(30-max(0,累计Damage-初始甲))/逐回合(本·血·甲·金·板·战力·对手战力)/对手列表/事件统计。缺任一项 = 提取失败。

## 4. 信息核验(每批必做, 用户方法论)

### 4.1 回放记录验证
- [ ] 抽查 1-3 局: 报告数据与回放原始记录逐项比对(本数/血量/护甲/购买)
- [ ] 血量合理性: rank1 局最终血 > 0; rank7-8 局应有血量归零过程
- [ ] 护甲递减: Armor 应逐回合非增(饰品补甲除外)
- [ ] 对手存在: 每回合应有 1 个当前对阵对手(战棋轮转)

### 4.2 环境/卡牌验证(入库前置)
- [ ] 报告标注的 build 与回放实际 build 一致
- [ ] 报告中引用的卡牌, 在当前 build 卡池存在(不在池的卡要标注来源: 回放/官方)
- [ ] 英雄技能文本与当前 build 一致(旧 build 英雄技能可能不同)
- [ ] **未通过 → 修复后重提, 不得带病入库**

## 5. 策略分析(单局深度, 输出固定模板)

### 5.1 分层策略(新: 环境层 + 通用层)
策略分析必须**分两层**:

**A. 环境特定层(随补丁变)**:
- 当前补丁的强势流派/卡池引擎(如 build248348 的克罗米法术流)
- 当前补丁的英雄技能联动
- 标注: 适用 build, 补丁更新后需复查

**B. 通用层(游戏本质, 跨环境成立)**:
- 升级时机逻辑: "升本是买引擎卡的入口, 不是够钱就升"
- 对子策略: "引擎卡/核心卡买对子, 三连=发现+翻倍"
- 爆发回合: "攒对子→集中三连→先开经济饰品再刷"
- 满编换血: "有更高价值卡就卖最弱"
- 法术双重价值: "法术=引擎燃料+货币, 缺钱优先卖"
- 经济节奏: "理财卡早拿, 高本经济饰品优先"
- **通用技巧入库时标记: [通用] 前缀, 不绑定 build**

### 5.2 输出模板(每局固定)
```
## 策略总结
### 环境: season14 / 36.2.x / build xxx
### 路线: <流派名>(<驱动因素>)
### 升级节奏: T..升2/T..升3/T..升4/T..升5/T..升6
### 关键决策点: <3-5条, 每条=操作+动机+收益>
### 可编码规则(环境层): <绑定 build 的判定>
### 可编码规则(通用层): <跨环境成立的判定, [通用]标记>
```

## 6. 知识库积累(批量化沉淀)

- [ ] 高名次局(rank ≤ 2)报告 → `hero_audit/games/`
- [ ] 汇总清单 → `hero_audit/HIGH_QUALITY_GAMES.md`(英雄/名次/报告路径/**build**)
- [ ] **环境特定模式** → `knowledge-base/KNOWLEDGE_BASE.md` 标注适用 build
- [ ] **通用技巧** → `knowledge-base/KNOWLEDGE_BASE.md` 以 `[通用]` 前缀积累, 不绑定 build
- [ ] 每条模式标注: 来源局数 / 证据报告 / 适用环境 / 验证状态

## 7. 算法对接(策略 → 代码 → 验证)

1. 从"可编码规则"清单选改动(一次 1 条, 用户方法论)
2. 定位模块(LevelingModule / MinionPickModule / SellModule / RefreshModule)
3. 情景测试 → 控制变量 A/B(同 seed 同池, ≥12 局, 多 seed 平均)
4. **只保留不劣化**, 否则回滚
5. 记录到 `optimization-log/OPTIMIZATION_LOG.md`(R编号 + 环境build)
6. 环境特定规则: 标注适用 build, 补丁更新后重跑 A/B 确认仍有效

## 8. 对手剧本注入(拟真验证, 待建)

- [ ] 目标: 每回合把模拟器 HeuristicAI 对手板面替换为真实回放对手板面
- [ ] 数据: 从 replay_full `turns[].Opponents[]` 提取 → `opponent_script.json`(含 build 标注)
- [ ] 机制: SimulationEngine 加 `--opponent-script` 参数(参照 R315 --start-comps)
- [ ] 验证: Bob 在真实对手强度下的表现(名次/血量/前4率), 且对手剧本与 Bob 同环境

---

## 9. 维护与版本

- 提取器变更 → 更新本 SOP 第 3 节命令并重跑核验
- **赛季/补丁更新** → 更新环境对照表(1.2节), 旧报告保留但标注"已过期环境"
- 新机制/新卡 → 更新分类器(参考 INFO_EXTRACTION_POINTS.md 机制清单)
- SOP 变更 → 版本号+1, 记录变更原因
- 通用技巧积累 → 跨补丁持续有效, 优先复用于新环境

## 10. 相关文件索引

| 文件 | 作用 |
|---|---|
| tools/simulation/extract_replay_full.js | 通用提取器(本 SOP 核心工具) |
| hero_audit/README_WORKFLOW.md | 快速上手(精简版) |
| hero_audit/WORKFLOW_EXTRACTION.md | 提取细节(技术参考) |
| hero_audit/DECISION_ANALYSIS_002f34ab.md | 深度分析示例(克罗米局) |
| hero_audit/INFO_EXTRACTION_POINTS.md | 信息提取点/机制分类器 |
| knowledge-base/KNOWLEDGE_BASE.md | 知识库主文件(环境层+通用层) |
| optimization-log/OPTIMIZATION_LOG.md | 迭代日志(R编号+build) |
