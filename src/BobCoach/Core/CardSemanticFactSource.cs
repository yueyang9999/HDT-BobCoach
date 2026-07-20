using System;
using System.Collections.Generic;

namespace BobCoach.Engine
{
    internal sealed class CardSemanticFact
    {
        public string CardId;
        public List<string> Mechanics = new List<string>();
        public string TextZhCn = "";
        public string TextEnUs = "";
    }

    internal struct CardSemanticCombo
    {
        public string Mechanic { get; private set; }
        public double Weight { get; private set; }

        public CardSemanticCombo(string mechanic, double weight)
        {
            Mechanic = mechanic ?? "";
            Weight = weight;
        }
    }

    public sealed class CardSemanticsData
    {
        private readonly HashSet<string> _mechanics;
        private readonly List<CardSemanticCombo> _combos;
        private readonly HashSet<string> _providesMechanics;

        internal CardSemanticsData(
            IEnumerable<string> mechanics,
            IEnumerable<CardSemanticCombo> combos,
            IEnumerable<string> providesMechanics)
        {
            _mechanics = new HashSet<string>(mechanics ?? new string[0], StringComparer.Ordinal);
            _combos = new List<CardSemanticCombo>(combos ?? new CardSemanticCombo[0]);
            _providesMechanics = new HashSet<string>(providesMechanics ?? new string[0], StringComparer.Ordinal);
        }

        internal IReadOnlyList<CardSemanticCombo> Combos { get { return _combos; } }
        internal IReadOnlyCollection<string> ProvidesMechanics { get { return _providesMechanics; } }

        public bool HasMechanic(string mechanic)
        {
            return !string.IsNullOrEmpty(mechanic) && _mechanics.Contains(mechanic);
        }

        internal bool Provides(string mechanic)
        {
            return !string.IsNullOrEmpty(mechanic) && _providesMechanics.Contains(mechanic);
        }
    }

    internal interface ICardSemanticSource
    {
        bool TryGet(string cardId, out CardSemanticsData semantics);
    }

    internal interface ICardSemanticFactSource
    {
        bool TryGet(string cardId, out CardSemanticFact fact);
    }
}
