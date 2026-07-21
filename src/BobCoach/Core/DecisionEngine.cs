using System;
using System.Collections.Generic;
using System.Linq;

namespace BobCoach.Engine
{
    /// <summary>
    /// 升本建议返回结构
    /// </summary>
    public struct LevelUpSuggestion
    {
        public string Suggestion;
        public string Reason;
        public LevelUpSuggestion(string suggestion, string reason)
        {
            Suggestion = suggestion;
            Reason = reason;
        }
    }

    /// <summary>
    /// 购买排名条目
    /// </summary>
    internal struct RankedBuy
    {
        public int Index;
        public double Score;
    }

    /// <summary>
    /// 商店卡牌评分结果（供 UI 渲染卡片级高亮使用）
    /// </summary>
    public struct ShopCardScore
    {
        public int Index;
        public int ShopPosition;     // 游戏7槽ZonePosition (0-6)
        public int EntityId;         // 游戏实体ID, 用于覆盖层位置跟踪
        public double Score;
        public string CardName;
        public string Tribe;
        public int Tier;
        public string Reason;        // 评分原因
        public bool IsSpell;         // 是否是酒馆法术
        public float EconomyValue;   // 经济价值(0-1)
        public float CombatValue;    // 战力价值(0-1)
        public float GrowthValue;    // 成长潜力(0-1)
        public bool IsCoreCombo;     // 是否combo引擎(铜须/附魔师类)
        public bool HasClassification;
        public CardClassifier.CardRole PrimaryRole;
        public float PickRate;       // 抓取率(0-1)
        public bool IsTriple;       // 场面+手牌≥2张同卡 + 店里有第三张 = 三连机会
        public double Gev;          // P1影子: GEV金币等价值(StatTempo+EffectValue+Synergy+Triple)
        public double Net;          // P1影子: Net = Gev − 实付费用, 步骤A并行输出不改主Score
        public double StatTempo;    // P1影子: GEV分项-身材
        public double EffectValue;  // P1影子: GEV分项-文字效果
    }

    /// <summary>
    /// 决策引擎对外接口
    /// </summary>
    public interface IDecisionProvider
    {
        GameAction GetBestAction(GameState state);
        List<int> GetRecommendedBuyIndices(GameState state, int topN = 3);
        LevelUpSuggestion GetLevelUpSuggestion(GameState state);
    }

    /// <summary>
    /// 有限前瞻决策引擎。
    /// MVP: 深度=1 贪心搜索，枚举所有动作 → 模拟 → 评估价值 → 选最优。
    /// </summary>
    public class DecisionEngine : IDecisionProvider
    {
        private readonly ValueFunction _vf;
        private readonly Simulator _sim;
        private readonly FeatureExtractor _fe;
        private readonly ActionEnumerator _enumerator;
        private readonly CompLockDetector _compLock;
        private readonly TurnPhaseEngine _turnPhase;
        private readonly HeroPowerEngine _heroPower;
        private readonly SynergyEngine _synergy;
        private readonly PrizeSpellRegistry _prizeRegistry;
        private readonly ITrinketFactSource _trinketFactSource;
        private readonly TrinketRecommendationService _trinketRecommendations;
        public readonly AnomalyRegistry AnomalyReg;
        private readonly CombatSimulator _combat;
        private readonly CardClassifier _classifier;
        private readonly ContextDetector _contextDetector;
        private readonly StrategyAdjuster _strategyAdjuster;
        private readonly EffectValueTable _effectTable;   // P1: 本机事实派生GEV效果表

        // Track1(0709): EffectValueTable 嫁接 VF 作有界加法项(治 VF 不读文字效果盲区)。
        // bonus = min(λ×EffectValue, CAP)，仅随从(法术走 V1.3 差异化评分，防双算)。回滚=LAMBDA 置 0。
        // λ 默认 0.20(dev 网格校准); 评测/回滚可用环境变量 BOBCOACH_EFFECT_LAMBDA 覆盖(如 0 = 纯 VF 基线)。
        // 生产运行时(HDT 插件)不设该变量 → 恒为 0.20, 行为不变。用于 λ 终判 λ=0.20 vs λ=0 离线对比。
        private static readonly double EFFECT_BONUS_LAMBDA = ReadEffectLambda();
        private const double EFFECT_BONUS_CAP = 0.6;      // 硬上界=15%典型VF分(median≈3.95)

        private static double ReadEffectLambda()
        {
            try
            {
                var s = Environment.GetEnvironmentVariable("BOBCOACH_EFFECT_LAMBDA");
                double v;
                if (!string.IsNullOrEmpty(s) &&
                    double.TryParse(s, System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out v) && v >= 0)
                    return v;
            }
            catch { }
            return 0.20;
        }
        private ProfileEngine _profile;
        private MCTSSearch _mcts;
        public int LookaheadDepth { get; set; }
        public DecisionMode Mode { get; set; }
        public HeroPowerEngine HeroPower => _heroPower;
        internal ITrinketFactSource TrinketFactSource => _trinketFactSource;
        public ContextDetector ContextDetector => _contextDetector;
        public FeatureExtractor GetFeatureExtractor() { return _fe; }
        public ValueFunction GetValueFunction() { return _vf; }
        // 二步前瞻结果: 供HintLine显示完整操作序列
        public GameAction BestTwoStepAction { get; private set; }
        public GameState BestTwoStepState { get; private set; }
        private GameAction _bestTwoStepAction;
        private GameState _bestTwoStepState;

        public DecisionEngine()
            : this(new CachedTrinketFactSource(new HearthDbTrinketFactSource()))
        {
        }

        internal DecisionEngine(ITrinketFactSource trinketFactSource)
        {
            LookaheadDepth = 1;
            Mode = DecisionMode.Meta;
            _vf = new ValueFunction();
            _sim = new Simulator();
            _fe = new FeatureExtractor();
            _enumerator = new ActionEnumerator();
            _compLock = new CompLockDetector();
            _turnPhase = new TurnPhaseEngine();
            _heroPower = new HeroPowerEngine();
            _synergy = new SynergyEngine();
            _prizeRegistry = new PrizeSpellRegistry();
            _trinketFactSource = trinketFactSource;
            _trinketRecommendations = new TrinketRecommendationService(
                _trinketFactSource, new TrinketRuleEvaluator());
            AnomalyReg = new AnomalyRegistry(
                new CachedAnomalyDefinitionSource(
                    new HearthDbAnomalyFactSource(),
                    new AnomalyRuleEvaluator()));
            _combat = new CombatSimulator();
            _contextDetector = new ContextDetector();
            _strategyAdjuster = new StrategyAdjuster();
            _effectTable = new EffectValueTable();   // P1: 本机事实派生GEV效果表
            var semanticSource = new CachedCardSemanticSource(
                new HearthDbCardSemanticFactSource(),
                new CardSemanticRuleEvaluator());
            var classificationSource = new CachedCardClassificationSource(
                new HearthDbCardClassificationFactSource(),
                semanticSource,
                new CardClassificationRuleEvaluator());
            _classifier = new CardClassifier(classificationSource);
            _fe.SetCardSemanticSource(semanticSource);
            _compLock.SetCardSemanticSource(semanticSource);
            _synergy.SetCardSemanticSource(semanticSource);
            LoadResources();
            _mcts = new MCTSSearch(_sim, _enumerator, _fe, _vf);
        }

        private void LoadResources()
        {
            try
            {
                CardPoolSampler.Initialize(
                    new HearthDbCardPoolMembershipSource(),
                    new HearthDbCardPoolFactSource(),
                    null);
                // 设置HeroPower引用给SynergyEngine用于英雄-流派亲和计算
                _synergy.SetHeroPowerEngine(_heroPower);
            }
            catch
            {
                // 资源加载失败时引擎正常降级运行
            }
        }

        public void SetProfileEngine(ProfileEngine profile)
        {
            _profile = profile;
        }

        /// <summary>机制驱动判断：某张卡是否为经济卡（跨版本通用）</summary>
        public bool IsEconomyCard(string cardId)
        {
            return _classifier != null && _classifier.IsEconomyCard(cardId);
        }

        // ── IDecisionProvider ──

