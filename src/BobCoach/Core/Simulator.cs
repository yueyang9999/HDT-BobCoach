using System;
using System.Collections.Generic;
using System.Linq;

namespace BobCoach.Engine
{
    /// <summary>
    /// 轻量级状态模拟器。避免深拷贝，使用字段级浅拷贝 + 增量修改。
    /// 模拟购买/刷新/升本/出售后的下一状态，不模拟战斗细节。
    /// </summary>
    public class Simulator
    {
        // 星级基础战力（与 FeatureExtractor 对齐）
        private static readonly Dictionary<int, double> TierPower = new Dictionary<int, double>
        {
            { 1, 0.3 }, { 2, 0.5 }, { 3, 0.8 }, { 4, 1.2 }, { 5, 1.8 }, { 6, 2.5 }, { 7, 3.5 }
        };

        /// <summary>
        /// 模拟执行 action 后的下一状态（浅拷贝，不修改原始状态）。
        /// </summary>
        public GameState Simulate(GameState state, GameAction action)
        {
            if (state == null || action == null) return state;

            var next = ShallowCopy(state);

            switch (action.Type)
            {
                case ActionType.BuyMinion:
                case ActionType.BuySpell:
                    SimulateBuy(next, action);
                    break;
                case ActionType.Refresh:
                    SimulateRefresh(next);
                    break;
                case ActionType.Upgrade:
                    SimulateUpgrade(next);
                    break;
                case ActionType.SellMinion:
                    SimulateSell(next, action);
                    break;
                case ActionType.UseHeroPower:
                    SimulateHeroPower(next, action);
                    break;
                case ActionType.PickDiscover:
                    SimulatePickDiscover(next, action);
                    break;
                case ActionType.FreezeShop:
                    // 冻结商店: 无状态变化(仅flag, ShallowCopy已拷贝)
                    break;
                case ActionType.PickTrinket:
                    // 饰品选择: 状态不变(本回合不消耗金币)
                    break;
                case ActionType.SendGoldToTeammate:
                    SimulateSendGoldToTeammate(next, action);
                    break;
            }

            return next;
        }

        private void SimulateBuy(GameState state, GameAction action)
        {
            if (action.TargetIndex < 0 || action.TargetIndex >= state.ShopMinions.Count)
                return;

            var bought = state.ShopMinions[action.TargetIndex];
            bool isSpell = bought.IsSpell;
            if ((action.Type == ActionType.BuySpell) != isSpell) return;
            if (isSpell && state.HandMinions.Count >= 10) return;
            if (!isSpell && state.BoardMinions.Count >= 7 && state.HandMinions.Count >= 10)
                return;
            var rules = state.EffectiveRules ?? EffectiveGameRules.Default;
            int cost = GameRuleEvaluator.GetPurchaseCost(state, bought, state.HeroCardId, rules);
            bool isFirstPurchase = !state.FirstPurchaseUsedThisTurn;

            if (state.Gold < cost) return; // 买不起
            state.Gold -= cost;

            // 从商店移除
            state.ShopMinions.RemoveAt(action.TargetIndex);

            // 加入手牌或直接上场
            if (isSpell)
            {
                state.HandMinions.Add(bought);
            }
            else if (state.BoardMinions.Count < 7)
            {
                state.BoardMinions.Add(bought);
            }
            else
            {
                state.HandMinions.Add(bought);
            }

            if (rules.FirstPurchaseExtraCopy != null && isFirstPurchase
                && action.PurchaseSource == "tavern_shop")
            {
                var copyRule = rules.FirstPurchaseExtraCopy;
                state.PendingPurchaseRewardExpectations.Add(new PurchaseRewardExpectation(
                    copyRule.SourceId + ":first_purchase_extra_copy@" + state.Turn,
                    bought.CardId, copyRule.ExtraCopyCount, isSpell,
                    bought.Golden, copyRule.SourceId));
            }

            if (!isSpell && (rules.GoldenCopyRequirement != 3
                    || !TripleRuleEvaluator.GrantsStandardDiscover(rules))
                && TripleRuleEvaluator.CountOwnedCopies(state, bought.CardId)
                    >= rules.GoldenCopyRequirement)
                ResolveGoldenOverride(state, bought, rules);

            state.FirstPurchaseUsedThisTurn = true;
            if (!isSpell) state.FirstMinionPurchaseUsedThisTurn = true;
            if (rules.RefreshAfterPurchase)
                ReplaceShop(state);
        }

