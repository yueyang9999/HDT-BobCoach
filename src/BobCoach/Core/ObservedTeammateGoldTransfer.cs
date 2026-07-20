namespace BobCoach.Engine
{
    /// <summary>Power.log精确动作块证明的一次真实队友金币发送。</summary>
    public sealed class ObservedTeammateGoldTransfer
    {
        public ObservedTeammateGoldTransfer(
            string occurrenceId, int turn, int amount, int actionEntityId,
            string evidenceId, string sourceId)
        {
            OccurrenceId = occurrenceId ?? "";
            Turn = turn;
            Amount = amount;
            ActionEntityId = actionEntityId;
            EvidenceId = evidenceId ?? "";
            SourceId = sourceId ?? "";
        }

        public string OccurrenceId { get; private set; }
        public int Turn { get; private set; }
        public int Amount { get; private set; }
        public int ActionEntityId { get; private set; }
        public string EvidenceId { get; private set; }
        public string SourceId { get; private set; }
    }
}
