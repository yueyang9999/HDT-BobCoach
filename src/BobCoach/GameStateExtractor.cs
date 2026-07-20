using System;
using System.Collections.Generic;
using System.Linq;
using Hearthstone_Deck_Tracker;
using Hearthstone_Deck_Tracker.Hearthstone.Entities;
using HearthDb.Enums;
using BobCoach.Engine;

namespace BobCoach
{
    /// <summary>
    /// 从 Core.Game.Entities 提取酒馆战棋游戏状态，输出强类型 GameState 供决策引擎消费。
    /// 提取逻辑基于 BobObserver 的 ExtractGameState 复用。
    /// </summary>
    public class GameStateExtractor
    {
        private int _localPlayerCtrl = -1;
        // 跨回合累积！不清空。杜绝幽灵实体被重复提取。
        private HashSet<int> _extractedShopIds = new HashSet<int>();
        // 对手随从entity ID集合，战斗阶段收集后供商店提取排除
        private HashSet<int> _opponentIds = new HashSet<int>();
        private int _lastExtractTurn = -1;
        // B17修复: 商店提取回退缓存 — 同回合内若提取为空，复用上次非空结果
        private List<Engine.MinionData> _cachedShopMinions = null;
        private int _cachedShopTurn = -1;
        // 发现选项检测 — 手牌减少+ZONE=6候选过滤 (事件驱动, 避免Spellcraft等假阳性)
        private int _prevHandCount = -1;
        // 饰品已选标记 — 检测TrinketOffer从≥4降到≤3时置位
        private int _prevGoldenCount = 0;

        // 自追踪金币 (HDT的RESOURCES标签在BG不可靠) — 纯计算逻辑抽离至 Core/GoldTracker.cs
        private readonly Engine.GoldTracker _goldTracker = new Engine.GoldTracker();
        private readonly PurchaseRewardTracker _purchaseRewardTracker =
            new PurchaseRewardTracker();
        private readonly UpgradePrizeTracker _upgradePrizeTracker =
            new UpgradePrizeTracker();
        private readonly TurnStartCardGrantTracker _turnStartCardGrantTracker =
            new TurnStartCardGrantTracker();
        private readonly SharedTurnEventTracker _sharedTurnEventTracker =
            new SharedTurnEventTracker();
        private readonly SharedCardVoteTracker _sharedCardVoteTracker =
            new SharedCardVoteTracker();
        private readonly SecondHeroPowerDiscoverTracker _secondHeroPowerDiscoverTracker =
            new SecondHeroPowerDiscoverTracker();
        private readonly TeammateGoldTransferTracker _teammateGoldTransferTracker =
            new TeammateGoldTransferTracker();

        // 英雄技能引擎引用 — 用于正确判断Passive/Active
        private HeroPowerEngine _heroPowerEngine;
        private ITrinketFactSource _trinketFactSource;
        private static readonly ShopItemFactResolver PurchaseFactResolver =
            new ShopItemFactResolver(new HearthDbCardPurchaseFactSource());

        public void SetHeroPowerEngine(HeroPowerEngine engine) { _heroPowerEngine = engine; }
        public void SetAnomalyRegistry(AnomalyRegistry reg) { _anomalyReg = reg; }
        internal void SetTrinketFactSource(ITrinketFactSource source)
        {
            _trinketFactSource = source;
        }
        public bool MarkUpgradePrizeDiscoverClaimed()
        {
            return _upgradePrizeTracker.MarkOldestPendingClaimed();
        }
        public bool ObserveSharedTurnEventOutcome(
            string occurrenceId, string outcomeId, string evidenceSource)
        {
            return _sharedTurnEventTracker.ObserveOutcome(
                occurrenceId, outcomeId, evidenceSource);
        }
        public bool ObserveSharedCardVoteSelection(
            string occurrenceId, string selectedCardId,
            string selectingPlayerId, string evidenceSource)
        {
            return _sharedCardVoteTracker.ObserveSelection(
                occurrenceId, selectedCardId, selectingPlayerId, evidenceSource);
        }
        public bool ObserveSecondHeroPowerChoiceBatch(
            int choiceId, int observedTurn,
            IEnumerable<string> candidateCardIds, string evidenceSource)
        {
            return _secondHeroPowerDiscoverTracker.ObserveBatch(
                choiceId, observedTurn, candidateCardIds, evidenceSource);
        }
        public bool ObserveSecondHeroPowerChoiceSelection(
            int choiceId, string selectedCardId, string evidenceSource)
        {
            return _secondHeroPowerDiscoverTracker.ObserveSelection(
                choiceId, selectedCardId, evidenceSource);
        }
        public bool ObserveTeammateGoldTransfer(
            TeammateGoldTransferRule rule, int turn, string actionCardId,
            int actionEntityId, string evidenceId, string evidenceSource)
        {
            return _teammateGoldTransferTracker.Observe(
                rule, turn, actionCardId, actionEntityId, evidenceId, evidenceSource);
        }
        private AnomalyRegistry _anomalyReg;
        private int _prevTavernTier = 0;
        private HashSet<int> _prevHandIdsForGold = null;   // stale-buy 购买证据: 上帧手牌实体ID
        private HashSet<int> _prevShopIdsForGold = null;   // stale-buy 购买证据: 上帧商店实体ID
        private Dictionary<int, Engine.MinionData> _prevShopCardsForGold = null;
        private string _prevHeroCardId = null;
        // v2 饰品状态机精简: 移除TTL超时/抑制, 依赖Power.log ChoiceList事件(PowerLogWatcher)控制显隐
        // 实体扫描仅填充选项内容(名称/描述/种族)
        private int _lastTrinketOfferCount = 0;
        private bool _trinketPickedThisTurn = false;
        private int _trinketPickedTurn = -1;
        // 发现触发门控: 仅当检测到真实发现事件(三连/技能/战吼)时才扫描zone6
        // 防止塔维什技能等非发现操作将zone6过渡实体误判为发现选项
        public bool DiscoverTriggerActive = false;
        public bool DiscoverZone6Triggered = false;
        private List<Engine.TrinketOption> _cachedDiscoverOptions = null;
        private DateTime _lastDiscoverExtractTime = DateTime.MinValue;

        /// <summary>清除发现缓存: 新一轮发现触发时调用, 防止旧选项混入新发现</summary>
        public void ClearDiscoverCache()
        {
            _cachedDiscoverOptions = null;
            _lastDiscoverExtractTime = DateTime.MinValue;
        }
        /// <summary>上一帧手牌数减少(刚打出了卡牌), 用于法术/战吼发现触发</summary>
        public bool HandDecreasedThisFrame = false;
        public bool Zone6FreshThisFrame = false;
        public int Zone6EntityCountThisFrame = 0;
        public int Zone6NewEntityCountThisFrame = 0;  // 本帧新增(非总数): 发现候选批次特征为一次性+3~4
        public int Zone6NewNonTrinketCountThisFrame = 0;  // 本帧新增中排除MagicItem饰品噪声后的数量
        public int Zone6NewDistinctCandidateCount = 0;    // 本帧新增去重候选cardId数(排除杂项, 07062242: 每候选2实体)
        private List<string> _lastNewZone6CardIds = new List<string>();  // SKIP诊断: 新增实体cardId样本
        private int _zonePosDiagCount = 0;  // ZonePosition诊断计数器
        private int _zone6DiagCount = 0;     // 发现区诊断计数器
        private int _zone6DiagTurn = -1;     // 诊断计数所属回合(07062157: 8次上限在T3耗尽, 出售发现时刻全盲 → 改每回合重置)
        private HashSet<int> _prevZone6EntityIds = new HashSet<int>();
        private DateTime _lastZone6EntityRefresh = DateTime.MinValue;
        // zone6实体首次出现时间: 用于过滤长期滞留的过渡实体(战斗衍生物等)
        private const double Zone6FreshWindowSeconds = 1.5;
        private Dictionary<int, DateTime> _zone6FirstSeen = new Dictionary<int, DateTime>();
        private HashSet<int> _allSeenBoardHandIds = new HashSet<int>(); // 持久化: 所有上过板面/手牌的实体(过滤发现误判)
        private int _trinketDiagCount = 0;   // 饰品提取诊断计数器
        private int _trinketPassDiagCount = 0; // 过渡回合放行诊断计数器(0611 06111357 问题B定位)
        private int _prevOwnedTrinketCount = -1; // 已拥有饰品数(追踪选中)
        private HashSet<string> _lastPickedOfferIds = null; // 上次选中时的offer卡牌ID集合(检测新轮次)
        private bool _timewarpedNewRecruitActive = false; // BG34_Treasure_917: 使用后本局酒馆始终7张牌
        private HashSet<int> _prevTimewarpedNewRecruitHandIds = new HashSet<int>();

        // 战斗伤害校准追踪
        private int _prevHealthForCombat = -1;  // 进入战斗前的血量
        private int _prevArmorForCombat = -1;   // 进入战斗前的护甲
        private int _lastCombatDamage = 0;       // 最近一次战斗受到的伤害
        private int _combatDamageSamples = 0;    // 样本数

        /// <summary>获取最近一次战斗实际伤害 (用于CombatSimulator校准)</summary>
        public int LastCombatDamage => _lastCombatDamage;
        public int CombatDamageSamples => _combatDamageSamples;

        static GameStateExtractor()
        {
            Engine.CardNameService.Initialize();
        }

        private static void PluginLog(string msg)
        {
            try
            {
                var dir = BobCoachDataPaths.Root;
                System.IO.Directory.CreateDirectory(dir);
                var path = System.IO.Path.Combine(dir, "bob_coach.log");
                var line = string.Format("[{0:O}] [Export] {1}\n", DateTime.UtcNow, msg);
                System.IO.File.AppendAllText(path, line, System.Text.Encoding.UTF8);
            }
            catch { }
        }

        private static bool TryResolveShopItem(
            Entity entity, out ResolvedShopItemFact resolved)
        {
            resolved = new ResolvedShopItemFact();
            if (entity == null || string.IsNullOrEmpty(entity.CardId)) return false;

            ShopItemKind? cardTypeKind = null;
            if (entity.HasTag(GameTag.CARDTYPE))
            {
                int cardType = GetTagStaticSafe(entity, GameTag.CARDTYPE);
                if (cardType == (int)CardType.MINION)
                    cardTypeKind = ShopItemKind.Minion;
                else if (cardType == (int)CardType.SPELL
                    || cardType == (int)CardType.BATTLEGROUND_SPELL)
                    cardTypeKind = ShopItemKind.TavernSpell;
            }

            bool isBattlegroundsSpell = false;
            try { isBattlegroundsSpell = entity.IsBattlegroundsSpell; }
            catch { }

            ShopItemObservation observation;
            if (!ShopItemFactResolver.TryCreateObservation(
                entity.CardId,
                cardTypeKind,
                isBattlegroundsSpell,
                entity.HasTag(GameTag.COST),
                GetTagStaticSafe(entity, GameTag.COST),
                out observation))
                return false;
            return PurchaseFactResolver.TryResolve(observation, out resolved);
        }

        private static int GetTagStaticSafe(Entity entity, GameTag tag)
        {
            try { return entity.GetTag(tag); }
            catch { return 0; }
        }

        // ── 公共入口 ──

        private static int _extractFailCount = 0;

        /// <summary>
        /// 每局开始时重置累积状态（幽灵实体过滤器等）。
        /// </summary>
        public void Reset()
        {
            _extractedShopIds.Clear();
            _opponentIds.Clear();
            _lastExtractTurn = -1;
            _localPlayerCtrl = -1;
            _extractFailCount = 0;
            _shopDiagCount = 0;
            _cachedShopMinions = null;
            _cachedShopTurn = -1;
            _prevHandCount = -1;
            _prevGoldenCount = 0;
            _goldTracker.Reset();
            _purchaseRewardTracker.Reset();
            _upgradePrizeTracker.Reset();
            _turnStartCardGrantTracker.Reset();
            _sharedTurnEventTracker.Reset();
            _sharedCardVoteTracker.Reset();
            _secondHeroPowerDiscoverTracker.Reset();
            _teammateGoldTransferTracker.Reset();
            _prevTavernTier = 0;
            _prevHandIdsForGold = null;
            _prevShopIdsForGold = null;
            _prevShopCardsForGold = null;
            _cachedDiscoverOptions = null; _lastDiscoverExtractTime = DateTime.MinValue;
            _zone6FirstSeen.Clear();
            _lastTrinketOfferCount = 0;
            _trinketPickedThisTurn = false;
            _prevOwnedTrinketCount = -1;
            _lastPickedOfferIds = null;
            _timewarpedNewRecruitActive = false;
            _prevTimewarpedNewRecruitHandIds = new HashSet<int>();
            _trinketPickedTurn = -1;
            _completedTrinketIds = null;
            _trinketRoundResolvedTurn = -1;
            _equippedTrinketIds = new HashSet<string>();
            _trinketLesserTurn = 6;
            _trinketGreaterTurn = 9;
            _trinketOfferFirstTurn = -1;
            _lastTrinketOfferTurn = -1;
            _zonePosDiagCount = 0;
            _zone6DiagCount = 0;
            _trinketDiagCount = 0;
            _trinketPassDiagCount = 0;
            _prevZone6EntityIds = new HashSet<int>();
            _lastZone6EntityRefresh = DateTime.MinValue;
            _zone6FirstSeen = new Dictionary<int, DateTime>();
            _allSeenBoardHandIds = new HashSet<int>();
            HandDecreasedThisFrame = false;
            Zone6FreshThisFrame = false;
            Zone6EntityCountThisFrame = 0;
            Zone6NewEntityCountThisFrame = 0;
            Zone6NewNonTrinketCountThisFrame = 0;
            Zone6NewDistinctCandidateCount = 0;
        }