        /// <summary>
        /// 获取当前回合最优动作（深度=1 贪心）。
        /// 返回 null 表示没有可行动作或不在对局中。
        /// </summary>
        public GameAction GetBestAction(GameState state)
        {
            if (state == null || !state.GameActive) return null;
            _bestTwoStepAction = null; _bestTwoStepState = null;
            BestTwoStepAction = null; BestTwoStepState = null;

            var actions = _enumerator.Enumerate(state, state.HeroCardId);
            if (actions.Count == 0) return null;
            var effectiveRules = state.EffectiveRules ?? EffectiveGameRules.Default;

            // ── 混合决策规则层（28/28 基准校准 v1.1） ──

            int shopMax = MaxShopTier(state);
            int boardMax = MaxBoardTier(state);
            bool shopGood = ShopImprovesBoard(state);
            int upCost = GetEffectiveUpgradeCostOrUnavailable(state);
            int maxTavernTier = state.EffectiveRules != null
                ? state.EffectiveRules.MaxTavernTier : 6;
            float boardPwr = _fe.ComputeBoardPower(state.BoardMinions);
            // 升本金币合理性: 升本后至少留1金(除非满场无法买牌)
            bool canUpgrade = upCost != int.MaxValue && state.TavernTier < maxTavernTier
                && state.Gold >= upCost
                && (state.Gold - upCost >= 1 || state.BoardMinions.Count >= state.MaxBoardSlots);

            // ══ 情境检测 (v2.0自适应层) ══
            var contextResult = _contextDetector.Detect(state, _fe);
            var heroStrat = _heroPower.GetStrategy(state.HeroCardId);
            var adjusted = _strategyAdjuster.GetAdjustedStrategy(contextResult, _vf.Weights, heroStrat);

            // DESPERATE模式: 阻止升本
            if (adjusted.BlockLevelUp)
            {
                actions.RemoveAll(a => a.Type == ActionType.Upgrade);
            }

            // ── R0: 激进升本曲线 (核心修复: 升本优先于买牌) ──
            // 期望曲线: T2升2(4费), T5升3(7费), T7升4(9费), T9升5, T10+升6
            int expectedLv = state.Turn <= 1 ? 1 : state.Turn <= 3 ? 2 : state.Turn <= 5 ? 3
                : state.Turn <= 7 ? 4 : state.Turn <= 8 ? 5 : 6;
            int tierGap = expectedLv - state.TavernTier;
            bool boardFullR0 = state.BoardMinions.Count >= state.MaxBoardSlots;
            if (canUpgrade && state.TavernTier < maxTavernTier && tierGap >= 1)
            {
                // 等级落后≥2: 即使血量低也要升本 (高本牌=战力倍增器)
                // DESPERATE时不执行此规则
                if (tierGap >= 2 && contextResult.Type != SituationType.DESPERATE)
                {
                    foreach (var a in actions)
                        if (a.Type == ActionType.Upgrade) return a;
                }
                // 等级落后1级 + (板面≥3随从 或 血量>20 或 满场) → 升本
                if (state.BoardMinions.Count >= 3 || state.Health > adjusted.DangerHp || boardFullR0)
                {
                    foreach (var a in actions)
                        if (a.Type == ActionType.Upgrade) return a;
                }
            }

            // R1: 血量≤危险线 → 优先买怪保命，绝境时允许低价升本翻盘
            if (state.Health <= adjusted.DangerHp)
            {
                GameAction bestBuy = null;
                double bestBuyVal = double.NegativeInfinity;
                foreach (var a in actions)
                {
                    if (a.Type != ActionType.BuyMinion) continue;
                    var ns = _sim.Simulate(state, a);
                    var f = _fe.Extract(ns);
                    double v = _vf.Evaluate(f);
                    if (v > bestBuyVal) { bestBuyVal = v; bestBuy = a; }
                }
                if (bestBuy != null) return bestBuy;

                // 绝境升本: 升本费≤3且酒馆等级显著落后 → 升本来翻盘
                bool r1BoardFull = state.BoardMinions.Count >= state.MaxBoardSlots;
                if ((upCost <= 3 || r1BoardFull) && state.Gold >= upCost && state.BoardMinions.Count >= 2
                    && state.TavernTier < 5)
                {
                    int expTierR1 = Math.Min(6, state.Turn / 3 + 1);
                    if (state.TavernTier < expTierR1 || r1BoardFull)
                    {
                        foreach (var a in actions)
                            if (canUpgrade && a.Type == ActionType.Upgrade) return a;
                    }
                }
                // 卖弱换强: 低血量时仍然可以优化板面
                if (state.BoardMinions.Count >= 3 && state.Gold >= 2)
                {
                    int weakIdx = FindWeakestBoardMinion(state, null);
                    if (weakIdx >= 0 && state.Gold + 1 >= 3)
                    {
                        int bestBuyIdx = FindBestShopReplace(state, state.BoardMinions[weakIdx]);
                        if (bestBuyIdx >= 0)
                        {
                            foreach (var a in actions)
                                if (a.Type == ActionType.SellMinion && a.TargetIndex == weakIdx)
                                    return a;
                        }
                    }
                }
                // 彻底没钱也没升本机会 → 挂机攒钱
                return null;
            }

            // ── A(#3, fable5 Opt1): rated 饰品优先级 ──
            // 位置=保命(R1)/落后≥2必升(R0)之后、升本曲线(R2+)与所有买卖刷新之前。
            // 序: 保命 > 落后必升 > rated饰品 > 升本曲线/买卖刷新/尾部加权/MCTS。
            // 饰品仅T6/T9出现; 无rated时 helper 返null天然休眠; early-return 使尾部MCTS对饰品动作不可达。
            {
                var ratedTrinketAction = TryGetRatedTrinketAction(state);
                if (ratedTrinketAction != null) return ratedTrinketAction;
            }

            // R2: T2-T3 1本 → 升2本 (84局校准: 玩家T2升2本)
            if (state.TavernTier == 1 && state.Turn >= 2 && state.Turn <= 3)
            {
                int upCostR2 = GetEffectiveUpgradeCostOrUnavailable(state);
                if (state.Gold >= upCostR2 && state.BoardMinions.Count >= 1)
                {
                    foreach (var a in actions)
                        if (canUpgrade && a.Type == ActionType.Upgrade) return a;
                }
            }

            // R2.5: T4-T5 2本 → 升3本 (玩家校准: T5升3本, 板面≥3)
            // 416局校准: 店有>=2张高质量卡时降低升本优先级, 真人选择先买牌
            if (state.TavernTier == 2 && state.Turn >= 4 && state.Turn <= 5
                && state.BoardMinions.Count >= 3)
            {
                int upCostR25 = GetEffectiveUpgradeCostOrUnavailable(state);
                int goodShopCount = CountHighQualityShopCards(state, state.TavernTier);
                bool shopTooGoodToSkip = goodShopCount >= 2 && state.Gold >= 3;
                if (state.Gold >= upCostR25 && canUpgrade && !shopTooGoodToSkip)
                {
                    foreach (var a in actions)
                        if (a.Type == ActionType.Upgrade) return a;
                }
            }

            // R2.7: T6-T7 3本 → 升4本 (玩家校准: T7升4本, 板面≥4)
            if (state.TavernTier == 3 && state.Turn >= 6 && state.Turn <= 7
                && state.BoardMinions.Count >= 4)
            {
                int upCostR27 = GetEffectiveUpgradeCostOrUnavailable(state);
                if (state.Gold >= upCostR27 && canUpgrade)
                {
                    foreach (var a in actions)
                        if (a.Type == ActionType.Upgrade) return a;
                }
            }

            // R2.8: T8-T10 4本 → 升5本 (玩家校准: T10升5本, 板面≥5)
            if (state.TavernTier == 4 && state.Turn >= 8 && state.Turn <= 10
                && state.BoardMinions.Count >= 5)
            {
                int upCostR28 = GetEffectiveUpgradeCostOrUnavailable(state);
                if (state.Gold >= upCostR28 && canUpgrade)
                {
                    foreach (var a in actions)
                        if (a.Type == ActionType.Upgrade) return a;
                }
            }

            // R-spell-early: 前6回合经济法术 → 抢节奏优先抓取
            if (state.Turn <= 6 && state.ShopMinions != null)
            {
                for (int si = 0; si < state.ShopMinions.Count; si++)
                {
                    var sp = state.ShopMinions[si];
                    if (!sp.IsSpell || sp.CardId == null) continue;
                    int spellCost = GameRuleEvaluator.GetPurchaseCost(
                        state, sp, state.HeroCardId, effectiveRules);
                    if (state.Gold < spellCost) continue;
                    string cid = sp.CardId;

                    // 钻探原油(BG28_805): T2 cost3, 永久提升金上限+1 → 长效收益最高
                    if (cid.Contains("BG28_805"))
                    {
                        foreach (var a in actions)
                            if (a.Type == ActionType.BuySpell && a.TargetIndex == si) return a;
                    }
                    // 拼命发掘(BG28_571): T2 cost3(HP支付), 获得1金 → 血换节奏
                    if (cid.Contains("BG28_571") && state.Health > 20)
                    {
                        foreach (var a in actions)
                            if (a.Type == ActionType.BuySpell && a.TargetIndex == si) return a;
                    }
                    // 慎重投资(BG28_800): T3 cost1, 下回合+2金 → 100%ROI
                    if (cid.Contains("BG28_800") && state.Gold >= spellCost + 3)
                    {
                        foreach (var a in actions)
                            if (a.Type == ActionType.BuySpell && a.TargetIndex == si) return a;
                    }
                    // 主厨甄选(BG28_518): T2 cost2, 定向找同族 → 板面有随从时拿
                    if (cid.Contains("BG28_518") && state.BoardMinions.Count >= 1 && state.Gold >= spellCost + 3)
                    {
                        foreach (var a in actions)
                            if (a.Type == ActionType.BuySpell && a.TargetIndex == si) return a;
                    }
                    // 酒馆币(BG28_810): T1 cost1, 直接+1金 → 免费刷新/补费
                    if (cid.Contains("BG28_810") && state.Gold >= spellCost + 2)
                    {
                        foreach (var a in actions)
                            if (a.Type == ActionType.BuySpell && a.TargetIndex == si) return a;
                    }
                }
            }

            // R-time: 回合到达标准升本窗口 → 无条件按时升本
            // (5000局数据: Heuristic仅凭按时升本即碾压复杂规则, 核心逻辑=时机到了就升)
            // v1.5修复: 升5本(T8)需板面战力≥2.0, 避免升本后空窗暴毙
            int timeNextTier = Math.Min(state.TavernTier + 1, 6);
            if (FeatureExtractor.StandardLevelTurn.ContainsKey(timeNextTier))
            {
                int stdTurn = FeatureExtractor.StandardLevelTurn[timeNextTier];
                if (state.Turn >= stdTurn && state.BoardMinions.Count >= 1
                    && state.Gold >= upCost)
                {
                    // T5+升本需板面战力检查
                    if (timeNextTier >= 5)
                    {
                        if (FeatureExtractor.LevelUpSafePower.ContainsKey(timeNextTier))
                        {
                            double safePwr = FeatureExtractor.LevelUpSafePower[timeNextTier];
                            if (boardPwr >= safePwr * 0.8)
                            {
                                foreach (var a in actions)
                                    if (canUpgrade && a.Type == ActionType.Upgrade) return a;
                            }
                            // 板面不足: 延迟升本, 优先买牌稳场
                        }
                    }
                    else
                    {
                        foreach (var a in actions)
                            if (canUpgrade && a.Type == ActionType.Upgrade) return a;
                    }
                }
            }

            // R3: 满场+店无高本 → 升本
            if (state.BoardMinions.Count >= state.MaxBoardSlots && shopMax <= state.TavernTier)
            {
                foreach (var a in actions)
                    if (canUpgrade && a.Type == ActionType.Upgrade) return a;
            }

            // R3.5: 板面≥5张+店有更强牌+卖弱够钱买 → 卖最弱腾位
            // (438局数据: 赢家板面≥5时开始换血, 非满场才换)
            if (state.BoardMinions.Count >= FeatureExtractor.PivotMinBoardSize && state.Gold >= 2)
            {
                int weakIdx = FindWeakestBoardMinion(state, null);
                if (weakIdx >= 0 && state.Gold + 1 >= 3)
                {
                    int bestBuyIdx = FindBestShopReplace(state, state.BoardMinions[weakIdx]);
                    if (bestBuyIdx >= 0)
                    {
                        // 有值得换的牌，推荐卖最弱
                        foreach (var a in actions)
                            if (a.Type == ActionType.SellMinion && a.TargetIndex == weakIdx)
                                return a;
                    }
                }
            }

            // R4: 场面达标+店差 → 激进升本 (使用P10阈值, 因R-time已处理标准窗口)
            // (标准窗口由R-time无条件处理, R4负责提前升本场景)
            bool earlyWindow = state.TavernTier < maxTavernTier
                && FeatureExtractor.StandardLevelTurn.ContainsKey(state.TavernTier + 1)
                && state.Turn < FeatureExtractor.StandardLevelTurn[state.TavernTier + 1];
            if (earlyWindow)
            {
                double r4Threshold = FeatureExtractor.LevelUpAggressivePower.ContainsKey(state.TavernTier + 1)
                    ? FeatureExtractor.LevelUpAggressivePower[state.TavernTier + 1] : 1.5;
                int r4BoardNeed = FeatureExtractor.LevelUpAggressiveBoardSize.ContainsKey(state.TavernTier + 1)
                    ? FeatureExtractor.LevelUpAggressiveBoardSize[state.TavernTier + 1] : 3;
                if (boardPwr >= r4Threshold && state.BoardMinions.Count >= r4BoardNeed
                    && shopMax <= state.TavernTier)
                {
                    foreach (var a in actions)
                        if (canUpgrade && a.Type == ActionType.Upgrade) return a;
                }
            }

            // R4.5: 落后对手→升本找核心翻盘 (放宽门槛: 健康>8, 留1金即可)
            // (438局数据: 赢家常在BP落后30%时升5/6本, 翻盘窗口2.8回合)
            float avgOppPwr = _fe.ComputeAvgOpponentPower(state.Opponents);
            if (avgOppPwr > 0 && boardPwr < avgOppPwr * 0.7f
                && state.TavernTier < maxTavernTier && state.Health > 8
                && state.Gold >= upCost && state.Turn >= 4)
            {
                if (state.Gold - upCost >= 1)
                {
                    foreach (var a in actions)
                        if (canUpgrade && a.Type == ActionType.Upgrade) return a;
                }
            }

            // R-tech: 绝望战力差距 → 对手2x我方以上+中期→触发Tech Pivot
            // (438局数据: 淘汰前战力比<0.7触发翻盘, <0.5触发绝望模式)
            bool desperateGap = false;
            float myRawStats = SumAttackHealth(state.BoardMinions);
            float oppRawStats = SumOpponentStats(state.Opponents);
            // 板面战力比 < 0.5 (对手2x以上) 且血量尚可 → 常规买牌无法追赶
            float oppBoardPwr = _fe.ComputeAvgOpponentPower(state.Opponents);
            if (oppRawStats > 0 && boardPwr > 0 && oppBoardPwr > 0)
            {
                float powerRatio = boardPwr / oppBoardPwr;
                if (powerRatio < FeatureExtractor.DesperatePowerRatio && state.Health > 10 && state.Turn >= 6)
                {
                    desperateGap = true;
                }
            }
            if (oppRawStats > 0 && myRawStats < oppRawStats * 0.25f && state.Health > 10 && state.Turn >= 7)
            {
                desperateGap = true;
                // 绝望差距下，刷新优先级高于买普通随从
                if (state.Gold >= 1 || state.FreeRefreshCount > 0)
                {
                    // 检查是否有剧毒/圣盾卡可买，没有则刷新
                    bool hasTechInShop = ShopHasTechCard(state);
                    // 校准: 真实玩家几乎不刷新(0%准确率), 只在明确需要时推荐
                    // 仅在免费刷新 + 绝望差距 + 场面满时推荐刷新
                    if (!hasTechInShop && state.FreeRefreshCount > 0 && state.BoardMinions.Count >= 6)
                    {
                        foreach (var a in actions)
                            if (a.Type == ActionType.Refresh) return a;
                    }
                }
            }

            // R-triple-timing: 三连时序规则 — 真人数据91%先升本再合金
            // 当店里有三连可完成 + 当前能升本时, 优先升本, 下回合再合金
            int tripleIdx = FindTripleInShop(state);
            if (tripleIdx >= 0 && canUpgrade && state.TavernTier < maxTavernTier)
            {
                foreach (var a in actions)
                    if (a.Type == ActionType.Upgrade) return a;
            }

            // R5: 三连检测 → 拿牌凑三连 + 评估发现奖励价值
            if (tripleIdx >= 0 && state.Gold >= 3)
            {
                // 三连价值 = 金色卡加成(1.5~1.8x) + 发现奖励(高一级卡牌期望值)
                double tripleValue = EstimateTripleRewardValue(state, tripleIdx);
                if (tripleValue > 0.2)  // 任何三连都值得拿(金色+发现奖励)
                {
                    foreach (var a in actions)
                        if (a.Type == ActionType.BuyMinion && a.TargetIndex == tripleIdx)
                            return a;
                }
            }

            // R6: 店有好货(≥当前本+2) → 优先买高本卡
            if (shopMax >= state.TavernTier + 2 && state.Gold >= 3)
            {
                GameAction bestBuy = null;
                double bestBuyVal = double.NegativeInfinity;
                foreach (var a in actions)
                {
                    if (a.Type != ActionType.BuyMinion) continue;
                    var ns = _sim.Simulate(state, a);
                    var f = _fe.Extract(ns);
                    double v = _vf.Evaluate(f);
                    if (v > bestBuyVal) { bestBuyVal = v; bestBuy = a; }
                }
                if (bestBuy != null) return bestBuy;
            }

            // R7: 店有比场面更强的牌 + 场面未满 + 非早期必升本回合 → 优先买
            if (shopGood && (state.BoardMinions.Count < 7 || state.Turn >= 8) && state.Gold >= 3)
            {
                bool lowTierUpgrade = (state.TavernTier <= 2 && state.Turn <= 4 && upCost <= 4);
                if (!lowTierUpgrade || shopMax >= state.TavernTier + 2)
                {
                    GameAction bestBuy = null;
                    double bestBuyVal = double.NegativeInfinity;
                    foreach (var a in actions)
                    {
                        if (a.Type != ActionType.BuyMinion) continue;
                        var ns = _sim.Simulate(state, a);
                        var f = _fe.Extract(ns);
                        double v = _vf.Evaluate(f);
                        if (v > bestBuyVal) { bestBuyVal = v; bestBuy = a; }
                    }
                    if (bestBuy != null) return bestBuy;
                }
            }

            // R8: 店没好货(≤当前本)+健康+升级后有余钱 → 升本（店不能改善场面时）
            if (shopMax > 0 && shopMax <= state.TavernTier
                && state.Health > 15 && (state.Gold - upCost) >= 1 && !shopGood)
            {
                // 店极差(全<当前本)且升级余钱<3 → 优先刷新
                if (!(shopMax < state.TavernTier && (state.Gold - upCost) < 3))
                {
                    foreach (var a in actions)
                        if (canUpgrade && a.Type == ActionType.Upgrade) return a;
                }
            }

            // R9: 店普通(==当前本)+场面弱+余钱充裕 → 升本（店不能改善场面时）
            if (shopMax > 0 && shopMax == state.TavernTier
                && boardPwr < 1.5f && (state.Gold - upCost) >= 2 && !shopGood)
            {
                foreach (var a in actions)
                    if (canUpgrade && a.Type == ActionType.Upgrade) return a;
            }

            // ── V1.2 策略修饰层 ──
            var compLockResult = _compLock.Detect(state);
            var phaseResult = _turnPhase.Evaluate(state);
            // heroStrat already computed at context detection block
            float[] phaseWeights = _turnPhase.GetPhaseAdjustedWeights(_vf.Weights, phaseResult);
            // ══ V2.0: 情境权重叠加(在阶段权重之上) ══
            float[] adjustedWeights = new float[phaseWeights.Length];
            for (int wi = 0; wi < phaseWeights.Length; wi++)
                adjustedWeights[wi] = phaseWeights[wi] + (adjusted.Weights[wi] - _vf.Weights[wi]);
            // ── 后期模式(T7+): 动态调整权重 — 提升tavern_value, 降低board_power ──
            // T8+决策匹配率趋零原因: board_power在后期饱和(板面已有5-7高星随从),
            // can_upgrade在T6后消失, tavern_value(0.09)严重低估T5-T6核心卡价值
            bool lateGameWeightBoost = state.Turn >= 7 && state.TavernTier >= 5;
            float[] evalWeights = adjustedWeights;
            if (lateGameWeightBoost)
            {
                evalWeights = new float[adjustedWeights.Length];
                Array.Copy(adjustedWeights, evalWeights, adjustedWeights.Length);
                evalWeights[FeatureExtractor.F_TAVERN_VALUE] *= 1.8f;   // 0.09 → 0.162
                evalWeights[FeatureExtractor.F_BOARD_POWER] *= 0.7f;     // 0.225 → 0.158
            }

            // ── V(s) 贪心搜索 (V1.3: top-3跟踪用于二步前瞻) ──
            double bestValue = double.NegativeInfinity;
            GameAction bestAction = null;
            var topActions = new List<(GameAction Action, double Value, GameState NextState)>();
            bool boardFull = state.BoardMinions.Count >= state.MaxBoardSlots;

            foreach (var action in actions)
            {
                var nextState = _sim.Simulate(state, action);
                var features = _fe.Extract(nextState);
                double value = _vf.Evaluate(features, evalWeights);

                // belt-and-suspenders: gold=0且无免费刷新→刷新不可能
                if (action.Type == ActionType.Refresh && state.Gold < 1 && state.FreeRefreshCount == 0)
                    value *= 0.0;

                // ── 出售惩罚 (22局实测: Top4卖26.2%Bot4卖10.3%, 换血是优势策略) ──
                if (action.Type == ActionType.SellMinion && action.CardId != null)
                {
                    // 基础惩罚: 0.7(原0.5) — 提升卖牌意愿, 配合shouldSell质量门控
                    double sellMultiplier = 0.7;

                    if (!boardFull)
                        sellMultiplier *= 0.5;  // 板面未满: 卖弱买强是合理策略

                    if (IsUniqueTribeMinion(state, action.TargetIndex))
                        sellMultiplier *= 0.5;  // 唯一同族: 破坏种族协同

                    // 经济卡无额外惩罚，其他卡使用标准出售系数。
                    if (_classifier.IsEconomyCard(action.CardId))
                        sellMultiplier *= 1.0;   // 经济卡出售不额外惩罚
                    else
                        sellMultiplier *= 0.85;  // 过渡战力: 标准出售(0.85, 原0.8)

                    value *= sellMultiplier;
                }

                // ── 后期出售: 适度惩罚, shouldSell门控已做严格质量过滤 ──
                if (action.Type == ActionType.SellMinion && state.Turn >= 8)
                    value *= 0.8f;

                // ── 满场处理 (P0修复): 不再无条件推出售、惩罚刷新 ──
                if (boardFull)
                {
                    if (action.Type == ActionType.Refresh)
                    {
                        // 满场刷新不惩罚: 找核心卡/科技卡是正常操作
                        // 仅当店已有好货时略微降权(优先买而非刷)
                        if (shopGood) value *= 0.90;
                        else value *= 1.05; // 店不好时刷新是正确选择
                    }
                    else if (action.Type == ActionType.SellMinion)
                    {
                        // 出售仅在商店有明确更强卡时才加权
                        bool hasBetterShopCard = false;
                        if (action.TargetIndex >= 0 && action.TargetIndex < state.BoardMinions.Count)
                        {
                            var sold = state.BoardMinions[action.TargetIndex];
                            float soldPower = sold.Attack * 0.6f + sold.Health * 0.4f;
                            foreach (var sc in state.ShopMinions)
                            {
                                if (sc.IsSpell) continue;
                                float shopPower = sc.Attack * 0.6f + sc.Health * 0.4f;
                                if (shopPower > soldPower * 1.1f || sc.Tier > sold.Tier)
                                { hasBetterShopCard = true; break; }
                            }
                        }
                        value *= hasBetterShopCard ? 1.15 : 0.85;
                    }
                }

                // ── 刷新相关 ──
                if (action.Type == ActionType.Refresh && state.FreeRefreshCount > 0 && !boardFull)
                    value *= (state.FreeRefreshCount > 5) ? 1.03 : 1.1;

                // ── 早期刷新惩罚: T1-T5每块钱都关键, T1-T2绝对不刷 ──
                if (action.Type == ActionType.Refresh && state.Turn <= 2 && state.FreeRefreshCount == 0)
                    value *= 0.0;       // T1-T2: 绝对禁止付费刷新
                if (action.Type == ActionType.Refresh && state.Turn == 3
                    && state.Gold - 1 < 3 && state.FreeRefreshCount == 0)
                    value *= 0.05f;     // T3: 刷后买不起=几乎禁止
                if (action.Type == ActionType.Refresh && state.Turn >= 3 && state.Turn <= 5
                    && state.FreeRefreshCount == 0)
                    value *= 0.15f;     // T3-T5: 强惩罚(only free refresh acceptable)
                // T1-T2 额外防护: 如果还有可买的随从且金币≥3, 加倍抑制Refresh
                if (action.Type == ActionType.Refresh && state.Turn <= 2 && state.Gold >= 3
                    && state.ShopMinions.Count > 0 && state.BoardMinions.Count < state.MaxBoardSlots)
                    value *= 0.0;

                // ── V1.3 刷新期望增益: 店差→刷新更值, 店好→刷新不值 ──
                if (action.Type == ActionType.Refresh && state.FreeRefreshCount == 0)
                {
                    double refreshGain = ProbabilityCalculator.EstimateRefreshGain(
                        state.TavernTier, state.ShopMinions);
                    // 当前店比期望差→刷新加分; 当前店好→刷新降分
                    if (refreshGain > 0.25)
                        value *= 1.25;  // 店很差, 刷新很值
                    else if (refreshGain > 0.10)
                        value *= 1.10;  // 店略差, 刷新有价值
                    else if (refreshGain < -0.15)
                        value *= 0.80;  // 店不错, 刷新不值
                }

                // ── 刷新校准: 店有好牌时不刷 (真人匹配率仅31%) ──
                if (action.Type == ActionType.Refresh && state.FreeRefreshCount == 0)
                {
                    float bestShopNorm = _fe.GetBestTavernScore(state.ShopMinions);
                    // 店里有>=1张高质量卡(score>=0.5) → 刷新分数×0.5
                    if (bestShopNorm >= 0.5f)
                        value *= 0.5f;
                    // 店里有>=2张可用卡(tier>=本-1) → 刷新分数×0.6
                    else if (state.ShopMinions != null)
                    {
                        int buyableCount = 0;
                        foreach (var sc in state.ShopMinions)
                        {
                            if (sc.IsSpell) continue;
                            if (sc.Tier >= Math.Max(1, state.TavernTier - 1) && state.Gold >= 3)
                                buyableCount++;
                        }
                        if (buyableCount >= 2) value *= 0.6f;
                    }
                }

                // ── 后期付费刷新: 锁定后店无匹配更应刷(22局实测Top4刷20.2%Bot4刷15.4%) ──
                if (action.Type == ActionType.Refresh && lateGameWeightBoost && state.FreeRefreshCount == 0)
                {
                    bool compLocked = compLockResult.State != LockState.None;
                    bool shopHasMatchingCards = false;
                    if (compLocked && state.ShopMinions != null)
                    {
                        var tribeHint = compLockResult.DominantTribe;
                        if (!string.IsNullOrEmpty(tribeHint))
                        {
                            foreach (var sm in state.ShopMinions)
                                if (sm.Tribe != null && MinionData.TribeMatches(sm.Tribe, tribeHint))
                                { shopHasMatchingCards = true; break; }
                        }
                    }
                    if (compLocked && !shopHasMatchingCards)
                        value *= 1.05f; // 流派锁+店无匹配 → 刷的价值更高
                    else
                        value *= 0.85f; // 加权(原0.7), 避免完全扼杀刷新技术
                }

                // ── 高本买低本惩罚 ──
                if (action.Type == ActionType.BuyMinion && state.TavernTier >= 4
                    && action.TargetIndex < state.ShopMinions.Count
                    && state.ShopMinions[action.TargetIndex].Tier <= state.TavernTier - 2)
                    value *= 0.7;

                // ── 三连加分(含发现奖励) ──
                if (action.Type == ActionType.BuyMinion && tripleIdx >= 0
                    && action.TargetIndex == tripleIdx) value *= 1.6;  // 1.5x金色+0.1x发现奖励

                // ── 刷新场景修正 ──
                if (action.Type == ActionType.Refresh && shopMax > 0
                    && shopMax < state.TavernTier && !boardFull) value *= 1.15;
                if (action.Type == ActionType.Refresh && shopGood) value *= 0.85;

                // ── 买强牌加分 ──
                if (action.Type == ActionType.BuyMinion && state.ShopMinions != null
                    && action.TargetIndex < state.ShopMinions.Count
                    && state.ShopMinions[action.TargetIndex].Tier > boardMax)
                    value *= 1.1;

                // ── 后期(T7+)T5+买牌加分: 核心卡价值远超普通卡 ──
                if (lateGameWeightBoost && action.Type == ActionType.BuyMinion
                    && action.TargetIndex < state.ShopMinions.Count
                    && state.ShopMinions[action.TargetIndex].Tier >= 5)
                    value *= 1.25f;

                // ── V1.5 绝望差距Tech Pivot: 剧毒/圣盾卡大幅加分 ──
                if (desperateGap && action.Type == ActionType.BuyMinion
                    && action.TargetIndex < state.ShopMinions.Count)
                {
                    var techCard = state.ShopMinions[action.TargetIndex];
                    if (techCard.Poisonous)
                        value *= 3.0;          // 剧毒：攻防双杀，最高优先级
                    else if (techCard.Venomous)
                        value *= 2.0;          // 烈毒：仅攻击侧秒杀
                    else if (techCard.DivineShield)
                        value *= 1.6;          // 圣盾配合剧毒
                    else if (techCard.Reborn)
                        value *= 1.3;
                }
                if (desperateGap && action.Type == ActionType.Refresh && state.Turn >= 7)
                    value *= 1.8;  // 绝望时刷新优先级大幅提升（找tech卡），T7以上才允许
                if (desperateGap && action.Type == ActionType.SellMinion
                    && FindDeadWeight(state) == action.TargetIndex)
                    value *= 1.5;  // 卖死重随从来刷新

                // ── V1.3 Synergy: 场协同分修正 ──
                if (action.Type == ActionType.BuyMinion
                    && action.TargetIndex < state.ShopMinions.Count)
                {
                    var shopCard = state.ShopMinions[action.TargetIndex];
                    var synergyScore = _synergy.ScoreCard(shopCard.CardId, shopCard.Tribe,
                        state, state.HeroCardId);
                    if (synergyScore.TotalScore > 0.1f)
                        value *= (1.0f + synergyScore.TotalScore * 0.25f);
                }

                // ── V1.2 CompLock: 购买流派匹配修正 ──
                if (action.Type == ActionType.BuyMinion && compLockResult.State != LockState.None
                    && action.TargetIndex < state.ShopMinions.Count)
                {
                    string shopTribe = state.ShopMinions[action.TargetIndex].Tribe;
                    float lockMult = _compLock.GetBuyMultiplier(shopTribe, compLockResult);
                    if (lockMult != 1.0f) value *= lockMult;
                }

                // ── V1.2 HeroPower: 英雄特定修正 ──
                if (action.Type == ActionType.Upgrade)
                    value += heroStrat.UpgradeValueBias + adjusted.LevelUpBias;
                if (action.Type == ActionType.Refresh)
                    value += heroStrat.RefreshValueBias;
                if (action.Type == ActionType.BuyMinion)
                    value += heroStrat.BuyValueBias;
                if (action.Type == ActionType.UseHeroPower)
                    value += _heroPower.GetStrategyForPower(
                        state.HeroCardId, action.CardId).PowerValueBias;
                if (action.Type == ActionType.SellMinion)
                    value += adjusted.SellBias;

                // ── V2.0 Context: tech卡调整 ──
                if (desperateGap && action.Type == ActionType.BuyMinion
                    && action.TargetIndex < state.ShopMinions.Count)
                {
                    var techCard = state.ShopMinions[action.TargetIndex];
                    if (techCard.Poisonous) value *= adjusted.TechCardMultiplier;
                    else if (techCard.Venomous) value *= adjusted.TechCardMultiplier * 0.7f;
                    else if (techCard.DivineShield) value *= adjusted.TechCardMultiplier * 0.5f;
                }

                // ── V1.4 CombatSim: 战斗风险评估 (V1.3: OpponentModel回退) ──
                if ((action.Type == ActionType.Upgrade || action.Type == ActionType.SellMinion)
                    && state.Health <= 25)
                {
                    int estimatedDamage;
                    var oppBoard = state.Opponents?.SelectMany(o => o.BoardMinions).ToList();
                    if (oppBoard != null && oppBoard.Count > 0)
                    {
                        // HDT 有对手板面数据: 全模拟
                        var oppData = state.Opponents?.FirstOrDefault();
                        var combatResult = _combat.Simulate(nextState.BoardMinions,
                            oppBoard, nextState.TavernTier,
                            oppData?.TavernTier ?? 1,
                            playerHeroCardId: nextState.HeroCardId,
                            opponentHeroCardId: oppData?.HeroCardId,
                            playerHand: nextState.HandMinions,
                            playerTrinkets: nextState.ActiveTrinketContext,
                            opponentTrinkets: ActiveTrinketContext.Empty);
                        estimatedDamage = combatResult.PlayerWon ? 0 : combatResult.DamageDealtToPlayer;
                    }
                    else
                    {
                        // 无对手数据: 启发式模型估算
                        double myPower = _fe.ComputeBoardPower(nextState.BoardMinions);
                        double oppPower = OpponentModel.EstimateOpponentPower(state.Turn);
                        if (myPower >= oppPower * 1.2)
                            estimatedDamage = 0; // 明显强于典型对手
                        else
                            estimatedDamage = OpponentModel.EstimateDamage(state.Turn,
                                myPower < oppPower * 0.5 ? 1.4 : 1.0); // 战力落后→伤害更高
                    }
                    if (estimatedDamage > 0)
                    {
                        float hpAfter = state.Health - estimatedDamage;
                        if (hpAfter <= 0)
                            value *= 0.1;   // 致命伤害 → 几乎禁止
                        else if (hpAfter <= 7)
                            value *= 0.5;    // 进入斩杀线
                        else if (hpAfter <= 15 && state.Turn >= 8)
                            value *= 0.8;    // 后期低血量
                    }
                }

                // ── 冻结商店价值: 仅两条件 — 三连缺钱 或 核心牌缺钱 ──
                if (action.Type == ActionType.FreezeShop)
                {
                    int tripleInShop = FindTripleInShop(state);
                    bool hasTripleTarget = tripleInShop >= 0;
                    bool hasCompCoreUnaffordable = false;
                    if (compLockResult != null && compLockResult.State != LockState.None && state.Gold < 3)
                    {
                        string domTribe = compLockResult.DominantTribe ?? "";
                        hasCompCoreUnaffordable = state.ShopMinions.Any(s =>
                            !string.IsNullOrEmpty(s.Tribe) && !string.IsNullOrEmpty(domTribe)
                            && s.Tribe.Contains(domTribe));
                    }

                    if (state.Turn <= 3)
                        value *= 0.01f;  // T1-T3几乎从不冻结
                    else if (hasTripleTarget && state.Gold < 3)
                        value *= 1.5f;   // 三连缺钱 → 推荐冻结
                    else if (hasCompCoreUnaffordable)
                        value *= 1.3f;   // 核心牌缺钱 → 推荐冻结
                    else
                        value *= 0.01f;  // 其他情况不冻结
                }

                // ── 发现/三选一评估: 高星卡加分, 流派匹配加分 ──
                if (action.Type == ActionType.PickDiscover && state.DiscoverOptions != null)
                {
                    int idx = action.TargetIndex;
                    if (idx >= 0 && idx < state.DiscoverOptions.Count)
                    {
                        var opt = state.DiscoverOptions[idx];
                        int tier = opt.Tier > 0 ? opt.Tier : 1;
                        // 高星发现: T5+大幅加分, T4中等加分
                        if (tier >= 5) value *= 1.3;
                        else if (tier >= 4) value *= 1.15;
                        else if (tier >= 3) value *= 1.05;
                        // 流派匹配: 部落/名称匹配
                        string compDir = GetDomTribe(state);
                        if (!string.IsNullOrEmpty(compDir))
                        {
                            string dn = opt.TrinketName ?? "";
                            if (dn.Contains(compDir)) value *= 1.25;
                        }
                        // 引擎卡(铜须/瑞文等): 强烈推荐
                        string name = opt.TrinketName ?? "";
                        if (name.Contains("铜须") || name.Contains("瑞文") || name.Contains("达瑞尔"))
                            value *= 1.35;
                    }
                }

                // ── V1.5 经济卡抗败策略：预期战败时买理财卡 ──
                if (action.Type == ActionType.BuyMinion && _classifier.IsEconomyCard(action.CardId)
                    && state.Health > 15 && state.Turn >= 4)
                {
                    // 快速检查是否可能战败
                    float avgOpp = _fe.ComputeAvgOpponentPower(state.Opponents);
                    if (avgOpp > 0 && boardPwr < avgOpp * 0.8f)
                    {
                        // 预期战败 → 买理财卡，下回合出售获得金币爆发
                        value *= 1.25;
                    }
                }

                if (value > bestValue)
                {
                    bestValue = value;
                    bestAction = action;
                }
                // 维护 top-3 列表 (供二步前瞻使用)
                if (value > double.NegativeInfinity)
                {
                    topActions.Add((action, value, nextState));
                    if (topActions.Count > 3)
                    {
                        topActions.Sort((a, b) => b.Value.CompareTo(a.Value));
                        topActions.RemoveAt(topActions.Count - 1);
                    }
                }
            }

            // ── 搜索后刷新审核 (替代旧版"刷新0%准确率"硬覆盖) ──
            // 付费刷新在中后期是合法策略：找对子三连、搜核心卡、满场找提升
            if (bestAction != null && bestAction.Type == ActionType.Refresh
                && state.FreeRefreshCount == 0 && !desperateGap)
            {
                // 早期拦截: T5以下除非满场否则不付费刷新，前期每枚铸币都应买随从或升本
                bool earlyBoardFull = state.BoardMinions.Count >= state.MaxBoardSlots;
                if (state.Turn <= 5 && !earlyBoardFull && state.Gold >= 3)
                {
                    var fallbackBuy = actions
                        .Where(a => a.Type == ActionType.BuyMinion || a.Type == ActionType.BuySpell)
                        .OrderByDescending(a => a.TargetIndex < state.ShopMinions.Count
                            ? state.ShopMinions[a.TargetIndex].Tier : 0)
                        .FirstOrDefault();
                    if (fallbackBuy != null) return fallbackBuy;
                }
                // T1-T2终极兜底: fallbackBuy为null(无BuyMinion/BuySpell动作)→找任意非Refresh返回
                if (state.Turn <= 2 && state.Gold >= 3)
                {
                    var anyNonRefresh = actions.FirstOrDefault(a =>
                        a.Type != ActionType.Refresh && a.Type != ActionType.FreezeShop);
                    if (anyNonRefresh != null) return anyNonRefresh;
                }

                // 判断刷新是否有明确搜索目标
                bool hasPairOnBoard = false;
                var boardCardIds = new HashSet<string>();
                foreach (var bm in state.BoardMinions)
                {
                    if (!string.IsNullOrEmpty(bm.CardId) && !boardCardIds.Add(bm.CardId))
                        hasPairOnBoard = true; // 重复cardId=对子
                }

                bool lateGame = state.Turn >= 9;
                bool midGame = state.Turn >= 6;
                bool bf = state.BoardMinions.Count >= state.MaxBoardSlots;
                bool badShop = shopMax < state.TavernTier;
                bool compLocked = compLockResult != null && compLockResult.State != LockState.None;
                bool hasGoldToSpare = state.Gold >= 5; // 有富余金币可刷新

                // 允许付费刷新的条件（满足任一即可）：
                // 1. 有对子 + 中后期 → 追三连
                // 2. 后期 + 满场 → 找提升
                // 3. 后期 + 坏商店 + 有余钱 → 值得刷
                // 4. 流派锁定 + 中后期 → 找核心
                bool allowPaidRefresh = false;
                if (hasPairOnBoard && midGame) allowPaidRefresh = true;
                else if (lateGame && bf) allowPaidRefresh = true;
                else if (lateGame && badShop && hasGoldToSpare) allowPaidRefresh = true;
                else if (compLocked && lateGame) allowPaidRefresh = true;

                if (!allowPaidRefresh)
                {
                    var bestBuyAction = actions
                        .Where(a => a.Type == ActionType.BuyMinion || a.Type == ActionType.BuySpell)
                        .OrderByDescending(a => {
                            if (a.TargetIndex < state.ShopMinions.Count)
                                return state.ShopMinions[a.TargetIndex].Tier;
                            return 0;
                        }).FirstOrDefault();
                    if (bestBuyAction != null) return bestBuyAction;
                }
            }
            // 出售限制: 除非替换后有实质提升, 否则不推荐
            if (bestAction != null && bestAction.Type == ActionType.SellMinion)
            {
                // 卖→买替换价值检测: 店里有比出售卡明显更强的牌才推荐换
                int sellIdx = bestAction.TargetIndex;
                bool shouldSell = false;
                // 前置条件: 手牌有随从(非咒术/饰品)(可填位) 或 卖后金币≥3(能买店牌)
                int handMinionCount = state.HandMinions.Count(h => !h.IsSpell);
                bool canReplaceSlot = handMinionCount > 0 || state.Gold + 1 >= 3;
                // T6满级: 仅当店有更好牌+手牌可填位才卖(无升本路径无需攒金)
                if (state.TavernTier >= 6 && handMinionCount == 0) canReplaceSlot = false;
                if (canReplaceSlot && sellIdx >= 0 && sellIdx < state.BoardMinions.Count)
                {
                    var sold = state.BoardMinions[sellIdx];
                    // 不卖金色随从
                    if (sold.Golden) { shouldSell = false; goto sellDone; }
                    double soldPower = _fe.ComputeBoardPower(new List<MinionData> { sold });
                    double bestShopPower = 0;
                    int bestShopTier = 1;
                    foreach (var shop in state.ShopMinions)
                    {
                        double p = _fe.ComputeBoardPower(new List<MinionData> { shop });
                        if (p > bestShopPower) { bestShopPower = p; bestShopTier = shop.Tier; }
                    }
                    int soldTier = sold.Tier;
                    // 同星或更高星出售: 阈值提高到50%
                    double threshold = soldTier <= bestShopTier ? 1.50 : 1.25;
                    if (bestShopPower >= soldPower * threshold)
                        shouldSell = true;
                }
                sellDone:
                if (!shouldSell)
                {
                    // 回退到买牌或刷新
                    var bestBuy = actions.Where(a => a.Type == ActionType.BuyMinion).FirstOrDefault();
                    if (bestBuy != null) return bestBuy;
                    var bestRefresh = actions.Where(a => a.Type == ActionType.Refresh).FirstOrDefault();
                    if (bestRefresh != null) return bestRefresh;
                }
                else return bestAction;
            }

            // ══ V1.3 二步前瞻入主搜索: top-3动作 × 第二步穷举 ══
            const double TWO_STEP_DISCOUNT = 0.85f; // 第二步价值折扣 (与 rules.json 一致)
            const double TWO_STEP_THRESHOLD = 1.03;   // 需比单步高8%才覆盖
            double bestTwoStepCombined = double.NegativeInfinity;
            GameAction bestTwoStepStep1 = null;
            GameAction bestTwoStepStep2 = null;
            GameState bestTwoStepS1State = null;
            bool twoStepExplored = false;

            if (state.Gold >= 3) // 至少3金才有二步操作空间
            {
                topActions.Sort((a, b) => b.Value.CompareTo(a.Value));
                int explored = 0;
                foreach (var (a1, v1, s1) in topActions)
                {
                    if (explored >= 3) break;
                    if (a1.Type == ActionType.Upgrade || a1.Type == ActionType.PickTrinket) continue;
                    if (s1.Gold < 1) continue;

                    explored++;
                    var step2Actions = _enumerator.Enumerate(s1, state.HeroCardId);
                    double bestS2Val = double.NegativeInfinity;
                    GameAction bestS2 = null;

                    foreach (var a2 in step2Actions)
                    {
                        if (a2.Type == ActionType.FreezeShop) continue;
                        var s2 = _sim.Simulate(s1, a2);
                        var f2 = _fe.Extract(s2);
                        double v2 = _vf.Evaluate(f2, evalWeights);
                        if (a2.Type == ActionType.UseHeroPower)
                            v2 += _heroPower.GetStrategyForPower(
                                state.HeroCardId, a2.CardId).PowerValueBias;
                        if (v2 > bestS2Val) { bestS2Val = v2; bestS2 = a2; }
                    }

                    if (bestS2 != null)
                    {
                        twoStepExplored = true;
                        double combined = v1 + TWO_STEP_DISCOUNT * bestS2Val;
                        if (combined > bestTwoStepCombined)
                        {
                            bestTwoStepCombined = combined;
                            bestTwoStepStep1 = a1;
                            bestTwoStepStep2 = bestS2;
                            bestTwoStepS1State = s1;
                        }
                    }
                }

                // 二步组合显著优于单步时覆盖
                if (twoStepExplored && bestTwoStepStep1 != null
                    && bestTwoStepCombined > bestValue * TWO_STEP_THRESHOLD)
                {
                    _bestTwoStepAction = bestTwoStepStep2;
                    _bestTwoStepState = bestTwoStepS1State;
                    BestTwoStepAction = _bestTwoStepAction;
                    BestTwoStepState = _bestTwoStepState;
                    return bestTwoStepStep1; // 返回二步组合的第一步
                }
            }

            // 回退: 仅最佳单步触发二步前瞻 (保留供HintLine显示)
            if (!twoStepExplored && bestAction != null
                && bestAction.Type != ActionType.Upgrade
                && bestAction.Type != ActionType.PickTrinket
                && state.Gold >= 3)
            {
                var nextState = _sim.Simulate(state, bestAction);
                if (nextState.Gold >= 1)
                {
                    var step2Actions = _enumerator.Enumerate(nextState, state.HeroCardId);
                    double bestStep2Value = double.NegativeInfinity;
                    GameAction bestStep2Fallback = null;
                    foreach (var a2 in step2Actions)
                    {
                        if (a2.Type == ActionType.FreezeShop) continue;
                        var s3 = _sim.Simulate(nextState, a2);
                        var f3 = _fe.Extract(s3);
                        double v3 = _vf.Evaluate(f3, evalWeights);
                        if (a2.Type == ActionType.UseHeroPower)
                            v3 += _heroPower.GetStrategyForPower(
                                state.HeroCardId, a2.CardId).PowerValueBias;
                        if (v3 > bestStep2Value) { bestStep2Value = v3; bestStep2Fallback = a2; }
                    }
                    if (bestStep2Fallback != null)
                    {
                        _bestTwoStepAction = bestStep2Fallback;
                        _bestTwoStepState = nextState;
                    }
                }
            }

            // ══ 饰品推荐: 已上移为 rated 饰品 early-return(见上方 A(#3) 段, TryGetRatedTrinketAction) ══
            // 此处不再重复判定 — 单一真理来源, 避免前后两套 rated/unrated 判定漂移。

            // ══ MCTS前瞻升级 (v1.5): 用真实卡池模拟的有限搜索替代纯V(s)二步前瞻 ══
            if (CardPoolSampler.IsInitialized && state.Gold >= 3 && state.BoardMinions.Count < 7)
            {
                try
                {
                    var mctsResult = _mcts.Search(state);
                    if (mctsResult != null && mctsResult.CompletedInBudget && mctsResult.BestAction != null)
                    {
                        // MCTS结果覆盖贪心搜索: 找到比规则层+贪心更好的全局最优
                        // 仅当MCTS推荐与规则层推荐不同且MCTS值显著更高时才覆盖
                        bool replace = false;
                        if (bestAction == null)
                            replace = true;
                        else if (mctsResult.BestAction.Type != bestAction.Type
                            || mctsResult.BestAction.TargetIndex != bestAction.TargetIndex)
                            replace = true;

                        if (replace)
                        {
                            bestAction = mctsResult.BestAction;
                        }

                        // MCTS第2步升级二步前瞻
                        if (mctsResult.SecondAction != null)
                        {
                            _bestTwoStepAction = mctsResult.SecondAction;
                            // 从bestAction模拟出next state
                            _bestTwoStepState = _sim.Simulate(state, bestAction);
                        }
                    }
                }
                catch { /* MCTS降级: 搜索异常时回退到贪心二步前瞻 */ }
            }

            // ── 理财转化: 余钱优先买经济法术/理财随从, 不浪费铸币 ──
            if (bestAction == null && state.Gold >= 1 && state.Gold < 3
                && state.ShopMinions != null && state.ShopMinions.Count > 0)
            {
                foreach (var a in actions)
                {
                    if (a.Type == ActionType.BuySpell && a.TargetIndex < state.ShopMinions.Count)
                    {
                        var sc = state.ShopMinions[a.TargetIndex];
                        int spellCost = GameRuleEvaluator.GetPurchaseCost(
                            state, sc, state.HeroCardId, effectiveRules);
                        if (sc.IsSpell && state.Gold >= spellCost)
                        {
                            // 经济法术: 铸币/免费刷新类优先
                            bool isEcon = _classifier.IsEconomyCard(sc.CardId)
                                || (sc.CardName != null && (sc.CardName.Contains("铸币") || sc.CardName.Contains("金币")));
                            if (isEcon) return a;
                        }
                    }
                }
            }

            BestTwoStepAction = _bestTwoStepAction;
            BestTwoStepState = _bestTwoStepState;
            return bestAction;
        }

