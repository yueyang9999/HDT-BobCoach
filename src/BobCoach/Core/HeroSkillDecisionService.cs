using System;

namespace BobCoach.Engine
{
    public sealed class HeroSkillDecision
    {
        public bool ShouldUse;
        public double Score;
        public int Cost;
        public string Reason = "";
    }

    /// <summary>
    /// 主动英雄技能使用决策。被动技能不提示；
    /// 主动技能必须通过费用、解锁、目标、性价比和机会成本门控。
    /// </summary>
    public static class HeroSkillDecisionService
    {
        public static HeroSkillDecision Decide(
            string powerType,
            int cost,
            int unlockTurn,
            int unlockTier,
            int liveGold,
            int turn,
            int tavernTier,
            int boardSize,
            int shopSize,
            string usePurpose,
            bool hasValidTarget,
            bool exhausted = false,
            string heroCardId = null)
        {
            if (powerType == "Passive" || powerType == "Aura")
            {
                return new HeroSkillDecision
                {
                    ShouldUse = false,
                    Cost = cost,
                    Reason = "passive",
                };
            }

            if (exhausted)
            {
                return new HeroSkillDecision
                {
                    ShouldUse = false,
                    Cost = cost,
                    Reason = "used-this-turn",
                };
            }

            if (liveGold < cost)
            {
                return new HeroSkillDecision
                {
                    ShouldUse = false,
                    Cost = cost,
                    Reason = "insufficient-gold",
                };
            }

            if (turn < unlockTurn || tavernTier < unlockTier)
            {
                return new HeroSkillDecision
                {
                    ShouldUse = false,
                    Cost = cost,
                    Reason = "locked",
                };
            }

            if (usePurpose == "Buff" && boardSize <= 0)
            {
                return new HeroSkillDecision
                {
                    ShouldUse = false,
                    Cost = cost,
                    Reason = "no-board-target",
                };
            }

            if ((usePurpose == "Combat" || usePurpose == "Generic")
                && boardSize <= 0)
            {
                return new HeroSkillDecision
                {
                    ShouldUse = false,
                    Cost = cost,
                    Reason = "no-board-target",
                };
            }

            if (usePurpose == "Economy")
            {
                return new HeroSkillDecision
                {
                    ShouldUse = liveGold - cost >= 0,
                    Score = 0.7,
                    Cost = cost,
                    Reason = "economy",
                };
            }

            bool targetOk = hasValidTarget || usePurpose == "Economy"
                || usePurpose == "Resource";
            if (!targetOk)
            {
                return new HeroSkillDecision
                {
                    ShouldUse = false,
                    Cost = cost,
                    Reason = "invalid-target",
                };
            }

            // R236 深度英雄开发(2026-08, 真实回放节奏提炼):
            // 每英雄技能节奏——真实玩家"何时用/怎么用"的特定打法。
            // 数据源: firestone 1675局 + global 48局 技能回合模式。
            if (!string.IsNullOrEmpty(heroCardId))
            {
                // 雷诺·杰克逊: 每局限一次点金, 真实 T9 才用(板面成型后) — 前期用=浪费
                if (heroCardId == "TB_BaconShop_HERO_41")
                {
                    bool boardMature = boardSize >= 5;
                    bool hasGoodTarget = shopSize > 0 || boardSize >= 4;
                    if (turn < 7 || !boardMature)
                        return new HeroSkillDecision { ShouldUse = false, Cost = cost, Reason = "雷诺: 前期不用点金(每局限一次, 等板面成型)" };
                    if (!hasGoodTarget)
                        return new HeroSkillDecision { ShouldUse = false, Cost = cost, Reason = "雷诺: 无优质目标" };
                }
                // 玛维·影歌: 锁牌需商店有好牌(真实71%回合用, 店差不用)
                if (heroCardId == "TB_BaconShop_HERO_62")
                {
                    if (shopSize == 0)
                        return new HeroSkillDecision { ShouldUse = false, Cost = cost, Reason = "玛维: 商店无牌可锁" };
                    // 店有好牌才锁(高本/大身材 — 真实节奏: 锁值得的牌)
                    if (shopSize <= 2 && turn <= 4)
                        return new HeroSkillDecision { ShouldUse = true, Score = 0.75, Cost = cost, Reason = "玛维: 早期锁高价值牌(预购)" };
                }
                // 托瓦格尔: 11费技能前期付不起, 真实 T5 才用 — 金币充足才用
                if (heroCardId == "BG23_HERO_305")
                {
                    int effectiveCost = Math.Max(0, 11 - turn);
                    if (liveGold < effectiveCost)
                        return new HeroSkillDecision { ShouldUse = false, Cost = effectiveCost, Reason = "托瓦格尔: 费用" + effectiveCost + "不足" };
                    return new HeroSkillDecision { ShouldUse = true, Score = 0.8, Cost = effectiveCost, Reason = "托瓦格尔: 偷取酒馆(费用已降)" };
                }
                // 凯瑞尔·罗姆: 0费随机buff, 真实100%每回合用(无成本收益)
                if (heroCardId == "BG21_HERO_000" && cost == 0 && boardSize > 0)
                    return new HeroSkillDecision { ShouldUse = true, Score = 0.9, Cost = cost, Reason = "凯瑞尔: 免费buff每回合用" };
                // 塞纳留斯: 铸币上限+1, 真实 T1 就用(长期收益)
                if (heroCardId == "BG32_HERO_001" && cost > 0 && turn <= 6 && liveGold >= cost)
                    return new HeroSkillDecision { ShouldUse = true, Score = 0.85, Cost = cost, Reason = "塞纳留斯: 铸币上限+1(前期优先)" };
            }


            double score = 0.55 + (liveGold - cost) * 0.05 + boardSize * 0.02;
            if (shopSize > 0 && usePurpose == "Resource") score += 0.15;
            return new HeroSkillDecision
            {
                ShouldUse = score > 0.5,
                Score = score,
                Cost = cost,
                Reason = "active",
            };
        }
    }
}