        /// <summary>
        /// 战斗阶段调用：收集对手随从entity ID供后续商店提取排除幽灵实体。
        /// </summary>
        public void CollectOpponentIds()
        {
            try
            {
                var game = Core.Game;
                if (game == null || game.Entities == null) return;
                _opponentIds.Clear();
                foreach (var e in game.Entities.Values.ToList())
                {
                    if (e == null || string.IsNullOrEmpty(e.CardId)) continue;
                    if (!IsBaconCard(e.CardId)) continue;
                    if (IsHeroOrPower(e)) continue;
                    int ctrl;
                    try { ctrl = e.GetTag(GameTag.CONTROLLER); } catch { continue; }
                    if (ctrl <= 1) continue;
                    _opponentIds.Add(e.Id);
                }
            }
            catch { }
        }

        public Engine.GameState Extract()
        {
            HandDecreasedThisFrame = false;
            Zone6FreshThisFrame = false;
            Zone6EntityCountThisFrame = 0;
            Zone6NewEntityCountThisFrame = 0;
            Zone6NewNonTrinketCountThisFrame = 0;
            Zone6NewDistinctCandidateCount = 0;
            try
            {
                var game = Core.Game;
                if (game == null || game.Entities == null)
                {
                    if (_extractFailCount++ < 3)
                        ExtractorLog("Extract null: game=" + (game == null ? "null" : "ok") + " entities=" + (game != null && game.Entities == null ? "null" : "?"));
                    return null;
                }

                if (!game.IsBattlegroundsMatch)
                {
                    if (_extractFailCount++ < 3)
                        ExtractorLog("Extract null: not BG match. CurrentGameType=" + game.CurrentGameType);
                    return null;
                }

                var entities = game.Entities.Values.ToList();
                if (entities.Count == 0)
                {
                    if (_extractFailCount++ < 3)
                        ExtractorLog("Extract null: entities.Count == 0");
                    return null;
                }

                var playerHero = FindPlayerHero(entities);
                if (_localPlayerCtrl <= 0 && playerHero != null)
                    _localPlayerCtrl = GetTag(playerHero, GameTag.CONTROLLER);
                if (playerHero == null)
                {
                    if (_extractFailCount++ < 3)
                    {
                        var heroLikeEntities = new List<string>();
                        foreach (var e in entities)
                        {
                            if (e == null || string.IsNullOrEmpty(e.CardId)) continue;
                            if (e.CardId.Contains("HERO") || e.CardId.StartsWith("TB_BaconShop"))
                            {
                                var ctrl = GetTag(e, GameTag.CONTROLLER);
                                heroLikeEntities.Add(string.Format("{0}(ctrl={1},isHero={2},inPlay={3})",
                                    e.CardId, ctrl, e.IsHero, e.IsInPlay));
                            }
                        }
                        ExtractorLog(string.Format("Extract null: FindPlayerHero failed. Entities count={0}. Hero-like: [{1}]",
                            entities.Count, string.Join(", ", heroLikeEntities)));
                    }
                    return null;
                }

                _extractFailCount = 0;

                var state = new Engine.GameState();

                int turn = game.GetTurnNumber();
                if (turn < 1 || turn > 30) turn = 1;

                state.Turn = turn;
                state.TavernTier = GetPlayerTechLevel(entities);
                state.Health = GetPlayerHealth(playerHero);
                state.MaxHealth = state.Health > 30 ? state.Health : 30;
                state.Armor = GetTag(playerHero, GameTag.ARMOR);

                // 战斗伤害校准: 商店阶段检测血量变化→记录实际战斗伤害
                bool isCombat = game.IsBattlegroundsCombatPhase;
                if (!isCombat && _prevHealthForCombat >= 0)
                {
                    int prevTotal = _prevHealthForCombat + Math.Max(0, _prevArmorForCombat);
                    int curTotal = state.Health + Math.Max(0, state.Armor);
                    if (prevTotal > curTotal)
                    {
                        _lastCombatDamage = prevTotal - curTotal;
                        _combatDamageSamples++;
                    }
                }
                if (isCombat)
                {
                    _prevHealthForCombat = state.Health;
                    _prevArmorForCombat = state.Armor;
                }
                state.HeroCardId = playerHero.CardId ?? "";

                // 畸变检测: 保留主畸变与动态全局效果，不能按实体顺序只取第一个。
                var observedAnomalyIds = entities
                    .Where(e => e != null && !string.IsNullOrEmpty(e.CardId)
                        && e.CardId.Contains("Anomaly"))
                    .Select(e => e.CardId)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                bool isDuos = game.IsBattlegroundsDuosMatch;
                state.IsDuos = isDuos;
                state.AnomalyContext = AnomalyContext.Resolve(observedAnomalyIds, _anomalyReg, isDuos);
                state.AnomalyId = state.AnomalyContext.PrimaryAnomalyId;
                state.EffectiveRules = AnomalyRuleResolver.Resolve(state.AnomalyContext, _anomalyReg);
                state.TavernUpgradeCost = GetTavernUpgradeCost(entities);
                state.HeroName = GetCardName(playerHero.CardId);
                var hpEntities = SelectHeroPowerEntities(entities, state.HeroCardId);
                var hpEntity = SelectPrimaryHeroPowerEntity(
                    hpEntities, state.HeroCardId, state.EffectiveRules);
                var hpObservations = new List<HeroPowerObservation>();
                foreach (var entity in hpEntities)
                {
                    HeroStrategy strategy = null;
                    if (_heroPowerEngine != null)
                    {
                        strategy = entity == hpEntity
                            ? _heroPowerEngine.GetStrategyForPower(state.HeroCardId, entity.CardId)
                            : _heroPowerEngine.GetStrategy(entity.CardId);
                    }
                    hpObservations.Add(new HeroPowerObservation
                    {
                        CardId = entity.CardId,
                        EntityId = entity.Id,
                        Cost = GetTag(entity, GameTag.COST),
                        Exhausted = entity.HasTag(GameTag.EXHAUSTED),
                        IsActive = strategy != null && (strategy.PowerType == HeroPowerType.Active
                            || strategy.PowerType == HeroPowerType.Conditional),
                        HasDiscover = (strategy != null && strategy.HasDiscover)
                            || HeroPowerTextHasDiscover(entity),
                        UnlockTurn = strategy != null && strategy.UnlockTurn > 0
                            ? strategy.UnlockTurn : 1,
                        UnlockTier = strategy != null && strategy.UnlockTier > 0
                            ? strategy.UnlockTier : 1,
                        SpecialRule = strategy != null ? (strategy.SpecialRule ?? "") : "",
                    });
                }
                state.HeroPowers = HeroPowerStateResolver.Resolve(
                    hpObservations, hpEntity != null ? hpEntity.CardId : "",
                    state.EffectiveRules, turn, state.TavernTier).ToList();
                var primaryPower = state.HeroPowers.FirstOrDefault(power => power.IsPrimary);
                state.HeroPowerCost = primaryPower != null ? primaryPower.Cost : 1;
                state.HeroPowerCardId = primaryPower != null ? primaryPower.CardId : "";
                state.HeroPowerType = primaryPower != null && primaryPower.IsActive
                    ? "Active" : "Passive";
                state.HeroPowerExhausted = primaryPower != null && primaryPower.Exhausted;
                state.HeroPowerUnlockTurn = primaryPower != null ? primaryPower.UnlockTurn : 1;
                state.HeroPowerUnlockTier = primaryPower != null ? primaryPower.UnlockTier : 1;
                state.HeroPowerSpecial = primaryPower != null ? primaryPower.SpecialRule : "";
                state.HeroPowerHasDiscover = primaryPower != null && primaryPower.HasDiscover;
                state.HasSecondHeroPower = state.HeroPowers.Any(power => power.IsSecondary);
                state.ExhaustedHeroPowerCount = state.HeroPowers.Count(power => power.Exhausted);
                _secondHeroPowerDiscoverTracker.Advance(
                    state.EffectiveRules.SecondHeroPowerDiscover, state.HeroPowers);
                state.SecondHeroPowerChoiceExpectations =
                    _secondHeroPowerDiscoverTracker.Expectations.ToList();
                state.ObservedSecondHeroPowerChoiceBatches =
                    _secondHeroPowerDiscoverTracker.ObservedBatches.ToList();
                state.ObservedSecondHeroPowerChoiceSelections =
                    _secondHeroPowerDiscoverTracker.Selections.ToList();
                state.ObservedSecondHeroPowerEntities =
                    _secondHeroPowerDiscoverTracker.EntityObservations.ToList();
                state.ObservedTeammateGoldTransfers =
                    _teammateGoldTransferTracker.Observations.ToList();
                state.GameActive = true;
                // 战棋满币: T1=3, T2=4, ... T8起=10。原 3+Max(0,turn-2) 从T2起每回合少算1金
                // (T2算3应为4), 是 gold 提前归零、铸币0仍推荐的根因(0611 06111155 诊断)。
                state.MaxGold = Math.Min(10, 2 + turn);
                state.Phase = game.IsBattlegroundsCombatPhase ? "combat" : "shop";
                _upgradePrizeTracker.Advance(
                    state.Turn, state.TavernTier, state.Phase == "shop",
                    state.EffectiveRules.UpgradePrize);
                state.TavernUpgradeOccurrences =
                    _upgradePrizeTracker.ObservedThisFrame.ToList();
                state.PendingPrizeDiscoverExpectations =
                    _upgradePrizeTracker.Pending.ToList();
                state.ClaimedPrizeDiscoverOccurrences =
                    new HashSet<string>(_upgradePrizeTracker.ClaimedOccurrences);
                _sharedTurnEventTracker.Advance(
                    state.Turn, state.EffectiveRules.SharedYoggWheel);
                state.SharedTurnEventOccurrences =
                    _sharedTurnEventTracker.ObservedThisFrame.ToList();
                state.PendingSharedTurnEvents =
                    _sharedTurnEventTracker.Pending.ToList();
                state.ObservedSharedTurnEventOutcomes =
                    _sharedTurnEventTracker.Outcomes.ToList();
                state.PlayerId = _localPlayerCtrl > 0 ? _localPlayerCtrl : 1;

                state.BoardMinions = ExtractBoardMinions(entities);
                state.HandMinions = ExtractHandMinions(entities);
                _sharedCardVoteTracker.Advance(
                    state.Turn, state.EffectiveRules.SharedCardVote,
                    state.HandMinions, state.Phase == "combat");
                state.SharedCardVoteOccurrences =
                    _sharedCardVoteTracker.ObservedOccurrencesThisFrame.ToList();
                state.PendingSharedCardVoteSelections =
                    _sharedCardVoteTracker.PendingSelections.ToList();
                state.ObservedSharedCardVoteSelections =
                    _sharedCardVoteTracker.Selections.ToList();
                state.SharedCardGrantExpectations =
                    _sharedCardVoteTracker.GrantExpectations.ToList();
                state.ObservedLocalSharedCardGrants =
                    _sharedCardVoteTracker.LocalGrantObservations.ToList();
                _turnStartCardGrantTracker.Advance(
                    state.Turn, state.EffectiveRules.PortalInBottleAtTurnStart,
                    state.HandMinions);
                state.TurnStartCardGrantOccurrences =
                    _turnStartCardGrantTracker.ObservedThisFrame.ToList();
                state.PendingTurnStartCardGrantExpectations =
                    _turnStartCardGrantTracker.Pending.ToList();
                state.ClaimedTurnStartCardGrantOccurrences =
                    new HashSet<string>(_turnStartCardGrantTracker.ClaimedOccurrences);
                StartResourceExpectationEvaluator.RecordObservedState(
                    state, state.EffectiveRules.StartResourceExpectations);
                // 先提取对手信息收集幽灵ID，再提取商店时排除
                state.Opponents = ExtractOpponents(entities);
                state.HeroIdentityExpectations = HeroRosterOverrideEvaluator.Evaluate(
                    state.EffectiveRules.AllHeroesOverride,
                    state.PlayerId,
                    state.HeroCardId,
                    state.Opponents).ToList();
                state.ShopMinions = ExtractShopMinions(entities, turn);

                // 金币: 必须在 ShopMinions/BoardMinions 赋值之后计算 —— TrackGold 的购买/升本/售出
                // 检测依赖 state.ShopMinions.Count / state.BoardMinions.Count。此前 TrackGold 在它们
                // 赋值前调用, 读到的恒为空 List → 购买检测从不触发 → 升本/买怪不扣费、gold 卡死
                // (0611 06110931 真因, codereview P0)。战棋 HDT RESOURCES 滞后, 自追踪由操作驱动更准。
                int trackedGold = TrackGold(state, entities);
                int hdtGold = ReadGold();
                // 畸变可能使 maxGold 超 10, TrackGold 内已按畸变算上限, 此处放宽到 20 校验
                int goldCap = System.Math.Max(state.MaxGold, 20);
                if (trackedGold >= 0 && trackedGold <= goldCap)
                    state.Gold = trackedGold;
                else if (hdtGold >= 0)
                    state.Gold = hdtGold;
                else
                    state.Gold = Math.Max(0, trackedGold);
                // 兜底只处理追踪器尚未初始化的异常值。不能把真实 0 金改回 3 金，
                // 否则 T2 刚升本后会继续显示买牌推荐。
                if (trackedGold < 0 && state.Gold <= 0 && state.ShopMinions.Count > 0 && turn <= 2)
                    state.Gold = state.MaxGold;
                // 手牌减少检测: 记录当前帧手牌数，供发现提取判断"玩家是否刚打出了一张手牌"
                int currentHandCount = state.HandMinions.Count;
                bool handDecreased = _prevHandCount >= 0 && currentHandCount < _prevHandCount;
                HandDecreasedThisFrame = handDecreased;
                _prevHandCount = currentHandCount;
                state.FrozenShop = IsFrozenShop(entities);
                state.FreeRefreshCount = CountFreeRefresh(entities);
                state.HeroOptions = ExtractHeroOptions(entities, turn);
                state.TrinketOffer = ExtractTrinketOffer(entities, turn);
                state.ActiveTrinkets = _equippedTrinketIds.ToList();
                UpdateTimewarpedNewRecruitState(entities, state);
                state.ReplenishingShopActive = HasReplenishingShopEffect(state);
                // 构建已知实体ID集合供发现提取排除
                var knownIds = new HashSet<int>();
                foreach (var bm in state.BoardMinions) { knownIds.Add(bm.EntityId); _allSeenBoardHandIds.Add(bm.EntityId); }
                foreach (var hm in state.HandMinions) { knownIds.Add(hm.EntityId); _allSeenBoardHandIds.Add(hm.EntityId); }
                foreach (var sm in state.ShopMinions) knownIds.Add(sm.EntityId);
                // 合并持久化已见实体: 曾经上过板面/手牌的实体不可能是新发现选项
                foreach (var id in _allSeenBoardHandIds) knownIds.Add(id);
                // 收集板面+手牌+商店CardId用于发现选项去重(商店卡牌在zone6过渡时会被误检)
                var knownCardIds = new HashSet<string>();
                var knownNames = new HashSet<string>();
                foreach (var bm in state.BoardMinions) { if (!string.IsNullOrEmpty(bm.CardId)) knownCardIds.Add(bm.CardId); if (!string.IsNullOrEmpty(bm.CardName)) knownNames.Add(bm.CardName); }
                foreach (var hm in state.HandMinions) { if (!string.IsNullOrEmpty(hm.CardId)) knownCardIds.Add(hm.CardId); if (!string.IsNullOrEmpty(hm.CardName)) knownNames.Add(hm.CardName); }
                foreach (var sm in state.ShopMinions) { if (!string.IsNullOrEmpty(sm.CardId)) knownCardIds.Add(sm.CardId); if (!string.IsNullOrEmpty(sm.CardName)) knownNames.Add(sm.CardName); }
                // 三连检测: 金色随从数量增加 → 三连奖励发现触发
                int curGolden = 0;
                foreach (var bm in state.BoardMinions) if (bm.Golden) curGolden++;
                bool tripleTriggered = curGolden > _prevGoldenCount;
                _prevGoldenCount = curGolden;
                // 扫描zone 6实体: T3+才有真正发现(三连/技能/畸变), T1-T2全部是实体过渡
                // CARDTYPE: 4=MINION 5=SPELL 6=ENCHANTMENT 7=HERO 10=HERO_POWER
                // v2: 门控由DiscoverTriggerActive(事件驱动, EvaluateAndRender设置)控制
                // 实体新鲜度: 追踪zone6实体ID变化, 排除长期滞留的过期实体
                var zone6Entities = entities.Where(e => { int z = GetTag(e, GameTag.ZONE); return z == 6; }).ToList();
                var currentZone6Ids = new HashSet<int>(zone6Entities.Select(e => e.Id));
                bool zone6Changed = !currentZone6Ids.SetEquals(_prevZone6EntityIds);
                // 本帧zone6新增实体数(区别于总数): 发现面板典型为一次性新增3-4个候选
                var newZone6Entities = zone6Entities.Where(e => !_prevZone6EntityIds.Contains(e.Id)).ToList();
                Zone6NewEntityCountThisFrame = newZone6Entities.Count;
                // 非饰品新增数: 饰品池实体(MagicItem)每回合大量进出zone6, 是发现批次检测的主要噪声源
                // (07062107: T3 一次性+11个饰品池实体把3~4窗口挡住)
                Zone6NewNonTrinketCountThisFrame = newZone6Entities.Count(e =>
                    !string.IsNullOrEmpty(e.CardId) && !e.CardId.Contains("MagicItem"));
                // 发现候选批次特征(07062242实测): 3候选×2实体=6个新增, 按实体数3~4判定不命中 →
                // 改按"去重后的候选cardId数": 排除空cardId/按钮/英雄/畸变/饰品槽位等杂项实体。
                Zone6NewDistinctCandidateCount = newZone6Entities
                    .Where(e => !string.IsNullOrEmpty(e.CardId)
                        && !e.CardId.Contains("MagicItem")
                        && !e.CardId.Contains("_Button")
                        && !e.CardId.Contains("HERO")
                        && !e.CardId.Contains("Anomaly")
                        && !e.CardId.Contains("Trinket"))
                    .Select(e => e.CardId)
                    .Distinct()
                    .Count();
                _lastNewZone6CardIds = newZone6Entities
                    .Select(e => e.CardId ?? "?").Take(8).ToList();
                if (zone6Changed)
                {
                    _lastZone6EntityRefresh = DateTime.UtcNow;
                    // zone6实体变化=新一轮发现 → 清除旧缓存防止轮次混淆
                    _cachedDiscoverOptions = null;
                }
                // 更新zone6实体首次出现时间记录
                foreach (var id in currentZone6Ids)
                {
                    if (!_zone6FirstSeen.ContainsKey(id))
                        _zone6FirstSeen[id] = DateTime.UtcNow;
                }
                // 清理已不在zone6的记录(防字典膨胀)
                var goneZone6 = _zone6FirstSeen.Keys.Where(id => !currentZone6Ids.Contains(id)).ToList();
                foreach (var id in goneZone6) _zone6FirstSeen.Remove(id);
                _prevZone6EntityIds = currentZone6Ids;
                // 发现触发必须由 BobCoachPlugin/Power.log 的显式事件打开。
                // zone6 只提供候选实体, 不能自触发面板, 否则战斗残影/饰品选择会误弹发现。
                bool zone6Fresh = (DateTime.UtcNow - _lastZone6EntityRefresh).TotalSeconds < Zone6FreshWindowSeconds;
                bool zone6Active = zone6Fresh && currentZone6Ids.Count > 0;
                Zone6FreshThisFrame = zone6Fresh;
                Zone6EntityCountThisFrame = currentZone6Ids.Count;
                bool shouldExtract = DiscoverTriggerActive;
                state.DiscoverGatePassed=shouldExtract;
                // 诊断(问题7: 重拾灵魂英雄技能发现没触发): zone6有实体却没提取→打印门控因子
                if (_zone6DiagTurn != turn) { _zone6DiagTurn = turn; _zone6DiagCount = 0; }
                if (turn >= 3 && currentZone6Ids.Count > 0 && !shouldExtract && _zone6DiagCount < 8)
                {
                    ExtractorLog(string.Format("DIAG DiscoverGate SKIP: zone6={0} fresh={1} trigger={2} new={3} newNonTrinket={4} newIds=[{5}] → 未提取(英雄技能发现可能漏触发)",
                        currentZone6Ids.Count, zone6Fresh, DiscoverTriggerActive,
                        Zone6NewEntityCountThisFrame, Zone6NewNonTrinketCountThisFrame,
                        string.Join(",", _lastNewZone6CardIds)));
                    _zone6DiagCount++;
                }
                var extracted = (turn >= 3 && shouldExtract)
                    ? ExtractDiscoverOptionsStrict(entities, knownIds, knownCardIds, knownNames)
                    : null;
                // 发现缓存: 仅在检测窗口内保留，超时后清除防跨轮次污染
                if (extracted != null && extracted.Count >= 2)
                    _cachedDiscoverOptions = extracted;
                else if (extracted == null && _cachedDiscoverOptions != null
                    && (DateTime.UtcNow - _lastDiscoverExtractTime).TotalSeconds < 1.5)
                    extracted = _cachedDiscoverOptions;  // 缓存未过期→继续显示
                else
                    _cachedDiscoverOptions = null;
                if (extracted != null && extracted.Count >= 2)
                    _lastDiscoverExtractTime = DateTime.UtcNow;
                state.DiscoverOptions = extracted ?? new List<Engine.TrinketOption>();

                // 动态检测可用种族 (从商店+板面+对手板面收集)
                DetectAvailableTribes(state);

                return state;
            }
            catch (Exception ex)
            {
                if (_extractFailCount++ < 3)
                    ExtractorLog("Extract exception: " + ex);
                return null;
            }
        }

