namespace BobCoach.Engine
{
    /// <summary>解释真实时空扭曲候选批次的报价规则；不会创建候选。</summary>
    public sealed class TimewarpOfferRule
    {
        public TimewarpOfferRule(
            string kind,
            int offerCount,
            bool golden,
            bool grantsTripleReward,
            string sourceId)
        {
            Kind = kind ?? "";
            OfferCount = offerCount;
            Golden = golden;
            GrantsTripleReward = grantsTripleReward;
            SourceId = sourceId ?? "";
        }

        public string Kind { get; private set; }
        public int OfferCount { get; private set; }
        public bool Golden { get; private set; }
        public bool GrantsTripleReward { get; private set; }
        public string SourceId { get; private set; }
    }
}
