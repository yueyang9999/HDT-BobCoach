using System;
using System.Collections.Generic;

namespace BobCoach.Engine
{
    internal sealed class TrinketRecommendation
    {
        public int Index;
        public string CardId;
        public string DisplayName;
        public int RuleScore;
        public bool IsLesser;
        public bool IsUnrated;
        public List<string> MatchedRuleIds = new List<string>();
    }

    internal sealed class TrinketRecommendationService
    {
        private readonly ITrinketFactSource _source;
        private readonly TrinketRuleEvaluator _evaluator;

        public TrinketRecommendationService(
            ITrinketFactSource source, TrinketRuleEvaluator evaluator)
        {
            _source = source;
            _evaluator = evaluator;
        }

        public List<TrinketRecommendation> Evaluate(
            IList<TrinketOption> offers, string dominantTribe)
        {
            var results = new List<TrinketRecommendation>();
            if (offers == null) return results;

            for (int index = 0; index < offers.Count; index++)
            {
                TrinketOption offer = offers[index];
                if (offer == null) continue;
                if (!string.IsNullOrEmpty(offer.CardId)
                    && offer.CardId.StartsWith("__TRINKET_PENDING", StringComparison.Ordinal))
                    continue;
                var recommendation = new TrinketRecommendation
                {
                    Index = index,
                    CardId = offer.CardId ?? "",
                    DisplayName = !string.IsNullOrEmpty(offer.TrinketName)
                        ? offer.TrinketName : offer.CardId ?? "",
                    IsLesser = offer.IsLesser,
                    IsUnrated = true,
                };

                TrinketFact fact;
                try
                {
                    if (_source != null && _evaluator != null
                        && _source.TryGet(offer.CardId, out fact)
                        && string.Equals(fact.CardId, offer.CardId, StringComparison.Ordinal))
                    {
                        TrinketEvaluation evaluation = _evaluator.Evaluate(fact, dominantTribe);
                        if (evaluation.IsValid)
                        {
                            recommendation.DisplayName = evaluation.DisplayName;
                            recommendation.IsLesser = evaluation.IsLesser;
                            recommendation.IsUnrated = !evaluation.IsRated;
                            recommendation.RuleScore = evaluation.RuleScore;
                            recommendation.MatchedRuleIds.AddRange(evaluation.MatchedRuleIds);
                        }
                    }
                }
                catch
                {
                    recommendation.IsUnrated = true;
                    recommendation.RuleScore = 0;
                    recommendation.MatchedRuleIds.Clear();
                }
                results.Add(recommendation);
            }

            results.Sort(Compare);
            return results;
        }

        private static int Compare(
            TrinketRecommendation left, TrinketRecommendation right)
        {
            if (left.IsUnrated != right.IsUnrated) return left.IsUnrated ? 1 : -1;
            int score = right.RuleScore.CompareTo(left.RuleScore);
            if (score != 0) return score;
            int cardId = string.CompareOrdinal(left.CardId ?? "", right.CardId ?? "");
            return cardId != 0 ? cardId : left.Index.CompareTo(right.Index);
        }
    }
}
