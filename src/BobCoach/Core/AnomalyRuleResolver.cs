using System.Collections.Generic;
using System.Linq;

namespace BobCoach.Engine
{
    /// <summary>把当前畸变上下文解析为消费者唯一读取的有效规则快照。</summary>
    public static class AnomalyRuleResolver
    {
        public static EffectiveGameRules Resolve(AnomalyContext context, AnomalyRegistry registry)
        {
            bool manualRefreshAllowed = true;
            int? minionPurchaseCostOverride = null;
            int? firstMinionPurchaseCost = null;
            bool refreshAfterPurchase = false;
            int maxTavernTier = 6;
            int goldenCopyRequirement = 3;
            string goldenRewardOverride = "standard_discover";
            int startArmorDelta = 0;
            FirstPurchaseExtraCopyRule firstPurchaseExtraCopy = null;
            UpgradePrizeRule upgradePrize = null;
            PortalInBottleRule portalInBottleAtTurnStart = null;
            SharedYoggWheelRule sharedYoggWheel = null;
            SharedCardVoteRule sharedCardVote = null;
            BuddyPoolRule buddyPool = null;
            AllHeroesOverrideRule allHeroesOverride = null;
            SecondHeroPowerDiscoverRule secondHeroPowerDiscover = null;
            TeammateGoldTransferRule teammateGoldTransfer = null;
            var startResourceExpectations = new List<StartResourceExpectation>();
            var scheduledGrants = new List<ScheduledGrant>();
            var secondaryHeroPowers = new List<SecondaryHeroPowerRule>();
            var timewarpVisits = new List<TimewarpVisit>();
            var timewarpOfferRules = new List<TimewarpOfferRule>();
            var timewarpPoolMergeRules = new List<TimewarpPoolMergeRule>();
            int unscheduledRandomTimewarpVisitCount = 0;
            bool lesserTimewarpEnabled = true;
            int timewarpMarkDelta = 0;
            int? sharedTimewarpMarkBudget = null;
            bool carryTimewarpMarksToGreater = false;
            var sourceIds = new List<string>();
            var conflicts = new List<string>();
            if (context != null && registry != null)
            {
                var ids = new List<string>();
                if (!string.IsNullOrEmpty(context.PrimaryAnomalyId)) ids.Add(context.PrimaryAnomalyId);
                ids.AddRange(context.ActiveGlobalEffectIds);
                foreach (var id in ids.Distinct().OrderBy(value => value))
                {
                    int ruleOrdinal = 0;
                    foreach (var rule in registry.GetTypedRules(id))
                    {
                        if (rule.Type == "manual_refresh_allowed" && rule.BoolValue.HasValue)
                        {
                            manualRefreshAllowed = rule.BoolValue.Value;
                            if (!sourceIds.Contains(id)) sourceIds.Add(id);
                        }
                        else if (rule.Type == "minion_cost_override" && rule.IntValue.HasValue)
                        {
                            minionPurchaseCostOverride = rule.IntValue.Value;
                            if (!sourceIds.Contains(id)) sourceIds.Add(id);
                        }
                        else if (rule.Type == "refresh_after_purchase" && rule.BoolValue.HasValue)
                        {
                            refreshAfterPurchase = rule.BoolValue.Value;
                            if (!sourceIds.Contains(id)) sourceIds.Add(id);
                        }
                        else if (rule.Type == "first_minion_purchase_cost" && rule.IntValue.HasValue)
                        {
                            firstMinionPurchaseCost = rule.IntValue.Value;
                            if (!sourceIds.Contains(id)) sourceIds.Add(id);
                        }
                        else if (rule.Type == "max_tavern_tier" && rule.IntValue.HasValue)
                        {
                            maxTavernTier = rule.IntValue.Value;
                            if (!sourceIds.Contains(id)) sourceIds.Add(id);
                        }
                        else if (rule.Type == "golden_copy_requirement" && rule.IntValue.HasValue)
                        {
                            if (rule.IntValue.Value >= 2)
                            {
                                goldenCopyRequirement = rule.IntValue.Value;
                                if (!sourceIds.Contains(id)) sourceIds.Add(id);
                            }
                            else conflicts.Add(id + ":golden_copy_requirement");
                        }
                        else if (rule.Type == "golden_reward_override"
                            && !string.IsNullOrEmpty(rule.StringValue))
                        {
                            if (rule.StringValue == "standard_discover"
                                || rule.StringValue == "tavern_coin")
                            {
                                goldenRewardOverride = rule.StringValue;
                                if (!sourceIds.Contains(id)) sourceIds.Add(id);
                            }
                            else conflicts.Add(id + ":golden_reward_override");
                        }
                        else if (rule.Type == "start_armor_delta")
                        {
                            if (rule.IntValue.HasValue && rule.IntValue.Value > 0)
                            {
                                startArmorDelta = rule.IntValue.Value;
                                if (!sourceIds.Contains(id)) sourceIds.Add(id);
                            }
                            else conflicts.Add(id + ":start_armor_delta");
                        }
                        else if (rule.Type == "start_with_card")
                        {
                            int count = rule.ExplicitCount.HasValue
                                ? rule.ExplicitCount.Value : 1;
                            if (!string.IsNullOrEmpty(rule.CardId) && count > 0
                                && !rule.GoldenInvalid)
                            {
                                startResourceExpectations.Add(new StartResourceExpectation(
                                    id + ":" + rule.Type + ":" + ruleOrdinal,
                                    "hand_card", rule.CardId, count,
                                    rule.Golden.HasValue && rule.Golden.Value, id));
                                if (!sourceIds.Contains(id)) sourceIds.Add(id);
                            }
                            else conflicts.Add(id + ":start_with_card");
                        }
                        else if (rule.Type == "start_with_board_minion")
                        {
                            int count = rule.ExplicitCount.HasValue
                                ? rule.ExplicitCount.Value : 1;
                            if (!string.IsNullOrEmpty(rule.CardId) && count > 0
                                && !rule.GoldenInvalid)
                            {
                                startResourceExpectations.Add(new StartResourceExpectation(
                                    id + ":" + rule.Type + ":" + ruleOrdinal,
                                    "board_minion", rule.CardId, count,
                                    rule.Golden.HasValue && rule.Golden.Value, id));
                                if (!sourceIds.Contains(id)) sourceIds.Add(id);
                            }
                            else conflicts.Add(id + ":start_with_board_minion");
                        }
                        else if (rule.Type == "first_purchase_extra_copy")
                        {
                            if (rule.IntValue.HasValue && rule.IntValue.Value > 0
                                && rule.CardType == "card")
                            {
                                firstPurchaseExtraCopy = new FirstPurchaseExtraCopyRule(
                                    rule.IntValue.Value, rule.CardType, id);
                                if (!sourceIds.Contains(id)) sourceIds.Add(id);
                            }
                            else conflicts.Add(id + ":first_purchase_extra_copy");
                        }
                        else if (rule.Type == "discover_prize_after_upgrade")
                        {
                            if (rule.InitialTier >= 1 && rule.InitialTier <= 4
                                && rule.ImprovesEveryTurns > 0)
                            {
                                upgradePrize = new UpgradePrizeRule(
                                    rule.InitialTier, rule.ImprovesEveryTurns, 4, id);
                                if (!sourceIds.Contains(id)) sourceIds.Add(id);
                            }
                            else conflicts.Add(id + ":discover_prize_after_upgrade");
                        }
                        else if (rule.Type == "portal_in_bottle_at_turn_start")
                        {
                            if (rule.CardId == "BGDUO_113" && rule.Count > 0)
                            {
                                portalInBottleAtTurnStart = new PortalInBottleRule(
                                    rule.CardId, rule.Count, id);
                                if (!sourceIds.Contains(id)) sourceIds.Add(id);
                            }
                            else conflicts.Add(id + ":portal_in_bottle_at_turn_start");
                        }
                        else if (rule.Type == "shared_yogg_wheel_at_turn_start")
                        {
                            if (rule.BoolValue.HasValue && rule.BoolValue.Value)
                            {
                                sharedYoggWheel = new SharedYoggWheelRule(id);
                                if (!sourceIds.Contains(id)) sourceIds.Add(id);
                            }
                            else conflicts.Add(id + ":shared_yogg_wheel_at_turn_start");
                        }
                        else if (rule.Type == "shared_card_vote_each_turn")
                        {
                            if (rule.GrantAt == "turn_end")
                            {
                                sharedCardVote = new SharedCardVoteRule(id);
                                if (!sourceIds.Contains(id)) sourceIds.Add(id);
                            }
                            else conflicts.Add(id + ":shared_card_vote_each_turn");
                        }
                        else if (rule.Type == "include_buddies_in_tavern")
                        {
                            if (rule.BoolValue.HasValue && rule.BoolValue.Value)
                            {
                                buddyPool = new BuddyPoolRule(id);
                                if (!sourceIds.Contains(id)) sourceIds.Add(id);
                            }
                            else conflicts.Add(id + ":include_buddies_in_tavern");
                        }
                        else if (rule.Type == "all_heroes_override")
                        {
                            if (rule.StringValue == "manager_marin"
                                && rule.HeroCardId == "BG30_HERO_304")
                            {
                                allHeroesOverride = new AllHeroesOverrideRule(
                                    rule.HeroCardId, id);
                                if (!sourceIds.Contains(id)) sourceIds.Add(id);
                            }
                            else conflicts.Add(id + ":all_heroes_override");
                        }
                        else if (rule.Type == "discover_second_hero_power_at_game_start")
                        {
                            if (rule.Count == 1)
                            {
                                secondHeroPowerDiscover = new SecondHeroPowerDiscoverRule(
                                    rule.Count, id);
                                if (!sourceIds.Contains(id)) sourceIds.Add(id);
                            }
                            else conflicts.Add(
                                id + ":discover_second_hero_power_at_game_start");
                        }
                        else if (rule.Type == "send_gold_to_teammate")
                        {
                            if (rule.CardId == "BGDUO_Anomaly_007t"
                                && rule.GoldPerUse == 1 && rule.MaxPerTurn == 2)
                            {
                                teammateGoldTransfer = new TeammateGoldTransferRule(
                                    rule.CardId, rule.GoldPerUse, rule.MaxPerTurn, id);
                                if (!sourceIds.Contains(id)) sourceIds.Add(id);
                            }
                            else conflicts.Add(id + ":send_gold_to_teammate");
                        }
                        else if (rule.Type == "scheduled_golden_minion_discover")
                        {
                            if (rule.Turn > 0 && rule.Tier > 0 && rule.Tier <= 7
                                && rule.Count > 0)
                            {
                                scheduledGrants.Add(new ScheduledGrant(
                                    id + ":" + rule.Type + ":" + ruleOrdinal,
                                    "golden_minion_discover", rule.Turn, 0,
                                    rule.Tier, "", rule.Count, true, new int[0]));
                                if (!sourceIds.Contains(id)) sourceIds.Add(id);
                            }
                            else conflicts.Add(id + ":scheduled_golden_minion_discover");
                        }
                        else if (rule.Type == "scheduled_card_grant")
                        {
                            if (rule.EveryTurns > 0 && !string.IsNullOrEmpty(rule.CardId)
                                && rule.Count > 0)
                            {
                                scheduledGrants.Add(new ScheduledGrant(
                                    id + ":" + rule.Type + ":" + ruleOrdinal,
                                    "card_grant", 0, rule.EveryTurns, 0,
                                    rule.CardId, rule.Count, false, new int[0]));
                                if (!sourceIds.Contains(id)) sourceIds.Add(id);
                            }
                            else conflicts.Add(id + ":scheduled_card_grant");
                        }
                        else if (rule.Type == "scheduled_prize_discover")
                        {
                            if (rule.EveryTurns > 0 && rule.Count > 0)
                            {
                                scheduledGrants.Add(new ScheduledGrant(
                                    id + ":" + rule.Type + ":" + ruleOrdinal,
                                    "prize_discover", 0, rule.EveryTurns, 0,
                                    "", rule.Count, false, new int[0]));
                                if (!sourceIds.Contains(id)) sourceIds.Add(id);
                            }
                            else conflicts.Add(id + ":scheduled_prize_discover");
                        }
                        else if (rule.Type == "tier_locked_minion_discovers")
                        {
                            bool validTiers = rule.Tiers != null && rule.Tiers.Count > 0
                                && rule.Tiers.All(tier => tier > 0 && tier <= 7)
                                && rule.Tiers.Distinct().Count() == rule.Tiers.Count;
                            if (validTiers && rule.CountEach > 0)
                            {
                                scheduledGrants.Add(new ScheduledGrant(
                                    id + ":" + rule.Type + ":" + ruleOrdinal,
                                    "tier_locked_minion_discover", 1, 0, 0,
                                    "", rule.CountEach, false, rule.Tiers));
                                if (!sourceIds.Contains(id)) sourceIds.Add(id);
                            }
                            else conflicts.Add(id + ":tier_locked_minion_discovers");
                        }
                        else if (rule.Type == "second_hero_power")
                        {
                            int unlockTurn = rule.UnlockTurn.HasValue ? rule.UnlockTurn.Value : 1;
                            bool validTrinketCopy = string.IsNullOrEmpty(rule.CopiesPurchasedTrinket)
                                || rule.CopiesPurchasedTrinket == "lesser"
                                || rule.CopiesPurchasedTrinket == "greater";
                            if (!string.IsNullOrEmpty(rule.CardId)
                                && unlockTurn > 0 && validTrinketCopy)
                            {
                                secondaryHeroPowers.Add(new SecondaryHeroPowerRule(
                                    rule.CardId, unlockTurn, rule.CopiesPurchasedTrinket, id));
                                if (!sourceIds.Contains(id)) sourceIds.Add(id);
                            }
                            else conflicts.Add(id + ":second_hero_power");
                        }
                        else if (rule.Type == "scheduled_lesser_timewarp")
                        {
                            if (rule.Turn > 0)
                            {
                                timewarpVisits.Add(new TimewarpVisit(
                                    id + ":" + rule.Type + ":" + ruleOrdinal + "@" + rule.Turn,
                                    "lesser", rule.Turn, id));
                                if (!sourceIds.Contains(id)) sourceIds.Add(id);
                            }
                            else conflicts.Add(id + ":scheduled_lesser_timewarp");
                        }
                        else if (rule.Type == "scheduled_greater_timewarp")
                        {
                            var turns = rule.Turn > 0
                                ? new List<int> { rule.Turn }
                                : (rule.Turns ?? new List<int>());
                            bool validTurns = turns.Count > 0
                                && turns.All(turn => turn > 0)
                                && turns.Distinct().Count() == turns.Count;
                            if (validTurns)
                            {
                                foreach (var visitTurn in turns.OrderBy(turn => turn))
                                {
                                    timewarpVisits.Add(new TimewarpVisit(
                                        id + ":" + rule.Type + ":" + ruleOrdinal + "@" + visitTurn,
                                        "greater", visitTurn, id));
                                }
                                if (!sourceIds.Contains(id)) sourceIds.Add(id);
                            }
                            else conflicts.Add(id + ":scheduled_greater_timewarp");
                        }
                        else if (rule.Type == "random_extra_timewarp")
                        {
                            if (rule.Count > 0)
                            {
                                unscheduledRandomTimewarpVisitCount += rule.Count;
                                if (!sourceIds.Contains(id)) sourceIds.Add(id);
                            }
                            else conflicts.Add(id + ":random_extra_timewarp");
                        }
                        else if (rule.Type == "lesser_timewarp_enabled" && rule.BoolValue.HasValue)
                        {
                            lesserTimewarpEnabled = rule.BoolValue.Value;
                            if (!sourceIds.Contains(id)) sourceIds.Add(id);
                        }
                        else if (rule.Type == "greater_timewarp_golden_offers")
                        {
                            if (rule.Count > 0 && rule.GrantsTripleReward.HasValue)
                            {
                                timewarpOfferRules.Add(new TimewarpOfferRule(
                                    "greater", rule.Count, true,
                                    rule.GrantsTripleReward.Value, id));
                                if (!sourceIds.Contains(id)) sourceIds.Add(id);
                            }
                            else conflicts.Add(id + ":greater_timewarp_golden_offers");
                        }
                        else if (rule.Type == "lesser_timewarp_golden_offers")
                        {
                            if (rule.Count > 0 && rule.GrantsTripleReward.HasValue)
                            {
                                timewarpOfferRules.Add(new TimewarpOfferRule(
                                    "lesser", rule.Count, true,
                                    rule.GrantsTripleReward.Value, id));
                                if (!sourceIds.Contains(id)) sourceIds.Add(id);
                            }
                            else conflicts.Add(id + ":lesser_timewarp_golden_offers");
                        }
                        else if (rule.Type == "timewarp_mark_delta")
                        {
                            if (rule.IntValue.HasValue && rule.IntValue.Value != 0
                                && rule.Period == "timewarp")
                            {
                                timewarpMarkDelta += rule.IntValue.Value;
                                if (!sourceIds.Contains(id)) sourceIds.Add(id);
                            }
                            else conflicts.Add(id + ":timewarp_mark_delta");
                        }
                        else if (rule.Type == "shared_timewarp_mark_budget")
                        {
                            if (rule.IntValue.HasValue && rule.IntValue.Value > 0
                                && rule.CarryToGreater.HasValue)
                            {
                                sharedTimewarpMarkBudget = rule.IntValue.Value;
                                carryTimewarpMarksToGreater = rule.CarryToGreater.Value;
                                if (!sourceIds.Contains(id)) sourceIds.Add(id);
                            }
                            else conflicts.Add(id + ":shared_timewarp_mark_budget");
                        }
                        else if (rule.Type == "merge_lesser_timewarp_pool")
                        {
                            if (rule.Turn > 0)
                            {
                                timewarpPoolMergeRules.Add(new TimewarpPoolMergeRule(
                                    "lesser", rule.Turn, id));
                                if (!sourceIds.Contains(id)) sourceIds.Add(id);
                            }
                            else conflicts.Add(id + ":merge_lesser_timewarp_pool");
                        }
                        else if (rule.Type == "merge_greater_timewarp_pool")
                        {
                            if (rule.Turn > 0)
                            {
                                timewarpPoolMergeRules.Add(new TimewarpPoolMergeRule(
                                    "greater", rule.Turn, id));
                                if (!sourceIds.Contains(id)) sourceIds.Add(id);
                            }
                            else conflicts.Add(id + ":merge_greater_timewarp_pool");
                        }
                        ruleOrdinal++;
                    }
                }
            }
            return new EffectiveGameRules(
                minionPurchaseCostOverride, firstMinionPurchaseCost,
                manualRefreshAllowed, null, refreshAfterPurchase, maxTavernTier,
                goldenCopyRequirement, goldenRewardOverride, startArmorDelta,
                firstPurchaseExtraCopy, upgradePrize, portalInBottleAtTurnStart,
                sharedYoggWheel, sharedCardVote, buddyPool, allHeroesOverride,
                secondHeroPowerDiscover, teammateGoldTransfer,
                startResourceExpectations,
                scheduledGrants, secondaryHeroPowers, timewarpVisits, timewarpOfferRules,
                timewarpPoolMergeRules,
                unscheduledRandomTimewarpVisitCount, lesserTimewarpEnabled, timewarpMarkDelta,
                sharedTimewarpMarkBudget, carryTimewarpMarksToGreater,
                sourceIds, conflicts);
        }
    }
}
