using System;

namespace BobCoach.Engine
{
    /// <summary>
    /// 面板生命周期状态机推进器 — 纯函数，按引用写回 PanelState。
    ///
    /// 设计要点：PanelState 是 struct（值类型），必须用 ref 写回，否则相位变更
    /// 丢失在副本里（这是 2026-06-10 之前状态机从未生效的根因）。
    /// 状态机由 BobCoachPlugin 持有的持久字段驱动，跨帧存活；GameState 每帧重建，
    /// 不再持有面板生命周期状态。
    /// </summary>
    public static class PanelStateMachine
    {
        /// <summary>发现面板 Active 相位最大驻留（ticks），超时强制进入 Fading。</summary>
        private const long MaxActiveTicks = 8L * 10000000L; // 8s

        /// <summary>
        /// 推进单个面板状态机一帧。
        /// </summary>
        /// <param name="ps">面板状态（ref 写回）</param>
        /// <param name="active">本帧面板内容是否应可见（offer/发现选项存在且满足触发条件）</param>
        /// <param name="turn">当前回合（进入 Active 时记录 CreatedTurn）</param>
        /// <param name="enforceMaxActive">是否启用 Active 最大驻留兜底（发现面板=true，饰品=false）</param>
        public static void Advance(ref PanelState ps, bool active, int turn, bool enforceMaxActive)
        {
            long now = DateTime.UtcNow.Ticks;
            switch (ps.Phase)
            {
                case PanelPhase.Idle:
                    if (active)
                    {
                        ps.Transition(PanelPhase.Active);
                        ps.CreatedTurn = turn;
                    }
                    break;

                case PanelPhase.Active:
                    if (!active)
                    {
                        // 内容消失 → 进入 Fading 滞回
                        ps.Transition(PanelPhase.Fading);
                    }
                    else if (enforceMaxActive && (now - ps.PhaseEnteredTicks) > MaxActiveTicks)
                    {
                        // 最大驻留兜底：长期滞留(如 zone6 残留实体) → 强制收尾
                        ps.Transition(PanelPhase.Fading);
                    }
                    break;

                case PanelPhase.Fading:
                    if (active)
                    {
                        // 内容重新出现 → 回到 Active(抖动期间不闪)
                        ps.Transition(PanelPhase.Active);
                    }
                    else if ((now - ps.PhaseEnteredTicks) >= ps.HysteresisTicks)
                    {
                        ps.Transition(PanelPhase.Expired);
                    }
                    break;

                case PanelPhase.Expired:
                    // Expired 是"本帧应清除渲染"的一次性信号(消费方读到后清 UI)。
                    // 下一帧 Advance 自动回 Idle, 并立即根据 active 决定是否重入 Active —
                    // 这样状态机自闭环, 不依赖消费方写回(消费方若被早返回跳过, 状态机仍能复活)。
                    ps.Transition(PanelPhase.Idle);
                    if (active)
                    {
                        ps.Transition(PanelPhase.Active);
                        ps.CreatedTurn = turn;
                    }
                    break;
            }
        }
    }
}
