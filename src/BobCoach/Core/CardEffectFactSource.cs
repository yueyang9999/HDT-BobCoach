using System.Collections.Generic;

namespace BobCoach.Engine
{
    internal enum CardEffectCardType
    {
        Unknown = 0,
        Minion = 1,
        TavernSpell = 2,
    }

    internal sealed class CardEffectFact
    {
        public string CardId { get; set; }
        public CardEffectCardType CardType { get; set; }
        public string TextZhCn { get; set; }
        public IReadOnlyList<int> ScriptData { get; set; }
        public int Attack { get; set; }
        public int Health { get; set; }
        public int Tier { get; set; }
    }

    internal interface ICardEffectFactSource
    {
        bool TryGet(string cardId, out CardEffectFact fact);
    }

    internal sealed class CardEffectDefinition
    {
        public string Type { get; private set; }
        public double ValueGold { get; private set; }
        public string Per { get; private set; }
        public string Tribe { get; private set; }

        public CardEffectDefinition(string type, double valueGold, string per, string tribe = null)
        {
            Type = type ?? "";
            ValueGold = valueGold;
            Per = per ?? "";
            Tribe = tribe ?? "";
        }
    }
}