        /// <summary>已知引擎卡（铜须/瑞文/达卡莱等）— 永不推荐出售</summary>
        private static readonly HashSet<string> KnownEngineCards = new HashSet<string>
        {
            "BG_LOE_077",    // 铜须 (战吼两次)
            "BG26_ICC_901",  // 达卡莱附魔师 (回合结束两次)
            "BG35_883",      // 巴琳达·斯通赫尔斯 (法术施放两次)
            "BG33_825",      // 私掠者 (悬赏令施放两次)
            "BGS_018",       // 提图斯·瑞文戴尔 (亡语两次)
            "BG_DAL_775",    // 瑞文戴尔男爵 (亡语触发两次)
            "BG35_155",      // 骷髅狂射手
        };
        private static bool IsKnownEngine(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return false;
            return KnownEngineCards.Contains(cardId);
        }

        /// <summary>找场上最弱随从（优先低星→低攻→低血），跳过圣盾/烈毒</summary>
        private int FindWeakestBoardMinion(GameState state, CompLockResult compLock = null)
        {
            if (state.BoardMinions == null || state.BoardMinions.Count == 0) return -1;
            int worstIdx = -1;
            double worstScore = double.MaxValue;

            for (int i = 0; i < state.BoardMinions.Count; i++)
            {
                var m = state.BoardMinions[i];
                // 保护金色+关键词随从
                if (m.Golden) continue;
                if (m.Tier >= 4 && (m.DivineShield || m.Poisonous || m.Venomous || m.Reborn)) continue;
                // 保护已知引擎卡: 铜须/瑞文/达卡莱/巴琳达等 — 永不推荐出售
                bool isEngine = IsKnownEngine(m.CardId)
                    || (m.Attack <= 5 && m.Health <= 10 && m.Tier >= 4);
                double sc = m.Tier * 8.0 + m.Attack * 0.5 + m.Health * 0.3 + (isEngine ? 80 : 0);
                if (sc < worstScore) { worstScore = sc; worstIdx = i; }
            }
            if (worstIdx < 0)
            {
                for (int i = 0; i < state.BoardMinions.Count; i++)
                {
                    var m = state.BoardMinions[i];
                    if (m.Golden) continue;
                    double sc = m.Tier * 6.0 + m.Attack * 0.5 + m.Health * 0.3;
                    if (sc < worstScore) { worstScore = sc; worstIdx = i; }
                }
            }
            return worstIdx;
        }

