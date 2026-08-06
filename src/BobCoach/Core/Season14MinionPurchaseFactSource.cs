using System;

namespace BobCoach.Engine
{
    internal sealed class Season14MinionPurchaseFactSource : ICardPurchaseFactSource
    {
        private readonly Season14MinionRegistry _registry;

        internal Season14MinionPurchaseFactSource()
        {
            _registry = new Season14MinionRegistry();
            _registry.LoadFromConfiguredRoot();
        }

        internal Season14MinionPurchaseFactSource(Season14MinionRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException("registry");
        }

        public bool TryGet(string cardId, out CardPurchaseFact fact)
        {
            fact = new CardPurchaseFact();
            Season14MechanicCardFact ignored;
            if (!_registry.TryGetPoolMinion(cardId, out ignored))
                return false;
            fact = new CardPurchaseFact
            {
                CardId = cardId,
                Kind = ShopItemKind.Minion,
                BaseCost = -1,
            };
            return true;
        }
    }
}
