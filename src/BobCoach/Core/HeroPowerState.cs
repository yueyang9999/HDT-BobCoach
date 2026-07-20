namespace BobCoach.Engine
{
    /// <summary>从HDT实体提取的英雄技能事实，供纯解析器消费。</summary>
    public sealed class HeroPowerObservation
    {
        public string CardId = "";
        public int EntityId;
        public int Cost;
        public bool Exhausted;
        public bool IsActive;
        public bool HasDiscover;
        public int UnlockTurn = 1;
        public int UnlockTier = 1;
        public string SpecialRule = "";
    }

    /// <summary>单个实际英雄技能的独立运行时状态。</summary>
    public sealed class HeroPowerState
    {
        public string CardId = "";
        public int EntityId;
        public int Cost;
        public bool Exhausted;
        public bool IsPrimary;
        public bool IsSecondary;
        public bool IsActive;
        public bool IsUnlocked;
        public bool HasDiscover;
        public int UnlockTurn = 1;
        public int UnlockTier = 1;
        public string SpecialRule = "";

        public HeroPowerState Copy()
        {
            return (HeroPowerState)MemberwiseClone();
        }
    }
}
