namespace BobCoach.Engine
{
    /// <summary>升级后应出现的奖品发现；不代表候选或奖品已经到账。</summary>
    public sealed class PrizeDiscoverExpectation
    {
        internal PrizeDiscoverExpectation(
            string occurrenceId, int triggerTurn, int prizeTier,
            int count, string sourceId)
        {
            OccurrenceId = occurrenceId ?? "";
            TriggerTurn = triggerTurn;
            PrizeTier = prizeTier;
            Count = count;
            SourceId = sourceId ?? "";
        }

        public string OccurrenceId { get; private set; }
        public int TriggerTurn { get; private set; }
        public int PrizeTier { get; private set; }
        public int Count { get; private set; }
        public string SourceId { get; private set; }
    }
}
