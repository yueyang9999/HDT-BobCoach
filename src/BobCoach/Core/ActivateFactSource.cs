using System;

namespace BobCoach.Engine
{
    internal sealed class ActivateFact
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

    internal interface IActivateFactSource
    {
        bool TryGet(string cardId, out ActivateFact fact);
    }

    internal sealed class ActivateFactSource : IActivateFactSource
    {
        private readonly ActivateRegistry _registry;

        internal ActivateFactSource()
        {
            _registry = new ActivateRegistry();
            _registry.LoadFromConfiguredRoot();
        }

        internal ActivateFactSource(ActivateRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException("registry");
        }

        public bool TryGet(string cardId, out ActivateFact fact)
        {
            fact = new ActivateFact();
            Season14MechanicCardFact source;
            if (!_registry.TryGet(cardId, out source)
                || !string.Equals(source.CardId, cardId, StringComparison.Ordinal))
                return false;

            fact = new ActivateFact
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
