using System;

namespace BobCoach.Engine
{
    internal sealed class FishbaitFact
    {
        internal string CardId;
        internal string NameZhCn;
        internal string NameEnUs;
        internal string TextZhCn;
        internal string TextEnUs;
        internal string Type;
        internal int Cost;
        internal int TechLevel;
        internal int Attack;
        internal int Health;
        internal bool IsBattlegroundsPoolMinion;
    }

    internal interface IFishbaitFactSource
    {
        bool TryGet(string cardId, out FishbaitFact fact);
    }

    internal sealed class FishbaitFactSource : IFishbaitFactSource
    {
        private readonly FishbaitRegistry _registry;

        internal FishbaitFactSource()
        {
            _registry = new FishbaitRegistry();
            _registry.LoadFromConfiguredRoot();
        }

        internal FishbaitFactSource(FishbaitRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException("registry");
        }

        public bool TryGet(string cardId, out FishbaitFact fact)
        {
            fact = new FishbaitFact();
            Season14MechanicCardFact source;
            if (!_registry.TryGet(cardId, out source)
                || !string.Equals(source.CardId, cardId, StringComparison.Ordinal))
                return false;

            fact = new FishbaitFact
            {
                CardId = source.CardId,
                NameZhCn = source.NameZhCn,
                NameEnUs = source.NameEnUs,
                TextZhCn = source.TextZhCn,
                TextEnUs = source.TextEnUs,
                Type = source.Type,
                Cost = source.Cost,
                TechLevel = source.TechLevel,
                Attack = source.Attack,
                Health = source.Health,
                IsBattlegroundsPoolMinion = source.IsBattlegroundsPoolMinion,
            };
            return true;
        }
    }
}
