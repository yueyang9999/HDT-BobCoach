using System;
using System.Collections.Generic;
using System.Linq;

namespace BobCoach.Engine
{
    internal interface ICardEffectSource
    {
        bool TryGet(string cardId, out IReadOnlyList<CardEffectDefinition> effects);
    }

    internal interface ICardEffectCardIdNormalizer
    {
        bool TryNormalize(string cardId, out string normalCardId);
    }

    internal sealed class CachedCardEffectSource : ICardEffectSource
    {
        private static readonly IReadOnlyList<CardEffectDefinition> Empty
            = Array.AsReadOnly(new CardEffectDefinition[0]);

        private readonly ICardEffectFactSource _facts;
        private readonly ICardEffectRuleEvaluator _rules;
        private readonly ICardEffectCardIdNormalizer _normalizer;
        private readonly object _gate = new object();
        private readonly Dictionary<string, CacheEntry> _cache
            = new Dictionary<string, CacheEntry>(StringComparer.Ordinal);
        private readonly HashSet<string> _normalizationFailures
            = new HashSet<string>(StringComparer.Ordinal);

        public CachedCardEffectSource(
            ICardEffectFactSource facts,
            ICardEffectRuleEvaluator rules,
            ICardEffectCardIdNormalizer normalizer)
        {
            _facts = facts ?? throw new ArgumentNullException(nameof(facts));
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
            _normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
        }

        public bool TryGet(string cardId, out IReadOnlyList<CardEffectDefinition> effects)
        {
            effects = Empty;
            if (string.IsNullOrEmpty(cardId)) return false;

            lock (_gate)
            {
                if (_normalizationFailures.Contains(cardId)) return false;
                string normalCardId;
                try
                {
                    if (!_normalizer.TryNormalize(cardId, out normalCardId)
                        || string.IsNullOrEmpty(normalCardId))
                    {
                        _normalizationFailures.Add(cardId);
                        return false;
                    }
                }
                catch
                {
                    _normalizationFailures.Add(cardId);
                    return false;
                }

                CacheEntry cached;
                if (_cache.TryGetValue(normalCardId, out cached))
                {
                    effects = cached.Effects;
                    return cached.Success;
                }

                CardEffectFact fact;
                IReadOnlyList<CardEffectDefinition> derived = Empty;
                bool success;
                try
                {
                    success = _facts.TryGet(normalCardId, out fact)
                        && fact != null
                        && string.Equals(fact.CardId, normalCardId, StringComparison.Ordinal)
                        && _rules.TryEvaluate(fact, out derived);
                }
                catch
                {
                    success = false;
                    derived = Empty;
                }

                var snapshot = success && derived != null
                    ? Array.AsReadOnly(derived.ToArray())
                    : Empty;
                cached = new CacheEntry { Success = success, Effects = snapshot };
                _cache[normalCardId] = cached;
                effects = snapshot;
                return success;
            }
        }

        private sealed class CacheEntry
        {
            public bool Success;
            public IReadOnlyList<CardEffectDefinition> Effects;
        }
    }
}
