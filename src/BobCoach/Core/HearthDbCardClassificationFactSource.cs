using System;
using System.Collections.Generic;

namespace BobCoach.Engine
{
    internal sealed class HearthDbCardClassificationFactSource
        : ICardClassificationFactSource
    {
        public bool TryGet(string cardId, out CardClassificationFact fact)
        {
            fact = null;
            if (string.IsNullOrEmpty(cardId)) return false;

            try
            {
                HearthDb.Card card;
                if (!HearthDb.Cards.All.TryGetValue(cardId, out card)
                    || card == null
                    || !string.Equals(card.Id, cardId, StringComparison.Ordinal))
                    return false;

                CardClassificationCardType cardType;
                if (card.Type == HearthDb.Enums.CardType.MINION)
                    cardType = CardClassificationCardType.Minion;
                else if (card.Type == HearthDb.Enums.CardType.BATTLEGROUND_SPELL)
                    cardType = CardClassificationCardType.BattlegroundSpell;
                else
                    return false;

                fact = new CardClassificationFact
                {
                    CardId = card.Id,
                    CardType = cardType,
                    Mechanics = new List<string>(card.Mechanics ?? new string[0]),
                    TextZhCn = card.GetLocText(HearthDb.Enums.Locale.zhCN) ?? "",
                };
                return true;
            }
            catch
            {
                fact = null;
                return false;
            }
        }
    }
}
