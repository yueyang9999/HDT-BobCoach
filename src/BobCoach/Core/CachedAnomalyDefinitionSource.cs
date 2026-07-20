using System;
using System.Collections.Generic;

namespace BobCoach.Engine
{
    internal sealed class CachedAnomalyDefinitionSource : IAnomalyDefinitionSource
    {
        private readonly IAnomalyFactSource _facts;
        private readonly IAnomalyRuleEvaluator _rules;
        private readonly object _sync = new object();
        private readonly Dictionary<string, CacheEntry> _cache
            = new Dictionary<string, CacheEntry>(StringComparer.Ordinal);

        public CachedAnomalyDefinitionSource(
            IAnomalyFactSource facts,
            IAnomalyRuleEvaluator rules)
        {
            _facts = facts ?? throw new ArgumentNullException("facts");
            _rules = rules ?? throw new ArgumentNullException("rules");
        }

        public bool TryGet(string cardId, out AnomalyDefinition definition)
        {
            definition = null;
            if (string.IsNullOrEmpty(cardId)) return false;

            lock (_sync)
            {
                CacheEntry entry;
                if (!_cache.TryGetValue(cardId, out entry))
                {
                    entry = Evaluate(cardId);
                    _cache[cardId] = entry;
                }
                if (!entry.Success || entry.Definition == null) return false;
                definition = CopyDefinition(entry.Definition);
                return definition != null;
            }
        }

        private CacheEntry Evaluate(string cardId)
        {
            try
            {
                AnomalyFact fact;
                AnomalyDefinition definition;
                if (!_facts.TryGet(cardId, out fact)
                    || fact == null
                    || !string.Equals(fact.RequestedCardId, cardId, StringComparison.Ordinal)
                    || !string.Equals(fact.AnomalyCardId, cardId, StringComparison.Ordinal)
                    || !_rules.TryEvaluate(fact, out definition)
                    || !IsValidDefinition(definition, cardId))
                    return new CacheEntry();

                return new CacheEntry
                {
                    Success = true,
                    Definition = CopyDefinition(definition),
                };
            }
            catch
            {
                return new CacheEntry();
            }
        }

        private static bool IsValidDefinition(AnomalyDefinition definition, string cardId)
        {
            return definition != null
                && string.Equals(definition.AnomalyCardId, cardId, StringComparison.Ordinal)
                && (definition.Lifecycle == "primary"
                    || definition.Lifecycle == "timewarp_effect")
                && (definition.Scope == "solo" || definition.Scope == "duo")
                && definition.Rules != null
                && definition.Rules.Count > 0;
        }

        internal static AnomalyDefinition CopyDefinition(AnomalyDefinition source)
        {
            if (source == null || source.Rules == null) return null;
            var rules = new List<AnomalyRegistry.TypedRule>(source.Rules.Count);
            foreach (var rule in source.Rules)
            {
                if (rule == null) return null;
                rules.Add(CopyRule(rule));
            }
            return new AnomalyDefinition
            {
                AnomalyCardId = source.AnomalyCardId ?? "",
                Lifecycle = source.Lifecycle ?? "",
                Scope = source.Scope ?? "",
                Rules = rules,
            };
        }

        internal static AnomalyRegistry.TypedRule CopyRule(AnomalyRegistry.TypedRule source)
        {
            return new AnomalyRegistry.TypedRule
            {
                Type = source.Type,
                IntValue = source.IntValue,
                BoolValue = source.BoolValue,
                StringValue = source.StringValue,
                Turn = source.Turn,
                EveryTurns = source.EveryTurns,
                InitialTier = source.InitialTier,
                ImprovesEveryTurns = source.ImprovesEveryTurns,
                Tier = source.Tier,
                CardId = source.CardId,
                HeroCardId = source.HeroCardId,
                CardType = source.CardType,
                Count = source.Count,
                ExplicitCount = source.ExplicitCount,
                CountEach = source.CountEach,
                GoldPerUse = source.GoldPerUse,
                MaxPerTurn = source.MaxPerTurn,
                Golden = source.Golden,
                GoldenInvalid = source.GoldenInvalid,
                UnlockTurn = source.UnlockTurn,
                CopiesPurchasedTrinket = source.CopiesPurchasedTrinket,
                GrantsTripleReward = source.GrantsTripleReward,
                Period = source.Period,
                GrantAt = source.GrantAt,
                CarryToGreater = source.CarryToGreater,
                Tiers = new List<int>(source.Tiers ?? new List<int>()),
                Turns = new List<int>(source.Turns ?? new List<int>()),
            };
        }

        private sealed class CacheEntry
        {
            public bool Success;
            public AnomalyDefinition Definition;
        }
    }
}