        /// <summary>店里有比场上随从更好的牌吗？星级差距+战力提升综合判断</summary>
        private int FindBestShopReplace(GameState state, MinionData weakBoard)
        {
            if (state.ShopMinions == null || state.ShopMinions.Count == 0) return -1;
            int bestIdx = -1;
            double bestGain = -1;
            double weakScore = weakBoard.Tier * 8.0 + weakBoard.Attack * 0.5 + weakBoard.Health * 0.3;

            for (int i = 0; i < state.ShopMinions.Count; i++)
            {
                var s = state.ShopMinions[i];
                if (s.IsSpell) continue;
                double shopScore = s.Tier * 8.0 + s.Attack * 0.5 + s.Health * 0.3;
                int tierGap = s.Tier - weakBoard.Tier;

                // 星级差距≥2: 绝对替换 (高2星的牌即使白板也值)
                if (tierGap >= 2) { bestIdx = i; break; }

                // 星级差距=1 + 战力提升>15% → 替换
                if (tierGap >= 1 && shopScore > weakScore * 1.15)
                { bestIdx = i; break; }

                // 同星级但战力显著提升>25%
                double gain = (shopScore - weakScore) / Math.Max(1, weakScore);
                if (gain > 0.25 && gain > bestGain)
                { bestGain = gain; bestIdx = i; }
            }
            return bestIdx;
        }

