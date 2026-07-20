using System;

namespace BobCoach.Engine
{
    internal interface IHeroPowerCardLookup
    {
        bool TryGetByCardId(string cardId, out HearthDb.Card card);
        bool TryGetByDbfId(int dbfId, out HearthDb.Card card);
    }

    internal sealed class HearthDbHeroPowerCardLookup : IHeroPowerCardLookup
    {
        public bool TryGetByCardId(string cardId, out HearthDb.Card card)
        {
            return HearthDb.Cards.All.TryGetValue(cardId, out card);
        }

        public bool TryGetByDbfId(int dbfId, out HearthDb.Card card)
        {
            return HearthDb.Cards.AllByDbfId.TryGetValue(dbfId, out card);
        }
    }

    internal sealed class HearthDbHeroPowerFactSource : IHeroPowerFactSource
    {
        private readonly IHeroPowerCardLookup _cards;

        public HearthDbHeroPowerFactSource()
            : this(new HearthDbHeroPowerCardLookup())
        {
        }

        internal HearthDbHeroPowerFactSource(IHeroPowerCardLookup cards)
        {
            _cards = cards ?? throw new ArgumentNullException("cards");
        }

        public bool TryGet(string cardId, out HeroPowerFact fact)
        {
            fact = null;
            if (string.IsNullOrEmpty(cardId)) return false;

            try
            {
                HearthDb.Card requested;
                if (!_cards.TryGetByCardId(cardId, out requested)
                    || !IsExact(requested, cardId))
                    return false;

                HearthDb.Card hero;
                HearthDb.Card power;
                if (requested.Type == HearthDb.Enums.CardType.HERO)
                {
                    if (!TryResolveHero(requested, out hero)) return false;
                    if (!TryResolvePower(hero, out power)) return false;
                }
                else if (requested.Type == HearthDb.Enums.CardType.HERO_POWER)
                {
                    power = requested;
                    if (!TryResolveBaseHero(power, out hero)) return false;
                }
                else
                {
                    return false;
                }

                if (hero.Type != HearthDb.Enums.CardType.HERO
                    || power.Type != HearthDb.Enums.CardType.HERO_POWER
                    || hero.Entity == null
                    || power.Entity == null)
                    return false;

                int armor = hero.Entity.GetTag(HearthDb.Enums.GameTag.ARMOR);
                int cost = power.Cost;
                string text = power.GetLocText(HearthDb.Enums.Locale.zhCN) ?? "";
                if (armor < 0 || armor > 100 || cost < 0 || cost > 20
                    || string.IsNullOrWhiteSpace(text))
                    return false;

                fact = new HeroPowerFact
                {
                    RequestedCardId = requested.Id,
                    HeroCardId = hero.Id,
                    PowerCardId = power.Id,
                    HeroArmor = armor,
                    PowerCost = cost,
                    HideCost = power.Entity.GetTag(HearthDb.Enums.GameTag.HIDE_COST) != 0,
                    BaconHeroPowerActivated = power.Entity.GetTag(
                        HearthDb.Enums.GameTag.BACON_HERO_POWER_ACTIVATED) != 0,
                    TextZhCn = text,
                    ScriptData = new[]
                    {
                        power.Entity.GetTag(HearthDb.Enums.GameTag.TAG_SCRIPT_DATA_NUM_1),
                        power.Entity.GetTag(HearthDb.Enums.GameTag.TAG_SCRIPT_DATA_NUM_2),
                        power.Entity.GetTag(HearthDb.Enums.GameTag.TAG_SCRIPT_DATA_NUM_3),
                        power.Entity.GetTag(HearthDb.Enums.GameTag.TAG_SCRIPT_DATA_NUM_4),
                        power.Entity.GetTag(HearthDb.Enums.GameTag.TAG_SCRIPT_DATA_NUM_5),
                        power.Entity.GetTag(HearthDb.Enums.GameTag.TAG_SCRIPT_DATA_NUM_6),
                    },
                };
                return true;
            }
            catch
            {
                fact = null;
                return false;
            }
        }

        private bool TryResolveHero(HearthDb.Card requested, out HearthDb.Card hero)
        {
            hero = requested;
            int parentDbfId = requested.Entity.GetTag(
                HearthDb.Enums.GameTag.BACON_SKIN_PARENT_ID);
            if (parentDbfId <= 0) return true;
            if (!_cards.TryGetByDbfId(parentDbfId, out hero)
                || hero == null
                || hero.DbfId != parentDbfId
                || hero.Type != HearthDb.Enums.CardType.HERO)
                return false;
            return true;
        }

        private bool TryResolvePower(HearthDb.Card hero, out HearthDb.Card power)
        {
            power = null;
            int powerDbfId = hero.Entity.GetTag(HearthDb.Enums.GameTag.SHOWN_HERO_POWER);
            return powerDbfId > 0
                && _cards.TryGetByDbfId(powerDbfId, out power)
                && power != null
                && power.DbfId == powerDbfId
                && power.Type == HearthDb.Enums.CardType.HERO_POWER;
        }

        private bool TryResolveBaseHero(HearthDb.Card power, out HearthDb.Card hero)
        {
            hero = null;
            int heroDbfId = power.Entity.GetTag(
                HearthDb.Enums.GameTag.BACON_HEROPOWER_BASE_HERO_ID);
            return heroDbfId > 0
                && _cards.TryGetByDbfId(heroDbfId, out hero)
                && hero != null
                && hero.DbfId == heroDbfId
                && hero.Type == HearthDb.Enums.CardType.HERO;
        }

        private static bool IsExact(HearthDb.Card card, string cardId)
        {
            return card != null
                && card.Entity != null
                && string.Equals(card.Id, cardId, StringComparison.Ordinal);
        }
    }
}
