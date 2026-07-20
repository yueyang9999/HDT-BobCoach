namespace BobCoach.Engine
{
    /// <summary>共享牌的真实到账观察；当前仅能证明本地玩家。</summary>
    public sealed class SharedCardGrantObservation
    {
        internal SharedCardGrantObservation(
            string occurrenceId, string cardId, int entityId,
            string observedScope, string evidenceSource)
        {
            OccurrenceId = occurrenceId ?? "";
            CardId = cardId ?? "";
            EntityId = entityId;
            ObservedScope = observedScope ?? "";
            EvidenceSource = evidenceSource ?? "";
        }

        public string OccurrenceId { get; private set; }
        public string CardId { get; private set; }
        public int EntityId { get; private set; }
        public string ObservedScope { get; private set; }
        public string EvidenceSource { get; private set; }
    }
}
