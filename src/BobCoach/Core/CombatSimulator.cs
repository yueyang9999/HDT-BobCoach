using System;
using System.Collections.Generic;
using System.Linq;

namespace BobCoach.Engine
{
    public class CombatResult
    {
        public bool PlayerWon;
        public int PlayerSurvivors;
        public int OpponentSurvivors;
        public int DamageDealtToPlayer;
        public int DamageDealtToOpponent;
        public double PlayerWinProb;  // 0-1 简化为 1/0/0.5
    }

    /// <summary>
    /// 轻量战斗模拟器 — 事件队列驱动版本 (v3.0)。
    /// 基于关键词系统（圣盾/复生/风怒/超级风怒/潜行/烈毒/嘲讽/顺劈/超杀/复仇/进击）
    /// 做逐随从攻击循环，预测胜负和伤害。
    /// 卡牌特定效果通过 CombatEffects 注册表驱动（亡语/光环/受伤时/复仇/战斗开始时/挤爆）。
    ///
    /// S13规则: 存活多的一方先攻 → 存活相同则酒馆等级高先攻 → 等级相同随机。
    /// 同时伤害: 攻击方和目标同时造成攻击力数值的伤害（防御方反击）。
    /// 站位保持: 保持玩家布置的站位，攻击从左到右按原始顺序。
    /// 嘲讽随机: 从所有存活嘲讽中随机选择攻击目标。
    /// 顺劈(Cleave): 主目标正常同时伤害, 相邻目标只受伤害不反击。
    /// 超杀(Overkill): 攻击消灭目标后, 多余伤害溅射相邻随从。
    /// 复仇(Avenge): 通过CombatEffects注册表驱动事件队列。
    /// 事件触发顺序: 受伤时 > 亡语 > 光环修正 > 复生 > 复仇。
    /// </summary>
    public class CombatSimulator
    {
        private const int MaxAttacks = 200;
        private static readonly Random _rng = new Random();

        public CombatSimulator()
        {
            // CombatEffects 为静态注册表，无需显式加载
        }

