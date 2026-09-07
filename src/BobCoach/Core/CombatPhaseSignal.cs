namespace BobCoach.Engine
{
    /// <summary>
    /// 战斗阶段判定: Power.log 权威阶段(MAIN_COMBAT) 与 HDT combat flag 的 OR 组合。
    /// 1.2.16-fix I-20260907-18(塞纳留斯 090858, rank1): HDT flag 在招募→战斗翻转存在滞后
    /// (~1.4s, bob_coach.log CombatEnter 实测), 若主渲染 gate 只信 flag, 招募尾声的推荐
    /// (含技能/发现类)会被渲染并残留进战斗画面。Power.log 阶段(炉石内部 STEP)更早到达,
    /// 双信号取 OR 提前拦截。与 BuildDarkGiftTipContext 既有双信号判定同理念(2026-08 先例)。
    /// 纯函数: powerLogPhase 为空/未知时退化为仅 hdtFlag(不破坏无 Power.log 环境)。
    /// </summary>
    public static class CombatPhaseSignal
    {
        public static bool IsInCombat(string powerLogPhase, bool hdtCombatFlag)
        {
            if (hdtCombatFlag) return true;
            return string.Equals(
                powerLogPhase, "MAIN_COMBAT", System.StringComparison.Ordinal);
        }
    }
}
