using System;
using System.Collections.Generic;
using System.Linq;

namespace BobCoach.Engine
{
    public enum ActionType
    {
        BuyMinion,
        BuySpell,
        Refresh,
        Upgrade,
        SellMinion,
        FreezeShop,
        UseHeroPower,
        PickTrinket,
        PickDiscover,
        SendGoldToTeammate,
        Activate,
        PlayMinion,
        UseSpell,
    }

    public class GameAction
    {
        public ActionType Type;
        public int TargetIndex;    // 酒馆位置 / 手牌位置 / 场面位置
        public string CardId;      // 目标卡牌 ID
        public string PurchaseSource = ""; // tavern_shop/unknown/timewarp
        public int Cost;
        public string Description;
        public bool RequiresTarget;
    }

    /// <summary>
    /// 枚举当前状态下所有可行动作，规则参数从 GameState 动态计算。
    /// </summary>
    public class ActionEnumerator
    {
        private static Season14RulesEvaluator _season14;
        private static readonly object Season14Sync = new object();

        private static Season14RulesEvaluator GetSeason14()
        {
            lock (Season14Sync)
            {
                if (_season14 == null)
                {
                    _season14 = new Season14RulesEvaluator();
                    _season14.LoadFromConfiguredRoot();
                }
                return _season14;
            }
        }

