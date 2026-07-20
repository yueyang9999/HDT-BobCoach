namespace BobCoach.Engine
{
    /// <summary>每回合开始由所有玩家共享同一结果的尤格轮盘规则。</summary>
    public sealed class SharedYoggWheelRule
    {
        internal SharedYoggWheelRule(string sourceId)
        {
            Trigger = "turn_start";
            SharedScope = "all_players";
            SourceId = sourceId ?? "";
        }

        public string Trigger { get; private set; }
        public string SharedScope { get; private set; }
        public string SourceId { get; private set; }
    }
}