        private static void ResolveGoldenOverride(
            GameState state, MinionData bought, EffectiveGameRules rules)
        {
            int remaining = rules.GoldenCopyRequirement;
            remaining -= RemoveNormalCopies(state.BoardMinions, bought.CardId, remaining);
            if (remaining > 0)
                RemoveNormalCopies(state.HandMinions, bought.CardId, remaining);

            var golden = CloneMinion(bought);
            golden.Golden = true;
            golden.EntityId = 0;
            state.HandMinions.Add(golden);

            if (string.Equals(rules.GoldenRewardOverride, "tavern_coin", StringComparison.Ordinal))
            {
                state.HandMinions.Add(new MinionData
                {
                    CardId = "BG28_810",
                    CardName = "酒馆币",
                    Tier = 0,
                    Cost = 0,
                    IsSpell = true,
                });
            }
        }

        private static int RemoveNormalCopies(
            List<MinionData> cards, string cardId, int limit)
        {
            int removed = 0;
            for (int i = cards.Count - 1; i >= 0 && removed < limit; i--)
            {
                var card = cards[i];
                if (card != null && !card.Golden && !card.IsSpell
                    && string.Equals(card.CardId, cardId, StringComparison.Ordinal))
                {
                    cards.RemoveAt(i);
                    removed++;
                }
            }
            return removed;
        }

        private static MinionData CloneMinion(MinionData source)
        {
            return new MinionData
            {
                CardId = source.CardId,
                CardName = source.CardName,
                Attack = source.Attack,
                Health = source.Health,
                Tier = source.Tier,
                Position = source.Position,
                Golden = source.Golden,
                Taunt = source.Taunt,
                DivineShield = source.DivineShield,
                Windfury = source.Windfury,
                MegaWindfury = source.MegaWindfury,
                Stealth = source.Stealth,
                Reborn = source.Reborn,
                Poisonous = source.Poisonous,
                Venomous = source.Venomous,
                Cleave = source.Cleave,
                Overkill = source.Overkill,
                AvengeCount = source.AvengeCount,
                CardText = source.CardText,
                Tribe = source.Tribe,
                IsSpell = source.IsSpell,
                Cost = source.Cost,
                IsFrozen = source.IsFrozen,
            };
        }

        private void SimulateRefresh(GameState state)
        {
            var rules = state.EffectiveRules ?? EffectiveGameRules.Default;
            if (!rules.ManualRefreshAllowed) return;

            int cost = GameRuleEvaluator.GetRefreshCost(state, state.HeroCardId, rules);
            if (state.Gold < cost) return;
            state.Gold -= cost;
            if (state.FreeRefreshCount > 0)
                state.FreeRefreshCount--;

            ReplaceShop(state);
        }

        private void ReplaceShop(GameState state)
        {
            // v1.5: 使用真实卡池采样替代假卡占位符
            if (CardPoolSampler.IsInitialized)
            {
                var availableTribes = state.AvailableTribes != null
                    ? new HashSet<string>(state.AvailableTribes) : new HashSet<string>();
                state.ShopMinions = CardPoolSampler.SampleShop(
                    state.TavernTier, availableTribes,
                    state.EffectiveRules ?? EffectiveGameRules.Default, state.Turn);
            }
            else
            {
                // 降级: 占位符
                var avgPower = TierPower.ContainsKey(state.TavernTier)
                    ? TierPower[state.TavernTier] : 0.5;
                state.ShopMinions = new List<MinionData>();
                for (int i = 0; i < Math.Min(state.TavernTier + 1, 6); i++)
                {
                    state.ShopMinions.Add(new MinionData
                    {
                        CardId = "sim_refresh",
                        CardName = "刷新随从",
                        Tier = state.TavernTier,
                        Attack = (int)(avgPower * 3),
                        Health = (int)(avgPower * 3),
                        Position = i,
                    });
                }
            }
        }