        private static void ExtractorLog(string msg)
        {
            try
            {
                var bobDir = BobCoachDataPaths.Root;
                System.IO.Directory.CreateDirectory(bobDir);
                var logPath = System.IO.Path.Combine(bobDir, "bob_coach.log");
                var line = string.Format("[{0:O}] [Extractor] {1}\n", DateTime.UtcNow, msg);
                System.IO.File.AppendAllText(logPath, line, System.Text.Encoding.UTF8);
            }
            catch { }
        }

        // ── 实体查找 ──

        private Entity FindPlayerHero(List<Entity> entities)
        {
            var knownLocalHero = FindKnownLocalHero(entities);
            if (knownLocalHero != null) return knownLocalHero;
            if (_localPlayerCtrl > 0) return null;

            var localPlayerCtrl = -1;
            try
            {
                var playerEntity = Core.Game != null ? Core.Game.PlayerEntity : null;
                if (playerEntity != null)
                    localPlayerCtrl = GetTag(playerEntity, GameTag.CONTROLLER);
            }
            catch { }
            foreach (var e in entities)
            {
                if (localPlayerCtrl > 0) break;
                if (!IsBattlegroundsHeroEntity(e)) continue;
                if (!e.IsInPlay) continue;
                localPlayerCtrl = GetTag(e, GameTag.CONTROLLER);
                if (localPlayerCtrl > 0) break;
            }

            if (localPlayerCtrl <= 0)
            {
                foreach (var e in entities)
                {
                    if (!IsBattlegroundsHeroEntity(e)) continue;
                    localPlayerCtrl = GetTag(e, GameTag.CONTROLLER);
                    if (localPlayerCtrl > 0) break;
                }
            }

            if (localPlayerCtrl <= 0) return null;

            Entity best = null;
            foreach (var e in entities)
            {
                if (!IsBattlegroundsHeroEntity(e)) continue;
                var ctrl = GetTag(e, GameTag.CONTROLLER);
                if (ctrl == localPlayerCtrl)
                {
                    if (e.IsInPlay) return e;
                    if (best == null) best = e;
                }
            }
            return best;
        }

