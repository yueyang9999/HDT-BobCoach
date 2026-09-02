using System;
using System.Collections.Generic;
using System.Linq;

namespace BobCoach.Engine
{
    /// <summary>
    /// Gates normal tavern recruitment during hero powers that skip early turns.
    /// The gate persists a confirmed AFK signal across frames because HDT can
    /// replace the visible hero power entity with utility entities during the
    /// same turn (e.g. TB_BaconShop_HP_044 -> TB_BaconShop_UpdateDmgCap).
    /// </summary>
    public sealed class EarlyTurnRecruitGate
    {
        public const string AfkPowerCardId = "TB_BaconShop_HP_044";
        public const string AfkHeroCardId = "TB_BaconShop_HERO_16";
        public const string AfkLegacyHeroCardId = "TB_BaconShop_HERO_KelThuzad";

        private bool _afkConfirmed;
        private string _afkConfirmedHeroCardId = "";

        public bool AfkConfirmed
        {
            get { return _afkConfirmed; }
        }

        public string AfkConfirmedHeroCardId
        {
            get { return _afkConfirmedHeroCardId; }
        }

        public void Reset()
        {
            _afkConfirmed = false;
            _afkConfirmedHeroCardId = "";
        }

        public void Observe(string heroCardId, IEnumerable<HeroPowerState> heroPowers)
        {
            if (string.IsNullOrEmpty(heroCardId) || heroPowers == null) return;
            if (!HasAfkSignal(heroPowers)) return;
            _afkConfirmed = true;
            _afkConfirmedHeroCardId = heroCardId;
        }

        public bool BlocksEarlyRecruit(
            int turn,
            string heroCardId,
            string heroPowerCardId,
            IEnumerable<HeroPowerState> heroPowers)
        {
            if (turn >= 3 || string.IsNullOrEmpty(heroCardId)) return false;
            if (_afkConfirmed
                && string.Equals(_afkConfirmedHeroCardId, heroCardId, StringComparison.Ordinal))
                return true;
            return HasAfkSignal(heroPowers)
                || string.Equals(heroPowerCardId, AfkPowerCardId, StringComparison.Ordinal)
                || IsKnownAfkHeroCardId(heroCardId);
        }

        public static bool IsKnownAfkHeroCardId(string heroCardId)
        {
            return string.Equals(heroCardId, AfkHeroCardId, StringComparison.Ordinal)
                || string.Equals(heroCardId, AfkLegacyHeroCardId, StringComparison.Ordinal);
        }

        private static bool HasAfkSignal(IEnumerable<HeroPowerState> heroPowers)
        {
            if (heroPowers == null) return false;
            return heroPowers.Any(power =>
                power != null
                && (string.Equals(power.SpecialRule, "AFK", StringComparison.Ordinal)
                    || string.Equals(power.CardId, AfkPowerCardId, StringComparison.Ordinal)));
        }
    }
}
