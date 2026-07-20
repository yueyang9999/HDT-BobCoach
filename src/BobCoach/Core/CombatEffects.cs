using System;
using System.Collections.Generic;

namespace BobCoach.Engine
{
    /// <summary>
    /// 卡牌战斗效果处理器集合。
    /// 每个字段为null表示该卡牌无此类型效果。
    /// </summary>
    public class CardHandlers
    {
        /// <summary>亡语效果: (ctx, 死亡单位, 己方阵容, 敌方阵容)</summary>
        public Action<CombatContext, CombatUnit, List<CombatUnit>, List<CombatUnit>> Deathrattle;

        /// <summary>战斗开始时效果: (ctx, 己方阵容, 敌方阵容)</summary>
        public Action<CombatContext, List<CombatUnit>, List<CombatUnit>> StartOfCombat;

        /// <summary>光环效果: (ctx, 光环单位, 己方阵容, 敌方阵容)</summary>
        public Action<CombatContext, CombatUnit, List<CombatUnit>, List<CombatUnit>> Aura;

        /// <summary>受伤时效果: (ctx, 受伤单位, 己方阵容, 敌方阵容, 事件队列)</summary>
        public Action<CombatContext, CombatUnit, List<CombatUnit>, List<CombatUnit>, CombatEventQueue> WhenDamaged;

        /// <summary>复仇效果: (ctx, 复仇单位, 己方阵容, 敌方阵容, 事件队列)</summary>
        public Action<CombatContext, CombatUnit, List<CombatUnit>, List<CombatUnit>, CombatEventQueue> Avenge;

        /// <summary>亡语增幅器: 非null即表示"此单位提供亡语触发翻倍"。</summary>
        public Func<bool> DeathrattleAmp;

        /// <summary>进击(Rally): (ctx, 进击单位, 己方阵容, 敌方阵容, 目标单位)</summary>
        public Action<CombatContext, CombatUnit, List<CombatUnit>, List<CombatUnit>, CombatUnit> Rally;

        /// <summary>挤爆(Cram): 召唤失败时触发 (ctx, 触发单位, 己方阵容, 敌方阵容)</summary>
        public Action<CombatContext, CombatUnit, List<CombatUnit>, List<CombatUnit>> Cram;
    }

    /// <summary>
    /// 英雄技能战斗效果条目。
    /// </summary>
    public class HeroPowerEntry
    {
        public bool Priority;   // 是否优先级英雄技能(高于随从/饰品)
        public Action<CombatContext, List<CombatUnit>, List<CombatUnit>> Handler;
    }

    /// <summary>
    /// CombatEffects — 卡牌战斗效果注册表。
    /// 从JS CombatEffects.js 完整移植，驱动事件队列的战斗模拟。
    /// 覆盖所有已注册卡牌的死亡/战斗开始/光环/受伤/复仇/亡语增幅等战斗效果。
    /// </summary>
    public static class CombatEffects
    {
        private static Dictionary<string, CardHandlers> _registry = new Dictionary<string, CardHandlers>();
        private static Dictionary<string, HeroPowerEntry> _heroPowers = new Dictionary<string, HeroPowerEntry>();

        /// <summary>
        /// 注册单张卡牌的战斗效果处理器。
        /// </summary>
        public static void Register(string cardId, CardHandlers handlers)
        {
            _registry[cardId] = handlers;
        }

        /// <summary>
        /// 批量注册卡牌效果。
        /// </summary>
        public static void RegisterAll(Dictionary<string, CardHandlers> cardMap)
        {
            foreach (var kv in cardMap)
            {
                _registry[kv.Key] = kv.Value;
            }
        }

        /// <summary>
        /// 获取卡牌的战斗效果处理器，无注册则返回null。
        /// </summary>
        public static CardHandlers GetHandlers(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return null;
            CardHandlers result;
            _registry.TryGetValue(cardId, out result);
            return result;
        }

        /// <summary>
        /// 获取英雄技能战斗效果，无注册则返回null。
        /// 先精确匹配，再尝试包含匹配(兼容不同格式的英雄ID)。
        /// </summary>
        public static HeroPowerEntry GetHeroPower(string heroId)
        {
            if (string.IsNullOrEmpty(heroId)) return null;
            HeroPowerEntry result;
            if (_heroPowers.TryGetValue(heroId, out result))
                return result;

            // 包含匹配：遍历注册表找到包含heroId的键，或heroId包含注册键
            foreach (var kv in _heroPowers)
            {
                if (heroId.Contains(kv.Key) || kv.Key.Contains(heroId))
                    return kv.Value;
            }
            return null;
        }

        /// <summary>
        /// 静态构造：加载所有内建卡牌效果注册和英雄技能注册。
        /// </summary>
        static CombatEffects()
        {
            RegisterAll(Builtins);
            RegisterHeroPowers(HeroPowerBuiltins);
        }

        private static void RegisterHeroPowers(Dictionary<string, HeroPowerEntry> map)
        {
            foreach (var kv in map)
            {
                _heroPowers[kv.Key] = kv.Value;
            }
        }

