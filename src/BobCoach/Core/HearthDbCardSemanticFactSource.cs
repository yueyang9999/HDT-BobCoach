using System;
using System.Collections.Generic;

namespace BobCoach.Engine
{
    internal sealed class HearthDbCardSemanticFactSource : ICardSemanticFactSource
    {
        public bool TryGet(string cardId, out CardSemanticFact fact)
        {
            fact = null;
            if (string.IsNullOrEmpty(cardId)) return false;

            try
            {
                HearthDb.Card card;
                if (!HearthDb.Cards.All.TryGetValue(cardId, out card)
                    || card == null
                    || !string.Equals(card.Id, cardId, StringComparison.Ordinal)
                    || card.Type != HearthDb.Enums.CardType.MINION)
                    return false;

                fact = new CardSemanticFact
                {
                    CardId = card.Id,
                    Mechanics = new List<string>(card.Mechanics ?? new string[0]),
                    TextZhCn = card.GetLocText(HearthDb.Enums.Locale.zhCN) ?? "",
                    TextEnUs = card.GetLocText(HearthDb.Enums.Locale.enUS) ?? "",
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
