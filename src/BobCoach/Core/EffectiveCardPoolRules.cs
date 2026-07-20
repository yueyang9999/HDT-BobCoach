using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace BobCoach.Engine
{
    /// <summary>特殊公共卡池规则的单一只读入口。</summary>
    public sealed class EffectiveCardPoolRules
    {
        private readonly ReadOnlyCollection<TimewarpPoolMergeRule> _timewarpPoolMergeRules;

        internal EffectiveCardPoolRules(
            BuddyPoolRule buddyPool,
            IList<TimewarpPoolMergeRule> timewarpPoolMergeRules)
        {
            BuddyPool = buddyPool;
            _timewarpPoolMergeRules = new ReadOnlyCollection<TimewarpPoolMergeRule>(
                new List<TimewarpPoolMergeRule>(
                    timewarpPoolMergeRules ?? new TimewarpPoolMergeRule[0]));
        }

        public BuddyPoolRule BuddyPool { get; private set; }
        public IList<TimewarpPoolMergeRule> TimewarpPoolMergeRules
        {
            get { return _timewarpPoolMergeRules; }
        }
    }
}
