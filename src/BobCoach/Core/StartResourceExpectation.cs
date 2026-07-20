namespace BobCoach.Engine
{
    public sealed class StartResourceExpectation
    {
        public StartResourceExpectation(
            string id, string kind, string cardId, int count, bool golden, string sourceId)
        {
            Id = id ?? "";
            Kind = kind ?? "";
            CardId = cardId ?? "";
            Count = count;
            Golden = golden;
            SourceId = sourceId ?? "";
        }

        public string Id { get; private set; }
        public string Kind { get; private set; }
        public string CardId { get; private set; }
        public int Count { get; private set; }
        public bool Golden { get; private set; }
        public string SourceId { get; private set; }
    }

    public enum StartResourceVerificationStatus
    {
        Pending,
        Observed,
        Mismatched,
    }

}
