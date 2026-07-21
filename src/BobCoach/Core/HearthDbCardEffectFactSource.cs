using System;

namespace BobCoach.Engine
{
    internal sealed class HearthDbCardEffectFactSource : ICardEffectFactSource
    {
        public bool TryGet(string cardId, out CardEffectFact fact)
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

                CardEffectCardType type;
                if (card.Type == HearthDb.Enums.CardType.MINION)
                    type = CardEffectCardType.Minion;
                else if (card.Type == HearthDb.Enums.CardType.BATTLEGROUND_SPELL)
                    type = CardEffectCardType.TavernSpell;
                else
                    return false;

                fact = new CardEffectFact
                {
                    CardId = cardId,
                    CardType = type,
                    TextZhCn = card.GetLocText(HearthDb.Enums.Locale.zhCN) ?? "",
                    TextEnUs = card.GetLocText(HearthDb.Enums.Locale.enUS) ?? "",
                    ScriptData = Array.AsReadOnly(new[]
                    {
                        card.Entity.GetTag(HearthDb.Enums.GameTag.TAG_SCRIPT_DATA_NUM_1),
                        card.Entity.GetTag(HearthDb.Enums.GameTag.TAG_SCRIPT_DATA_NUM_2),
                        card.Entity.GetTag(HearthDb.Enums.GameTag.TAG_SCRIPT_DATA_NUM_3),
                        card.Entity.GetTag(HearthDb.Enums.GameTag.TAG_SCRIPT_DATA_NUM_4),
                        card.Entity.GetTag(HearthDb.Enums.GameTag.TAG_SCRIPT_DATA_NUM_5),
                        card.Entity.GetTag(HearthDb.Enums.GameTag.TAG_SCRIPT_DATA_NUM_6),
                    }),
                    Attack = type == CardEffectCardType.Minion ? card.Attack : 0,
                    Health = type == CardEffectCardType.Minion ? card.Health : 0,
                    Tier = card.TechLevel,
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

    internal sealed class HearthDbCardEffectCardIdNormalizer : ICardEffectCardIdNormalizer
    {
        public bool TryNormalize(string cardId, out string normalCardId)
        {
            normalCardId = "";
            if (string.IsNullOrEmpty(cardId)) return false;

            try
            {
                HearthDb.Card requested;
                if (!HearthDb.Cards.All.TryGetValue(cardId, out requested)
                    || requested == null
                    || !string.Equals(requested.Id, cardId, StringComparison.Ordinal))
                    return false;

                string mapped;
                if (!HearthDb.Cards.TripleToNormalCardIds.TryGetValue(cardId, out mapped))
                {
                    normalCardId = cardId;
                    return true;
                }

                HearthDb.Card normal;
                if (string.IsNullOrEmpty(mapped)
                    || !HearthDb.Cards.All.TryGetValue(mapped, out normal)
                    || normal == null
                    || !string.Equals(normal.Id, mapped, StringComparison.Ordinal))
                    return false;
                normalCardId = mapped;
                return true;
            }
            catch
            {
                normalCardId = "";
                return false;
            }
        }
    }
}
