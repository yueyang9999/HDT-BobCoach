using System;
using System.Collections.Generic;

namespace BobCoach.Engine
{
    internal interface IHeroStrategySource
    {
        bool TryGet(string cardId, out HeroStrategy strategy);
    }

    internal sealed class CachedHeroStrategySource : IHeroStrategySource
    {
        private readonly IHeroPowerFactSource _facts;
        private readonly IHeroStrategyRuleEvaluator _rules;
        private readonly object _sync = new object();
        private readonly Dictionary<string, CacheEntry> _cache
            = new Dictionary<string, CacheEntry>(StringComparer.Ordinal);

        public CachedHeroStrategySource(
            IHeroPowerFactSource facts,
            IHeroStrategyRuleEvaluator rules)
        {
            _facts = facts ?? throw new ArgumentNullException("facts");
            _rules = rules ?? throw new ArgumentNullException("rules");
        }

        public bool TryGet(string cardId, out HeroStrategy strategy)
        {
            strategy = null;
            if (string.IsNullOrEmpty(cardId)) return false;

            lock (_sync)
            {
                CacheEntry entry;
                if (!_cache.TryGetValue(cardId, out entry))
                {
                    entry = Evaluate(cardId);
                    _cache[cardId] = entry;
                }
                if (!entry.Success || entry.Strategy == null) return false;
                strategy = Copy(entry.Strategy);
                return true;
            }
        }

        private CacheEntry Evaluate(string cardId)
        {
            try
            {
                HeroPowerFact fact;
                HeroStrategy strategy;
                if (!_facts.TryGet(cardId, out fact)
                    || fact == null
                    || !_rules.TryEvaluate(fact, out strategy)
                    || strategy == null)
                    return new CacheEntry();
                return new CacheEntry { Success = true, Strategy = Copy(strategy) };
            }
            catch
            {
                return new CacheEntry();
            }
        }

        internal static HeroStrategy Copy(HeroStrategy source)
        {
            if (source == null) return null;
            return new HeroStrategy
            {
                HeroCardId = source.HeroCardId ?? "",
                HeroName = "",
                PowerType = source.PowerType,
                Archetype = source.Archetype,
                PowerCost = source.PowerCost,
                UnlockTurn = source.UnlockTurn,
                UnlockTier = source.UnlockTier,
                HasDiscover = source.HasDiscover,
                PowerHint = "",
                LevelAggression = source.LevelAggression,
                UpgradeValueBias = source.UpgradeValueBias,
                RefreshValueBias = source.RefreshValueBias,
                BuyValueBias = source.BuyValueBias,
                PowerValueBias = source.PowerValueBias,
                TribeAffinity = new Dictionary<string, float>(
                    source.TribeAffinity ?? new Dictionary<string, float>(),
                    StringComparer.Ordinal),
                SynergyTags = new HashSet<string>(
                    source.SynergyTags ?? new HashSet<string>(),
                    StringComparer.Ordinal),
                UsePurpose = source.UsePurpose,
                SpecialRule = source.SpecialRule ?? "",
            };
        }

        private sealed class CacheEntry
        {
            public bool Success;
            public HeroStrategy Strategy;
        }
    }
}