        private List<Entity> SelectHeroPowerEntities(List<Entity> entities, string heroCardId)
        {
            if (entities == null) return new List<Entity>();
            var candidates = new List<Entity>();
            foreach (var e in entities)
            {
                if (!IsHeroPowerEntity(e, heroCardId)) continue;
                int ctrl = GetTag(e, GameTag.CONTROLLER);
                if (_localPlayerCtrl > 0 && ctrl != _localPlayerCtrl) continue;
                candidates.Add(e);
            }
            return candidates.OrderBy(e => e.Id).ToList();
        }

        private Entity SelectPrimaryHeroPowerEntity(
            List<Entity> candidates, string heroCardId, EffectiveGameRules rules)
        {
            if (candidates == null || candidates.Count == 0) return null;
            var fixedSecondaryIds = new HashSet<string>(
                (rules ?? EffectiveGameRules.Default).SecondaryHeroPowers
                    .Select(rule => rule.CardId), StringComparer.Ordinal);
            Entity best = null;
            int bestScore = int.MinValue;
            foreach (var e in candidates)
            {
                if (fixedSecondaryIds.Contains(e.CardId)) continue;
                int score = 0;
                int ctrl = GetTag(e, GameTag.CONTROLLER);
                if (_localPlayerCtrl > 0 && ctrl == _localPlayerCtrl) score += 1000;
                if (!string.IsNullOrEmpty(heroCardId) && !string.IsNullOrEmpty(e.CardId)
                    && e.CardId == heroCardId + "p") score += 10000;
                else if (!string.IsNullOrEmpty(heroCardId) && !string.IsNullOrEmpty(e.CardId)
                    && e.CardId.Contains(heroCardId)) score += 5000;
                else if (!string.IsNullOrEmpty(heroCardId) && !string.IsNullOrEmpty(e.CardId)
                    && e.CardId.EndsWith("p")
                    && e.CardId.StartsWith(heroCardId.Substring(0, Math.Min(10, heroCardId.Length))))
                    score += 2000;

                if (score > bestScore || (score == bestScore && best != null && e.Id < best.Id))
                {
                    bestScore = score;
                    best = e;
                }
            }
            return best;
        }

        private Entity FindKnownLocalHero(List<Entity> entities)
        {
            if (_localPlayerCtrl <= 0 || entities == null) return null;
            Entity fallback = null;
            foreach (var entity in entities)
            {
                if (!IsBattlegroundsHeroEntity(entity)
                    || GetTag(entity, GameTag.CONTROLLER) != _localPlayerCtrl)
                    continue;
                if (entity.IsInPlay) return entity;
                if (fallback == null) fallback = entity;
            }
            return fallback;
        }

        private bool IsHeroPowerEntity(Entity e, string heroCardId)
        {
            if (e == null || string.IsNullOrEmpty(e.CardId)) return false;
            if (e.GetTag(GameTag.CARDTYPE) == 10) return true; // CARDTYPE_HERO_POWER
            if (e.CardId.Contains("_HP_") || e.CardId.Contains("HeroPower") || e.CardId.Contains("BACON_HERO_POWER")) return true;
            if (!string.IsNullOrEmpty(heroCardId)
                && e.CardId.EndsWith("p")
                && e.CardId.StartsWith(heroCardId.Substring(0, Math.Min(10, heroCardId.Length))))
                return true;
            return false;
        }

        private bool HeroPowerTextHasDiscover(Entity e)
        {
            if (e == null || string.IsNullOrEmpty(e.CardId)) return false;
            try
            {
                HearthDb.Card c;
                if (HearthDb.Cards.All.TryGetValue(e.CardId, out c) && c != null)
                {
                    string text = c.Text ?? "";
                    if (text.Contains("Discover") || text.Contains("发现")) return true;
                }
            }
            catch { }
            string name = GetCardName(e.CardId) ?? "";
            return name.Contains("发现") || name.Contains("Discover");
        }

        private int GetPlayerTechLevel(List<Entity> entities)
        {
            foreach (var e in entities)
            {
                if (!(GetTag(e, GameTag.CONTROLLER) == _localPlayerCtrl)) continue;
                var tl = GetTag(e, GameTag.TECH_LEVEL);
                if (tl >= 1 && tl <= 7) return tl;
                tl = GetTag(e, GameTag.PLAYER_TECH_LEVEL);
                if (tl >= 1 && tl <= 7) return tl;
            }
            return 1;
        }

        private int GetTavernUpgradeCost(List<Entity> entities)
        {
            foreach (var e in entities)
            {
                if (e == null || string.IsNullOrEmpty(e.CardId)) continue;
                if (!e.CardId.StartsWith("TB_BaconShopTechUp", StringComparison.Ordinal)) continue;
                if (GetTag(e, GameTag.CONTROLLER) != _localPlayerCtrl || !e.IsInPlay) continue;
                if (!e.HasTag(GameTag.COST)) continue;
                int cost = GetTag(e, GameTag.COST);
                if (cost >= 0 && cost <= 20) return cost;
            }
            return -1;
        }

        private int GetPlayerHealth(Entity hero)
        {
            var health = GetTag(hero, GameTag.HEALTH);
            var damage = GetTag(hero, GameTag.DAMAGE);
            if (health <= 0) health = 30;
            if (damage > 0 && health > 30) health -= damage;
            return Math.Max(0, health);
        }

        /// <summary>直接从HDT PlayerEntity读取金币, 每帧调用, 不做自追踪</summary>
        private static int ReadGold()
        {
            try
            {
                var pe = Core.Game.PlayerEntity;
                if (pe != null)
                {
                    int r = pe.GetTag(GameTag.RESOURCES);
                    if (r >= 0 && r <= 20) return r;
                }
            }
            catch { }
            return 3; // 兜底: T1默认3金币
        }

        /// <summary>
        /// 自追踪金币: 薄封装 — 提取畸变参数 + 读 HDT 标签, 纯计算委托 Core/GoldTracker。
        /// 不依赖HDT的RESOURCES标签做逐帧追踪(在BG中不实时更新), 仅回合切换时作基准校准。
        /// </summary>
        private int TrackGold(Engine.GameState state, List<Entity> entities)
        {
            int turn = state.Turn;
            var effectiveRules = state.EffectiveRules ?? EffectiveGameRules.Default;
            state.FirstPurchaseUsedThisTurn = _goldTracker.IsFirstPurchaseUsed(turn);
            state.FirstMinionPurchaseUsedThisTurn =
                _goldTracker.IsFirstMinionPurchaseUsed(turn);

            // 畸变加成
            int bonusGold = 0, goldPerTurn = 0, minionCost = 3, freeRefresh = 0, freeCards = 0;
            bool firstBuyFree = false;
            if (effectiveRules.MinionPurchaseCostOverride.HasValue)
                minionCost = effectiveRules.MinionPurchaseCostOverride.Value;
            if (effectiveRules.FirstMinionPurchaseCost == 0)
                firstBuyFree = true;

            int curShop = state.ShopMinions.Count;
            var curShopIds = state.ShopMinions.Select(m => m.EntityId).ToList();
            int curBoard = state.BoardMinions.Count;
            int curHand = state.HandMinions.Count;

            // stale-buy 购买证据: 本帧新增手牌实体中来自上帧商店实体集合的数量。
            // 饰品给牌/发现拿牌也会"手牌增加+商店不变", 但实体ID不来自商店 → 不算购买(07062107)。
            var curHandIds = state.HandMinions.Select(m => m.EntityId).ToList();
            var newlyObservedHandCards = _prevHandIdsForGold == null
                ? new List<Engine.MinionData>()
                : state.HandMinions.Where(card => card != null && card.EntityId > 0
                    && !_prevHandIdsForGold.Contains(card.EntityId)).ToList();
            int handGainedFromShop = 0;
            int purchasedMinionCount = 0;
            var observedPurchaseCosts = new List<int>();
            var observedPurchases = new List<ObservedPurchase>();
            if (_prevHandIdsForGold != null && _prevShopIdsForGold != null)
            {
                foreach (var handCard in state.HandMinions)
                {
                    int hid = handCard.EntityId;
                    if (hid <= 0 || _prevHandIdsForGold.Contains(hid) || !_prevShopIdsForGold.Contains(hid))
                        continue;
                    handGainedFromShop++;
                    Engine.MinionData previousShopCard;
                    if (_prevShopCardsForGold != null
                        && _prevShopCardsForGold.TryGetValue(hid, out previousShopCard))
                    {
                        int resolvedCost = GameRuleEvaluator.GetPurchaseCost(
                            state, previousShopCard, state.HeroCardId, effectiveRules);
                        int observedCost = resolvedCost == int.MaxValue ? -1 : resolvedCost;
                        observedPurchaseCosts.Add(observedCost);
                        observedPurchases.Add(new ObservedPurchase(
                            turn, hid, previousShopCard.CardId,
                            previousShopCard.IsSpell,
                            observedCost, "tavern_shop", true, previousShopCard.Golden));
                        if (!previousShopCard.IsSpell && previousShopCard.Tier > 0)
                        {
                            purchasedMinionCount++;
                            state.FirstMinionPurchaseUsedThisTurn = true;
                        }
                    }
                }
            }
            _prevHandIdsForGold = new HashSet<int>(curHandIds);
            _prevShopIdsForGold = new HashSet<int>(curShopIds);
            _prevShopCardsForGold = state.ShopMinions
                .Where(card => card != null && card.EntityId > 0)
                .GroupBy(card => card.EntityId)
                .ToDictionary(group => group.Key, group => group.First());

            // HDT RESOURCES 标签 (-1=不可用), 仅供 GoldTracker 回合切换时校准基准
            int hdtTag = -1;
            try { hdtTag = Core.Game.PlayerEntity?.GetTag(GameTag.RESOURCES) ?? -1; } catch { }

            int tracked = _goldTracker.Advance(
                turn, curShop, curShopIds, curBoard, curHand, state.TavernTier,
                bonusGold, goldPerTurn, minionCost, freeRefresh, freeCards, firstBuyFree,
                hdtTag, handGainedFromShop, observedPurchaseCosts, purchasedMinionCount,
                effectiveRules, state.TavernUpgradeCost, observedPurchases);
            state.FirstPurchaseUsedThisTurn = _goldTracker.IsFirstPurchaseUsed(turn);
            state.FirstMinionPurchaseUsedThisTurn =
                _goldTracker.IsFirstMinionPurchaseUsed(turn);
            _purchaseRewardTracker.Advance(
                turn, effectiveRules.FirstPurchaseExtraCopy,
                observedPurchases, newlyObservedHandCards);
            state.PendingPurchaseRewardExpectations =
                _purchaseRewardTracker.Pending.ToList();
            state.ClaimedPurchaseRewardOccurrences =
                new HashSet<string>(_purchaseRewardTracker.ClaimedOccurrences);

            // 诊断: 与HDT标签对比。注意 HDT RESOURCES 在招募阶段内保持回合起始满金不减,
            // self < HDT 是"花过钱"的正常态; self > HDT 才是异常(自跟踪多算了收入)。
            if (hdtTag >= 0 && Math.Abs(hdtTag - tracked) > 1)
                PluginLog(string.Format("GoldTrack: self={0} HDT={1} diff={2} shop={3} board={4} tier={5}",
                    tracked, hdtTag, hdtTag - tracked, curShop, curBoard, state.TavernTier));

            // 升本扣费已由 GoldTracker.Advance 内部处理(检测 tier 增长), 此处不再二次扣除。
            // (07061713 复盘: 此处曾重复扣费, 与 Advance 内部扣费叠加导致金币系统性下漂到 0)
            _prevTavernTier=state.TavernTier;
            return tracked;
        }

        private int _goldDiagCount = 0;

