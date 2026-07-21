using System;
using System.Collections.Generic;
using System.Linq;

namespace BobCoach.Engine
{
    /// <summary>
    /// 战斗单位 — 模拟器内部表示，承载所有战斗关键词和运行时状态。
    /// </summary>
    public class CombatUnit
    {
        public string CardId = "";
        public string NameCn = "";
        public int Attack;
        public int Health;
        public int MaxHealth;
        public int BaseAttack;
        public int BaseHealth;

        // 战斗关键词
        public bool DivineShield;
        public bool Reborn;
        public bool Poisonous;  // Venomous合并到此字段
        public bool Taunt;
        public int Tier;
        public bool Alive;
        public bool RebornUsed;
        public bool Golden;
        public bool MegaWindfury;
        public int WindfuryAttacksLeft;  // 剩余攻击次数(普通1, 风怒2, 超级风怒4)
        public bool Stealth;             // 潜行关键词(原始)
        public bool Stealthed;           // 当前是否潜行中(初始true, 攻击后false)
        public bool Cleave;              // 顺劈: 同时伤害相邻目标
        public bool Overkill;            // 超杀: 消灭后多余伤害溅射
        public int AvengeCount;          // 复仇(N), 0=无复仇
        public bool AvengeTriggered;     // 本轮复仇是否已触发
        public int DeathCountWitnessed;  // 见证的友方死亡数

        // 触发类效果识别
        public bool HasDeathrattle;
        public bool HasStartOfCombat;
        public bool HasAura;
        public bool HasAvenge;
        public bool HasWhenDamaged;
        public bool HasRally;            // 进击(Rally): 远程先攻

        // 卡牌类型标签
        public List<string> MinionTypes = new List<string>();
        public List<string> Mechanics = new List<string>();
        public int Position;

        // 运行时状态
        public bool DeathrattleTriggered;   // 亡语已触发过(金色可触发2次)
        public int DeathrattleCount;        // 亡语可触发次数(普通1, 金色2)
        public bool StartOfCombatTriggered; // 战斗开始时效果已触发
        public bool RebornOnly;             // 金色复生: 后续带复生字样的token

        // 特殊数据存储(从MinionData/board继承)
        public object StoredCopy;           // 缝合回收者存储的副本
        public Dictionary<string, object> Extra = new Dictionary<string, object>();

        // 战斗结算用
        public CombatUnit KilledBy;         // 击杀本随从的单位

        /// <summary>浅拷贝创建战斗单位副本。</summary>
        public CombatUnit ShallowCopy()
        {
            return (CombatUnit)this.MemberwiseClone();
        }
    }

    /// <summary>
    /// 战斗上下文 — 持有双方阵容、事件队列和辅助方法。
    /// 战斗开始时创建一次，贯穿整个战斗生命周期。
    /// </summary>
    public class CombatContext
    {
        public List<CombatUnit> AttackerSide;
        public List<CombatUnit> DefenderSide;
        public Random Rng;
        public CombatEventQueue EventQueue;
        public int DeathCount; // 全场死亡计数(用于光环/复仇)

        // 饰品/英雄技能处理器(战斗开始时阶段)
        public Action<CombatContext, List<CombatUnit>, List<CombatUnit>> AttackerPriorityHeroPower;
        public Action<CombatContext, List<CombatUnit>, List<CombatUnit>> DefenderPriorityHeroPower;
        public Action<CombatContext, List<CombatUnit>, List<CombatUnit>> AttackerHeroPower;
        public Action<CombatContext, List<CombatUnit>, List<CombatUnit>> DefenderHeroPower;
        public List<Action<CombatContext, List<CombatUnit>, List<CombatUnit>>> AttackerTrinketHandlers;
        public List<Action<CombatContext, List<CombatUnit>, List<CombatUnit>>> DefenderTrinketHandlers;

        // 所有参与战斗的单位(用于赛后统计)
        public List<CombatUnit> AllUnits = new List<CombatUnit>();

