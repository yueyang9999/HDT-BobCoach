namespace BobCoach.Engine
{
    /// <summary>某回合应发生的一次共享选牌批次。</summary>
    public sealed class SharedCardVoteOccurrence
    {
        internal SharedCardVoteOccurrence(
            string occurrenceId, int turn, string sourceId)
        {
            OccurrenceId = occurrenceId ?? "";
            Turn = turn;
            SourceId = sourceId ?? "";
        }

        public string OccurrenceId { get; private set; }
        public int Turn { get; private set; }
        public string SourceId { get; private set; }
    }
}
