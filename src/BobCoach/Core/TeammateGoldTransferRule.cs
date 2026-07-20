namespace BobCoach.Engine
{
    /// <summary>duo中主动向队友发送金币的精确动作规则。</summary>
    public sealed class TeammateGoldTransferRule
    {
        public TeammateGoldTransferRule(
            string actionCardId, int goldPerUse, int maxPerTurn, string sourceId)
        {
            ActionCardId = actionCardId ?? "";
            GoldPerUse = goldPerUse;
            MaxPerTurn = maxPerTurn;
            SourceId = sourceId ?? "";
        }

        public string ActionCardId { get; private set; }
        public int GoldPerUse { get; private set; }
        public int MaxPerTurn { get; private set; }
        public string SourceId { get; private set; }
    }
}