        /// <summary>累加场面随从的攻击+生命，用于检测战力鸿沟</summary>
        private static float SumAttackHealth(List<MinionData> minions)
        {
            if (minions == null || minions.Count == 0) return 0f;
            float sum = 0f;
            foreach (var m in minions)
                sum += m.Attack * 0.7f + m.Health * 0.3f;
            return sum;
        }

        /// <summary>估算对手场面平均攻血</summary>
        private static float SumOpponentStats(List<OpponentData> opponents)
        {
            if (opponents == null || opponents.Count == 0) return 0f;
            float total = 0f; int count = 0;
            foreach (var o in opponents)
            {
                if (!o.Alive) continue;
                foreach (var m in o.BoardMinions)
                {
                    total += m.Attack * 0.7f + m.Health * 0.3f;
                    count++;
                }
            }
            return count > 0 ? total / count * 7f : 0f;  // 估算满场总战力
        }

        /// <summary>商店中是否有剧毒/烈毒/圣盾/消灭型 tech 卡</summary>
        private static bool ShopHasTechCard(GameState state)
        {
            if (state.ShopMinions == null) return false;
            foreach (var m in state.ShopMinions)
            {
                if (m.Poisonous || m.Venomous || m.DivineShield) return true;
                // 剧毒/烈毒按需区分,优先剧毒
            }
            return false;
        }

        /// <summary>找"死重"随从：对当前局面贡献接近0的非核心卡</summary>
        private int FindDeadWeight(GameState state)
        {
            if (state.BoardMinions == null || state.BoardMinions.Count < 6) return -1;
            float oppStats = SumOpponentStats(state.Opponents);
            if (oppStats <= 0) return -1;

            int worstIdx = -1;
            float worstValue = float.MaxValue;
            for (int i = 0; i < state.BoardMinions.Count; i++)
            {
                var m = state.BoardMinions[i];
                // 保护经济引擎和combo核心
                if (m.Golden || m.DivineShield || m.Poisonous || m.Venomous || m.Reborn || m.Taunt) continue;
                // 剧毒/烈毒/圣盾/复生/嘲讽 → 关键机制卡,不卖
                if (_classifier != null && (_classifier.IsEconomyCard(m.CardId) || _classifier.IsAmplifier(m.CardId)))
                    continue;
                // 计算相对贡献：攻血 vs 对手战力
                float contrib = (m.Attack * 0.7f + m.Health * 0.3f) / Math.Max(1f, oppStats);
                if (contrib < 0.05f && contrib < worstValue)
                {
                    worstValue = contrib;
                    worstIdx = i;
                }
            }
            return worstIdx;
        }

        /// <summary>检查目标随从是否是板面上唯一同种族随从（卖出会破坏种族协同）</summary>
        private static bool IsUniqueTribeMinion(GameState state, int targetIndex)
        {
            if (state.BoardMinions == null || targetIndex < 0 || targetIndex >= state.BoardMinions.Count)
                return false;
            var target = state.BoardMinions[targetIndex];
            if (string.IsNullOrEmpty(target.Tribe)) return false;

            foreach (var t in MinionData.GetTribesArray(target.Tribe))
            {
                int count = 0;
                for (int i = 0; i < state.BoardMinions.Count; i++)
                {
                    if (i == targetIndex) continue;
                    var bm = state.BoardMinions[i];
                    if (!string.IsNullOrEmpty(bm.Tribe) && MinionData.TribeMatches(bm.Tribe, t))
                        count++;
                }
                if (count == 0) return true;  // 该种族只有这一个随从
            }
            return false;
        }

        private int MaxShopTier(GameState state)
        {
            int max = 0;
            if (state.ShopMinions != null)
                foreach (var m in state.ShopMinions)
                    if (m.Tier > max) max = m.Tier;
            return max;
        }

        /// <summary>
        /// 场面最高星级。用于判断店能否改善场面。
        /// </summary>
        private int MaxBoardTier(GameState state)
        {
            int max = 0;
            if (state.BoardMinions != null)
                foreach (var m in state.BoardMinions)
                    if (m.Tier > max) max = m.Tier;
            return max;
        }

        /// <summary>
        /// 店里是否有比场面最强怪更高星的牌——即店能改善场面。
        /// </summary>
        private bool ShopImprovesBoard(GameState state)
        {
            // 场面为空时，任何店里有怪都能改善场面
            if (state.BoardMinions == null || state.BoardMinions.Count == 0)
                return state.ShopMinions != null && state.ShopMinions.Count > 0;
            return MaxShopTier(state) > MaxBoardTier(state);
        }

        /// <summary>
        /// 统计店里高质量卡牌数量 (tier >= currentTier+1 或 部落匹配板上主流)
        /// 416局真人数据: 店有>=2张好卡时, 真人优先买牌而非升本
        /// </summary>
        private int CountHighQualityShopCards(GameState state, int currentTier)
        {
            if (state.ShopMinions == null || state.ShopMinions.Count == 0) return 0;
            string domTribe = GetDominantTribe(state);
            int count = 0;
            foreach (var sm in state.ShopMinions)
            {
                if (sm.IsSpell) continue;
                // 高星级: tier >= current+1
                if (sm.Tier >= currentTier + 1) { count++; continue; }
                // 部落匹配: 与板上主流种族相同
                if (!string.IsNullOrEmpty(domTribe) && !string.IsNullOrEmpty(sm.Tribe))
                {
                    foreach (var t in MinionData.GetTribesArray(sm.Tribe))
                        if (t == domTribe) { count++; break; }
                }
            }
            return count;
        }

        /// <summary>
        /// 评估三连奖励的期望价值（金色卡 + 发现高一级随从）。
        /// 返回值: 0~1, 越高越值得拿三连。
        /// </summary>
        private double EstimateTripleRewardValue(GameState state, int shopTripleIdx)
        {
            if (shopTripleIdx < 0 || state.ShopMinions == null || shopTripleIdx >= state.ShopMinions.Count)
                return 0;
            var tripleCard = state.ShopMinions[shopTripleIdx];
            int tier = tripleCard.Tier;

            // 金色卡价值: 身材翻倍1.5x (关键词无额外金色效果)
            double goldenBonus = 1.5;

            var rules = state.EffectiveRules ?? EffectiveGameRules.Default;
            double rewardPower = 0;
            if (TripleRuleEvaluator.GrantsStandardDiscover(rules))
            {
                // 标准发现奖励: 从高一级卡池3选1, 期望取星级战力
                int discoverTier = Math.Min(tier + 1, rules.MaxTavernTier);
                rewardPower = FeatureExtractor.TierPower.ContainsKey(discoverTier)
                    ? FeatureExtractor.TierPower[discoverTier] : 0.3;
                rewardPower *= 1.2;
            }
            else if (string.Equals(rules.GoldenRewardOverride,
                "tavern_coin", StringComparison.Ordinal))
            {
                rewardPower = 1.0; // 一张酒馆币的显式1金币经济价值
            }

            // 总价值: 金色卡的战力提升 + 发现奖励
            double basePower = FeatureExtractor.TierPower.ContainsKey(tier)
                ? FeatureExtractor.TierPower[tier] : 0.3;
            double tripleTotalValue = basePower * (goldenBonus - 1.0) + rewardPower;

            // 标准化到0~1
            return Math.Min(1.0, tripleTotalValue / 3.0);
        }

        /// <summary>
        /// 检测店里是否有能按当前有效阈值合金的牌。
        /// 返回酒馆位置索引，无则返回-1。C#用CardId精确匹配，比JS的tier代理更可靠。
        /// </summary>
        private int FindTripleInShop(GameState state)
        {
            if (state == null || state.ShopMinions == null || state.ShopMinions.Count == 0)
                return -1;
            var rules = state.EffectiveRules ?? EffectiveGameRules.Default;

            // 找店里能凑三连的牌，优先高星级
            int bestIdx = -1;
            int bestTier = 0;
            for (int i = 0; i < state.ShopMinions.Count; i++)
            {
                var shopCard = state.ShopMinions[i];
                if (shopCard == null || shopCard.IsSpell
                    || !TripleRuleEvaluator.CompletesGolden(state, shopCard.CardId, rules))
                    continue;
                if (shopCard.Tier > bestTier)
                {
                    bestTier = shopCard.Tier;
                    bestIdx = i;
                }
            }
            return bestIdx;
        }

