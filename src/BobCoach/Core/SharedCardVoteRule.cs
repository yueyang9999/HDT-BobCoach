namespace BobCoach.Engine
{
    /// <summary>每回合共享选牌并延迟发放的只读规则。</summary>
    public sealed class SharedCardVoteRule
    {
        internal SharedCardVoteRule(string sourceId)
        {
            SelectionAt = "turn_start";
            GrantAt = "turn_end";
            SharedScope = "all_players";
            SourceId = sourceId ?? "";
        }

        public string SelectionAt { get; private set; }
        public string GrantAt { get; private set; }
        public string SharedScope { get; private set; }
        public string SourceId { get; private set; }
    }
}
