using System;
using System.Collections.Generic;
using System.Text;

namespace BobCoach.Engine
{
    internal static class CardClassificationTextNormalizer
    {
        public static string Normalize(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            var result = new StringBuilder(value.Length);
            bool insideTag = false;
            bool pendingSpace = false;
            foreach (char c in value)
            {
                if (c == '<')
                {
                    insideTag = true;
                    continue;
                }
                if (c == '>')
                {
                    insideTag = false;
                    continue;
                }
                if (insideTag) continue;
                if (char.IsWhiteSpace(c))
                {
                    pendingSpace = result.Length > 0;
                    continue;
                }
                if (pendingSpace)
                {
                    result.Append(' ');
                    pendingSpace = false;
                }
                result.Append(c);
            }
            return result.ToString();
        }
    }

    internal sealed class CardClassificationRuleEvaluator : ICardClassificationEvaluator
    {
        private readonly CardClassifier _classifier = new CardClassifier();

        public bool TryEvaluate(
            CardClassificationFact fact,
            CardSemanticsData semantics,
            out CardClassifier.CardClassification classification)
        {
            classification = new CardClassifier.CardClassification();
            if (fact == null || string.IsNullOrEmpty(fact.CardId)
                || (fact.CardType != CardClassificationCardType.Minion
                    && fact.CardType != CardClassificationCardType.BattlegroundSpell))
                return false;

            var mechanics = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string mechanic in fact.Mechanics ?? new List<string>())
                if (!string.IsNullOrEmpty(mechanic) && seen.Add(mechanic))
                    mechanics.Add(mechanic);

            if (fact.CardType == CardClassificationCardType.Minion)
            {
                if (semantics == null) return false;
                AddSemanticMechanic(semantics, "BATTLECRY", mechanics, seen);
                AddSemanticMechanic(semantics, "DEATHRATTLE", mechanics, seen);
                AddSemanticMechanic(semantics, "END_OF_TURN", mechanics, seen);
                AddSemanticMechanic(semantics, "SUMMON", mechanics, seen);
            }

            classification = _classifier.Classify(
                fact.CardId,
                CardClassificationTextNormalizer.Normalize(fact.TextZhCn),
                mechanics);
            return true;
        }

        private static void AddSemanticMechanic(
            CardSemanticsData semantics,
            string mechanic,
            List<string> mechanics,
            HashSet<string> seen)
        {
            if (semantics.HasMechanic(mechanic) && seen.Add(mechanic))
                mechanics.Add(mechanic);
        }
    }
}
