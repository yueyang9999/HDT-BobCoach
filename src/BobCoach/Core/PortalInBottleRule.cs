namespace BobCoach.Engine
{
    /// <summary>duo每回合开始的瓶中传送门预期；不代表卡牌已经到账。</summary>
    public sealed class PortalInBottleRule
    {
        internal PortalInBottleRule(string cardId, int count, string sourceId)
        {
            CardId = cardId ?? "";
            Count = count;
            SourceId = sourceId ?? "";
        }

        public string CardId { get; private set; }
        public int Count { get; private set; }
        public string SourceId { get; private set; }
    }
}
