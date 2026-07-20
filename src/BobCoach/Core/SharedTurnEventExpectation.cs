namespace BobCoach.Engine
{
    /// <summary>某回合应发生的共享事件；不代表随机结果已经观察。</summary>
    public sealed class SharedTurnEventExpectation
    {
        internal SharedTurnEventExpectation(
            string occurrenceId, int turn, string kind,
            string sharedScope, string sourceId)
        {
            OccurrenceId = occurrenceId ?? "";
            Turn = turn;
            Kind = kind ?? "";
            SharedScope = sharedScope ?? "";
            SourceId = sourceId ?? "";
        }

        public string OccurrenceId { get; private set; }
        public int Turn { get; private set; }
        public string Kind { get; private set; }
        public string SharedScope { get; private set; }
        public string SourceId { get; private set; }
    }
}