        /// <summary>
        /// 内建卡牌效果注册表 — 从JS CombatEffects._builtins 完整移植。
        /// </summary>
        private static Dictionary<string, CardHandlers> Builtins
        {
            get
            {
                var b = new Dictionary<string, CardHandlers>();

                // ═══════════════════════════════════════
                // 亡语召唤类
                // ═══════════════════════════════════════

                // BG28_300: 亡语-召唤2个1/1骷髅
                b["BG28_300"] = new CardHandlers
                {
                    Deathrattle = (ctx, u, side, enemy) =>
                    {
                        for (int i = 0; i < 2; i++)
                            ctx.SpawnToken(side, ctx.BuildToken(1, 1, "骷髅",
                                minionTypes: new List<string> { "亡灵" }));
                    }
                };

                // BG32_430: 亡语-召唤2个1/1嘲讽野猪人
                b["BG32_430"] = new CardHandlers
                {
                    Deathrattle = (ctx, u, side, enemy) =>
                    {
                        for (int i = 0; i < 2; i++)
                            ctx.SpawnToken(side, ctx.BuildToken(1, 1, "野猪人",
                                taunt: true, minionTypes: new List<string> { "野猪人" }));
                    }
                };

                // BG19_010: 亡语-召唤2/3嘲讽龟
                b["BG19_010"] = new CardHandlers
                {
                    Deathrattle = (ctx, u, side, enemy) =>
                    {
                        ctx.SpawnToken(side, ctx.BuildToken(2, 3, "龟",
                            taunt: true, minionTypes: new List<string> { "野兽" }));
                    }
                };

                // BG26_800: 亡语-召唤2个0/1嘲讽豹宝宝 (魔刃豹)
                b["BG26_800"] = new CardHandlers
                {
                    Deathrattle = (ctx, u, side, enemy) =>
                    {
                        for (int i = 0; i < 2; i++)
                            ctx.SpawnToken(side, ctx.BuildToken(0, 1, "豹宝宝",
                                taunt: true, minionTypes: new List<string> { "野兽" }));
                    }
                };

                // BG29_611: 亡语-召唤1/1微型机器人
                b["BG29_611"] = new CardHandlers
                {
                    Deathrattle = (ctx, u, side, enemy) =>
                    {
                        ctx.SpawnToken(side, ctx.BuildToken(1, 1, "微型机器人",
                            minionTypes: new List<string> { "机械" }));
                    }
                };

                // BGS_018: 亡语-召唤3/3鬣狗
                b["BGS_018"] = new CardHandlers
                {
                    Deathrattle = (ctx, u, side, enemy) =>
                    {
                        ctx.SpawnToken(side, ctx.BuildToken(3, 3, "鬣狗",
                            minionTypes: new List<string> { "野兽" }));
                    }
                };

                // BG20_402: 亡语-召唤2/2圣盾微型机器人
                b["BG20_402"] = new CardHandlers
                {
                    Deathrattle = (ctx, u, side, enemy) =>
                    {
                        ctx.SpawnToken(side, ctx.BuildToken(2, 2, "微型机器人",
                            divineShield: true, minionTypes: new List<string> { "机械" }));
                    }
                };

                // BG21_046: 亡语-召唤2个1/1滑油
                b["BG21_046"] = new CardHandlers
                {
                    Deathrattle = (ctx, u, side, enemy) =>
                    {
                        for (int i = 0; i < 2; i++)
                            ctx.SpawnToken(side, ctx.BuildToken(1, 1, "滑油",
                                minionTypes: new List<string> { "机械" }));
                    }
                };

                // BG26_803: 亡语-召唤3/3复生亡灵 (永恒召唤者)
                b["BG26_803"] = new CardHandlers
                {
                    Deathrattle = (ctx, u, side, enemy) =>
                    {
                        ctx.SpawnToken(side, ctx.BuildToken(3, 3, "亡灵",
                            reborn: true, minionTypes: new List<string> { "亡灵" }));
                    }
                };

                // BG34_630: 亡语-召唤3/3暮光龙崽
                b["BG34_630"] = new CardHandlers
                {
                    Deathrattle = (ctx, u, side, enemy) =>
                    {
                        ctx.SpawnToken(side, ctx.BuildToken(3, 3, "暮光龙崽",
                            minionTypes: new List<string> { "龙" }));
                    }
                };

                // BG25_010: 亡语-召唤2/1手(复生)
                b["BG25_010"] = new CardHandlers
                {
                    Deathrattle = (ctx, u, side, enemy) =>
                    {
                        ctx.SpawnToken(side, ctx.BuildToken(2, 1, "手",
                            reborn: true, minionTypes: new List<string> { "亡灵" }));
                    }
                };

                // BG30_125: 亡语-召唤3个1/1骷髅
                b["BG30_125"] = new CardHandlers
                {
                    Deathrattle = (ctx, u, side, enemy) =>
                    {
                        for (int i = 0; i < 3; i++)
                            ctx.SpawnToken(side, ctx.BuildToken(1, 1, "骷髅",
                                minionTypes: new List<string> { "亡灵" }));
                    }
                };

                // BG32_172: 亡语-召唤星元自动机token(3/4)
                b["BG32_172"] = new CardHandlers
                {
                    Deathrattle = (ctx, u, side, enemy) =>
                    {
                        ctx.SpawnToken(side, ctx.BuildToken(3, 4, "星元自动机",
                            minionTypes: new List<string> { "机械" }));
                    }
                };

                // BG34_731: 亡语-召唤2个1/1嘲讽暮光龙崽
                b["BG34_731"] = new CardHandlers
                {
                    Deathrattle = (ctx, u, side, enemy) =>
                    {
                        for (int i = 0; i < 2; i++)
                            ctx.SpawnToken(side, ctx.BuildToken(1, 1, "暮光龙崽",
                                taunt: true, minionTypes: new List<string> { "龙" }));
                    }
                };

                // BG35_604: 亡语-召唤2个3/2下水道老鼠
                b["BG35_604"] = new CardHandlers
                {
                    Deathrattle = (ctx, u, side, enemy) =>
                    {
                        for (int i = 0; i < 2; i++)
                            ctx.SpawnToken(side, ctx.BuildToken(3, 2, "下水道老鼠",
                                minionTypes: new List<string> { "野兽" }));
                    }
                };

                // BG25_009: 亡语-召唤4/2永恒骑士
                b["BG25_009"] = new CardHandlers
                {
                    Deathrattle = (ctx, u, side, enemy) =>
                    {
                        ctx.SpawnToken(side, ctx.BuildToken(4, 2, "永恒骑士",
                            minionTypes: new List<string> { "亡灵" }));
                    }
                };

                // BG34_Giant_618: 亡语-召唤5个1/1骷髅 (巨型)
                b["BG34_Giant_618"] = new CardHandlers
                {
                    Deathrattle = (ctx, u, side, enemy) =>
                    {
                        for (int i = 0; i < 5; i++)
                            ctx.SpawnToken(side, ctx.BuildToken(1, 1, "骷髅",
                                minionTypes: new List<string> { "亡灵" }));
                    }
                };

                // ═══════════════════════════════════════
                // 亡语非召唤类
                // ═══════════════════════════════════════

                // BG_DAL_775: 亡语-对所有随从造成3点伤害 (坑道爆破师)
                b["BG_DAL_775"] = new CardHandlers
                {
                    Deathrattle = (ctx, u, side, enemy) =>
                    {
                        for (int i = 0; i < side.Count; i++)
                        {
                            if (side[i].Alive)
                            {
                                side[i].Health -= 3;
                                side[i].MaxHealth -= 3;
                            }
                        }
                        for (int j = 0; j < enemy.Count; j++)
                        {
                            if (enemy[j].Alive)
                            {
                                enemy[j].Health -= 3;
                                enemy[j].MaxHealth -= 3;
                            }
                        }
                    }
                };

                // BG23_318: 亡语-消灭击杀本随从的随从 (火车王)
                b["BG23_318"] = new CardHandlers
                {
                    Deathrattle = (ctx, u, side, enemy) =>
                    {
                        if (u.KilledBy != null && u.KilledBy.Alive)
                        {
                            u.KilledBy.Health = 0;
                            u.KilledBy.Alive = false;
                        }
                        else if (enemy.Count > 0)
                        {
                            // 回退：消灭敌方攻击力最高的
                            CombatUnit best = enemy[0];
                            for (int ei = 1; ei < enemy.Count; ei++)
                            {
                                if (enemy[ei].Alive && enemy[ei].Attack > best.Attack)
                                    best = enemy[ei];
                            }
                            if (best.Alive)
                            {
                                best.Health = 0;
                                best.Alive = false;
                            }
                        }
                    }
                };

                // BG35_122: 亡语-相邻随从+5/+5并获得嘲讽 (坚定的防御者)
                b["BG35_122"] = new CardHandlers
                {
                    Deathrattle = (ctx, u, side, enemy) =>
                    {
                        int idx = side.IndexOf(u);
                        if (idx > 0 && side[idx - 1].Alive)
                        {
                            side[idx - 1].Attack += 5;
                            side[idx - 1].Health += 5;
                            side[idx - 1].MaxHealth += 5;
                            side[idx - 1].Taunt = true;
                        }
                        if (idx < side.Count - 1 && side[idx + 1].Alive)
                        {
                            side[idx + 1].Attack += 5;
                            side[idx + 1].Health += 5;
                            side[idx + 1].MaxHealth += 5;
                            side[idx + 1].Taunt = true;
                        }
                    }
                };

                // BG23_017: 亡语-宝石额外+1/+1 (鲜血勇士T7野猪人)
                b["BG23_017"] = new CardHandlers
                {
                    Deathrattle = (ctx, u, side, enemy) =>
                    {
                        // 标记宝石加成(非战斗直接效果，留给赛后处理)
                        int val = u.Extra.ContainsKey("_bloodGemBonus") ? (int)u.Extra["_bloodGemBonus"] : 0;
                        u.Extra["_bloodGemBonus"] = val + 1;
                    }
                };

                // BG27_016: 亡语-酒馆+5/+5 (萨格拉斯的勇士T7恶魔)
                b["BG27_016"] = new CardHandlers
                {
                    Deathrattle = (ctx, u, side, enemy) =>
                    {
                        int val = u.Extra.ContainsKey("_tavernBuff") ? (int)u.Extra["_tavernBuff"] : 0;
                        u.Extra["_tavernBuff"] = val + 5;
                    }
                };

                // BG34_319: 亡语-获取T6随从 (莱T7)
                b["BG34_319"] = new CardHandlers
                {
                    Deathrattle = (ctx, u, side, enemy) =>
                    {
                        u.Extra["_leDrop"] = true;
                    }
                };

                // BG31_999: 缝合回收者 — 战斗开始时+亡语召唤副本
                b["BG31_999"] = new CardHandlers
                {
                    StartOfCombat = (ctx, side, enemy) =>
                    {
                        // 实际销毁在SimulationEngine中完成，此处标记已触发
                    },
                    Deathrattle = (ctx, u, side, enemy) =>
                    {
                        if (u.StoredCopy != null)
                        {
                            // 尝试生成副本(简化: 使用存储的引用)
                            var copy = u.StoredCopy as CombatUnit;
                            if (copy != null)
                            {
                                var summoned = ctx.BuildToken(copy.Attack, copy.MaxHealth,
                                    copy.NameCn, copy.Taunt, copy.DivineShield, copy.Reborn,
                                    copy.Poisonous, copy.Tier, copy.MinionTypes);
                                summoned.Attack = copy.Attack;
                                summoned.Health = copy.Health;
                                summoned.Alive = true;
                                ctx.SpawnToken(side, summoned);
                            }
                        }
                    }
                };

                // ═══════════════════════════════════════
                // 战斗开始时效果
                // ═══════════════════════════════════════

                // BG26_805: 战斗开始时-所有友方野兽+1攻击力 (哼鸣蜂鸟)
                b["BG26_805"] = new CardHandlers
                {
                    StartOfCombat = (ctx, side, enemy) =>
                    {
                        for (int i = 0; i < side.Count; i++)
                        {
                            if (side[i].Alive && ctx.IsMinionType(side[i], "野兽"))
                                side[i].Attack += 1;
                        }
                    }
                };

                // BG25_040: 战斗开始时-所有友方龙+2/+1
                b["BG25_040"] = new CardHandlers
                {
                    StartOfCombat = (ctx, side, enemy) =>
                    {
                        for (int i = 0; i < side.Count; i++)
                        {
                            if (side[i].Alive && ctx.IsMinionType(side[i], "龙"))
                            {
                                side[i].Attack += 2;
                                side[i].Health += 1;
                                side[i].MaxHealth += 1;
                            }
                        }
                    }
                };

                // BGS_078: 巨大的金刚鹦鹉 — 战斗开始时触发最左侧亡语(支持瑞文增幅)
                b["BGS_078"] = new CardHandlers
                {
                    StartOfCombat = (ctx, side, enemy) =>
                    {
                        for (int i = 0; i < side.Count; i++)
                        {
                            if (!side[i].Alive || !side[i].HasDeathrattle) continue;
                            var h = GetHandlers(side[i].CardId);
                            if (h != null && h.Deathrattle != null)
                            {
                                int macawMult = 1;
                                for (int mi = 0; mi < side.Count; mi++)
                                {
                                    if (side[mi].Alive)
                                    {
                                        var mh = GetHandlers(side[mi].CardId);
                                        if (mh != null && mh.DeathrattleAmp != null)
                                        {
                                            macawMult = side[mi].Golden ? 3 : 2;
                                            break;
                                        }
                                    }
                                }
                                var capturedUnit = side[i];
                                var capturedSide = side;
                                var drHandler = h.Deathrattle;
                                for (int mc = 0; mc < macawMult; mc++)
                                {
                                    drHandler(ctx, capturedUnit, capturedSide, enemy);
                                    ctx.ProcessAllAuras();
                                }
                                break; // 仅触发最左侧一个
                            }
                        }
                    }
                };

                // BG20_201: 旧版金刚鹦鹉
                b["BG20_201"] = new CardHandlers
                {
                    StartOfCombat = (ctx, side, enemy) =>
                    {
                        for (int i = 0; i < side.Count; i++)
                        {
                            if (!side[i].Alive || !side[i].HasDeathrattle) continue;
                            var h = GetHandlers(side[i].CardId);
                            if (h != null && h.Deathrattle != null)
                            {
                                int macawMult = 1;
                                for (int mi = 0; mi < side.Count; mi++)
                                {
                                    if (side[mi].Alive)
                                    {
                                        var mh = GetHandlers(side[mi].CardId);
                                        if (mh != null && mh.DeathrattleAmp != null)
                                        {
                                            macawMult = side[mi].Golden ? 3 : 2;
                                            break;
                                        }
                                    }
                                }
                                var capturedUnit = side[i];
                                var capturedSide = side;
                                var drHandler = h.Deathrattle;
                                for (int mc = 0; mc < macawMult; mc++)
                                {
                                    drHandler(ctx, capturedUnit, capturedSide, enemy);
                                    ctx.ProcessAllAuras();
                                }
                                break;
                            }
                        }
                    }
                };

                // BG26_801: 重金属双头龙 — 战斗开始时触发相邻随从战吼(+3攻击力)
                b["BG26_801"] = new CardHandlers
                {
                    StartOfCombat = (ctx, side, enemy) =>
                    {
                        int idx = -1;
                        for (int si = 0; si < side.Count; si++)
                        {
                            if (side[si].CardId == "BG26_801") { idx = si; break; }
                        }
                        if (idx >= 0)
                        {
                            if (idx > 0 && side[idx - 1].Alive) side[idx - 1].Attack += 3;
                            if (idx < side.Count - 1 && side[idx + 1].Alive) side[idx + 1].Attack += 3;
                        }
                    }
                };

                // ═══════════════════════════════════════
                // 光环效果
                // ═══════════════════════════════════════

                // BG25_013: 光环-攻击力等于死亡计数
                b["BG25_013"] = new CardHandlers
                {
                    Aura = (ctx, u, side, enemy) =>
                    {
                        u.Attack = ctx.DeathCount;
                    }
                };

                // BG20_301: 光环-全体友方+2/+2
                b["BG20_301"] = new CardHandlers
                {
                    Aura = (ctx, u, side, enemy) =>
                    {
                        u.Attack += 2;
                        u.Health += 2;
                        u.MaxHealth += 2;
                    }
                };

                // BGS_041: 光环-全体友方龙+1/+1 (卡雷苟斯)
                b["BGS_041"] = new CardHandlers
                {
                    Aura = (ctx, u, side, enemy) =>
                    {
                        for (int i = 0; i < side.Count; i++)
                        {
                            if (side[i].Alive && ctx.IsMinionType(side[i], "龙"))
                            {
                                side[i].Attack += 1;
                                side[i].Health += 1;
                                side[i].MaxHealth += 1;
                            }
                        }
                    }
                };

                // BG_TTN_401: 光环-每有一个星元自动机+3/+2 (星元自动机本体)
                b["BG_TTN_401"] = new CardHandlers
                {
                    Aura = (ctx, u, side, enemy) =>
                    {
                        int copies = 0;
                        for (int i = 0; i < side.Count; i++)
                        {
                            if (side[i].CardId == "BG_TTN_401") copies++;
                        }
                        u.Attack += 3 * (copies - 1);
                        u.Health += 2 * (copies - 1);
                        u.MaxHealth += 2 * (copies - 1);
                    }
                };

                // BG25_022: 光环-全体友方机械+2攻击力 (义肢假手)
                b["BG25_022"] = new CardHandlers
                {
                    Aura = (ctx, u, side, enemy) =>
                    {
                        for (int i = 0; i < side.Count; i++)
                        {
                            if (side[i].Alive && ctx.IsMinionType(side[i], "机械"))
                                side[i].Attack += 2;
                        }
                    }
                };

                // BG_ICC_026: 光环-亡语随从+2攻击力 (布莱恩铜须)
                b["BG_ICC_026"] = new CardHandlers
                {
                    Aura = (ctx, u, side, enemy) =>
                    {
                        for (int i = 0; i < side.Count; i++)
                        {
                            if (side[i].Alive && side[i].HasDeathrattle)
                                side[i].Attack += 2;
                        }
                    }
                };

                // BG34_322: 坚韧的科多兽(T7) — 战斗中召唤时设定为自身属性(限3次)
                b["BG34_322"] = new CardHandlers
                {
                    Aura = (ctx, u, side, enemy) =>
                    {
                        int triggers = u.Extra.ContainsKey("_kodoTriggers") ? (int)u.Extra["_kodoTriggers"] : 0;
                        if (triggers < 3 && ctx.LastSummoned != null && ctx.LastSummoned != u)
                        {
                            int mul = u.Golden ? 2 : 1;
                            int targetAtk = u.Attack * mul;
                            int targetHp = u.Health * mul;
                            ctx.LastSummoned.Attack = Math.Max(ctx.LastSummoned.Attack, targetAtk);
                            ctx.LastSummoned.Health = Math.Max(ctx.LastSummoned.Health, targetHp);
                            ctx.LastSummoned.MaxHealth = Math.Max(ctx.LastSummoned.MaxHealth, targetHp);
                            u.Extra["_kodoTriggers"] = triggers + 1;
                        }
                    }
                };

                // ═══════════════════════════════════════
                // 受伤时效果
                // ═══════════════════════════════════════

                // BG29_806: 受伤时-给一个友方野兽+3/+2 (炫彩灼天者)
                b["BG29_806"] = new CardHandlers
                {
                    WhenDamaged = (ctx, damagedUnit, side, enemy, queue) =>
                    {
                        if (!ctx.IsMinionType(damagedUnit, "野兽")) return;
                        for (int i = 0; i < side.Count; i++)
                        {
                            if (side[i].Alive && side[i] != damagedUnit && ctx.IsMinionType(side[i], "野兽"))
                            {
                                side[i].Attack += 3;
                                side[i].Health += 2;
                                side[i].MaxHealth += 2;
                                break;
                            }
                        }
                    }
                };

                // ═══════════════════════════════════════
                // 复仇效果
                // ═══════════════════════════════════════

                // BG24_047: 复仇-召唤2/2嘲讽野猪人
                b["BG24_047"] = new CardHandlers
                {
                    Avenge = (ctx, u, side, enemy, queue) =>
                    {
                        ctx.SpawnToken(side, ctx.BuildToken(2, 2, "野猪人",
                            taunt: true, minionTypes: new List<string> { "野猪人" }));
                    }
                };

                // ═══════════════════════════════════════
                // 亡语增幅器
                // ═══════════════════════════════════════

                // BG25_354: 瑞文戴尔 — 亡语触发两次
                b["BG25_354"] = new CardHandlers { DeathrattleAmp = () => true };

                // TB_BaconUps_135: 瑞文戴尔(旧版)
                b["TB_BaconUps_135"] = new CardHandlers { DeathrattleAmp = () => true };

                // BG_LOE_077: 瑞文戴尔(探险者协会版)
                b["BG_LOE_077"] = new CardHandlers { DeathrattleAmp = () => true };

                // ═══════════════════════════════════════
                // 进击(Rally)效果
                // ═══════════════════════════════════════

                // BG25_016: 进击-移除目标复生+嘲讽 (辛多雷直射手)
                b["BG25_016"] = new CardHandlers
                {
                    Rally = (ctx, u, side, enemy, target) =>
                    {
                        if (target != null)
                        {
                            target.Reborn = false;
                            target.Taunt = false;
                            if (target.Mechanics != null)
                            {
                                target.Mechanics.Remove("REBORN");
                                target.Mechanics.Remove("TAUNT");
                            }
                        }
                    }
                };

                // BG34_604: 进击-获得目标攻击力 (英勇的逆袭者)
                b["BG34_604"] = new CardHandlers
                {
                    Rally = (ctx, u, side, enemy, target) =>
                    {
                        if (target != null)
                            u.Attack += target.Attack;
                    }
                };

                // BG34_320: 进击-每个类型各一个友方+12/+12 (最后的生物T7)
                b["BG34_320"] = new CardHandlers
                {
                    Rally = (ctx, u, side, enemy, target) =>
                    {
                        var done = new HashSet<string>();
                        for (int fi = 0; fi < side.Count; fi++)
                        {
                            if (!side[fi].Alive) continue;
                            var tribes = side[fi].MinionTypes;
                            if (tribes == null) continue;
                            for (int ft = 0; ft < tribes.Count; ft++)
                            {
                                if (!done.Contains(tribes[ft]))
                                {
                                    side[fi].Attack += 12;
                                    side[fi].Health += 12;
                                    side[fi].MaxHealth += 12;
                                    done.Add(tribes[ft]);
                                }
                            }
                        }
                    }
                };

                // BG34_765: 进击-3个友方获得本随从攻击力, 金色2倍 (死海破坏者 T6 35.6)
                b["BG34_765"] = new CardHandlers
                {
                    Rally = (ctx, u, side, enemy, target) =>
                    {
                        int count = 3;
                        int atk = u.Attack * (u.Golden ? 2 : 1);
                        var others = new List<CombatUnit>();
                        for (int i = 0; i < side.Count; i++)
                            if (side[i].Alive && side[i] != u) others.Add(side[i]);
                        for (int i = others.Count - 1; i > 0; i--)
                        { int j = ctx.Rng.Next(i + 1); var t = others[i]; others[i] = others[j]; others[j] = t; }
                        for (int i = 0; i < count && i < others.Count; i++)
                            others[i].Attack += atk;
                    }
                };

                // BG33_240: 进击-2条友方龙获得本随从生命上限, 金色2倍 (魅惑之翼 T6 35.6)
                b["BG33_240"] = new CardHandlers
                {
                    Rally = (ctx, u, side, enemy, target) =>
                    {
                        int count = 2;
                        int hp = (u.MaxHealth > 0 ? u.MaxHealth : u.Health) * (u.Golden ? 2 : 1);
                        var dragons = new List<CombatUnit>();
                        for (int i = 0; i < side.Count; i++)
                            if (side[i].Alive && side[i] != u && side[i].MinionTypes != null
                                && side[i].MinionTypes.Contains("DRAGON"))
                                dragons.Add(side[i]);
                        for (int i = dragons.Count - 1; i > 0; i--)
                        { int j = ctx.Rng.Next(i + 1); var t = dragons[i]; dragons[i] = dragons[j]; dragons[j] = t; }
                        for (int i = 0; i < count && i < dragons.Count; i++)
                        {
                            dragons[i].Health += hp;
                            dragons[i].MaxHealth += hp;
                        }
                    }
                };

                // ═══════════════════════════════════════
                // 挤爆(Cram)效果
                // ═══════════════════════════════════════

                // BG30_129: 挤爆-己方召唤失败→全体友方永久+2/+1(金+4/+2) (古墓捣蛋鬼)
                b["BG30_129"] = new CardHandlers
                {
                    Cram = (ctx, u, side, enemy) =>
                    {
                        int atkBuff = u.Golden ? 4 : 2;
                        int hpBuff = u.Golden ? 2 : 1;
                        for (int i = 0; i < side.Count; i++)
                        {
                            if (side[i].Alive)
                            {
                                side[i].Attack += atkBuff;
                                side[i].Health += hpBuff;
                                side[i].MaxHealth += hpBuff;
                            }
                        }
                    }
                };

                // ═══ 35.6 甲虫系统 (Beetle Growth) ═══
                // BG31_803 T1嗡鸣害虫: 亡语→召唤1只甲虫
                b["BG31_803"] = new CardHandlers
                {
                    Deathrattle = (ctx, u, side, enemy) =>
                    {
                        int count = u.Golden ? 2 : 1;
                        for (int i = 0; i < count && side.Count < 7; i++)
                            SummonBeetle(ctx, side);
                    }
                };

                // BG31_801 T2森林游虫: 亡语→召唤1只甲虫
                b["BG31_801"] = new CardHandlers
                {
                    Deathrattle = (ctx, u, side, enemy) =>
                    {
                        int count = u.Golden ? 2 : 1;
                        for (int i = 0; i < count && side.Count < 7; i++)
                            SummonBeetle(ctx, side);
                    }
                };

                // BG31_809 T5绿松石飞掠虫: 亡语→召唤1只甲虫
                b["BG31_809"] = new CardHandlers
                {
                    Deathrattle = (ctx, u, side, enemy) =>
                    {
                        int count = u.Golden ? 2 : 1;
                        for (int i = 0; i < count && side.Count < 7; i++)
                            SummonBeetle(ctx, side);
                    }
                };

                // BG32_204 T6丝柔烁光蛾: 受伤→甲虫+2/+1 + 亡语→召唤1只甲虫
                b["BG32_204"] = new CardHandlers
                {
                    WhenDamaged = (ctx, u, side, enemy, queue) =>
                    {
                        int mult = u.Golden ? 2 : 1;
                        int ba = 2 * mult, bh = 1 * mult;
                        int curAtk = ctx.Extra.ContainsKey("_beetleAtkBonus") ? (int)ctx.Extra["_beetleAtkBonus"] : 0;
                        int curHp = ctx.Extra.ContainsKey("_beetleHpBonus") ? (int)ctx.Extra["_beetleHpBonus"] : 0;
                        ctx.Extra["_beetleAtkBonus"] = curAtk + ba;
                        ctx.Extra["_beetleHpBonus"] = curHp + bh;
                        foreach (var m in side)
                        {
                            if (m.Alive && m.CardId == "BEETLE_TOKEN")
                            { m.Attack += ba; m.Health += bh; m.MaxHealth += bh; }
                        }
                    },
                    Deathrattle = (ctx, u, side, enemy) =>
                    {
                        int count = u.Golden ? 2 : 1;
                        for (int i = 0; i < count && side.Count < 7; i++)
                            SummonBeetle(ctx, side);
                    }
                };

                // BG34_Giant_687 T5时空扭曲巢穴群居虫: 进击→甲虫+2/+2 + 亡语→召唤1只甲虫
                b["BG34_Giant_687"] = new CardHandlers
                {
                    Rally = (ctx, u, side, enemy, target) =>
                    {
                        int mult = u.Golden ? 2 : 1;
                        int ba = 2 * mult, bh = 2 * mult;
                        int curAtk = ctx.Extra.ContainsKey("_beetleAtkBonus") ? (int)ctx.Extra["_beetleAtkBonus"] : 0;
                        int curHp = ctx.Extra.ContainsKey("_beetleHpBonus") ? (int)ctx.Extra["_beetleHpBonus"] : 0;
                        ctx.Extra["_beetleAtkBonus"] = curAtk + ba;
                        ctx.Extra["_beetleHpBonus"] = curHp + bh;
                        foreach (var m in side)
                        {
                            if (m.Alive && m.CardId == "BEETLE_TOKEN")
                            { m.Attack += ba; m.Health += bh; m.MaxHealth += bh; }
                        }
                    },
                    Deathrattle = (ctx, u, side, enemy) =>
                    {
                        int count = u.Golden ? 2 : 1;
                        for (int i = 0; i < count && side.Count < 7; i++)
                            SummonBeetle(ctx, side);
                    }
                };

                // ═══════════════════════════════════════
                // 手牌战斗效果 (hand combat effects)
                // ═══════════════════════════════════════

                // BG32_330 好斗的斥候(T1鱼人): 战斗开始时若在手牌，召唤自身复制
                b["BG32_330"] = new CardHandlers
                {
                    StartOfCombat = (ctx, side, enemy) =>
                    {
                        var hand = (side == ctx.AttackerSide) ? ctx.AttackerHand : ctx.DefenderHand;
                        if (hand == null) return;
                        foreach (var hm in hand)
                        {
                            if (hm.CardId == "BG32_330")
                            {
                                ctx.SummonFromHand(hand, side, cardId: "BG32_330", consume: false);
                                break;
                            }
                        }
                    }
                };

                // BG27_556 凶饿的觅食者(T4鱼人): 战斗开始时从手牌召唤攻击力最高的鱼人
                b["BG27_556"] = new CardHandlers
                {
                    StartOfCombat = (ctx, side, enemy) =>
                    {
                        var atkUnits = side;
                        var unit = atkUnits.Find(u => u.Alive && u.CardId == "BG27_556");
                        if (unit == null) return;
                        var hand = (side == ctx.AttackerSide) ? ctx.AttackerHand : ctx.DefenderHand;
                        if (hand == null || hand.Count == 0) return;
                        int count = unit.Golden ? 2 : 1;
                        for (int i = 0; i < count && hand.Count > 0; i++)
                            ctx.SummonFromHand(hand, side, highestAttack: true, minionType: "鱼人", consume: true);
                    }
                };

                // BG26_354 合唱鱼人(T6鱼人): 战斗开始时获得手牌中所有随从的属性值
                b["BG26_354"] = new CardHandlers
                {
                    StartOfCombat = (ctx, side, enemy) =>
                    {
                        var unit = side.Find(u => u.Alive && u.CardId == "BG26_354");
                        if (unit == null) return;
                        var hand = (side == ctx.AttackerSide) ? ctx.AttackerHand : ctx.DefenderHand;
                        if (hand == null) return;
                        int totalAtk = 0, totalHp = 0;
                        foreach (var hm in hand)
                        {
                            totalAtk += (hm.Attack > 0 ? hm.Attack : 0) * (hm.Golden ? 2 : 1);
                            totalHp += (hm.Health > 0 ? hm.Health : 0) * (hm.Golden ? 2 : 1);
                        }
                        int mult = unit.Golden ? 2 : 1;
                        unit.Attack += totalAtk * mult;
                        unit.Health += totalHp * mult;
                        unit.MaxHealth += totalHp * mult;
                    }
                };

                // BG34_140 飞行专家(T2鱼人): 进击时从手牌召唤攻击力最高的随从
                b["BG34_140"] = new CardHandlers
                {
                    Rally = (ctx, unit, side, enemy, target) =>
                    {
                        if (unit == null || !unit.Alive) return;
                        var hand = (side == ctx.AttackerSide) ? ctx.AttackerHand : ctx.DefenderHand;
                        if (hand == null || hand.Count == 0) return;
                        ctx.SummonFromHand(hand, side, highestAttack: true, consume: true);
                    }
                };

                return b;
            }
        }

