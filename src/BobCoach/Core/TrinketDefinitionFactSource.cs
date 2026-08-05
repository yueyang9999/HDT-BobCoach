using System;

namespace BobCoach.Engine
{
    /// <summary>Season14 generated trinket registry as an exact CardId fact source.</summary>
    internal sealed class TrinketDefinitionFactSource : ITrinketFactSource
    {
        private readonly TrinketDefinitionRegistry _registry;

        internal TrinketDefinitionFactSource()
        {
            _registry = new TrinketDefinitionRegistry();
            _registry.LoadFromConfiguredRoot();
        }

        internal TrinketDefinitionFactSource(TrinketDefinitionRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException("registry");
        }

        public bool TryGet(string cardId, out TrinketFact fact)
        {
            fact = new TrinketFact();
            TrinketDefinitionRegistry.TrinketDefinition definition;
            if (!_registry.TryGet(cardId, out definition)
                || !string.Equals(definition.CardId, cardId, StringComparison.Ordinal))
                return false;

            fact = new TrinketFact
            {
                CardId = definition.CardId,
                IsLesser = definition.IsLesser,
                Cost = definition.Cost,
                NameZhCn = definition.NameCn,
                NameEnUs = definition.NameEn,
                TextZhCn = definition.TextCn,
                TextEnUs = definition.TextEn,
            };
            return true;
        }
    }
}
