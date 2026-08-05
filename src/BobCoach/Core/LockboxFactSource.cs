using System;

namespace BobCoach.Engine
{
    internal sealed class LockboxFact
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

    internal interface ILockboxFactSource
    {
        bool TryGet(string cardId, out LockboxFact fact);
    }

    internal sealed class LockboxFactSource : ILockboxFactSource
    {
        private readonly LockboxRegistry _registry;

        internal LockboxFactSource()
        {
            _registry = new LockboxRegistry();
            _registry.LoadFromConfiguredRoot();
        }

        internal LockboxFactSource(LockboxRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException("registry");
        }

        public bool TryGet(string cardId, out LockboxFact fact)
        {
            fact = new LockboxFact();
            Season14MechanicCardFact source;
            if (!_registry.TryGet(cardId, out source)
                || !string.Equals(source.CardId, cardId, StringComparison.Ordinal))
                return false;

            fact = new LockboxFact
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