        /// <summary>
        /// 枚举所有合法动作。heroCardId 用于英雄特定规则（米尔豪斯刷新费等）。
        /// </summary>
        public List<GameAction> Enumerate(
            GameState state, string heroCardId = "", bool includeTrinket = true)
        {
            var actions = new List<GameAction>();

            if (state == null || !state.GameActive) return actions;
            if (!state.NormalRecruitAvailable) return actions;
            var rules = state.EffectiveRules ?? EffectiveGameRules.Default;
            int liveGold = state.LiveGold >= 0 ? state.LiveGold : state.Gold;

            // 购买: 随从需板面空间(<7), 法术需手牌空间(<10)
            bool canBuyMinion = state.BoardMinions.Count < state.MaxBoardSlots
                || state.HandMinions.Count < 10;
            bool canBuySpell = state.HandMinions.Count < 10;
            for (int i = 0; i < state.ShopMinions.Count; i++)
            {
                var m = state.ShopMinions[i];
                if (m == null) continue;
                bool isSpell = m.IsSpell;
                if (isSpell && !canBuySpell) continue; // 手牌满→不能买法术
                if (!isSpell && !canBuyMinion) continue; // 板面满→不能买随从
                int itemCost = GameRuleEvaluator.GetPurchaseCost(state, m, heroCardId, rules);
                if (liveGold >= itemCost)
                {
                    string costNote = itemCost != 3 ? " (" + itemCost + "费)" : "";
                    string label = isSpell ? "使用 " : "购买 ";
                    actions.Add(new GameAction
                    {
                        Type = isSpell ? ActionType.BuySpell : ActionType.BuyMinion,
                        TargetIndex = i,
                        CardId = m.CardId,
                        PurchaseSource = "tavern_shop",
                        Description = label + m.CardName + " (T" + m.Tier + ")" + costNote
                    });
                }
            }

            // 刷新
            for (int i = 0; i < state.HandMinions.Count; i++)
            {
                var hand = state.HandMinions[i];
                if (hand == null) continue;
                if (hand.IsFrozen) continue;
                if (hand.IsSpell)
                {
                    int spellCost = 0;
                    if (liveGold < spellCost) continue;
                    actions.Add(new GameAction
                    {
                        Type = ActionType.UseSpell,
                        TargetIndex = i,
                        CardId = hand.CardId,
                        Cost = spellCost,
                        Description = "use " + hand.CardName + " (" + spellCost + " gold)"
                    });
                }
                else if (state.BoardMinions.Count < state.MaxBoardSlots
                    && !HandCombatCardPolicy.ShouldKeepInHand(hand.CardId)
                    && !HeroPowerRuleCatalog.ShouldHoldNextTurnGoldBattlecry(
                        state.Turn, hand.CardId, state.HeroPowerCardId))
                {
                    actions.Add(new GameAction
                    {
                        Type = ActionType.PlayMinion,
                        TargetIndex = i,
                        CardId = hand.CardId,
                        Description = "play " + hand.CardName
                    });
                }
            }

            int refreshCost = GameRuleEvaluator.GetRefreshCost(state, heroCardId, rules);
            if (rules.ManualRefreshAllowed && liveGold >= refreshCost)
            {
                string desc = state.FreeRefreshCount > 0
                    ? "免费刷新 (" + state.FreeRefreshCount + " 次可用)"
                    : "刷新酒馆 (" + refreshCost + " 金币)";
                actions.Add(new GameAction
                {
                    Type = ActionType.Refresh,
                    TargetIndex = -1,
                    Description = desc
                });
            }

            // 升本
            int? upgradeCost = GameRuleEvaluator.GetUpgradeCost(state, rules);
            if (upgradeCost.HasValue && liveGold >= upgradeCost.Value)
            {
                actions.Add(new GameAction
                {
                    Type = ActionType.Upgrade,
                    Description = "升 " + (state.TavernTier + 1) + " 本 (" + upgradeCost.Value + " 金币)"
                });
            }

            // 冻结商店（未冻结且买不起牌时可操作，冻住好牌等下一回合）
            if (!state.FrozenShop && state.ShopMinions.Count > 0 && liveGold < 3)
            {
                actions.Add(new GameAction
                {
                    Type = ActionType.FreezeShop,
                    Description = "冻结商店 (0 费)"
                });
            }

            // 出售随从: 仅满场(7)且卖后有金买牌时才生成
            // 板面不满→有空位直接买, 不需要卖
            if (state.ShopMinions.Count > 0)
            {
                int minBuyCost = GameRuleEvaluator.GetPurchaseCost(
                    state, new MinionData { Tier = 1 }, heroCardId, rules);
                foreach (var shop in state.ShopMinions)
                {
                    if (shop == null) continue;
                    int cost = GameRuleEvaluator.GetPurchaseCost(
                        state, shop, heroCardId, rules);
                    if (cost >= 0 && cost < minBuyCost)
                        minBuyCost = cost;
                }
                bool hasSellableMinion = state.BoardMinions
                    .Any(m => m != null && !m.Golden);
                if (liveGold + 1 >= minBuyCost && hasSellableMinion)
                {
                    var sellCandidates = state.BoardMinions
                        .Select((m, idx) => new { m, idx, power = m.Attack * 0.6 + m.Health * 0.4 + m.Tier * 2 })
                        .Where(x => !x.m.Golden
                            && (state.RecentlyBoughtEntityIds == null
                                || !state.RecentlyBoughtEntityIds.Contains(x.m.EntityId)))
                        .OrderBy(x => x.power)
                        .Take(3)
                        .ToList();
                    foreach (var x in sellCandidates)
                    {
                        actions.Add(new GameAction
                        {
                            Type = ActionType.SellMinion,
                            TargetIndex = x.idx,
                            CardId = x.m.CardId,
                            Description = "出售 " + x.m.CardName
                        });
                    }
                }
            }

            // 英雄技能：新状态模型按技能独立枚举；空列表时保留主技能兼容路径。
            if (state.HeroPowers != null && state.HeroPowers.Count > 0)
            {
                var seenPowers = new HashSet<string>(StringComparer.Ordinal);
                foreach (var power in state.HeroPowers)
                {
                    if (power == null) continue;
                    if (HeroPowerRuleCatalog.IsUtilityPowerCardId(power.CardId))
                        continue;
                    if (!string.IsNullOrEmpty(power.CardId)
                        && !seenPowers.Add(power.CardId))
                        continue;
                    if (rules.TeammateGoldTransfer != null
                        && power.CardId == rules.TeammateGoldTransfer.ActionCardId)
                        continue;
                    int effectiveCost = power.Cost > 0
                        ? power.Cost
                        : (state.HeroPowerCost >= 0 ? state.HeroPowerCost : power.Cost);
                    if (!power.IsActive || !power.IsUnlocked
                        || power.Exhausted || liveGold < effectiveCost
                        || !HasValidHeroPowerTarget(state, power)) continue;
                    bool requiresTarget = HeroPowerRuleCatalog.RequiresTarget(
                        GetHeroCardIdForPower(state, power),
                        power.SpecialRule,
                        power.HasDiscover);
                    string costDesc = effectiveCost > 0
                        ? " (" + effectiveCost + " 费)" : " (0费)";
                    actions.Add(new GameAction
                    {
                        Type = ActionType.UseHeroPower,
                        CardId = power.CardId,
                        Cost = effectiveCost,
                        RequiresTarget = requiresTarget,
                        TargetIndex = requiresTarget ? 0 : -1,
                        Description = "使用技能" + costDesc
                    });
                }
            }
            else if (state.HeroPowerType == "Active"
                && !HeroPowerRuleCatalog.IsUtilityPowerCardId(state.HeroPowerCardId)
                && (rules.TeammateGoldTransfer == null
                    || state.HeroPowerCardId != rules.TeammateGoldTransfer.ActionCardId)
                && !state.HeroPowerExhausted
                && liveGold >= state.HeroPowerCost
                && state.Turn >= state.HeroPowerUnlockTurn
                && state.TavernTier >= state.HeroPowerUnlockTier
                && HasValidHeroPowerTarget(state))
            {
                string costDesc = state.HeroPowerCost > 0
                    ? " (" + state.HeroPowerCost + " 费)" : " (0费)";
                bool requiresTarget = HeroPowerRuleCatalog.RequiresTarget(
                    state.HeroCardId,
                    state.HeroPowerSpecial,
                    state.HeroPowerHasDiscover);
                actions.Add(new GameAction
                {
                    Type = ActionType.UseHeroPower,
                    CardId = state.HeroPowerCardId,
                    RequiresTarget = requiresTarget,
                    TargetIndex = requiresTarget ? 0 : -1,
                    Description = "使用技能" + costDesc
                });
            }

            // 发动: Season14 Activate 词条。每回合一次，费用不足或已发动时不出候选。
            var season14 = GetSeason14();
            if (season14.IsLoaded && state.ActivatedThisTurn != null)
            {
                foreach (var boardMinion in state.BoardMinions)
                {
                    if (boardMinion == null || string.IsNullOrEmpty(boardMinion.CardId))
                        continue;
                    if (state.ActivatedThisTurn.Contains(boardMinion.EntityId))
                        continue;
                    ActivateFact activate;
                    if (!season14.TryGetActivate(boardMinion.CardId, out activate))
                        continue;
                    if (liveGold < activate.ActivateCost)
                        continue;
                    string displayName = !string.IsNullOrEmpty(boardMinion.CardName)
                        ? boardMinion.CardName
                        : !string.IsNullOrEmpty(activate.NameZhCn)
                            ? activate.NameZhCn
                            : "unknown";
                    actions.Add(new GameAction
                    {
                        Type = ActionType.Activate,
                        TargetIndex = boardMinion.Position >= 0
                            ? boardMinion.Position
                            : state.BoardMinions.IndexOf(boardMinion),
                        CardId = boardMinion.CardId,
                        Cost = activate.ActivateCost,
                        Description = "发动 " + displayName + " (" + activate.ActivateCost + "费)",
                    });
                }
            }

            var transferRule = rules.TeammateGoldTransfer;
            if (state.IsDuos && transferRule != null
                && liveGold >= transferRule.GoldPerUse
                && TeammateGoldTransferEvaluator.GetUsedCount(state, state.Turn)
                    < transferRule.MaxPerTurn)
            {
                var transferPower = state.HeroPowers != null
                    ? state.HeroPowers.FirstOrDefault(power => power != null
                        && power.CardId == transferRule.ActionCardId
                        && power.IsActive && power.IsUnlocked && !power.Exhausted)
                    : null;
                if (transferPower != null)
                {
                    actions.Add(new GameAction
                    {
                        Type = ActionType.SendGoldToTeammate,
                        CardId = transferRule.ActionCardId,
                        Description = "向队友发送1枚铸币 (1费)",
                    });
                }
            }

            // 饰品报价: 与发现面板同源, 有报价即可选。
            if (includeTrinket && state.TrinketOffer != null
                && state.TrinketOffer.Count > 0)
            {
                for (int i = 0; i < state.TrinketOffer.Count; i++)
                {
                    var offer = state.TrinketOffer[i];
                    if (offer == null) continue;
                    actions.Add(new GameAction
                    {
                        Type = ActionType.PickTrinket,
                        TargetIndex = i,
                        CardId = offer.CardId,
                        Description = "选择饰品: " + offer.TrinketName
                    });
                }
            }

            // 发现选择 (3选1, 三连奖励等)
            if (state.DiscoverOptions != null && state.DiscoverOptions.Count > 0)
            {
                for (int i = 0; i < state.DiscoverOptions.Count; i++)
                {
                    actions.Add(new GameAction
                    {
                        Type = ActionType.PickDiscover,
                        TargetIndex = i,
                        CardId = state.DiscoverOptions[i].CardId,
                        Description = "选 " + state.DiscoverOptions[i].TrinketName
                    });
                }
            }

            return actions;
        }

