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
        private HeroPlaybookRegistry _heroPlaybook;
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
        private readonly MainlineStrategyPlanner _mainline;
        private CompGuideCatalog _compGuideCatalog;
        private CardObjectDatabase _cardObjectDatabase;

        // Track1(0709): EffectValueTable 嫁接 VF 作有界加法项(治 VF 不读文字效果盲区)。
        // bonus = min(λ×EffectValue, CAP)，仅随从(法术走 V1.3 差异化评分，防双算)。回滚=LAMBDA 置 0。
        // λ 默认 0.20(dev 网格校准); 评测/回滚可用环境变量 BOBCOACH_EFFECT_LAMBDA 覆盖(如 0 = 纯 VF 基线)。
        // 生产运行时(HDT 插件)不设该变量 → 恒为 0.20, 行为不变。用于 λ 终判 λ=0.20 vs λ=0 离线对比。
        private static readonly double EFFECT_BONUS_LAMBDA = ReadEffectLambda();
        private const double EFFECT_BONUS_CAP = 0.6;      // 硬上界=15%典型VF分(median≈3.95)
        private const double COMP_GUIDE_WEIGHT_LAMBDA = 0.02;

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

        // 1.2.5 D1 A/B: 升本回合尾金刷新豁免开关 (BOBCOACH_UPGRADE_TAIL_REFRESH=1 启用)
        private static readonly bool UPGRADE_TAIL_REFRESH_ENABLED = ReadUpgradeTailRefreshFlag();

        private static bool ReadUpgradeTailRefreshFlag()
        {
            try
            {
                var s = Environment.GetEnvironmentVariable("BOBCOACH_UPGRADE_TAIL_REFRESH");
                return !string.IsNullOrEmpty(s) && s.Trim() == "1";
            }
            catch { return false; }
        }
        private ProfileEngine _profile;
        private MCTSSearch _mcts;
        public int LookaheadDepth { get; set; }
        public DecisionMode Mode { get; set; }
        public HeroPowerEngine HeroPower => _heroPower;
        public HeroPlaybookRegistry HeroPlaybook => _heroPlaybook;
        internal ITrinketFactSource TrinketFactSource => _trinketFactSource;
        public ContextDetector ContextDetector => _contextDetector;
        public FeatureExtractor GetFeatureExtractor() { return _fe; }
        public ValueFunction GetValueFunction() { return _vf; }
        // 二步前瞻结果: 供HintLine显示完整操作序列
        public GameAction BestTwoStepAction { get; private set; }
        public GameState BestTwoStepState { get; private set; }
        private GameAction _bestTwoStepAction;
        private GameState _bestTwoStepState;
        private MainlinePlan _lastMainlinePlan;
        // R227 战力速率: 上回合板面战力与回合号(用于判断战力曲线变化速率,
        // 负速率/停滞时升本=速败高发区, 横向比对: 克罗米rank6升4本速率-0.2速败,
        // rank4升4本速率+1.2吃分; 德纳修斯rank4升6本后速率-3.1战力倒退)
        private float _lastTurnBoardPower = -1f;
        private int _lastTurnNumber = -1;
        private int _lastTurnTier = -1;
        // R230 趋势记忆: 近3回合战力序列(动态判断用——单帧阈值看不出"板面在缩水",
        // 3回合窗口能识别停滞/下滑的提前信号)
        private readonly Queue<float> _powerTrendQueue = new Queue<float>();
        private readonly StrategicMemory _strategyMemory = new StrategicMemory();
        private readonly StrategySequencePlanner _strategySequencePlanner =
            new StrategySequencePlanner();
        private StrategyPlan _lastStrategyPlan;
        internal MainlinePlan LastMainlinePlan => _lastMainlinePlan;
        public StrategyPlan LastStrategyPlan
        {
            get { return _lastStrategyPlan; }
            private set { _lastStrategyPlan = value; }
        }
        public StrategicMemorySnapshot StrategicMemorySnapshot =>
            _strategyMemory.Snapshot;

        public void ResetStrategicMemory()
        {
            _strategyMemory.Reset();
            _lastStrategyPlan = null;
            _lastMainlinePlan = null;
        }

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
            _heroPlaybook = new HeroPlaybookRegistry();
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
            _mainline = new MainlineStrategyPlanner();
            _strategyMemory = new StrategicMemory();
            _strategySequencePlanner = new StrategySequencePlanner();
            _compGuideCatalog = new CompGuideCatalog();
            _cardObjectDatabase = new CardObjectDatabase();
            _compGuideCatalog.LoadFromConfiguredRoot();
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
                _cardObjectDatabase.LoadFromConfiguredRoot();
                _heroPlaybook.LoadFromConfiguredRoot();
                // 设置HeroPower引用给SynergyEngine用于英雄-流派亲和计算
                _synergy.SetHeroPowerEngine(_heroPower);
            }
            catch
            {
                // 资源加载失败时引擎正常降级运行
            }
        }

        public void HydrateReplayRules(GameState state)
        {
            if (state == null) return;
            var season14 = Season14RulesEvaluator.GetShared();
            if (season14.IsLoaded && !season14.IsMechanicEnabled("anomaly"))
            {
                state.AnomalyContext = AnomalyContext.Empty;
                state.AnomalyId = "";
            }
            state.AnomalyContext = state.AnomalyContext ?? AnomalyContext.Empty;
            state.EffectiveRules = AnomalyRuleResolver.Resolve(
                state.AnomalyContext, AnomalyReg);
            state.EffectiveRules = TrinketEffectResolver.Resolve(
                state.ActiveTrinkets).ApplyTo(state.EffectiveRules);
        }

        public bool CanRefreshLeaveMeaningfulBudget(GameState state)
        {
            if (state == null) return false;
            var rules = state.EffectiveRules ?? EffectiveGameRules.Default;
            int refreshCost = GameRuleEvaluator.GetRefreshCost(
                state, state.HeroCardId, rules);
            if (refreshCost <= 0 || state.FreeRefreshCount > 0) return true;
            int gold = state.LiveGold >= 0 ? state.LiveGold : state.Gold;
            int remaining = gold - refreshCost;
            if (remaining < 0) return false;
            int minBuy = GameRuleEvaluator.GetPurchaseCost(
                state, new MinionData { Tier = 1 }, state.HeroCardId, rules);
            if (remaining >= minBuy) return true;
            if (state.TavernUpgradeCost >= 0
                && remaining >= state.TavernUpgradeCost)
                return true;
            if (state.ShopMinions != null)
            {
                foreach (var shop in state.ShopMinions)
                {
                    if (shop == null) continue;
                    int cost = GameRuleEvaluator.GetPurchaseCost(
                        state, shop, state.HeroCardId, rules);
                    if (remaining >= cost) return true;
                }
            }
            return false;
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

        public bool IsCoreComboCard(string cardId)
        {
            return _classifier != null && _classifier.IsAmplifier(cardId);
        }

        public bool IsResourceCard(string cardId)
        {
            var classification = GetClassification(cardId);
            if (!classification.HasValue) return false;
            return classification.Value.EconomyValue >= 0.35f
                || IsResourceRole(classification.Value.PrimaryRole);
        }

        public CardClassifier.CardRole GetPrimaryCardRole(string cardId)
        {
            var classification = GetClassification(cardId);
            return classification.HasValue
                ? classification.Value.PrimaryRole
                : CardClassifier.CardRole.Unknown;
        }

        public CardObjectFact GetCardObjectFact(string cardId)
        {
            if (_cardObjectDatabase == null || string.IsNullOrEmpty(cardId)) return null;
            CardObjectFact fact;
            try
            {
                return _cardObjectDatabase.TryGet(cardId, out fact) ? fact : null;
            }
            catch
            {
                return null;
            }
        }

        private CardClassifier.CardClassification? GetClassification(string cardId)
        {
            if (_classifier == null || string.IsNullOrEmpty(cardId)) return null;
            try { return _classifier.GetClassification(cardId); }
            catch { return null; }
        }

        // R220: 瓦尔登·晨拥——刷新后复制酒馆最高级随从并冻结(刷新具有免费复制价值)
        private static bool IsVardenRefreshHero(GameState state)
        {
            return state != null
                && (state.HeroCardId ?? "").StartsWith("BG22_HERO_004");
        }

        // R223: 布莱恩任务活跃(英雄=恐龙大师布莱恩 且 铜须未获得)
        private static bool IsBrannQuestActive(GameState state)
        {
            if (state == null || string.IsNullOrEmpty(state.HeroCardId)) return false;
            if (!state.HeroCardId.StartsWith("TB_BaconShop_HERO_43")) return false;
            if (state.BoardMinions != null)
                foreach (var m in state.BoardMinions)
                    if (m != null && (m.CardId == "BG_LOE_077" || m.CardId == "BG_LOE_077_G")) return false;
            if (state.HandMinions != null)
                foreach (var m in state.HandMinions)
                    if (m != null && (m.CardId == "BG_LOE_077" || m.CardId == "BG_LOE_077_G")) return false;
            return true;
        }

        // R223: 战听文本检测
        private static bool HasBattlecryText(MinionData card)
        {
            if (card == null || card.IsSpell) return false;
            var text = card.CardText ?? "";
            return text.Contains("战听");
        }

        private static bool IsResourceRole(CardClassifier.CardRole role)
        {
            return role == CardClassifier.CardRole.Economy
                || role == CardClassifier.CardRole.ValueGenerator
                || role == CardClassifier.CardRole.SpellSource
                || role == CardClassifier.CardRole.SpellCopier;
        }

        /// <summary>R206 移植: 机制价值评分(版本无关卡牌理解)</summary>
        /// 身材基准(tier 1-7: 4/6/6/9/10/13/18) + 关键词价值 + 种族匹配; 输出 0-1 归一
        private static double ComputeMechanicValue(MinionData card)
        {
            if (card == null || card.IsSpell) return 0d;
            int tier = card.Tier < 1 ? 1 : (card.Tier > 7 ? 7 : card.Tier);
            double[] baseMap = { 0, 4, 6, 6, 9, 10, 13, 18 };
            double s = ((card.Attack + card.Health) / baseMap[tier]) * 2.2;
            if (card.DivineShield) s += 1.2;
            if (card.Windfury || card.MegaWindfury) s += 1.2;
            if (card.Reborn) s += 1.0;
            if (card.Poisonous || card.Venomous) s += 1.0;
            if (card.Taunt) s += 0.4;
            var text = card.CardText ?? "";
            if (text.Contains("亡语")) s += 0.6;
            if (text.Contains("战听")) s += 0.6;
            if (text.Contains("回合结束时")) s += 0.6;
            if (text.Contains("战斗开始时")) s += 0.6;
            s = Math.Min(8, Math.Max(1.5, s));
            return (s - 1.5) / 6.5;
        }

        // ── IDecisionProvider ──

        /// <summary>
        /// 获取当前回合最优动作（深度=1 贪心）。
        /// 返回 null 表示没有可行动作或不在对局中。
        /// </summary>
        public GameAction GetBestAction(GameState state)
        {
            if (state == null || !state.GameActive) return null;
            NormalizeLiveGold(state);
            _bestTwoStepAction = null; _bestTwoStepState = null;
            BestTwoStepAction = null; BestTwoStepState = null;

            var actions = _enumerator.Enumerate(
                state, state.HeroCardId,
                includeTrinket: state.TrinketOffer != null && state.TrinketOffer.Count > 0);
            var effectiveRules = state.EffectiveRules ?? EffectiveGameRules.Default;
            var mainlinePlan = _mainline.Analyze(
                state, _compGuideCatalog, _compLock.Detect(state));
            _lastMainlinePlan = mainlinePlan;
            try
            {
                var memoryBeforePlan = _strategyMemory.Snapshot;
                _lastStrategyPlan = _strategySequencePlanner.Build(
                    state, memoryBeforePlan, mainlinePlan);
            }
            catch
            {
                _lastStrategyPlan = null;
            }
            try
            {
                _strategyMemory.Observe(state, mainlinePlan, _lastStrategyPlan);
            }
            catch
            {
                // Shadow memory must never break the visible legacy path.
            }
            bool MainlineBlocksUpgrade()
            {
                return _mainline.ShouldSkipUpgrade(state, mainlinePlan);
            }

            var synergyTribeBuy = TryPickSynergyTribeBuy(
                state, actions, mainlinePlan);
            if (synergyTribeBuy != null)
                return synergyTribeBuy;

            // ── 混合决策规则层（28/28 基准校准 v1.1） ──

            int shopMax = MaxShopTier(state);
            int boardMax = MaxBoardTier(state);
            bool shopGood = ShopImprovesBoard(state);
            int upCost = GetEffectiveUpgradeCostOrUnavailable(state);
            int maxTavernTier = state.EffectiveRules != null
                ? state.EffectiveRules.MaxTavernTier : 6;
            float boardPwr = _fe.ComputeBoardPower(state.BoardMinions);
            // R227 战力速率: 本回合相对上回合的板面战力增量(仅同一酒馆等级内有效,
            // 跨等级回合因牌池变化速率失真, 仅作方向参考: <0 = 战力在倒退)
            // R238 对抗性审查修复: 原实现无条件覆盖 _lastTurnBoardPower——实机一回合
            // 多次调用 GetBestAction(1365/1376/1387), 第二次调用就把 _lastTurnNumber
            // 写成当前回合, 下回合 state.Turn == _lastTurnNumber+1 恒不成立 →
            // boardRate 恒 0 → 速率豁免失效(低板面高动能成功局被误伤)。
            // 现在仅在回合号变化时更新(用 _trendRecordedTurn 记录已记录的回合)。
            float boardRate = 0f;
            if (state.Turn != _lastTurnNumber)
            {
                if (_lastTurnBoardPower >= 0f && state.Turn == _lastTurnNumber + 1
                    && _lastTurnNumber > 0 && state.TavernTier == _lastTurnTier)
                {
                    boardRate = boardPwr - _lastTurnBoardPower;
                }
                _lastTurnBoardPower = boardPwr;
                _lastTurnNumber = state.Turn;
                _lastTurnTier = state.TavernTier;
                // R238: 趋势队列与速率同回合门控(防同回合多次调用污染队列)
                _powerTrendQueue.Enqueue(boardPwr);
                while (_powerTrendQueue.Count > 3) _powerTrendQueue.Dequeue();
            }
            // R230 趋势记忆: 维护3回合战力队列 → 增速/停滞/下滑标志
            // 25局验证校准: 单帧下滑与失败相关性弱(成功局转型也下滑, 米罗克r1下滑3次);
            // 连续下滑(3回合窗口净跌)才是崩盘信号(德纳修斯r4升6本后速率-3.1)。
            float powerTrend = 0f;
            bool powerStagnant = false;
            bool powerDeclining = false;
            if (_powerTrendQueue.Count >= 3)
            {
                float[] trend = _powerTrendQueue.ToArray();
                powerTrend = (trend[2] - trend[0]) / 2f;      // 3回合平均增速
                powerStagnant = powerTrend < 0.5f && powerTrend > -0.5f
                    && state.Turn >= 6;                        // 停滞(增速≈0, 中后期)
                powerDeclining = trend[2] < trend[0] - 1.0f
                    && state.Turn >= 6;                        // 3回合净跌>1.0(连续下滑)
            }
            bool upgradeLethal = OpponentModel.IsLevelUpLethal(
                state.Health, state.Turn);
            // 升本金币合理性: 升本后至少留1金(除非满场无法买牌)
            // R243: 首升豁免——真实玩家 T2 首升率 64%(回放统计, 升完 0 金);
            // 原规则 T2 有4金升2本4费 → 0金剩余 → canUpgrade 恒 false,
            // T2 结构性无法首升, 全链路升级滞后 3-4 回合(模拟器全量对比证实);
            // 首升(1→2本)豁免留金要求, 对齐真实节奏。
            bool goldReserveOk = state.TavernTier == 1
                || state.Gold - upCost >= 1
                || state.BoardMinions.Count >= state.MaxBoardSlots;
            bool canUpgrade = upCost != int.MaxValue && state.TavernTier < maxTavernTier
                && state.Gold >= upCost
                && !upgradeLethal
                && goldReserveOk;

            // ══ 情境检测 (v2.0自适应层, R230 趋势感知) ══
            var contextResult = _contextDetector.Detect(
                state, _fe, powerTrend, powerStagnant, powerDeclining);
            var heroStrat = _heroPower.GetStrategyForPower(
                state.HeroCardId, state.HeroPowerCardId);
            var adjusted = _strategyAdjuster.GetAdjustedStrategy(contextResult, _vf.Weights, heroStrat);

            // DESPERATE模式: 阻止升本
            if (adjusted.BlockLevelUp)
            {
                actions.RemoveAll(a => a.Type == ActionType.Upgrade);
            }

            // ── R0: 激进升本曲线 (核心修复: 升本优先于买牌) ──
            // 期望曲线: T2升2(4费), T5升3(7费), T7升4(9费), T9升5, T10+升6
            // R245 审查(回滚): 曾尝试T7+提前到5本, 模拟器全量否决
            // (T7升5本名次5.32 vs 未升4.14); 维持 T8→5本。
            int expectedLv = state.Turn <= 1 ? 1 : state.Turn <= 3 ? 2 : state.Turn <= 5 ? 3
                : state.Turn <= 7 ? 4 : state.Turn <= 8 ? 5 : 6;
            int tierGap = expectedLv - state.TavernTier;
            bool boardFullR0 = state.BoardMinions.Count >= state.MaxBoardSlots;
            if (canUpgrade && state.TavernTier < maxTavernTier && tierGap >= 1)
            {
                // 等级落后≥2: 即使血量低也要升本 (高本牌=战力倍增器)
                // DESPERATE时不执行此规则
                // R238 对抗性审查修复: 落后追赶也需最低板面底线(审查C: 1随从板面
                // 0.3 直接升4本=失败局高发)。升4本+需板面≥安全线50%(追赶放宽但
                // 有底线, 板面太差升了也打不过), 升3本仍放宽。
                if (tierGap >= 2 && contextResult.Type != SituationType.DESPERATE)
                {
                    bool r0Gap2Safe = true;
                    if (state.TavernTier + 1 >= 4
                        && FeatureExtractor.LevelUpSafePower.ContainsKey(state.TavernTier + 1))
                        r0Gap2Safe = boardPwr >= FeatureExtractor.LevelUpSafePower[state.TavernTier + 1] * 0.5;
                    if (r0Gap2Safe)
                    {
                        foreach (var a in actions)
                            if (a.Type == ActionType.Upgrade) return a;
                    }
                }
                // 等级落后1级 + (板面≥3随从 或 血量>20 或 满场) → 升本
                // R229: 套 R227 板面安全线——真实对局实证(迦拉克隆rank6): T3 板面0.6
                // 血量30 触发"血多就升"漏洞, 升3本后板面空转 3 回合(0.6→0.9→0.9)。
                // 升4本+需板面≥安全线(3.0/6.5/10.0); 升3本不拦(与 R-time 一致:
                // 瓦尔登rank4板面0升3本也赢, 判别力弱); 全量25局验证: 失败局18次
                // 被拦 vs 成功局仅9次(低板面高动能/技能流英雄, 均延迟非禁止)。
                // 速率豁免与 R-time 一致: 板面不足但速率≥0.5(升6本≥1.0)允许升——
                // 防误伤低板面高动能成功局(巴罗夫rank1升4本速率1.6/米罗克1.6)。
                bool r0PowerSafe = true;
                int r0Next = state.TavernTier + 1;
                if (r0Next >= 4 && FeatureExtractor.LevelUpSafePower.ContainsKey(r0Next))
                {
                    double r0SafePwr = FeatureExtractor.LevelUpSafePower[r0Next];
                    float r0RateNeed = r0Next == 6 ? 1.0f : 0.5f;
                    r0PowerSafe = boardPwr >= r0SafePwr || boardRate >= r0RateNeed;
                }
                if ((state.BoardMinions.Count >= 3 || state.Health > adjusted.DangerHp || boardFullR0)
                    && r0PowerSafe && !MainlineBlocksUpgrade())
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
                // R238 对抗性审查修复: 绝境翻盘保留但需最低板面(板面<1.0 升了也
                // 打不过, 不如买牌); 板面≥1.0 时搏高本翻盘仍合理(审查C建议)。
                bool r1BoardFull = state.BoardMinions.Count >= state.MaxBoardSlots;
                if ((upCost <= 3 || r1BoardFull) && state.Gold >= upCost && state.BoardMinions.Count >= 2
                    && state.TavernTier < 5 && boardPwr >= 1.0f)
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
                                {
                                    if (IsSellWorthwhile(state, weakIdx))
                                        return a;
                                }
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

            var highPriorityHeroPower = TryGetHighPriorityHeroPlaybookAction(
                state, actions, mainlinePlan);
            if (highPriorityHeroPower != null)
                return highPriorityHeroPower;

            // R2: T2-T3 1本 → 升2本 (84局校准: 玩家T2升2本)
            if (state.TavernTier == 1 && state.Turn >= 2 && state.Turn <= 3)
            {
                int upCostR2 = GetEffectiveUpgradeCostOrUnavailable(state);
                if (state.Gold >= upCostR2 && state.BoardMinions.Count >= 1
                    && !MainlineBlocksUpgrade())
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
                if (state.Gold >= upCostR25 && canUpgrade && !shopTooGoodToSkip
                    && !MainlineBlocksUpgrade())
                {
                    foreach (var a in actions)
                        if (a.Type == ActionType.Upgrade) return a;
                }
            }

            // R2.7: T6-T7 3本 → 升4本 (玩家校准: T7升4本, 板面≥4)
            // R227: 加板面战力安全线——失败局(rank5-8)升4本时板面均值仅2.0,
            // 成功局均值5.2; 板面<3.0 升本=速败高发区, 延迟升本先买牌稳场。
            if (state.TavernTier == 3 && state.Turn >= 6 && state.Turn <= 7
                && state.BoardMinions.Count >= 4
                && boardPwr >= (FeatureExtractor.LevelUpSafePower.ContainsKey(4)
                    ? FeatureExtractor.LevelUpSafePower[4] : 3.0))
            {
                int upCostR27 = GetEffectiveUpgradeCostOrUnavailable(state);
                if (state.Gold >= upCostR27 && canUpgrade
                    && !MainlineBlocksUpgrade())
                {
                    foreach (var a in actions)
                        if (a.Type == ActionType.Upgrade) return a;
                }
            }

            // R2.8: T8-T10 4本 → 升5本 (玩家校准: T10升5本, 板面≥5)
            // R227: 加板面战力安全线——失败局升5本时板面均值5.5 vs 成功局7.4,
            // 板面<6.5 升5本=速败高发区, 延迟升本先稳场。
            if (state.TavernTier == 4 && state.Turn >= 8 && state.Turn <= 10
                && state.BoardMinions.Count >= 5
                && boardPwr >= (FeatureExtractor.LevelUpSafePower.ContainsKey(5)
                    ? FeatureExtractor.LevelUpSafePower[5] : 6.5))
            {
                int upCostR28 = GetEffectiveUpgradeCostOrUnavailable(state);
                if (state.Gold >= upCostR28 && canUpgrade
                    && !MainlineBlocksUpgrade())
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
                    // 赢家的面包: 有板面目标时前中期购买，持续战力收益高于普通低费法术。
                    if ((cid.Contains("BG36_883")
                            || (sp.CardName != null
                                && sp.CardName.Contains("赢家的面包")))
                        && state.Turn <= 4
                        && state.BoardMinions.Count >= 1)
                    {
                        foreach (var a in actions)
                            if (a.Type == ActionType.BuySpell && a.TargetIndex == si) return a;
                    }
                    // 酒馆币(BG28_810): T1 cost1, 直接+1金 → 仅剩1铸币也要抓，有余钱仍可买
                    // 2026-08-19 B11 修复：回合1(3金)买酒馆币=花1得1净零，却抢走
                    // 买3费随从的节奏（用户实测：艾萨拉回合1"囤铸币被推荐直接使用"；
                    // bestAction=BuySpell 0.61 vs Shadow BuyMinion 0.79 不一致的根因）。
                    // 修正条件：买后仍买得起3费随从(gold-cost>=3) 或 仅剩1金不买浪费。
                    if (cid.Contains("BG28_810")
                        && (state.Gold == spellCost || state.Gold - spellCost >= 3))
                    {
                        foreach (var a in actions)
                            if (a.Type == ActionType.BuySpell && a.TargetIndex == si) return a;
                    }
                }
            }

            // R-time: 回合到达标准升本窗口 → 按时升本
            // (5000局数据: Heuristic仅凭按时升本即碾压复杂规则, 核心逻辑=时机到了就升)
            // R227: 升4本以上统一套板面战力安全线(阈值=成功局25%分位),
            // 低于安全线=速败高发区, 延迟升本先稳场; 不再用 *0.8 折扣(旧阈值1.8形同虚设)。
            int timeNextTier = Math.Min(state.TavernTier + 1, 6);
            if (FeatureExtractor.StandardLevelTurn.ContainsKey(timeNextTier))
            {
                int stdTurn = FeatureExtractor.StandardLevelTurn[timeNextTier];
                if (state.Turn >= stdTurn && state.BoardMinions.Count >= 1
                    && state.Gold >= upCost)
                {
                    // 升4本+需板面战力检查 (R227: 失败局升4本板面均值2.0 vs 成功局5.2;
                    // 升3本不拦——判别力弱, 瓦尔登rank4板面0升3本也赢, 误伤>收益)
                    bool powerSafe = true;
                    if (timeNextTier >= 4 && FeatureExtractor.LevelUpSafePower.ContainsKey(timeNextTier))
                    {
                        double safePwr = FeatureExtractor.LevelUpSafePower[timeNextTier];
                        // 后期(T>=11)升6本豁免: 米罗克T13升6本(板4.6)rank1吃鸡,
                        // 后期阵容转型升本不算速败; 速败集中在T7-T9升6本。
                        bool lateSix = timeNextTier == 6 && state.Turn >= 11;
                        // R227 速率约束(横向比对结论):
                        //   - 板面达标 → 直接放行(速率不否决: 巴罗夫rank1 T10升6本
                        //     板12.4速率-1.8是卖牌换阵假象, 仍吃鸡)。
                        //   - 板面不足 → 需速率达标才升: 失败局升4本板面均值2.0速率≈0
                        //     (克罗米rank6 -0.2/牛头人-0.8/米尔豪斯-0.8全速败);
                        //     成功局低板面升本全靠动能(rank4阮大师升5本速率1.3、
                        //     rank1巴罗夫升5本速率0.5)。升6本需动能>=1.0
                        //     (德纳修斯rank1停5本养到22.6; rank4升6本前速率0.1→战力倒退)。
                        float rateNeed = timeNextTier == 6 ? 1.0f : 0.5f;
                        bool rateOk = boardRate >= rateNeed;
                        powerSafe = lateSix || boardPwr >= safePwr || rateOk;
                    }
                    if (powerSafe && !MainlineBlocksUpgrade())
                    {
                        foreach (var a in actions)
                            if (canUpgrade && a.Type == ActionType.Upgrade) return a;
                    }
                    // 板面不足: 延迟升本, 优先买牌稳场
                }
            }

            // R3: 满场+店无高本 → 升本
            if (state.BoardMinions.Count >= state.MaxBoardSlots
                && shopMax <= state.TavernTier
                && !MainlineBlocksUpgrade())
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
                            {
                                if (IsSellWorthwhile(state, weakIdx))
                                    return a;
                            }
                    }
                }
            }

            // R4: 场面达标+店差 → 提前升本 (早于标准窗口)
            // R227: 提前升本同样套板面战力安全线(旧P10阈值0.9/1.3形同虚设,
            // 失败局在板面1~2时提前升4/5本→速败)。板面达标才允许抢窗口。
            bool earlyWindow = state.TavernTier < maxTavernTier
                && FeatureExtractor.StandardLevelTurn.ContainsKey(state.TavernTier + 1)
                && state.Turn < FeatureExtractor.StandardLevelTurn[state.TavernTier + 1];
            if (earlyWindow)
            {
                double r4Threshold = FeatureExtractor.LevelUpSafePower.ContainsKey(state.TavernTier + 1)
                    ? FeatureExtractor.LevelUpSafePower[state.TavernTier + 1] : 1.5;
                int r4BoardNeed = FeatureExtractor.LevelUpSafeBoardSize.ContainsKey(state.TavernTier + 1)
                    ? FeatureExtractor.LevelUpSafeBoardSize[state.TavernTier + 1] : 3;
                if (boardPwr >= r4Threshold && state.BoardMinions.Count >= r4BoardNeed
                    && shopMax <= state.TavernTier
                    && !MainlineBlocksUpgrade())
                {
                    foreach (var a in actions)
                        if (canUpgrade && a.Type == ActionType.Upgrade) return a;
                }
            }

            // R4.5: 落后对手→升本找核心翻盘 (放宽门槛: 健康>8, 留1金即可)
            // (438局数据: 赢家常在BP落后30%时升5/6本, 翻盘窗口2.8回合)
            // R238 对抗性审查修复: 低战力是触发条件但无绝对安全线——板面4.0升5本
            // 也是失败局高发模式(弱板面+落后双杀)。加最低翻盘线: 升5本需板面≥4.0,
            // 升6本需≥6.0(低于此线升了也打不过, 不如先买牌稳场)。
            float avgOppPwr = _fe.ComputeAvgOpponentPower(state.Opponents);
            if (avgOppPwr > 0 && boardPwr < avgOppPwr * 0.7f
                && state.TavernTier < maxTavernTier && state.Health > 8
                && state.Gold >= upCost && state.Turn >= 4)
            {
                int nextTierR45 = state.TavernTier + 1;
                double minComebackPower = nextTierR45 >= 6 ? 6.0 : 4.0;
                if (state.Gold - upCost >= 1 && boardPwr >= minComebackPower)
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

            // P7 2026-08(用户实测: 决赛阶段无针对意识, 对手成型后仍乱拿牌):
            // 后期决赛主动针对——不限于绝望差距: 回合>=9 且存活玩家<=2(决赛)或
            // 对手战力接近(ratio<0.85), 即使我方不绝望也应主动找强制交换机制牌(剧毒/烈毒/圣盾)。
            // 触发后: 剧毒/烈毒/圣盾买牌加分(见下方 endgameTechPivot 修正), 刷新找tech优先级提升。
            bool endgameTechPivot = false;
            if (state.Turn >= 9 && state.Health > 10)
            {
                int aliveCount = 0;
                if (state.Opponents != null)
                {
                    foreach (var op in state.Opponents)
                        if (op.Alive) aliveCount++;
                }
                float oppPowerE = _fe.ComputeAvgOpponentPower(state.Opponents);
                if (aliveCount <= 2 || (boardPwr > 0 && oppPowerE > 0 && (float)boardPwr / oppPowerE < 0.85f))
                    endgameTechPivot = true;
            }

            // R-triple-timing: 三连时序规则 — 真人数据91%先升本再合金
            // 主线思考覆盖: 如果三连是当前主线大节点且可凑钱拿牌, 不再强行“先升本再合金”
            int tripleIdx = FindTripleInShop(state);
            if (tripleIdx >= 0 && canUpgrade && state.TavernTier < maxTavernTier
                && !MainlineBlocksUpgrade())
            {
                foreach (var a in actions)
                    if (a.Type == ActionType.Upgrade) return a;
            }

            // R5: 三连检测 → 拿牌凑三连 + 评估发现奖励价值
            if (tripleIdx >= 0)
            {
                // 三连价值 = 金色卡加成(1.5~1.8x) + 发现奖励(高一级卡牌期望值)
                double tripleValue = EstimateTripleRewardValue(state, tripleIdx);
                int tripleCost = GameRuleEvaluator.GetPurchaseCost(
                    state, state.ShopMinions[tripleIdx], state.HeroCardId,
                    state.EffectiveRules ?? EffectiveGameRules.Default);
                if (tripleValue > 0.2 && state.Gold >= tripleCost)
                {
                    foreach (var a in actions)
                        if (a.Type == ActionType.BuyMinion && a.TargetIndex == tripleIdx)
                            return a;
                }
                if (tripleValue > 0.2 && state.Gold + 1 >= tripleCost)
                {
                    int weakIdx = FindWeakestBoardMinion(state, null);
                    if (weakIdx >= 0 && IsSellWorthwhile(state, weakIdx))
                    {
                        foreach (var a in actions)
                            if (a.Type == ActionType.SellMinion && a.TargetIndex == weakIdx)
                                return a;
                    }
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
            // R238 对抗性审查修复: 套 R227 安全线——店差不是升本的充分条件,
            // 板面不足时升本=速败(失败局高发模式), 升4本+需板面达标。
            if (shopMax > 0 && shopMax <= state.TavernTier
                && state.Health > 15 && (state.Gold - upCost) >= 1 && !shopGood
                && !MainlineBlocksUpgrade())
            {
                bool r8Safe = true;
                if (state.TavernTier + 1 >= 4
                    && FeatureExtractor.LevelUpSafePower.ContainsKey(state.TavernTier + 1))
                    r8Safe = boardPwr >= FeatureExtractor.LevelUpSafePower[state.TavernTier + 1];
                // 店极差(全<当前本)且升级余钱<3 → 优先刷新
                if (r8Safe
                    && !(shopMax < state.TavernTier && (state.Gold - upCost) < 3))
                {
                    foreach (var a in actions)
                        if (canUpgrade && a.Type == ActionType.Upgrade) return a;
                }
            }

            // R9: 店普通(==当前本)+场面弱+余钱充裕 → 升本（店不能改善场面时）
            // R238 对抗性审查修复: "场面弱"是触发条件但无最低门槛——板面0.5升4本
            // 也是失败局高发模式。升4本+需板面达标(升3本仍放宽, 与 R-time 一致)。
            if (shopMax > 0 && shopMax == state.TavernTier
                && boardPwr < 1.5f && (state.Gold - upCost) >= 2 && !shopGood
                && !MainlineBlocksUpgrade())
            {
                bool r9Safe = true;
                if (state.TavernTier + 1 >= 4
                    && FeatureExtractor.LevelUpSafePower.ContainsKey(state.TavernTier + 1))
                    r9Safe = boardPwr >= FeatureExtractor.LevelUpSafePower[state.TavernTier + 1];
                if (r9Safe)
                {
                    foreach (var a in actions)
                        if (canUpgrade && a.Type == ActionType.Upgrade) return a;
                }
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

                // Activate: 发动是 Season14 真实金币动作，必须参与统一评分；
                // 缺少效果语义时按统一软评分给出有界加成，避免被搜索当作“状态不变”忽略。
                if (action.Type == ActionType.Activate)
                {
                    int liveGold = state.LiveGold >= 0 ? state.LiveGold : state.Gold;
                    var activateScore = UnifiedActionScorer.Score(
                        "Activate",
                        action.Cost,
                        liveGold,
                        (int)boardPwr,
                        (int)_fe.ComputeAvgOpponentPower(state.Opponents),
                        state.Health,
                        state.Turn,
                        state.TavernTier,
                        0.5,
                        0.0,
                        true,
                        boardFull,
                        false);
                    if (activateScore.Score < 0) value *= 0.0;
                    else value += activateScore.Score * 1.5;
                }

                if (action.Type == ActionType.UseSpell)
                {
                    if (IsEconomyCard(action.CardId)) value += 0.35;
                    if (IsResourceCard(action.CardId))
                        value += state.Turn <= 5 ? 0.45 : (state.Turn <= 9 ? 0.30 : 0.15);
                    if (IsCoreComboCard(action.CardId))
                        value += state.Turn <= 9 ? 0.35 : 0.50;
                }

                if (action.Type == ActionType.PlayMinion)
                    value += 0.03 * state.HandMinions.Count;

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

                    // R231 记忆闭环: 核心卡/对子记忆保护——防止"买了核心又卖"瞎玩。
                    // StrategicMemory 已记录 ObservedCoreCardIds/ObservedPairCardIds
                    // (此前零消费=遗忘), 现在接入卖牌决策:
                    // 核心卡卖牌重罚×0.25; 对子卡卖牌重罚×0.35(凑三连中卖掉=白费积累)。
                    try
                    {
                        var memSnap = _strategyMemory.Snapshot;
                        if (memSnap != null && !string.IsNullOrEmpty(action.CardId))
                        {
                            string baseCid = NormalizeGoldenId(action.CardId);
                            if (memSnap.ObservedCoreCardIds.Contains(baseCid)
                                || memSnap.ObservedCoreCardIds.Contains(action.CardId))
                                sellMultiplier *= 0.25f;
                            else if (memSnap.ObservedPairCardIds.Contains(baseCid)
                                || memSnap.ObservedPairCardIds.Contains(action.CardId))
                                sellMultiplier *= 0.35f;
                        }
                    }
                    catch { /* 记忆保护失败不影响主决策 */ }

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
                {
                    bool hasAffordableShopCard = actions.Any(a =>
                        a.Type == ActionType.BuyMinion || a.Type == ActionType.BuySpell);
                    bool hasGoodShop = HasFreezeWorthKeepingShop(state);
                    if (!hasAffordableShopCard && hasGoodShop)
                        value *= 0.05f;  // 保留免费刷新，冻结好店
                    else if (hasGoodShop)
                        value *= 0.70f;  // 店已有好牌，不烧掉可累积的免费刷新
                    else
                        value *= (state.FreeRefreshCount > 5) ? 1.03 : 1.1;
                }

                // ── 早期刷新惩罚: T1-T5每块钱都关键, T1-T2绝对不刷 ──
                if (action.Type == ActionType.Refresh && state.Turn <= 2 && state.FreeRefreshCount == 0)
                    value *= 0.0;       // T1-T2: 绝对禁止付费刷新
                // 1.2.5 D1(A/B候选): 升本回合尾金刷新豁免 — 玩家在本回合升过本且商店无可买时,
                // 花尾金(1-2)刷新换店是真实打法(大帝T3/吉恩T6-7 跨局证据), 非"刷后买不起浪费"。
                // 仅 BOBCOACH_UPGRADE_TAIL_REFRESH=1 时豁免(供模拟器 A/B; 默认关闭不改变现行为)。
                bool upgradeTailRefresh = UPGRADE_TAIL_REFRESH_ENABLED
                    && state.LastUpgradeTurn == state.Turn && state.Gold < 3
                    && (state.ShopMinions == null || state.ShopMinions.Count == 0
                        || state.ShopMinions.All(sc => sc == null || sc.IsSpell || sc.Cost > state.Gold));
                if (action.Type == ActionType.Refresh && state.Turn == 3
                    && state.Gold - 1 < 3 && state.FreeRefreshCount == 0 && !upgradeTailRefresh)
                    value *= 0.05f;     // T3: 刷后买不起=几乎禁止(升本尾金刷新豁免)
                if (action.Type == ActionType.Refresh && state.Turn >= 3 && state.Turn <= 5
                    && state.FreeRefreshCount == 0 && !upgradeTailRefresh
                    && !IsVardenRefreshHero(state))   // R220: 瓦尔登刷新=技能复制最高级随从(免费+冻结), 不受这个惩罚
                    value *= 0.15f;     // T3-T5: 强惩罚(only free refresh acceptable; 升本尾金豁免)
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
                // R226: 前期(T1-T4)刷新偏差(英雄运营节奏指纹)
                if (action.Type == ActionType.Refresh && heroStrat.EarlyRefBias != 0f && state.Turn <= 4)
                    value *= (1.0f + heroStrat.EarlyRefBias * 0.15f);

                if (action.Type == ActionType.Refresh && lateGameWeightBoost && state.FreeRefreshCount == 0)
                {
                    // R230 全量3348局校准: T10+每回合刷新 吃鸡2.66 vs 前4 1.76 vs 出局0.46
                    // ——后期"目标性刷新"(锁流派+店无匹配)是吃鸡核心行为, 单调强判别。
                    // 分层增强: 硬锁+店无匹配×1.5 / 软锁+店无匹配×1.3 / 无锁不增强(避免
                    // R192 无脑放宽刷新无效的老问题——只有"有目标"才值得刷)。
                    bool compLocked = compLockResult.State != LockState.None;
                    bool hardLocked = compLockResult.State == LockState.Hard;
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
                        value *= hardLocked ? 1.5f : 1.3f; // 流派锁+店无匹配 → 目标性刷新
                    else if (compLocked)
                        value *= 1.0f;                      // 已锁+店有匹配 → 先买, 不刷
                    else
                        value *= 0.85f;                     // 无锁 → 刷的价值低, 降权
                }

                // ── 高本买低本惩罚 ──
                if (action.Type == ActionType.BuyMinion && state.TavernTier >= 4
                    && action.TargetIndex < state.ShopMinions.Count
                    && state.ShopMinions[action.TargetIndex].Tier <= state.TavernTier - 2)
                    value *= 0.7;

                // ── 三连加分(含发现奖励) ──
                if (action.Type == ActionType.BuyMinion && tripleIdx >= 0
                    && action.TargetIndex == tripleIdx) value *= 1.6;  // 1.5x金色+0.1x发现奖励

                // ── R228/R231 凑对子加权(滚雪球): 板面+手牌已有1-2张同名 → 买店里同名牌凑三连 ──
                // 数据依据: 本赛季279局 firestone 终局板面, 成功局三连数2.57 vs 失败局1.24;
                // R231 全量3348局: 74%局终局留对子但仅9%合三连 → "记住对子不追第三张"。
                // 差一张三连加权 1.5→2.2(第三张的发现奖励是滚雪球核心, 必须强过买新卡);
                // 买第二张 1.3→1.4(对子起步积累)。
                if (action.Type == ActionType.BuyMinion
                    && action.TargetIndex < state.ShopMinions.Count)
                {
                    var shopCardForPair = state.ShopMinions[action.TargetIndex];
                    if (shopCardForPair != null && !shopCardForPair.IsSpell)
                    {
                        int sameCount = CountSameCardAcrossBoardAndHand(state, shopCardForPair.CardId);
                        if (sameCount >= 2 && tripleIdx < 0) value *= 2.2f;   // 差1张三连: 强优先
                        else if (sameCount == 1) value *= 1.4f;               // 买第二张凑对子
                    }
                }

                // ── R231 对子记忆→刷新: 记得在凑对子时, 刷新找第三张更有价值 ──
                // 全量3348局: 74%局留对子但仅9%合三连 → 记忆了对子却不去找第三张。
                // 板面/手牌有对子(记忆 ObservedPairCardIds 或实时 CountSame)且店里
                // 无同名第三张时, 刷新加权(找第三张比买新卡更值——发现奖励滚雪球)。
                if (action.Type == ActionType.Refresh && state.Turn >= 4)
                {
                    bool chasingTriple = false;
                    try
                    {
                        var memSnap2 = _strategyMemory.Snapshot;
                        chasingTriple = memSnap2 != null && memSnap2.ObservedPairCardIds.Count > 0;
                    }
                    catch { }
                    if (!chasingTriple && state.BoardMinions != null)
                    {
                        // 实时兜底: 板面有对子
                        var boardCounts2 = new Dictionary<string, int>(StringComparer.Ordinal);
                        foreach (var bm2 in state.BoardMinions)
                        {
                            if (bm2 == null || string.IsNullOrEmpty(bm2.CardId) || bm2.Golden) continue;
                            string bid2 = NormalizeGoldenId(bm2.CardId);
                            int bc2;
                            boardCounts2.TryGetValue(bid2, out bc2);
                            boardCounts2[bid2] = bc2 + 1;
                        }
                        foreach (var kv in boardCounts2)
                            if (kv.Value >= 2) { chasingTriple = true; break; }
                    }
                    if (chasingTriple && !shopGood)
                        value *= 1.2f;   // 追三连+店差 → 刷新找第三张
                }

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
                // P7: 决赛主动针对——不绝望但已进决赛/对手接近时, 剧毒/烈毒/圣盾仍加分(力度低于绝望)
                if (endgameTechPivot && action.Type == ActionType.BuyMinion
                    && action.TargetIndex < state.ShopMinions.Count)
                {
                    var techCard2 = state.ShopMinions[action.TargetIndex];
                    if (techCard2.Poisonous)
                        value *= 2.2;          // 剧毒：决赛针对最强
                    else if (techCard2.Venomous)
                        value *= 1.7;          // 烈毒
                    else if (techCard2.DivineShield)
                        value *= 1.35;         // 圣盾
                }
                if (endgameTechPivot && action.Type == ActionType.Refresh && state.Turn >= 9)
                    value *= 1.35;  // 决赛刷新找tech卡(力度低于绝望1.8)
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

                // ── V2.2 机制价值修正(R206移植, 版本无关卡牌理解) ──
                // 模拟器验证: 无权重卡机制评分使 A 轨均排 5.14→4.32。插件用 HearthDb 语义无权重表盲区, 但机制价值(身材/关键词)是通用信号——新卡/冷门卡也能凭自身机制评估强度
                if (action.Type == ActionType.BuyMinion && action.TargetIndex < state.ShopMinions.Count)
                {
                    var mechCard = state.ShopMinions[action.TargetIndex];
                    double mechVal = ComputeMechanicValue(mechCard);
                    if (mechVal > 0d) value *= (1.0 + mechVal * 0.25);
                }
                // R223: 任务型英雄感知——布莱恩(买 4 战听随从 → 获取铜须): 任务未完成时战听随从加权
                if (action.Type == ActionType.BuyMinion && action.TargetIndex < state.ShopMinions.Count
                    && IsBrannQuestActive(state))
                {
                    var questCard = state.ShopMinions[action.TargetIndex];
                    if (HasBattlecryText(questCard)) value *= 1.3f; // 任务进度优先
                }
                // R226: 前期(T1-T4)买随从偏差(英雄运营节奏指纹)
                if (action.Type == ActionType.BuyMinion && heroStrat.EarlyBuyBias != 0f && state.Turn <= 4)
                    value *= (1.0f + heroStrat.EarlyBuyBias * 0.25f);

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
                {
                    value += heroStrat.UpgradeValueBias + adjusted.LevelUpBias;
                    // R225: 数据驱动英雄升级激进度(1281局校准 0.62-1.15)
                    if (heroStrat.LevelAggression > 0.05f && heroStrat.LevelAggression != 1.0f)
                        value *= heroStrat.LevelAggression;
                    // R224: 克罗米前期升本抑制(历史 21 局 T1-T3 升本 0/21)
                    if (state.Turn <= 4 && (state.HeroCardId ?? "").StartsWith("BG34_HERO_001"))
                        value *= 0.25;
                    // R225: 数据驱动前期升本抑制窗口(阿凯/67号等保场面型)
                    if (heroStrat.EarlyUpgradeSuppressTurn > 0 && state.Turn <= heroStrat.EarlyUpgradeSuppressTurn)
                        value *= 0.4;
                }
                if (action.Type == ActionType.Refresh)
                    value += heroStrat.RefreshValueBias;
                if (action.Type == ActionType.BuyMinion)
                    value += heroStrat.BuyValueBias;
                if (action.Type == ActionType.UseHeroPower)
                {
                    value = AdjustHeroPowerValue(state, action, value);
                }
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
                    bool hasScoutTriplePrep = false;
                    // P-玛维 2026-08(用户实测: 板面3对1星+商店耐心的侦查员, 出售发现1星找3连, 算法未提示冻结):
                    // 准备型三连——板面对子>=2 且商店有侦查员(出售发现1星, 每回合提升)时冻结备三连。
                    if (state.BoardMinions != null && state.BoardMinions.Count >= 4)
                    {
                        var pairCounts = state.BoardMinions.Where(m => m != null && !string.IsNullOrEmpty(m.CardId))
                            .GroupBy(m => m.CardId).Count(g => g.Count() >= 2);
                        bool hasScout = state.ShopMinions != null && state.ShopMinions.Any(s =>
                            s != null && (s.CardId == "BG24_715" || s.CardId == "BG24_715_G"
                                || (s.CardText ?? "").Contains("出售本随从") && (s.CardText ?? "").Contains("发现一个等级1")));
                        hasScoutTriplePrep = pairCounts >= 2 && hasScout;
                    }
                    bool hasCompCoreUnaffordable = false;
                    bool preserveFreeRefresh = state.FreeRefreshCount > 0
                        && state.Gold < 3
                        && HasFreezeWorthKeepingShop(state);
                    if (compLockResult != null && compLockResult.State != LockState.None && state.Gold < 3)
                    {
                        string domTribe = compLockResult.DominantTribe ?? "";
                        hasCompCoreUnaffordable = state.ShopMinions.Any(s =>
                            !string.IsNullOrEmpty(s.Tribe) && !string.IsNullOrEmpty(domTribe)
                            && s.Tribe.Contains(domTribe));
                    }

                    if (state.Turn <= 3)
                    {
                        bool eudoraEarly = HeroPowerRuleCatalog.IsEudoraHero(
                            state.HeroCardId)
                            && state.Gold < 3;
                        value *= (eudoraEarly || preserveFreeRefresh)
                            && (hasTripleTarget
                                || hasCompCoreUnaffordable
                                || HasEudoraWorthFreezing(state))
                                ? 1.35f
                                : 0.01f;  // T1-T3几乎从不冻结，尤朵拉保留1金挖宝除外
                    }
                    else if ((hasTripleTarget || hasScoutTriplePrep) && state.Gold < 3)
                        value *= 1.5f;   // 三连缺钱/侦查员备三连 → 推荐冻结
                    else if (hasCompCoreUnaffordable)
                        value *= 1.3f;   // 核心牌缺钱 → 推荐冻结
                    else if (preserveFreeRefresh)
                        value *= 1.3f;   // 保留免费刷新 → 推荐冻结好店
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

                // Season14: comp guide core weight as bounded additive signal after all multipliers.
                if (action.Type == ActionType.BuyMinion
                    && action.TargetIndex < state.ShopMinions.Count)
                {
                    value += CompGuideWeightBonus(
                        state.ShopMinions[action.TargetIndex], state);
                }

                // ── 主线策略层: 核心节点/资源分配/升本与出售的长期判断 ──
                value = _mainline.Adjust(value, state, action, mainlinePlan);

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
                int sellIdx = bestAction.TargetIndex;
                if (!IsSellWorthwhile(state, sellIdx))
                {
                    var bestBuy = actions.Where(a => a.Type == ActionType.BuyMinion).FirstOrDefault();
                    if (bestBuy != null) return bestBuy;
                    var bestRefresh = actions.Where(a => a.Type == ActionType.Refresh).FirstOrDefault();
                    if (bestRefresh != null) return bestRefresh;
                    bestAction = null;
                }
            }

            // ══ V1.3 二步前瞻入主搜索: top-3动作 × 第二步穷举 ══
            const double TWO_STEP_DISCOUNT = 0.85f; // 第二步价值折扣 (与 rules.json 一致)
            const double TWO_STEP_THRESHOLD = 1.03;   // 需比单步高8%才覆盖
            double bestTwoStepCombined = double.NegativeInfinity;
            GameAction bestTwoStepStep1 = null;
            GameAction bestTwoStepStep2 = null;
            GameState bestTwoStepS1State = null;
            bool twoStepExplored = false;

            bool canExploreTwoStep = !ShouldPreserveHeroPlaybookAction(
                    state, bestAction)
                && (state.Gold >= 1
                    || actions.Any(a => a.Type == ActionType.SellMinion
                        && IsSellWorthwhile(state, a.TargetIndex)));
            if (canExploreTwoStep)
            {
                topActions.Sort((a, b) => b.Value.CompareTo(a.Value));
                int explored = 0;
                foreach (var (a1, v1, s1) in topActions)
                {
                    if (explored >= 3) break;
                    if (a1.Type == ActionType.Upgrade || a1.Type == ActionType.PickTrinket) continue;

                    explored++;
                    var step2Actions = _enumerator.Enumerate(
                        s1, state.HeroCardId, includeTrinket: false);
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
                    && bestTwoStepCombined > bestValue * TWO_STEP_THRESHOLD
                    && (bestTwoStepStep1.Type != ActionType.SellMinion
                        || IsSellWorthwhile(state, bestTwoStepStep1.TargetIndex)))
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
                && canExploreTwoStep)
            {
                var nextState = _sim.Simulate(state, bestAction);
                if (nextState.Gold >= 1)
                {
                    var step2Actions = _enumerator.Enumerate(
                        nextState, state.HeroCardId, includeTrinket: false);
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
            if (CardPoolSampler.IsInitialized && state.Gold >= 3
                && state.BoardMinions.Count < 7
                && !ShouldPreserveHeroPlaybookAction(state, bestAction))
            {
                try
                {
                    var mctsResult = _mcts.Search(state, includeTrinket: false);
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

            // MCTS/搜索后仍可能覆盖出售动作；最终统一执行出售质量门禁。
            if (bestAction != null && bestAction.Type == ActionType.SellMinion
                && !IsSellActionValid(state, bestAction))
            {
                bestAction = null;
            }

            if ((bestAction == null || bestAction.Type == ActionType.FreezeShop)
                && ShouldUseMethodicalMadnessNow(state, mainlinePlan, bestAction))
            {
                var methodicalMadness = TryGetMethodicalMadnessUse(state, actions);
                if (methodicalMadness != null)
                {
                    bestAction = methodicalMadness;
                    _bestTwoStepAction = null;
                    _bestTwoStepState = null;
                }
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
                                || (sc.CardName != null && (sc.CardName.Contains("铸币")
                                    || sc.CardName.Contains("金币")
                                    || sc.CardName.Contains("币")));
                            if (isEcon) return a;
                        }
                    }
                }
            }

            BestTwoStepAction = _bestTwoStepAction;
            BestTwoStepState = _bestTwoStepState;
            return bestAction;
        }

        public bool IsSellActionValid(GameState state, GameAction action)
        {
            if (state == null || action == null
                || action.Type != ActionType.SellMinion)
                return false;
            return IsSellWorthwhile(state, action.TargetIndex);
        }

        private double AdjustHeroPowerValue(
            GameState state,
            GameAction action,
            double value)
        {
            if (state == null || action == null) return value;
            // R230 2026-08(用户实测: 玛维'锁牌'技能节奏不会——插件侧技能评分过低, 开局组合不推荐):
            // 玛维技能=锁商店一张牌2回合后免费获得(本质是预购+提前锁定), 前期(板面空/商店有好牌)价值最高。
            // 评分加分: 锁牌相当于锁定未来价值; 开局 T1-T3 商店有值得锁的牌时大幅加分。
            if ((state.HeroCardId ?? "").Contains("TB_BaconShop_HERO_62"))
            {
                bool shopHasWorth = state.ShopMinions != null && state.ShopMinions.Any(s =>
                    s != null && (s.Tier >= 2 || (s.Attack + s.Health) >= 8
                        || (s.CardText ?? "").Contains("核心") || (s.CardText ?? "").Contains("发现")));
                if (state.Turn <= 3 && shopHasWorth)
                    value += 0.45;  // 开局锁好牌: 预购价值高
                else if (shopHasWorth)
                    value += 0.25;  // 常规: 商店有好牌时锁
                else
                    value += 0.10;  // 商店没好牌: 低价值但保持频率
            }
            value += _heroPower.GetStrategyForPower(
                state.HeroCardId, action.CardId).PowerValueBias;
            var playbook = EvaluateHeroPowerPlaybook(state, action);
            if (playbook == null) return value;
            if (ShouldDeferHeroPlaybookForVisiblePriority(
                    state, _lastMainlinePlan)
                && playbook.ActionValueBias >= 0.8)
                return value * 0.0;
            return playbook.ShouldUse
                ? value + playbook.ActionValueBias
                : value * 0.0;
        }

        private HeroPlaybookDecision EvaluateHeroPowerPlaybook(
            GameState state,
            GameAction action)
        {
            if (state == null || action == null) return null;
            var playbookProfile = _heroPlaybook != null
                ? _heroPlaybook.GetProfileForPower(
                    state.HeroCardId, action.CardId)
                : null;
            if (playbookProfile == null) return null;

            HeroPowerState selectedPower = state.HeroPowers != null
                ? state.HeroPowers.FirstOrDefault(power =>
                    power != null && power.CardId == action.CardId)
                : null;
            int powerCost = selectedPower != null
                ? (selectedPower.Cost > 0
                    ? selectedPower.Cost
                    : (state.HeroPowerCost >= 0 ? state.HeroPowerCost : selectedPower.Cost))
                : state.HeroPowerCost;
            bool exhausted = selectedPower != null
                ? selectedPower.Exhausted
                : state.HeroPowerExhausted;
            int liveGold = state.LiveGold >= 0
                ? state.LiveGold : state.Gold;
            bool hasTarget = state.BoardMinions.Count > 0
                || state.ShopMinions.Count > 0
                || action.RequiresTarget == false;

            var playbook = HeroPlaybookDecisionService.Decide(
                playbookProfile,
                playbookProfile.PowerType,
                powerCost,
                liveGold,
                state.Turn,
                state.TavernTier,
                state.Health,
                state.BoardMinions.Count,
                state.ShopMinions.Count,
                exhausted,
                hasTarget);
            return playbook;
        }

        private GameAction TryGetHighPriorityHeroPlaybookAction(
            GameState state,
            List<GameAction> actions,
            MainlinePlan mainlinePlan)
        {
            if (state == null || actions == null) return null;
            if (ShouldDeferHeroPlaybookForVisiblePriority(state, mainlinePlan))
                return null;
            foreach (var action in actions)
            {
                if (action == null
                    || action.Type != ActionType.UseHeroPower)
                    continue;
                var playbook = EvaluateHeroPowerPlaybook(state, action);
                if (playbook != null
                    && playbook.ShouldUse
                    && playbook.ActionValueBias >= 0.8)
                    return action;
            }
            return null;
        }

        private bool ShouldPreserveHeroPlaybookAction(
            GameState state,
            GameAction action)
        {
            if (action == null || action.Type != ActionType.UseHeroPower)
                return false;
            if (ShouldDeferHeroPlaybookForVisiblePriority(state, _lastMainlinePlan))
                return false;
            var playbook = EvaluateHeroPowerPlaybook(state, action);
            return playbook != null
                && playbook.ShouldUse
                && playbook.ActionValueBias >= 0.8;
        }

        private bool ShouldDeferHeroPlaybookForVisiblePriority(
            GameState state,
            MainlinePlan mainlinePlan)
        {
            if (state == null) return true;
            if (mainlinePlan == null) mainlinePlan = _lastMainlinePlan;

            var rules = state.EffectiveRules ?? EffectiveGameRules.Default;
            int gold = state.LiveGold >= 0 ? state.LiveGold : state.Gold;
            if (state.Turn <= 5
                && state.ShopMinions != null
                && mainlinePlan != null
                && mainlinePlan.CoreCardIds != null)
            {
                foreach (var shop in state.ShopMinions)
                {
                    if (shop == null || shop.IsSpell) continue;
                    if (!mainlinePlan.CoreCardIds.Contains(shop.CardId)) continue;
                    int cost = GameRuleEvaluator.GetPurchaseCost(
                        state, shop, state.HeroCardId, rules);
                    if (gold >= cost) return true;
                }
            }

            if (state.Turn <= 5 && state.ShopMinions != null)
            {
                foreach (var shop in state.ShopMinions)
                {
                    if (shop == null || shop.IsSpell) continue;
                    int cost = GameRuleEvaluator.GetPurchaseCost(
                        state, shop, state.HeroCardId, rules);
                    if (gold >= cost
                        && TripleRuleEvaluator.CompletesGolden(
                            state, shop.CardId, rules))
                        return true;
                }
            }

            int upCost = GetEffectiveUpgradeCostOrUnavailable(state);
            if (state.Turn >= 2 && state.Turn <= 5
                && upCost != int.MaxValue
                && state.TavernTier < rules.MaxTavernTier
                && gold >= upCost
                && !_mainline.ShouldSkipUpgrade(
                    state, mainlinePlan ?? _lastMainlinePlan)
                && !OpponentModel.IsLevelUpLethal(state.Health, state.Turn))
            {
                int requiredBoard = state.TavernTier == 1 ? 1
                    : state.TavernTier == 2 ? 3 : 4;
                if (state.BoardMinions != null
                    && state.BoardMinions.Count >= requiredBoard)
                    return true;
            }
            return false;
        }

        private bool ShouldUseMethodicalMadnessNow(
            GameState state,
            MainlinePlan mainlinePlan,
            GameAction currentBest)
        {
            if (state == null || state.ShopMinions == null
                || state.BoardMinions == null || state.Turn < 6)
                return false;
            if (currentBest != null && currentBest.Type != ActionType.FreezeShop)
                return false;
            if (mainlinePlan != null
                && (mainlinePlan.ShouldBuyCoreFirst
                    || mainlinePlan.HasCoreInShop
                    || mainlinePlan.HasTripleInShop))
                return false;
            if (state.ShopMinions.Count(shop => shop != null && !shop.IsSpell) < 2)
                return false;
            if (HasFreezeWorthKeepingShop(state))
                return false;

            var rules = state.EffectiveRules ?? EffectiveGameRules.Default;
            int gold = state.LiveGold >= 0 ? state.LiveGold : state.Gold;
            bool hasAffordableBuy = state.BoardMinions.Count < state.MaxBoardSlots
                && state.ShopMinions.Any(shop => shop != null
                    && !shop.IsSpell
                    && gold >= GameRuleEvaluator.GetPurchaseCost(
                        state, shop, state.HeroCardId, rules));
            if (hasAffordableBuy)
                return false;

            int upCost = GetEffectiveUpgradeCostOrUnavailable(state);
            int maxTier = rules.MaxTavernTier;
            if (upCost != int.MaxValue
                && state.TavernTier < maxTier
                && gold >= upCost
                && !OpponentModel.IsLevelUpLethal(state.Health, state.Turn))
                return false;

            return true;
        }

        private static GameAction TryGetMethodicalMadnessUse(
            GameState state,
            List<GameAction> actions)
        {
            if (state == null || actions == null || state.BoardMinions == null
                || state.HandMinions == null)
                return null;
            bool hasFriendlyDemon = state.BoardMinions.Any(board =>
                board != null
                && MinionData.TribeMatches(board.Tribe, "恶魔"));
            if (!hasFriendlyDemon) return null;

            foreach (var action in actions)
            {
                if (action == null || action.Type != ActionType.UseSpell
                    || action.CardId != "BG36_880")
                    continue;
                if (action.TargetIndex >= 0
                    && action.TargetIndex < state.HandMinions.Count
                    && state.HandMinions[action.TargetIndex] != null)
                    return action;
            }
            return null;
        }

        private GameAction TryPickSynergyTribeBuy(
            GameState state,
            List<GameAction> actions,
            MainlinePlan mainlinePlan)
        {
            if (state == null || actions == null || mainlinePlan == null
                || string.IsNullOrEmpty(mainlinePlan.CompDir)
                || state.ShopMinions == null || state.BoardMinions == null
                || state.Turn < 6)
                return null;
            if (mainlinePlan.BoardCoreCount + mainlinePlan.HandCoreCount <= 0)
                return null;
            bool hasBoardCommitment = state.BoardMinions.Any(board =>
                board != null
                && MinionData.TribeMatches(board.Tribe, mainlinePlan.CompDir));
            if (!hasBoardCommitment) return null;

            GameAction best = null;
            double bestScore = double.MinValue;
            foreach (var action in actions)
            {
                if (action == null || action.Type != ActionType.BuyMinion
                    || action.TargetIndex < 0
                    || action.TargetIndex >= state.ShopMinions.Count)
                    continue;
                var shop = state.ShopMinions[action.TargetIndex];
                if (shop == null || shop.IsSpell
                    || !MinionData.TribeMatches(shop.Tribe, mainlinePlan.CompDir))
                    continue;
                double score = shop.Tier * 4.0 + shop.Attack * 0.15
                    + shop.Health * 0.10;
                bool isMainlineCore = !string.IsNullOrEmpty(shop.CardId)
                    && mainlinePlan.CoreCardIds.Any(id =>
                    string.Equals(id, shop.CardId, StringComparison.Ordinal)
                    || (shop.CardId.EndsWith("_G", StringComparison.Ordinal)
                        && string.Equals(
                            id,
                            shop.CardId.Substring(0, shop.CardId.Length - 2),
                            StringComparison.Ordinal)));
                if (isMainlineCore) score += 100.0;
                var rules = state.EffectiveRules ?? EffectiveGameRules.Default;
                if (TripleRuleEvaluator.CompletesGolden(
                    state, shop.CardId, rules))
                    score += 80.0;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = action;
                }
            }

            bool boardFullForSynergyBuy = state.BoardMinions.Count >= state.MaxBoardSlots;
            if (!boardFullForSynergyBuy || best == null)
                return best;

            var bestShop = state.ShopMinions[best.TargetIndex];
            if (bestShop == null || bestShop.IsSpell) return null;
            var rulesForSell = state.EffectiveRules ?? EffectiveGameRules.Default;
            int gold = state.LiveGold >= 0 ? state.LiveGold : state.Gold;
            int purchaseCost = GameRuleEvaluator.GetPurchaseCost(
                state, bestShop, state.HeroCardId, rulesForSell);
            if (gold + 1 < purchaseCost) return null;

            int sellIdx = -1;
            double sellScore = double.MaxValue;
            for (int i = 0; i < state.BoardMinions.Count; i++)
            {
                var board = state.BoardMinions[i];
                if (board == null || board.Golden) continue;
                if (state.RecentlyBoughtEntityIds != null
                    && state.RecentlyBoughtEntityIds.Contains(board.EntityId))
                    continue;
                if (IsProtectedSellMinion(state, i)) continue;
                bool isBoardCore = !string.IsNullOrEmpty(board.CardId)
                    && mainlinePlan.CoreCardIds.Any(id =>
                    string.Equals(id, board.CardId, StringComparison.Ordinal)
                    || (board.CardId.EndsWith("_G", StringComparison.Ordinal)
                        && string.Equals(
                            id,
                            board.CardId.Substring(0, board.CardId.Length - 2),
                            StringComparison.Ordinal)));
                if (isBoardCore) continue;

                double boardScore = board.Tier * 8.0
                    + board.Attack * 0.5 + board.Health * 0.3;
                if (MinionData.TribeMatches(
                    board.Tribe, mainlinePlan.CompDir))
                    boardScore += 80.0;
                if (boardScore < sellScore)
                {
                    sellScore = boardScore;
                    sellIdx = i;
                }
            }
            if (sellIdx < 0 || !IsSellWorthwhile(state, sellIdx))
                return null;

            var sold = state.BoardMinions[sellIdx];
            double soldPower = sold.Tier * 8.0
                + sold.Attack * 0.5 + sold.Health * 0.3;
            double shopPower = bestShop.Tier * 8.0
                + bestShop.Attack * 0.5 + bestShop.Health * 0.3;
            if (bestShop.Tier <= sold.Tier && shopPower <= soldPower * 1.15)
                return null;

            var sellAction = actions.FirstOrDefault(action =>
                action != null && action.Type == ActionType.SellMinion
                && action.TargetIndex == sellIdx);
            if (sellAction == null) return null;
            best = new GameAction { Type = ActionType.SellMinion,
                TargetIndex = sellAction.TargetIndex,
                CardId = sellAction.CardId,
                PurchaseSource = sellAction.PurchaseSource,
                Cost = sellAction.Cost,
                Description = sellAction.Description,
                RequiresTarget = sellAction.RequiresTarget
            };
            return best;
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
            "BG34_140",      // 飞行专家 (进击拉手牌最高攻, 手牌流引擎)
            "BG34_140_G",    // 飞行专家 金色
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
                if (state.RecentlyBoughtEntityIds != null
                    && state.RecentlyBoughtEntityIds.Contains(m.EntityId))
                    continue;
                if (IsProtectedSellMinion(state, i)) continue;
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
                    if (IsProtectedSellMinion(state, i)) continue;
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
                if (OpponentTechAdvisor.IsSituationalTechCard(m.CardId)
                    && OpponentTechAdvisor.IsTechCardWorthKeeping(
                        m.CardId, state.Opponents))
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

        private static void NormalizeLiveGold(GameState state)
        {
            if (state == null || state.LiveGold < 0) return;
            state.Gold = state.LiveGold;
        }

        private bool IsSellWorthwhile(GameState state, int sellIndex)
        {
            if (state == null || sellIndex < 0 || sellIndex >= state.BoardMinions.Count)
                return false;
            var sold = state.BoardMinions[sellIndex];
            if (sold == null || sold.Golden) return false;
            if (IsSelfReplacingEconomySell(state, sold)) return true;
            if (state.RecentlyBoughtEntityIds != null
                && state.RecentlyBoughtEntityIds.Contains(sold.EntityId))
                return false;
            if (IsProtectedSellMinion(state, sellIndex)) return false;

            int gold = state.LiveGold >= 0 ? state.LiveGold : state.Gold;
            if (state.BoardMinions.Count >= state.MaxBoardSlots)
            {
                var hand = state.HandMinions != null
                    ? state.HandMinions.FirstOrDefault(card => card != null && !card.IsSpell)
                    : null;
                if (hand != null && IsHandReplacementBetter(state, sold, hand))
                    return true;
            }
            if (HasBetterShopAfterSell(state, sold, gold)) return true;

            var rules = state.EffectiveRules ?? EffectiveGameRules.Default;
            int? upgradeCost = GameRuleEvaluator.GetUpgradeCost(state, rules);
            int maxTier = rules != null ? rules.MaxTavernTier : 6;
            if (upgradeCost.HasValue && upgradeCost.Value > 0
                && gold + 1 >= upgradeCost.Value
                && state.TavernTier < maxTier
                && state.Turn >= 6
                && state.Health > 15
                && state.BoardMinions.Count >= 4
                && !OpponentModel.IsLevelUpLethal(state.Health, state.Turn)
                && IsDeadWeightSell(state, sellIndex, sold))
                return true;
            return false;
        }

        private bool IsSelfReplacingEconomySell(GameState state, MinionData sold)
        {
            if (state == null || sold == null
                || (sold.CardId != "BG33_140" && sold.CardId != "BG33_140_G"))
                return false;
            int gold = state.LiveGold >= 0 ? state.LiveGold : state.Gold;
            var rules = state.EffectiveRules ?? EffectiveGameRules.Default;
            foreach (var shop in state.ShopMinions ?? Enumerable.Empty<MinionData>())
            {
                if (shop == null || !shop.IsSpell) continue;
                int cost = GameRuleEvaluator.GetPurchaseCost(
                    state, shop, state.HeroCardId, rules);
                if (gold >= cost) continue;
                if (gold + 1 < cost) continue;
                if (IsResourceCard(shop.CardId)
                    || (shop.CardName != null
                        && (shop.CardName.Contains("铸币")
                            || shop.CardName.Contains("金币")
                            || shop.CardName.Contains("币"))))
                    return true;
            }
            return false;
        }

        private bool IsProtectedSellMinion(GameState state, int index)
        {
            if (state == null || state.BoardMinions == null
                || index < 0 || index >= state.BoardMinions.Count)
                return true;
            var m = state.BoardMinions[index];
            if (m == null || m.Golden) return true;
            if (IsKnownEngine(m.CardId)) return true;
            // 1.2.2 feedbackD: 手牌召唤引擎(飞行专家 BG34_140 等) — 手牌还有随从时
            // 卖它=手牌随从无法进战斗(进击拉手牌最高攻仅本场), 用户T9实测严重失误。
            if (m.CardId != null
                && (m.CardId == "BG34_140" || m.CardId == "BG34_140_G")
                && state.HandMinions != null
                && state.HandMinions.Any(h => h != null && !h.IsSpell))
                return true;
            if (_lastMainlinePlan != null
                && _lastMainlinePlan.CoreCardIds != null
                && _lastMainlinePlan.CoreCardIds.Contains(m.CardId))
                return true;
            if (m.CardId != null
                && m.CardId.EndsWith("_G", StringComparison.Ordinal)
                && _lastMainlinePlan != null
                && _lastMainlinePlan.CoreCardIds != null
                && _lastMainlinePlan.CoreCardIds.Contains(
                    m.CardId.Substring(0, m.CardId.Length - 2)))
                return true;
            if (OpponentTechAdvisor.IsSituationalTechCard(m.CardId)
                && OpponentTechAdvisor.IsTechCardWorthKeeping(
                    m.CardId, state.Opponents))
                return true;
            if (_classifier != null
                && (IsResourceCard(m.CardId)
                    || IsCoreComboCard(m.CardId)))
                return true;
            if (m.Tier >= 5 && IsLikelyResourceText(m.CardId))
                return true;
            if (m.Tier >= 4 && (m.DivineShield || m.Poisonous || m.Venomous || m.Reborn))
                return true;
            if (m.Tier >= 3 && (m.DivineShield || m.Poisonous || m.Venomous))
                return true;
            if (m.Taunt)
            {
                int tauntCount = state.BoardMinions.Count(
                    other => other != null && other.Taunt);
                if (tauntCount <= 1) return true;
            }
            return IsUniqueTribeMinion(state, index);
        }

        private static bool IsLikelyResourceText(string cardId)
        {
            string text = GetHearthDbText(cardId);
            if (string.IsNullOrEmpty(text)) return false;
            return text.Contains("塑造法术")
                || text.Contains("随机获取")
                || text.Contains("获取一张")
                || text.Contains("发现")
                || text.Contains("每回合都会提升");
        }

        private bool IsDeadWeightSell(GameState state, int index, MinionData sold)
        {
            if (state == null || state.BoardMinions == null
                || state.BoardMinions.Count < 3 || sold == null)
                return false;
            double soldPower = _fe.ComputeBoardPower(
                new List<MinionData> { sold });
            double avgPower = _fe.ComputeBoardPower(state.BoardMinions)
                / state.BoardMinions.Count;
            if (soldPower <= 0 || avgPower <= 0) return false;
            return soldPower <= avgPower * 0.6
                && sold.Tier <= Math.Max(1, state.TavernTier - 1);
        }

        private bool IsHandReplacementBetter(
            GameState state, MinionData sold, MinionData hand)
        {
            if (state == null || sold == null || hand == null) return false;
            if (hand.Golden) return true;
            double soldPower = _fe.ComputeBoardPower(
                new List<MinionData> { sold });
            double handPower = _fe.ComputeBoardPower(
                new List<MinionData> { hand });
            if (handPower >= soldPower * 1.15) return true;
            if (_classifier == null) return false;
            bool resource = IsResourceCard(hand.CardId);
            bool core = IsCoreComboCard(hand.CardId);
            return (resource || core) && handPower >= soldPower * 0.80;
        }

        private bool HasBetterShopAfterSell(
            GameState state, MinionData sold, int gold)
        {
            if (state == null || state.ShopMinions == null || sold == null) return false;
            var rules = state.EffectiveRules ?? EffectiveGameRules.Default;

            foreach (var shop in state.ShopMinions)
            {
                if (shop == null) continue;
                int cost = GameRuleEvaluator.GetPurchaseCost(
                    state, shop, state.HeroCardId, rules);
                if (gold >= cost) continue;
                if (gold + 1 < cost) continue;
                if (shop.IsSpell)
                {
                    if (IsResourceCard(shop.CardId))
                        return true;
                    continue;
                }
                if (IsShopMinionBetter(state, sold, shop))
                    return true;
            }
            return false;
        }

        private bool IsShopMinionBetter(
            GameState state, MinionData sold, MinionData shop)
        {
            if (state == null || sold == null || shop == null) return false;
            var rules = state.EffectiveRules ?? EffectiveGameRules.Default;
            if (TripleRuleEvaluator.CompletesGolden(state, shop.CardId, rules))
                return true;

            if (OpponentTechAdvisor.IsSituationalTechCard(shop.CardId)
                && OpponentTechAdvisor.IsTechCardWorthKeeping(
                    shop.CardId, state.Opponents))
                return true;

            double soldPower = _fe.ComputeBoardPower(
                new List<MinionData> { sold });
            double shopPower = _fe.ComputeBoardPower(
                new List<MinionData> { shop });
            if (_classifier != null
                && (IsResourceCard(shop.CardId)
                    || IsCoreComboCard(shop.CardId)))
            {
                if (shopPower >= soldPower * 1.05)
                    return true;
                double boardAvg = state.BoardMinions != null
                    && state.BoardMinions.Count > 0
                        ? _fe.ComputeBoardPower(state.BoardMinions)
                            / state.BoardMinions.Count
                        : 0;
                return soldPower <= boardAvg * 0.65;
            }

            double threshold = shop.Tier >= sold.Tier ? 1.20 : 1.50;
            return shopPower >= soldPower * threshold;
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

        // R228: 统计板面+手牌中同名非金色随从数量(凑对子/三连用)
        private int CountSameCardAcrossBoardAndHand(GameState state, string cardId)
        {
            if (string.IsNullOrEmpty(cardId) || state == null) return 0;
            int count = 0;
            if (state.BoardMinions != null)
            {
                foreach (var m in state.BoardMinions)
                {
                    if (m == null) continue;
                    if (!m.Golden && string.Equals(m.CardId, cardId, StringComparison.Ordinal))
                        count++;
                }
            }
            if (state.HandMinions != null)
            {
                foreach (var m in state.HandMinions)
                {
                    if (m == null) continue;
                    if (!m.Golden && string.Equals(m.CardId, cardId, StringComparison.Ordinal))
                        count++;
                }
            }
            return count;
        }

        // R231: 归一化金色后缀(去 _G), 与 StrategicMemory.NormalizeGolden 一致
        private static string NormalizeGoldenId(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return "";
            return cardId.EndsWith("_G", StringComparison.Ordinal)
                ? cardId.Substring(0, cardId.Length - 2)
                : cardId;
        }

        private bool HasEudoraWorthFreezing(GameState state)
        {
            if (state == null || state.ShopMinions == null)
                return false;
            var rules = state.EffectiveRules ?? EffectiveGameRules.Default;
            foreach (var shop in state.ShopMinions)
            {
                if (shop == null) continue;
                if (!shop.IsSpell && shop.Tier >= 2) return true;
                if (TripleRuleEvaluator.CompletesGolden(state, shop.CardId, rules))
                    return true;
                if (_classifier != null
                    && (_classifier.IsEconomyCard(shop.CardId)
                        || _classifier.IsAmplifier(shop.CardId)))
                    return true;
            }
            return false;
        }

        private bool HasFreezeWorthKeepingShop(GameState state)
        {
            if (state == null || state.ShopMinions == null)
                return false;
            var rules = state.EffectiveRules ?? EffectiveGameRules.Default;
            foreach (var shop in state.ShopMinions)
            {
                if (shop == null) continue;
                if (TripleRuleEvaluator.CompletesGolden(state, shop.CardId, rules))
                    return true;
                if (shop.Tier >= 2) return true;
                if (_classifier != null
                    && (_classifier.IsEconomyCard(shop.CardId)
                        || _classifier.IsAmplifier(shop.CardId)))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 统一候选评分：所有合法动作进入同一评分面，安全规则保留为硬过滤。
        /// 该结果用于内部计划与回放评估，不直接作为 UI 文案展示。
        /// </summary>
        public List<BobCoach.Engine.Decision.RankedAction> GetRankedCandidates(
            GameState state, int topN = 3)
        {
            var result = new List<BobCoach.Engine.Decision.RankedAction>();
            if (state == null || !state.GameActive || !state.NormalRecruitAvailable)
                return result;
            NormalizeLiveGold(state);

            List<GameAction> actions;
            try
            {
                actions = _enumerator.Enumerate(
                    state, state.HeroCardId, includeTrinket: false);
            }
            catch
            {
                return result;
            }

            var scored = new List<BobCoach.Engine.Decision.RankedAction>();
            foreach (var action in actions)
            {
                if (action == null) continue;
                if (action.Type == ActionType.SellMinion
                    && !IsSellWorthwhile(state, action.TargetIndex))
                    continue;
                if (action.Type == ActionType.Refresh
                    && !CanRefreshLeaveMeaningfulBudget(state))
                    continue;
                if (action.Type == ActionType.Upgrade
                    && OpponentModel.IsLevelUpLethal(state.Health, state.Turn))
                    continue;

                try
                {
                    var next = _sim.Simulate(state, action);
                    if (next == null) continue;
                    var features = _fe.Extract(next);
                    double value = _vf.Evaluate(features);
                    var nextTurn = TurnProjector.Advance(next);
                    if (nextTurn != null)
                    {
                        var nextTurnFeatures = _fe.Extract(nextTurn);
                        value += 0.1 * nextTurnFeatures[FeatureExtractor.F_GOLD];
                        value += 0.02 * nextTurnFeatures[FeatureExtractor.F_BOARD_POWER];
                    }
                    if (action.Type == ActionType.UseHeroPower)
                        value = AdjustHeroPowerValue(state, action, value);
                    if (action.Type == ActionType.PlayMinion)
                        value += 0.03 * state.HandMinions.Count;
                    if (action.Type == ActionType.UseSpell)
                    {
                        if (IsEconomyCard(action.CardId)) value += 0.35;
                        if (IsResourceCard(action.CardId))
                            value += state.Turn <= 5 ? 0.45 : (state.Turn <= 9 ? 0.30 : 0.15);
                        if (IsCoreComboCard(action.CardId))
                            value += state.Turn <= 9 ? 0.35 : 0.50;
                    }
                    if (double.IsNaN(value) || double.IsInfinity(value))
                        continue;
                    scored.Add(new BobCoach.Engine.Decision.RankedAction(
                        action,
                        BobCoach.Engine.Decision.ActionDescriptor.CreateKey(action),
                        value,
                        null));
                }
                catch
                {
                    // 单候选失败不影响其它候选进入排名。
                }
            }

            for (int index = 0; index < scored.Count; index++)
            {
                var candidate = scored[index];
                if (candidate == null || _lastStrategyPlan == null
                    || _lastStrategyPlan.Steps == null
                    || _lastStrategyPlan.Steps.Count == 0)
                    continue;
                var first = _lastStrategyPlan.Steps[0].Action;
                if (first != null
                    && first.Type == candidate.Action.Type
                    && first.TargetIndex == candidate.Action.TargetIndex
                    && string.Equals(first.CardId ?? "", candidate.Action.CardId ?? "",
                        StringComparison.Ordinal))
                {
                    scored[index] = new BobCoach.Engine.Decision.RankedAction(
                        candidate.Action,
                        candidate.ActionKey,
                        candidate.Score + 0.15,
                        candidate.Factors);
                }
            }

            scored.Sort((a, b) =>
            {
                int cmp = b.Score.CompareTo(a.Score);
                if (cmp != 0) return cmp;
                return string.CompareOrdinal(a.ActionKey, b.ActionKey);
            });

            if (topN <= 0) topN = scored.Count;
            for (int i = 0; i < Math.Min(topN, scored.Count); i++)
                result.Add(scored[i]);
            return result;
        }

        /// <summary>
        /// 跨回合计划：对当前回合 Top 动作执行后投影下一回合，再从下一回合格局中找最佳后续动作。
        /// UI 不展示完整规划，只由上层决定是否使用第一步和必要时的紧凑动作链。
        /// </summary>
        public CrossTurnPlan GetCrossTurnPlan(GameState state, int topN = 3)
        {
            if (state == null || !state.GameActive) return null;
            var current = GetRankedCandidates(state, topN);
            if (current == null || current.Count == 0) return null;

            CrossTurnPlan best = null;
            foreach (var candidate in current)
            {
                try
                {
                    var afterFirst = _sim.Simulate(state, candidate.Action);
                    if (afterFirst == null) continue;
                    var nextTurn = TurnProjector.Advance(afterFirst);
                    if (nextTurn == null) continue;

                    var nextActions = _enumerator.Enumerate(
                        nextTurn, nextTurn.HeroCardId, includeTrinket: false);
                    double bestNextValue = 0;
                    GameAction bestNext = null;
                    foreach (var nextAction in nextActions)
                    {
                        if (nextAction == null) continue;
                        if (nextAction.Type == ActionType.SellMinion
                            && !IsSellWorthwhile(nextTurn, nextAction.TargetIndex))
                            continue;
                        var nextAfter = _sim.Simulate(nextTurn, nextAction);
                        if (nextAfter == null) continue;
                        double nextValue = _vf.Evaluate(_fe.Extract(nextAfter));
                        if (nextValue > bestNextValue)
                        {
                            bestNextValue = nextValue;
                            bestNext = nextAction;
                        }
                    }

                    double combined = candidate.Score + 0.6 * bestNextValue;
                    if (best == null || combined > best.CombinedScore)
                    {
                        best = new CrossTurnPlan
                        {
                            FirstAction = candidate.Action,
                            NextTurnAction = bestNext,
                            CombinedScore = combined,
                            StateAfterFirst = afterFirst,
                            NextTurnState = nextTurn,
                        };
                    }
                }
                catch
                {
                    // 单条跨回合路径失败不影响其它候选。
                }
            }
            return best;
        }

        /// <summary>
        /// 返回推荐购买的酒馆位置索引，按优先级降序。
        /// </summary>
        public List<int> GetRecommendedBuyIndices(GameState state, int topN = 3)
        {
            var result = new List<int>();
            if (state == null || !state.GameActive) return result;
            NormalizeLiveGold(state);

            // V1.2: 阵容锁定 + 英雄策略预计算
            CompLockResult compLockResult; HeroStrategy heroStrat;
            try
            {
                compLockResult = _compLock.Detect(state);
                heroStrat = _heroPower.GetStrategyForPower(
                    state.HeroCardId, state.HeroPowerCardId);
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

                double situationalTechMult = OpponentTechAdvisor
                    .GetSituationalTechMultiplier(shopCard.CardId, state.Opponents);
                if (situationalTechMult != 1.0)
                    score *= situationalTechMult;

                // Track1(0709): EffectValue 有界加法项(治VF不读文字效果盲区; 仅随从, 链末端不被放大)
                score += EffectValueBonus(shopCard, state, dominantTribe);
                score += AttachedResourceBonus(shopCard);
                score += StatGiftCardBonus(shopCard, state);
                score += WhereverScalingTempoBonus(shopCard, state);
                score += (state.ActiveTrinketContext ?? ActiveTrinketContext.Empty)
                    .GetCardSynergyScore(shopCard);
                score += CompGuideWeightBonus(shopCard, state);

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
        private bool CanAffordAfterOneSell(GameState state, int cardCost)
        {
            if (state == null) return false;
            if (state.Gold >= cardCost) return true;
            if (cardCost > state.Gold + 1) return false;
            if (state.BoardMinions == null || state.BoardMinions.Count == 0) return false;
            return FindWeakestBoardMinion(state, null) >= 0;
        }

        public List<ShopCardScore> GetShopCardScores(GameState state)
        {
            var result = new List<ShopCardScore>();
            if (state == null || !state.GameActive || !state.NormalRecruitAvailable)
                return result;
            NormalizeLiveGold(state);

            CompLockResult compLockResult; HeroStrategy heroStrat; string dominantTribe;
            try
            {
                compLockResult = _compLock.Detect(state);
                heroStrat = _heroPower.GetStrategyForPower(
                    state.HeroCardId, state.HeroPowerCardId);
                dominantTribe = GetDominantTribe(state);
            }
            catch { return result; }
            var rules = state.EffectiveRules ?? EffectiveGameRules.Default;
            bool canBuyMinion = state.BoardMinions == null
                || state.BoardMinions.Count < state.MaxBoardSlots
                || state.HandMinions == null
                || state.HandMinions.Count < 10;
            bool canBuySpell = state.HandMinions == null
                || state.HandMinions.Count < 10;

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
                bool shopIsSpell = shopCard.IsSpell;
                if (shopIsSpell && !canBuySpell) continue;
                if (!shopIsSpell && !canBuyMinion) continue;
                int cardCost = GameRuleEvaluator.GetPurchaseCost(
                    state, shopCard, state.HeroCardId, rules);
                bool sellProjected = CanAffordAfterOneSell(state, cardCost);
                if (state.Gold < cardCost && !sellProjected) continue;

                double score;
                try
                {
                    var actionType = shopIsSpell ? ActionType.BuySpell : ActionType.BuyMinion;
                    var action = new GameAction
                    {
                        Type = actionType, TargetIndex = i, CardId = shopCard.CardId,
                        PurchaseSource = "tavern_shop",
                    };
                    var simState = state;
                    if (state.Gold < cardCost && sellProjected)
                    {
                        int sellIdx = FindWeakestBoardMinion(state, null);
                        if (sellIdx < 0) continue;
                        var sold = state.BoardMinions[sellIdx];
                        simState = _sim.Simulate(state, new GameAction
                        {
                            Type = ActionType.SellMinion,
                            TargetIndex = sellIdx,
                            CardId = sold.CardId,
                        });
                        if (simState == null || simState.Gold < cardCost) continue;
                    }
                    var next = _sim.Simulate(simState, action);
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
                        var hs = _heroPower.GetStrategyForPower(
                            state.HeroCardId, state.HeroPowerCardId);
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

                    // P-玛维 2026-08(用户实测: 开局'2铸币法术+主动技能禁锢'组合算法不推荐):
                    // 玛维技能1费禁锢, 开局(T1-T3)买低费法术(招募新人/发现类)可立即打出+禁锢, 组合价值高。
                    if ((state.HeroCardId ?? "").Contains("TB_BaconShop_HERO_62")
                        && state.Turn <= 3 && shopCard.IsSpell)
                    {
                        score *= 1.5f;  // 玛维开局法术: 打出去触发 + 配合1费禁锢
                    }

                    // 默认法术轻微降分(不增加板面), 但上述特殊类型已覆盖主要场景
                    if (survivalMult <= 1.01f && scalingMult <= 1.01f)
                        score *= 0.95;  // 轻量惩罚, 避免经济型法术被过度压制
                }


                // P13 2026-08(用户实测: 迷诱咒术师'买→发动'从未被推荐, 发动机制形同虚设):
                // 发动卡(Season14 发动注册表命中)购买评分含'发动价值'——否则普通身材的发动随从
                // 走 V(s) 裸评分进不了 top-3, 二步前瞻永远探索不到'买→发动'组合。
                // 高价值发动效果(偷取/发现/获取/香蕉/随机施放)大幅加分; 普通 buff 类轻微加分。
                if (!shopCard.IsSpell)
                {
                    var season14Shared = Engine.Season14RulesEvaluator.GetShared();
                    Engine.ActivateFact actFact;
                    if (season14Shared != null && season14Shared.TryGetActivate(shopCard.CardId, out actFact) && actFact != null)
                    {
                        string actText = (actFact.TextZhCn ?? "") + (actFact.TextEnUs ?? "");
                        bool highValueAct = actText.Contains("偷取") || actText.Contains("Steal")
                            || actText.Contains("发现") || actText.Contains("Discover")
                            || actText.Contains("获取") || actText.Contains("gain")
                            || actText.Contains("香蕉") || actText.Contains("随机施放");
                        if (highValueAct)
                            score *= (state.Turn <= 6 ? 2.6f : 2.0f);  // 前期偷怪/发现价值极高
                        else
                            score *= 1.25f;  // 普通发动卡: 含可发动维度, 轻加分
                    }
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
                bool offTribe = !shopCard.IsSpell
                    && IsOffTribeMinion(shopCard.Tribe, dominantTribe);
                if (offTribe && state.BoardMinions.Count >= 4)
                    score *= 0.62;

                score += heroStrat.BuyValueBias;

                // 2026-08-19 实测修复(B2/B3): 劣势保命模式——自己战力明显落后对手
                // 或血量危险时, 即时战力卡(高攻/嘲讽/圣盾/烈毒)加分, 一回合拉不起
                // 战力的低费弱卡减分(打不过时保名次优先, 不贪成长)
                if (!shopCard.IsSpell && state.Opponents != null && state.Opponents.Count > 0)
                {
                    float oppPwr = _fe.ComputeAvgOpponentPower(state.Opponents);
                    float myPwr = _fe.ComputeBoardPower(state.BoardMinions);
                    float pwrRatio = oppPwr > 0 ? myPwr / oppPwr : 2f;
                    bool disadvantaged = pwrRatio < 0.7f
                        || (state.Health <= 15 && pwrRatio < 0.9f);
                    if (disadvantaged)
                    {
                        float immediate = shopCard.Attack * 0.01f;
                        if (shopCard.Taunt) immediate += 0.25f;
                        if (shopCard.DivineShield) immediate += 0.25f;
                        if (shopCard.Poisonous || shopCard.Venomous) immediate += 0.2f;
                        if (shopCard.Reborn) immediate += 0.15f;
                        if (immediate > 0f) score *= (1f + immediate);
                        if (shopCard.Tier <= 2 && shopCard.Attack + shopCard.Health < 8)
                            score *= 0.6f;
                    }
                }

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

                bool chromieSpellHero = HeroPowerRuleCatalog.IsChromieHero(state.HeroCardId);
                if (chromieSpellHero && shopCard.IsSpell)
                {
                    int spellShopCount = state.ShopMinions.Count(
                        m => m != null && m.IsSpell);
                    score *= spellShopCount >= 3 ? 1.35f : 1.20f;
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

                // P11 2026-08(用户实测: 后期强制交互牌权重不足——火车王排第4, 复生烈毒组合不识别):
                // 后期(T>=9)强制交换牌加权: 火车王(亡语杀击杀者)/英勇的逆袭者(进击获攻)/剧毒/烈毒/圣盾。
                // 复生+烈毒组合: 板面已有烈毒/剧毒随从时, 坎格尔学徒(召最先死机械复制)等复制牌价值极高。
                if (state.Turn >= 9 && !shopCard.IsSpell)
                {
                    bool isForceSwap = Engine.OpponentTechAdvisor.IsSituationalTechCard(shopCard.CardId)
                        || shopCard.Poisonous || shopCard.Venomous;
                    if (isForceSwap)
                    {
                        score *= 1.5f;  // 强制交换牌后期优先(决赛针对)
                        bool boardHasVenom = state.BoardMinions != null
                            && state.BoardMinions.Any(m => m != null && (m.Poisonous || m.Venomous));
                        if (boardHasVenom && shopCard.CardId != null
                            && (shopCard.CardId == "BGS_012" // 坎格尔学徒: 召最先死机械复制
                                || shopCard.CardId == "BGS_012_G"
                                || (shopCard.CardText ?? "").Contains("最先死亡")))
                            score *= 2.2f;  // 板面有烈毒 + 学徒 → 复制烈毒组合, 交换能力翻倍
                    }
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
                if (!isTriple && state.Turn >= 3)
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
                score += AttachedResourceBonus(shopCard);
                score += StatGiftCardBonus(shopCard, state);
                score += WhereverScalingTempoBonus(shopCard, state);
                score += (state.ActiveTrinketContext ?? ActiveTrinketContext.Empty)
                    .GetCardSynergyScore(shopCard);
                score += CompGuideWeightBonus(shopCard, state);

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
                for (int i = 0; i < state.ShopMinions.Count; i++)
                {
                    var shopCard = state.ShopMinions[i];
                    if (shopCard == null) continue;
                    bool isSpell = shopCard.IsSpell;
                    if (isSpell && !canBuySpell) continue;
                    if (!isSpell && !canBuyMinion) continue;
                    int cardCost = GameRuleEvaluator.GetPurchaseCost(
                        state, shopCard, state.HeroCardId, rules);
                    if (state.Gold < cardCost && !CanAffordAfterOneSell(state, cardCost))
                        continue;

                    var classification = _classifier != null
                        ? _classifier.GetClassification(shopCard.CardId)
                        : null;

                    double fallbackScore = shopCard.Tier * 10.0 + shopCard.Attack * 0.35 + shopCard.Health * 0.25;
                    if (!string.IsNullOrEmpty(dominantTribe) && !string.IsNullOrEmpty(shopCard.Tribe)
                        && MinionData.TribeMatches(shopCard.Tribe, dominantTribe))
                        fallbackScore *= 1.20;
                    if (!shopCard.IsSpell && state.BoardMinions.Count >= 4
                        && IsOffTribeMinion(shopCard.Tribe, dominantTribe))
                        fallbackScore *= 0.62;
                    double situationalTechMult = OpponentTechAdvisor
                        .GetSituationalTechMultiplier(shopCard.CardId, state.Opponents);
                    if (situationalTechMult != 1.0)
                        fallbackScore *= situationalTechMult;
                    fallbackScore += AttachedResourceBonus(shopCard);
                    fallbackScore += StatGiftCardBonus(shopCard, state);
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
            NormalizeLiveGold(state);
            if (!state.NormalRecruitAvailable)
                return new LevelUpSuggestion("NO_GOLD", "当前不可招募");

            int maxTavernTier = state.EffectiveRules != null
                ? state.EffectiveRules.MaxTavernTier : 6;
            if (state.TavernTier >= maxTavernTier)
                return new LevelUpSuggestion("MAX", "已满级");

            int? resolvedCost = GameRuleEvaluator.GetUpgradeCost(
                state, state.EffectiveRules ?? EffectiveGameRules.Default);
            if (!resolvedCost.HasValue)
                return new LevelUpSuggestion("NO_GOLD", "升本费用未确认");
            int cost = resolvedCost.Value;
            if (OpponentModel.IsLevelUpLethal(state.Health, state.Turn))
                return new LevelUpSuggestion("DANGER",
                    "当前血量过低，升本被斩杀风险过大");
            // 2026-08-19 实测修复(B4): 连续升本抑制——上一回合刚升过本且已3本+,
            // 本回合不再推荐升本(升完吃光金币→被迫卖牌/买不起, 用户实测牛头人T4场景)
            if (state.TavernTier >= 3
                && state.LastUpgradeTurn == state.Turn - 1)
                return new LevelUpSuggestion("STABILIZE",
                    "刚升过本，先补战力再考虑升本");
            var heroStrat = _heroPower.GetStrategyForPower(
                state.HeroCardId, state.HeroPowerCardId);
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

            // P5 2026-08(用户实测: 回合7战力稳却不升本找核心, 继续买杂毛浪费铸币):
            // 板面成型+血量健康+非满级 → 优先升本找核心(战力提升曲线的关键: 高本卡池>买低质随从)。
            // 触发条件保守: 板面>=5随从(成型) + 平均身材>=8(战力稳) + 血>=20 + 金币够 + 非残局。
            if (state.TavernTier < maxTavernTier
                && state.BoardMinions != null && state.BoardMinions.Count >= 5
                && state.Health >= 20 && state.Gold >= cost
                && !OpponentModel.IsLevelUpLethal(state.Health, state.Turn))
            {
                int totalStats = 0, count = 0;
                foreach (var m in state.BoardMinions)
                {
                    if (m == null) continue;
                    totalStats += (m.Attack > 0 ? m.Attack : 0) + (m.Health > 0 ? m.Health : 0);
                    count++;
                }
                double avgStats = count > 0 ? (double)totalStats / count : 0;
                if (avgStats >= 8 && state.Turn >= 7)
                    return new LevelUpSuggestion("LEVEL_UP",
                        "战力稳 — 升" + (state.TavernTier + 1) + "本找核心, 别再买杂毛");
            }

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

        /// <summary>
        /// 附加资源价值：塑造法术等随从自带的持续资源不计入身材，但实际收益高于裸身材。
        /// </summary>
        private static double AttachedResourceBonus(MinionData card)
        {
            if (card == null || card.IsSpell || string.IsNullOrEmpty(card.CardId))
                return 0.0;
            try
            {
                HearthDb.Card c;
                if (!HearthDb.Cards.All.TryGetValue(card.CardId, out c) || c == null)
                    return 0.0;
                string text = c.Text ?? "";
                text += " " + (c.GetLocText(HearthDb.Enums.Locale.zhCN) ?? "");
                bool spellcraft = (c.Mechanics != null
                        && c.Mechanics.Contains("BACON_SPELLCRAFT_ID"))
                    || text.Contains("Spellcraft")
                    || text.Contains("塑造法术");
                bool nextTurnGold = (text.Contains("Gold")
                        && text.Contains("next turn"))
                    || (text.Contains("下回合获得")
                        && text.Contains("铸币"));
                bool immediateCoin = (text.Contains("Coin")
                        && text.Contains("Gain"))
                    || (text.Contains("获得")
                        && text.Contains("铸币"));
                bool alwaysGolden = text.Contains("always Golden")
                    || text.Contains("始终为金色");
                if (immediateCoin) return 1.5;
                if (nextTurnGold) return 0.8;
                if (alwaysGolden)
                    return 1.0 + (card.DivineShield ? 0.25 : 0.0);
                if (spellcraft)
                    return Math.Min(1.2, 0.6 + Math.Max(1, card.Tier) * 0.15);
                return 0.0;
            }
            catch
            {
                return 0.0;
            }
        }

        private static double StatGiftCardBonus(MinionData card, GameState state)
        {
            if (card == null || string.IsNullOrEmpty(card.CardId) || state == null)
                return 0.0;
            string text = GetHearthDbText(card.CardId);
            if (StatGiftRuleEvaluator.IsStatGiftGeneratorText(text))
            {
                double strength = MaxStatGiftAmplifierStrength(state);
                return 0.6 + (strength - 1.0) * 1.5;
            }
            if (StatGiftRuleEvaluator.IsStatGiftAmplifierText(text))
            {
                double strength = StatGiftRuleEvaluator.GetAmplifierStrength(text);
                double baseValue = 0.5 + (strength - 1.0) * 1.8;
                return HasStatGiftGenerator(state) ? baseValue + 1.0 : baseValue;
            }
            return 0.0;
        }

        private static double WhereverScalingTempoBonus(MinionData card, GameState state)
        {
            if (card == null || card.IsSpell || card.Tier > 3 || state == null)
                return 0.0;
            if (state.Turn < 4 || state.Turn > 9
                || state.BoardMinions.Count > 5)
                return 0.0;
            string text = GetHearthDbText(card.CardId);
            if (text.Contains("无论本随从在哪")
                || text.Contains("本随从在哪")
                || (card.CardName != null && card.CardName.Contains("星元自动机")))
                return 1.0;
            return 0.0;
        }

        private static string GetHearthDbText(string cardId)
        {
            try
            {
                HearthDb.Card c;
                if (!HearthDb.Cards.All.TryGetValue(cardId, out c) || c == null)
                    return "";
                string text = c.Text ?? "";
                text += " " + (c.GetLocText(HearthDb.Enums.Locale.zhCN) ?? "");
                return text;
            }
            catch
            {
                return "";
            }
        }

        private static bool HasStatGiftGenerator(GameState state)
        {
            return HasStatGiftRole(state, true);
        }

        private static bool HasStatGiftAmplifier(GameState state)
        {
            return HasStatGiftRole(state, false);
        }

        private static bool HasStatGiftRole(GameState state, bool generator)
        {
            foreach (var minion in state.BoardMinions)
                if (minion != null && HasStatGiftRoleForCard(minion.CardId, generator))
                    return true;
            foreach (var minion in state.HandMinions)
                if (minion != null && HasStatGiftRoleForCard(minion.CardId, generator))
                    return true;
            foreach (var minion in state.ShopMinions)
                if (minion != null && HasStatGiftRoleForCard(minion.CardId, generator))
                    return true;
            return false;
        }

        private static bool HasStatGiftRoleForCard(string cardId, bool generator)
        {
            if (string.IsNullOrEmpty(cardId)) return false;
            string text = GetHearthDbText(cardId);
            return generator
                ? StatGiftRuleEvaluator.IsStatGiftGeneratorText(text)
                : StatGiftRuleEvaluator.IsStatGiftAmplifierText(text);
        }

        private static double MaxStatGiftAmplifierStrength(GameState state)
        {
            double max = 1.0;
            foreach (var minion in state.BoardMinions)
            {
                if (minion == null || string.IsNullOrEmpty(minion.CardId)) continue;
                string text = GetHearthDbText(minion.CardId);
                if (!StatGiftRuleEvaluator.IsStatGiftAmplifierText(text)) continue;
                double strength = StatGiftRuleEvaluator.GetAmplifierStrength(text);
                if (strength > max) max = strength;
            }
            foreach (var minion in state.HandMinions)
            {
                if (minion == null || string.IsNullOrEmpty(minion.CardId)) continue;
                string text = GetHearthDbText(minion.CardId);
                if (!StatGiftRuleEvaluator.IsStatGiftAmplifierText(text)) continue;
                double strength = StatGiftRuleEvaluator.GetAmplifierStrength(text);
                if (strength > max) max = strength;
            }
            foreach (var minion in state.ShopMinions)
            {
                if (minion == null || string.IsNullOrEmpty(minion.CardId)) continue;
                string text = GetHearthDbText(minion.CardId);
                if (!StatGiftRuleEvaluator.IsStatGiftAmplifierText(text)) continue;
                double strength = StatGiftRuleEvaluator.GetAmplifierStrength(text);
                if (strength > max) max = strength;
            }
            return max;
        }

        private double CompGuideWeightBonus(MinionData card, GameState state)
        {
            if (card == null || card.IsSpell || state == null) return 0.0;
            try
            {
                if (_compGuideCatalog == null || !_compGuideCatalog.IsLoaded)
                    return 0.0;

                CompGuideCardWeight weight;
                if (!_compGuideCatalog.TryGetWeight(
                    CompGuideWeightKind.Core, card.CardName, out weight)
                    || weight == null || !weight.RawWeight.HasValue)
                    return 0.0;

                if (string.IsNullOrEmpty(state.CurrentCompGuideTier))
                    return 0.0;

                int tierWeight;
                if (!_compGuideCatalog.TryGetTierWeight(
                    state.CurrentCompGuideTier, out tierWeight))
                    return 0.0;

                return weight.RawWeight.Value * tierWeight
                    * COMP_GUIDE_WEIGHT_LAMBDA;
            }
            catch
            {
                return 0.0;
            }
        }

        private double CompGuideTrinketWeightBonus(
            TrinketRecommendation recommendation, out bool isRated)
        {
            isRated = false;
            if (recommendation == null) return 0.0;
            try
            {
                if (_compGuideCatalog == null || !_compGuideCatalog.IsLoaded)
                    return 0.0;

                CompGuideCardWeight weight;
                if (!_compGuideCatalog.TryGetWeight(
                    CompGuideWeightKind.Trinket,
                    recommendation.DisplayName, out weight)
                    || weight == null || !weight.RawWeight.HasValue)
                    return 0.0;

                isRated = true;
                return weight.RawWeight.Value * COMP_GUIDE_WEIGHT_LAMBDA;
            }
            catch
            {
                isRated = false;
                return 0.0;
            }
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

        private static bool IsOffTribeMinion(string tribeField, string dominantTribe)
        {
            if (string.IsNullOrEmpty(tribeField) || string.IsNullOrEmpty(dominantTribe))
                return false;
            if (dominantTribe == "全" || dominantTribe == "全部"
                || dominantTribe == "ALL")
                return false;
            foreach (var tribe in MinionData.GetTribesArray(tribeField))
            {
                if (tribe == dominantTribe || tribe == "全" || tribe == "全部"
                    || tribe == "ALL")
                    return false;
            }
            return true;
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
            var mainline = _mainline.Analyze(
                state, _compGuideCatalog, compLockResult);
            _lastMainlinePlan = mainline;

            if (compLockResult.State == LockState.Hard)
                result.LockIcon = "H";
            else if (compLockResult.State == LockState.Soft)
                result.LockIcon = "S";
            result.Archetype = mainline.Archetype ?? "";
            result.Reason = mainline.Reason ?? "";
            result.MissingCoreCardNames = mainline.MissingCoreCardNames
                ?? new List<string>();

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
            // 无报价→null。ActionEnumerator 已将每个 TrinketOffer 枚举为 PickTrinket 候选。
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
            int healthArmor = state.Health + state.Armor;
            var boardKeywords = ExtractBoardKeywords(state);
            var recommendations = _trinketRecommendations.Evaluate(
                state.TrinketOffer, domTribe, healthArmor, state.Turn, boardKeywords,
                state.BoardMinions == null ? -1 : state.BoardMinions.Count);
            foreach (var recommendation in recommendations)
            {
                double guideWeightBonus = CompGuideTrinketWeightBonus(
                    recommendation, out bool isGuideRated);
                results.Add(new TrinketScore
                {
                    Index = recommendation.Index,
                    CardId = recommendation.CardId,
                    Name = recommendation.DisplayName,
                    Score = recommendation.RuleScore + guideWeightBonus,
                    IsLesser = recommendation.IsLesser,
                    IsUnrated = isGuideRated
                        ? false
                        : recommendation.IsUnrated,
                    MatchedRuleIds = recommendation.MatchedRuleIds,
                });
            }
            return results;
        }

        private static List<string> ExtractBoardKeywords(GameState state)
        {
            var keywords = new List<string>();
            if (state == null || state.BoardMinions == null) return keywords;
            // 机制枚举 -> 中文关键词(对齐模拟器 R204 _kwMap)
            var textMap = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "战吼", "BATTLECRY" }, { "亡语", "DEATHRATTLE" },
                { "圣盾", "DIVINE_SHIELD" }, { "复生", "REBORN" },
                { "风怒", "WINDFURY" }, { "嘲讽", "TAUNT" },
                { "剧毒", "VENOMOUS" }, { "磁力", "MAGNETIC" },
                { "战斗开始", "START_OF_COMBAT" }, { "光环", "AURA" },
                { "回合结束", "END_OF_TURN_TRIGGER" }, { "进击", "BACON_RALLY" },
                { "塑造法术", "BACON_SPELLCRAFT_ID" }, { "复仇", "AVENGE" },
            };
            foreach (var m in state.BoardMinions)
            {
                if (m == null) continue;
                string text = (m.CardText ?? "") + " " + (m.CardName ?? "");
                foreach (var kv in textMap)
                {
                    if (text.Contains(kv.Key) && !keywords.Contains(kv.Value))
                        keywords.Add(kv.Value);
                }
                // R214 补充: 核心引擎卡检测(对齐模拟器 R210)——铜须/瑞文类
                // 文本含"你的战吼/你的亡语/触发两次"等特征, 标记为 ENGINE_*
                // 供 TrinketRuleEvaluator 协同加分(+2, 高于普通板面关键词 +1)
                if (text.Contains("战吼") && (text.Contains("你的战吼")
                    || text.Contains("战吼触发两次") || text.Contains("战吼会触发")))
                { if (!keywords.Contains("ENGINE_BATTLECRY")) keywords.Add("ENGINE_BATTLECRY"); }
                if (text.Contains("亡语") && (text.Contains("你的亡语")
                    || text.Contains("亡语触发两次") || text.Contains("亡语会触发")))
                { if (!keywords.Contains("ENGINE_DEATHRATTLE")) keywords.Add("ENGINE_DEATHRATTLE"); }
                if (text.Contains("磁力") && (text.Contains("你的磁力")
                    || text.Contains("磁力触发") || text.Contains("磁力吸附")))
                { if (!keywords.Contains("ENGINE_MAGNET")) keywords.Add("ENGINE_MAGNET"); }
                if (text.Contains("回合结束") && text.Contains("触发两次"))
                { if (!keywords.Contains("ENGINE_END_OF_TURN")) keywords.Add("ENGINE_END_OF_TURN"); }
            }
            return keywords;
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