        // 召唤位置追踪(亡语召唤在召唤者右侧)
        public int LastSummonerPos = -1;
        public int SummonerOffset = 0;
        public CombatUnit LastSummoned;

        // 战斗级全局状态存储 (甲虫buff等跨单位效果)
        public Dictionary<string, object> Extra = new Dictionary<string, object>();

        // 手牌随从（供START_OF_COMBAT等效果查询/召唤）
        public List<MinionData> AttackerHand = new List<MinionData>();
        public List<MinionData> DefenderHand = new List<MinionData>();
        public ActiveTrinketContext AttackerTrinkets { get; private set; }
        public ActiveTrinketContext DefenderTrinkets { get; private set; }

        public CombatContext(List<CombatUnit> atkUnits, List<CombatUnit> defUnits, Random rng,
            List<MinionData> attackerHand = null, List<MinionData> defenderHand = null,
            ActiveTrinketContext attackerTrinkets = null,
            ActiveTrinketContext defenderTrinkets = null)
        {
            AttackerSide = atkUnits;
            DefenderSide = defUnits;
            Rng = rng ?? new Random();
            EventQueue = new CombatEventQueue();
            DeathCount = 0;
            AttackerHand = attackerHand ?? new List<MinionData>();
            DefenderHand = defenderHand ?? new List<MinionData>();
            AttackerTrinkets = attackerTrinkets ?? ActiveTrinketContext.Empty;
            DefenderTrinkets = defenderTrinkets ?? ActiveTrinketContext.Empty;

            // 初始化allUnits引用
            foreach (var u in atkUnits) AllUnits.Add(u);
            foreach (var u in defUnits) AllUnits.Add(u);
        }

        // ── 辅助方法 ──

        /// <summary>
        /// 构建token随从用于亡语/复仇召唤。
        /// </summary>
        public CombatUnit BuildToken(int attack, int health, string name = "",
            bool taunt = false, bool divineShield = false, bool reborn = false,
            bool venomous = false, int tier = 1, List<string> minionTypes = null)
        {
            return new CombatUnit
            {
                CardId = "token_" + (name.Length > 0 ? name : "tk"),
                NameCn = name.Length > 0 ? name : "token",
                Attack = attack,
                Health = health,
                MaxHealth = health,
                BaseAttack = attack,
                BaseHealth = health,
                DivineShield = divineShield,
                Reborn = reborn,
                Poisonous = venomous,
                Taunt = taunt,
                Tier = tier,
                Alive = true,
                RebornUsed = false,
                Golden = false,
                WindfuryAttacksLeft = 1,
                MinionTypes = minionTypes ?? new List<string>(),
                Position = 999,
            };
        }

        /// <summary>
        /// 在战场上召唤token。
        /// S13规则: 战场上限7格，超出触发挤爆机制。
        /// 亡语召唤在召唤者右侧(side.IndexOf + pos+1)，多次召唤累加偏移。
        /// </summary>
        public CombatUnit SpawnToken(List<CombatUnit> side, CombatUnit token, int insertAfterIndex = -1)
        {
            const int MaxSlots = 7;
            if (side.Count >= MaxSlots)
            {
                TriggerCram(side);
                return null;
            }

            // 确保token已初始化(_combatInit)
            token.Alive = true;
            token.RebornUsed = false;
            token.WindfuryAttacksLeft = 1;

            int insertPos;
            if (insertAfterIndex >= 0 && insertAfterIndex < side.Count)
            {
                insertPos = insertAfterIndex + 1;
            }
            else if (LastSummonerPos >= 0)
            {
                insertPos = LastSummonerPos + 1 + SummonerOffset;
                if (insertPos > side.Count) insertPos = side.Count;
                SummonerOffset++;
            }
            else
            {
                insertPos = side.Count;
            }

            side.Insert(insertPos, token);
            if (ReferenceEquals(side, AttackerSide))
                AttackerTrinkets.ApplySummon(token);
            else if (ReferenceEquals(side, DefenderSide))
                DefenderTrinkets.ApplySummon(token);
            token.Position = insertPos;
            AllUnits.Add(token);
            LastSummoned = token;
            return token;
        }

