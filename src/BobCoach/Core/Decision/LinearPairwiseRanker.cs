using System;
using System.Collections.Generic;
using System.Linq;

namespace BobCoach.Engine.Decision
{
    public sealed class ScoredFeatureVector
    {
        public double Score { get; private set; }
        public double[] Contributions { get; private set; }

        public ScoredFeatureVector(double score, double[] contributions)
        {
            Score = score;
            Contributions = contributions ?? new double[0];
        }
    }

    public sealed class LinearRankedCandidate
    {
        public ActionDescriptor Action { get; private set; }
        public ActionFeatureVector Features { get; private set; }

        public LinearRankedCandidate(ActionDescriptor action, ActionFeatureVector features)
        {
            if (action == null) throw new ArgumentNullException("action");
            if (features == null) throw new ArgumentNullException("features");
            Action = action;
            Features = features;
        }
    }

    public sealed class LinearPairwiseRanker
    {
        private readonly RankingModelPayload _model;

        public LinearPairwiseRanker(RankingModelPayload model)
        {
            ValidateModel(model);
            _model = model;
        }

        public ScoredFeatureVector Score(ActionDescriptor action, ActionFeatureVector vector)
        {
            if (action == null || action.Action == null) throw new ArgumentException("action");
            if (vector == null || vector.Values == null || vector.Values.Length != _model.Weights.Length)
                throw new ArgumentException("vector");

            var contributions = new double[_model.Weights.Length];
            var total = _model.Intercept + ActionTypeBias(action.Action.Type);
            for (var index = 0; index < contributions.Length; index++)
            {
                var value = vector.Values[index];
                if (!IsFinite(value)) throw new ArgumentException("vector");
                var normalized = (value - _model.Means[index]) / _model.StandardDeviations[index];
                if (!IsFinite(normalized)) throw new ArgumentException("vector");
                contributions[index] = normalized * _model.Weights[index];
                total += contributions[index];
            }
            if (!IsFinite(total)) throw new ArgumentException("score");
            return new ScoredFeatureVector(total, contributions);
        }

        public IList<RankedAction> Rank(IEnumerable<LinearRankedCandidate> candidates)
        {
            if (candidates == null) return new List<RankedAction>();

            return candidates.Select(candidate =>
            {
                if (candidate == null) throw new ArgumentException("candidate");
                var scored = Score(candidate.Action, candidate.Features);
                var factors = new List<DecisionFactor>();
                for (var index = 0; index < scored.Contributions.Length; index++)
                    factors.Add(new DecisionFactor(ActionFeatureSchema.Names[index], scored.Contributions[index]));
                return new RankedAction(candidate.Action.Action, candidate.Action.ActionKey, scored.Score, factors);
            }).OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.ActionKey ?? "", StringComparer.Ordinal)
                .ToList();
        }

        private double ActionTypeBias(ActionType type)
        {
            var oneHot = ActionTypeBiases.OneHot(type);
            var total = 0d;
            for (var index = 0; index < oneHot.Length; index++) total += oneHot[index] * _model.ActionTypeBiases[index];
            return total;
        }

        private static void ValidateModel(RankingModelPayload model)
        {
            if (model == null) throw new ArgumentNullException("model");
            var count = ActionFeatureSchema.Names.Count;
            ValidateFiniteArray(model.Weights, count, "weights", false);
            ValidateFiniteArray(model.Means, count, "means", false);
            ValidateFiniteArray(model.StandardDeviations, count, "standard deviations", true);
            ValidateFiniteArray(model.ActionTypeBiases, ActionTypeBiases.OrderedTypes.Count,
                "action type biases", false);
            if (!IsFinite(model.Intercept)) throw new ArgumentException("intercept");
        }

        private static void ValidateFiniteArray(double[] values, int expectedCount, string name,
            bool strictlyPositive)
        {
            if (values == null || values.Length != expectedCount) throw new ArgumentException(name);
            for (var index = 0; index < values.Length; index++)
            {
                if (!IsFinite(values[index]) || (strictlyPositive && values[index] <= 0d))
                    throw new ArgumentException(name);
            }
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
