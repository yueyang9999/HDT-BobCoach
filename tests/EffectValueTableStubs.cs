using System;
using System.Collections.Generic;
using System.Linq;

namespace BobCoach.Engine
{
    public sealed class GameState
    {
        public int Turn { get; set; }
        public int Health { get; set; }
        public int TavernTier { get; set; }
        public List<MinionData> BoardMinions { get; set; } = new List<MinionData>();
    }

    public sealed class MinionData
    {
        public string CardId { get; set; }
        public string Tribe { get; set; }
        public int Attack { get; set; }
        public int Health { get; set; }
        public bool Golden { get; set; }
        public bool DivineShield { get; set; }
        public bool Poisonous { get; set; }
        public bool Venomous { get; set; }
        public bool Taunt { get; set; }
        public bool Reborn { get; set; }
        public bool Windfury { get; set; }
        public bool MegaWindfury { get; set; }

        public static bool TribeMatches(string tribeField, string target)
        {
            if (string.IsNullOrEmpty(tribeField) || string.IsNullOrEmpty(target)) return false;
            return tribeField.Split(',').Any(value =>
                string.Equals(value.Trim(), target, StringComparison.Ordinal));
        }
    }
}
