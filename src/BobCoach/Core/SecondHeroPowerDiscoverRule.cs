namespace BobCoach.Engine
{
    /// <summary>开局动态发现第二英雄技能的只读预期，不包含候选或选中结果。</summary>
    public sealed class SecondHeroPowerDiscoverRule
    {
        public SecondHeroPowerDiscoverRule(int count, string sourceId)
        {
            Count = count;
            Trigger = "game_start";
            SourceId = sourceId ?? "";
        }

        public int Count { get; private set; }
        public string Trigger { get; private set; }
        public string SourceId { get; private set; }
    }
}
