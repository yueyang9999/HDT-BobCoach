using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace BobCoach.Engine
{
    internal sealed class AnomalyRuleEvaluator : IAnomalyRuleEvaluator
    {
        private const RegexOptions MatchOptions = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        public bool TryEvaluate(AnomalyFact fact, out AnomalyDefinition definition)
        {
            definition = null;
            if (!IsValidFact(fact)) return false;

            try
            {
                List<AnomalyRegistry.TypedRule> zhRules;
                List<AnomalyRegistry.TypedRule> enRules;
                bool hasZh = TryDerive(Normalize(fact.TextZhCn, fact.ScriptData), fact, out zhRules);
                bool hasEn = TryDerive(Normalize(fact.TextEnUs, fact.ScriptData), fact, out enRules);
                if (!hasZh && !hasEn) return false;
                List<AnomalyRegistry.TypedRule> rules;
                if (!TryChooseRules(hasZh, zhRules, hasEn, enRules, out rules)) return false;
                string lifecycle;
                if (!TryDetermineLifecycle(rules, out lifecycle)) return false;
                definition = new AnomalyDefinition
                {
                    AnomalyCardId = fact.AnomalyCardId,
                    Lifecycle = lifecycle,
                    Scope = fact.IsDuosExclusive ? "duo" : "solo",
                    Rules = rules,
                };
                return true;
            }
            catch
            {
                definition = null;
                return false;
            }
        }

        private static bool IsValidFact(AnomalyFact fact)
        {
            if (fact == null
                || string.IsNullOrWhiteSpace(fact.RequestedCardId)
                || string.IsNullOrWhiteSpace(fact.AnomalyCardId)
                || !string.Equals(fact.RequestedCardId, fact.AnomalyCardId, StringComparison.Ordinal)
                || fact.ScriptData == null
                || fact.ScriptData.Length != 6
                || fact.ScriptData.Any(value => value < -100000 || value > 100000))
                return false;

            return !string.IsNullOrWhiteSpace(fact.TextZhCn)
                || !string.IsNullOrWhiteSpace(fact.TextEnUs);
        }

        private static string Normalize(string value, int[] scriptData)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            string result = Regex.Replace(value, "<[^>]+>", "");
            result = Regex.Replace(result, @"\[x\]", "", MatchOptions);
            for (int index = 0; index < scriptData.Length; index++)
            {
                result = result.Replace(
                    "{" + index.ToString(CultureInfo.InvariantCulture) + "}",
                    scriptData[index].ToString(CultureInfo.InvariantCulture));
            }
            if (Regex.IsMatch(result, @"\{\d+\}")) return "";
            result = Regex.Replace(
                result,
                @"[（(][^）)]*(?:还剩|remaining)[^）)]*@[^）)]*[）)]",
                "",
                MatchOptions);
            result = Regex.Replace(result, @"@\w*", "", MatchOptions);
            result = result.Replace('\u3000', ' ');
            return Regex.Replace(result, @"\s+", " ").Trim();
        }

        private static bool TryDerive(
            string text,
            AnomalyFact fact,
            out List<AnomalyRegistry.TypedRule> rules)
        {
            rules = new List<AnomalyRegistry.TypedRule>();
            if (string.IsNullOrEmpty(text)) return false;

            Match match;
            int value;

            match = Match(text,
                @"(?:对战|游戏).{0,8}开始.{0,8}(?:有|拥有|获得)\s*(\d+)\s*枚?铸币",
                @"(?:battle|game).{0,8}start.{0,12}(?:with|have)\s*(\d+)\s*(?:gold|coins?)");
            if (TryReadInt(match, 0, 20, out value))
                rules.Add(IntRule("start_gold_override", value));

            match = Match(text,
                @"随从.{0,8}(?:消耗|花费)\s*(\d+)\s*枚?铸币",
                @"minions?\s+cost\s*\(?(\d+)\)?\s*(?:gold)?");
            if (TryReadInt(match, 0, 10, out value))
                rules.Add(IntRule("minion_cost_override", value));

            if (IsMatch(text, @"(?:不能|无法)刷新酒馆", @"(?:cannot|can't)\s+refresh\s+(?:the\s+)?tavern"))
                rules.Add(BoolRule("manual_refresh_allowed", false));

            if (IsMatch(text,
                @"购买.{0,12}后.{0,16}(?:自行|自动)?刷新",
                @"(?:refreshes?.{0,12}after\s+you\s+(?:buy|purchase)|after\s+(?:a\s+)?purchase.{0,12}refresh)"))
                rules.Add(BoolRule("refresh_after_purchase", true));

            if (IsMatch(text,
                @"每回合.{0,20}(?:购买的)?第一个随从.{0,4}免费",
                @"(?:first\s+minion.{0,20}(?:each|every)\s+turn.{0,8}free|(?:each|every)\s+turn.{0,20}first\s+minion.{0,8}free)"))
            {
                var rule = IntRule("first_minion_purchase_cost", 0);
                rule.Period = "turn";
                rules.Add(rule);
            }

            if (IsMatch(text,
                @"每回合.{0,24}第一次购买.{0,20}(?:额外|另一).{0,4}复制",
                @"(?:first\s+purchase.{0,20}(?:each|every)\s+turn.{0,20}extra\s+copy|(?:each|every)\s+turn.{0,24}first\s+purchase.{0,20}extra\s+copy)"))
            {
                var rule = IntRule("first_purchase_extra_copy", 1);
                rule.CardType = "card";
                rules.Add(rule);
            }

            match = Match(text,
                @"(?:只需|仅需).{0,4}([二2]).{0,8}(?:复制|相同).{0,24}金色",
                @"(?:only\s+need|requires?)\s*(\d+)\s+cop(?:y|ies).{0,20}golden");
            if (match.Success && TryReadCount(match.Groups[1].Value, 2, 3, out value))
                rules.Add(IntRule("golden_copy_requirement", value));

            if (IsMatch(text,
                @"三连奖励.{0,20}(?:改为)?(?:获取|获得)?酒馆币",
                @"(?:no|not).{0,12}triple reward.{0,24}(?:instead.{0,8})?tavern coin"))
            {
                rules.Add(new AnomalyRegistry.TypedRule
                {
                    Type = "golden_reward_override",
                    StringValue = "tavern_coin",
                });
            }

            if (IsMatch(text,
                @"(?:开局|对战开始).{0,8}(?:拥有|获得).{0,4}一张",
                @"start.{0,12}with.{0,8}(?:a|one)\s+card"))
            {
                if (string.IsNullOrEmpty(fact.EvolutionCardId)
                    || (fact.EvolutionCardType != "spell"
                        && fact.EvolutionCardType != "battleground_spell"))
                    return false;
                rules.Add(new AnomalyRegistry.TypedRule
                {
                    Type = "start_with_card",
                    CardId = fact.EvolutionCardId,
                    CardType = fact.EvolutionCardType,
                    Count = 1,
                });
            }

            if (IsMatch(text,
                @"(?:对战|游戏).{0,8}开始.{0,12}场上.{0,12}(?:一个|1个)",
                @"start.{0,12}(?:on\s+the\s+)?(?:board|battlefield).{0,12}(?:a|one)\s+(?:golden\s+)?minion"))
            {
                bool textIsGolden = IsMatch(text, @"金色", @"golden");
                if (string.IsNullOrEmpty(fact.EvolutionCardId)
                    || fact.EvolutionCardType != "minion"
                    || textIsGolden != fact.EvolutionIsGolden)
                    return false;
                rules.Add(new AnomalyRegistry.TypedRule
                {
                    Type = "start_with_board_minion",
                    CardId = fact.EvolutionCardId,
                    CardType = "minion",
                    Count = 1,
                    Golden = fact.EvolutionIsGolden,
                });
            }

            match = Match(text,
                @"(?:(?:存在|开放).{0,4}酒馆等级|酒馆.{0,8}(?:最高|可升至|可以升级到).{0,4}(?:等级)?)\s*(\d+)",
                @"tavern\s+tier\s*(\d+).{0,8}(?:exists|available)");
            if (TryReadInt(match, 1, 7, out value))
                rules.Add(IntRule("max_tavern_tier", value));

            match = Match(text,
                @"(?:对战|游戏).{0,8}开始.{0,16}(?:额外)?(?:拥有|获得|增加)\s*(\d+)\s*点护甲",
                @"start.{0,16}with.{0,8}(\d+)\s+(?:extra\s+)?armor");
            if (TryReadInt(match, 1, 100, out value))
                rules.Add(IntRule("start_armor_delta", value));

            if (IsMatch(text, @"酒馆中.{0,8}(?:会)?出现伙伴", @"buddies.{0,12}(?:appear|available).{0,12}tavern"))
                rules.Add(BoolRule("include_buddies_in_tavern", true));

            match = Match(text,
                @"升级酒馆后.{0,16}发现.{0,8}等级\s*(\d+).{0,8}暗月奖品.{0,12}(\d+)\s*回合后提升",
                @"after.{0,8}upgrade.{0,12}discover.{0,8}(?:tier|level)\s*(\d+).{0,12}darkmoon prize.{0,20}(?:improves?|upgrades?).{0,8}(\d+)\s+turn");
            int initialTier;
            int improvesEvery;
            if (TryReadInt(match, 1, 4, 1, out initialTier)
                && TryReadInt(match, 1, 20, 2, out improvesEvery))
            {
                rules.Add(new AnomalyRegistry.TypedRule
                {
                    Type = "discover_prize_after_upgrade",
                    InitialTier = initialTier,
                    ImprovesEveryTurns = improvesEvery,
                });
            }

            match = Match(text,
                @"每\s*(\d+)\s*个?回合.{0,16}发现.{0,8}暗月奖品",
                @"every\s*(\d+)\s+turns?.{0,16}discover.{0,8}darkmoon prize");
            if (TryReadInt(match, 1, 20, out value))
            {
                rules.Add(new AnomalyRegistry.TypedRule
                {
                    Type = "scheduled_prize_discover",
                    EveryTurns = value,
                    Count = 1,
                });
            }

            if (IsMatch(text,
                @"每个回合开始时.{0,20}所有玩家.{0,12}(?:转动|旋转).{0,16}(?:尤格|命运之轮)",
                @"start\s+of\s+each\s+turn.{0,20}all\s+players.{0,16}(?:yogg|wheel)"))
                rules.Add(BoolRule("shared_yogg_wheel_at_turn_start", true));

            if (IsMatch(text,
                @"每个回合开始时.{0,24}(?:一个玩家|玩家).{0,12}选择一张牌.{0,24}回合结束时.{0,16}所有玩家.{0,12}(?:获取|获得)",
                @"start\s+of\s+each\s+turn.{0,32}(?:a\s+)?player.{0,16}chooses?\s+a\s+card.{0,32}end\s+of\s+(?:the\s+)?turn.{0,20}all\s+players.{0,12}(?:get|gain)"))
            {
                rules.Add(new AnomalyRegistry.TypedRule
                {
                    Type = "shared_card_vote_each_turn",
                    GrantAt = "turn_end",
                });
            }

            match = Match(text,
                @"第\s*(\d+)\s*回合.{0,16}发现.{0,8}等级\s*(\d+).{0,8}金色随从",
                @"turn\s*(\d+).{0,16}discover.{0,8}(?:tier|level)\s*(\d+).{0,8}golden minion");
            int turn;
            int tier;
            if (TryReadInt(match, 1, 20, 1, out turn)
                && TryReadInt(match, 1, 7, 2, out tier))
            {
                rules.Add(new AnomalyRegistry.TypedRule
                {
                    Type = "scheduled_golden_minion_discover",
                    Turn = turn,
                    Tier = tier,
                    Count = 1,
                });
            }

            match = Match(text,
                @"每\s*(\d+)\s*个?回合.{0,16}(?:获取|获得)一张",
                @"every\s*(\d+)\s+turns?.{0,16}(?:get|gain)\s+a\s+(?:spell\s+)?card");
            if (match.Success)
            {
                if (!TryReadInt(match, 1, 20, out value)
                    || string.IsNullOrEmpty(fact.EvolutionCardId)
                    || (fact.EvolutionCardType != "spell"
                        && fact.EvolutionCardType != "battleground_spell"))
                    return false;
                rules.Add(new AnomalyRegistry.TypedRule
                {
                    Type = "scheduled_card_grant",
                    EveryTurns = value,
                    CardId = fact.EvolutionCardId,
                    CardType = fact.EvolutionCardType,
                    Count = 1,
                });
            }

            List<int> lockedTiers;
            if (TryReadTierLockedDiscovers(text, out lockedTiers))
            {
                rules.Add(new AnomalyRegistry.TypedRule
                {
                    Type = "tier_locked_minion_discovers",
                    CountEach = 1,
                    Tiers = lockedTiers,
                });
            }
            else if (IsMatch(text,
                @"发现.{0,12}(?:等级|酒馆等级).{0,24}随从各.{0,4}个.{0,24}对应等级",
                @"discover.{0,24}(?:tier|level).{0,24}minions?.{0,24}(?:matching|corresponding)\s+(?:tier|level)"))
            {
                return false;
            }

            if (IsMatch(text,
                @"将.{0,28}变为.{0,8}(?:你的)?第二英雄技能",
                @"(?:make|turn).{0,28}(?:your\s+)?second\s+hero\s+power"))
            {
                if (string.IsNullOrEmpty(fact.EvolutionCardId)
                    || fact.EvolutionCardType != "hero_power")
                    return false;

                var rule = new AnomalyRegistry.TypedRule
                {
                    Type = "second_hero_power",
                    CardId = fact.EvolutionCardId,
                    CardType = "hero_power",
                };
                var unlock = Match(text,
                    @"第\s*(\d+)\s*回合解锁",
                    @"unlock(?:s|ed)?\s+(?:on|at)\s+turn\s*(\d+)");
                if (unlock.Success)
                {
                    if (!TryReadInt(unlock, 1, 20, out value)) return false;
                    rule.UnlockTurn = value;
                }
                if (IsMatch(text,
                    @"小型",
                    @"lesser"))
                    rule.CopiesPurchasedTrinket = "lesser";
                else if (IsMatch(text,
                    @"大型",
                    @"greater"))
                    rule.CopiesPurchasedTrinket = "greater";
                rules.Add(rule);

                if (!rule.UnlockTurn.HasValue && fact.ScriptData[0] > 0)
                {
                    int scheduledTurn = fact.ScriptData[0] + 1;
                    if (scheduledTurn == 5)
                    {
                        rules.Add(new AnomalyRegistry.TypedRule
                        {
                            Type = "scheduled_lesser_timewarp",
                            Turn = scheduledTurn,
                        });
                    }
                    else if (scheduledTurn == 8)
                    {
                        rules.Add(new AnomalyRegistry.TypedRule
                        {
                            Type = "scheduled_greater_timewarp",
                            Turn = scheduledTurn,
                        });
                    }
                }
            }

            if (IsMatch(text,
                @"(?:对战|游戏).{0,8}开始.{0,12}发现.{0,8}(?:一项|一个)?第二英雄技能",
                @"start.{0,16}discover.{0,8}(?:a\s+)?second\s+hero\s+power"))
            {
                rules.Add(new AnomalyRegistry.TypedRule
                {
                    Type = "discover_second_hero_power_at_game_start",
                    Count = 1,
                });
            }

            if (IsMatch(text,
                @"所有英雄.{0,8}(?:均|都)为",
                @"all\s+heroes.{0,8}(?:are|become)"))
            {
                if (string.IsNullOrEmpty(fact.OverrideHeroCardId)) return false;
                rules.Add(new AnomalyRegistry.TypedRule
                {
                    Type = "all_heroes_override",
                    StringValue = "manager_marin",
                    HeroCardId = fact.OverrideHeroCardId,
                });
            }

            if (IsMatch(text,
                @"每个回合开始时.{0,16}(?:获取|获得)一张",
                @"start\s+of\s+each\s+turn.{0,16}(?:get|gain)\s+a\s+card"))
            {
                if (!fact.IsDuosExclusive
                    || string.IsNullOrEmpty(fact.EvolutionCardId)
                    || (fact.EvolutionCardType != "spell"
                        && fact.EvolutionCardType != "battleground_spell"))
                    return false;
                rules.Add(new AnomalyRegistry.TypedRule
                {
                    Type = "portal_in_bottle_at_turn_start",
                    CardId = fact.EvolutionCardId,
                    CardType = fact.EvolutionCardType,
                    Count = 1,
                });
            }

            match = Match(text,
                @"每回合.{0,20}向.{0,8}队友.{0,8}发送最多\s*(\d+)\s*枚?铸币",
                @"each\s+turn.{0,20}send.{0,8}teammate.{0,8}(?:up\s+to\s+)?(\d+)\s*(?:gold|coins?)");
            if (match.Success)
            {
                if (!fact.IsDuosExclusive
                    || !TryReadInt(match, 1, 20, out value)
                    || string.IsNullOrEmpty(fact.EvolutionCardId)
                    || (fact.EvolutionCardType != "hero_power"
                        && fact.EvolutionCardType != "spell"
                        && fact.EvolutionCardType != "battleground_spell"))
                    return false;
                rules.Add(new AnomalyRegistry.TypedRule
                {
                    Type = "send_gold_to_teammate",
                    CardId = fact.EvolutionCardId,
                    CardType = fact.EvolutionCardType,
                    GoldPerUse = 1,
                    MaxPerTurn = value,
                });
            }

            match = Match(text,
                @"第\s*(\d+)\s*回合.{0,12}前往.{0,8}小型时空扭曲",
                @"(?:on\s+)?turn\s*(\d+).{0,12}visit.{0,8}(?:the\s+)?lesser timewarp");
            if (TryReadInt(match, 1, 20, out value))
            {
                rules.Add(new AnomalyRegistry.TypedRule
                {
                    Type = "scheduled_lesser_timewarp",
                    Turn = value,
                });
            }

            List<int> greaterTurns;
            if (TryReadScheduledGreaterTurns(text, out greaterTurns))
            {
                var rule = new AnomalyRegistry.TypedRule
                {
                    Type = "scheduled_greater_timewarp",
                };
                if (greaterTurns.Count == 1) rule.Turn = greaterTurns[0];
                else rule.Turns = greaterTurns;
                rules.Add(rule);
            }
            else if (IsMatch(text,
                @"第.{0,20}回合.{0,16}前往.{0,8}大型时空扭曲",
                @"turn.{0,20}visit.{0,8}(?:the\s+)?greater timewarp"))
            {
                return false;
            }

            if (IsMatch(text,
                @"(?:某个|一个)随机回合.{0,12}(?:额外)?(?:前往|进入)一次时空扭曲",
                @"(?:a|one)\s+random\s+turn.{0,16}(?:extra|additional)?.{0,8}(?:visit|enter).{0,8}timewarp"))
            {
                rules.Add(new AnomalyRegistry.TypedRule
                {
                    Type = "random_extra_timewarp",
                    Count = 1,
                });
            }

            if (IsMatch(text,
                @"(?:没有|不会有|禁用)小型时空扭曲",
                @"(?:no|without|disable(?:d)?)\s+(?:the\s+)?lesser timewarp"))
                rules.Add(BoolRule("lesser_timewarp_enabled", false));

            match = Match(text,
                @"大型时空扭曲.{0,12}(?:会)?提供\s*(\d+)\s*个金色随从",
                @"greater timewarps?.{0,12}offer\s*(\d+)\s+golden minions?");
            if (match.Success)
            {
                if (!TryReadInt(match, 1, 7, out value)
                    || !IsMatch(text,
                        @"(?:这些|该).{0,8}随从.{0,8}(?:不会|不).{0,8}(?:获得|获取)三连奖励",
                        @"(?:they|these minions?).{0,8}(?:do not|don't|no).{0,8}(?:grant|get).{0,8}triple rewards?"))
                    return false;
                rules.Add(new AnomalyRegistry.TypedRule
                {
                    Type = "greater_timewarp_golden_offers",
                    Count = value,
                    GrantsTripleReward = false,
                });
            }

            match = Match(text,
                @"小型时空扭曲.{0,12}(?:会)?提供\s*(\d+)\s*个金色随从",
                @"lesser timewarps?.{0,12}offer\s*(\d+)\s+golden minions?");
            if (match.Success)
            {
                if (!TryReadInt(match, 1, 7, out value)
                    || !IsMatch(text,
                        @"(?:这些|该).{0,8}随从.{0,8}(?:不会|不).{0,8}(?:获得|获取)三连奖励",
                        @"(?:they|these minions?).{0,8}(?:do not|don't|no).{0,8}(?:grant|get).{0,8}triple rewards?"))
                    return false;
                rules.Add(new AnomalyRegistry.TypedRule
                {
                    Type = "lesser_timewarp_golden_offers",
                    Count = value,
                    GrantsTripleReward = false,
                });
            }

            match = Match(text,
                @"每次时空扭曲.{0,16}额外获得\s*([一二三四五六七八九十\d]+)\s*枚?时光标记",
                @"each timewarp.{0,16}(?:get|gain)\s+(?:an\s+)?extra\s+(\d+)\s+timewarp marks?");
            if (match.Success)
            {
                if (!TryReadCount(match.Groups[1].Value, 1, 20, out value)) return false;
                var rule = IntRule("timewarp_mark_delta", value);
                rule.Period = "timewarp";
                rules.Add(rule);
            }

            match = Match(text,
                @"(?:全部|总共)\s*(\d+)\s*枚?时光标记.{0,24}小型时空扭曲.{0,32}(?:保留|带到|延续).{0,12}大型时空扭曲",
                @"(?:all|total)\s*(\d+)\s+timewarp marks?.{0,24}lesser timewarp.{0,32}(?:carry|remain|keep).{0,12}greater timewarp");
            if (match.Success)
            {
                if (!TryReadInt(match, 1, 20, out value)) return false;
                var rule = IntRule("shared_timewarp_mark_budget", value);
                rule.CarryToGreater = true;
                rules.Add(rule);
            }

            match = Match(text,
                @"小型时空扭曲.{0,8}随从.{0,12}第\s*(\d+)\s*回合.{0,12}进入.{0,8}(?:酒馆)?随从池",
                @"lesser timewarp.{0,8}minions?.{0,16}(?:enter|join).{0,12}(?:pool|tavern pool).{0,8}turn\s*(\d+)");
            if (TryReadInt(match, 1, 20, out value))
            {
                rules.Add(new AnomalyRegistry.TypedRule
                {
                    Type = "merge_lesser_timewarp_pool",
                    Turn = value,
                });
            }

            match = Match(text,
                @"大型时空扭曲.{0,8}随从.{0,12}第\s*(\d+)\s*回合.{0,12}进入.{0,8}(?:酒馆)?随从池",
                @"greater timewarp.{0,8}minions?.{0,16}(?:enter|join).{0,12}(?:pool|tavern pool).{0,8}turn\s*(\d+)");
            if (TryReadInt(match, 1, 20, out value))
            {
                rules.Add(new AnomalyRegistry.TypedRule
                {
                    Type = "merge_greater_timewarp_pool",
                    Turn = value,
                });
            }

            return rules.Count > 0 && HasUniqueRuleTypes(rules);
        }

        private static bool TryDetermineLifecycle(
            IReadOnlyCollection<AnomalyRegistry.TypedRule> rules,
            out string lifecycle)
        {
            lifecycle = null;
            if (rules == null || rules.Count == 0) return false;

            var effectTypes = new HashSet<string>(StringComparer.Ordinal)
            {
                "greater_timewarp_golden_offers",
                "lesser_timewarp_golden_offers",
                "timewarp_mark_delta",
                "shared_timewarp_mark_budget",
            };
            bool hasEffect = rules.Any(rule => effectTypes.Contains(rule.Type));
            bool hasLesserDisabled = rules.Any(rule => rule.Type == "lesser_timewarp_enabled");
            bool hasGreaterSchedule = rules.Any(rule => rule.Type == "scheduled_greater_timewarp");
            bool hasOtherRules = rules.Any(rule => !effectTypes.Contains(rule.Type)
                && rule.Type != "lesser_timewarp_enabled"
                && rule.Type != "scheduled_greater_timewarp");

            if (hasEffect)
            {
                if (hasLesserDisabled || hasGreaterSchedule || hasOtherRules) return false;
                lifecycle = "timewarp_effect";
                return true;
            }

            if (hasLesserDisabled)
            {
                if (!hasGreaterSchedule || hasOtherRules) return false;
                lifecycle = "timewarp_effect";
                return true;
            }

            lifecycle = "primary";
            return true;
        }

        private static bool TryReadScheduledGreaterTurns(string text, out List<int> turns)
        {
            turns = null;
            var multiple = Match(text,
                @"第\s*(\d+)\s*回合和第\s*(\d+)\s*回合.{0,8}(?:各)?前往.{0,8}大型时空扭曲",
                @"turn\s*(\d+)\s+and\s+turn\s*(\d+).{0,8}visit.{0,8}(?:the\s+)?greater timewarp");
            if (multiple.Success)
            {
                int first;
                int second;
                if (!TryReadInt(multiple, 1, 20, 1, out first)
                    || !TryReadInt(multiple, 1, 20, 2, out second)
                    || first == second)
                    return false;
                turns = new List<int> { first, second };
                return true;
            }

            var single = Match(text,
                @"第\s*(\d+)\s*回合.{0,12}前往.{0,8}大型时空扭曲",
                @"(?:on\s+)?turn\s*(\d+).{0,12}visit.{0,8}(?:the\s+)?greater timewarp");
            int turn;
            if (!TryReadInt(single, 1, 20, out turn)) return false;
            turns = new List<int> { turn };
            return true;
        }

        private static bool TryReadCount(string raw, int minimum, int maximum, out int value)
        {
            value = 0;
            if (int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out value))
                return value >= minimum && value <= maximum;
            var chinese = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                { "一", 1 }, { "二", 2 }, { "三", 3 }, { "四", 4 }, { "五", 5 },
                { "六", 6 }, { "七", 7 }, { "八", 8 }, { "九", 9 }, { "十", 10 },
            };
            return chinese.TryGetValue(raw, out value) && value >= minimum && value <= maximum;
        }

        private static bool TryReadTierLockedDiscovers(string text, out List<int> tiers)
        {
            tiers = null;
            var match = Match(text,
                @"发现.{0,8}等级\s*([0-9、，,和及\s]+)的?随从各(?:一|1)个.{0,24}对应等级",
                @"discover.{0,8}(?:tier|level)s?\s*([0-9,\sand]+)\s+minions?.{0,24}(?:matching|corresponding)\s+(?:tier|level)");
            if (!match.Success) return false;

            var values = Regex.Matches(match.Groups[1].Value, @"\d+")
                .Cast<Match>()
                .Select(item => int.Parse(item.Value, CultureInfo.InvariantCulture))
                .ToList();
            if (values.Count == 0
                || values.Any(value => value < 1 || value > 7)
                || values.Distinct().Count() != values.Count)
                return false;
            tiers = values;
            return true;
        }

        private static bool HasUniqueRuleTypes(IEnumerable<AnomalyRegistry.TypedRule> rules)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            return rules.All(rule => !string.IsNullOrEmpty(rule.Type) && seen.Add(rule.Type));
        }

        private static Match Match(string text, string zhPattern, string enPattern)
        {
            var match = Regex.Match(text, zhPattern, MatchOptions);
            return match.Success ? match : Regex.Match(text, enPattern, MatchOptions);
        }

        private static bool IsMatch(string text, string zhPattern, string enPattern)
        {
            return Regex.IsMatch(text, zhPattern, MatchOptions)
                || Regex.IsMatch(text, enPattern, MatchOptions);
        }

        private static bool TryReadInt(Match match, int minimum, int maximum, out int value)
        {
            return TryReadInt(match, minimum, maximum, 1, out value);
        }

        private static bool TryReadInt(
            Match match,
            int minimum,
            int maximum,
            int group,
            out int value)
        {
            value = 0;
            return match != null
                && match.Success
                && match.Groups.Count > group
                && int.TryParse(match.Groups[group].Value, NumberStyles.None, CultureInfo.InvariantCulture, out value)
                && value >= minimum
                && value <= maximum;
        }

        private static AnomalyRegistry.TypedRule IntRule(string type, int value)
        {
            return new AnomalyRegistry.TypedRule { Type = type, IntValue = value };
        }

        private static AnomalyRegistry.TypedRule BoolRule(string type, bool value)
        {
            return new AnomalyRegistry.TypedRule { Type = type, BoolValue = value };
        }

        private static bool HaveSameSignature(
            IEnumerable<AnomalyRegistry.TypedRule> left,
            IEnumerable<AnomalyRegistry.TypedRule> right)
        {
            return string.Equals(Signature(left), Signature(right), StringComparison.Ordinal);
        }

        private static bool TryChooseRules(
            bool hasLeft,
            List<AnomalyRegistry.TypedRule> left,
            bool hasRight,
            List<AnomalyRegistry.TypedRule> right,
            out List<AnomalyRegistry.TypedRule> rules)
        {
            rules = null;
            if (hasLeft && !hasRight) { rules = left; return true; }
            if (!hasLeft && hasRight) { rules = right; return true; }
            if (!hasLeft || !hasRight) return false;
            if (HaveSameSignature(left, right)) { rules = left; return true; }

            var leftTypes = new HashSet<string>(left.Select(rule => rule.Type), StringComparer.Ordinal);
            var rightTypes = new HashSet<string>(right.Select(rule => rule.Type), StringComparer.Ordinal);
            if (leftTypes.IsProperSupersetOf(rightTypes)) { rules = left; return true; }
            if (rightTypes.IsProperSupersetOf(leftTypes)) { rules = right; return true; }
            return false;
        }

        private static string Signature(IEnumerable<AnomalyRegistry.TypedRule> rules)
        {
            return string.Join("|", rules
                .OrderBy(rule => rule.Type, StringComparer.Ordinal)
                .Select(RuleSignature));
        }

        private static string RuleSignature(AnomalyRegistry.TypedRule rule)
        {
            var value = new StringBuilder();
            Append(value, rule.Type);
            Append(value, rule.IntValue);
            Append(value, rule.BoolValue);
            Append(value, rule.StringValue);
            Append(value, rule.Turn);
            Append(value, rule.EveryTurns);
            Append(value, rule.InitialTier);
            Append(value, rule.ImprovesEveryTurns);
            Append(value, rule.Tier);
            Append(value, rule.CardId);
            Append(value, rule.HeroCardId);
            Append(value, rule.CardType);
            Append(value, rule.Count);
            Append(value, rule.ExplicitCount);
            Append(value, rule.CountEach);
            Append(value, rule.GoldPerUse);
            Append(value, rule.MaxPerTurn);
            Append(value, rule.Golden);
            Append(value, rule.GoldenInvalid);
            Append(value, rule.UnlockTurn);
            Append(value, rule.CopiesPurchasedTrinket);
            Append(value, rule.GrantsTripleReward);
            Append(value, rule.Period);
            Append(value, rule.GrantAt);
            Append(value, rule.CarryToGreater);
            Append(value, string.Join(",", rule.Tiers ?? new List<int>()));
            Append(value, string.Join(",", rule.Turns ?? new List<int>()));
            return value.ToString();
        }

        private static void Append(StringBuilder target, object value)
        {
            target.Append(value == null ? "<null>" : Convert.ToString(value, CultureInfo.InvariantCulture));
            target.Append('\u001f');
        }
    }
}
