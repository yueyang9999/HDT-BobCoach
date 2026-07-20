namespace BobCoach.Engine
{
    /// <summary>伙伴公共池规则的唯一生效判断。</summary>
    public static class BuddyCardPoolEvaluator
    {
        public static bool IsEnabled(EffectiveGameRules rules, int turn)
        {
            var cardPoolRules = (rules ?? EffectiveGameRules.Default).CardPoolRules;
            var buddyRule = cardPoolRules != null ? cardPoolRules.BuddyPool : null;
            return buddyRule != null && buddyRule.SharedPool && buddyRule.NormalOnly
                && turn >= buddyRule.EnterTurn;
        }
    }
}
