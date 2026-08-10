using System;

namespace BobCoach.Engine
{
    public sealed class HeroSkillFact
    {
        public string HeroCardId = "";
        public string HeroPowerCardId = "";
        public string BuddyCardId = "";
        public string PowerType = "Passive";
        public int Cost;
        public int UnlockTurn = 1;
        public int UnlockTier = 1;
        public string TextCn = "";
        public string UsePurpose = "None";
        public string TargetRule = "";
        public bool IsActive = false;
    }

    /// <summary>
    /// 英雄技能统一目录。当前封装 HeroPowerEngine 的策略事实，
    /// Phase 2 会把版本化 hero_skill_registry.json 作为最终数据源。
    /// </summary>
    public sealed class HeroSkillCatalog
    {
        private readonly HeroPowerEngine _engine;

        public HeroSkillCatalog()
            : this(new HeroPowerEngine())
        {
        }

        internal HeroSkillCatalog(HeroPowerEngine engine)
        {
            _engine = engine ?? throw new ArgumentNullException("engine");
        }

        public bool TryGet(string heroCardId, string heroPowerCardId, out HeroSkillFact fact)
        {
            fact = null;
            if (string.IsNullOrEmpty(heroCardId)) return false;

            var strategy = _engine.GetStrategyForPower(heroCardId, heroPowerCardId);
            if (strategy == null) return false;

            bool isActive = strategy.PowerType == HeroPowerType.Active
                || strategy.PowerType == HeroPowerType.Conditional;
            fact = new HeroSkillFact
            {
                HeroCardId = strategy.HeroCardId,
                HeroPowerCardId = heroPowerCardId ?? "",
                PowerType = strategy.PowerType.ToString(),
                Cost = strategy.PowerCost,
                UnlockTurn = strategy.UnlockTurn,
                UnlockTier = strategy.UnlockTier,
                TextCn = strategy.PowerHint,
                UsePurpose = strategy.UsePurpose.ToString(),
                IsActive = isActive,
            };
            return true;
        }
    }
}
