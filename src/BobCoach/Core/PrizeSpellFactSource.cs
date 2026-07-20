using System.Collections.Generic;

namespace BobCoach.Engine
{
    internal enum PrizeSpellCardType
    {
        Unknown = 0,
        Spell = 1,
        Minion = 2,
    }

    internal sealed class PrizeSpellFact
    {
        public string CardId { get; set; }
        public PrizeSpellCardType CardType { get; set; }
        public int PrizeTier { get; set; }
        public int TechLevel { get; set; }
        public string TextZhCn { get; set; }
        public IReadOnlyList<int> ScriptData { get; set; }
    }

    internal interface IPrizeSpellFactSource
    {
        bool TryGet(string cardId, out PrizeSpellFact fact);
    }

    internal enum PrizeSpellRole
    {
        Unknown = 0,
        Economy = 1,
        Scaling = 2,
        Tempo = 3,
        Discover = 4,
        Utility = 5,
        Minion = 6,
    }

    internal sealed class PrizeSpellPolicy
    {
        public string CardId { get; private set; }
        public int PrizeTier { get; private set; }
        public PrizeSpellRole Role { get; private set; }

        public PrizeSpellPolicy(string cardId, int prizeTier, PrizeSpellRole role)
        {
            CardId = cardId ?? "";
            PrizeTier = prizeTier;
            Role = role;
        }
    }
}
