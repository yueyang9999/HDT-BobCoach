namespace BobCoach.Engine
{
    public sealed class PurchaseRewardExpectation
    {
        public PurchaseRewardExpectation(
            string occurrenceId, string cardId, int count, bool isSpell,
            bool golden, string sourceId)
        {
            OccurrenceId = occurrenceId ?? "";
            CardId = cardId ?? "";
            Count = count;
            IsSpell = isSpell;
            Golden = golden;
            SourceId = sourceId ?? "";
        }

        public string OccurrenceId { get; private set; }
        public string CardId { get; private set; }
        public int Count { get; private set; }
        public bool IsSpell { get; private set; }
        public bool Golden { get; private set; }
        public string SourceId { get; private set; }
    }
}