        /// <summary>检查英雄技能在当前板面/商店状态下是否有有效目标</summary>
        private static bool HasValidHeroPowerTarget(GameState state, HeroPowerState power = null)
        {
            var sp = power != null ? (power.SpecialRule ?? "") : (state.HeroPowerSpecial ?? "");
            string hid = power != null
                ? (power.IsSecondary
                    ? (power.BaseHeroCardId ?? "")
                    : (!string.IsNullOrEmpty(power.BaseHeroCardId)
                        ? power.BaseHeroCardId
                        : state.HeroCardId ?? ""))
                : (state.HeroCardId ?? "");
            int board = state.BoardMinions.Count;
            int shop = state.ShopMinions.Count;

            // ═══════════════════════════════════════
            // 英雄特定覆盖: 同一special值不同英雄有不同目标条件
            // ═══════════════════════════════════════

            // 塔隆血魔: 选择友方随从消灭+复活, 不限种族(技能已更新)
            if (hid.Contains("BG25_HERO_103"))
                return board > 0;

            // 亚煞极: 战斗开始时召唤当前等级随从, 无目标限制
            if (hid.Contains("TB_BaconShop_HERO_92"))
                return true;

            // 疯狂金字塔: 偷取酒馆随从 → 需商店有随从
            if (hid.Contains("TB_BaconShop_HERO_39"))
                return shop > 0;

            // 钩牙船长: 移除一个友方随从后发现在低一级随从
            if (hid.Contains("TB_BaconShop_HERO_67"))
                return board > 0;

            // 巫妖王: 给任意随从(板面或商店)加复生
            if (hid.Contains("TB_BaconShop_HERO_22"))
                return board > 0 || shop > 0;

            // 托奇: 替换刷新效果(刷新含高1级随从), 无目标限制
            if (hid.Contains("TB_BaconShop_HERO_28"))
                return true;

            // 玛里苟斯: 替换一张牌(板面或手牌), 需有可替换目标
            if (hid.Contains("TB_BaconShop_HERO_58"))
                return board > 0 || state.HandMinions.Count > 0;

            // 沃金: 选2个随从交换攻击力(可指向板面+商店)
            if (hid.Contains("BG20_HERO_201"))
                return board + shop >= 2;

            // 努波顿: 获取上一法术复制(需使用过酒馆法术,游戏端校验)
            if (hid.Contains("BG31_HERO_003"))
                return !string.IsNullOrEmpty(state.LastUsedTavernSpellCardId);

            // ═══════════════════════════════════════
            // special规则匹配
            // ═══════════════════════════════════════

            // trigger_battlecry(_free): 沙德沃克 — 板面有战吼随从(查HearthDb mechanics)
            if (sp.Contains("trigger_battlecry"))
                return state.BoardMinions.Any(m => CardHasMechanic(m.CardId, "BATTLECRY"));

            // eat_shop_minion: 典狱长 — 板面有亡灵 + 商店有随从
            if (sp == "eat_shop_minion")
                return board > 0 && state.BoardMinions.Any(m => MinionData.TribeMatches(m.Tribe, "亡灵")) && shop > 0;

            // triple_helper: 杰弗里斯 — 板面或手牌有对子
            if (sp == "triple_helper")
                return HasPairOnBoard(state) || HasPairInHand(state);

            // summon_highest_hp: 钩牙船长 — 板面有随从可移除
            if (sp == "summon_highest_hp")
                return board > 0;

            // divine_shield: 堕落的乔治 — 板面有随从
            if (sp == "divine_shield")
                return board > 0;

            // one_time_golden: 雷诺 — 板面有随从
            if (sp == "one_time_golden")
                return board > 0;

            // alternating_buff: 因葛 — 板面有随从
            if (sp == "alternating_buff")
                return board > 0;

            // tavern_manipulation: 迦拉克隆(托奇已在上方hero覆盖) — 商店有随从
            if (sp == "tavern_manipulation")
                return shop > 0;

            // damage_to_gold: 巫妖巴兹亚尔 — 商店有随从
            if (sp == "damage_to_gold")
                return shop > 0;

            // dormant_double_stats: 玛维 — 商店有随从
            if (sp == "dormant_double_stats")
                return shop > 0;

            // copy_highest_atk: 泽瑞拉 — 商店有随从
            if (sp == "copy_highest_atk")
                return shop > 0;

            // ═══════════════════════════════════════
            // 白名单: 不需目标验证的特殊类型
            // ═══════════════════════════════════════

            // discover类技能: 目标在发现UI中选择
            if (power != null ? power.HasDiscover : state.HeroPowerHasDiscover)
                return true;

            // 以下special对应的技能点一下即可，无需指定板面/商店目标：
            //   secret(选奥秘), bet_on_winner(竞猜), choose_element(选元素),
            //   dig_golden(挖金), steal_first_kill(偷击杀), steal_opponent_board(偷板面),
            //   refresh_to_spells(刷新变法), blood_gem(鲜血宝石), custom_undead(造亡灵),
            //   spell_synergy(法术协同), discover_buddy(发现伙伴), magnetic(磁力发现),
            //   armor_based_budget(护甲转铸币等), free_swap_per_turn(随机替换),
            //   replace_higher_tier(传递随从)
            if (HeroPowerRuleCatalog.IsNoTargetSpecialRule(
                    sp,
                    hid,
                    power != null ? power.CardId : state.HeroPowerCardId))
                return true;

            // 未知special: 默认拒绝
            return false;
        }

