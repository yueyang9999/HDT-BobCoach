using System;
using System.Collections.Generic;

namespace BobCoach.Engine
{
    internal sealed class SemanticSynergyEvaluator
    {
        private const double WeightedShopMax = 12.0;
        private readonly ICardSemanticSource _source;

        public SemanticSynergyEvaluator(ICardSemanticSource source)
        {
            _source = source;
        }

        public float ComputeWeightedShopScore(
            IEnumerable<string> boardCardIds,
            IEnumerable<string> shopCardIds)
        {
            var providers = CollectProviders(boardCardIds);
            if (providers.Count == 0 || shopCardIds == null) return 0f;

            double total = 0;
            foreach (string cardId in shopCardIds)
            {
                CardSemanticsData semantics;
                if (!TryGet(cardId, out semantics)) continue;
                foreach (CardSemanticCombo combo in semantics.Combos)
                    if (providers.Contains(combo.Mechanic)) total += combo.Weight;
            }
            return (float)Math.Min(1.0, total / WeightedShopMax);
        }

        public float ComputeMatchRatio(
            IEnumerable<string> boardCardIds,
            string candidateCardId)
        {
            var providers = CollectProviders(boardCardIds);
            if (providers.Count == 0) return 0f;

            CardSemanticsData semantics;
            if (!TryGet(candidateCardId, out semantics) || semantics.Combos.Count == 0)
                return 0f;

            int matches = 0;
            foreach (CardSemanticCombo combo in semantics.Combos)
                if (providers.Contains(combo.Mechanic)) matches++;
            return matches > 0
                ? Math.Min(1f, matches / (float)semantics.Combos.Count)
                : 0f;
        }

        private HashSet<string> CollectProviders(IEnumerable<string> cardIds)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            if (cardIds == null) return result;
            foreach (string cardId in cardIds)
            {
                CardSemanticsData semantics;
                if (!TryGet(cardId, out semantics)) continue;
                foreach (string provider in semantics.ProvidesMechanics)
                    result.Add(provider);
            }
            return result;
        }

        private bool TryGet(string cardId, out CardSemanticsData semantics)
        {
            semantics = null;
            if (_source == null || string.IsNullOrEmpty(cardId)) return false;
            try { return _source.TryGet(cardId, out semantics) && semantics != null; }
            catch { semantics = null; return false; }
        }
    }
}