        /// <summary>
        /// 模拟玩家场面 vs 对手场面的一场战斗。
        /// 签名保持向后兼容 DecisionEngine 所有调用点。
        /// </summary>
        public CombatResult Simulate(List<MinionData> playerBoard, List<MinionData> opponentBoard,
            int playerTier = 1, int opponentTier = 1, int turn = 1, int alivePlayerCount = 8,
            string playerHeroCardId = null, string opponentHeroCardId = null,
            List<MinionData> playerHand = null, List<MinionData> opponentHand = null)
        {
            var result = new CombatResult();

            if (playerBoard == null || playerBoard.Count == 0)
            {
                result.PlayerWon = false;
                result.DamageDealtToPlayer = opponentTier + (opponentBoard != null
                    ? opponentBoard.Sum(m => m.Tier) : 0);
                return result;
            }
            if (opponentBoard == null || opponentBoard.Count == 0)
            {
                result.PlayerWon = true;
                result.DamageDealtToOpponent = playerTier + playerBoard.Sum(m => m.Tier);
                result.PlayerSurvivors = playerBoard.Count;
                return result;
            }

            var atkUnits = BuildUnits(playerBoard);
            var defUnits = BuildUnits(opponentBoard);

            // ── 构建战斗上下文(事件队列+辅助方法) ──
            var ctx = new CombatContext(atkUnits, defUnits, _rng, playerHand, opponentHand);

            // 注入英雄技能处理器
            if (!string.IsNullOrEmpty(playerHeroCardId))
            {
                var hpEntry = CombatEffects.GetHeroPower(playerHeroCardId);
                if (hpEntry != null)
                {
                    if (hpEntry.Priority)
                        ctx.AttackerPriorityHeroPower = hpEntry.Handler;
                    else
                        ctx.AttackerHeroPower = hpEntry.Handler;
                }
            }
            if (!string.IsNullOrEmpty(opponentHeroCardId))
            {
                var hpEntry = CombatEffects.GetHeroPower(opponentHeroCardId);
                if (hpEntry != null)
                {
                    if (hpEntry.Priority)
                        ctx.DefenderPriorityHeroPower = hpEntry.Handler;
                    else
                        ctx.DefenderHeroPower = hpEntry.Handler;
                }
            }

            // ── S13 攻击先手判定: 存活多的一方先攻 → 存活相同则酒馆等级高先攻 → 随机 ──
            int playerAlive = atkUnits.Count(u => u.Alive);
            int opponentAlive = defUnits.Count(u => u.Alive);

            List<CombatUnit> strikeTeam, receiveTeam;
            if (playerAlive > opponentAlive)
            {
                strikeTeam = atkUnits; receiveTeam = defUnits;
            }
            else if (opponentAlive > playerAlive)
            {
                strikeTeam = defUnits; receiveTeam = atkUnits;
            }
            else if (playerTier > opponentTier)
            {
                strikeTeam = atkUnits; receiveTeam = defUnits;
            }
            else if (opponentTier > playerTier)
            {
                strikeTeam = defUnits; receiveTeam = atkUnits;
            }
            else
            {
                strikeTeam = _rng.Next(2) == 0 ? atkUnits : defUnits;
                receiveTeam = strikeTeam == atkUnits ? defUnits : atkUnits;
            }

            // ── Phase 1: 战斗开始时 — 英雄技能优先 → 随从交替左→右 → 饰品 ──
            PhaseStartOfCombat(atkUnits, defUnits, ctx);
            ctx.ProcessEvents();

            // ── Phase 2: 交替攻击循环 — 每轮攻击后处理事件队列 ──
            int attacks = 0;
            while (HasAlive(strikeTeam) && HasAlive(receiveTeam) && attacks < MaxAttacks)
            {
                DoAttack(strikeTeam, receiveTeam, ctx);
                ctx.ProcessEvents();
                if (!HasAlive(receiveTeam)) break;

                DoAttack(receiveTeam, strikeTeam, ctx);
                ctx.ProcessEvents();
                if (!HasAlive(strikeTeam)) break;
                attacks++;
            }

            // 最终事件处理
            ctx.ProcessEvents();

            int atkSurvivors = atkUnits.Count(u => u.Alive);
            int defSurvivors = defUnits.Count(u => u.Alive);
            bool playerWon = atkSurvivors > 0 && defSurvivors == 0;

            result.PlayerWon = playerWon;
            result.PlayerSurvivors = atkSurvivors;
            result.OpponentSurvivors = defSurvivors;
            result.PlayerWinProb = playerWon ? 1.0 : (defSurvivors > 0 && atkSurvivors > 0 ? 0.5 : 0.0);

            // 伤害 = 酒馆等级 + 存活随从星级之和
            if (!playerWon)
            {
                int rawDamage = opponentTier + defUnits.Where(u => u.Alive).Sum(u => u.Tier);
                result.DamageDealtToPlayer = ApplyDamageCap(rawDamage, turn, alivePlayerCount);
            }
            else
            {
                int rawDamage = playerTier + atkUnits.Where(u => u.Alive).Sum(u => u.Tier);
                result.DamageDealtToOpponent = ApplyDamageCap(rawDamage, turn, alivePlayerCount);
            }

            return result;
        }

        /// <summary>
        /// 快速评估：给定场面，预测对随机对手的胜率和期望伤害。
        /// 用于 DecisionEngine 的"升本风险评估"。
        /// </summary>
        public CombatResult EstimateAgainstAverage(List<MinionData> playerBoard,
            int playerTier, float avgOpponentPower, int opponentTier)
        {
            var estOpponent = new List<MinionData>();
            int cardCount = Math.Min(7, Math.Max(1, (int)(avgOpponentPower / 0.5f)));
            for (int i = 0; i < cardCount; i++)
            {
                estOpponent.Add(new MinionData
                {
                    CardId = "est_opp_" + i,
                    Tier = opponentTier,
                    Attack = (int)(avgOpponentPower * 2 + 1),
                    Health = (int)(avgOpponentPower * 2 + 2),
                });
            }
            return Simulate(playerBoard, estOpponent, playerTier, opponentTier);
        }