        /// <summary>
        /// 返回推荐购买的酒馆位置索引，按优先级降序。
        /// </summary>
        public List<int> GetRecommendedBuyIndices(GameState state, int topN = 3)
        {
            var result = new List<int>();
            if (state == null || !state.GameActive) return result;

            // V1.2: 阵容锁定 + 英雄策略预计算
            CompLockResult compLockResult; HeroStrategy heroStrat;
            try
            {
                compLockResult = _compLock.Detect(state);
                heroStrat = _heroPower.GetStrategy(state.HeroCardId);
            }
            catch { return result; }

            var rules = state.EffectiveRules ?? EffectiveGameRules.Default;
            var ranked = new List<RankedBuy>();
            for (int i = 0; i < state.ShopMinions.Count; i++)
            {
                var shopCard = state.ShopMinions[i];
                if (shopCard == null) continue;
                int itemCost = GameRuleEvaluator.GetPurchaseCost(
                    state, shopCard, state.HeroCardId, rules);
                if (state.Gold < itemCost) continue;

                double score;
                try
                {
                    bool isSpell = shopCard.IsSpell;
                    var action = new GameAction
                    {
                        Type = isSpell ? ActionType.BuySpell : ActionType.BuyMinion,
                        TargetIndex = i,
                        CardId = shopCard.CardId,
                        PurchaseSource = "tavern_shop",
                    };
                    var next = _sim.Simulate(state, action);
                    if (next == null) continue;
                    var features = _fe.Extract(next);
                    score = _vf.Evaluate(features);
                }
                catch { continue; }

                // 个性化模式
                if (Mode == DecisionMode.Personal && _profile != null)
                {
                    score = _profile.GetPersonalizedScore(shopCard.CardId, (float)score, shopCard.Tribe, "");
                }

                // V1.2: CompLock — 流派偏好修正
                if (compLockResult.State != LockState.None && !string.IsNullOrEmpty(shopCard.Tribe))
                {
                    float lockMult = _compLock.GetBuyMultiplier(shopCard.Tribe, compLockResult);
                    if (lockMult != 1.0f) score *= lockMult;
                }

                // V1.2: HeroPower — 英雄种族偏好
                if (!string.IsNullOrEmpty(shopCard.Tribe))
                {
                    float heroAffinity = _heroPower.GetTribeAffinity(state.HeroCardId, shopCard.Tribe);
                    if (heroAffinity > 0) score *= (1f + heroAffinity);
                }

                // 种族协同加权
                string dominantTribe = GetDominantTribe(state);
                if (!string.IsNullOrEmpty(dominantTribe) && !string.IsNullOrEmpty(shopCard.Tribe))
                {
                    if (MinionData.TribeMatches(shopCard.Tribe, dominantTribe))
                        score *= 1.15;
                }
                if (!string.IsNullOrEmpty(dominantTribe) && string.IsNullOrEmpty(shopCard.Tribe))
                {
                    score = BoostNeutralCoreCard(shopCard.CardId, dominantTribe, score);
                }

                // V1.2: 英雄特定购买加分
                score += heroStrat.BuyValueBias;

                // V1.3: 场协同分
                var synScore = _synergy.ScoreCard(shopCard.CardId, shopCard.Tribe,
                    state, state.HeroCardId);
                if (synScore.TotalScore > 0.1f)
                    score *= (1.0 + synScore.TotalScore * 0.25);

                // 畸变感知: 购买评分加成
                score *= GetAnomalyBuyMultiplier(state, shopCard);

                // Track1(0709): EffectValue 有界加法项(治VF不读文字效果盲区; 仅随从, 链末端不被放大)
                score += EffectValueBonus(shopCard, state, dominantTribe);
                score += (state.ActiveTrinketContext ?? ActiveTrinketContext.Empty)
                    .GetCardSynergyScore(shopCard);

                ranked.Add(new RankedBuy { Index = i, Score = score });
            }

            ranked.Sort((a, b) => b.Score.CompareTo(a.Score));
            foreach (var r in ranked)
            {
                if (result.Count >= topN) break;
                result.Add(r.Index);
            }
            return result;
        }

