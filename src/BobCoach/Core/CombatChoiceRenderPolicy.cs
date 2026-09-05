namespace BobCoach.Engine
{
    /// <summary>
    /// 决定 Power.log 权威发现批次是否可在 HDT 战斗 flag 下渲染。
    /// 1.2.11 I-20260905-11(回合8/9 战斗阶段出发现UI): 真战斗阶段(MAIN_COMBAT,
    /// Power.log 权威阶段)不渲染发现提示 — 游戏内面板只在招募阶段出现, 战斗动画中
    /// 上屏会被局内图层遮挡/时序过早(与时空扭曲 C1 2026-08-19 同理念)。批次保留,
    /// 由招募阶段 EvaluateAndRender 路径渲染。
    /// 但保留 08-07 lagging-flag 保护: HDT combat flag 滞后于 Power.log(招募已开始
    /// 而 HDT 仍报战斗)时, 权威 active 批次仍应渲染, 避免面板漏显。
    /// </summary>
    public static class CombatChoiceRenderPolicy
    {
        public static bool CanRenderDiscoverDuringCombat(
            PowerLogChoiceBatch batch, bool panelActive)
        {
            return CanRenderDiscoverDuringCombat(batch, panelActive, "");
        }

        /// <param name="powerLogPhase">Power.log 当前阶段(MAIN_COMBAT=真战斗, 其余=招募/lagging)。</param>
        public static bool CanRenderDiscoverDuringCombat(
            PowerLogChoiceBatch batch, bool panelActive, string powerLogPhase)
        {
            if (!panelActive) return false;
            if (batch == null) return false;
            if (batch.ChoiceId >= 0
                && batch.Candidates != null
                && batch.Candidates.Count >= 2)
            {
                // 1.2.11: 真战斗阶段不渲染发现 UI(等招募路径); 空/未知阶段按 lagging 处理放行。
                if (string.Equals(powerLogPhase, "MAIN_COMBAT", System.StringComparison.Ordinal))
                    return false;
                return true;
            }
            return false;
        }
    }
}