        private int GetResources(List<Entity> entities)
        {
            int result = -1;
            string source = "none";

            // 尝试多个标签来源
            int[] tagIds = { (int)GameTag.RESOURCES, 173, 174, 175 };
            string[] tagNames = { "RESOURCES", "173", "174", "175" };

            // 扫描英雄实体获取所有金相关标签
            foreach (var e in entities)
            {
                if (!(GetTag(e, GameTag.CONTROLLER) == _localPlayerCtrl)) continue;
                if (!e.IsHero) continue;

                for (int i = 0; i < tagIds.Length; i++)
                {
                    try
                    {
                        int val = e.GetTag((GameTag)tagIds[i]);
                        if (val >= 0 && val <= 20)
                        {
                            result = val;
                            source = "hero." + tagNames[i];
                            break;
                        }
                    }
                    catch { }
                }
                if (result >= 0) break;
            }

            // 兜底: PlayerEntity
            if (result < 0) try
            {
                int r = Core.Game.PlayerEntity?.GetTag(GameTag.RESOURCES) ?? -1;
                if (r >= 0 && r <= 20) { result = r; source = "playerEnt"; }
            }
            catch { }

            if (result < 0) result = 3;

            // 每30帧诊断
            _goldDiagCount++;
            if (_goldDiagCount % 30 == 1)
                PluginLog(string.Format("GoldDiag: source={0} value={1}", source, result));

            return result;
        }

        private bool IsFrozenShop(List<Entity> entities)
        {
            foreach (var e in entities)
            {
                if (GetTag(e, GameTag.BACON_FREEZE) > 0) return true;
            }
            return false;
        }

        private int CountFreeRefresh(List<Entity> entities)
        {
            if (entities == null) return 0;
            return GameRuleEvaluator.ResolveFreeRefreshCount(
                entities.Select(entity => new FreeRefreshObservation
                {
                    CardId = entity.CardId,
                    ControllerId = GetTag(entity, GameTag.CONTROLLER),
                    TaggedCount = entity.HasTag(GameTag.BACON_FREE_REFRESH_COUNT)
                        ? entity.GetTag(GameTag.BACON_FREE_REFRESH_COUNT) : 0,
                }),
                _localPlayerCtrl);
        }

        // ── 随从提取 ──

        private List<Engine.MinionData> ExtractBoardMinions(List<Entity> entities)
        {
            var result = new List<Engine.MinionData>();
            foreach (var e in entities)
            {
                if (!e.IsInPlay) continue;
                if (!(GetTag(e, GameTag.CONTROLLER) == _localPlayerCtrl)) continue;
                if (IsHeroOrPower(e)) continue;
                if (string.IsNullOrEmpty(e.CardId)) continue;
                if (!IsBaconCard(e.CardId)) continue;
                if (e.Health <= 0) continue;
                if (e.IsBattlegroundsTrinket) continue;

                var minion = EntityToMinion(e);
                if (minion != null) result.Add(minion);
            }
            result.Sort((a, b) => a.Position.CompareTo(b.Position));
            return result;
        }

        private List<Engine.MinionData> ExtractHandMinions(List<Entity> entities)
        {
            var result = new List<Engine.MinionData>();
            foreach (var e in entities)
            {
                if (!e.IsInHand) continue;
                if (!(GetTag(e, GameTag.CONTROLLER) == _localPlayerCtrl)) continue;
                if (IsHeroOrPower(e)) continue;
                if (string.IsNullOrEmpty(e.CardId)) continue;
                if (!IsBaconCard(e.CardId)) continue;

                var card = EntityToMinion(e);
                if (card != null) result.Add(card);
            }
            result.Sort((a, b) => a.Position.CompareTo(b.Position)); // ZonePosition升序: 左→右对应索引0→N-1
            return result;
        }

        private static int _shopDiagCount = 0;
        private List<Engine.MinionData> ExtractShopMinions(List<Entity> entities, int turn)
        {
            var result = new List<Engine.MinionData>();
            var seenEntityIds = new HashSet<int>();

            // 每帧清空实体缓存，确保同回合内购买/刷新后能重新提取所有实体
            _lastExtractTurn = turn;
            _extractedShopIds.Clear();

            foreach (var e in entities)
            {
                if (string.IsNullOrEmpty(e.CardId)) continue;
                if (seenEntityIds.Contains(e.Id)) continue;
                if (!IsBaconCard(e.CardId)) continue;

                // 严格排除：场上(仅本地玩家)、手牌、英雄、技能、饰品
                // 酒馆实体在对手侧ZONE_PLAY, IsInPlay=true但controller!=本地玩家, 不能过滤
                if (e.IsInPlay && GetTag(e, GameTag.CONTROLLER) == _localPlayerCtrl) continue;
                if (e.IsInHand) continue;
                if (e.IsInGraveyard) continue;
                if (IsHeroOrPower(e)) continue;
                if (e.IsBattlegroundsTrinket) continue;

                ResolvedShopItemFact purchaseFact;
                if (!TryResolveShopItem(e, out purchaseFact)) continue;
                bool isSpell = purchaseFact.Kind == ShopItemKind.TavernSpell;
                // 血量检查仅对随从有效, 法术HP=0但依然是合法商店实体
                if (!isSpell && e.Health <= 0) continue;

                // 排除对手随从（战斗残留幽灵实体）
                // 注意: 不在此处检查EXHAUSTED, 商店实体在zone过渡时可能短暂带有该标记
                if (_opponentIds.Contains(e.Id)) continue;

                var zone = GetTag(e, GameTag.ZONE);
                // 商店实体可能在对手侧ZONE_PLAY(1)或ZONE_REMOVEDFROMGAME(5)
                if (zone != 1 && zone != 5) continue;

                // ZONE_POSITION是1-based(游戏引擎从1开始编号), 转为0-based slot索引
                int zonePos = e.HasTag(GameTag.ZONE_POSITION) ? e.GetTag(GameTag.ZONE_POSITION) : -1;
                int slotIndex = zonePos - 1; // 1-based → 0-based
                // 诊断: 首次发现 ZonePosition时记录原始值+转换后值
                if (zonePos > 0 && _zonePosDiagCount < 20)
                {
                    ExtractorLog(string.Format("DIAG ZonePos found: cardId={0} rawZonePos={1}→slot={2} eid={3}",
                        e.CardId, zonePos, slotIndex, e.Id));
                    _zonePosDiagCount++;
                }
                if (slotIndex < 0 || slotIndex >= 7) continue;

                if (_extractedShopIds.Contains(e.Id)) continue;
                _extractedShopIds.Add(e.Id);
                seenEntityIds.Add(e.Id);

                var tier = GetTag(e, GameTag.TECH_LEVEL);
                if (tier < 1 || tier > 6) tier = GetCardTechLevel(e.CardId);

                int cost = purchaseFact.Cost;
                result.Add(new Engine.MinionData
                {
                    CardId = e.CardId,
                    CardName = GetCardName(e.CardId),
                    EntityId = e.Id,
                    Tier = tier,
                    Attack = e.Attack,
                    Health = e.Health,
                    Position = slotIndex,
                    Golden = GetTag(e, GameTag.PREMIUM) >= 1,
                    Taunt = e.HasTag(GameTag.TAUNT),
                    DivineShield = e.HasTag(GameTag.DIVINE_SHIELD),
                    Windfury = e.HasTag(GameTag.WINDFURY),
                    Reborn = e.HasTag(GameTag.REBORN),
                    Poisonous = e.HasTag(GameTag.POISONOUS),
                    Venomous = e.HasTag(GameTag.VENOMOUS),
                    Tribe = string.Join(",", GetTribes(e)),
                    IsSpell = isSpell,
                    Cost = cost,
                    IsFrozen = e.HasTag(GameTag.UNTOUCHABLE),
                });
            }

            // 去重：同CardId最多保留2张（允许商店真有重复牌），按entityId优先
            var deduped = new List<Engine.MinionData>();
            var cardIdCounts = new Dictionary<string, int>();
            foreach (var item in result)
            {
                if (string.IsNullOrEmpty(item.CardId)) { deduped.Add(item); continue; }
                int cnt;
                cardIdCounts.TryGetValue(item.CardId, out cnt);
                if (cnt < 2)
                {
                    deduped.Add(item);
                    cardIdCounts[item.CardId] = cnt + 1;
                }
            }
            result = deduped;

            // 保留原始 ShopPosition(ZONE_POSITION-1), 由渲染层按酒馆展示槽位数映射。
            // 不能压成 dense index: 辛达苟萨/法术槽/少供给场景会导致视觉槽位整体偏移。
            result.Sort((a, b) => { int c = a.Position.CompareTo(b.Position); return c != 0 ? c : a.EntityId.CompareTo(b.EntityId); });

            // 始终cap到7, 防止幽灵实体溢出
            if (result.Count > 7)
            {
                if (_shopDiagCount < 20)
                    ExtractorLog(string.Format("Shop cap: {0} -> 7 (turn={1})", result.Count, turn));
                result = result.GetRange(0, 7);
            }

            // B17修复: 同回合商店提取为空时复用缓存，避免实体更新窗口(购买/刷新后新实体未创建)导致漏判
            if (result.Count == 0 && turn >= 1 && _cachedShopMinions != null && _cachedShopTurn == turn)
            {
                if (_shopDiagCount < 20)
                    ExtractorLog(string.Format("Shop diag T{0}: EMPTY → using cache ({1} items, turn={2})",
                        turn, _cachedShopMinions.Count, _cachedShopTurn));
                result = _cachedShopMinions;
            }
            else if (result.Count > 0)
            {
                // 更新非空缓存，仅保留可靠快照
                _cachedShopMinions = new List<Engine.MinionData>(result);
                _cachedShopTurn = turn;
            }
            // 未命中缓存且结果为空：可能是英雄选择/饰品选择等合法空商店阶段，缓存不清除

            // 诊断日志 (前20次)
            if (_shopDiagCount < 20)
            {
                if (result.Count > 0)
                {
                    ExtractorLog(string.Format("Shop diag T{0}: {1} items, top3:", turn, result.Count));
                    var top3 = result.GetRange(0, Math.Min(3, result.Count));
                    foreach (var item in top3)
                    {
                        ExtractorLog(string.Format("  cardId={0} tier={1} atk={2} hp={3} pos={4} eid={5}",
                            item.CardId, item.Tier, item.Attack, item.Health,
                            item.Position, item.EntityId));
                    }
                }
                else
                {
                    ExtractorLog(string.Format("Shop diag T{0}: EMPTY (0 shop entities found)", turn));
                }
                _shopDiagCount++;
            }

            return result;
        }

        private Engine.MinionData EntityToMinion(Entity e)
        {
            ResolvedShopItemFact purchaseFact;
            if (!TryResolveShopItem(e, out purchaseFact)) return null;
            var tribes = GetTribes(e);
            var tier = GetTag(e, GameTag.TECH_LEVEL);
            if (tier < 1 || tier > 6) tier = GetCardTechLevel(e.CardId);
            bool isSpell = purchaseFact.Kind == ShopItemKind.TavernSpell;
            int cost = purchaseFact.Cost;
            return new Engine.MinionData
            {
                CardId = e.CardId ?? "",
                CardName = GetCardName(e.CardId),
                EntityId = e.Id,
                Attack = e.Attack,
                Health = e.Health,
                Tier = tier,
                Position = e.ZonePosition,
                Golden = GetTag(e, GameTag.PREMIUM) >= 1,
                Taunt = e.HasTag(GameTag.TAUNT),
                DivineShield = e.HasTag(GameTag.DIVINE_SHIELD),
                Windfury = e.HasTag(GameTag.WINDFURY),
                Reborn = e.HasTag(GameTag.REBORN),
                Poisonous = e.HasTag(GameTag.POISONOUS),
                Venomous = e.HasTag(GameTag.VENOMOUS),
                Tribe = tribes.Count > 0 ? string.Join(",", tribes) : "",
                IsSpell = isSpell,
                Cost = cost,
                IsFrozen = e.HasTag(GameTag.UNTOUCHABLE) || e.HasTag(GameTag.CANT_BE_TARGETED_BY_ABILITIES)
                    || e.HasTag(GameTag.DORMANT) || e.HasTag(GameTag.CANT_PLAY)
                    || e.HasTag(GameTag.LITERALLY_UNPLAYABLE)  // 托里姆禁锢卡等
                    || (isSpell && e.Attack <= 0 && e.Health <= 0),
            };
        }

        // ── 英雄选项 ──

        private List<Engine.HeroOption> ExtractHeroOptions(List<Entity> entities, int turn)
        {
            var result = new List<Engine.HeroOption>();
            if (turn > 1) return result;

            foreach (var e in entities)
            {
                if (string.IsNullOrEmpty(e.CardId)) continue;
                if (!e.CardId.StartsWith("TB_BaconShop_HERO")) continue;
                if (GetTag(e, GameTag.CONTROLLER) == _localPlayerCtrl) continue;
                if (e.CardId.Contains("_PH") || e.CardId.Contains("_SKIN")) continue;

                result.Add(new Engine.HeroOption
                {
                    CardId = e.CardId,
                    HeroName = GetCardName(e.CardId),
                    EntityId = e.Id,
                });
            }

            return result;
        }