        // ── 内部实现 ──

        /// <summary>从MinionData构建单个CombatUnit（供CombatContext.SummonFromHand等调用）。</summary>
        public static CombatUnit BuildUnitStatic(MinionData m)
        {
            if (m == null) return null;
            int mul = m.Golden ? 2 : 1;
            var ceHandlers = CombatEffects.GetHandlers(m.CardId);
            bool hasDeathrattle = m.Reborn || (ceHandlers != null && ceHandlers.Deathrattle != null);
            bool hasStartOfCombat = ceHandlers != null && ceHandlers.StartOfCombat != null;
            bool hasAura = ceHandlers != null && ceHandlers.Aura != null;
            bool hasAvenge = m.AvengeCount > 0 || (ceHandlers != null && ceHandlers.Avenge != null);
            bool hasWhenDamaged = ceHandlers != null && ceHandlers.WhenDamaged != null;
            bool hasRally = ceHandlers != null && ceHandlers.Rally != null;

            int windfuryAttacks = 1;
            if (m.MegaWindfury) windfuryAttacks = 4;
            else if (m.Windfury) windfuryAttacks = 2;

            bool hasCleave = m.Cleave
                || (m.CardName?.Contains("狂战") == true)
                || (m.CardName?.Contains("顺劈") == true)
                || (m.CardText?.Contains("相邻") == true)
                || (m.CardText?.Contains("两侧") == true);

            bool hasOverkill = m.Overkill
                || (m.CardText?.Contains("超过目标生命值") == true)
                || (m.CardName?.Contains("野火") == true);

            var tribes = new List<string>();
            if (!string.IsNullOrEmpty(m.Tribe))
            {
                foreach (var p in m.Tribe.Split(','))
                {
                    var trimmed = p.Trim();
                    if (!string.IsNullOrEmpty(trimmed) && !tribes.Contains(trimmed))
                        tribes.Add(trimmed);
                }
            }

            int drCount = hasDeathrattle ? (m.Golden ? 2 : 1) : 0;

            return new CombatUnit
            {
                CardId = m.CardId,
                NameCn = m.CardName,
                Attack = m.Attack * mul,
                Health = m.Health * mul,
                MaxHealth = m.Health * mul,
                BaseAttack = m.Attack * mul,
                BaseHealth = m.Health * mul,
                DivineShield = m.DivineShield,
                Reborn = m.Reborn,
                Poisonous = m.Poisonous || m.Venomous,
                Taunt = m.Taunt,
                Tier = m.Tier,
                Alive = true,
                Golden = m.Golden,
                DeathrattleCount = drCount,
                DeathrattleTriggered = false,
                WindfuryAttacksLeft = windfuryAttacks,
                MegaWindfury = m.MegaWindfury,
                Stealth = m.Stealth,
                Stealthed = m.Stealth,
                Cleave = hasCleave,
                Overkill = hasOverkill,
                AvengeCount = m.AvengeCount,
                AvengeTriggered = false,
                DeathCountWitnessed = 0,
                HasDeathrattle = hasDeathrattle,
                HasStartOfCombat = hasStartOfCombat,
                HasAura = hasAura,
                HasAvenge = hasAvenge,
                HasWhenDamaged = hasWhenDamaged,
                HasRally = hasRally,
                MinionTypes = tribes,
                Position = 0,
            };
        }