        /// <summary>
        /// 甲虫token统一创建函数 — 基础2/2 + 全局buff叠加。
        /// </summary>
        private static void SummonBeetle(CombatContext ctx, List<CombatUnit> side)
        {
            int atkBonus = ctx.Extra.ContainsKey("_beetleAtkBonus") ? (int)ctx.Extra["_beetleAtkBonus"] : 0;
            int hpBonus = ctx.Extra.ContainsKey("_beetleHpBonus") ? (int)ctx.Extra["_beetleHpBonus"] : 0;
            var beetle = new CombatUnit
            {
                CardId = "BEETLE_TOKEN", NameCn = "甲虫", Tier = 1,
                Attack = 2 + atkBonus, Health = 2 + hpBonus, MaxHealth = 2 + hpBonus,
                Alive = true, Golden = false, Taunt = false, DivineShield = false,
                MegaWindfury = false, Reborn = false
            };
            side.Add(beetle);
        }

        /// <summary>
        /// 内建英雄技能战斗效果注册表 — 从JS CombatEffects._heroPowers 移植。
        /// </summary>
        private static Dictionary<string, HeroPowerEntry> HeroPowerBuiltins
        {
            get
            {
                var hp = new Dictionary<string, HeroPowerEntry>();

                // ══ 优先级英雄技能（Phase 1a: 高于所有随从/饰品）══

                // 伊利丹: 最左最右+2/+1并立即攻击
                hp["TB_BaconShop_HERO_08"] = new HeroPowerEntry
                {
                    Priority = true,
                    Handler = (ctx, side, enemy) =>
                    {
                        if (side.Count > 0 && side[0].Alive)
                        {
                            side[0].Attack += 2;
                            side[0].Health += 1;
                            side[0].MaxHealth += 1;
                        }
                        if (side.Count > 1 && side[side.Count - 1].Alive)
                        {
                            side[side.Count - 1].Attack += 2;
                            side[side.Count - 1].Health += 1;
                            side[side.Count - 1].MaxHealth += 1;
                        }
                    }
                };

                // 塔维什: 战斗开始时召唤3/3 token
                hp["TB_BaconShop_HERO_58"] = new HeroPowerEntry
                {
                    Priority = true,
                    Handler = (ctx, side, enemy) =>
                    {
                        if (side.Count < 7)
                        {
                            ctx.SpawnToken(side, ctx.BuildToken(3, 3, "塔维什弹药", tier: 1));
                        }
                    }
                };

                // ══ 普通英雄技能（Phase 1b）══

                // 奥拉基尔: 最左随从获得风怒+圣盾+嘲讽
                hp["TB_BaconShop_HERO_76"] = new HeroPowerEntry
                {
                    Priority = false,
                    Handler = (ctx, side, enemy) =>
                    {
                        if (side.Count > 0 && side[0].Alive)
                        {
                            side[0].WindfuryAttacksLeft = 2; // 风怒
                            side[0].DivineShield = true;
                            side[0].Taunt = true;
                        }
                    }
                };

                // 死亡之翼: 全体随从永久+2攻击力
                hp["TB_BaconShop_HERO_52"] = new HeroPowerEntry
                {
                    Priority = false,
                    Handler = (ctx, side, enemy) =>
                    {
                        for (int i = 0; i < side.Count; i++)
                        {
                            if (side[i].Alive) side[i].Attack += 2;
                        }
                        for (int j = 0; j < enemy.Count; j++)
                        {
                            if (enemy[j].Alive) enemy[j].Attack += 2;
                        }
                    }
                };

                // 亚煞极: 召唤一个当前酒馆等级的随机随从
                hp["TB_BaconShop_HERO_92"] = new HeroPowerEntry
                {
                    Priority = false,
                    Handler = (ctx, side, enemy) =>
                    {
                        int tier = 1; // 简化: 使用T1 token
                        ctx.SpawnToken(side, ctx.BuildToken(tier * 2, tier * 2, "亚煞极随从", tier: tier));
                    }
                };

                // 瓦托格尔女王: 每个类型各一个友方+1/+1
                hp["TB_BaconShop_HERO_14"] = new HeroPowerEntry
                {
                    Priority = false,
                    Handler = (ctx, side, enemy) =>
                    {
                        var done = new HashSet<string>();
                        for (int i = 0; i < side.Count; i++)
                        {
                            if (!side[i].Alive) continue;
                            var types = side[i].MinionTypes;
                            if (types == null) continue;
                            for (int t = 0; t < types.Count; t++)
                            {
                                if (!done.Contains(types[t]))
                                {
                                    side[i].Attack += 1;
                                    side[i].Health += 1;
                                    side[i].MaxHealth += 1;
                                    done.Add(types[t]);
                                }
                            }
                        }
                    }
                };

                // 塔姆辛: 攻击力最低的随从获得亡语
                hp["BG20_HERO_282"] = new HeroPowerEntry
                {
                    Priority = false,
                    Handler = (ctx, side, enemy) =>
                    {
                        CombatUnit lowest = null;
                        int lowestAtk = int.MaxValue;
                        for (int i = 0; i < side.Count; i++)
                        {
                            if (side[i].Alive && side[i].Attack < lowestAtk)
                            {
                                lowest = side[i];
                                lowestAtk = side[i].Attack;
                            }
                        }
                        if (lowest != null)
                        {
                            // 动态注册亡语
                            var capturedUnit = lowest;
                            Register(lowest.CardId + "_tamsin", new CardHandlers
                            {
                                Deathrattle = (c, u, s, e) =>
                                {
                                    int atkVal = capturedUnit.Attack;
                                    int hpVal = capturedUnit.Health;
                                    for (int j = 0; j < s.Count; j++)
                                    {
                                        if (s[j].Alive && s[j] != capturedUnit)
                                        {
                                            s[j].Attack += atkVal;
                                            s[j].Health += hpVal;
                                            s[j].MaxHealth += hpVal;
                                        }
                                    }
                                }
                            });
                            lowest.HasDeathrattle = true;
                        }
                    }
                };

                // 塔隆血魔: 消灭最左随从，存储复制
                hp["BG25_HERO_103"] = new HeroPowerEntry
                {
                    Priority = false,
                    Handler = (ctx, side, enemy) =>
                    {
                        if (side.Count > 0 && side[0].Alive)
                        {
                            var target = side[0];
                            // 存储副本到Extra(简化)
                            target.Extra["_talonCopy"] = true;
                            target.Alive = false;
                            ctx.OnDeath(target);
                        }
                    }
                };

                // 布鲁坎: 唤起元素光环(简化: 全体+1/+1)
                hp["BG22_HERO_001"] = new HeroPowerEntry
                {
                    Priority = false,
                    Handler = (ctx, side, enemy) =>
                    {
                        for (int i = 0; i < side.Count; i++)
                        {
                            if (side[i].Alive)
                            {
                                side[i].Attack += 1;
                                side[i].Health += 1;
                                side[i].MaxHealth += 1;
                            }
                        }
                    }
                };

                return hp;
            }
        }
    }
}