        private void SimulateUpgrade(GameState state)
        {
            var rules = state.EffectiveRules ?? EffectiveGameRules.Default;
            int? cost = GameRuleEvaluator.GetUpgradeCost(state, rules);
            if (!cost.HasValue || state.Gold < cost.Value) return;
            int fromTier = state.TavernTier;
            state.Gold -= cost.Value;
            state.TavernTier++;
            state.LastUpgradeTurn = state.Turn;  // 记录升级回合
            var occurrence = UpgradePrizeEvaluator.CreateOccurrence(
                rules.UpgradePrize, state.Turn, fromTier,
                state.TavernTier, "simulated_action");
            var expectation = UpgradePrizeEvaluator.CreateExpectation(
                rules.UpgradePrize, occurrence);
            if (occurrence != null) state.TavernUpgradeOccurrences.Add(occurrence);
            if (expectation != null)
                state.PendingPrizeDiscoverExpectations.Add(expectation);
        }

        private void SimulateSell(GameState state, GameAction action)
        {
            if (action.TargetIndex < 0 || action.TargetIndex >= state.BoardMinions.Count)
                return;

            var sold = state.BoardMinions[action.TargetIndex];
            state.BoardMinions.RemoveAt(action.TargetIndex);
            state.Gold += 1; // 出售获得 1 金币

            // 特殊卡牌额外金币（如白赚赌徒返 3 金币）
            if (sold.CardId == "BGS_049") state.Gold += 2;
        }

        private void SimulateHeroPower(GameState state, GameAction action)
        {
            var transferRule = (state.EffectiveRules ?? EffectiveGameRules.Default)
                .TeammateGoldTransfer;
            if (transferRule != null && action.CardId == transferRule.ActionCardId)
                return;
            HeroPowerState selectedPower = null;
            if (state.HeroPowers != null && state.HeroPowers.Count > 0)
            {
                selectedPower = state.HeroPowers.FirstOrDefault(power => power != null
                    && power.CardId == action.CardId);
                if (selectedPower == null || !selectedPower.IsActive || !selectedPower.IsUnlocked
                    || selectedPower.Exhausted || state.Gold < selectedPower.Cost) return;
                state.Gold -= selectedPower.Cost;
                selectedPower.Exhausted = true;
                state.HasSecondHeroPower = state.HeroPowers.Count(power => power.IsSecondary) > 0;
                state.ExhaustedHeroPowerCount = state.HeroPowers.Count(power => power.Exhausted);
                if (selectedPower.IsPrimary)
                    state.HeroPowerExhausted = true;
            }
            else
            {
                if (state.HeroPowerExhausted || state.Gold < state.HeroPowerCost) return;
                state.Gold -= state.HeroPowerCost;
                state.HeroPowerExhausted = true;
            }
            string powerCardId = selectedPower != null
                ? selectedPower.CardId : state.HeroPowerCardId;
            int powerCost = selectedPower != null
                ? selectedPower.Cost : state.HeroPowerCost;
            // 塞纳留斯类: 铸币上限+1
            if (powerCardId == "BG32_HERO_001p")
                state.MaxGold = Math.Min(11, state.MaxGold + 1);
            // 沙德沃克(0费): 触发战吼 → 场面有战吼随从时提升板面战力
            if (powerCost == 0 && powerCardId != null
                && powerCardId.Contains("23"))
            {
                // 检查场面是否有战吼随从(Integer power proxy: +5 attack体现战吼二次触发)
                bool hasBattlecry = false;
                foreach (var bm in state.BoardMinions)
                {
                    var name = bm.CardName ?? "";
                    if (name.Contains("侦查员") || name.Contains("发现") || name.Contains("战吼")
                        || name.Contains("铜须") || bm.CardId == "BG28_300")
                    { hasBattlecry = true; break; }
                }
                if (hasBattlecry)
                {
                    // 模拟战吼再触发: 给战吼随从+temp buff(简化为板面power增加)
                    foreach (var bm in state.BoardMinions)
                        if (!bm.Golden) { bm.Attack += 3; bm.Health += 2; break; }
                }
            }
        }