        private List<CombatUnit> BuildUnits(List<MinionData> minions)
        {
            var units = new List<CombatUnit>();
            for (int i = 0; i < minions.Count; i++)
            {
                var unit = BuildUnitStatic(minions[i]);
                if (unit != null)
                {
                    unit.Position = i;
                    units.Add(unit);
                }
            }
            return units;
        }

        private bool HasAlive(List<CombatUnit> units)
        {
            return units.Any(u => u.Alive);
        }

        /// <summary>
        /// 一次攻击。攻击方按原始站位从左到右取第一个存活随从攻击；
        /// 同时伤害：双方同时造成攻击力数值的伤害（防御方反击）。
        /// 支持顺劈(Cleave)/超杀(Overkill)/风怒(Windfury)/进击(Rally)循环。
        /// 死亡/亡语/复生/受伤时通过 CombatContext 事件队列处理。
        /// </summary>
        private void DoAttack(List<CombatUnit> attackers, List<CombatUnit> defenders, CombatContext ctx)
        {
            // 按原始站位从左到右取第一个存活随从
            var attacker = attackers.FirstOrDefault(u => u.Alive);
            if (attacker == null) return;

            // 潜行随从主动攻击后显形
            if (attacker.Stealthed) attacker.Stealthed = false;

            // ── 进击(Rally): 先远程伤害再近战 ──
            if (attacker.HasRally && attacker.Alive)
            {
                var target = SelectTarget(defenders);
                if (target == null) return;
                var rallyCEH = CombatEffects.GetHandlers(attacker.CardId);
                if (rallyCEH != null && rallyCEH.Rally != null)
                {
                    rallyCEH.Rally(ctx, attacker, attackers, defenders, target);
                }
                else
                {
                    // 通用进击: 远程造成攻击力伤害
                    int rallyDmg = attacker.Attack;
                    if (target.DivineShield)
                        target.DivineShield = false;
                    else
                    {
                        target.Health -= rallyDmg;
                        if (target.Health <= 0)
                        {
                            target.KilledBy = attacker;
                            target.Alive = false;
                            ctx.OnDeath(target);
                        }
                    }
                }
                // 目标被进击打死 → 跳过近战攻击
                if (!target.Alive) return;
            }

            // 风怒/超级风怒: 攻击 WindfuryAttacksLeft 次
            while (attacker.WindfuryAttacksLeft > 0 && attacker.Alive && HasAlive(defenders))
            {
                var target = SelectTarget(defenders);
                if (target == null) break;

                // ── 顺劈分支: 主目标正常结算，相邻只受伤不反击 ──
                if (attacker.Cleave)
                {
                    ExecuteCleave(attacker, target, attackers, defenders, ctx);
                }
                else
                {
                    int defHpBefore = target.Health;
                    ExecuteSingleHit(attacker, target, attackers, defenders, ctx);

                    // ── 超杀分支: 消灭目标后多余伤害溅射相邻 ──
                    if (attacker.Overkill && !target.Alive && defHpBefore > 0)
                    {
                        int excess = Math.Max(1, attacker.Attack - defHpBefore);
                        int tIdx = defenders.IndexOf(target);
                        if (tIdx > 0 && defenders[tIdx - 1].Alive)
                            ApplySplashDamage(defenders[tIdx - 1], excess, defenders, ctx);
                        if (tIdx < defenders.Count - 1 && defenders[tIdx + 1].Alive)
                            ApplySplashDamage(defenders[tIdx + 1], excess, defenders, ctx);
                    }
                }

                attacker.WindfuryAttacksLeft--;
            }
        }

