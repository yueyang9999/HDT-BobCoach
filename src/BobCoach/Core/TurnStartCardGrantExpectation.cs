namespace BobCoach.Engine
{
    /// <summary>某回合开始应获得的卡牌；不代表手牌实体已经到账。</summary>
    public sealed class TurnStartCardGrantExpectation
    {
        internal TurnStartCardGrantExpectation(
            string occurrenceId, int turn, string cardId, int count, string sourceId)
        {
            OccurrenceId = occurrenceId ?? "";
            Turn = turn;
            CardId = cardId ?? "";
            Count = count;
            SourceId = sourceId ?? "";
        }

        public string OccurrenceId { get; private set; }
        public int Turn { get; private set; }
        public string CardId { get; private set; }
        public int Count { get; private set; }
        public string SourceId { get; private set; }
    }
}
