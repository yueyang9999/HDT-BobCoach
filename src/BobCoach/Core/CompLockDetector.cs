using System;
using System.Collections.Generic;

namespace BobCoach.Engine
{
    public enum LockState { None, Soft, Hard }

    public class CompLockResult
    {
        public LockState State;
        public string DominantTribe = "";
        public float Confidence;
        public Dictionary<string, float> TribeWeights = new Dictionary<string, float>();
        public string Archetype = "";
        public string Reason = "";
    }

    /// <summary>
    /// 阵容锁定检测器。多信号融合判断玩家是否已锁定流派方向，
    /// 输出偏好向量供 DecisionEngine 在购买/升本决策中使用。
    /// </summary>
    public class CompLockDetector
    {
        // 信号权重
        private const float W_TRIBE_COUNT = 0.35f;
        private const float W_ENGINE_CARD = 0.25f;
        private const float W_HERO_MATCH = 0.20f;
        private const float W_TURN = 0.10f;
        private const float W_BOARD_RATIO = 0.10f;

        // Soft/Hard 阈值
        private const float SOFT_THRESHOLD = 0.35f;
        private const float HARD_THRESHOLD = 0.65f;

        // 已知引擎卡 → 对应流派（CardId → Tribe）
        private static readonly Dictionary<string, string> EngineCardTribe = new Dictionary<string, string>
        {
            // 野兽 — 跳蛙流/亡语流
            { "BG25_012", "BEAST" },  // 跳蛙骑士 (deathrattle amp)
            { "BG20_201", "BEAST" },  // 巨狼鹦鹉 (trigger battlecry)
            { "BGS_018",  "BEAST" },  // 食腐鬣狗
            { "BG21_040", "BEAST" },  // 鸟类的伙伴
            { "BG22_401", "BEAST" },  // 香蕉猛击者
            // 机械 — 偏折/滑油流
            { "BG20_402", "MECHANICAL" }, // 偏折机器人 (divine shield reset)
            { "BG21_046", "MECHANICAL" }, // 滑油机器人 (stat scale)
            { "BGS_032",  "MECHANICAL" }, // 钴制卫士
            { "BG25_022", "MECHANICAL" }, // 欧米伽毁灭者
            { "BG22_202", "MECHANICAL" }, // 机械金刚
            // 龙 — 卡雷/战斗号角流
            { "BG23_019", "DRAGON" },  // 卡雷苟斯 (dragon scale)
            { "BG21_038", "DRAGON" },  // 普瑞斯托的火花
            { "BG25_040", "DRAGON" },  // 闪鳞龙兽
            { "BG20_301", "DRAGON" },  // 拉佐格尔
            // 元素 — 诺米/巴琳达流
            { "BG20_806", "ELEMENTAL" }, // 诺米 (elemental scale)
            { "BG35_883", "ELEMENTAL" }, // 巴琳达 (double spell)
            { "BG23_014", "ELEMENTAL" }, // 回收者
            { "BG21_042", "ELEMENTAL" }, // 飓风元素
            // 鱼人 — 铜须战吼流
            { "BG_ICC_026", "MURLOC" },  // 铜须 (battlecry double)
            { "BGS_010",    "MURLOC" },  // 蛮鱼斥候
            { "BG20_501",   "MURLOC" },  // 毒鳍鱼人
            { "BG24_049",   "MURLOC" },  // 巴斯鱼人
            // 海盗 — 舰长流
            { "BG25_155", "PIRATE" },    // 海上劫掠者 (pirate buff)
            { "BG22_801", "PIRATE" },    // 海盗船工
            { "BG23_015", "PIRATE" },    // 佩吉·布特雷斯
            { "BG27_537", "PIRATE" },    // 赏金猎人
            // 亡灵 — 永恒骑士流
            { "BG26_801", "UNDEAD" },    // 永恒骑士 (undead scale)
            { "BG26_354", "UNDEAD" },    // 枯萎收割者
            { "BG26_803", "UNDEAD" },    // 亡语者姐妹
            { "BG28_801", "UNDEAD" },    // 死亡使者
            // 野猪人 — 宝石流
            { "BG24_047", "QUILBOAR" },  // 荆棘野猪人 (quilboar gem)
            { "BG24_048", "QUILBOAR" },  // 宝石切割者
            { "BG25_051", "QUILBOAR" },  // 野猪宝石大师
            { "BG27_048", "QUILBOAR" },  // 坚牙野猪人
            // 纳迦 — 法术流
            { "BG23_016", "NAGA" },      // 沙锤 (naga spell)
            { "BG23_017", "NAGA" },      // 海潮女巫
            { "BG24_041", "NAGA" },      // 深海雕刻者
            { "BG24_042", "NAGA" },      // 闪鳞纳迦
            // 恶魔 — 吞噬流
            { "BG24_025", "DEMON" },     // 饥饿的魔蝠 (demon consume)
            { "BG24_026", "DEMON" },     // 深渊召唤者
            { "BG25_030", "DEMON" },     // 邪能领主
            { "BG26_035", "DEMON" },     // 虚空恐魔
        };