        /// <summary>
        /// 单次同时伤害结算。攻击方和目标同时造成攻击力数值的伤害。
        /// 死亡通过 ctx.OnDeath 统一入队处理。
        /// </summary>
        private void ExecuteSingleHit(CombatUnit attacker, CombatUnit target,
            List<CombatUnit> attackers, List<CombatUnit> defenders, CombatContext ctx)
        {
            bool atkHadDS = attacker.DivineShield;
            bool defHadDS = target.DivineShield;
            bool defTookDmg = false;
            bool atkTookDmg = false;

            // 1. 攻击方→目标伤害
            if (defHadDS)
                target.DivineShield = false;
            else
            {
                if (attacker.Poisonous && target.Health > 0)
                    target.Health = 0;
                else
                    target.Health -= attacker.Attack;
                defTookDmg = true;
            }

            // 2. 目标→攻击方伤害（同时！防御方反击）
            if (target.Alive)
            {
                if (atkHadDS)
                    attacker.DivineShield = false;
                else
                {
                    if (target.Poisonous && attacker.Health > 0)
                        attacker.Health = 0;
                    else
                        attacker.Health -= target.Attack;
                    atkTookDmg = true;
                }
            }

            // 3. 检查死亡 + 记录击杀者
            bool defDied = target.Health <= 0;
            bool atkDied = attacker.Health <= 0;
            if (defDied) target.KilledBy = attacker;
            if (atkDied) attacker.KilledBy = target;

            // 4. 通过 CombatContext 事件队列处理死亡/亡语/复生/受伤时
            if (defDied)
            {
                target.Alive = false;
                ctx.OnDeath(target);
            }
            if (atkDied)
            {
                attacker.Alive = false;
                ctx.OnDeath(attacker);
            }
            if (defTookDmg && target.Alive)
            {
                ctx.TriggerWhenDamaged(target);
            }
            if (atkTookDmg && attacker.Alive)
            {
                ctx.TriggerWhenDamaged(attacker);
            }
        }

        /// <summary>
        /// 顺劈攻击: 主目标正常同时伤害; 相邻目标只受伤害不反击。
        /// 相邻目标受击时可触发烈毒(Poisonous)效果。
        /// </summary>
        private void ExecuteCleave(CombatUnit attacker, CombatUnit primaryTarget,
            List<CombatUnit> attackers, List<CombatUnit> defenders, CombatContext ctx)
        {
            // 主目标: 正常同时伤害结算
            ExecuteSingleHit(attacker, primaryTarget, attackers, defenders, ctx);

            // 相邻目标: 只受伤害不反击
            int targetIdx = defenders.IndexOf(primaryTarget);
            var splashTargets = new List<CombatUnit>();
            if (targetIdx > 0) splashTargets.Add(defenders[targetIdx - 1]);
            if (targetIdx < defenders.Count - 1) splashTargets.Add(defenders[targetIdx + 1]);

            foreach (var st in splashTargets)
            {
                if (!st.Alive || !attacker.Alive) continue;
                if (st.DivineShield)
                    st.DivineShield = false;
                else
                {
                    st.Health -= attacker.Attack;
                    if (attacker.Poisonous && st.Health > 0)
                        st.Health = 0;
                }

                if (st.Health <= 0)
                {
                    st.KilledBy = attacker;
                    st.Alive = false;
                    ctx.OnDeath(st);
                }
            }
        }

        /// <summary>
        /// 溅射伤害: 圣盾先吸收, 否则扣血, 随后死亡结算。
        /// 用于超杀(Overkill)多余伤害溅射相邻目标。
        /// </summary>
        private void ApplySplashDamage(CombatUnit target, int damage, List<CombatUnit> unitList,
            CombatContext ctx)
        {
            if (!target.Alive) return;
            if (target.DivineShield)
                target.DivineShield = false;
            else
                target.Health -= damage;

            if (target.Health <= 0)
            {
                target.Alive = false;
                ctx.OnDeath(target);
            }
        }

