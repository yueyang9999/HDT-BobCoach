using System;
using System.Collections.Generic;

namespace BobCoach.Engine
{
    public sealed class TrinketShadowOffer
    {
        public string CardId = "";
        public string Name = "";
        public double Score;
        public bool IsUnrated;
    }

    public sealed class TrinketShadowRecord
    {
        public int SchemaVersion = 2;
        public int ChoiceId;
        public int TaskList;
        public string SourceCardId = "";
        public string SelectionContext = "unknown";
        public bool EligibleForCalibration;
        public string SelectedCardId = "";
        public string CompletionStatus = "completed";
        public int Turn;
        public int TavernTier;
        public bool Lesser;
        public int Health;
        public List<TrinketShadowOffer> Offers = new List<TrinketShadowOffer>();
    }

    /// <summary>先暂存报价评分，收到同 choiceId 的完成事件后才产出可分析记录。</summary>
    public sealed class TrinketShadowCaptureSession
    {
        private TrinketShadowRecord _pending;

        public void Stage(TrinketChoiceBinding binding, int tavernTier, bool lesser, int health,
            List<TrinketShadowOffer> offers)
        {
            if (binding == null || binding.Batch == null) return;
            _pending = new TrinketShadowRecord
            {
                ChoiceId = binding.Batch.ChoiceId,
                TaskList = binding.Batch.TaskList,
                SourceCardId = binding.Batch.SourceCardId ?? "",
                SelectionContext = ToSchemaContext(binding.Context),
                EligibleForCalibration = binding.EligibleForCalibration,
                Turn = binding.TargetTurn,
                TavernTier = tavernTier,
                Lesser = lesser,
                Health = health,
                Offers = offers != null
                    ? new List<TrinketShadowOffer>(offers)
                    : new List<TrinketShadowOffer>(),
            };
        }

        public TrinketShadowRecord Complete(TrinketChoiceBinding binding,
            PowerLogChoiceCompletion completion)
        {
            if (_pending == null || binding == null || binding.Batch == null || completion == null)
                return null;
            if (_pending.ChoiceId != binding.Batch.ChoiceId
                || _pending.ChoiceId != completion.ChoiceId)
                return null;
            if (string.IsNullOrEmpty(completion.SelectedCardId)) return null;
            var result = _pending;
            result.SelectedCardId = completion.SelectedCardId ?? "";
            _pending = null;
            return result;
        }

        public void Reset()
        {
            _pending = null;
        }

        private static string ToSchemaContext(TrinketChoiceContext context)
        {
            switch (context)
            {
                case TrinketChoiceContext.ScheduledLesser: return "scheduled_lesser";
                case TrinketChoiceContext.ScheduledGreater: return "scheduled_greater";
                case TrinketChoiceContext.AnomalyExtra: return "anomaly_extra";
                case TrinketChoiceContext.OtherTrinket: return "other_trinket";
                default: return "unknown";
            }
        }
    }
}