        private void SimulateSendGoldToTeammate(GameState state, GameAction action)
        {
            var rule = (state.EffectiveRules ?? EffectiveGameRules.Default)
                .TeammateGoldTransfer;
            if (!state.IsDuos || rule == null || action.CardId != rule.ActionCardId
                || state.Gold < rule.GoldPerUse
                || TeammateGoldTransferEvaluator.GetUsedCount(state, state.Turn)
                    >= rule.MaxPerTurn)
                return;
            var actionPower = state.HeroPowers != null
                ? state.HeroPowers.FirstOrDefault(power => power != null
                    && power.CardId == rule.ActionCardId && power.IsActive
                    && power.IsUnlocked && !power.Exhausted)
                : null;
            if (actionPower == null) return;

            int ordinal = TeammateGoldTransferEvaluator.GetUsedCount(state, state.Turn) + 1;
            state.Gold -= rule.GoldPerUse;
            state.SimulatedTeammateGoldTransfers.Add(new SimulatedTeammateGoldTransfer(
                rule.SourceId + ":send_gold_to_teammate@" + state.Turn + "#" + ordinal,
                state.Turn, rule.GoldPerUse, rule.SourceId));
        }

        private void SimulatePickDiscover(GameState state, GameAction action)
        {
            if (state.DiscoverOptions == null || action.TargetIndex < 0
                || action.TargetIndex >= state.DiscoverOptions.Count) return;
            var picked = state.DiscoverOptions[action.TargetIndex];
            // 发现卡星级: 查HearthDb实体卡库, 降级到DiscoverOption自带Tier, 最终默认1
            int tier = picked.Tier > 0 ? picked.Tier : 1;
            if (tier <= 1)
            {
                try
                {
                    HearthDb.Card c;
                    if (HearthDb.Cards.All.TryGetValue(picked.CardId, out c) && c != null && c.TechLevel > 0)
                        tier = c.TechLevel;
                }
                catch { }
            }
            int atk = picked.Attack > 0 ? picked.Attack : 0;
            int hp = picked.Health > 0 ? picked.Health : 0;
            state.HandMinions.Add(new MinionData
            {
                CardId = picked.CardId,
                CardName = picked.TrinketName,
                Tier = tier,
                Attack = atk,
                Health = hp,
            });
            state.DiscoverOptions.Clear(); // 选完后清除
        }

        // ── 浅拷贝 ──