        private static string GetHeroCardIdForPower(
            GameState state, HeroPowerState power)
        {
            if (power == null) return state != null ? state.HeroCardId : "";
            if (power.IsSecondary)
                return power.BaseHeroCardId ?? "";
            return !string.IsNullOrEmpty(power.BaseHeroCardId)
                ? power.BaseHeroCardId
                : (state != null ? state.HeroCardId : "");
        }

        private static bool HasPairOnBoard(GameState state)
        {
            var counts = new Dictionary<string, int>();
            foreach (var m in state.BoardMinions)
            {
                if (string.IsNullOrEmpty(m.CardId)) continue;
                int c; counts.TryGetValue(m.CardId, out c);
                counts[m.CardId] = c + 1;
            }
            return counts.Values.Any(v => v >= 2);
        }

        private static bool HasPairInHand(GameState state)
        {
            // 检查手牌+板面组合是否有对子
            var allIds = new List<string>();
            foreach (var m in state.BoardMinions) { if (!string.IsNullOrEmpty(m.CardId)) allIds.Add(m.CardId); }
            foreach (var m in state.HandMinions) { if (!string.IsNullOrEmpty(m.CardId)) allIds.Add(m.CardId); }
            var counts = allIds.GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());
            return counts.Values.Any(v => v >= 2);
        }

