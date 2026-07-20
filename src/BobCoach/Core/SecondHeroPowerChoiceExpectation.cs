namespace BobCoach.Engine
{
    /// <summary>本局开局第二技能发现的只读发生预期。</summary>
    public sealed class SecondHeroPowerChoiceExpectation
    {
        public SecondHeroPowerChoiceExpectation(string occurrenceId, int count, string sourceId)
        {
            OccurrenceId = occurrenceId ?? "";
            Count = count;
            SourceId = sourceId ?? "";
        }

        public string OccurrenceId { get; private set; }
        public int Count { get; private set; }
        public string SourceId { get; private set; }
    }
}
