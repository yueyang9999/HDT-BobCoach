using System;
using System.Collections.Generic;

namespace BobCoach.Engine
{
    internal sealed class HearthDbCardPoolFactSource : ICardPoolFactSource
    {
        private static readonly Dictionary<HearthDb.Enums.Race, string> RaceNames
            = new Dictionary<HearthDb.Enums.Race, string>
            {
                { HearthDb.Enums.Race.DEMON, "恶魔" },
                { HearthDb.Enums.Race.MURLOC, "鱼人" },
                { HearthDb.Enums.Race.BEAST, "野兽" },
                { HearthDb.Enums.Race.MECHANICAL, "机械" },
                { HearthDb.Enums.Race.ELEMENTAL, "元素" },
                { HearthDb.Enums.Race.PIRATE, "海盗" },
                { HearthDb.Enums.Race.DRAGON, "龙" },
                { HearthDb.Enums.Race.QUILBOAR, "野猪人" },
                { HearthDb.Enums.Race.NAGA, "纳迦" },
                { HearthDb.Enums.Race.UNDEAD, "亡灵" },
            };

        public bool TryGetCard(string cardId, out CardPoolFact fact)
        {
            fact = new CardPoolFact();
            if (string.IsNullOrEmpty(cardId)) return false;

            try
            {
                HearthDb.Card card;
                if (!HearthDb.Cards.All.TryGetValue(cardId, out card) || card == null)
                    return false;
                if (!string.Equals(card.Id, cardId, StringComparison.Ordinal)) return false;
                bool isMinion = card.Type == HearthDb.Enums.CardType.MINION;
                bool isSpell = card.Type == HearthDb.Enums.CardType.SPELL
                    || card.Type == HearthDb.Enums.CardType.BATTLEGROUND_SPELL;
                if (!isMinion && !isSpell) return false;
                if (isSpell && card.Cost < 0) return false;
                if (card.TechLevel < 1 || card.TechLevel > 7) return false;

                var tribes = new List<string>();
                if (isMinion)
                {
                    AddRace(card.Race, tribes);
                    AddRace(card.SecondaryRace, tribes);
                    if (tribes.Count == 0) tribes.Add("中立");
                }

                fact = new CardPoolFact
                {
                    CardId = cardId,
                    Tier = card.TechLevel,
                    TribeCn = string.Join(",", tribes),
                    IsSpell = isSpell,
                    Cost = isSpell ? card.Cost : -1,
                    Attack = isMinion ? card.Attack : 0,
                    Health = isMinion ? card.Health : 0,
                };
                return true;
            }
            catch
            {
                fact = new CardPoolFact();
                return false;
            }
        }

        private static void AddRace(HearthDb.Enums.Race race, List<string> tribes)
        {
            if (race == HearthDb.Enums.Race.ALL)
            {
                foreach (string value in RaceNames.Values)
                    if (!tribes.Contains(value)) tribes.Add(value);
                return;
            }
            string name;
            if (RaceNames.TryGetValue(race, out name) && !tribes.Contains(name))
                tribes.Add(name);
        }
    }
}