        /// <summary>
        /// 从手牌召唤随从到场上（本场战斗临时，consumed从手牌移除）。
        /// </summary>
        public CombatUnit SummonFromHand(List<MinionData> hand, List<CombatUnit> side,
            string cardId = null, bool highestAttack = false, string minionType = null, bool consume = true)
        {
            if (hand == null || hand.Count == 0) return null;
            const int MaxSlots = 7;
            if (side.Count(u => u.Alive) >= MaxSlots)
            {
                TriggerCram(side);
                return null;
            }

            MinionData target = null;
            int targetIdx = -1;

            if (!string.IsNullOrEmpty(cardId))
            {
                for (int i = 0; i < hand.Count; i++)
                {
                    if (hand[i].CardId == cardId) { target = hand[i]; targetIdx = i; break; }
                }
            }
            else if (highestAttack)
            {
                int bestAtk = -1;
                for (int i = 0; i < hand.Count; i++)
                {
                    var hm = hand[i];
                    int atk = (hm.Attack > 0 ? hm.Attack : 1) * (hm.Golden ? 2 : 1);
                    bool typeMatch = string.IsNullOrEmpty(minionType)
                        || (hm.Tribe != null && hm.Tribe.Contains(minionType));
                    if (typeMatch && atk > bestAtk) { bestAtk = atk; target = hm; targetIdx = i; }
                }
            }

            if (target == null) return null;

            var token = CombatSimulator.BuildUnitStatic(target);
            SpawnToken(side, token);

            if (consume)
                hand.RemoveAt(targetIdx);

            return token;
        }

        /// <summary>
        /// 判断随从是否属于指定种族。
        /// </summary>
        public bool IsMinionType(CombatUnit unit, string type)
        {
            if (unit.MinionTypes == null || unit.MinionTypes.Count == 0) return false;
            return unit.MinionTypes.Contains(type);
        }

        /// <summary>
        /// 查找单位所属的阵营(AttackerSide或DefenderSide)。
        /// </summary>
        public List<CombatUnit> FindSide(CombatUnit unit)
        {
            if (AttackerSide.Contains(unit)) return AttackerSide;
            return DefenderSide;
        }

        /// <summary>
        /// 查找单位的敌对阵营。
        /// </summary>
        public List<CombatUnit> EnemySide(CombatUnit unit)
        {
            if (AttackerSide.Contains(unit)) return DefenderSide;
            return AttackerSide;
        }

        // ── 内部方法 ──

