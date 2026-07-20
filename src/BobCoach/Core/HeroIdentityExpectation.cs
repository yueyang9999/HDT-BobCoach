namespace BobCoach.Engine
{
    /// <summary>单个已知玩家控制器的英雄身份核验结果。</summary>
    public sealed class HeroIdentityExpectation
    {
        public HeroIdentityExpectation(
            int controllerId,
            string expectedHeroCardId,
            string observedHeroCardId,
            string status,
            string sourceId)
        {
            ControllerId = controllerId;
            ExpectedHeroCardId = expectedHeroCardId ?? "";
            ObservedHeroCardId = observedHeroCardId ?? "";
            Status = status ?? "pending";
            SourceId = sourceId ?? "";
        }

        public int ControllerId { get; private set; }
        public string ExpectedHeroCardId { get; private set; }
        public string ObservedHeroCardId { get; private set; }
        public string Status { get; private set; }
        public string SourceId { get; private set; }
    }
}
