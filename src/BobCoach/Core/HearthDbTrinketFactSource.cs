using System;

namespace BobCoach.Engine
{
    internal sealed class HearthDbTrinketFactSource : ITrinketFactSource
    {
        public bool TryGet(string cardId, out TrinketFact fact)
        {
            fact = new TrinketFact();
            if (string.IsNullOrEmpty(cardId)) return false;

            try
            {
                HearthDb.Card card;
                if (!HearthDb.Cards.All.TryGetValue(cardId, out card)
                    || card == null
                    || !string.Equals(card.Id, cardId, StringComparison.Ordinal)
                    || card.Type != HearthDb.Enums.CardType.BATTLEGROUND_TRINKET
                    || (card.SpellSchool != 11 && card.SpellSchool != 12))
                    return false;

                string nameZhCn = "";
                string nameEnUs = "";
                string textZhCn = "";
                string textEnUs = "";
                foreach (var tag in card.Entity.Tags)
                {
                    if (tag == null) continue;
                    if (tag.Name == "CARDNAME")
                    {
                        nameZhCn = tag.LocStringZhCn ?? "";
                        nameEnUs = tag.LocStringEnUs ?? "";
                    }
                    else if (tag.Name == "CARDTEXT")
                    {
                        textZhCn = tag.LocStringZhCn ?? "";
                        textEnUs = tag.LocStringEnUs ?? "";
                    }
                }

                fact = new TrinketFact
                {
                    CardId = card.Id,
                    IsLesser = card.SpellSchool == 11,
                    Cost = card.Cost,
                    NameZhCn = nameZhCn,
                    NameEnUs = nameEnUs,
                    TextZhCn = textZhCn,
                    TextEnUs = textEnUs,
                };
                return true;
            }
            catch
            {
                fact = new TrinketFact();
                return false;
            }
        }
    }
}