        // ── 饰品 ──

        private int _prevOfferCount = 0;
        private int _trinketLesserTurn = 6;
        private int _trinketGreaterTurn = 9;
        private int _lastTrinketOfferTurn = -1;     // 最近一次检测到饰品offer的回合
        private int _trinketOfferFirstTurn = -1;    // 当前饰品轮次首次出现的回合(用于跨回合抑制)
        private HashSet<string> _completedTrinketIds = null; // 已完成饰品轮的CardId集合(用于过滤残留实体)
        private int _trinketRoundResolvedTurn = -1;          // 本回合已完成选取的回合号(同回合残留压制/placeholder判定)
        /// <summary>本回合饰品选取是否已完成(plugin placeholder判定用, 07062107: owned绝对计数被英雄额外饰品破坏)</summary>
        public int TrinketRoundResolvedTurn { get { return _trinketRoundResolvedTurn; } }
        private HashSet<string> _equippedTrinketIds = new HashSet<string>(); // 已装备(inPlay)饰品CardId — 可靠的completed来源

        private List<Engine.TrinketOption> ExtractTrinketOffer(List<Entity> entities, int turn)
        {
            // 每帧先记录已装备(inPlay)饰品CardId — 这是可靠的"已选完"信号。
            // (旧逻辑bug: completed靠NewRound时offer集合提取, 含null→过滤恒失效; 已装备饰品inPlay的CardId才是确定的)
            foreach (var e in entities)
            {
                if (e.IsBattlegroundsTrinket && e.IsInPlay && HasLocalTrinketFact(e.CardId))
                    _equippedTrinketIds.Add(e.CardId);
            }

            int offerCount = entities.Count(e => e.IsBattlegroundsTrinket && !e.IsInHand && !e.IsInPlay
                && HasLocalTrinketFact(e.CardId) && !_equippedTrinketIds.Contains(e.CardId));

            bool trinketTurnChanged = turn != _lastTrinketOfferTurn;
            // 新饰品轮次: 回合变化时清除上轮offer缓存(不限T6, 畸变可改变饰品回合)
            if (trinketTurnChanged)
            {
                _lastPickedOfferIds = null;
                // 首次出现真实offer才设firstTurn
                // 已装备数: 达2则一轮(大+小)完成, 重置firstTurn准备下一可能轮次(畸变多轮)
                if (_equippedTrinketIds.Count >= 2)
                {
                    _trinketOfferFirstTurn = offerCount > 0 ? turn : -1;
                }
                PluginLog(string.Format("TrinketNewRound: T{0}→T{1}, offer={2} equipped={3} firstTurn={4}",
                    _lastTrinketOfferTurn, turn, offerCount, _equippedTrinketIds.Count, _trinketOfferFirstTurn));
            }
            _lastTrinketOfferTurn = turn;
            bool newOfferBatch = offerCount > 0 && _prevOfferCount == 0;
            bool scheduledTrinketTurn = trinketTurnChanged
                && (turn == _trinketLesserTurn || turn == _trinketGreaterTurn);
            if (_completedTrinketIds != null && _completedTrinketIds.Count > 0
                && (newOfferBatch || scheduledTrinketTurn))
                _completedTrinketIds = null;
            _prevOfferCount = offerCount;

            var result = new List<Engine.TrinketOption>();
            bool scheduledTrinketChoiceTurn = turn == _trinketLesserTurn || turn == _trinketGreaterTurn;
            int ownedRealTrinketCount = entities.Count(e =>
                e.IsBattlegroundsTrinket && HasLocalTrinketFact(e.CardId)
                && (e.IsInHand || e.IsInPlay));
            // 诊断: 每30帧输出所有trinket实体(含被过滤的), 用于定位大饰品检测失败原因
            _trinketDiagCount++;
            var allTrinketEntities = entities.Where(e => e.IsBattlegroundsTrinket && !string.IsNullOrEmpty(e.CardId)).ToList();
            if (_trinketDiagCount % 30 == 1 && allTrinketEntities.Count > 0)
            {
                var details = allTrinketEntities.Select(e => string.Format("{0}(inHand={1} inPlay={2} ctrl={3} zone={4})",
                    e.CardId, e.IsInHand, e.IsInPlay, GetTag(e, GameTag.CONTROLLER), e.HasTag(GameTag.ZONE) ? e.GetTag(GameTag.ZONE) : -1));
                PluginLog(string.Format("TrinketDiag T{0}: {1} entities equipped=[{2}] → [{3}]", turn,
                    allTrinketEntities.Count, string.Join(",", _equippedTrinketIds), string.Join(", ", details)));
            }
            foreach (var e in entities)
            {
                Engine.TrinketFact localFact;
                if (!TryGetLocalTrinketFact(e.CardId, out localFact)) continue;
                if (!e.IsBattlegroundsTrinket) continue;
                if (e.IsInHand) continue;
                int ctrl = GetTag(e, GameTag.CONTROLLER);
                // 已放置的饰品(zone=1 inPlay)不在可选offer中, 不限ctrl(不同渠道ctrl值不同:5/8/13)
                if (e.IsInPlay) continue;
                // 过滤已装备饰品的残留实体(同CardId的zone=5残留): 已装备的不再作为offer。
                // 神秘魔方会以同一CardId既作为已拥有饰品/选择源, 又作为可替换offer出现, 不能按普通残留过滤。
                if (_equippedTrinketIds.Contains(e.CardId) && !IsReplaceableTrinketOfferDuplicate(e.CardId)) continue;
                // 过滤已完成轮次的残留实体: CardId在上轮已选中/超时→本轮不再显示
                if (_completedTrinketIds != null && _completedTrinketIds.Contains(e.CardId)) continue;
                var opt = new Engine.TrinketOption
                {
                    CardId = e.CardId,
                    TrinketName = !string.IsNullOrEmpty(localFact.NameZhCn)
                        ? localFact.NameZhCn
                        : !string.IsNullOrEmpty(localFact.NameEnUs)
                            ? localFact.NameEnUs : localFact.CardId,
                    Description = "",
                    IsLesser = localFact.IsLesser,
                    EntityId = e.Id,
                };
                result.Add(opt);
                // 不再break: 收集所有BACON_TRINKET实体用于新轮次检测(小饰品残留可能遮挡大饰品实体)
            }

            if (scheduledTrinketChoiceTurn && result.Count >= 2)
            {
                bool expectLesser = turn == _trinketLesserTurn;
                bool expectGreater = turn == _trinketGreaterTurn;
                if (expectLesser || expectGreater)
                {
                    var typed = result.Where(t => t != null && t.IsLesser == expectLesser).ToList();
                    if (typed.Count >= 2 && typed.Count < result.Count)
                    {
                        ExtractorLog(string.Format("DIAG Trinket: type-filtered T{0} expected={1} before={2} kept=[{3}]",
                            turn, expectLesser ? "lesser" : "greater", result.Count,
                            string.Join(",", typed.Select(t => t.CardId))));
                        // 首见延迟诊断(07061713: T9真实候选比placeholder晚17s, 归因实体晚到 vs 门控延迟)
                        var nowDiag = DateTime.UtcNow;
                        ExtractorLog(string.Format("DIAG TrinketFirstSeen: T{0} [{1}]", turn,
                            string.Join(",", typed.Select(t =>
                            {
                                double ageSec = _zone6FirstSeen.TryGetValue(t.EntityId, out var fsAt)
                                    ? (nowDiag - fsAt).TotalSeconds : -1;
                                return string.Format("{0}:eid={1}:age={2:F1}s", t.CardId, t.EntityId, ageSec);
                            }))));
                        result = typed;
                    }
                    else if (typed.Count < 2 && result.Count > 0)
                    {
                        ExtractorLog(string.Format("DIAG Trinket: expected {0} batch pending T{1}, suppress mismatched options=[{2}]",
                            expectLesser ? "lesser" : "greater", turn,
                            string.Join(",", result.Select(t => t.CardId).Distinct())));
                        result.Clear();
                    }
                }
            }
            // 已拥有饰品计数: InHand(刚选未放)或InPlay(已放置), 不限ctrl(饰品ctrl值多样:1/5/8/13)
            int ownedTrinketCount = ownedRealTrinketCount;
            // 检测到选取(owned增加): 把当前result(未选中的兄弟实体)记入completed, 防其残留到后续回合。
            // 已选中那个已inPlay→由 _equippedTrinketIds 过滤; 兄弟实体zone=5需显式记completed。
            // 每次选取都累积(不止owned>=2), 修问题3(T7小饰品兄弟残留)。
            if (_prevOwnedTrinketCount >= 0 && ownedTrinketCount > _prevOwnedTrinketCount)
            {
                var siblings = result.Where(t => !string.IsNullOrEmpty(t.CardId)).Select(t => t.CardId);
                if (_completedTrinketIds == null) _completedTrinketIds = new HashSet<string>();
                foreach (var cid in siblings) _completedTrinketIds.Add(cid);
                _lastPickedOfferIds = new HashSet<string>(siblings);
                _trinketRoundResolvedTurn = turn;
                ExtractorLog(string.Format("DIAG Trinket: picked {0}→{1}, round resolved, completed+=[{2}]",
                    _prevOwnedTrinketCount, ownedTrinketCount,
                    _completedTrinketIds != null ? string.Join(",", _completedTrinketIds) : ""));
                // 07062107修复: picked帧的result就是残留兄弟实体, 立即清空 —
                // 否则本帧仍以offer渲染(T6选完后"1-4号推荐"闪现的根因)。
                result.Clear();
            }
            _prevOwnedTrinketCount = ownedTrinketCount;

            // offer完全消失(选完最后离场) → 把上次offer记入completed补强过滤
            if (_lastTrinketOfferCount > 0 && result.Count == 0 && _lastPickedOfferIds != null)
            {
                if (_completedTrinketIds == null) _completedTrinketIds = new HashSet<string>();
                foreach (var cid in _lastPickedOfferIds) if (!string.IsNullOrEmpty(cid)) _completedTrinketIds.Add(cid);
            }
            _lastTrinketOfferCount = result.Count;

            var uniqueOfferIds = result
                .Where(t => !string.IsNullOrEmpty(t.CardId))
                .Select(t => t.CardId)
                .Distinct()
                .ToList();
            if (result.Count >= 2 && uniqueOfferIds.Count < 2)
            {
                if (_completedTrinketIds == null) _completedTrinketIds = new HashSet<string>();
                foreach (var cid in uniqueOfferIds) _completedTrinketIds.Add(cid);
                ExtractorLog(string.Format("DIAG Trinket: duplicate-only residue suppressed [{0}]",
                    string.Join(",", uniqueOfferIds)));
                result.Clear();
            }

            bool waitingForGreaterTrinketTurn = ownedRealTrinketCount == 1
                && turn > _trinketLesserTurn
                && turn < _trinketGreaterTurn;
            bool hasRepeatingReplacementTrinket = _equippedTrinketIds != null
                && _equippedTrinketIds.Contains("BG30_MagicItem_703");
            // 07062107修复: 本回合已完成选取 → 同回合后续任何批次都是残留(T6选完后另一批3个
            // 小饰品残留渲染2分钟的根因: 旧压制条件 owned>=2 在T6 owned=1 时不生效)。
            // 神秘魔方(同CardId替换机制)例外, 允许同回合再次出现。
            if (result.Count >= 2 && _trinketRoundResolvedTurn == turn && !hasRepeatingReplacementTrinket)
            {
                if (_completedTrinketIds == null) _completedTrinketIds = new HashSet<string>();
                foreach (var cid in uniqueOfferIds) _completedTrinketIds.Add(cid);
                ExtractorLog(string.Format("DIAG Trinket: same-turn post-resolve residue suppressed T{0} options=[{1}]",
                    turn, string.Join(",", uniqueOfferIds)));
                result.Clear();
            }
            if (result.Count >= 2 && waitingForGreaterTrinketTurn && !hasRepeatingReplacementTrinket)
            {
                if (_completedTrinketIds == null) _completedTrinketIds = new HashSet<string>();
                foreach (var cid in uniqueOfferIds) _completedTrinketIds.Add(cid);
                _lastTrinketOfferCount = 0;
                ExtractorLog(string.Format("DIAG Trinket: pre-greater residue suppressed T{0} owned={1} options=[{2}]",
                    turn, ownedRealTrinketCount, string.Join(",", uniqueOfferIds)));
                result.Clear();
            }

            if (result.Count >= 2 && ownedRealTrinketCount >= 2)
            {
                if (_completedTrinketIds == null) _completedTrinketIds = new HashSet<string>();
                foreach (var cid in uniqueOfferIds) _completedTrinketIds.Add(cid);
                ExtractorLog(string.Format("DIAG Trinket: post-choice residue suppressed T{0} owned={1} options=[{2}]",
                    turn, ownedRealTrinketCount, string.Join(",", uniqueOfferIds)));
                result.Clear();
            }

            // 补设firstTurn: 若NewRound时未检测到实体但后续帧出现了, 在此处记录
            if (_trinketOfferFirstTurn < 0 && result.Count >= 2)
                _trinketOfferFirstTurn = turn;

            // 注: 已移除旧的按回合间隔猜测的残留抑制分支(会误杀T9真大饰品offer, 问题6根因)。
            // 现改为源头双过滤(_equippedTrinketIds + _completedTrinketIds), 残留实体在foreach中已被滤掉,
            // result 只含真实新轮次offer, 无需回合兜底抑制。
            if (result.Count < 2)
                return new List<Engine.TrinketOption>();
            if (result.Count > 0 && _trinketDiagCount % 30 == 0)
            {
                ExtractorLog(string.Format("DIAG Trinket: {0} options [{1}]",
                    result.Count, string.Join(", ", result.Select(t => t.TrinketName ?? t.CardId))));
            }
            if (result.Count > 4)
            {
                if (scheduledTrinketChoiceTurn)
                {
                    var salvaged = new List<Engine.TrinketOption>();
                    var seen = new HashSet<string>();
                    foreach (var opt in result.OrderByDescending(t => t.EntityId))
                    {
                        if (opt == null || string.IsNullOrEmpty(opt.CardId)) continue;
                        if (seen.Contains(opt.CardId)) continue;
                        seen.Add(opt.CardId);
                        salvaged.Add(opt);
                        if (salvaged.Count >= 4) break;
                    }
                    if (salvaged.Count >= 2)
                    {
                        ExtractorLog(string.Format("DIAG Trinket: polluted offer salvaged count={0} unique={1} kept=[{2}]",
                            result.Count, uniqueOfferIds.Count, string.Join(",", salvaged.Select(t => t.CardId))));
                        return salvaged;
                    }
                }
                ExtractorLog(string.Format("DIAG Trinket: polluted offer suppressed count={0} unique={1} [{2}]",
                    result.Count, uniqueOfferIds.Count, string.Join(",", uniqueOfferIds)));
                return new List<Engine.TrinketOption>();
            }
            return result;
        }