        // 英雄→流派天然匹配 (HeroCardId 子串 → Tribe)
        // v1.5: 扩充到完整P0硬锁定英雄列表 (来源: hero_tribe_affinity.json)
        private static readonly Dictionary<string, string> HeroTribeAffinity = new Dictionary<string, string>
        {
            // 龙系英雄 (P0: 技能仅产出龙)
            { "HERO_53", "DRAGON" },  // 伊瑟拉 — 刷新额外提供龙
            { "HERO_56", "DRAGON" },  // 阿莱克丝塔萨 — 发现龙牌
            { "HERO_305", "DRAGON" }, // 奥妮克希亚 — 召唤雏龙
            { "HERO_58", "DRAGON" },  // 玛里苟斯 (fix: was 48)  // 玛里苟斯 — 随从变龙

            // 机械系英雄 (P0)
            { "HERO_17", "MECHANICAL" }, // 米尔菲丝 — 发现磁力机械
            { "HERO_200", "MECHANICAL" }, // 伊妮·积雷 — 获取机械牌
            { "HERO_802", "MECHANICAL" }, // 阿塔尼斯 — 星灵机械

            // 鱼人系英雄 (P0)
            { "HERO_55", "MURLOC" },  // 菌菇术士弗洛格尔 — 出售后获取鱼人
            { "HERO_93", "MURLOC" },  // 恩佐斯 (fix: was 94)  // 恩佐斯 — 开局鱼人
            { "HERO_75", "MURLOC" },  // 拉卡尼休 — 统计依赖鱼人

            // 海盗系英雄 (P0)
            { "HERO_18", "PIRATE" },  // 帕奇斯 — 获取海盗
            { "HERO_101", "PIRATE" }, // 霍格船长 — 购买海盗+1铸币

            // 元素系英雄 (P0)
            { "HERO_78", "ELEMENTAL" }, // 齐恩瓦拉 — 元素牌升本减费
            { "BG22_HERO_001", "ELEMENTAL" }, // 布鲁坎 (精确匹配, 避免子串碰撞) // 布鲁坎 — 选择元素唤起

            // 纳迦系英雄 (P0)
            { "HERO_007", "NAGA" },  // 艾萨拉女王 — 发现纳迦
            { "HERO_304", "NAGA" },  // 瓦丝琪女士 — 发现纳迦

            // 亡灵系英雄 (P0)
            { "HERO_702", "UNDEAD" }, // 典狱长 — 消灭亡灵获取亡灵
            { "HERO_100", "UNDEAD" }, // 普崔塞德教授 — 制造亡灵; +洛卡拉统计依赖

            // 恶魔系英雄
            { "HERO_37", "DEMON" },  // 加拉克苏斯大王 (fix: was 35)  // 加拉克苏斯大王

            // 野猪人系英雄
            { "HERO_103", "QUILBOAR" }, // 亡语者布莱克松 — 鲜血宝石
            { "HERO_800", "QUILBOAR" }, // 泰瑟兰 — 统计依赖野猪人

            // 野兽系英雄 (P2: 策略关联)
            { "HERO_38", "BEAST" },  // 穆克拉
            { "HERO_52", "BEAST" },  // 死亡之翼 (fix: was 50)  // 死亡之翼
        };

