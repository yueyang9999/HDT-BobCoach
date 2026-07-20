namespace BobCoach.Engine
{
    /// <summary>HDT真实第二技能实体对动态选择结果的到账证明。</summary>
    public sealed class SecondHeroPowerEntityObservation
    {
        public SecondHeroPowerEntityObservation(
            string occurrenceId, string cardId, int entityId, string evidenceSource)
        {
            OccurrenceId = occurrenceId ?? "";
            CardId = cardId ?? "";
            EntityId = entityId;
            EvidenceSource = evidenceSource ?? "";
        }

        public string OccurrenceId { get; private set; }
        public string CardId { get; private set; }
        public int EntityId { get; private set; }
        public string EvidenceSource { get; private set; }
    }
}
