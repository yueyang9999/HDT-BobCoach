using System;
using System.Collections.Generic;

namespace BobCoach.Engine
{
    internal sealed class CachedCardSemanticSource : ICardSemanticSource
    {
        private readonly ICardSemanticFactSource _factSource;
        private readonly CardSemanticRuleEvaluator _evaluator;
        private readonly object _gate = new object();
        private readonly Dictionary<string, CardSemanticsData> _cache
            = new Dictionary<string, CardSemanticsData>(StringComparer.Ordinal);
        private readonly HashSet<string> _missing
            = new HashSet<string>(StringComparer.Ordinal);

        public CachedCardSemanticSource(
            ICardSemanticFactSource factSource,
            CardSemanticRuleEvaluator evaluator)
        {
            _factSource = factSource;
            _evaluator = evaluator;
        }

        public bool TryGet(string cardId, out CardSemanticsData semantics)
        {
            semantics = null;
            if (string.IsNullOrEmpty(cardId)) return false;
            lock (_gate)
            {
                if (_cache.TryGetValue(cardId, out semantics)) return true;
                if (_missing.Contains(cardId)) return false;
                if (_factSource == null || _evaluator == null) return false;

                try
                {
                    CardSemanticFact fact;
                    if (!_factSource.TryGet(cardId, out fact) || fact == null
                        || !string.Equals(fact.CardId, cardId, StringComparison.Ordinal))
                    {
                        _missing.Add(cardId);
                        return false;
                    }
                    semantics = _evaluator.Evaluate(fact);
                    if (semantics == null)
                    {
                        _missing.Add(cardId);
                        return false;
                    }
                    _cache[cardId] = semantics;
                    return true;
                }
                catch
                {
                    semantics = null;
                    _missing.Add(cardId);
                    return false;
                }
            }
        }
    }
}
