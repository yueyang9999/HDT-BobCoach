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
