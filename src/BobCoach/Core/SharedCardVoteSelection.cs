namespace BobCoach.Engine
{
    /// <summary>由显式外部证据确认的共享选牌结果。</summary>
    public sealed class SharedCardVoteSelection
    {
        internal SharedCardVoteSelection(
            string occurrenceId, string selectedCardId,
            string selectingPlayerId, string evidenceSource)
        {
            OccurrenceId = occurrenceId ?? "";
            SelectedCardId = selectedCardId ?? "";
            SelectingPlayerId = selectingPlayerId ?? "";
            EvidenceSource = evidenceSource ?? "";
        }

        public string OccurrenceId { get; private set; }
        public string SelectedCardId { get; private set; }
        public string SelectingPlayerId { get; private set; }
        public string EvidenceSource { get; private set; }
    }
}
