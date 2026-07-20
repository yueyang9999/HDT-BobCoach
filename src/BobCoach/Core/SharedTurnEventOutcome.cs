namespace BobCoach.Engine
{
    /// <summary>由真实外部证据确认的共享事件结果。</summary>
    public sealed class SharedTurnEventOutcome
    {
        internal SharedTurnEventOutcome(
            string occurrenceId, string outcomeId, string evidenceSource)
        {
            OccurrenceId = occurrenceId ?? "";
            OutcomeId = outcomeId ?? "";
            EvidenceSource = evidenceSource ?? "";
        }

        public string OccurrenceId { get; private set; }
        public string OutcomeId { get; private set; }
        public string EvidenceSource { get; private set; }
    }
}
