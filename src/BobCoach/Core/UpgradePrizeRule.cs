namespace BobCoach.Engine
{
    /// <summary>升级酒馆后奖品发现的等级进度规则；不代表发现已经发生。</summary>
    public sealed class UpgradePrizeRule
    {
        internal UpgradePrizeRule(
            int initialTier, int improvesEveryTurns, int maxTier, string sourceId)
        {
            InitialTier = initialTier;
            ImprovesEveryTurns = improvesEveryTurns;
            MaxTier = maxTier;
            SourceId = sourceId ?? "";
        }

        public int InitialTier { get; private set; }
        public int ImprovesEveryTurns { get; private set; }
        public int MaxTier { get; private set; }
        public string SourceId { get; private set; }
    }
}