        /// <summary>
        /// 随从死亡时调用 — 入队亡语/复生/光环修正/复仇事件。
        /// </summary>
        public void OnDeath(CombatUnit unit)
        {
            DeathCount++;
            unit.Position = FindSide(unit).IndexOf(unit);

            // 更新光环类随从的死亡计数
            UpdateDeathAuras(unit);

            // 记录亡语召唤位置
            LastSummonerPos = FindSide(unit).IndexOf(unit);
            SummonerOffset = 0;

            var deathSide = FindSide(unit);
            var enemySideList = EnemySide(unit);

            // 1) 亡语事件入队 (支持瑞文戴尔 deathrattleAmp)
            if (unit.HasDeathrattle && !unit.DeathrattleTriggered && unit.DeathrattleCount > 0)
            {
                unit.DeathrattleTriggered = true;
                var handlers = CombatEffects.GetHandlers(unit.CardId);
                if (handlers != null && handlers.Deathrattle != null)
                {
                    // 检查同侧死亡之语增幅(瑞文: 普通2x, 金色3x)
                    int drMult = GetDeathrattleAmp(deathSide);
                    var capturedUnit = unit;
                    var capturedSide = deathSide;
                    var capturedEnemy = enemySideList;
                    var drHandler = handlers.Deathrattle;

                    for (int dc = 0; dc < drMult; dc++)
                    {
                        EventQueue.Push(new CombatEvent
                        {
                            Type = "DEATHRATTLE",
                            Priority = CombatEventPriority.Deathrattle,
                            Handler = (ctx) =>
                            {
                                drHandler(ctx, capturedUnit, capturedSide, capturedEnemy);
                                // 亡语召唤的token需要被光环感知(科多兽等)
                                ProcessAllAuras();
                            },
                            Data = new Dictionary<string, object> { { "cardId", unit.CardId } }
                        });
                    }
                }

                // 金色亡语可再触发
                if (unit.Golden && unit.DeathrattleCount >= 2)
                {
                    unit.DeathrattleCount--;
                    unit.DeathrattleTriggered = false;
                }
            }

            // 2) 复生事件入队 (在亡语之后)
            if (unit.Reborn && !unit.RebornUsed)
            {
                var capturedUnit = unit;
                var capturedRebornSide = deathSide;
                EventQueue.Push(new CombatEvent
                {
                    Type = "REBORN",
                    Priority = CombatEventPriority.Reborn,
                    Handler = (ctx) =>
                    {
                        if (!capturedUnit.Alive && capturedUnit.Reborn && !capturedUnit.RebornUsed)
                        {
                            capturedUnit.Alive = true;
                            capturedUnit.Attack = capturedUnit.BaseAttack;
                            capturedUnit.Health = 1;
                            capturedUnit.MaxHealth = 1;
                            capturedUnit.RebornUsed = true;
                            capturedUnit.DivineShield = false;
                            capturedUnit.Taunt = false;
                            capturedUnit.Stealthed = false;
                            capturedUnit.WindfuryAttacksLeft = capturedUnit.MegaWindfury ? 4 : 1;
                            capturedUnit.DeathrattleTriggered = false;
                            capturedUnit.AvengeTriggered = false;
                            capturedUnit.DeathCountWitnessed = 0;
                            capturedUnit.KilledBy = null;

                            if (ReferenceEquals(capturedRebornSide, AttackerSide))
                                AttackerTrinkets.ApplySummon(capturedUnit);
                            else if (ReferenceEquals(capturedRebornSide, DefenderSide))
                                DefenderTrinkets.ApplySummon(capturedUnit);
                        }
                    },
                    Data = new Dictionary<string, object> { { "cardId", unit.CardId } }
                });
            }

            // 3) 光环修正事件
            EventQueue.Push(new CombatEvent
            {
                Type = "AURA_UPDATE",
                Priority = CombatEventPriority.AuraUpdate,
                Handler = (ctx) => { ProcessAllAuras(); },
                Data = new Dictionary<string, object>()
            });

            // 4) 复仇事件
            TriggerAvenge(unit, deathSide);
        }

        /// <summary>
        /// "受伤时"效果 — 最高优先级，立即入队。
        /// </summary>
        public void TriggerWhenDamaged(CombatUnit damagedUnit)
        {
            if (!damagedUnit.Alive) return;
            var side = FindSide(damagedUnit);
            for (int i = 0; i < side.Count; i++)
            {
                if (side[i].Alive && side[i].HasWhenDamaged)
                {
                    var handlers = CombatEffects.GetHandlers(side[i].CardId);
                    if (handlers != null && handlers.WhenDamaged != null)
                    {
                        var wdHandler = handlers.WhenDamaged;
                        var capturedUnit = side[i];
                        var capturedSide = side;
                        var capturedDmg = damagedUnit;
                        EventQueue.Push(new CombatEvent
                        {
                            Type = "WHEN_DAMAGED",
                            Priority = CombatEventPriority.WhenDamaged,
                            Handler = (ctx) => wdHandler(ctx, capturedDmg, capturedSide, EnemySide(capturedSide[0]), EventQueue),
                            Data = new Dictionary<string, object> { { "cardId", capturedUnit.CardId } }
                        });
                    }
                }
            }
        }

        /// <summary>
        /// 处理事件队列。
        /// </summary>
        public void ProcessEvents()
        {
            EventQueue.ProcessAll(this);
        }

