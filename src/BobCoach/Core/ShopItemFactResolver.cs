using System;

namespace BobCoach.Engine
{
    internal sealed class ShopItemFactResolver
    {
        private readonly ICardPurchaseFactSource _source;

        public ShopItemFactResolver(ICardPurchaseFactSource source)
        {
            _source = source;
        }

        public static bool TryCreateObservation(
            string cardId,
            ShopItemKind? cardTypeKind,
            bool isBattlegroundsSpell,
            bool hasCost,
            int observedCost,
            out ShopItemObservation observation)
        {
            observation = new ShopItemObservation();
            if (string.IsNullOrEmpty(cardId)) return false;
            if (cardTypeKind == ShopItemKind.Minion && isBattlegroundsSpell)
                return false;

            observation = new ShopItemObservation
            {
                CardId = cardId,
                ObservedKind = cardTypeKind.HasValue
                    ? cardTypeKind
                    : (isBattlegroundsSpell
                        ? (ShopItemKind?)ShopItemKind.TavernSpell : null),
                ObservedCost = hasCost ? (int?)observedCost : null,
            };
            return true;
        }

        public bool TryResolve(
            ShopItemObservation observation, out ResolvedShopItemFact resolved)
        {
            resolved = new ResolvedShopItemFact();
            if (string.IsNullOrEmpty(observation.CardId)) return false;

            CardPurchaseFact localFact;
            bool hasLocalFact = TryGetLocalFact(observation.CardId, out localFact);
            if (!observation.ObservedKind.HasValue && !hasLocalFact) return false;
            if (observation.ObservedKind.HasValue && hasLocalFact
                && observation.ObservedKind.Value != localFact.Kind)
                return false;
            ShopItemKind kind = observation.ObservedKind.HasValue
                ? observation.ObservedKind.Value : localFact.Kind;
            if (observation.ObservedCost.HasValue
                && observation.ObservedCost.Value < 0)
                return false;

            int cost = -1;
            if (kind == ShopItemKind.TavernSpell)
            {
                if (observation.ObservedCost.HasValue)
                    cost = observation.ObservedCost.Value;
                else if (hasLocalFact
                    && localFact.Kind == ShopItemKind.TavernSpell
                    && localFact.BaseCost >= 0)
                    cost = localFact.BaseCost;
                else
                    return false;
            }

            resolved = new ResolvedShopItemFact
            {
                CardId = observation.CardId,
                Kind = kind,
                Cost = cost,
            };
            return true;
        }

        private bool TryGetLocalFact(string cardId, out CardPurchaseFact fact)
        {
            fact = new CardPurchaseFact();
            if (_source == null) return false;
            try
            {
                if (!_source.TryGet(cardId, out fact)
                    || !string.Equals(fact.CardId, cardId, StringComparison.Ordinal))
                    return false;
                return fact.Kind == ShopItemKind.TavernSpell
                    ? fact.BaseCost >= 0
                    : fact.BaseCost == -1;
            }
            catch
            {
                fact = new CardPurchaseFact();
                return false;
            }
        }
    }
}