        private bool HasReplenishingShopEffect(Engine.GameState state)
        {
            if (_equippedTrinketIds != null && _equippedTrinketIds.Contains("BG30_MagicItem_841"))
                return true;
            if (_timewarpedNewRecruitActive)
                return true;
            return state != null
                && state.TavernTier < 6
                && state.ShopMinions != null
                && state.ShopMinions.Count >= 7;
        }

        private void UpdateTimewarpedNewRecruitState(List<Entity> entities, Engine.GameState state)
        {
            const string timewarpedNewRecruitId = "BG34_Treasure_917";
            var currentHandIds = new HashSet<int>();
            if (entities != null)
            {
                foreach (var e in entities)
                {
                    if (e == null || e.CardId != timewarpedNewRecruitId) continue;
                    if (e.IsInHand) currentHandIds.Add(e.Id);
                }
            }

            if (!_timewarpedNewRecruitActive
                && _prevTimewarpedNewRecruitHandIds.Count > 0
                && _prevTimewarpedNewRecruitHandIds.Any(id => !currentHandIds.Contains(id)))
            {
                _timewarpedNewRecruitActive = true;
                ExtractorLog("DIAG ReplenishingShop: Timewarped New Recruit active (BG34_Treasure_917)");
            }

            _prevTimewarpedNewRecruitHandIds = currentHandIds;
        }

        private bool HasLocalTrinketFact(string cardId)
        {
            Engine.TrinketFact fact;
            return TryGetLocalTrinketFact(cardId, out fact);
        }

        private static bool IsReplaceableTrinketOfferDuplicate(string cardId)
        {
            // Mystery Cube can appear as the owned replacement source and as a current choice option.
            return cardId == "BG30_MagicItem_703";
        }

        private bool TryGetLocalTrinketFact(string cardId, out Engine.TrinketFact fact)
        {
            fact = new Engine.TrinketFact();
            if (string.IsNullOrEmpty(cardId) || _trinketFactSource == null) return false;
            try
            {
                return _trinketFactSource.TryGet(cardId, out fact)
                    && string.Equals(fact.CardId, cardId, StringComparison.Ordinal);
            }
            catch
            {
                fact = new Engine.TrinketFact();
                return false;
            }
        }

        /// <summary>严格过滤zone 6实体: 仅接受MINION(4)/SPELL(5), 排除英雄(7)/技能(10)/附魔(6)/饰品</summary>
        private List<Engine.TrinketOption> ExtractDiscoverOptionsStrict(List<Entity> entities, HashSet<int> knownIds, HashSet<string> knownCardIds, HashSet<string> knownNames = null)
        {
            var candidates = new List<Engine.TrinketOption>();
            int foundValid = 0;
            foreach (var e in entities)
            {
                if (string.IsNullOrEmpty(e.CardId)) continue;
                // 必须在SETASIDE区域 (zone=6)
                int zone = e.HasTag(GameTag.ZONE) ? e.GetTag(GameTag.ZONE) : 0;
                if (zone != 6) continue;
                // CARDTYPE限制: 随从(4)/法术(5)/英雄技能(10, 双重宇宙等畸变)
                int ct = e.HasTag(GameTag.CARDTYPE) ? e.GetTag(GameTag.CARDTYPE) : 0;
                if (ct != 4 && ct != 5 && ct != 10) continue;
                // 排除已存在于战场/手牌/商店的实体
                if (e.IsInPlay || e.IsInHand) continue;
                if (knownIds.Contains(e.Id)) continue;
                if (knownCardIds != null && knownCardIds.Contains(e.CardId)) continue;
                // 排除长期滞留的zone6实体(超过10秒): 战斗衍生物/过渡实体常长期滞留
                if (_zone6FirstSeen.TryGetValue(e.Id, out var firstSeen))
                {
                    if ((DateTime.UtcNow - firstSeen).TotalSeconds > 10)
                        continue;
                }
                // 排除0/0空壳
                if (e.Attack <= 0 && e.Health <= 0) continue;
                // 卡牌ID必须是BG前缀
                if (!e.CardId.StartsWith("BG") && !e.CardId.StartsWith("TB_Bacon")) continue;
                // 排除hero/trinket/enchantment卡牌 (英雄技能CARDTYPE=10允许_HP前缀)
                if (ct != 10)
                {
                    if (e.CardId.Contains("_HERO_") || e.CardId.Contains("_HP") || e.CardId.Contains("MagicItem")
                        || e.CardId.Contains("Trinket") || e.CardId.Contains("Enchantment") || e.CardId.EndsWith("_PH"))
                        continue;
                }
                else
                {
                    // 英雄技能: 排除非BG前缀和token
                    if (!e.CardId.StartsWith("BG") && !e.CardId.StartsWith("TB_Bacon")) continue;
                    if (e.CardId.EndsWith("_PH") || e.CardId.Contains("token")) continue;
                }
                foundValid++;
                // 排除金色卡: zone6中的金色卡是三连奖励(非发现选项)
                if (GetTag(e, GameTag.PREMIUM) >= 1) continue;
                // 排除token/衍生卡: cardId含_t后缀(需下划线前缀防误杀)
                if (e.CardId.EndsWith("_t") || e.CardId.EndsWith("t2") || e.CardId.EndsWith("t3")
                    || e.CardId.Contains("token"))
                    continue;
                string resolvedName = GetCardName(e.CardId);
                if (resolvedName.Contains("时空扭曲") || resolvedName.Contains("Timewarp")
                    || e.CardId.Contains("Timewarp") || e.CardId.Contains("timewarp"))
                    continue;
                // 常见token/衍生名黑名单
                if (resolvedName == "甲虫" || resolvedName == "新生幼苗" || resolvedName == "触手"
                    || resolvedName == "机械幼龙" || resolvedName == "微型机器人"
                    || resolvedName == "小鬼" || resolvedName == "白银之手新兵")
                    continue;
                // 注意: 不移除knownNames匹配 — 发现完全可能给板面已有的同名卡(如三连奖励)
                int tier = GetTag(e, GameTag.TECH_LEVEL);
                if (tier < 1) tier = 1;
                candidates.Add(new Engine.TrinketOption
                {
                    CardId = e.CardId,
                    TrinketName = resolvedName,
                    EntityId = e.Id,
                    Tier = tier,
                    Attack = e.Attack,
                    Health = e.Health,
                });
            }
            // 标准发现: 2-3候选直接返回; >3时取前3(噪声环境常见); <2时返回空
            if (candidates.Count >= 2 && candidates.Count <= 3)
            {
                if (_zone6DiagCount < 5)
                {
                    ExtractorLog(string.Format("DIAG Discover strict: {0} candidates from {1} zone6 valid [{2}]",
                        candidates.Count, foundValid,
                        string.Join(", ", candidates.Select(c => c.TrinketName ?? c.CardId))));
                    _zone6DiagCount++;
                }
                return candidates;
            }
            else if (candidates.Count > 3)
            {
                // >3 候选 = zone6 混入非发现实体(战斗衍生物/过渡实体)。炉石发现恒为 2-3 选项,
                // 此前 Take(3) 盲取前3会捞到幻影选项(0611 实测: 构造亡灵第二次发现 zone6 涨到 10,
                // Take(3) 给出场上和选项里都没有的卡)。无法可靠区分时返回空, 不猜。
                if (_zone6DiagCount < 8)
                {
                    // 0611 06110521 问题5+7: 打出污染候选的实体明细(cardId+eid+首见秒), 下局区分
                    // 真发现选项 vs 战斗衍生物 vs 英雄技能衍生(重拾灵魂BG34_HeroPowerSpell_018)
                    var now = DateTime.UtcNow;
                    var detail = candidates.Select(c => {
                        double age = _zone6FirstSeen.TryGetValue(c.EntityId, out var fs) ? (now - fs).TotalSeconds : -1;
                        return string.Format("{0}(eid={1} age={2:F0}s)", c.CardId, c.EntityId, age);
                    });
                    ExtractorLog(string.Format("DIAG Discover strict: {0} candidates > 3 (zone6 污染, 返回空) from {1} valid → [{2}]",
                        candidates.Count, foundValid, string.Join(", ", detail)));
                    _zone6DiagCount++;
                }
                return new List<Engine.TrinketOption>();
            }
            return new List<Engine.TrinketOption>();
        }

        private List<Engine.TrinketOption> ExtractDiscoverOptions(List<Entity> entities, bool gate, HashSet<int> knownIds, HashSet<string> knownCardIds = null)
        {
            // gate为false时跳过提取 (仅当hasDiscoverSource&&(handDecreased||discoverCandidatesExist)时启用)
            if (!gate) return new List<Engine.TrinketOption>();

            var candidates = new List<Engine.TrinketOption>();

            foreach (var e in entities)
            {
                if (string.IsNullOrEmpty(e.CardId)) continue;
                // 必须是SETASIDE区域 (发现卡片在此区域出现)
                int zone = e.HasTag(GameTag.ZONE) ? e.GetTag(GameTag.ZONE) : 0;
                if (zone != 6) continue;
                // 排除已在战场/手牌的实体 (二次确认，防zone标记延迟)
                if (e.IsInPlay || e.IsInHand) continue;
                // 跳过已知板面/手牌/商店实体 (已在场上)
                if (knownIds.Contains(e.Id)) continue;
                // 跳过已知CardId: 板面/手牌已有的卡名→不是发现选项
                if (knownCardIds != null && knownCardIds.Contains(e.CardId)) continue;
                // 跳过饰品实体
                if (e.IsBattlegroundsTrinket) continue;
                // 只接受随从(4)或法术(5)
                int ct = e.HasTag(GameTag.CARDTYPE) ? e.GetTag(GameTag.CARDTYPE) : 0;
                if (ct != 4 && ct != 5) continue;
                // 必须是本地玩家控制
                int ctrl;
                try { ctrl = e.GetTag(GameTag.CONTROLLER); } catch { continue; }
                if (ctrl != _localPlayerCtrl) continue;
                // 跳过英雄/技能实体
                if (IsHeroOrPower(e)) continue;
                if (e.CardId.StartsWith("TB_BaconShop_HERO")) continue;
                // 跳过附魔(6)和英雄(7)类型
                if (ct == 6 || ct == 7) continue;
                // 跳过多余法术/附魔实体: Spellcraft/Enchantment标签
                if (e.HasTag(GameTag.SPELLCRAFT)) continue;
                if (e.CardId != null && (e.CardId.Contains("Enchantment") || e.CardId.Contains("Ench"))) continue;
                // 必须有有效的攻/血 (发现卡片通常有攻血数值)
                if (e.Health <= 0 && e.Attack <= 0 && ct != 5) continue;

                int dTier = GetTag(e, GameTag.TECH_LEVEL);
                if (dTier < 1 || dTier > 6) dTier = GetCardTechLevel(e.CardId);
                candidates.Add(new Engine.TrinketOption
                {
                    CardId = e.CardId,
                    TrinketName = GetCardName(e.CardId),
                    Description = "",
                    EntityId = e.Id,
                    Tier = dTier,
                    Attack = e.Attack,
                    Health = e.Health,
                });
            }
            // 发现面板: 3选1(常规)或2选1(部分英雄技能)
            return (candidates.Count >= 2 && candidates.Count <= 3)
                ? candidates : new List<Engine.TrinketOption>();
        }

