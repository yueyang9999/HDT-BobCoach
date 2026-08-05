using System;

namespace BobCoach.Engine
{
    internal sealed class DarkGiftFact
    {
        internal string CardId;
        internal string NameZhCn;
        internal string NameEnUs;
        internal string Type;
        internal string TextZhCn;
        internal string TextEnUs;
    }

    internal interface IDarkGiftFactSource
    {
        bool TryGet(string cardId, out DarkGiftFact fact);
    }

    internal sealed class DarkGiftFactSource : IDarkGiftFactSource
    {
        private readonly DarkGiftRegistry _registry;

        internal DarkGiftFactSource()
        {
            _registry = new DarkGiftRegistry();
            _registry.LoadFromConfiguredRoot();
        }

        internal DarkGiftFactSource(DarkGiftRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException("registry");
        }

        public bool TryGet(string cardId, out DarkGiftFact fact)
        {
            fact = new DarkGiftFact();
            DarkGiftRegistry.DarkGiftEffectFact source;
            if (!_registry.TryGet(cardId, out source)
                || !string.Equals(source.CardId, cardId, StringComparison.Ordinal))
                return false;

            fact = new DarkGiftFact
            {
                CardId = source.CardId,
                NameZhCn = source.NameZhCn,
                NameEnUs = source.NameEnUs,
                Type = source.Type,
                TextZhCn = source.TextZhCn,
                TextEnUs = source.TextEnUs,
            };
            return true;
        }
    }
}
