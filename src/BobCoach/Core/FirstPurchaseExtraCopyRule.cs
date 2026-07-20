namespace BobCoach.Engine
{
    public sealed class FirstPurchaseExtraCopyRule
    {
        public FirstPurchaseExtraCopyRule(int extraCopyCount, string cardType, string sourceId)
        {
            ExtraCopyCount = extraCopyCount;
            CardType = cardType ?? "";
            SourceId = sourceId ?? "";
        }

        public int ExtraCopyCount { get; private set; }
        public string CardType { get; private set; }
        public string SourceId { get; private set; }
    }
}
