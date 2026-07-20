using System;
using System.Collections.Generic;

namespace BobCoach.Engine
{
    internal sealed class CachedCardClassificationSource : ICardClassificationSource
    {
        private readonly ICardClassificationFactSource _factSource;
        private readonly ICardSemanticSource _semanticSource;
        private readonly ICardClassificationEvaluator _evaluator;
        private readonly object _gate = new object();
        private readonly Dictionary<string, CardClassifier.CardClassification> _cache
            = new Dictionary<string, CardClassifier.CardClassification>(StringComparer.Ordinal);
        private readonly HashSet<string> _missing
            = new HashSet<string>(StringComparer.Ordinal);

        public CachedCardClassificationSource(
            ICardClassificationFactSource factSource,
            ICardSemanticSource semanticSource,
            ICardClassificationEvaluator evaluator)
        {
            _factSource = factSource;
            _semanticSource = semanticSource;
            _evaluator = evaluator;
        }

        public bool TryGet(
            string cardId, out CardClassifier.CardClassification classification)
        {
            classification = new CardClassifier.CardClassification();
            if (string.IsNullOrEmpty(cardId)) return false;
            lock (_gate)
            {
                if (_cache.TryGetValue(cardId, out classification)) return true;
                if (_missing.Contains(cardId)) return false;
                if (_factSource == null || _evaluator == null)
                {
                    _missing.Add(cardId);
                    return false;
                }

                try
                {
                    CardClassificationFact fact;
                    if (!_factSource.TryGet(cardId, out fact) || fact == null
                        || !string.Equals(fact.CardId, cardId, StringComparison.Ordinal))
                        return MarkMissing(cardId, out classification);

                    CardSemanticsData semantics = null;
                    if (fact.CardType == CardClassificationCardType.Minion
                        && (_semanticSource == null
                            || !_semanticSource.TryGet(cardId, out semantics)
                            || semantics == null))
                        return MarkMissing(cardId, out classification);

                    if (!_evaluator.TryEvaluate(fact, semantics, out classification))
                        return MarkMissing(cardId, out classification);
                    _cache[cardId] = classification;
                    return true;
                }
                catch
                {
                    return MarkMissing(cardId, out classification);
                }
            }
        }

        private bool MarkMissing(
            string cardId, out CardClassifier.CardClassification classification)
        {
            classification = new CardClassifier.CardClassification();
            _missing.Add(cardId);
            return false;
        }
    }
}
