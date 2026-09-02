using System;

namespace BobCoach.Engine
{
    /// <summary>
    /// Cross-cutting hero power facts used by extraction, action enumeration,
    /// and UI so all layers consume the same rules.
    /// </summary>
    public static class HeroPowerRuleCatalog
    {
        public static bool IsEudoraHero(string heroCardId)
        {
            return !string.IsNullOrEmpty(heroCardId)
                && heroCardId.IndexOf("TB_BaconShop_HERO_64", StringComparison.Ordinal) >= 0;
        }

        public static bool IsEudoraPower(string powerCardId)
        {
            return string.Equals(powerCardId, "TB_BaconShop_HP_074", StringComparison.Ordinal);
        }

        public static bool IsChromieHero(string heroCardId)
        {
            return !string.IsNullOrEmpty(heroCardId)
                && heroCardId.IndexOf("BG34_HERO_001", StringComparison.Ordinal) >= 0;
        }

        public static bool IsChromiePower(string powerCardId)
        {
            return string.Equals(powerCardId, "BG34_HERO_001p", StringComparison.Ordinal);
        }

        public static bool IsFinleySeedPower(string powerCardId)
        {
            return string.Equals(powerCardId, "TB_BaconShop_HP_057", StringComparison.Ordinal);
        }

        public static bool IsLuckyRollPower(string powerCardId)
        {
            return string.Equals(powerCardId, "BG28_HERO_400p", StringComparison.Ordinal);
        }

        public static int GetPrimaryHeroPowerPreference(
            string heroCardId,
            string powerCardId,
            bool hasFinleyReplacement)
        {
            if (!hasFinleyReplacement) return 0;
            if (IsFinleySeedPower(powerCardId)) return -1000;
            if (!string.IsNullOrEmpty(powerCardId)
                && powerCardId.EndsWith("p", StringComparison.Ordinal))
                return 2000;
            return 0;
        }

        public static string GetHeroCardIdForPower(
            string heroCardId, string powerCardId)
        {
            if (IsChromiePower(powerCardId)) return "BG34_HERO_001";
            if (IsEudoraPower(powerCardId)) return "TB_BaconShop_HERO_64";
            return heroCardId;
        }

        public static string GetSpecialRuleOverride(
            string heroCardId, string powerCardId)
        {
            if (IsEudoraHero(heroCardId) && IsEudoraPower(powerCardId))
                return "DIG_GOLDEN";
            if (IsChromieHero(heroCardId) && IsChromiePower(powerCardId))
                return "CHROMIE";
            return "";
        }

        public static bool ShouldReserveGoldForEudora(
            int turn, int digsRemaining)
        {
            if (turn <= 0) return false;
            if (digsRemaining > 0) return true;
            return digsRemaining < 0 && turn <= 6;
        }

        public static bool IsUtilityPowerCardId(string cardId)
        {
            return string.Equals(cardId, "TB_BaconShop_UpdateDmgCap", StringComparison.Ordinal);
        }

        public static bool IsNoTargetSpecialRule(
            string specialRule,
            string heroCardId,
            string powerCardId)
        {
            if (string.Equals(powerCardId, "BG32_HERO_001p", StringComparison.Ordinal))
                return true;
            if (string.IsNullOrEmpty(specialRule)) return false;

            switch (specialRule)
            {
                case "secret":
                case "bet_on_winner":
                case "choose_element":
                case "dig_golden":
                case "steal_first_kill":
                case "steal_opponent_board":
                case "refresh_to_spells":
                case "blood_gem":
                case "custom_undead":
                case "spell_synergy":
                case "discover_buddy":
                case "magnetic":
                case "armor_based_budget":
                case "free_swap_per_turn":
                case "replace_higher_tier":
                case "PUTRICIDE":
                case "ALEXSTRASZA":
                case "SNAKE_EYES":
                case "FINLEY":
                case "GENN":
                case "SYLVANAS":
                case "DENATHRIUS":
                case "LIFTOFF":
                case "TOCKI":
                case "CHROMIE":
                case "ELISE":
                case "LAKANISHU":
                case "CENARIUS":
                    return true;
                default:
                    return false;
            }
        }

        public static int AdvancePermanentGoldCap(
            string powerCardId,
            int currentMaxGold)
        {
            if (!string.Equals(powerCardId, "BG32_HERO_001p", StringComparison.Ordinal))
                return currentMaxGold;
            return Math.Min(20, currentMaxGold + 1);
        }

        public static bool RequiresTarget(
            string heroCardId,
            string specialRule,
            bool hasDiscover)
        {
            if (string.IsNullOrEmpty(heroCardId)) return false;

            // 钩牙“变废为宝”必须先移除友方随从，之后才进入发现面板。
            if (heroCardId.Contains("TB_BaconShop_HERO_67"))
                return true;

            if (hasDiscover) return false;

            if (heroCardId.Contains("TB_BaconShop_HERO_28")
                || heroCardId.Contains("TB_BaconShop_HERO_92")
                || heroCardId.Contains("BG31_HERO_003"))
                return false;

            if (heroCardId.Contains("BG25_HERO_103")
                || heroCardId.Contains("TB_BaconShop_HERO_58")
                || heroCardId.Contains("TB_BaconShop_HERO_22")
                || heroCardId.Contains("TB_BaconShop_HERO_39")
                || heroCardId.Contains("BG20_HERO_201"))
                return true;

            if (specialRule == "trigger_battlecry" || specialRule == "eat_shop_minion"
                || specialRule == "triple_helper" || specialRule == "summon_highest_hp"
                || specialRule == "divine_shield" || specialRule == "one_time_golden"
                || specialRule == "alternating_buff" || specialRule == "tavern_manipulation"
                || specialRule == "damage_to_gold" || specialRule == "dormant_double_stats"
                || specialRule == "copy_highest_atk")
                return true;

            return false;
        }

        public static bool ShouldHoldNextTurnGoldBattlecry(
            int turn, string cardId, string heroPowerCardId = null)
        {
            if (turn != 1) return false;
            if (IsLuckyRollPower(heroPowerCardId)) return false;
            return string.Equals(cardId, "BG26_135", StringComparison.Ordinal)
                || string.Equals(cardId, "BG26_135_G", StringComparison.Ordinal)
                || string.Equals(cardId, "BG34_Giant_001", StringComparison.Ordinal)
                || string.Equals(cardId, "BG34_Giant_001_G", StringComparison.Ordinal);
        }
    }
}