        private CardSemanticsData GetCardData(string cardId)
        {
            if (string.IsNullOrEmpty(cardId) || _semanticSource == null) return null;
            CardSemanticsData semantics;
            try { return _semanticSource.TryGet(cardId, out semantics) ? semantics : null; }
            catch { return null; }
        }

        private ICardSemanticSource _semanticSource;

        internal void SetCardSemanticSource(ICardSemanticSource source)
        {
            _semanticSource = source;
        }

        /// <summary>
        /// 检测当前场面阵容锁定状态。
        /// </summary>
        public CompLockResult Detect(GameState state)
        {
            var result = new CompLockResult();

            if (state == null || state.BoardMinions == null || state.BoardMinions.Count < 2)
            {
                result.State = LockState.None;
                result.Reason = "场面随从不足";
                result.TribeWeights = GetDefaultWeights();
                return result;
            }

            // 信号1: 同族随从统计
            var tribeCounts = CountTribes(state.BoardMinions);
            int maxSameTribe = 0;
            string dominantTribe = "";
            foreach (var kv in tribeCounts)
            {
                if (kv.Value > maxSameTribe)
                {
                    maxSameTribe = kv.Value;
                    dominantTribe = kv.Key;
                }
            }
            float tribeSignal = Math.Min(1f, maxSameTribe / 5f);

            // 信号2: 核心引擎卡检测
            int engineCards = 0;
            string engineTribe = "";
            foreach (var m in state.BoardMinions)
            {
                if (string.IsNullOrEmpty(m.CardId)) continue;
                string tribe;
                if (EngineCardTribe.TryGetValue(m.CardId, out tribe))
                {
                    engineCards++;
                    if (string.IsNullOrEmpty(engineTribe)) engineTribe = tribe;
                }
                else
                {
                    // 通过本机事实派生provider检测
                    var data = GetCardData(m.CardId);
                    if (data != null && data.ProvidesMechanics.Count > 0)
                    {
                        engineCards++;
                        if (string.IsNullOrEmpty(engineTribe) && !string.IsNullOrEmpty(m.Tribe))
                            engineTribe = m.Tribe;
                    }
                }
            }
            float engineSignal = Math.Min(1f, engineCards / 2f);

            // 信号3: 英雄-流派匹配
            float heroSignal = 0f;
            string heroTribe = "";
            if (!string.IsNullOrEmpty(state.HeroCardId))
            {
                foreach (var kv in HeroTribeAffinity)
                {
                    if (state.HeroCardId.Contains(kv.Key))
                    {
                        heroSignal = 0.6f;
                        heroTribe = kv.Value;
                        break;
                    }
                }
                // 如果英雄部落与场面主部落一致 → 强匹配
                if (!string.IsNullOrEmpty(heroTribe) && heroTribe == dominantTribe)
                    heroSignal = 1.0f;
            }

            // 信号4: 回合数
            float turnSignal = 0f;
            if (state.Turn >= 9) turnSignal = 1.0f;
            else if (state.Turn >= 6) turnSignal = 0.5f;
            else if (state.Turn >= 4) turnSignal = 0.2f;

            // 信号5: 场面同族占比
            int totalMinions = state.BoardMinions.Count;
            float ratioSignal = totalMinions > 0 ? (float)maxSameTribe / totalMinions : 0f;

            // 融合
            float score = tribeSignal * W_TRIBE_COUNT
                        + engineSignal * W_ENGINE_CARD
                        + heroSignal * W_HERO_MATCH
                        + turnSignal * W_TURN
                        + ratioSignal * W_BOARD_RATIO;

            result.Confidence = score;
            result.DominantTribe = dominantTribe;
            result.Archetype = DetectArchetype(state, dominantTribe);

            if (score >= HARD_THRESHOLD)
            {
                result.State = LockState.Hard;
                result.Reason = string.Format("硬锁: {0}流 (同族{1} + 引擎{2})",
                    dominantTribe, maxSameTribe, engineCards);
            }
            else if (score >= SOFT_THRESHOLD)
            {
                result.State = LockState.Soft;
                result.Reason = string.Format("软锁: {0}向 (同族{1})", dominantTribe, maxSameTribe);
            }
            else
            {
                result.State = LockState.None;
                result.Reason = "未锁定方向";
            }

            // 生成流派权重向量
            result.TribeWeights = BuildTribeWeights(tribeCounts, dominantTribe, result.State, heroTribe);

            return result;
        }