        /// <summary>
        /// 获取所有商店卡牌的评分（供UI卡片级高亮渲染）。
        /// </summary>
        public List<ShopCardScore> GetShopCardScores(GameState state)
        {
            var result = new List<ShopCardScore>();
            if (state == null || !state.GameActive) return result;

            CompLockResult compLockResult; HeroStrategy heroStrat; string dominantTribe;
            try
            {
                compLockResult = _compLock.Detect(state);
                heroStrat = _heroPower.GetStrategy(state.HeroCardId);
                dominantTribe = GetDominantTribe(state);
            }
            catch { return result; }
            var rules = state.EffectiveRules ?? EffectiveGameRules.Default;

            // 预统计场上+手牌各CardId出现次数（三连检测用CardId精确匹配）
            var boardHandCounts = new Dictionary<string, int>();
            foreach (var m in state.BoardMinions)
                if (!string.IsNullOrEmpty(m.CardId))
                { if (boardHandCounts.ContainsKey(m.CardId)) boardHandCounts[m.CardId]++; else boardHandCounts[m.CardId] = 1; }
            foreach (var m in state.HandMinions)
                if (!string.IsNullOrEmpty(m.CardId))
                { if (boardHandCounts.ContainsKey(m.CardId)) boardHandCounts[m.CardId]++; else boardHandCounts[m.CardId] = 1; }

            for (int i = 0; i < state.ShopMinions.Count; i++)
            {
                var shopCard = state.ShopMinions[i];
                if (shopCard == null) continue;
                int cardCost = GameRuleEvaluator.GetPurchaseCost(
                    state, shopCard, state.HeroCardId, rules);
                if (state.Gold < cardCost) continue;

                double score;
                try
                {
                    var actionType = shopCard.IsSpell ? ActionType.BuySpell : ActionType.BuyMinion;
                    var action = new GameAction
                    {
                        Type = actionType, TargetIndex = i, CardId = shopCard.CardId,
                        PurchaseSource = "tavern_shop",
                    };
                    var next = _sim.Simulate(state, action);
                    if (next == null) continue;
                    var features = _fe.Extract(next);
                    score = _vf.Evaluate(features);
                }
                catch { continue; }

                // ── V1.3 法术决策特征: 按法术类型差异化评分 ──
                if (shopCard.IsSpell)
                {
                    string cnName = shopCard.CardName ?? "";
                    string cardId = shopCard.CardId ?? "";
                    float survivalMult = 1f, scalingMult = 1f;

                    // 经济型: 铸币/刷新/理财 → 提升金效率
                    // 前5回合早期经济法术: 钻探原油/拼命发掘/慎重投资 大幅提分
                    bool early = state.Turn <= 5;
                    if (cnName.Contains("铸币") || cnName.Contains("金币") || cnName.Contains("刷新")
                        || cnName.Contains("理财") || cnName.Contains("钻探") || cnName.Contains("投资")
                        || cnName.Contains("发掘") || cnName.Contains("自负") || cnName.Contains("财富"))
                    {
                        float econMult = early ? 2.0f : 1.3f;
                        // 钻探原油(BG28_805): 永久+1金上限, 早期最高价值
                        if (cardId.Contains("BG28_805")) econMult = early ? 3.0f : 1.6f;
                        // 拼命发掘(BG28_571): HP换金, 血量安全时高价值
                        if (cardId.Contains("BG28_571")) econMult = (early && state.Health > 20) ? 2.2f : 0.8f;
                        // 慎重投资(BG28_800): 1金换下回合2金
                        if (cardId.Contains("BG28_800")) econMult = early ? 2.0f : 1.3f;
                        // 主厨甄选(BG28_518): 定向找同族
                        if (cardId.Contains("BG28_518")) econMult = (early && state.BoardMinions.Count >= 1) ? 1.8f : 0.9f;
                        score *= econMult;
                    }

                    // 生存型: 护甲/圣洁 → 血量压力大时大幅提分
                    if (cnName.Contains("护甲") || cnName.Contains("圣洁") || cnName.Contains("庇护")
                        || cardId.Contains("BG28_500") || cardId.Contains("Treasure_934"))
                    {
                        float hpRatio = state.Health / (float)Math.Max(1, state.MaxHealth);
                        float hpPressure = 1f - hpRatio;
                        float lethalRisk = 0f;
                        if (state.Opponents != null && state.Opponents.Count > 0)
                        {
                            float oppPwr = _fe.ComputeAvgOpponentPower(state.Opponents);
                            float myPwr = _fe.ComputeBoardPower(state.BoardMinions);
                            float pwrRatio = oppPwr > 0 ? myPwr / oppPwr : 2f;
                            if (pwrRatio < 0.5f && state.Health < 15)
                                lethalRisk = 1f - pwrRatio;
                        }
                        survivalMult = 1f + hpPressure * 1.5f + lethalRisk * 2.5f;
                        score *= Math.Min(3.5f, survivalMult);
                    }

                    // 成长型: 点金/发现/预订遗体 → 板面成型时提分
                    if (cnName.Contains("点金") || cnName.Contains("发现") || cnName.Contains("遗体")
                        || cnName.Contains("金色"))
                    {
                        int boardSz = state.BoardMinions.Count;
                        scalingMult = 1f + (boardSz >= 5 ? 0.6f : 0f) + (boardSz >= 7 ? 0.4f : 0f);
                        score *= scalingMult;
                    }

                    // 英雄技能型: 身份揭晓 → 当前技能弱时提分
                    if (cnName.Contains("身份揭晓") || cnName.Contains("英雄技能")
                        || cardId.Contains("HeroPowerSpell"))
                    {
                        var hs = _heroPower.GetStrategy(state.HeroCardId);
                        bool weakPower = hs.PowerType == HeroPowerType.Passive
                            || (hs.PowerCost > 3 && state.Turn >= 8);
                        if (weakPower) score *= 1.8;
                    }

                    // 战斗型: 防御者/甲虫 → 战力接近时提分
                    if (cnName.Contains("防御") || cnName.Contains("甲虫") || cnName.Contains("战斗"))
                    {
                        if (state.Opponents != null && state.Opponents.Count > 0)
                        {
                            float oppPwr = _fe.ComputeAvgOpponentPower(state.Opponents);
                            float myPwr = _fe.ComputeBoardPower(state.BoardMinions);
                            float ratio = oppPwr > 0 ? myPwr / oppPwr : 2f;
                            if (ratio > 0.6f && ratio < 1.4f) score *= 1.5;
                        }
                    }

                    // 背靠背(BG35_952): T4 cost1, 背靠背体系核心, 备着等灾变先锋
                    if (cardId.Contains("BG35_952") || cnName.Contains("背靠背"))
                        score *= (state.BoardMinions.Count >= 3 ? 1.8f : 1.2f);

                    // 惊扰墓穴(BG34_888): T4 cost2, 有机会出遗骸看管者→三连
                    if (cardId.Contains("BG34_888") || cnName.Contains("惊扰墓穴"))
                        score *= (state.TavernTier >= 3 && state.BoardMinions.Count <= 5 ? 1.6f : 1.0f);

                    // 奥术吸收(BG35_911): T4 cost1, 元素流核心成长
                    if (cardId.Contains("BG35_911") || cnName.Contains("奥术吸收"))
                    {
                        int elemCount = state.BoardMinions.Count(m => (m.Tribe ?? "").Contains("元素"));
                        if (elemCount >= 2) score *= 2.0f;
                    }

                    // 优势压制(BG28_573): T5 cost3, 后期针对大身材, 光环随从无效
                    if (cardId.Contains("BG28_573") || cnName.Contains("优势压制"))
                        score *= (state.Turn >= 10 ? 1.8f : 1.0f);

                    // 点金之触(BG28_830): T5 cost5, 稳定点金/排除其他随从
                    if (cardId.Contains("BG28_830") || cnName.Contains("点金之触"))
                        score *= (state.Gold >= 8 && state.BoardMinions.Count >= 4 ? 1.8f : 0.7f);

                    // 冒牌智慧之球(BG30_802): T6 cost4, 下次刷新触发随机强力效果
                    if (cardId.Contains("BG30_802") || cnName.Contains("智慧之球"))
                        score *= (state.Turn >= 8 && state.Gold >= 6 ? 2.0f : 0.6f);

                    // 哈缪尔法杖(EBG_Spell_038): T6 cost2, 定向搜索种族
                    if (cardId.Contains("EBG_Spell_038") || cnName.Contains("哈缪尔"))
                        score *= (state.Turn >= 10 ? 1.8f : 0.8f);

                    // 大地母亲之眼(EBG_Spell_017): T6 cost4, 金色化T4以下核心随从
                    if (cardId.Contains("EBG_Spell_017") || cnName.Contains("大地母亲"))
                    {
                        bool hasLowTierCore = state.BoardMinions.Any(m => m.Tier <= 4 && m.Golden == false);
                        score *= (hasLowTierCore ? 2.2f : 0.5f);
                    }

                    // 默认法术轻微降分(不增加板面), 但上述特殊类型已覆盖主要场景
                    if (survivalMult <= 1.01f && scalingMult <= 1.01f)
                        score *= 0.95;  // 轻量惩罚, 避免经济型法术被过度压制
                }

                if (Mode == DecisionMode.Personal && _profile != null)
                    score = _profile.GetPersonalizedScore(shopCard.CardId, (float)score, shopCard.Tribe, "");

                if (compLockResult.State != LockState.None && !string.IsNullOrEmpty(shopCard.Tribe))
                {
                    float lockMult = _compLock.GetBuyMultiplier(shopCard.Tribe, compLockResult);
                    if (lockMult != 1.0f) score *= lockMult;
                }

                if (!string.IsNullOrEmpty(shopCard.Tribe))
                {
                    float heroAffinity = _heroPower.GetTribeAffinity(state.HeroCardId, shopCard.Tribe);
                    if (heroAffinity > 0) score *= (1f + heroAffinity);
                }

                if (!string.IsNullOrEmpty(dominantTribe) && !string.IsNullOrEmpty(shopCard.Tribe))
                    if (MinionData.TribeMatches(shopCard.Tribe, dominantTribe)) score *= 1.15;
                if (!string.IsNullOrEmpty(dominantTribe) && string.IsNullOrEmpty(shopCard.Tribe))
                    score = BoostNeutralCoreCard(shopCard.CardId, dominantTribe, score);

                score += heroStrat.BuyValueBias;

                // Early-game differentiation (T1-2): amplify hero affinity, pair, keyword signals
                if (state.TavernTier <= 2 && state.Turn <= 5)
                {
                    if (!string.IsNullOrEmpty(shopCard.Tribe))
                    {
                        float heroAff = _heroPower.GetTribeAffinity(state.HeroCardId, shopCard.Tribe);
                        if (heroAff > 0f) score *= (1f + heroAff * 1.5f);
                    }
                    bool hasPair = state.BoardMinions.Any(m => m.CardId == shopCard.CardId);
                    if (hasPair) score *= 1.20f;
                    if (shopCard.DivineShield || shopCard.Poisonous || shopCard.Venomous)
                        score *= 1.12f;
                    if (shopCard.Taunt && state.BoardMinions.Count <= 2)
                        score *= 1.05f;
                }

                string reason = "";
                var synScore = _synergy.ScoreCard(
                    shopCard.CardId, shopCard.Tribe, state, state.HeroCardId);
                if (synScore.TotalScore > 0.1f)
                    score *= (1.0 + synScore.TotalScore * 0.25);
                if (!string.IsNullOrEmpty(synScore.Reason)) reason = synScore.Reason;

                // 卡牌分类: 用途+质量评级
                float economyVal = 0f, combatVal = 0.3f, growthVal = 0f;
                bool isCoreComboVal = false;
                bool hasClassification = false;
                var primaryRole = CardClassifier.CardRole.Unknown;
                if (_classifier != null)
                {
                    var cls = _classifier.GetClassification(shopCard.CardId);
                    if (cls.HasValue)
                    {
                        hasClassification = true;
                        primaryRole = cls.Value.PrimaryRole;
                        economyVal = cls.Value.EconomyValue;
                        combatVal = cls.Value.CombatValue;
                        growthVal = cls.Value.GrowthValue;
                        isCoreComboVal = cls.Value.IsCoreCombo;
                    }
                }

                bool isTriple = !shopCard.IsSpell
                    && TripleRuleEvaluator.CompletesGolden(state, shopCard.CardId,
                        state.EffectiveRules ?? EffectiveGameRules.Default);

                // ── 场面类型匹配: 按游戏阶段调整评分侧重 ──
                if (state.Turn <= 5)
                {
                    // 早期: 理财/召唤随从优先(加速升本/铺场面)
                    if (economyVal > 0.5f) score *= 1.25f;        // 强理财(战吼给币/召唤≥2)
                    else if (economyVal > 0.3f) score *= 1.15f;   // 弱理财(亡语召唤1个)
                    if (growthVal > 0.5f && economyVal < 0.3f) score *= 0.85f;
                }
                else if (state.Turn <= 9)
                {
                    // 中期: 同族协同牌优先
                    if (!string.IsNullOrEmpty(shopCard.Tribe) && !string.IsNullOrEmpty(dominantTribe)
                        && MinionData.TribeMatches(shopCard.Tribe, dominantTribe))
                        score *= 1.10f;
                }
                else
                {
                    // 后期: 三连候选+核心组件优先
                    if (isTriple) score *= 1.20f;
                    if (isCoreComboVal) score *= 1.15f;
                }

                // ── 策略检测: 战吼/理财流派评分通道 ──
                bool hasBrannOnBoard = state.BoardMinions.Any(m =>
                    m.CardId != null && (m.CardId == "BG_LOE_077" || m.CardId.Contains("BRAN")));
                bool isShudderwock = state.HeroCardId != null && state.HeroCardId.Contains("HERO_23");
                if ((hasBrannOnBoard || isShudderwock) && economyVal < 0.3f && !shopCard.IsSpell)
                {
                    if (_classifier != null && _classifier.IsAmplifier(shopCard.CardId))
                        score *= 0.25f;
                    else
                    {
                        try
                        {
                            HearthDb.Card c;
                            if (HearthDb.Cards.All.TryGetValue(shopCard.CardId, out c) && c != null
                                && c.Mechanics != null && c.Mechanics.Contains("BATTLECRY"))
                                score *= 1.15f;
                        }
                        catch { }
                    }
                }
                bool hasHoggerOrGallywix = state.BoardMinions.Any(m =>
                    m.CardId != null && (m.CardId.Contains("BGS_072") || m.CardId.Contains("BGS_030")));
                bool isTradePrince = state.HeroCardId != null && (state.HeroCardId.Contains("HERO_10")
                    || state.HeroCardId.Contains("GALLYWIX"));
                if ((hasHoggerOrGallywix || isTradePrince) && economyVal > 0.3f)
                    score *= 1.20f;

                // 星级质量惩罚: 商店有高星卡时低星卡降权(防T1压制T2+)
                // 仅在没有三连时惩罚; T1对子是例外
                if (!isTriple && state.BoardMinions.Count >= 2 && state.Turn >= 3)
                {
                    int maxShopTier = 0;
                    foreach (var sm in state.ShopMinions)
                        if (sm != null && sm.Tier > maxShopTier) maxShopTier = sm.Tier;
                    if (maxShopTier > shopCard.Tier && maxShopTier >= 2)
                    {
                        int gap = maxShopTier - shopCard.Tier;
                        if (gap >= 2) score *= 0.60;       // T1 vs T3+ → 大幅惩罚
                        else if (gap == 1) score *= 0.80;  // T1 vs T2 / T2 vs T3
                    }
                }

                // V1.3: 重复引擎卡检测 — 规则化: Amplifier + 同名非金 + "触发N次"句式 → 不叠加
                // 规则: 文本为"你的[关键词条]会触发N次/施放N次" — 两个普通版不叠加(上限=N)
                //   铜须: 战吼触发两次  达卡莱: 回合结束触发两次
                //   巴琳达: 法术施放两次  私掠者: 悬赏令施放两次
                //   金卡效果=触发三次(非普通×2)
                // 对比: "额外触发N次"(如瑞文) → 可叠加, 不在此列表
                if (_classifier != null && _classifier.IsAmplifier(shopCard.CardId)
                    && state.BoardMinions.Any(bm => bm.CardId == shopCard.CardId && !bm.Golden))
                {
                    // 仅对已知不叠加的放大器引擎卡施加惩罚
                    // 判定标准: 提供"额外触发"机制 (非 summon_extra/copy 类)
                    //   铜须: 战吼额外  达卡莱: 回合结束额外  巴琳达: 法术额外  私掠者: 悬赏令额外
                    var nonStackingEngines = new HashSet<string>
                    {
                        "BG_LOE_077", "BG26_ICC_901", "BG35_883", "BG33_825"
                    };
                    if (nonStackingEngines.Contains(shopCard.CardId))
                    {
                        score *= 0.25;
                        if (string.IsNullOrEmpty(reason)) reason = "不叠加";
                        else reason += "+不叠加";
                    }
                }

                // 时空扭曲随从加权兜底(0708 Bug3): 誓言石的召唤(BG34_Anomaly_805)等畸变下,
                // 小型/大型时空扭曲随从(名为"时空扭曲X", 如BG34_Giant_594绿植)T7/T10进池。
                // 这些是升级版池卡, 带强经济/滚雪球文字效果(如"回合结束随机获取当前等级随从"),
                // 但ValueFunction只评身材+种族协同、不读文字效果 → 系统性低估(实测0.56被埋为Minor)。
                // 兜底: 离族但强效, 施加有界加权提升推荐优先级。根治(文字效果估值)交fable5算法评审。
                if (!shopCard.IsSpell && (shopCard.CardName ?? "").Contains("时空扭曲"))
                {
                    score *= 1.30;
                    if (string.IsNullOrEmpty(reason)) reason = "时空扭曲";
                    else reason += "+时空扭曲";
                }

                // 畸变感知: 购买评分加成
                score *= GetAnomalyBuyMultiplier(state, shopCard);

                // Track1(0709): EffectValue 有界加法项(治VF不读文字效果盲区; 仅随从, 链末端不被放大)
                score += EffectValueBonus(shopCard, state, dominantTribe);
                score += (state.ActiveTrinketContext ?? ActiveTrinketContext.Empty)
                    .GetCardSynergyScore(shopCard);

                // ── P1 影子: GEV 金币等价值(与VF主Score并行输出, 不改现网) ──
                // synergy: 主导部落匹配(免锁, 单帧重放也有效) → +1.5。
                bool gevTribeMatch = !string.IsNullOrEmpty(dominantTribe) && !string.IsNullOrEmpty(shopCard.Tribe)
                    && MinionData.TribeMatches(shopCard.Tribe, dominantTribe);
                double gevSynergy = gevTribeMatch ? 1.5 : 0.0;
                double gevTriple = isTriple ? (1.5 + 0.5 * shopCard.Tier) : 0.0;
                double gevStat = _effectTable != null ? _effectTable.ComputeStatTempo(shopCard, state.TavernTier) : 0.0;
                double gevEffect = _effectTable != null ? _effectTable.ComputeEffectValue(shopCard, state, dominantTribe) : 0.0;
                double gev = _effectTable != null ? _effectTable.ComputeGEV(shopCard, state, gevSynergy, gevTriple, dominantTribe) : 0.0;
                double net = gev - cardCost;

                result.Add(new ShopCardScore
                {
                    Index = i,
                    ShopPosition = shopCard.Position,
                    EntityId = shopCard.EntityId,
                    Score = score,
                    Gev = gev,
                    Net = net,
                    StatTempo = gevStat,
                    EffectValue = gevEffect,
                    CardName = shopCard.CardName ?? shopCard.CardId,
                    Tribe = shopCard.Tribe ?? "",
                    Tier = shopCard.Tier,
                    Reason = reason,
                    IsSpell = shopCard.IsSpell,
                    EconomyValue = economyVal,
                    CombatValue = combatVal,
                    GrowthValue = growthVal,
                    IsCoreCombo = isCoreComboVal,
                    HasClassification = hasClassification,
                    PrimaryRole = primaryRole,
                    PickRate = CardQuality.GetPickRate(shopCard.CardId),
                    IsTriple = isTriple,
                });
            }
            if (result.Count == 0 && state.ShopMinions != null && state.ShopMinions.Count > 0)
            {
                bool canBuyMinion = state.BoardMinions == null || state.BoardMinions.Count < state.MaxBoardSlots;
                bool canBuySpell = state.HandMinions == null || state.HandMinions.Count < 10;
                for (int i = 0; i < state.ShopMinions.Count; i++)
                {
                    var shopCard = state.ShopMinions[i];
                    if (shopCard == null) continue;
                    bool isSpell = shopCard.IsSpell;
                    if (isSpell && !canBuySpell) continue;
                    if (!isSpell && !canBuyMinion) continue;
                    int cardCost = GameRuleEvaluator.GetPurchaseCost(
                        state, shopCard, state.HeroCardId, rules);
                    if (state.Gold < cardCost) continue;

                    var classification = _classifier != null
                        ? _classifier.GetClassification(shopCard.CardId)
                        : null;

                    double fallbackScore = shopCard.Tier * 10.0 + shopCard.Attack * 0.35 + shopCard.Health * 0.25;
                    if (!string.IsNullOrEmpty(dominantTribe) && !string.IsNullOrEmpty(shopCard.Tribe)
                        && MinionData.TribeMatches(shopCard.Tribe, dominantTribe))
                        fallbackScore *= 1.20;
                    result.Add(new ShopCardScore
                    {
                        Index = i,
                        ShopPosition = shopCard.Position,
                        EntityId = shopCard.EntityId,
                        Score = fallbackScore,
                        CardName = shopCard.CardName ?? shopCard.CardId,
                        Tribe = shopCard.Tribe ?? "",
                        Tier = shopCard.Tier,
                        Reason = "可买候选",
                        IsSpell = isSpell,
                        EconomyValue = classification.HasValue
                            ? classification.Value.EconomyValue : 0f,
                        CombatValue = classification.HasValue
                            ? classification.Value.CombatValue : 0.3f,
                        GrowthValue = classification.HasValue
                            ? classification.Value.GrowthValue : 0f,
                        IsCoreCombo = classification.HasValue
                            && classification.Value.IsCoreCombo,
                        HasClassification = classification.HasValue,
                        PrimaryRole = classification.HasValue
                            ? classification.Value.PrimaryRole
                            : CardClassifier.CardRole.Unknown,
                        PickRate = CardQuality.GetPickRate(shopCard.CardId),
                        IsTriple = !isSpell && TripleRuleEvaluator.CompletesGolden(
                            state, shopCard.CardId,
                            state.EffectiveRules ?? EffectiveGameRules.Default),
                    });
                }
            }
            return result;
        }

        /// <summary>
        /// 升本建议：返回 ("LEVEL_UP"|"STABILIZE"|"DANGER"|"NO_GOLD", 理由)。
        /// </summary>
        private static int GetEffectiveUpgradeCostOrUnavailable(GameState state)
        {
            int? cost = GameRuleEvaluator.GetUpgradeCost(
                state, state != null ? state.EffectiveRules : EffectiveGameRules.Default);
            return cost.HasValue ? cost.Value : int.MaxValue;
        }

