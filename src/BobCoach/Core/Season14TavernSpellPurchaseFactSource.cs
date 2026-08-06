using System;

namespace BobCoach.Engine
{
    internal sealed class Season14TavernSpellPurchaseFactSource
        : ICardPurchaseFactSource
    {
        private readonly Season14TavernSpellRegistry _registry;

        internal Season14TavernSpellPurchaseFactSource()
        {
            _registry = new Season14TavernSpellRegistry();
            _registry.LoadFromConfiguredRoot();
        }

        internal Season14TavernSpellPurchaseFactSource(
            Season14TavernSpellRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException("registry");
        }

        public bool TryGet(string cardId, out CardPurchaseFact fact)
        {
            fact = new CardPurchaseFact();
            Season14MechanicCardFact cardFact;
            if (!_registry.TryGetPoolSpell(cardId, out cardFact))
                return false;
            fact = new CardPurchaseFact
            {
                CardId = cardId,
                Kind = ShopItemKind.TavernSpell,
                BaseCost = cardFact.Cost,
            };
            return true;
        }
    }
}
