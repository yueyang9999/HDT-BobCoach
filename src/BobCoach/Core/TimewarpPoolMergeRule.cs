namespace BobCoach.Engine
{
    /// <summary>某类时空扭曲随从可进入公共卡池的生效回合。</summary>
    public sealed class TimewarpPoolMergeRule
    {
        public TimewarpPoolMergeRule(string kind, int turn, string sourceId)
        {
            Kind = kind ?? "";
            Turn = turn;
            SourceId = sourceId ?? "";
        }

        public string Kind { get; private set; }
        public int Turn { get; private set; }
        public string SourceId { get; private set; }
    }
}