        /// <summary>
        /// 对双方所有存活单位运行光环处理器。
        /// </summary>
        public void ProcessAllAuras()
        {
            ProcessAuraSide(AttackerSide);
            ProcessAuraSide(DefenderSide);

            // 光环可能复活0血随从
            var allSides = new List<List<CombatUnit>> { AttackerSide, DefenderSide };
            foreach (var side in allSides)
            {
                for (int j = 0; j < side.Count; j++)
                {
                    if (!side[j].Alive && side[j].Health > 0)
                    {
                        side[j].Alive = true;
                    }
                }
            }
        }

        private void ProcessAuraSide(List<CombatUnit> side)
        {
            for (int i = 0; i < side.Count; i++)
            {
                var u = side[i];
                if (u.Alive && u.HasAura)
                {
                    var handlers = CombatEffects.GetHandlers(u.CardId);
                    if (handlers != null && handlers.Aura != null)
                    {
                        handlers.Aura(this, u, side, EnemySide(u));
                    }
                }
            }
        }

        /// <summary>
        /// 获取亡语增幅倍数 — 扫描同侧存活的deathrattleAmp单位。
        /// 普通瑞文戴尔=2倍, 金色=3倍。取最大值。
        /// </summary>
        private int GetDeathrattleAmp(List<CombatUnit> side)
        {
            int amp = 1;
            for (int i = 0; i < side.Count; i++)
            {
                if (!side[i].Alive) continue;
                var handlers = CombatEffects.GetHandlers(side[i].CardId);
                if (handlers != null && handlers.DeathrattleAmp != null)
                {
                    int unitAmp = side[i].Golden ? 3 : 2;
                    if (unitAmp > amp) amp = unitAmp;
                }
            }
            return amp;
        }

        /// <summary>
        /// 更新双方所有光环随从的死亡计数。
        /// </summary>
        private void UpdateDeathAuras(CombatUnit deadUnit)
        {
            var allSides = new List<List<CombatUnit>> { AttackerSide, DefenderSide };
            foreach (var side in allSides)
            {
                for (int i = 0; i < side.Count; i++)
                {
                    if (side[i].Alive && side[i].HasAura)
                    {
                        // 死亡计数由ProcessAllAuras内的aura处理器自己查询ctx.DeathCount
                    }
                }
            }
        }

        /// <summary>
        /// S13挤爆: 召唤失败(满场)时触发古墓捣蛋鬼等效果。
        /// </summary>
        public void TriggerCram(List<CombatUnit> side)
        {
            for (int i = 0; i < side.Count; i++)
            {
                if (side[i].Alive)
                {
                    var handlers = CombatEffects.GetHandlers(side[i].CardId);
                    if (handlers != null && handlers.Cram != null)
                    {
                        var capturedUnit = side[i];
                        var capturedSide = side;
                        handlers.Cram(this, capturedUnit, capturedSide, EnemySide(capturedUnit));
                    }
                }
            }
        }

        /// <summary>
        /// 友方死亡时触发复仇效果入队。
        /// </summary>
        public void TriggerAvenge(CombatUnit deadUnit, List<CombatUnit> side)
        {
            for (int i = 0; i < side.Count; i++)
            {
                if (side[i].Alive && side[i].HasAvenge)
                {
                    // 检查复仇计数(简化: hasAvenge标记已满足)
                    var handlers = CombatEffects.GetHandlers(side[i].CardId);
                    if (handlers != null && handlers.Avenge != null)
                    {
                        var capturedUnit = side[i];
                        var capturedSide = side;
                        var avHandler = handlers.Avenge;
                        EventQueue.Push(new CombatEvent
                        {
                            Type = "AVENGE",
                            Priority = CombatEventPriority.Avenge,
                            Handler = (ctx) => avHandler(ctx, capturedUnit, capturedSide, EnemySide(capturedUnit), EventQueue),
                            Data = new Dictionary<string, object> { { "cardId", capturedUnit.CardId } }
                        });
                    }
                }
            }
        }
    }
}