        /// <summary>
        /// 选择攻击目标: 优先嘲讽(随机选)，排除未显形潜行，无嘲讽则随机选。
        /// </summary>
        private CombatUnit SelectTarget(List<CombatUnit> defenders)
        {
            // 过滤未显形潜行随从（除非场上所有存活随从都是潜行状态）
            var visible = defenders.Where(u => u.Alive && !u.Stealthed).ToList();
            if (visible.Count == 0)
                visible = defenders.Where(u => u.Alive).ToList();
            if (visible.Count == 0) return null;

            // 从所有存活嘲讽中随机选择
            var taunts = visible.Where(u => u.Taunt).ToList();
            if (taunts.Count > 0)
                return taunts[_rng.Next(taunts.Count)];

            // 无嘲讽则随机选择
            return visible[_rng.Next(visible.Count)];
        }

        // ── Phase 1: Start of Combat ──

        /// <summary>
        /// 战斗开始时阶段: 优先级英雄技能 → 普通英雄技能 → 随从交替左→右 → 饰品。
        /// </summary>
        private void PhaseStartOfCombat(List<CombatUnit> atkUnits, List<CombatUnit> defUnits, CombatContext ctx)
        {
            // Phase 1a: 优先级英雄技能（伊利丹、塔维什 — 高于所有随从/饰品）
            ProcessHeroStartOfCombat(atkUnits, defUnits, ctx, true);
            ProcessHeroStartOfCombat(defUnits, atkUnits, ctx, false, true);

            // Phase 1b: 普通英雄技能
            ProcessHeroStartOfCombat(atkUnits, defUnits, ctx, false);
            ProcessHeroStartOfCombat(defUnits, atkUnits, ctx, true);

            // Phase 1c: 收集有 START_OF_COMBAT 的随从，按交替左→右排序
            var socAtk = atkUnits.Where(u => u.Alive && u.HasStartOfCombat).ToList();
            var socDef = defUnits.Where(u => u.Alive && u.HasStartOfCombat).ToList();

            // Phase 1d: 交替结算随从战斗开始时效果
            int maxLen = Math.Max(socAtk.Count, socDef.Count);
            for (int i = 0; i < maxLen; i++)
            {
                if (i < socAtk.Count)
                    ExecuteStartOfCombat(socAtk[i], atkUnits, defUnits, ctx);
                if (i < socDef.Count)
                    ExecuteStartOfCombat(socDef[i], defUnits, atkUnits, ctx);
            }

            // Phase 1d.5: 手牌START_OF_COMBAT（好斗的斥候等 — 从手牌触发）
            // C#侧仅通过CombatEffects注册检测（MinionData无Mechanics字段）
            var socHandAtk = new List<MinionData>();
            if (ctx.AttackerHand != null)
            {
                foreach (var hm in ctx.AttackerHand)
                {
                    var hmCEH = CombatEffects.GetHandlers(hm.CardId);
                    if (hmCEH != null && hmCEH.StartOfCombat != null)
                        socHandAtk.Add(hm);
                }
            }
            var socHandDef = new List<MinionData>();
            if (ctx.DefenderHand != null)
            {
                foreach (var hm in ctx.DefenderHand)
                {
                    var hmCEH = CombatEffects.GetHandlers(hm.CardId);
                    if (hmCEH != null && hmCEH.StartOfCombat != null)
                        socHandDef.Add(hm);
                }
            }
            int maxHandLen = Math.Max(socHandAtk.Count, socHandDef.Count);
            for (int i = 0; i < maxHandLen; i++)
            {
                if (i < socHandAtk.Count)
                {
                    var hhAtk = CombatEffects.GetHandlers(socHandAtk[i].CardId);
                    if (hhAtk != null && hhAtk.StartOfCombat != null)
                        hhAtk.StartOfCombat(ctx, atkUnits, defUnits);
                }
                if (i < socHandDef.Count)
                {
                    var hhDef = CombatEffects.GetHandlers(socHandDef[i].CardId);
                    if (hhDef != null && hhDef.StartOfCombat != null)
                        hhDef.StartOfCombat(ctx, defUnits, atkUnits);
                }
            }

            // Phase 1e: 饰品战斗效果
            if (ctx.AttackerTrinketHandlers != null)
            {
                foreach (var handler in ctx.AttackerTrinketHandlers)
                    handler(ctx, atkUnits, defUnits);
            }
            if (ctx.DefenderTrinketHandlers != null)
            {
                foreach (var handler in ctx.DefenderTrinketHandlers)
                    handler(ctx, defUnits, atkUnits);
            }
        }