        /// <summary>通过HearthDb卡牌数据库查询卡牌是否含指定机制(BATTLECRY/DEATHRATTLE等)</summary>
        private static bool CardHasMechanic(string cardId, string mechanic)
        {
            if (string.IsNullOrEmpty(cardId) || string.IsNullOrEmpty(mechanic)) return false;
            try
            {
                HearthDb.Card c;
                if (HearthDb.Cards.All.TryGetValue(cardId, out c) && c != null)
                {
                    if (c.Mechanics != null && c.Mechanics.Contains(mechanic)) return true;
                    // fallback: 中文卡牌文本检测
                    string text = c.Text ?? "";
                    if (mechanic == "BATTLECRY" && text.Contains("战吼")) return true;
                    if (mechanic == "DEATHRATTLE" && text.Contains("亡语")) return true;
                }
            }
            catch { }
            return false;
        }

        // 酒馆战棋升本费用：基础费 - 每级独立折扣
        // 折扣从升到当前本的第2回合开始，每回-1，最低0
        // 基础费: 1→2:5, 2→3:7, 3→4:9, 4→5:11, 5→6:13
        public static int GetUpgradeCost(int currentTier, int turn, int lastUpgradeTurn)
        {
            int[] baseCosts = { 0, 5, 7, 9, 11, 13 };
            int baseCost = currentTier < baseCosts.Length ? baseCosts[currentTier] : 99;
            int discount = turn > lastUpgradeTurn ? turn - lastUpgradeTurn : 0;
            return Math.Max(0, baseCost - discount);
        }

        // 旧接口兼容（默认 lastUpgradeTurn=当前turn，即无折扣）
        public static int GetUpgradeCost(int currentTier, int turn)
        {
            return GetUpgradeCost(currentTier, turn, turn);
        }

        private static float ComputeAvgMinionPower(List<MinionData> minions)
        {
            if (minions == null || minions.Count == 0) return 0f;
            float total = 0f;
            foreach (var m in minions)
                total += m.Attack * 0.6f + m.Health * 0.4f;
            return total / minions.Count;
        }
    }
}
