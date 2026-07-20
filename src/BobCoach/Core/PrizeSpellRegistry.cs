namespace BobCoach.Engine
{
    internal sealed class PrizeSpellRegistry
    {
        private readonly IPrizeSpellSource _source;

        public PrizeSpellRegistry()
            : this(new CachedPrizeSpellSource(
                new HearthDbPrizeSpellFactSource(),
                new PrizeSpellRuleEvaluator()))
        {
        }

        internal PrizeSpellRegistry(IPrizeSpellSource source)
        {
            _source = source ?? throw new System.ArgumentNullException(nameof(source));
        }

        internal bool TryGet(string cardId, out PrizeSpellPolicy policy)
        {
            return _source.TryGet(cardId, out policy);
        }
    }
}