        /// <summary>
        /// 处理英雄战斗开始时技能。
        /// </summary>
        private void ProcessHeroStartOfCombat(List<CombatUnit> myUnits, List<CombatUnit> enemyUnits,
            CombatContext ctx, bool isDefender, bool isPriority = false)
        {
            var handler = isDefender
                ? (isPriority ? ctx.DefenderPriorityHeroPower : ctx.DefenderHeroPower)
                : (isPriority ? ctx.AttackerPriorityHeroPower : ctx.AttackerHeroPower);

            if (handler != null)
            {
                handler(ctx, myUnits, enemyUnits);
            }
        }

        /// <summary>
        /// 执行单个随从的战斗开始时效果(通过CombatEffects注册表驱动)。
        /// </summary>
        private void ExecuteStartOfCombat(CombatUnit unit, List<CombatUnit> ownSide,
            List<CombatUnit> enemySide, CombatContext ctx)
        {
            if (!unit.Alive) return;
            unit.StartOfCombatTriggered = true;
            var handlers = CombatEffects.GetHandlers(unit.CardId);
            if (handlers != null && handlers.StartOfCombat != null)
            {
                handlers.StartOfCombat(ctx, ownSide, enemySide);
            }
        }

        /// <summary>
        /// 单次攻击(不含风怒/超风循环), 用于 Start of Combat 阶段的英雄技能攻击。
        /// 同时伤害: 双方同时造成攻击力数值的伤害。
        /// </summary>
        private void DoAttackSingle(CombatUnit attacker, List<CombatUnit> attackerSide,
            List<CombatUnit> defenderSide, CombatContext ctx)
        {
            var target = SelectTarget(defenderSide);
            if (target == null) return;

            bool atkHadDS = attacker.DivineShield;
            bool defHadDS = target.DivineShield;
            bool defTookDmg = false;
            bool atkTookDmg = false;

            if (defHadDS)
                target.DivineShield = false;
            else
            {
                target.Health -= attacker.Attack;
                if (attacker.Poisonous && target.Health > 0)
                    target.Health = 0;
                defTookDmg = true;
            }

            if (target.Alive)
            {
                if (atkHadDS)
                    attacker.DivineShield = false;
                else
                {
                    attacker.Health -= target.Attack;
                    if (target.Poisonous && attacker.Health > 0)
                        attacker.Health = 0;
                    atkTookDmg = true;
                }
            }

            bool defDied = target.Health <= 0;
            bool atkDied = attacker.Health <= 0;
            if (defDied) { target.KilledBy = attacker; target.Alive = false; ctx.OnDeath(target); }
            if (atkDied) { attacker.KilledBy = target; attacker.Alive = false; ctx.OnDeath(attacker); }

            if (defTookDmg && target.Alive) ctx.TriggerWhenDamaged(target);
            if (atkTookDmg && attacker.Alive) ctx.TriggerWhenDamaged(attacker);
        }

        /// <summary>
        /// S13 伤害上限规则。
        /// T1-T3: 最大伤害 5; T4-T7: 最大伤害 10; T8+: 最大伤害 15。
        /// 存活玩家 ≤ 4 时无上限。
        /// </summary>
        private static int ApplyDamageCap(int rawDamage, int turn, int alivePlayerCount)
        {
            if (alivePlayerCount <= 4) return rawDamage;
            if (turn <= 3) return Math.Min(rawDamage, 5);
            if (turn <= 7) return Math.Min(rawDamage, 10);
            return Math.Min(rawDamage, 15);
        }
    }
}
