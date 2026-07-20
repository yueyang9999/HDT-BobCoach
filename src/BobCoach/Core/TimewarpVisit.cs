namespace BobCoach.Engine
{
    /// <summary>服务器计划的固定时空扭曲发生项；不代表实际批次已经出现。</summary>
    public sealed class TimewarpVisit
    {
        public TimewarpVisit(string id, string kind, int turn, string sourceId)
        {
            Id = id ?? "";
            Kind = kind ?? "";
            Turn = turn;
            SourceId = sourceId ?? "";
        }

        public string Id { get; private set; }
        public string Kind { get; private set; }
        public int Turn { get; private set; }
        public string SourceId { get; private set; }
    }
}
