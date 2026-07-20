using System;

namespace BobCoach.Engine
{
    internal sealed class HearthDbPrizeSpellFactSource : IPrizeSpellFactSource
    {
        public bool TryGet(string cardId, out PrizeSpellFact fact)
        {
            fact = null;
            if (string.IsNullOrEmpty(cardId)) return false;

            try
            {
                HearthDb.Card card;
                if (!HearthDb.Cards.All.TryGetValue(cardId, out card)
                    || card == null
                    || card.Entity == null
                    || !string.Equals(card.Id, cardId, StringComparison.Ordinal))
                    return false;

                int prizeTier = card.Entity.GetTag(
                    HearthDb.Enums.GameTag.BATTLEGROUNDS_DARKMOON_PRIZE_TURN);
                if (prizeTier < 1 || prizeTier > 4 || card.TechLevel != prizeTier)
                    return false;

                PrizeSpellCardType cardType;
                if (card.Type == HearthDb.Enums.CardType.SPELL)
                    cardType = PrizeSpellCardType.Spell;
                else if (card.Type == HearthDb.Enums.CardType.MINION)
                    cardType = PrizeSpellCardType.Minion;
                else
                    return false;

                var scriptData = Array.AsReadOnly(new[]
                {
                    card.Entity.GetTag(HearthDb.Enums.GameTag.TAG_SCRIPT_DATA_NUM_1),
                    card.Entity.GetTag(HearthDb.Enums.GameTag.TAG_SCRIPT_DATA_NUM_2),
                    card.Entity.GetTag(HearthDb.Enums.GameTag.TAG_SCRIPT_DATA_NUM_3),
                    card.Entity.GetTag(HearthDb.Enums.GameTag.TAG_SCRIPT_DATA_NUM_4),
                    card.Entity.GetTag(HearthDb.Enums.GameTag.TAG_SCRIPT_DATA_NUM_5),
                    card.Entity.GetTag(HearthDb.Enums.GameTag.TAG_SCRIPT_DATA_NUM_6),
                });
                fact = new PrizeSpellFact
                {
                    CardId = card.Id,
                    CardType = cardType,
                    PrizeTier = prizeTier,
                    TechLevel = card.TechLevel,
                    TextZhCn = card.GetLocText(HearthDb.Enums.Locale.zhCN) ?? "",
                    ScriptData = scriptData,
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
