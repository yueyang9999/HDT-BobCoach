using System;

namespace BobCoach.Engine
{
    internal enum ShopItemKind
    {
        Minion,
        TavernSpell,
    }

    internal struct CardPurchaseFact
    {
        public string CardId;
        public ShopItemKind Kind;
        public int BaseCost;
    }

    internal interface ICardPurchaseFactSource
    {
        bool TryGet(string cardId, out CardPurchaseFact fact);
    }

    internal sealed class CompositeCardPurchaseFactSource : ICardPurchaseFactSource
    {
        private readonly ICardPurchaseFactSource[] _sources;

        internal CompositeCardPurchaseFactSource(params ICardPurchaseFactSource[] sources)
        {
            _sources = sources ?? new ICardPurchaseFactSource[0];
        }

        public bool TryGet(string cardId, out CardPurchaseFact fact)
        {
            fact = new CardPurchaseFact();
            foreach (var source in _sources)
            {
                if (source == null) continue;
                CardPurchaseFact candidate;
                if (source.TryGet(cardId, out candidate)
                    && string.Equals(candidate.CardId, cardId, StringComparison.Ordinal))
                {
                    fact = candidate;
                    return true;
                }
            }
            return false;
        }
    }

    internal struct ShopItemObservation
    {
        public string CardId;
        public ShopItemKind? ObservedKind;
        public int? ObservedCost;
    }

    internal struct ResolvedShopItemFact
    {
        public string CardId;
        public ShopItemKind Kind;
        public int Cost;
    }
}