        private Dictionary<string, int> CountTribes(List<MinionData> minions)
        {
            var counts = new Dictionary<string, int>();
            foreach (var m in minions)
            {
                if (string.IsNullOrEmpty(m.Tribe)) continue;
                foreach (var t in MinionData.GetTribesArray(m.Tribe))
                {
                    int c;
                    counts.TryGetValue(t, out c);
                    counts[t] = c + 1;
                }
            }
            return counts;
        }

        /// <summary>
        /// 流派大类检测: TOKEN / DEATHRATTLE / BATTLE_CRY / STATS
        /// </summary>
        private string DetectArchetype(GameState state, string dominantTribe)
        {
            int deathrattle = 0, battlecry = 0, summon = 0;
            foreach (var m in state.BoardMinions)
            {
                if (string.IsNullOrEmpty(m.CardId)) continue;
                var data = GetCardData(m.CardId);
                if (data == null) continue;
                if (data.HasMechanic("DEATHRATTLE")) deathrattle++;
                if (data.HasMechanic("BATTLECRY")) battlecry++;
                if (data.HasMechanic("SUMMON")) summon++;
            }
            if (deathrattle >= 2) return "DEATHRATTLE";
            if (summon >= 2 && deathrattle >= 1) return "TOKEN";
            if (battlecry >= 2) return "BATTLE_CRY";
            return "STATS";
        }

        private Dictionary<string, float> BuildTribeWeights(Dictionary<string, int> counts,
            string dominantTribe, LockState state, string heroTribe)
        {
            var weights = GetDefaultWeights();
            if (state == LockState.None) return weights;

            float bonus = state == LockState.Hard ? 0.30f : 0.15f;

            // 提升主流派权重
            if (!string.IsNullOrEmpty(dominantTribe) && weights.ContainsKey(dominantTribe))
                weights[dominantTribe] += bonus;

            // 英雄倾向流派额外加分
            if (!string.IsNullOrEmpty(heroTribe) && weights.ContainsKey(heroTribe))
                weights[heroTribe] += bonus * 0.5f;

            return weights;
        }

        /// <summary>
        /// 获取某流派的购买乘性修正系数。
        /// Hard锁 +0.30, Soft锁 +0.15, None 0.
        /// </summary>
        public float GetBuyMultiplier(string shopTribe, CompLockResult lockResult)
        {
            if (lockResult == null || lockResult.State == LockState.None)
                return 1.0f;
            if (string.IsNullOrEmpty(shopTribe))
                return 1.0f;

            if (shopTribe == lockResult.DominantTribe)
                return lockResult.State == LockState.Hard ? 1.50f : 1.20f;

            // 惩罚：非主流派卡牌在硬锁时大幅扣分，流派确定后不应三心二意
            if (lockResult.State == LockState.Hard)
                return 0.50f;

            return 1.0f;
        }

        private static Dictionary<string, float> GetDefaultWeights()
        {
            return new Dictionary<string, float>
            {
                { "BEAST", 1.0f },
                { "MECHANICAL", 1.0f },
                { "DRAGON", 1.0f },
                { "ELEMENTAL", 1.0f },
                { "MURLOC", 1.0f },
                { "PIRATE", 1.0f },
                { "UNDEAD", 1.0f },
                { "QUILBOAR", 1.0f },
                { "NAGA", 1.0f },
                { "DEMON", 1.0f },
            };
        }
    }
}
