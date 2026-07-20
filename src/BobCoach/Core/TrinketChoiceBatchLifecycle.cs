using System;

namespace BobCoach.Engine
{
    public enum TrinketChoiceContext
    {
        Unknown,
        ScheduledLesser,
        ScheduledGreater,
        AnomalyExtra,
        OtherTrinket,
    }

    public sealed class TrinketChoiceBinding
    {
        public PowerLogChoiceBatch Batch;
        public int ObservedTurn;
        public int TargetTurn;
        public TrinketChoiceContext Context;
        public bool EligibleForCalibration;
    }

    /// <summary>
    /// 把异步 Power.log 饰品选择绑定到逻辑回合；不依赖 HDT 状态更新与日志到达的先后顺序。
    /// </summary>
    public sealed class TrinketChoiceBatchLifecycle
    {
        public TrinketChoiceBinding Current { get; private set; }

        public TrinketChoiceBinding Observe(PowerLogChoiceBatch batch, int observedTurn)
        {
            if (batch == null) throw new ArgumentNullException(nameof(batch));

            bool scheduled = !string.IsNullOrEmpty(batch.SourceCardId)
                && batch.SourceCardId.StartsWith("BG30_Trinket_", StringComparison.Ordinal);
            bool anomalyExtra = string.Equals(batch.SourceCardId, "BG30_MagicItem_703t", StringComparison.Ordinal);
            int targetTurn = scheduled && (observedTurn == 5 || observedTurn == 8)
                ? observedTurn + 1
                : observedTurn;

            var context = TrinketChoiceContext.OtherTrinket;
            if (scheduled && targetTurn == 6) context = TrinketChoiceContext.ScheduledLesser;
            else if (scheduled && targetTurn == 9) context = TrinketChoiceContext.ScheduledGreater;
            else if (anomalyExtra) context = TrinketChoiceContext.AnomalyExtra;

            Current = new TrinketChoiceBinding
            {
                Batch = batch,
                ObservedTurn = observedTurn,
                TargetTurn = targetTurn,
                Context = context,
                EligibleForCalibration = context == TrinketChoiceContext.ScheduledLesser
                    || context == TrinketChoiceContext.ScheduledGreater,
            };
            return Current;
        }

        public bool TryGetForTurn(int turn, out TrinketChoiceBinding binding)
        {
            binding = Current;
            if (binding == null) return false;
            // 带 choiceId 的 Power.log 批次是当前真实选择，生命周期由匹配的
            // CHOSEN/空批次结束。它可能在战斗中到达并跨入下一招募回合；
            // TargetTurn 只用于计划饰品分类和校准，不能充当过期条件。
            if (binding.Batch != null && binding.Batch.ChoiceId >= 0) return true;

            // 无身份的降级批次无法用 completion 关闭，保留回合上限安全网。
            if (turn <= binding.TargetTurn) return true;
            Current = null;
            binding = null;
            return false;
        }

        public TrinketChoiceBinding Complete(PowerLogChoiceCompletion completion)
        {
            if (completion == null || Current == null || Current.Batch == null) return null;
            if (completion.ChoiceId != Current.Batch.ChoiceId) return null;
            var completed = Current;
            Current = null;
            return completed;
        }

        public void Reset()
        {
            Current = null;
        }
    }
}
