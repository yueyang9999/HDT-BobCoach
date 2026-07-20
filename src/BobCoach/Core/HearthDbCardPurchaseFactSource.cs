using System;

namespace BobCoach.Engine
{
    internal sealed class HearthDbCardPurchaseFactSource : ICardPurchaseFactSource
    {
        public bool TryGet(string cardId, out CardPurchaseFact fact)
        {
            fact = new CardPurchaseFact();
            if (string.IsNullOrEmpty(cardId)) return false;

            try
            {
                HearthDb.Card card;
                if (!HearthDb.Cards.All.TryGetValue(cardId, out card)
                    || card == null
                    || !string.Equals(card.Id, cardId, StringComparison.Ordinal))
                    return false;

                bool isMinion = card.Type == HearthDb.Enums.CardType.MINION;
                bool isSpell = card.Type == HearthDb.Enums.CardType.SPELL
                    || card.Type == HearthDb.Enums.CardType.BATTLEGROUND_SPELL;
                if (!isMinion && !isSpell) return false;
                if (isSpell && card.Cost < 0) return false;

                fact = new CardPurchaseFact
                {
                    CardId = card.Id,
                    Kind = isSpell ? ShopItemKind.TavernSpell : ShopItemKind.Minion,
                    BaseCost = isSpell ? card.Cost : -1,
                };
                return true;
            }
            catch
            {
                fact = new CardPurchaseFact();
                return false;
            }
        }
    }
}