        private GameState ShallowCopy(GameState src)
        {
            return new GameState
            {
                GameActive = src.GameActive,
                IsDuos = src.IsDuos,
                AnomalyId = src.AnomalyId,
                AnomalyContext = src.AnomalyContext ?? AnomalyContext.Empty,
                EffectiveRules = src.EffectiveRules ?? EffectiveGameRules.Default,
                HeroCardId = src.HeroCardId,
                HeroName = src.HeroName,
                HeroPowerCost = src.HeroPowerCost,
                HeroPowerCardId = src.HeroPowerCardId,
                HeroPowerType = src.HeroPowerType,
                HeroPowerExhausted = src.HeroPowerExhausted,
                HasSecondHeroPower = src.HasSecondHeroPower,
                ExhaustedHeroPowerCount = src.ExhaustedHeroPowerCount,
                PlayerId = src.PlayerId,
                Gold = src.Gold,
                MaxGold = src.MaxGold,
                TavernTier = src.TavernTier,
                TavernUpgradeCost = src.TavernUpgradeCost,
                Health = src.Health,
                MaxHealth = src.MaxHealth,
                Turn = src.Turn,
                Phase = src.Phase,
                FrozenShop = src.FrozenShop,
                FreeRefreshCount = src.FreeRefreshCount,
                BoardMinions = new List<MinionData>(src.BoardMinions),
                HandMinions = new List<MinionData>(src.HandMinions),
                ShopMinions = new List<MinionData>(src.ShopMinions),
                HeroOptions = src.HeroOptions != null
                    ? new List<HeroOption>(src.HeroOptions) : new List<HeroOption>(),
                TrinketOffer = src.TrinketOffer != null
                    ? new List<TrinketOption>(src.TrinketOffer) : new List<TrinketOption>(),
                DiscoverOptions = src.DiscoverOptions != null
                    ? new List<TrinketOption>(src.DiscoverOptions) : new List<TrinketOption>(),
                Opponents = src.Opponents != null
                    ? new List<OpponentData>(src.Opponents) : new List<OpponentData>(),
                LastUpgradeTurn = src.LastUpgradeTurn,
                FirstPurchaseUsedThisTurn = src.FirstPurchaseUsedThisTurn,
                FirstMinionPurchaseUsedThisTurn = src.FirstMinionPurchaseUsedThisTurn,
                ClaimedScheduledGrantOccurrences = src.ClaimedScheduledGrantOccurrences != null
                    ? new HashSet<string>(src.ClaimedScheduledGrantOccurrences) : new HashSet<string>(),
                ObservedStartResourceExpectations = src.ObservedStartResourceExpectations != null
                    ? new HashSet<string>(src.ObservedStartResourceExpectations) : new HashSet<string>(),
                MismatchedStartResourceExpectations = src.MismatchedStartResourceExpectations != null
                    ? new HashSet<string>(src.MismatchedStartResourceExpectations) : new HashSet<string>(),
                PendingPurchaseRewardExpectations = src.PendingPurchaseRewardExpectations != null
                    ? new List<PurchaseRewardExpectation>(src.PendingPurchaseRewardExpectations)
                    : new List<PurchaseRewardExpectation>(),
                ClaimedPurchaseRewardOccurrences = src.ClaimedPurchaseRewardOccurrences != null
                    ? new HashSet<string>(src.ClaimedPurchaseRewardOccurrences) : new HashSet<string>(),
                TavernUpgradeOccurrences = src.TavernUpgradeOccurrences != null
                    ? new List<TavernUpgradeOccurrence>(src.TavernUpgradeOccurrences)
                    : new List<TavernUpgradeOccurrence>(),
                PendingPrizeDiscoverExpectations = src.PendingPrizeDiscoverExpectations != null
                    ? new List<PrizeDiscoverExpectation>(src.PendingPrizeDiscoverExpectations)
                    : new List<PrizeDiscoverExpectation>(),
                ClaimedPrizeDiscoverOccurrences = src.ClaimedPrizeDiscoverOccurrences != null
                    ? new HashSet<string>(src.ClaimedPrizeDiscoverOccurrences)
                    : new HashSet<string>(),
                TurnStartCardGrantOccurrences = src.TurnStartCardGrantOccurrences != null
                    ? new List<TurnStartCardGrantExpectation>(src.TurnStartCardGrantOccurrences)
                    : new List<TurnStartCardGrantExpectation>(),
                PendingTurnStartCardGrantExpectations =
                    src.PendingTurnStartCardGrantExpectations != null
                    ? new List<TurnStartCardGrantExpectation>(
                        src.PendingTurnStartCardGrantExpectations)
                    : new List<TurnStartCardGrantExpectation>(),
                ClaimedTurnStartCardGrantOccurrences =
                    src.ClaimedTurnStartCardGrantOccurrences != null
                    ? new HashSet<string>(src.ClaimedTurnStartCardGrantOccurrences)
                    : new HashSet<string>(),
                SharedTurnEventOccurrences = src.SharedTurnEventOccurrences != null
                    ? new List<SharedTurnEventExpectation>(src.SharedTurnEventOccurrences)
                    : new List<SharedTurnEventExpectation>(),
                PendingSharedTurnEvents = src.PendingSharedTurnEvents != null
                    ? new List<SharedTurnEventExpectation>(src.PendingSharedTurnEvents)
                    : new List<SharedTurnEventExpectation>(),
                ObservedSharedTurnEventOutcomes =
                    src.ObservedSharedTurnEventOutcomes != null
                    ? new List<SharedTurnEventOutcome>(src.ObservedSharedTurnEventOutcomes)
                    : new List<SharedTurnEventOutcome>(),
                SharedCardVoteOccurrences = src.SharedCardVoteOccurrences != null
                    ? new List<SharedCardVoteOccurrence>(src.SharedCardVoteOccurrences)
                    : new List<SharedCardVoteOccurrence>(),
                PendingSharedCardVoteSelections = src.PendingSharedCardVoteSelections != null
                    ? new List<SharedCardVoteOccurrence>(src.PendingSharedCardVoteSelections)
                    : new List<SharedCardVoteOccurrence>(),
                ObservedSharedCardVoteSelections = src.ObservedSharedCardVoteSelections != null
                    ? new List<SharedCardVoteSelection>(src.ObservedSharedCardVoteSelections)
                    : new List<SharedCardVoteSelection>(),
                SharedCardGrantExpectations = src.SharedCardGrantExpectations != null
                    ? new List<SharedCardGrantExpectation>(src.SharedCardGrantExpectations)
                    : new List<SharedCardGrantExpectation>(),
                ObservedLocalSharedCardGrants = src.ObservedLocalSharedCardGrants != null
                    ? new List<SharedCardGrantObservation>(src.ObservedLocalSharedCardGrants)
                    : new List<SharedCardGrantObservation>(),
                HeroIdentityExpectations = src.HeroIdentityExpectations != null
                    ? new List<HeroIdentityExpectation>(src.HeroIdentityExpectations)
                    : new List<HeroIdentityExpectation>(),
                SecondHeroPowerChoiceExpectations = src.SecondHeroPowerChoiceExpectations != null
                    ? new List<SecondHeroPowerChoiceExpectation>(
                        src.SecondHeroPowerChoiceExpectations)
                    : new List<SecondHeroPowerChoiceExpectation>(),
                ObservedSecondHeroPowerChoiceBatches =
                    src.ObservedSecondHeroPowerChoiceBatches != null
                    ? new List<SecondHeroPowerChoiceBatchObservation>(
                        src.ObservedSecondHeroPowerChoiceBatches)
                    : new List<SecondHeroPowerChoiceBatchObservation>(),
                ObservedSecondHeroPowerChoiceSelections =
                    src.ObservedSecondHeroPowerChoiceSelections != null
                    ? new List<SecondHeroPowerChoiceSelection>(
                        src.ObservedSecondHeroPowerChoiceSelections)
                    : new List<SecondHeroPowerChoiceSelection>(),
                ObservedSecondHeroPowerEntities = src.ObservedSecondHeroPowerEntities != null
                    ? new List<SecondHeroPowerEntityObservation>(
                        src.ObservedSecondHeroPowerEntities)
                    : new List<SecondHeroPowerEntityObservation>(),
                SimulatedTeammateGoldTransfers = src.SimulatedTeammateGoldTransfers != null
                    ? new List<SimulatedTeammateGoldTransfer>(
                        src.SimulatedTeammateGoldTransfers)
                    : new List<SimulatedTeammateGoldTransfer>(),
                ObservedTeammateGoldTransfers = src.ObservedTeammateGoldTransfers != null
                    ? new List<ObservedTeammateGoldTransfer>(
                        src.ObservedTeammateGoldTransfers)
                    : new List<ObservedTeammateGoldTransfer>(),
                ReplenishingShopActive = src.ReplenishingShopActive,
                Armor = src.Armor,
                AvailableTribes = src.AvailableTribes != null
                    ? new HashSet<string>(src.AvailableTribes) : new HashSet<string>(),
                ActiveTrinkets = src.ActiveTrinkets != null
                    ? new List<string>(src.ActiveTrinkets) : new List<string>(),
                HeroPowerUnlockTurn = src.HeroPowerUnlockTurn,
                HeroPowerUnlockTier = src.HeroPowerUnlockTier,
                HeroPowerSpecial = src.HeroPowerSpecial,
                HeroPowerHasDiscover = src.HeroPowerHasDiscover,
                HeroPowers = src.HeroPowers != null
                    ? src.HeroPowers.Select(power => power.Copy()).ToList()
                    : new List<HeroPowerState>(),
                _nodeHandIdx = -1,           // 重置Node桥接字段, 模拟状态不继承手牌推荐
                _nodeHandReason = "",
                _nodeHeroPower = false,
            };
        }
    }
}