        // 快速检测是否有发现候选实体 (ZONE=6中有非已知实体的随从/法术)
        private bool HasDiscoverCandidates(List<Entity> entities, HashSet<int> knownIds, HashSet<string> knownCardIds = null)
        {
            int count = 0;
            int zone6Total = 0;
            foreach (var e in entities)
            {
                int zone = e.HasTag(GameTag.ZONE) ? e.GetTag(GameTag.ZONE) : 0;
                if (zone == 6) zone6Total++;
            }
            // 诊断: 发现ZONE=6实体时记录详情
            if (zone6Total >= 1 && _zone6DiagCount < 10)
            {
                var diagLines = new List<string>();
                foreach (var e in entities)
                {
                    int z = e.HasTag(GameTag.ZONE) ? e.GetTag(GameTag.ZONE) : 0;
                    if (z != 6) continue;
                    diagLines.Add(string.Format("{0}(id={1},play={2},hand={3},atk={4},hp={5})",
                        e.CardId ?? "?", e.Id, e.IsInPlay, e.IsInHand, e.Attack, e.Health));
                }
                ExtractorLog(string.Format("DIAG Discover zone6({0} entities): {1}",
                    zone6Total, string.Join(" | ", diagLines)));
                _zone6DiagCount++;
            }
            // ── 主检测循环 ──
            foreach (var e in entities)
            {
                if (string.IsNullOrEmpty(e.CardId)) continue;
                int zone = e.HasTag(GameTag.ZONE) ? e.GetTag(GameTag.ZONE) : 0;
                if (zone != 6) continue;
                // 排除已在战场/手牌/商店的实体 (二次确认，防zone标记延迟)
                if (e.IsInPlay || e.IsInHand) continue;
                if (knownIds.Contains(e.Id)) continue;
                if (knownCardIds != null && knownCardIds.Contains(e.CardId)) continue;
                int ct = e.HasTag(GameTag.CARDTYPE) ? e.GetTag(GameTag.CARDTYPE) : 0;
                if (ct != 4 && ct != 5) continue;
                if (e.IsBattlegroundsTrinket) continue;
                int ctrl;
                try { ctrl = e.GetTag(GameTag.CONTROLLER); } catch { continue; }
                if (ctrl != _localPlayerCtrl) continue;
                if (IsHeroOrPower(e)) continue;
                // 排除0/0空壳实体 (附魔/效果标记)
                if (e.Attack <= 0 && e.Health <= 0 && ct != 5) continue;
                count++;
                if (count >= 3) return true;
            }
            return false;
        }

        // 检查玩家是否有发现触发源(手牌/场上随从的发现牌或发现型英雄技能)
        private bool HasDiscoverSource(List<Engine.MinionData> handMinions, List<Engine.MinionData> boardMinions, string heroPowerCardId)
        {
            // 英雄技能检测: 发现型技能(检查文本关键词)
            if (!string.IsNullOrEmpty(heroPowerCardId))
            {
                try
                {
                    HearthDb.Card hpCard;
                    if (HearthDb.Cards.All.TryGetValue(heroPowerCardId, out hpCard) && hpCard != null)
                    {
                        string hpText = hpCard.Text ?? "";
                        if (hpText.Contains("发现") || hpText.Contains("Discover"))
                            return true;
                    }
                }
                catch { }
            }
            // 手牌检测: 三连奖励卡 + HearthDb文本检测
            if (handMinions != null)
            {
                foreach (var hm in handMinions)
                {
                    if (string.IsNullOrEmpty(hm.CardId)) continue;
                    // 三连奖励卡: TB_BaconShop_Triples_* 直接视为发现源
                    if (hm.CardId.StartsWith("TB_BaconShop_Triples"))
                        return true;
                    try
                    {
                        HearthDb.Card c;
                        if (HearthDb.Cards.All.TryGetValue(hm.CardId, out c) && c != null)
                        {
                            string text = c.Text ?? "";
                            if (text.Contains("发现") || text.Contains("Discover"))
                                return true;
                        }
                    }
                    catch { }
                }
            }
            // 场上检测: 仅当手牌减少时(可能刚打出发现牌), 检查场上新增的随从
            // 不在此处实现; 由gate参数中的handDecreased + ExtractDiscoverOptions的gate覆盖
            return false;
        }

        // ── 对手 ──

        private List<Engine.OpponentData> ExtractOpponents(List<Entity> entities)
        {
            var opponents = new Dictionary<int, Engine.OpponentData>();
            foreach (var e in entities)
            {
                if (string.IsNullOrEmpty(e.CardId)) continue;
                var ctrl = GetTag(e, GameTag.CONTROLLER);
                if (ctrl <= 1 || ctrl == _localPlayerCtrl) continue;

                if (!opponents.ContainsKey(ctrl))
                {
                    opponents[ctrl] = new Engine.OpponentData
                    {
                        ControllerId = ctrl,
                    };
                }

                var opp = opponents[ctrl];

                if (IsBattlegroundsHeroEntity(e))
                {
                    opp.HeroCardId = e.CardId;
                    opp.HeroName = GetCardName(e.CardId);
                    var hp = GetTag(e, GameTag.HEALTH);
                    var dmg = GetTag(e, GameTag.DAMAGE);
                    if (hp <= 0) hp = 30;
                    if (dmg > 0 && hp > 30) hp -= dmg;
                    opp.Health = Math.Max(0, hp);
                    var tier = GetTag(e, GameTag.TECH_LEVEL);
                    if (tier == 0) tier = GetTag(e, GameTag.PLAYER_TECH_LEVEL);
                    if (tier == 0) tier = 1;
                    opp.TavernTier = tier;
                    opp.Alive = Math.Max(0, hp) > 0;
                }
                else if (e.IsInPlay && !IsHeroOrPower(e)
                    && !IsUIEntity(e))  // 排除按钮/附魔等非随从实体
                {
                    var minion = EntityToMinion(e);
                    if (minion != null) opp.BoardMinions.Add(minion);
                }
            }

            return opponents.Values.ToList();
        }

        // ── 种族 ──

        private List<string> GetTribes(Entity e)
        {
            var result = new List<string>();
            var race = GetTag(e, GameTag.CARDRACE);
            if (race > 0)
            {
                var raceName = RaceToString(race);
                if (!string.IsNullOrEmpty(raceName))
                    result.Add(raceName);
            }
            return result;
        }

        private static bool IsUIEntity(Entity e)
        {
            if (e == null || string.IsNullOrEmpty(e.CardId)) return true;
            var cid = e.CardId;
            // 按钮/附魔/系统实体
            if (cid.StartsWith("TB_BaconShop")) return true;
            if (cid.Contains("_Button") || cid.Contains("_Enchant") || cid.Contains("_Ench")) return true;
            if (cid.Contains("_FXWatcher") || cid.Contains("_DragSell") || cid.Contains("_DragBuy")) return true;
            if (cid.StartsWith("Bacon_")) return true;
            if (cid.Contains("_PlayerE") || cid.Contains("_Checke")) return true;
            ResolvedShopItemFact purchaseFact;
            return !TryResolveShopItem(e, out purchaseFact);
        }

        // 本局已检测到的可用种族缓存(跨回合持久)
        private static HashSet<string> _detectedTribes = new HashSet<string>();
        private static int _detectedTribesTurn = 0;

        private void DetectAvailableTribes(Engine.GameState state)
        {
            // 每局开始重置缓存
            if (state.Turn < _detectedTribesTurn) { _detectedTribes.Clear(); _detectedTribesTurn = 0; }
            _detectedTribesTurn = state.Turn;

            var allMinions = new List<Engine.MinionData>();
            allMinions.AddRange(state.ShopMinions);
            allMinions.AddRange(state.BoardMinions);
            foreach (var o in state.Opponents)
                if (o.Alive) allMinions.AddRange(o.BoardMinions);

            foreach (var m in allMinions)
            {
                foreach (var t in Engine.MinionData.GetTribesArray(m.Tribe))
                {
                    if (t != "ALL")
                        _detectedTribes.Add(t);
                }
            }

            // 更新state的可用种族
            state.AvailableTribes = new HashSet<string>(_detectedTribes);
        }

        private string RaceToString(int race)
        {
            switch (race)
            {
                case 14: return "鱼人";
                case 15: return "恶魔";
                case 17: return "机械";
                case 20: return "野兽";
                case 21: return "图腾";
                case 23: return "海盗";
                case 24: return "龙";
                case 26: return "全部";
                case 43: return "野猪人";
                case 88: return "元素";
                case 92: return "纳迦";
                case 136: return "亡灵";
                default: return "";
            }
        }

        // ── 工具函数 ──

        private int GetTag(Entity entity, GameTag tag)
        {
            try { return entity.GetTag(tag); }
            catch { return 0; }
        }

        private bool IsHeroOrPower(Entity e)
        {
            if (string.IsNullOrEmpty(e.CardId)) return false;
            if (e.CardId.Contains("_PH")) return true;
            return e.CardId.StartsWith("TB_BaconShop_HERO")
                || e.CardId.StartsWith("TB_BaconShop_HP_")
                || e.CardId.Contains("_HERO_POWER")
                || e.CardId.Contains("_HERO_")
                || e.CardId.Contains("_HEROPOWER_");
        }

        private bool IsBattlegroundsHeroEntity(Entity e)
        {
            return e != null && e.IsHero
                && BattlegroundsHeroIdentity.IsEligibleHeroCardId(e.CardId);
        }

        private static bool IsBaconCard(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return false;
            return cardId.StartsWith("TB_Bacon") || cardId.StartsWith("BG");
        }

        private int GetCardTechLevel(string cardId)
        {
            try
            {
                if (string.IsNullOrEmpty(cardId)) return 0;
                HearthDb.Card c;
                if (HearthDb.Cards.All.TryGetValue(cardId, out c) && c != null && c.TechLevel > 0)
                    return c.TechLevel;
                return GuessTierFromId(cardId);
            }
            catch { return GuessTierFromId(cardId); }
        }

        private int GuessTierFromId(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return 0;
            int t;
            t = Engine.CardNameService.GetTier(cardId);
            if (t > 0) return t;
            if (cardId.StartsWith("BG_"))
            {
                var baseId = cardId.Substring(3);
                try
                {
                    HearthDb.Card c;
                    if (HearthDb.Cards.All.TryGetValue(baseId, out c) && c != null && c.TechLevel > 0)
                        return c.TechLevel;
                }
                catch { }
            }
            return 0;
        }

        private string GetCardName(string cardId)
        {
            return Engine.CardNameService.GetName(cardId);
        }

        /// <summary>HearthDb Race 值 → 中文种族名，用于饰品种族检测兜底</summary>
        private static string MapHearthDbRace(int raceVal)
        {
            // HearthDb Race 枚举值 (兼容不同版本, 直接用int匹配)
            switch (raceVal)
            {
                case 20: return "野兽";    // BEAST
                case 17: return "机械";    // MECH / MECHANICAL
                case 15: return "恶魔";    // DEMON
                case 24: return "龙";      // DRAGON
                case 18: return "元素";    // ELEMENTAL
                case 11: return "亡灵";    // UNDEAD
                case 23: return "海盗";    // PIRATE
                case 43: return "野猪人";  // QUILBOAR
                case 92: case 93: return "纳迦"; // NAGA
                case 14: return "鱼人";    // MURLOC
                default: return "";
            }
        }
    }
}
