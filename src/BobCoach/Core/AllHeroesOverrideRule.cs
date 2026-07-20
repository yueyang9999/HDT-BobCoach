namespace BobCoach.Engine
{
    /// <summary>服务器对全体玩家英雄身份的只读预期，不创建或覆盖英雄实体。</summary>
    public sealed class AllHeroesOverrideRule
    {
        public AllHeroesOverrideRule(string targetHeroCardId, string sourceId)
        {
            TargetHeroCardId = targetHeroCardId ?? "";
            SharedScope = "all_players";
            SourceId = sourceId ?? "";
        }

        public string TargetHeroCardId { get; private set; }
        public string SharedScope { get; private set; }
        public string SourceId { get; private set; }
    }
}
