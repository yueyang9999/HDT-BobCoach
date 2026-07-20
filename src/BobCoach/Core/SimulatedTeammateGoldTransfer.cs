namespace BobCoach.Engine
{
    /// <summary>搜索分支中假设执行的一次金币发送，不是生产观察事实。</summary>
    public sealed class SimulatedTeammateGoldTransfer
    {
        public SimulatedTeammateGoldTransfer(
            string occurrenceId, int turn, int amount, string sourceId)
        {
            OccurrenceId = occurrenceId ?? "";
            Turn = turn;
            Amount = amount;
            SourceId = sourceId ?? "";
        }

        public string OccurrenceId { get; private set; }
        public int Turn { get; private set; }
        public int Amount { get; private set; }
        public string SourceId { get; private set; }
    }
}