        public LevelUpSuggestion GetLevelUpSuggestion(GameState state)
        {
            if (state == null || !state.GameActive)
                return new LevelUpSuggestion("NO_GOLD", "不在对局中");

            int maxTavernTier = state.EffectiveRules != null
                ? state.EffectiveRules.MaxTavernTier : 6;
            if (state.TavernTier >= maxTavernTier)
                return new LevelUpSuggestion("MAX", "已满级");

            int? resolvedCost = GameRuleEvaluator.GetUpgradeCost(
                state, state.EffectiveRules ?? EffectiveGameRules.Default);
            if (!resolvedCost.HasValue)
                return new LevelUpSuggestion("NO_GOLD", "升本费用未确认");
            int cost = resolvedCost.Value;
            var heroStrat = _heroPower.GetStrategy(state.HeroCardId);
            // 欧穆: 升本后得2币 → 有效费用-2 (有足够本钱就升)
            int effectiveCost = heroStrat.SpecialRule == "OMU" ? Math.Max(0, cost - 2) : cost;
            if (state.Gold < cost && state.Gold < effectiveCost)
                return new LevelUpSuggestion("NO_GOLD", "金币不足 (" + cost + " 费)");
            // 早期升本窗口：T1-T2一律推荐 (但金币未初始化时不推荐)
            if (state.TavernTier == 1 && state.Turn <= 3 && state.Gold > 0)
            {
                if (state.Gold >= cost)
                    return new LevelUpSuggestion("LEVEL_UP", "T" + state.Turn + " 升2本 — 抢占节奏");
                return new LevelUpSuggestion("STABILIZE", "下回合升2本 (需" + cost + "费)");
            }

            // 金币完全不够 → 不推荐升本
            if (state.Gold < cost && state.Gold < effectiveCost)
                return new LevelUpSuggestion("NO_GOLD", "金币不足 (" + cost + " 费)");

            // 欧穆特殊: 本金不够但返还后够 → 提示升本后有利余
            bool omuRefund = heroStrat.SpecialRule == "OMU" && state.Gold < cost && state.Gold >= effectiveCost;

            // 总是给出升本建议——让玩家自己权衡风险
            // 通过Suggestion字段区分推荐程度: LEVEL_UP=推荐, STABILIZE=观望, DANGER=风险
            var phaseResult = _turnPhase.Evaluate(state);

            var upgradeAction = new GameAction { Type = ActionType.Upgrade };
            var nextState = _sim.Simulate(state, upgradeAction);
            var features = _fe.Extract(nextState);
            var adjustedWeights = _turnPhase.GetPhaseAdjustedWeights(_vf.Weights, phaseResult);
            double upgradeValue = _vf.Evaluate(features, adjustedWeights);

            // 对照基准: 当前状态(存钱不动), 而非刷新(浪费1金最差动作)
            var currentFeatures = _fe.Extract(state);
            double currentValue = _vf.Evaluate(currentFeatures, adjustedWeights);

            upgradeValue += heroStrat.UpgradeValueBias;

            // 三连时序偏置: 店里有三连→升本后合金=更高本发现奖励
            // 真实数据: 91.2%先升本再合金, 当升本和三连同回合出现时100%先升本
            if (FindTripleInShop(state) >= 0 && state.TavernTier < maxTavernTier)
            {
                float tripleDeferBonus = state.TavernTier <= 3 ? 0.25f : 0.15f;
                upgradeValue += tripleDeferBonus;
            }

            double diff = upgradeValue - currentValue;

            // T3-T4, 2本→3本强烈推荐
            // 416局校准: 店有>=2张高质量卡时,真人优先买牌再升本(algoLevel_playerBuy=67次mismatch)
            if (state.TavernTier == 2 && state.Turn >= 3 && state.Turn <= 5)
            {
                int highQualityCount = CountHighQualityShopCards(state, state.TavernTier);
                if (state.Gold >= cost)
                {
                    if (highQualityCount >= 2 && state.Gold >= 3)
                        return new LevelUpSuggestion("STABILIZE", "店里有好牌 — 建议先买牌再升本");
                    return new LevelUpSuggestion("LEVEL_UP", "T" + state.Turn + " 升3本 — 扩大卡池");
                }
            }

            // 升本免费或极便宜 → 强烈推荐
            if (cost <= 1 && state.TavernTier < 5)
                return new LevelUpSuggestion("LEVEL_UP", "升本仅需" + cost + "费, 不容错过!");

            // 标准节奏: 到达标准升本回合 → 推荐
            if (FeatureExtractor.StandardLevelTurn.ContainsKey(state.TavernTier + 1)
                && state.Turn >= FeatureExtractor.StandardLevelTurn[state.TavernTier + 1])
            {
                // 5→6本: 门槛更严(18血), 残血升本等于自杀
                int dangerThreshold = state.TavernTier >= 5 ? 18 : 12;
                if (state.Health <= dangerThreshold)
                {
                    string msg = state.TavernTier >= 5
                        ? "血量" + state.Health + " — 升6本风险过大, 稳住吃鸡"
                        : "血量低, 但升本时机已到 — 自行判断";
                    return new LevelUpSuggestion("DANGER", msg);
                }
                return new LevelUpSuggestion("LEVEL_UP", "升" + (state.TavernTier + 1) + "本时机成熟");
            }

            // 血量极低: 仍然显示升本信息, 但标注风险
            if (state.Health <= 10)
            {
                string riskMsg = "血量" + state.Health + " — 升本有风险 (费" + cost + ")";
                return new LevelUpSuggestion("DANGER", riskMsg);
            }

            // T5: 升5本时机 (4→5), 场面≥4随从+血量≥25 → 直接推荐
            if (state.TavernTier == 4 && state.Turn <= 9 && state.BoardMinions.Count >= 4 && state.Health >= 25 && state.Gold >= cost)
                return new LevelUpSuggestion("LEVEL_UP", "T" + state.Turn + " 升5本 — 场面足够");

            // 常规判断
            bool hasTriple = FindTripleInShop(state) >= 0;
            if (diff > 0.03) return new LevelUpSuggestion("LEVEL_UP",
                hasTriple ? "升本→下回合合金 (费" + cost + ")" : "升本节奏合适 (费" + cost + ")");
            if (diff > 0) return new LevelUpSuggestion("STABILIZE",
                hasTriple ? "升本金卡发现更优 (费" + cost + ")" : "升本收益不显著 (费" + cost + ")");
            return new LevelUpSuggestion("STABILIZE", "建议先稳场面 (费" + cost + ")");
            return new LevelUpSuggestion("DANGER", "升本会降低战力");
        }

        // ── 中立核心卡 / 种族检测 ──

        /// <summary>
        /// Track1(0709): VF 有界加法项 = min(λ×EffectValue, CAP)。
        /// 仅随从(法术走 V1.3 差异化评分，防双算)；EffectValue 内含离族×0.6 门控；放乘子链末端不被放大。
        /// </summary>
        private double EffectValueBonus(MinionData card, GameState state, string dominantTribe)
        {
            if (card == null || card.IsSpell || _effectTable == null) return 0.0;
            double ev = _effectTable.ComputeEffectValue(card, state, dominantTribe);
            double bonus = EFFECT_BONUS_LAMBDA * ev;
            return bonus > EFFECT_BONUS_CAP ? EFFECT_BONUS_CAP : bonus;
        }

        private static string GetDominantTribe(GameState state)
        {
            if (state.BoardMinions == null || state.BoardMinions.Count < 2) return "";
            var tribeCounts = new Dictionary<string, int>();
            foreach (var m in state.BoardMinions)
            {
                if (string.IsNullOrEmpty(m.Tribe)) continue;
                foreach (var t in MinionData.GetTribesArray(m.Tribe))
                {
                    int c;
                    tribeCounts.TryGetValue(t, out c);
                    tribeCounts[t] = c + 1;
                }
            }
            string bestTribe = "";
            int bestCount = 0;
            foreach (var kv in tribeCounts)
            {
                if (kv.Value > bestCount) { bestCount = kv.Value; bestTribe = kv.Key; }
            }
            return bestCount >= 2 ? bestTribe : "";
        }

        private static double BoostNeutralCoreCard(string cardId, string dominantTribe, double score)
        {
            // 元素流核心：巴琳达·斯通赫尔斯（双倍法术）
            if (dominantTribe == "ELEMENTAL" && cardId == "BG35_883")
                return score * 1.25;
            // 海盗流核心：舰长尤朵拉/罗杰斯 等中立海盗辅助
            if (dominantTribe == "PIRATE" && cardId == "BG25_155")
                return score * 1.15;
            return score;
        }

        /// <summary>
        /// 畸变感知购买评分加成。双头之战下有对子→大幅加分(购买即可三连)；
        /// 泰坦钩爪首购免费→所有卡小幅加分；虚妄神像2张即三连→对子加分。
        /// </summary>
        private double GetAnomalyBuyMultiplier(GameState state, MinionData shopCard)
        {
            if (state == null || shopCard == null
                || string.IsNullOrEmpty(shopCard.CardId))
                return 1.0;

            string shopCardId = shopCard.CardId;
            bool isSpell = shopCard.IsSpell;
            var rules = state.EffectiveRules ?? EffectiveGameRules.Default;

            double mult = 1.0;

            // 双头之战: 首购卡牌给复制。法术也属于card，保留低幅有界加成；
            // 随从按是否已有一张区分“形成对子/直接三连”。
            if (rules.FirstPurchaseExtraCopy != null
                && !state.FirstPurchaseUsedThisTurn)
            {
                if (isSpell)
                {
                    if (rules.FirstPurchaseExtraCopy.CardType == "card")
                        mult *= 1.15;
                }
                else
                {
                    int owned = TripleRuleEvaluator.CountOwnedCopies(state, shopCardId);
                    if (owned >= 1) mult *= 2.0;   // 已有至少一张, 购买+复制可完成标准三连
                    else mult *= 1.15;             // 没有此卡, 购买+复制形成对子
                }
            }

            // 虚妄神像: 2张即三连 → 对子即可三连, 大幅加分
            int goldenRequirement = (state.ActiveTrinketContext
                ?? rules.ActiveTrinkets ?? ActiveTrinketContext.Empty)
                .GetGoldenCopyRequirement(shopCard, rules.GoldenCopyRequirement);
            if (!isSpell && goldenRequirement != 3
                && TripleRuleEvaluator.CompletesGolden(state, shopCardId, rules))
            {
                mult *= 2.5;
            }

            // 泰坦钩爪: 首购免费 → 所有卡价值提升
            if (!isSpell && rules.FirstMinionPurchaseCost == 0
                && !state.FirstMinionPurchaseUsedThisTurn)
                mult *= 1.20;

            // 高戈奈斯暴风: 随从固定2费 → 价值提升
            if (!isSpell && rules.MinionPurchaseCostOverride.HasValue
                && rules.MinionPurchaseCostOverride.Value < 3)
                mult *= 1.10;

            return mult;
        }

        /// <summary>
        /// 获取当前通用阵容锁定信息。
        /// </summary>
        public CompGuidance GetCompGuidance(GameState state)
        {
            var result = new CompGuidance();
            if (state == null) return result;

            var compLockResult = _compLock.Detect(state);

            if (compLockResult.State == LockState.Hard)
                result.LockIcon = "H";
            else if (compLockResult.State == LockState.Soft)
                result.LockIcon = "S";

            return result;
        }

        // ══ 饰品评估 ══
        public struct TrinketScore
        {
            public int Index;
            public string CardId;
            public string Name;
            public double Score;
            public bool IsLesser;
            public bool IsUnrated;  // P1.5: 无可靠评分 → 面板标"未知", 排序垫底不作首选
            public List<string> MatchedRuleIds;
        }

        /// <summary>
        /// A(#3, fable5 Opt1): 若存在 rated 饰品报价, 返回其 PickTrinket 动作; 否则 null。
        /// 复用 EvaluateTrinkets 单一评分入口(rated 排序在前); 全 unrated/无报价/未加载 → null → 让位普通规则。
        /// 无报价回合天然休眠; 饰品面板与本 hint 同源, 不产生分歧。
        /// </summary>
        private GameAction TryGetRatedTrinketAction(GameState state)
        {
            // 合法性 by-construction: 仅当 state.TrinketOffer 非空(游戏正在提供饰品选择)才返回 PickTrinket;
            // 无报价→null。ActionEnumerator 不枚举 PickTrinket, 但"有报价即可选"是确定性合法, 无需入枚举集。
            if (state.TrinketOffer == null || state.TrinketOffer.Count == 0) return null;
            var scores = EvaluateTrinkets(state);
            if (scores.Count == 0 || scores[0].IsUnrated) return null;
            var best = scores[0];
            return new GameAction
            {
                Type = ActionType.PickTrinket,
                TargetIndex = best.Index,
                CardId = best.CardId,
                Description = "推荐饰品: " + best.Name
            };
        }

        public List<TrinketScore> EvaluateTrinkets(GameState state)
        {
            var results = new List<TrinketScore>();
            if (state == null) return results;
            if (state.TrinketOffer == null || state.TrinketOffer.Count == 0) return results;

            string domTribe = GetDomTribe(state);
            var recommendations = _trinketRecommendations.Evaluate(
                state.TrinketOffer, domTribe);
            foreach (var recommendation in recommendations)
            {
                results.Add(new TrinketScore
                {
                    Index = recommendation.Index,
                    CardId = recommendation.CardId,
                    Name = recommendation.DisplayName,
                    Score = recommendation.RuleScore,
                    IsLesser = recommendation.IsLesser,
                    IsUnrated = recommendation.IsUnrated,
                    MatchedRuleIds = recommendation.MatchedRuleIds,
                });
            }
            return results;
        }

        private string GetDomTribe(GameState state)
        {
            // 简化: 从board随从名称推断主力种族
            var counts = new Dictionary<string, int>();
            var tribeKeys = new string[] { "野兽", "机械", "恶魔", "龙", "元素", "亡灵", "海盗", "野猪人", "纳迦", "鱼人" };
            if (state.BoardMinions != null)
            {
                foreach (var m in state.BoardMinions)
                {
                    var name = m.CardName ?? "";
                    foreach (var tk in tribeKeys)
                        if (name.Contains(tk))
                            counts[tk] = (counts.ContainsKey(tk) ? counts[tk] : 0) + 1;
                }
            }
            string best = null; int bestCount = 0;
            foreach (var kv in counts)
                if (kv.Value > bestCount) { best = kv.Key; bestCount = kv.Value; }
            return best;
        }

        /// <summary>评估暗月奖品发现选项。返回评分列表(高分=推荐)。</summary>
        public List<TrinketScore> EvaluatePrizeDiscovers(GameState state)
        {
            var results = new List<TrinketScore>();
            if (state.DiscoverOptions == null || state.DiscoverOptions.Count == 0) return results;

            int hp = state.Health + state.Armor;

            for (int i = 0; i < state.DiscoverOptions.Count; i++)
            {
                var opt = state.DiscoverOptions[i];
                string name = opt.TrinketName ?? "";
                PrizeSpellPolicy prize;
                if (!_prizeRegistry.TryGet(opt.CardId, out prize)) continue;
                double score = PrizeSpellScorer.Score(
                    prize, state.Gold, state.BoardMinions.Count, hp);

                results.Add(new TrinketScore { Index = i, CardId = opt.CardId, Name = name, Score = score });
            }
            results.Sort((a, b) => b.Score.CompareTo(a.Score));
            return results;
        }
    }
}
