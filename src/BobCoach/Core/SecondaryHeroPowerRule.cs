namespace BobCoach.Engine
{
    /// <summary>固定第二英雄技能的预期规则；实际存在与可用状态仍以HDT实体为准。</summary>
    public sealed class SecondaryHeroPowerRule
    {
        public SecondaryHeroPowerRule(
            string cardId,
            int unlockTurn,
            string copiesPurchasedTrinket,
            string sourceId)
        {
            CardId = cardId ?? "";
            UnlockTurn = unlockTurn;
            CopiesPurchasedTrinket = copiesPurchasedTrinket ?? "";
            SourceId = sourceId ?? "";
        }

        public string CardId { get; private set; }
        public int UnlockTurn { get; private set; }
        public string CopiesPurchasedTrinket { get; private set; }
        public string SourceId { get; private set; }
    }
}
