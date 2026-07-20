using System;
using System.Collections.Generic;

namespace BobCoach.Engine
{
    internal interface IPrizeSpellSource
    {
        bool TryGet(string cardId, out PrizeSpellPolicy policy);
    }

    internal sealed class CachedPrizeSpellSource : IPrizeSpellSource
    {
        private sealed class CacheEntry
        {
            public bool Success;
            public PrizeSpellPolicy Policy;
        }

        private readonly IPrizeSpellFactSource _facts;
        private readonly IPrizeSpellRuleEvaluator _rules;
        private readonly object _gate = new object();
        private readonly Dictionary<string, CacheEntry> _cache
            = new Dictionary<string, CacheEntry>(StringComparer.Ordinal);

        public CachedPrizeSpellSource(
            IPrizeSpellFactSource facts,
            IPrizeSpellRuleEvaluator rules)
        {
            _facts = facts ?? throw new ArgumentNullException(nameof(facts));
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        }

        public bool TryGet(string cardId, out PrizeSpellPolicy policy)
        {
            policy = null;
            if (string.IsNullOrEmpty(cardId)) return false;

            lock (_gate)
            {
                CacheEntry cached;
                if (_cache.TryGetValue(cardId, out cached))
                {
                    policy = cached.Policy;
                    return cached.Success;
                }

                bool success = false;
                PrizeSpellPolicy snapshot = null;
                try
                {
                    PrizeSpellFact fact;
                    PrizeSpellPolicy derived = null;
                    success = _facts.TryGet(cardId, out fact)
                        && fact != null
                        && string.Equals(fact.CardId, cardId, StringComparison.Ordinal)
                        && _rules.TryEvaluate(fact, out derived)
                        && derived != null
                        && string.Equals(derived.CardId, fact.CardId, StringComparison.Ordinal)
                        && derived.PrizeTier == fact.PrizeTier
                        && derived.Role != PrizeSpellRole.Unknown;
                    if (success)
                    {
                        snapshot = new PrizeSpellPolicy(
                            derived.CardId, derived.PrizeTier, derived.Role);
                    }
                }
                catch
                {
                    success = false;
                    snapshot = null;
                }

                var entry = new CacheEntry { Success = success, Policy = snapshot };
                _cache[cardId] = entry;
                policy = snapshot;
                return success;
            }
        }
    }
}
