namespace BobCoach.Engine
{
    /// <summary>共享选择牌在回合结束时的发放预期，不代表任何玩家已到账。</summary>
    public sealed class SharedCardGrantExpectation
    {
        internal SharedCardGrantExpectation(
            string occurrenceId, int turn, string cardId,
            string sharedScope, string sourceId)
        {
            OccurrenceId = occurrenceId ?? "";
            Turn = turn;
            CardId = cardId ?? "";
            SharedScope = sharedScope ?? "";
            SourceId = sourceId ?? "";
        }

        public string OccurrenceId { get; private set; }
        public int Turn { get; private set; }
        public string CardId { get; private set; }
        public string SharedScope { get; private set; }
        public string SourceId { get; private set; }
    }
}
