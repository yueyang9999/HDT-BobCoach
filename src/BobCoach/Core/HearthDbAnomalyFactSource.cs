using System;
using System.Linq;

namespace BobCoach.Engine
{
    internal interface IAnomalyCardLookup
    {
        bool TryGetByCardId(string cardId, out HearthDb.Card card);
        bool TryGetByDbfId(int dbfId, out HearthDb.Card card);
        bool TryGetNormalCardId(string goldenCardId, out string normalCardId);
        int GetTag(HearthDb.Card card, HearthDb.Enums.GameTag tag);
        string GetLocText(HearthDb.Card card, HearthDb.Enums.Locale locale);
    }

    internal sealed class HearthDbAnomalyCardLookup : IAnomalyCardLookup
    {
        public bool TryGetByCardId(string cardId, out HearthDb.Card card)
        {
            return HearthDb.Cards.All.TryGetValue(cardId, out card);
        }

        public bool TryGetByDbfId(int dbfId, out HearthDb.Card card)
        {
            return HearthDb.Cards.AllByDbfId.TryGetValue(dbfId, out card);
        }

        public bool TryGetNormalCardId(string goldenCardId, out string normalCardId)
        {
            return HearthDb.Cards.TripleToNormalCardIds.TryGetValue(
                goldenCardId, out normalCardId);
        }

        public int GetTag(HearthDb.Card card, HearthDb.Enums.GameTag tag)
        {
            return card.Entity.GetTag(tag);
        }

        public string GetLocText(HearthDb.Card card, HearthDb.Enums.Locale locale)
        {
            return card.GetLocText(locale);
        }
    }

    internal sealed class HearthDbAnomalyFactSource : IAnomalyFactSource
    {
        private readonly IAnomalyCardLookup _cards;

        public HearthDbAnomalyFactSource()
            : this(new HearthDbAnomalyCardLookup())
        {
        }

        internal HearthDbAnomalyFactSource(IAnomalyCardLookup cards)
        {
            _cards = cards ?? throw new ArgumentNullException("cards");
        }

        public bool TryGet(string cardId, out AnomalyFact fact)
        {
            fact = null;
            if (string.IsNullOrEmpty(cardId)) return false;

            try
            {
                HearthDb.Card anomaly;
                if (!_cards.TryGetByCardId(cardId, out anomaly)
                    || !IsExact(anomaly, cardId)
                    || anomaly.Type != HearthDb.Enums.CardType.BATTLEGROUND_ANOMALY)
                    return false;

                string textZhCn = _cards.GetLocText(anomaly, HearthDb.Enums.Locale.zhCN) ?? "";
                string textEnUs = _cards.GetLocText(anomaly, HearthDb.Enums.Locale.enUS) ?? "";
                if (string.IsNullOrWhiteSpace(textZhCn)
                    && string.IsNullOrWhiteSpace(textEnUs))
                    return false;

                var scriptData = new[]
                {
                    GetTag(anomaly, HearthDb.Enums.GameTag.TAG_SCRIPT_DATA_NUM_1),
                    GetTag(anomaly, HearthDb.Enums.GameTag.TAG_SCRIPT_DATA_NUM_2),
                    GetTag(anomaly, HearthDb.Enums.GameTag.TAG_SCRIPT_DATA_NUM_3),
                    GetTag(anomaly, HearthDb.Enums.GameTag.TAG_SCRIPT_DATA_NUM_4),
                    GetTag(anomaly, HearthDb.Enums.GameTag.TAG_SCRIPT_DATA_NUM_5),
                    GetTag(anomaly, HearthDb.Enums.GameTag.TAG_SCRIPT_DATA_NUM_6),
                };
                if (scriptData.Any(value => value < -100000 || value > 100000)) return false;

                string evolutionCardId = "";
                string evolutionCardType = "";
                bool evolutionIsGolden = false;
                int evolutionDbfId = GetTag(
                    anomaly, HearthDb.Enums.GameTag.BACON_EVOLUTION_CARD_ID);
                if (evolutionDbfId < 0) return false;
                if (evolutionDbfId > 0)
                {
                    HearthDb.Card evolution;
                    if (!_cards.TryGetByDbfId(evolutionDbfId, out evolution)
                        || !IsExactDbf(evolution, evolutionDbfId)
                        || !TryMapCardType(evolution.Type, out evolutionCardType))
                        return false;

                    evolutionCardId = evolution.Id;
                    string normalCardId;
                    if (_cards.TryGetNormalCardId(evolutionCardId, out normalCardId))
                    {
                        HearthDb.Card normal;
                        string normalType;
                        if (string.IsNullOrEmpty(normalCardId)
                            || !_cards.TryGetByCardId(normalCardId, out normal)
                            || !IsExact(normal, normalCardId)
                            || !TryMapCardType(normal.Type, out normalType)
                            || !string.Equals(normalType, evolutionCardType, StringComparison.Ordinal))
                            return false;
                        evolutionCardId = normalCardId;
                        evolutionCardType = normalType;
                        evolutionIsGolden = true;
                    }
                }

                string overrideHeroCardId = "";
                int overrideHeroDbfId = GetTag(
                    anomaly,
                    HearthDb.Enums.GameTag.BACON_ANOMALY_ALL_HEROES_ARE_THIS_DBID);
                if (overrideHeroDbfId < 0) return false;
                if (overrideHeroDbfId > 0)
                {
                    HearthDb.Card hero;
                    if (!_cards.TryGetByDbfId(overrideHeroDbfId, out hero)
                        || !IsExactDbf(hero, overrideHeroDbfId)
                        || hero.Type != HearthDb.Enums.CardType.HERO)
                        return false;
                    overrideHeroCardId = hero.Id;
                }

                fact = new AnomalyFact
                {
                    RequestedCardId = anomaly.Id,
                    AnomalyCardId = anomaly.Id,
                    IsDuosExclusive = GetTag(
                        anomaly,
                        HearthDb.Enums.GameTag.IS_BACON_DUOS_EXCLUSIVE) > 0,
                    EvolutionCardId = evolutionCardId,
                    EvolutionCardType = evolutionCardType,
                    EvolutionIsGolden = evolutionIsGolden,
                    OverrideHeroCardId = overrideHeroCardId,
                    ScriptData = scriptData,
                    TextZhCn = textZhCn,
                    TextEnUs = textEnUs,
                };
                return true;
            }
            catch
            {
                fact = null;
                return false;
            }
        }

        private int GetTag(HearthDb.Card card, HearthDb.Enums.GameTag tag)
        {
            return _cards.GetTag(card, tag);
        }

        private static bool IsExact(HearthDb.Card card, string cardId)
        {
            return card != null
                && card.Entity != null
                && string.Equals(card.Id, cardId, StringComparison.Ordinal);
        }

        private static bool IsExactDbf(HearthDb.Card card, int dbfId)
        {
            return card != null
                && card.Entity != null
                && !string.IsNullOrEmpty(card.Id)
                && card.DbfId == dbfId;
        }

        private static bool TryMapCardType(
            HearthDb.Enums.CardType type,
            out string cardType)
        {
            cardType = "";
            if (type == HearthDb.Enums.CardType.HERO_POWER)
                cardType = "hero_power";
            else if (type == HearthDb.Enums.CardType.MINION)
                cardType = "minion";
            else if (type == HearthDb.Enums.CardType.SPELL)
                cardType = "spell";
            else if (type == HearthDb.Enums.CardType.BATTLEGROUND_SPELL)
                cardType = "battleground_spell";
            else
                return false;
            return true;
        }
    }
}
