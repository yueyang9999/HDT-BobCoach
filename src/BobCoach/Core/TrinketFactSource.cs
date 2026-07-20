using System;
using System.Collections.Generic;

namespace BobCoach.Engine
{
    internal interface ITrinketFactSource
    {
        bool TryGet(string cardId, out TrinketFact fact);
    }

    internal struct TrinketFact
    {
        public string CardId;
        public bool IsLesser;
        public int Cost;
        public string NameZhCn;
        public string NameEnUs;
        public string TextZhCn;
        public string TextEnUs;
    }

    internal sealed class CachedTrinketFactSource : ITrinketFactSource
    {
        private readonly ITrinketFactSource _inner;
        private readonly object _sync = new object();
        private readonly Dictionary<string, TrinketFact> _facts
            = new Dictionary<string, TrinketFact>(StringComparer.Ordinal);

        public CachedTrinketFactSource(ITrinketFactSource inner)
        {
            _inner = inner;
        }

        public bool TryGet(string cardId, out TrinketFact fact)
        {
            fact = new TrinketFact();
            if (string.IsNullOrEmpty(cardId)) return false;

            lock (_sync)
            {
                if (_facts.TryGetValue(cardId, out fact)) return true;

                try
                {
                    if (_inner == null || !_inner.TryGet(cardId, out fact)
                        || !string.Equals(fact.CardId, cardId, StringComparison.Ordinal))
                    {
                        fact = new TrinketFact();
                        return false;
                    }
                }
                catch
                {
                    fact = new TrinketFact();
                    return false;
                }

                _facts[cardId] = fact;
                return true;
            }
        }
    }
}
