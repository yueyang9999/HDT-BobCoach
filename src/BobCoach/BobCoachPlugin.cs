using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Threading;
using Hearthstone_Deck_Tracker.Plugins;
using Hearthstone_Deck_Tracker.API;
using Newtonsoft.Json;
using Hearthstone_Deck_Tracker.Enums;
using HearthDb.Enums;
using BobCoach.Engine;
using BobCoach.Engine.Decision;

namespace BobCoach
{
    public class BobCoachPlugin : IPlugin
    {
        /// <summary>卡牌中文名查询 → CardNameService (统一入口)</summary>
        public static string GetCardNameCn(string cardId) => Engine.CardNameService.GetName(cardId);
        /// <summary>卡牌星级查询 → CardNameService (统一入口)</summary>
        public static int GetCardTier(string cardId) => Engine.CardNameService.GetTier(cardId);

        private GameStateExtractor _extractor;
        private DecisionEngine _engine;
        private DecisionPolicyBundle _decisionPolicies;
        private DecisionOrchestrator _decisionOrchestrator;
        private DecisionEventRecorder _decisionEventRecorder;
        private DecisionLifecycleTracker _decisionLifecycle;
        // 2026-08-19 用户授权: 默认同意本地决策事件录制(不再依赖手动弹窗点选)。
        // 隐私契约不变: 事件仅含决策状态/动作/结果, 本机保存(decision-data/decision-events.jsonl)。
        // 仍受 Power.log 配置检查门控(log.config 未配置时自动 fail-closed 不写)。
        private bool _decisionEventRecordingOptedIn = true;
        private FeatureExtractor _fe;
        private OverlayRenderer _renderer;
        private StatusStripSettingsWindow _statusStripSettingsWindow;
        private MenuItem _settingsMenuItem;
        private CardPoolTracker _poolTracker;
        private ProbabilityCalculator _probCalc;
        private ProfileEngine _profileEngine;
        private HeroPowerEngine _heroPowerEngine;
        private HeroPlaybookRegistry _heroPlaybook;
        private readonly HeroSkillCatalog _heroSkillCatalog = new HeroSkillCatalog();
        private static readonly Lazy<Season14TavernSpellRegistry> TavernSpellLookup =
            new Lazy<Season14TavernSpellRegistry>(() =>
            {
                var registry = new Season14TavernSpellRegistry();
                registry.LoadFromConfiguredRoot();
                return registry;
            });
        private string _lastUsedTavernSpellCardId = "";
        private string _lastUsedTavernSpellName = "";
        private DecisionVisualizer _visualizer;
        private GameRecord _currentGameRecord;
        private Engine.GlobalReplayRecorder _globalReplayRecorder;
        private readonly Dictionary<int, string> _powerEntityZones = new Dictionary<int, string>();
        private readonly Dictionary<int, string> _powerEntityCards = new Dictionary<int, string>();
        private readonly Dictionary<int, int> _powerEntityPlayers = new Dictionary<int, int>();
        private DecisionMode _decisionMode;
        private Engine.PowerLogWatcher _powerLogWatcher;
        private bool _powerLogEventsSubscribed;
        private bool _inBattlegrounds;
        private bool _engineReady;
        private string _powerLogPhase = "shop";
        private string _lastStateHash;
        private string _lastEvalDumpSig; // Phase0评测: 全状态转储节流签名
        private readonly Engine.TrinketShadowCaptureSession _trinketShadowCaptureSession =
            new Engine.TrinketShadowCaptureSession();
        // P1.5 Phase4(路线B冻结, 被动shadow): 默认关, 环境变量 BOBCOACH_TRINKET_SHADOW=1 开启。
        // 零副作用只读采集T6/T9饰品报价+引擎评分, 为未来B解冻(~300条)积累样本。类初始化读一次, 关时零帧开销。
        private static readonly bool _trinketShadowEnabled =
            Environment.GetEnvironmentVariable("BOBCOACH_TRINKET_SHADOW") == "1";
        private const bool TrinketRecommendationsVisible = true;
        private string _lastRenderedHash;
        private string _lastRenderedPlanHash;  // 防重复dispatch
        private bool _suggestionsActive;
        private bool _lastLiveSafetySuppressRefresh;
        private HashSet<int> _prevBoardIds = new HashSet<int>();
        private int _prevGoldenCount = -1;        // 检测三连触发
        private int _prevGoldenHandCount = -1;    // 检测手牌三连奖励触发
        private bool _prevHpExhausted = false;    // 检测英雄技能触发
        private HashSet<int> _prevExhaustedHeroPowerEntityIds = new HashSet<int>();
        private DateTime _boardLeaveDiscoverPendingUntil = DateTime.MinValue; // 出售发现源后的zone6等待窗口(07062157)
        private DateTime _handDiscoverPendingUntil = DateTime.MinValue;      // 手牌减少(法术/战吼)后的zone6等待窗口(07062242)
        private int _prevBoardCountForDiscover = -1;
        private bool _prevBoardHadDiscoverSource = false;
        private string _discoverSource = "";      // "triple" / "heroPower" / "battlecry" / ""
        private Engine.PowerLogChoiceBatch _discoverBatchFromLog = null;
        private Engine.PowerLogChoiceBatch _timewarpPurchaseBatchFromLog = null;
        private volatile bool _timewarpPurchaseClearRequested;
        private Engine.PowerLogChoiceBatch _darkGiftBatchFromLog = null;
        private int _darkGiftUsedCount;
        private int _darkGiftUsedTurn = -1;
        private bool _passiveChoiceActive;
        private readonly Engine.DarkGiftTipRenderCoordinator _darkGiftTipCoordinator =
            new Engine.DarkGiftTipRenderCoordinator(TimeSpan.FromSeconds(6));
        private readonly HashSet<int> _claimedUpgradePrizeChoiceIds = new HashSet<int>();
        private readonly Engine.TrinketChoiceBatchLifecycle _trinketChoiceLifecycle =
            new Engine.TrinketChoiceBatchLifecycle();
        private Engine.Season14RulesEvaluator _season14RulesEvaluator;
        private int _prevDiscoverCount = -1; // 上帧发现选项数, 仅用于诊断
        // v2 事件驱动标志位: Power.log ChoiceList 事件直接控制面板显隐, 替代TTL帧计数
        private bool _discoverPanelActive = false;
        private bool _trinketPanelActive = false;
        private bool _lastTrinketShowState = false;    // 面板显隐状态变化追踪
        private int _lastTrinketHideTurn = -1;
        private bool _lastDiscoverShowState = false;
        // 面板生命周期状态机(持久持有, 跨帧存活) — GameState 每帧重建无法持有, 这是状态机曾失效的根因
        private Engine.PanelState _trinketPanelState = new Engine.PanelState(Engine.PanelPhase.Idle, 1, 500);
        private Engine.PanelState _discoverPanelState = new Engine.PanelState(Engine.PanelPhase.Idle, 1, 1000);
        private Engine.UiTargetStateMachine _trinketTargetState = new Engine.UiTargetStateMachine(120, 1500, 2);
        private Engine.UiTargetStateMachine _discoverTargetState = new Engine.UiTargetStateMachine(120, 1000, 2);
        private DateTime _trinketHideRequestedAt = DateTime.MinValue;   // 饰品隐藏请求时间(滞回)
        private DateTime _discoverHideRequestedAt = DateTime.MinValue;  // 发现隐藏请求时间(滞回)
        private DateTime _discoverFirstShownAt = DateTime.MinValue;    // 发现面板首次显示时间(最大驻留)
        private DateTime _combatEndedAt = DateTime.MinValue; // 战斗结束时间(发现扫描冷却)
        private DateTime _discoverPanelActivatedAt = DateTime.MinValue; // Power.log面板标志设置时间
        private DateTime _trinketPanelActivatedAt = DateTime.MinValue;
        private DateTime _lastDiscoverTriggerTime = DateTime.MinValue;  // zone6/三连/技能触发时间(3s扫描窗口)
        private readonly HashSet<string> _triggeredScheduledDiscoverOccurrenceIds =
            new HashSet<string>(StringComparer.Ordinal);
        private HashSet<int> _prevHandIds = new HashSet<int>();
        private int _prevTavernTier = 1;
        private int _lastUpgradeTurn = 0;  // 0=未升过本, 防止T1误判justLeveled
        private GameState _prevState;  // 上一帧状态, 用于推导玩家操作
        private int _lastRenderShopCount = -1;
        private int _lastRenderBoardCount = -1;
        private int _lastRenderGold = -1;
        private int _lastRenderTrinketCount = -1;
        private int _lastRenderDiscoverCount = -1;

        private int _updateCallCount;
        private int _updateSkippedNoChange;
        private int _updateSkippedCombat;
        private int _updateEvaluated;
        // R219: 整回合动作链缓存(玩家连续操作时推荐计划而非时点快照)
        private int _turnPlanTurn = -1;
        private string _turnPlanShopSig = "";
        // P1 2026-08: 动态铸币管理——金币变化时动作链重算(升本后金币归零不再显示买卡)
        private int _turnPlanGold = -1;
        private string _turnPlanChain = "";
        private int _renderVersion;
        // 1.2.1 feedback4: DispatchRender merge-snapshot — high-frequency shop change (ReplenishingShop gauntlet)
//        auto-refill / rapid actions) raced _renderVersion so queued callbacks all expired & dropped, UI starved (turn7/8 no render at all).
//        On version-expiry now render latest pending snapshot (frame merge) so UI always reflects newest state.
//        see AGENTS.md Release Deployment SOP SHA pairing for deploy rules.
        private GameState _pendingRenderState;
        private Engine.VisualPlan _pendingRenderPlan;
        private string _pendingRenderHeroHint;
        private string _pendingRenderHeroUseSuggestion;
        private bool _wasInCombat = false;           // 阶段追踪
        private DateTime _lastShopChange = DateTime.MinValue;
        private DateTime _lastShopUiClearAt = DateTime.MinValue;
        private const int ShopUiClearCooldownMs = 250;
        private bool _lastFrameFrozen = false;       // 冻结跨回合UI保护
        private int _lastRenderTurn = 0;
        private int _lastUiDiagTurn = -1;
        private int _lastUiDiagGold = -1;
        private string _lastUiDiagSig = "";   // 1.2.2 T1状态条文字诊断节流
        private DateTime _lastRenderTime = DateTime.MinValue;
        private string _lastShopIdStr = "";
        private int _lastShopCount = 0;                    // 商店实体数稳定性跟踪
        private int _shopStableFrames = 0;                 // 连续相同帧数
        private Engine.VisualPlan _cachedPlan;         // 持久UI用的缓存计划
        private GameState _cachedState;                  // 持久UI用的缓存状态
        private bool _persistentDirty = true;            // Clear后需刷新持久UI
        private DateTime _lastF10Toggle = DateTime.MinValue;
        private bool _f10Down = false;
        private bool _f9Down = false;
        private DateTime _lastF9Toggle = DateTime.MinValue;
        private bool _f1Down, _f2Down, _f3Down, _f4Down, _f5Down, _f6Down;
        private UIDemoMode _demoMode;
        private int _lastHeroHintTurn = 0;       // 英雄提示每回合只显示一次

        // 购买平滑重绘: 检测购买/刷新事件, 控制淡入淡出时序
        private Engine.PurchaseDetector _purchaseDetector = new Engine.PurchaseDetector();
        private readonly Engine.ShopLayoutSlotPolicy _shopLayoutSlotPolicy = new Engine.ShopLayoutSlotPolicy();
        private Engine.ShopLayoutDecision _shopLayoutDecision = new Engine.ShopLayoutDecision(1, true);
        private bool _redrawPending = false;           // 购买后等待重绘
        private DateTime _redrawRequestedAt = DateTime.MinValue;
        private System.Windows.Threading.DispatcherTimer _combatWatchTimer; // 兜底战斗监控(唯一)
        private DateTime _recruitStartTime = DateTime.MinValue;  // 当前招募阶段开始时间
        private bool _preCombatCleared;                           // 临近战斗已提前清除UI
        private DateTime _lastRecruitEndTime = DateTime.MinValue; // 上回合招募结束时间(战斗开始)
        private DateTime _lastTargetPulseTime = DateTime.MinValue; // 目标脉冲最后设置时间(3秒持久)
        private DateTime _refreshGlowSince = DateTime.MinValue;    // 刷新光晕首次显示时间(10s自动抑制)
        private DateTime _lastEventEval = DateTime.MinValue;      // 事件驱动评估防抖
        private const int EventEvalDebounceMs = 200;               // 事件驱动防抖间隔(ms)
        private volatile bool _eventEvalRequested;                  // 后台线程通知UI线程评估
        private DateTime _freezeGlowSince = DateTime.MinValue;     // 冻结光晕首次显示时间(10s自动抑制)
        private double _learnedRecruitSec = 55;                   // 自学习的招募时长(秒), 默认55

        // 卡池过滤: 按当局种族限制卡牌
        private Dictionary<string, (int tier, string tribe, bool isSpell)> _cardMeta
            = new Dictionary<string, (int, string, bool)>();
        private bool _poolFiltered = false;
        private bool _buddyPoolEnabled = false;

        // ── IPlugin interface ──

        public string Name { get { return "BobCoach"; } }
        public string Description { get { return "酒馆战棋实时教学插件"; } }
        public string ButtonText { get { return "Bob教练"; } }
        public string Author { get { return "BobCoach"; } }
        public Version Version { get { return Assembly.GetExecutingAssembly().GetName().Version; } }
        public System.Windows.Controls.MenuItem MenuItem
        {
            get
            {
                if (_settingsMenuItem == null)
                {
                    _settingsMenuItem = new MenuItem { Header = "Bob教练设置" };
                    _settingsMenuItem.Click += (sender, args) => OpenStatusStripSettings();
                }
                return _settingsMenuItem;
            }
        }

        public void OnLoad()
        {
            var assembly = Assembly.GetExecutingAssembly();
            Log(string.Format("BobCoach v{0} loading...", assembly.GetName().Version));

            var integrity = Engine.PluginIntegrityVerifier.Verify(assembly.Location);
            if (!integrity.IsValid)
            {
                Log("Plugin integrity check FAILED: " + integrity.Reason);
                return;
            }
            Log("Plugin integrity check passed");

            if (!SecuritySelfCheck())
            {
                Log("Security self-check FAILED.");
                return;
            }

            try
            {
                _extractor = new GameStateExtractor();
                _engine = new DecisionEngine();
                using (var modelArtifact = assembly.GetManifestResourceStream(
                    "BobCoach.DecisionModels.linear-v1.envelope.json"))
                {
                    _decisionPolicies = DecisionPolicyFactory.Create(_engine, modelArtifact,
                        ProductionSeasonFeatureExtension.Instance);
                }
                _decisionOrchestrator = new DecisionOrchestrator(
                    _decisionPolicies.Legacy, _decisionPolicies.Unified,
                    NoOpShadowComparisonSink.Instance, DecisionRuntimeMode.Shadow);
                _fe = new FeatureExtractor();
                _renderer = new OverlayRenderer();
                // 2026-08-19 C2: UI 被游戏图层遮挡——确保 HDT overlay 窗口置顶
                // (插件渲染在 OverlayCanvas 上, 若 overlay 窗口未置顶则被游戏画面盖住;
                // 向上找父 Window 设置 Topmost, 失败静默不影响加载)
                try
                {
                    var ovCanvas = Core.OverlayCanvas;
                    if (ovCanvas != null)
                    {
                        System.Windows.Media.Visual visual = ovCanvas;
                        while (visual != null && !(visual is System.Windows.Window))
                            visual = System.Windows.Media.VisualTreeHelper.GetParent(visual) as System.Windows.Media.Visual;
                        var ovWindow = visual as System.Windows.Window;
                        if (ovWindow != null && !ovWindow.Topmost)
                        {
                            ovWindow.Topmost = true;
                            Log("C2: overlay window topmost enabled");
                        }
                    }
                }
                catch { }
                _poolTracker = new CardPoolTracker();
                _probCalc = new ProbabilityCalculator(_poolTracker);
                _fe.SetProbabilityCalculator(_probCalc);
                _profileEngine = new ProfileEngine();
                _heroPowerEngine = _engine.HeroPower;
                _heroPlaybook = _engine.HeroPlaybook;
                _extractor.SetHeroPowerEngine(_heroPowerEngine);
                _extractor.SetAnomalyRegistry(_engine.AnomalyReg);
                _extractor.SetTrinketFactSource(_engine.TrinketFactSource);
                _season14RulesEvaluator = new Engine.Season14RulesEvaluator();
                _season14RulesEvaluator.LoadFromConfiguredRoot();
                _extractor.SetSeason14RulesEvaluator(_season14RulesEvaluator);
                Log("Offline C# engine enabled");
                _visualizer = new DecisionVisualizer();
                _engine.SetProfileEngine(_profileEngine);
                _decisionMode = LoadDecisionMode();
                _engine.Mode = _decisionMode;
                _demoMode = new UIDemoMode();
                _engineReady = true;
                LoadLearnedRecruitSec(); // 跨会话继承自学习招募时长
                Log(string.Format("Decision mode: {0}", _decisionMode));

                // 诊断: 记录坐标系统参数
                try
                {
                    var client = Engine.SafeNativeMethods.GetHearthstoneClientRect();
                    if (client.HasValue)
                        Log(string.Format("CoordSys: clientRect=({0},{1})-({2},{3}) size={4}x{5}",
                            client.Value.Left, client.Value.Top,
                            client.Value.Right, client.Value.Bottom,
                            client.Value.Width, client.Value.Height));
                    else
                        Log("CoordSys: clientRect not available");
                }
                catch { }

                GameEvents.OnGameStart.Add(OnGameStart);
                GameEvents.OnGameEnd.Add(OnGameEnd);
                GameEvents.OnTurnStart.Add(OnTurnStart);
                GameEvents.OnInMenu.Add(OnInMenu);
                EnsureCombatWatchTimer();

                Log("BobCoach ready.");
            }
            catch (Exception ex)
            {
                Log("OnLoad error: " + ex);
                _engineReady = false;
            }
        }

        public void OnUnload()
        {
            _inBattlegrounds = false;
            _decisionEventRecordingOptedIn = false;
            ResetDecisionRecording();
            CloseStatusStripSettings();
            ClearSuggestions();
            try { UnsubscribePowerLogWatcherEvents(); } catch { }
            try { _powerLogWatcher?.StopWatching(); } catch { }
            try { if (_globalReplayRecorder != null) { _globalReplayRecorder.Dispose(); _globalReplayRecorder = null; } } catch { }
            try
            {
                if (_combatWatchTimer != null)
                {
                    _combatWatchTimer.Stop();
                    _combatWatchTimer = null;
                }
            }
            catch { }
            Log("BobCoach unloaded");
        }

        private void OpenStatusStripSettings()
        {
            if (_renderer == null)
            {
                Log("Status strip settings unavailable: renderer is not initialized");
                return;
            }

            if (_statusStripSettingsWindow != null)
            {
                if (_statusStripSettingsWindow.WindowState == WindowState.Minimized)
                    _statusStripSettingsWindow.WindowState = WindowState.Normal;
                _statusStripSettingsWindow.Activate();
                return;
            }

            var window = new StatusStripSettingsWindow(_renderer);
            window.Closed += StatusStripSettingsWindowClosed;
            _statusStripSettingsWindow = window;
            window.Show();
            window.Activate();
        }

        private void StatusStripSettingsWindowClosed(object sender, EventArgs e)
        {
            if (ReferenceEquals(_statusStripSettingsWindow, sender))
                _statusStripSettingsWindow = null;
        }

        private void CloseStatusStripSettings()
        {
            var window = _statusStripSettingsWindow;
            _statusStripSettingsWindow = null;
            if (window != null)
            {
                window.Closed -= StatusStripSettingsWindowClosed;
                window.Close();
            }
            _renderer?.SetStatusStripUnlocked(false);
        }

        public void OnButtonPress()
        {
            try
            {
                Log("Power.log configuration assistant requested");
                var plan = Engine.LogConfigEnsurer.Inspect();
                if (plan.Status == Engine.LogConfigStatus.Error)
                {
                    _decisionEventRecordingOptedIn = false;
                    ResetDecisionRecording();
                    MessageBox.Show("无法定位或读取 Hearthstone log.config。\n\n" +
                        string.Join("\n", plan.Changes ?? new List<string>()),
                        "Bob教练 Power.log 配置", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var configReady = plan.Status == Engine.LogConfigStatus.OK;
                var message = configReady
                    ? "Power.log 所需的 log.config 已完整。\n\n文件：" + plan.ConfigPath +
                        "\n\n是否允许 Bob教练在本次插件运行期间，将决策快照和观察到的玩家操作记录到本机 decision-events.jsonl？"
                    : "Bob教练需要以下 Power.log 配置。只有点击“是”才会写入。\n\n" +
                        "文件：" + plan.ConfigPath + "\n\n" +
                        "变更：\n- " + string.Join("\n- ", plan.Changes) + "\n\n" +
                        "拟议完整内容：\n" + plan.ProposedContent +
                        "\n\n点击“是”还会允许 Bob教练在本次插件运行期间，将决策快照和观察到的玩家操作记录到本机 decision-events.jsonl。是否应用并启用？";
                var result = MessageBox.Show(message, "Bob教练本地决策录制",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes)
                {
                    _decisionEventRecordingOptedIn = false;
                    ResetDecisionRecording();
                    Log("Power.log configuration and local decision recording declined");
                    return;
                }

                if (configReady)
                {
                    _decisionEventRecordingOptedIn = true;
                    MessageBox.Show("本地决策事件录制已启用，将从下一局开始生效。",
                        "Bob教练本地决策录制", MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    Log("Local decision recording opted in for this plugin session");
                    return;
                }

                var status = Engine.LogConfigEnsurer.Apply(plan);
                if (status == Engine.LogConfigStatus.Created || status == Engine.LogConfigStatus.Patched)
                {
                    _decisionEventRecordingOptedIn = true;
                    MessageBox.Show("log.config 已按确认内容写入。请完全关闭并重新启动炉石后生效。",
                        "Bob教练 Power.log 配置", MessageBoxButton.OK, MessageBoxImage.Information);
                    Log("Power.log configuration applied: " + status);
                }
                else if (status == Engine.LogConfigStatus.Conflict)
                {
                    _decisionEventRecordingOptedIn = false;
                    ResetDecisionRecording();
                    MessageBox.Show("确认期间 log.config 已被其他程序修改，本次未覆盖。请重新点击 Bob教练检查。",
                        "Bob教练 Power.log 配置", MessageBoxButton.OK, MessageBoxImage.Warning);
                    Log("Power.log configuration conflict; no file changed");
                }
                else
                {
                    _decisionEventRecordingOptedIn = false;
                    ResetDecisionRecording();
                    MessageBox.Show("写入 log.config 失败，本次未完成配置。",
                        "Bob教练 Power.log 配置", MessageBoxButton.OK, MessageBoxImage.Error);
                    Log("Power.log configuration apply failed; Power.log feature remains disabled");
                }
            }
            catch (Exception ex)
            {
                Log("Power.log configuration assistant error: " + ex.Message);
            }
        }

        public void OnUpdate()
        {
            _updateCallCount++;

            if (!_engineReady || !_inBattlegrounds) return;

            try
            {
                var game = Core.Game;
                if (game == null || game.Entities == null) return;

                // V1.4: PhaseDetector替代IsBattlegroundsCombatPhase
                int turn = 1; try { turn = game.GetTurnNumber(); } catch { }
                _renderer.UpdatePhase(turn);

                // F10校准热键: 手动边沿检测(上升沿触发切换)
                try
                {
                    short raw = BobCoach.Engine.SafeNativeMethods.GetAsyncKeyState(0x79);
                    bool isDown = (raw & 0x8000) != 0;
                    if (isDown && !_f10Down) // 上升沿: 刚按下
                    {
                        _f10Down = true;
                        if ((DateTime.Now - _lastF10Toggle).TotalSeconds > 0.8)
                        {
                            _lastF10Toggle = DateTime.Now;
                            if (_renderer.CalibrationActive)
                            {
                                _renderer.DeactivateCalibration();
                                _renderer.RefreshLayout(); // 立即加载新保存的ui_config.json
                                ClearSuggestions();
                                Log("CALIB OFF");
                            }
                            else
                            {
                                ClearSuggestions();
                                _renderer.ActivateCalibration();
                                Log("CALIB ON");
                            }
                        }
                    }
                    if (!isDown) _f10Down = false;

                    // F9演示模式热键
                    short f9raw = BobCoach.Engine.SafeNativeMethods.GetAsyncKeyState(0x78);
                    bool f9down = (f9raw & 0x8000) != 0;
                    if (f9down && !_f9Down)
                    {
                        _f9Down = true;
                        if ((DateTime.Now - _lastF9Toggle).TotalSeconds > 0.8)
                        {
                            _lastF9Toggle = DateTime.Now;
                            if (_demoMode.Active)
                            {
                                _demoMode.Deactivate();
                                ClearSuggestions();
                                Log("DEMO OFF");
                            }
                            else
                            {
                                ClearSuggestions();
                                _demoMode.Activate();
                                Log("DEMO ON");
                            }
                        }
                    }
                    if (!f9down) _f9Down = false;
                }
                catch { }

                // 校准模式：渲染校准覆盖层，跳过正常逻辑
                if (_renderer.CalibrationActive)
                {
                    _renderer.RenderCalibration();
                    // F1-F6按键检测(GetAsyncKeyState, 避免WPF焦点问题)
                    try
                    {
                        short f1raw = BobCoach.Engine.SafeNativeMethods.GetAsyncKeyState(0x70);
                        bool f1down = (f1raw & 0x8000) != 0;
                        if (f1down && !_f1Down) { _f1Down = true; _renderer.HandleCalibKey(Key.F1); }
                        if (!f1down) _f1Down = false;

                        short f2raw = BobCoach.Engine.SafeNativeMethods.GetAsyncKeyState(0x71);
                        bool f2down = (f2raw & 0x8000) != 0;
                        if (f2down && !_f2Down) { _f2Down = true; _renderer.HandleCalibKey(Key.F2); }
                        if (!f2down) _f2Down = false;

                        short f3raw = BobCoach.Engine.SafeNativeMethods.GetAsyncKeyState(0x72);
                        bool f3down = (f3raw & 0x8000) != 0;
                        if (f3down && !_f3Down) { _f3Down = true; _renderer.HandleCalibKey(Key.F3); }
                        if (!f3down) _f3Down = false;

                        short f4raw = BobCoach.Engine.SafeNativeMethods.GetAsyncKeyState(0x73);
                        bool f4down = (f4raw & 0x8000) != 0;
                        if (f4down && !_f4Down) { _f4Down = true; _renderer.HandleCalibKey(Key.F4); }
                        if (!f4down) _f4Down = false;

                        short f5raw = BobCoach.Engine.SafeNativeMethods.GetAsyncKeyState(0x74);
                        bool f5down = (f5raw & 0x8000) != 0;
                        if (f5down && !_f5Down) { _f5Down = true; _renderer.HandleCalibKey(Key.F5); }
                        if (!f5down) _f5Down = false;

                        short f6raw = BobCoach.Engine.SafeNativeMethods.GetAsyncKeyState(0x75);
                        bool f6down = (f6raw & 0x8000) != 0;
                        if (f6down && !_f6Down) { _f6Down = true; _renderer.HandleCalibKey(Key.F6); }
                        if (!f6down) _f6Down = false;
                    }
                    catch { }
                    // 方向键处理
                    try
                    {
                        if (System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.Up)) _renderer.HandleCalibKey(System.Windows.Input.Key.Up);
                        if (System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.Down)) _renderer.HandleCalibKey(System.Windows.Input.Key.Down);
                        if (System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.Left)) _renderer.HandleCalibKey(System.Windows.Input.Key.Left);
                        if (System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.Right)) _renderer.HandleCalibKey(System.Windows.Input.Key.Right);
                        if (System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.D1)) _renderer.HandleCalibKey(System.Windows.Input.Key.D1);
                        if (System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.D2)) _renderer.HandleCalibKey(System.Windows.Input.Key.D2);
                        if (System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.D3)) _renderer.HandleCalibKey(System.Windows.Input.Key.D3);
                        if (System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.D4)) _renderer.HandleCalibKey(System.Windows.Input.Key.D4);
                        if (System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.D5)) _renderer.HandleCalibKey(System.Windows.Input.Key.D5);
                        if (System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.D6)) _renderer.HandleCalibKey(System.Windows.Input.Key.D6);
                        if (System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.D7)) _renderer.HandleCalibKey(System.Windows.Input.Key.D7);
                        if (System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.D8)) _renderer.HandleCalibKey(System.Windows.Input.Key.D8);
                        if (System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.D9) || System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.NumPad9)) _renderer.HandleCalibKey(System.Windows.Input.Key.D9);
                        if (System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.G)) _renderer.HandleCalibKey(System.Windows.Input.Key.G);
                        if (System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.OemOpenBrackets)) _renderer.HandleCalibKey(System.Windows.Input.Key.OemOpenBrackets);
                        if (System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.OemCloseBrackets)) _renderer.HandleCalibKey(System.Windows.Input.Key.OemCloseBrackets);
                        if (System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.OemMinus)) _renderer.HandleCalibKey(System.Windows.Input.Key.OemMinus);
                        if (System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.OemPlus)) _renderer.HandleCalibKey(System.Windows.Input.Key.OemPlus);
                        if (System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.R)) _renderer.HandleCalibKey(System.Windows.Input.Key.R);
                        if (System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.S)) _renderer.HandleCalibKey(System.Windows.Input.Key.S);
                    }
                    catch { }
                    return;
                }

                // ── 战斗阶段: 仅信 HDT IsBattlegroundsCombatPhase (炉石稳定 API) ──
                // 注: 曾加 STEP>=10 启发式想"提前1帧清UI", 但 STEP 枚举 MAIN_ACTION=10 是招募主操作
                // 阶段(非战斗), 导致招募全程误判为战斗、面板渲染后立即被清(0610 回归)。已移除。
                // "防战斗残留"由 DispatchRender 内的 combat 检查 + capturedVersion 检查兜底。
                bool inCombat = false;
                try { inCombat = Core.Game.IsBattlegroundsCombatPhase; } catch { }
                if (inCombat || !_renderer.CanRender)
                {
                    if (!_wasInCombat)
                    {
                        // 进入战斗瞬间: 清UI, 保留plan缓存(出战斗时RefreshPersistentUI可复用)
                        Log(string.Format("CombatEnter: api={0} canRender={1} → Clear (turn={2})",
                            inCombat, _renderer.CanRender, turn));
                        try { _renderer.Clear(); } catch { }
                        _renderVersion++;
                        _suggestionsActive = false;
                        _persistentDirty = true;
                        _recruitStartTime = DateTime.MinValue; // 战斗开始, 重置招募计时
                    }
                    EnsureCombatWatchTimer();
                    RenderDarkGiftTipStatus();
                    _wasInCombat = true;
                    _lastStateHash = null; _lastRenderedHash = null; _lastShopIdStr = "";
                    _lastShopCount = 0; _shopStableFrames = 0;
                    if (_demoMode.Active) { _demoMode.Deactivate(); Log("DEMO OFF (combat)"); }
                    _extractor.CollectOpponentIds();
                    _updateSkippedCombat++;
                    return;
                }
                // 战斗结束检测: 记录时间, 发现扫描冷却2秒防战斗衍生物
                if (_wasInCombat)
                {
                    _combatEndedAt = DateTime.Now;
                    _shopLayoutSlotPolicy.BeginRefresh();
                }
                _wasInCombat = false;
                // 记录招募阶段开始时间(用于提前2-3秒清UI防战斗残留)
                if (_recruitStartTime == DateTime.MinValue)
                    _recruitStartTime = DateTime.Now;
                _preCombatCleared = false;

                // ── F9 UI演示模式 ──
                if (_demoMode.Active)
                {
                    try
                    {
                        if (System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.Left))
                            _demoMode.Prev();
                        if (System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.Right))
                            _demoMode.Next();
                    }
                    catch { }

                    var demoState = _demoMode.GetScenario();
                    var demoAction = _engine.GetBestAction(demoState);
                    var demoLevel = _engine.GetLevelUpSuggestion(demoState);
                    var demoCardScores = _engine.GetShopCardScores(demoState);
                    var demoPlan = _visualizer.CreateVisualPlan(demoState, demoAction, demoLevel, demoCardScores);
                    string demoHeroHint = HeroPowerDisplayTextResolver.Resolve(demoState.HeroPowerCardId);
                    DispatchRender(demoState, demoHeroHint, demoPlan);

                    // 显示场景名标签
                    try
                    {
                        _renderer.ShowSuggestionBadge(_demoMode.ScenarioName, "#FF9800",
                            string.Format("F9演示 | Turn{0} T{1} | 左右切场景", demoState.Turn, demoState.TavernTier));
                    }
                    catch { }
                    return;
                }

                // ── 提前清UI：根据回合估算招募时长，剩3秒时自动清除防止战斗残留 ──
                CheckPreCombatClear(turn);

                // ── 持久UI刷新（每帧，不受决策冷却影响）──
                RefreshPersistentUI();

                // ── 步骤1: 商店变化检测（必须在hash检查前，捕获实体过渡）──
                var shopIds = new List<int>();
                try
                {
                    foreach (var e in game.Entities.Values.ToList())
                    {
                        if (e == null) continue;
                        if (e.GetTag(HearthDb.Enums.GameTag.ZONE) != 5) continue;
                        int ct = e.HasTag(HearthDb.Enums.GameTag.CARDTYPE) ? e.GetTag(HearthDb.Enums.GameTag.CARDTYPE) : 0;
                        if (!Engine.ShopEntitySnapshotPolicy.IsChangeDetectionCandidate((CardType)ct)) continue;
                        shopIds.Add(e.Id);
                    }
                    shopIds.Sort();
                }
                catch { }
                string currentIdStr = string.Join(",", shopIds);
                // 仅当新商店非空且ID发生变化时触发（去掉_lastShopIdStr非空限制，修复空→满过渡漏检）
                if (currentIdStr.Length > 0 && currentIdStr != _lastShopIdStr)
                {
                    // 检测变化类型: 购买(数量减少) vs 刷新(ID全变)
                    var changeType = _purchaseDetector.DetectChange(shopIds.Count, shopIds);
                    if (changeType == Engine.ShopChangeType.Purchased || changeType == Engine.ShopChangeType.Refreshed)
                    {
                        if (changeType == Engine.ShopChangeType.Refreshed)
                            _shopLayoutSlotPolicy.MarkRefreshDetected();
                        else
                            _shopLayoutSlotPolicy.ObservePurchase();
                        RequestShopUiRefresh(DateTime.Now);
                        if (changeType == Engine.ShopChangeType.Purchased)
                        {
                            // 购买: 跳过所有防抖, 立即重评(ZonePosition已更新)
                            _lastShopChange = DateTime.MinValue;
                            _lastShopIdStr = currentIdStr;
                            _eventEvalRequested = true;
                            _redrawPending = false;
                            // 跳过末尾的 _lastShopChange=Now + return, 直接落入 EvaluateAndRender
                            goto AfterShopChangeDetection;
                        }
                        else
                        {
                            _redrawPending = true;
                            _redrawRequestedAt = DateTime.Now;
                            _lastShopChange = DateTime.Now;
                            _lastShopIdStr = currentIdStr;
                            return;
                        }
                    }
                    else if (changeType == Engine.ShopChangeType.None)
                    {
                        // 冻结/无变化：不清UI，保留上回合标签
                        _purchaseDetector.Snapshot(shopIds.Count, shopIds);
                    }
                    else
                    {
                        ClearSuggestions();
                    }
                    _lastShopChange = DateTime.Now;
                    _lastShopIdStr = currentIdStr;
                    return;
                }
                _lastShopIdStr = currentIdStr;

                AfterShopChangeDetection:

                // ── 步骤2: 购买/刷新去抖 — 购买100ms(卡牌动画+位置稳定), 其他50ms ──
                int debounceMs = _redrawPending ? 100 : 50;
                if ((DateTime.Now - _lastShopChange).TotalMilliseconds < debounceMs) return;

                // 购买标签逐帧跟随: 在 stateHash 跳过之前同步, 让标签随剩余卡左移平滑跟随(纯位置漂移)
                try
                {
                    if (_cachedState != null)
                    {
                        _renderer.SyncShopTagPositions(_cachedState.ShopMinions, _shopLayoutDecision);
                    }
                }
                catch (Exception ex) { Log("SyncShopTagPositions error: " + ex.Message); }

                // ── 步骤3: 状态哈希 — 仅在debounce过期后检查 ──
                var stateHash = ComputeStateHash();
                bool forceEval = _eventEvalRequested;
                _eventEvalRequested = false;
                if (!forceEval && stateHash == _lastStateHash)
                {
                    _updateSkippedNoChange++;
                    return;
                }
                _lastStateHash = stateHash;

                _updateEvaluated++;
                _updateSkippedNoChange = 0;
                // 购买/刷新重绘: 启用新元素淡入
                if (_redrawPending) _renderer.FadeInMode = true;
                EvaluateAndRender();
                _redrawPending = false;
                _renderer.FadeInMode = false;
            }
            catch (Exception ex)
            {
                Log("OnUpdate error: " + ex.Message);
                // 兜底: 评估崩溃时确保缓存可用, 至少状态栏能渲染
                if (_cachedPlan != null && _cachedState != null)
                    _persistentDirty = true;
            }
        }

        private void RequestShopUiRefresh(DateTime now)
        {
            _lastRenderedPlanHash = null; // 强制下一帧重绘

            if ((now - _lastShopUiClearAt).TotalMilliseconds < ShopUiClearCooldownMs)
                return;

            // 商店实体过渡期 ID 会连续抖动，限制建议元素清理频率。
            // 独立面板由各自状态机管理；状态条必须保留稳定根元素，避免中断解锁拖动。
            _renderer.ClearNonPanel();
            _renderVersion++; // 废止该帧前的异步dispatch
            _lastShopUiClearAt = now;
        }

        // ── Security self-check ──

        private bool SecuritySelfCheck()
        {
            try
            {
                var forbidden = new[] {
                    "ReadProcessMemory", "WriteProcessMemory", "CreateRemoteThread",
                    "SendInput", "mouse_event", "keybd_event",
                    "SetWindowsHookEx", "NtReadVirtualMemory"
                };
                var asm = Assembly.GetExecutingAssembly();
                var found = new List<string>();
                foreach (var t in asm.GetTypes())
                {
                    try
                    {
                        foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
                        {
                            foreach (var kw in forbidden)
                            {
                                if (m.Name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
                                    found.Add(m.Name);
                            }
                        }
                    }
                    catch { }
                }
                if (found.Count > 0)
                {
                    Log("SECURITY VIOLATION: " + string.Join(", ", found));
                    return false;
                }
                Log("Security check passed");
                return true;
            }
            catch (Exception ex) { Log("Security check error: " + ex); return false; }
        }

        // ── Game events ──

        private void OnGameStart()
        {
            _inBattlegrounds = false;
            _lastStateHash = null; _lastRenderedHash = null;
            _trinketShadowCaptureSession.Reset();
                _suggestionsActive = false;
                _lastLiveSafetySuppressRefresh = false;
                try
                {
                EnsureBattlegroundsInitialized();
            }
            catch { }
        }

        private void EnsureBattlegroundsInitialized()
        {
            if (_inBattlegrounds) return;
            _inBattlegrounds = false;
            _lastStateHash = null; _lastRenderedHash = null;
            _trinketShadowCaptureSession.Reset();
            _suggestionsActive = false;
            try
            {
                if (Core.Game == null || !Core.Game.IsBattlegroundsMatch) return;
                _inBattlegrounds = true;
                Log("BG game started");
                // 诊断BobsBuddy可用性 (仅在新局开始时检查)
                _combatPredictCheckCount = 0;
                _combatPredictAvailable = Engine.BobsBuddyBridge.Available;
                Log(string.Format("BobsBuddy availability: {0}", _combatPredictAvailable ? "AVAILABLE" : "NOT FOUND"));
                _cachedPlan = null; _cachedState = null;  // 清空上局缓存
                _persistentDirty = true;
                _engine?.ResetStrategicMemory();
                _extractor.Reset();  // 清除上局幽灵实体累积
                _visualizer.Reset();  // 重置流派锁+手牌标记缓存
                _purchaseDetector.Reset();  // 重置购买检测
                _shopLayoutSlotPolicy.Reset(1);
                _shopLayoutDecision = new Engine.ShopLayoutDecision(1, true);
                _redrawPending = false;
                _lastShopUiClearAt = DateTime.MinValue;
                _poolFiltered = false;  // 新局需要重新过滤卡池
                _buddyPoolEnabled = false;
                // 重置发现/饰品/英雄提示跨局状态
                _discoverSource = "";
                _prevGoldenCount = 0;
                _prevGoldenHandCount = 0;
                _prevHpExhausted = false;
                _prevExhaustedHeroPowerEntityIds.Clear();
                _triggeredScheduledDiscoverOccurrenceIds.Clear();
                _boardLeaveDiscoverPendingUntil = DateTime.MinValue;
                _handDiscoverPendingUntil = DateTime.MinValue;
                _prevBoardCountForDiscover = -1;
                _prevBoardHadDiscoverSource = false;
                _discoverPanelActive = false;
                _trinketPanelActive = false;
                _discoverBatchFromLog = null;
                _timewarpPurchaseBatchFromLog = null;
                _timewarpPurchaseClearRequested = false;
                _darkGiftBatchFromLog = null;
                _darkGiftUsedCount = 0;
                _darkGiftUsedTurn = -1;
                _powerLogPhase = "shop";
                _powerEntityZones.Clear();
                _powerEntityCards.Clear();
                _powerEntityPlayers.Clear();
                _passiveChoiceActive = false;
                _darkGiftTipCoordinator.Reset();
                _renderer.ClearDarkGiftCandidates();
                _claimedUpgradePrizeChoiceIds.Clear();
                _trinketChoiceLifecycle.Reset();
                // 跨局重置面板状态机回 Idle(防止上局 Active/Fading 残留)
                _trinketPanelState = new Engine.PanelState(Engine.PanelPhase.Idle, 1, 500);
                _discoverPanelState = new Engine.PanelState(Engine.PanelPhase.Idle, 1, 1000);
                _trinketTargetState.Reset();
                _discoverTargetState.Reset();
                _lastHeroHintTurn = 0;

                // 强制刷新布局计算器 (游戏窗口此时已存在)
                try
                {
                    _renderer.RefreshLayout();
                    var client = Engine.SafeNativeMethods.GetHearthstoneClientRect();
                    if (client.HasValue)
                        Log(string.Format("CoordSys: clientRect refreshed ({0},{1})-({2},{3}) size={4}x{5}",
                            client.Value.Left, client.Value.Top,
                            client.Value.Right, client.Value.Bottom,
                            client.Value.Width, client.Value.Height));
                    else
                        Log("CoordSys: clientRect still unavailable (game window not found)");
                }
                catch (Exception ex) { Log("CoordSys refresh error: " + ex.Message); }

                // 启动Power.log监控；这里只读检查log.config，不自动修改。
                if (_powerLogWatcher == null) _powerLogWatcher = new Engine.PowerLogWatcher();
                SubscribePowerLogWatcherEvents();
                _powerLogWatcher.StartWatching();

                _currentGameRecord = new GameRecord();
                if (_decisionEventRecordingOptedIn
                    && _powerLogWatcher.ConfigStatus == LogConfigStatus.OK
                    && (_powerLogWatcher.IsWatching || _powerLogWatcher.IsStarting))
                {
                    try
                    {
                        _decisionEventRecorder = DecisionEventRecorder.CreateForLocalReplay(
                            _decisionPolicies.ModelId, _decisionPolicies.ModelPayloadSha256);
                        var gameId = _currentGameRecord.Timestamp.ToUniversalTime().ToString("o")
                            + ":" + Guid.NewGuid().ToString("N");
                        _decisionLifecycle = new DecisionLifecycleTracker(gameId,
                            _decisionEventRecorder);
                        _decisionOrchestrator = new DecisionOrchestrator(
                            _decisionPolicies.Legacy, _decisionPolicies.Unified,
                            _decisionEventRecorder, DecisionRuntimeMode.Shadow);
                    }
                    catch (Exception ex)
                    {
                        Log("Decision recording unavailable: " + ex.Message);
                        ResetDecisionRecording();
                    }
                }
                else
                {
                    ResetDecisionRecording();
                }
                _prevState = null;  // 清空上一帧状态

                // Power.log 不可用时显示警告
                if (!_powerLogWatcher.IsWatching
                    && !string.IsNullOrEmpty(_powerLogWatcher.ConfigMessage))
                {
                    Log("PowerLog warning: " + _powerLogWatcher.ConfigMessage);
                    // 延迟显示，等HDT覆盖层就绪
                    var warnTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                    warnTimer.Tick += (s2, e2) =>
                    {
                        try
                        {
                            warnTimer.Stop();
                            _renderer.ShowSuggestionBadge("日志",
                                _powerLogWatcher.ConfigStatus == LogConfigStatus.Error ? "#FF5252" : "#FF9800",
                                _powerLogWatcher.ConfigMessage);
                        }
                        catch { }
                    };
                    warnTimer.Start();
                }

                InitPoolTracker();

                // 测试：立即在 HDT 覆盖层上画一个显眼的徽标
                var app = Application.Current;
                if (app != null)
                {
                    app.Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
                    {
                        try
                        {
                            _renderer.ShowSuggestionBadge("Bob", "#FFD700", "Bob教练已启动");
                            Log("TEST BADGE: rendered on HDT canvas");
                        }
                        catch (Exception ex) { Log("TEST BADGE error: " + ex); }
                    }));
                }
            }
            catch { }
        }

        private void InitPoolTracker()
        {
            try
            {
                if (_cardMeta.Count == 0)
                {
                    var snapshot = CardPoolSampler.GetCardMetaSnapshot();
                    if (snapshot.Count == 0) return;
                    _cardMeta = snapshot.ToDictionary(
                        kv => kv.Key,
                        kv => (kv.Value.Tier, kv.Value.TribeCn, kv.Value.IsSpell));
                }
                ReinitPoolTracker();
                Log(string.Format("PoolTracker initialized: {0} cards", _cardMeta.Count));
            }
            catch (Exception ex) { Log("InitPoolTracker error: " + ex.Message); }
        }

        private void ReinitPoolTracker(EffectiveGameRules rules = null)
        {
            try
            {
                var effectiveRules = rules ?? EffectiveGameRules.Default;
                var cardTiers = new Dictionary<string, int>();
                foreach (var kv in _cardMeta)
                    if (kv.Value.tier >= 1 && kv.Value.tier <= 6)
                        cardTiers[kv.Key] = kv.Value.tier;
                _poolTracker.Initialize(cardTiers, effectiveRules);
                _buddyPoolEnabled = CardPoolSampler.IsBuddyRegistryCompatible()
                    && BuddyCardPoolEvaluator.IsEnabled(effectiveRules, 1);
            }
            catch { }
        }

        private void ApplyPoolFilter(HashSet<string> availableTribes)
        {
            if (_poolFiltered || availableTribes == null || availableTribes.Count < 3) return;
            _poolTracker.FilterByAvailableTribes(availableTribes, _cardMeta);
            _poolFiltered = true;
            Log(string.Format("Pool filtered by tribes: {0} available", string.Join(",", availableTribes)));
        }

        private DecisionMode LoadDecisionMode()
        {
            try
            {
                var path = BobCoachDataPaths.GetPath("decision_mode.txt");
                if (System.IO.File.Exists(path))
                {
                    var content = System.IO.File.ReadAllText(path).Trim().ToLower();
                    if (content == "personal") return DecisionMode.Personal;
                }
            }
            catch { }
            return DecisionMode.Meta;
        }

        private void SaveDecisionMode(DecisionMode mode)
        {
            try
            {
                var dir = BobCoachDataPaths.Root;
                System.IO.Directory.CreateDirectory(dir);
                var path = System.IO.Path.Combine(dir, "decision_mode.txt");
                System.IO.File.WriteAllText(path, mode == DecisionMode.Personal ? "personal" : "meta");
            }
            catch { }
        }

        private void OnGameEnd()
        {
            _inBattlegrounds = false;
            ClearSuggestions();

            // 提取最终排名 (从Power.log事件或HDT API)
            int placement = 0;
            if (_currentGameRecord != null)
            {
                placement = ExtractFinalPlacement();
                if (placement > 0 && placement <= 8)
                    _currentGameRecord.FinalRank = placement;
            }

            try
            {
                if (placement > 0 && placement <= 8 && _decisionLifecycle != null)
                    _decisionLifecycle.AttachCombatOutcome(
                        new CombatOutcome("placement:" + placement), DateTime.UtcNow.ToString("o"));
            }
            catch (Exception ex)
            {
                Log("Decision outcome recording error: " + ex.Message);
            }
            finally
            {
                ResetDecisionRecording();
            }

            // 全局事件回放收尾：结算事件必须在 watcher 停止和快照导出前落盘。
            if (_globalReplayRecorder != null)
            {
                try
                {
                    _globalReplayRecorder.RecordGameEnd(
                        _currentGameRecord != null ? _currentGameRecord.FinalRank : placement,
                        _currentGameRecord != null ? _currentGameRecord.TurnCount : 0,
                        _currentGameRecord != null ? _currentGameRecord.TripleCount : 0,
                        _currentGameRecord != null ? _currentGameRecord.BoardPowerPeak : 0);
                }
                catch (Exception ex) { Log("Global replay end error: " + ex.Message); }
                try { _globalReplayRecorder.Dispose(); } catch { }
                _globalReplayRecorder = null;
            }

            // 记录本局数据
            if (_currentGameRecord != null && _profileEngine != null)
            {
                try
                {
                    _profileEngine.RecordGame(_currentGameRecord);
                    Log(string.Format("Game recorded: hero={0} tribes={1} turns={2}",
                        _currentGameRecord.HeroId,
                        _currentGameRecord.TribeCounts.Count,
                        _currentGameRecord.Turns.Count));
                }
                catch (Exception ex) { Log("RecordGame error: " + ex); }
            }

            // Power.log 回放导出 (如果watcher活跃) + 始终导出turn快照
            string powerLogPath = null;
            if (_powerLogWatcher != null)
            {
                bool hadActiveWatcher = _powerLogWatcher.IsWatching;
                UnsubscribePowerLogWatcherEvents();
                _powerLogWatcher.StopWatching();
                if (hadActiveWatcher)
                    powerLogPath = _powerLogWatcher.ExportReplay(
                        _currentGameRecord?.HeroId ?? "unknown",
                        _currentGameRecord?.HeroName ?? "",
                        _currentGameRecord?.FinalRank ?? 0,
                        _currentGameRecord?.TripleCount ?? 0,
                        _currentGameRecord?.BoardPowerPeak ?? 0,
                        _currentGameRecord?.Turns ?? new List<TurnSnapshot>());
            }

            // 始终导出turn快照 (即使Power.log不可用)
            if (_currentGameRecord != null && _currentGameRecord.Turns.Count > 0)
            {
                try
                {
                    var bobDir = BobCoachDataPaths.Root;
                    var replayDir = System.IO.Path.Combine(bobDir, "replays");
                    System.IO.Directory.CreateDirectory(replayDir);
                    var snapPath = System.IO.Path.Combine(replayDir,
                        string.Format("snapshot_{0:yyyyMMdd_HHmmss}_{1}.json",
                        DateTime.Now,
                        (_currentGameRecord.HeroId ?? "unknown").Replace("TB_BaconShop_HERO_", "")));
                    Engine.FastJsonWriter.Write(snapPath, new
                    {
                        hero = _currentGameRecord.HeroId,
                        heroName = _currentGameRecord.HeroName,
                        finalRank = _currentGameRecord.FinalRank,
                        turnCount = _currentGameRecord.TurnCount,
                        tripleCount = _currentGameRecord.TripleCount,
                        boardPowerPeak = _currentGameRecord.BoardPowerPeak,
                        timestamp = _currentGameRecord.Timestamp,
                        turns = _currentGameRecord.Turns.Select(t => new
                        {
                            turn = t.Turn, gold = t.Gold, maxGold = t.MaxGold,
                            tier = t.Tier, health = t.Health, armor = t.Armor,
                            boardPower = t.BoardPower, oppPower = t.OpponentPower,
                            board = t.Board.Select(m => new
                            {
                                id = m.CardId, name = m.Name, atk = m.Attack, hp = m.Health,
                                tier = m.Tier, pos = m.Position, golden = m.Golden,
                                taunt = m.Taunt, ds = m.DivineShield, reborn = m.Reborn,
                                poison = m.Poisonous, venom = m.Venomous, windfury = m.Windfury,
                                tribe = m.Tribe, spell = m.IsSpell, cost = m.Cost,
                            }).ToList(),
                            shop = t.Shop.Select(m => new
                            {
                                id = m.CardId, name = m.Name, atk = m.Attack, hp = m.Health,
                                tier = m.Tier, pos = m.Position, golden = m.Golden,
                                taunt = m.Taunt, ds = m.DivineShield, reborn = m.Reborn,
                                poison = m.Poisonous, venom = m.Venomous, windfury = m.Windfury,
                                tribe = m.Tribe, spell = m.IsSpell, cost = m.Cost,
                            }).ToList(),
                            hand = t.Hand.Select(m => new
                            {
                                id = m.CardId, name = m.Name, atk = m.Attack, hp = m.Health,
                                tier = m.Tier, pos = m.Position, golden = m.Golden,
                                taunt = m.Taunt, ds = m.DivineShield, reborn = m.Reborn,
                                poison = m.Poisonous, venom = m.Venomous, windfury = m.Windfury,
                                tribe = m.Tribe, spell = m.IsSpell, cost = m.Cost,
                            }).ToList(),
                            opponents = t.Opponents.Select(o => new
                            {
                                heroId = o.HeroId, heroName = o.HeroName,
                                hp = o.Health, tier = o.TavernTier, alive = o.Alive,
                                board = o.Board.Select(m => new
                                {
                                    id = m.CardId, name = m.Name, atk = m.Attack, hp = m.Health,
                                    tier = m.Tier, pos = m.Position, golden = m.Golden,
                                    taunt = m.Taunt, ds = m.DivineShield, reborn = m.Reborn,
                                    poison = m.Poisonous, venom = m.Venomous, windfury = m.Windfury,
                                    megaWf = m.MegaWindfury, stealth = m.Stealth,
                                    cleave = m.Cleave, overkill = m.Overkill,
                                    tribe = m.Tribe, spell = m.IsSpell, cost = m.Cost,
                                }).ToList(),
                            }).ToList(),
                            algoRec = t.AlgoRecommendation, algoRule = t.AlgoRule,
                            levelSug = t.LevelUpSuggestion, compDir = t.CompDirection,
                            timeline = t.PlayerTimeline,
                            featureVector = t.FeatureVector ?? "",
                            recommendedAction = t.RecommendedActionJson ?? "",
                            actionScores = t.ActionScoresJson ?? "",
                            shadowRecommendedAction = t.ShadowRecommendedActionJson ?? "",
                            shadowActionScores = t.ShadowActionScoresJson ?? "",
                            frozenShop = t.FrozenShop,
                        }).ToList()
                    });
                    Log("Turn snapshot saved: " + snapPath + " (" + _currentGameRecord.Turns.Count + " turns)");
                }
                catch (Exception ex2) { Log("Snapshot save error: " + ex2.Message); }
            }

            Log("BG game ended");
        }

        private void ResetDecisionRecording()
        {
            _decisionLifecycle = null;
            _decisionEventRecorder = null;
            if (_decisionPolicies != null)
                _decisionOrchestrator = new DecisionOrchestrator(
                    _decisionPolicies.Legacy, _decisionPolicies.Unified,
                    NoOpShadowComparisonSink.Instance, DecisionRuntimeMode.Shadow);
        }

        private IList<string> InferObservedActionKeys(GameState previous, GameState current)
        {
            if (previous == null || current == null || _decisionPolicies == null)
                return new List<string>();

            return ObservedActionInference.Infer(previous, current,
                _decisionPolicies.Candidates.Generate(previous));
        }

        /// <summary>从Power.log事件或HDT API提取最终排名(1-8)</summary>
        private int ExtractFinalPlacement()
        {
            // 方法1: 从Power.log事件中搜索PLAYER_LEADERBOARD_PLACE标签
            if (_powerLogWatcher != null && _powerLogWatcher.EventCount > 0)
            {
                try
                {
                    var events = _powerLogWatcher.Events;
                    for (int i = events.Count - 1; i >= 0; i--)
                    {
                        var e = events[i];
                        if (e.Tag == "PLAYER_LEADERBOARD_PLACE"
                            && e.NumericValue >= 1 && e.NumericValue <= 8
                            && (string.IsNullOrEmpty(e.CardId)
                                || Engine.BattlegroundsHeroIdentity.NormalizeHeroCardId(e.CardId)
                                    == _currentGameRecord.HeroId))
                            return e.NumericValue;
                    }
                }
                catch { }
            }

            // 方法2: 从HDT API获取
            try
            {
                if (Core.Game != null)
                {
                    int localCtrl = -1;
                    try
                    {
                        localCtrl = Core.Game.PlayerEntity != null
                            ? Core.Game.PlayerEntity.GetTag(
                                HearthDb.Enums.GameTag.CONTROLLER)
                            : -1;
                    }
                    catch { }
                    foreach (var entity in Core.Game.Entities.Values.ToList())
                    {
                        if (entity == null) continue;
                        if (localCtrl > 0
                            && entity.GetTag(HearthDb.Enums.GameTag.CONTROLLER) != localCtrl)
                            continue;
                        string normalizedHero = Engine.BattlegroundsHeroIdentity
                            .NormalizeHeroCardId(entity.CardId ?? "");
                        if (!entity.IsHero
                            && normalizedHero != _currentGameRecord.HeroId)
                            continue;
                        if (entity.HasTag(HearthDb.Enums.GameTag.PLAYER_LEADERBOARD_PLACE))
                        {
                            int place = entity.GetTag(HearthDb.Enums.GameTag.PLAYER_LEADERBOARD_PLACE);
                            if (place >= 1 && place <= 8) return place;
                        }
                    }
                }
            }
            catch { }

            return 0;
        }

        private void OnTurnStart(ActivePlayer player)
        {
            if (!_inBattlegrounds)
            {
                try
                {
                    EnsureBattlegroundsInitialized();
                }
                catch { }
            }
            if (_inBattlegrounds && _engineReady)
            {
                _shopLayoutSlotPolicy.BeginRefresh();
                _lastStateHash = null; _lastRenderedHash = null;
                _updateSkippedNoChange = 0;
                _updateSkippedCombat = 0;
            }
        }

        private void OnInMenu()
        {
            if (_inBattlegrounds)
            {
                _inBattlegrounds = false;
                ClearSuggestions();
                Log("Returned to menu");
            }
        }

        /// <summary>卡牌获取事件 → 立即清除旧标签, 下一帧Extract重绘正确位置</summary>
        // ── Safe call wrapper: 防止单个引擎方法异常崩掉整个 OnUpdate ──
        private T SafeCall<T>(Func<T> fn, string name)
        {
            try { return fn(); }
            catch (Exception ex)
            {
                // 诊断: 打印完整stack trace前5行定位crash精确位置
                var trace = ex.StackTrace ?? "";
                var topFrames = string.Join(" <- ", trace.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).Take(5));
                Log(string.Format("Engine.{0} error: {1} [trace: {2}]", name, ex.Message, topFrames));
                return default;
            }
        }

        private DecisionResult EvaluateRankingDecision(GameState state)
        {
            if (state != null)
            {
                state.LastUsedTavernSpellCardId = _lastUsedTavernSpellCardId ?? "";
                state.LastUsedTavernSpellName = _lastUsedTavernSpellName ?? "";
            }
            DecisionResult rankingDecision;
            if (_decisionLifecycle != null && _decisionEventRecorder != null)
            {
                try
                {
                    var observedActionKeys = InferObservedActionKeys(_prevState, state);
                    _decisionLifecycle.AttachObservedActions(observedActionKeys,
                        DateTime.UtcNow.ToString("o"));
                    var candidates = _decisionPolicies.Candidates.Generate(state);
                    var context = _decisionLifecycle.BeginOrReuse(state, candidates);
                    rankingDecision = SafeCall(
                        () => _decisionOrchestrator.Decide(state, context.DecisionId), "GetBestAction")
                        ?? UnavailableDecisionPolicy.For(FallbackReason.NoSafeAction).Decide(state);
                    _decisionEventRecorder.AttachRecommendation(context.DecisionId,
                        rankingDecision, DateTime.UtcNow.ToString("o"));
                    rankingDecision = TryResolveRankedFallback(state, rankingDecision);
                }
                catch (Exception ex)
                {
                    Log("Decision recording error: " + ex.Message);
                    ResetDecisionRecording();
                    rankingDecision = SafeCall(
                        () => _decisionOrchestrator.Decide(state), "GetBestAction")
                        ?? UnavailableDecisionPolicy.For(FallbackReason.NoSafeAction).Decide(state);
                }
            }
            else
            {
                if (_decisionOrchestrator == null)
                    _decisionOrchestrator = new DecisionOrchestrator(
                        _decisionPolicies.Legacy, _decisionPolicies.Unified,
                        NoOpShadowComparisonSink.Instance, DecisionRuntimeMode.Shadow);
                    rankingDecision = SafeCall(
                        () => _decisionOrchestrator.Decide(state), "GetBestAction")
                        ?? UnavailableDecisionPolicy.For(FallbackReason.NoSafeAction).Decide(state);
                    rankingDecision = TryResolveRankedFallback(state, rankingDecision);
            }
            return rankingDecision;
        }

        private DecisionResult TryResolveRankedFallback(
            GameState state, DecisionResult current)
        {
            if (current == null || current.SelectedAction != null
                || current.FallbackReason == FallbackReason.None)
                return current;
            try
            {
                var ranked = _engine.GetRankedCandidates(state, 1);
                if (ranked == null || ranked.Count == 0 || ranked[0] == null
                    || ranked[0].Action == null)
                    return current;
                return new DecisionResult(
                    "ranked-fallback",
                    ranked[0].Action,
                    ranked,
                    ranked[0].Score,
                    PlannerStatus.NotRequested,
                    FallbackReason.None,
                    "ranked fallback");
            }
            catch
            {
                return current;
            }
        }

        // ── Decision evaluation & render ──

        private void EnsureCombatWatchTimer()
        {
            if (_combatWatchTimer != null) return;

            _combatWatchTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(0.2) };
            _combatWatchTimer.Tick += (s3, e3) =>
            {
                try
                {
                    if (_timewarpPurchaseClearRequested)
                    {
                        _timewarpPurchaseClearRequested = false;
                        _renderer.ClearTimewarpPurchaseHint();
                    }

                    _darkGiftTipCoordinator.RefreshPressable(DateTime.Now);
                    var darkGiftStatus = _darkGiftTipCoordinator.GetStatus(DateTime.Now, BuildDarkGiftTipContext());
                    if (darkGiftStatus == null)
                    {
                        _renderer.ClearDarkGiftTip();
                        _renderer.ClearDarkGiftCandidates();
                    }

                    bool nowInCombat = Core.Game.IsBattlegroundsCombatPhase;
                    if (darkGiftStatus != null)
                    {
                        _eventEvalRequested = false;
                        _renderer.RefreshCanvasSize();
                        _renderer.ShowDarkGiftTip(darkGiftStatus);
                        if (_darkGiftBatchFromLog != null)
                            _renderer.ShowDarkGiftCandidates(
                                _darkGiftBatchFromLog.Candidates, true,
                                ScoreDarkGiftCandidates());
                    }
                    else if (nowInCombat)
                    {
                        if (_timewarpPurchaseBatchFromLog != null)
                        {
                            // 2026-08-19 实测修复(C1): 战斗阶段不渲染时空扭曲选择——
                            // 游戏内面板在招募阶段出现, 战斗阶段显示会被局内图层遮挡且
                            // 时序不同步(米罗克回合4反馈); 批次保留, 招募阶段路径渲染
                            _eventEvalRequested = false;
                            _renderer.ClearTimewarpPurchaseHint();
                        }
                        else if (CombatChoiceRenderPolicy.CanRenderDiscoverDuringCombat(
                            _discoverBatchFromLog, _discoverPanelActive))
                        {
                            _eventEvalRequested = false;
                            RenderAuthoritativeDiscoverDuringCombat();
                        }
                        else
                        {
                            _renderer.Clear();
                            _renderVersion++;
                            _suggestionsActive = false;
                            _persistentDirty = true;
                            _lastDiscoverShowState = false;
                        }
                    }
                    else if (_eventEvalRequested)
                    {
                        // Power.log运行在后台线程；选择界面静止时HDT可能数秒不调用OnUpdate。
                        // 由现有UI线程DispatcherTimer合并消费请求，避免后台线程直接访问HDT/WPF。
                        _eventEvalRequested = false;
                        EvaluateAndRender();
                    }
                }
                catch { }
            };
            _combatWatchTimer.Start();
        }

        private void RenderDarkGiftTipStatus()
        {
            var status = _darkGiftTipCoordinator.GetStatus(DateTime.Now, BuildDarkGiftTipContext());
            if (status == null)
            {
                _renderer.ClearDarkGiftTip();
                _renderer.ClearDarkGiftCandidates();
                return;
            }

            _renderer.RefreshCanvasSize();
            _renderer.ShowDarkGiftTip(status);
            if (_darkGiftBatchFromLog != null)
                _renderer.ShowDarkGiftCandidates(
                    _darkGiftBatchFromLog.Candidates, true,
                    ScoreDarkGiftCandidates());
        }

        private List<Engine.TrinketHint> ScoreDarkGiftCandidates()
        {
            var batch = _darkGiftBatchFromLog;
            if (batch == null || batch.Candidates == null || batch.Candidates.Count == 0)
                return new List<Engine.TrinketHint>();

            var state = _cachedState;
            string compDir = _cachedPlan?.Status?.CompDir ?? "";
            return Engine.DarkGiftCandidateAdvisor.Evaluate(
                batch.Candidates,
                state?.BoardMinions,
                compDir,
                Engine.CardNameService.GetName,
                ResolveCardTribes,
                Engine.CardNameService.GetTier,
                _engine != null ? _engine.IsResourceCard : (Func<string, bool>)null,
                _engine != null ? _engine.IsCoreComboCard : (Func<string, bool>)null);
        }

        private Engine.DarkGiftTipContext BuildDarkGiftTipContext()
        {
            var state = _cachedState;
            if (state == null)
            {
                return new Engine.DarkGiftTipContext
                {
                    StateAvailable = false,
                };
            }

            var liveScene = BuildLiveScene(state);
            string compDir = _cachedPlan?.Status?.CompDir ?? "";
            bool hasCoreSynergy = !string.IsNullOrEmpty(compDir)
                && state.BoardMinions != null
                && state.BoardMinions.Count >= 3;
            double offeredExpectedValue = hasCoreSynergy ? 0.65 : 0.5;
            int liveGold = GetLiveGold(state);
            bool inCombat = string.Equals(
                _powerLogPhase, "MAIN_COMBAT", StringComparison.Ordinal);
            try
            {
                if (Core.Game != null && Core.Game.IsBattlegroundsCombatPhase)
                    inCombat = true;
            }
            catch { }
            return new Engine.DarkGiftTipContext
            {
                StateAvailable = true,
                GameActive = state.GameActive,
                Phase = inCombat ? "combat" : (state.Phase ?? ""),
                Turn = state.Turn,
                TavernTier = state.TavernTier,
                Gold = liveGold,
                BoardMinionCount = state.BoardMinions?.Count ?? 0,
                ShopMinionCount = state.ShopMinions?.Count ?? 0,
                HandMinionCount = state.HandMinions?.Count ?? 0,
                Health = state.Health,
                BoardPower = liveScene.BoardPower,
                OpponentPower = liveScene.OpponentAveragePower,
                UsesRemaining = Math.Max(0, 3 - _darkGiftUsedCount),
                AlreadyUsedThisTurn = _darkGiftUsedTurn == state.Turn && _darkGiftUsedCount > 0,
                OfferedExpectedValue = offeredExpectedValue,
                HasCoreSynergy = hasCoreSynergy,
            };
        }

        private Engine.LiveSceneSnapshot BuildLiveScene(Engine.GameState state)
        {
            var rules = state.EffectiveRules ?? Engine.EffectiveGameRules.Default;
            int liveGold = state.LiveGold >= 0 ? state.LiveGold : state.Gold;
            int boardPower = (int)_fe.ComputeBoardPower(state.BoardMinions);
            int opponentPower = 0;
            int opponentCount = 0;
            if (state.Opponents != null)
            {
                foreach (var opponent in state.Opponents)
                {
                    if (opponent == null || !opponent.Alive) continue;
                    opponentPower += (int)_fe.ComputeBoardPower(opponent.BoardMinions);
                    opponentCount++;
                }
            }
            if (opponentCount > 0) opponentPower = opponentPower / opponentCount;

            int? upgradeCost = Engine.GameRuleEvaluator.GetUpgradeCost(state, rules);
            int refreshCost = Engine.GameRuleEvaluator.GetRefreshCost(
                state, state.HeroCardId, rules);
            int minBuyCost = Engine.GameRuleEvaluator.GetPurchaseCost(
                state, new Engine.MinionData { Tier = 1 }, state.HeroCardId, rules);
            bool canBuyAny = false;
            if (state.ShopMinions != null)
            {
                foreach (var shopMinion in state.ShopMinions)
                {
                    if (shopMinion == null) continue;
                    int cost = Engine.GameRuleEvaluator.GetPurchaseCost(
                        state, shopMinion, state.HeroCardId, rules);
                    if (liveGold >= cost)
                    {
                        canBuyAny = true;
                        break;
                    }
                }
            }

            var scene = Engine.LiveSceneAnalyzer.Analyze(
                state.Turn,
                state.TavernTier,
                state.Health,
                state.MaxHealth,
                state.BoardMinions == null ? 0 : state.BoardMinions.Count,
                state.HandMinions == null ? 0 : state.HandMinions.Count,
                state.ShopMinions == null ? 0 : state.ShopMinions.Count,
                boardPower,
                opponentPower,
                state.NormalRecruitAvailable,
                liveGold,
                state.MaxGold,
                upgradeCost,
                refreshCost,
                rules.ManualRefreshAllowed,
                canBuyAny);
            scene.RefreshCost = refreshCost;
            scene.MinBuyCost = minBuyCost;
            return scene;
        }

        private static string ResolveDisplayName(string cardName, string cardId)
        {
            return Engine.DisplayNameResolver.Resolve(
                cardId ?? "", cardName ?? "", Engine.CardNameService.GetName);
        }

        private List<string> ResolveCardTribes(string cardId)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(cardId)) return result;

            if (_cardMeta != null && _cardMeta.TryGetValue(cardId, out var meta))
            {
                foreach (var tribe in (meta.tribe ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!result.Contains(tribe)) result.Add(tribe);
                }
                if (result.Count > 0) return result;
            }

            try
            {
                HearthDb.Card c;
                if (HearthDb.Cards.All.TryGetValue(cardId, out c) && c != null)
                {
                    AddTribeCode(c.Race.ToString(), result);
                    AddTribeCode(c.SecondaryRace.ToString(), result);
                }
            }
            catch { }
            return result;
        }

        private static void AddTribeCode(string code, List<string> result)
        {
            if (string.IsNullOrEmpty(code) || result == null) return;
            string cn;
            if (_tribeCn.TryGetValue(code, out cn) && !result.Contains(cn))
                result.Add(cn);
        }

        private void EvaluateAndRender()
        {
            var darkGiftStatus = _darkGiftTipCoordinator.GetStatus(DateTime.Now, BuildDarkGiftTipContext());
            if (darkGiftStatus != null)
            {
                _renderer.RefreshCanvasSize();
                _renderer.ShowDarkGiftTip(darkGiftStatus);
                if (_darkGiftBatchFromLog != null)
                    _renderer.ShowDarkGiftCandidates(
                        _darkGiftBatchFromLog.Candidates, true,
                        ScoreDarkGiftCandidates());
                return;
            }
            _renderer.ClearDarkGiftTip();
            _renderer.ClearDarkGiftCandidates();

            // 战斗阶段不评估
            try { if (Core.Game.IsBattlegroundsCombatPhase) return; } catch { }

            if (_timewarpPurchaseBatchFromLog != null)
            {
                RenderTimewarpPurchaseHint();
                return;
            }

            // Power.log 发现批次以 CHOSEN/空批次为结束权威。战斗末尾触发的选择
            // 可能要等待数十秒才进入可渲染招募阶段，不能用墙钟超时提前丢弃。
            if (_trinketPanelActive && _trinketPanelActivatedAt != DateTime.MinValue
                && (DateTime.UtcNow - _trinketPanelActivatedAt).TotalSeconds > 5)
            {
                _trinketPanelActive = false;
                _trinketPanelActivatedAt = DateTime.MinValue;
            }

            // 发现扫描门控: 最近3秒内有真实触发(三连/技能/zone6变化) → 允许zone6扫描
            // _discoverPanelActive 仅由Power.log设置(非zone6), zone6扫描依赖 _lastDiscoverTriggerTime
            // 战斗结束冷却: 0.5秒内不扫描zone6, 防战斗衍生物被当成发现选项(缩短以覆盖战吼发现)
            bool combatCooldown = (DateTime.UtcNow - _combatEndedAt).TotalSeconds < 0.5;
            bool hasRecentTrigger = _lastDiscoverTriggerTime != DateTime.MinValue
                && (DateTime.UtcNow - _lastDiscoverTriggerTime).TotalSeconds < 5; // 3→5秒延长窗口
            bool initialDiscoverGate = (_discoverPanelActive || hasRecentTrigger) && !combatCooldown;
            _extractor.DiscoverTriggerActive = initialDiscoverGate;
            _extractor.PassiveChoiceActive = _passiveChoiceActive;
            _extractor.CurrentTrinketChoiceContext =
                _trinketChoiceLifecycle.Current == null
                    ? Engine.TrinketChoiceContext.Unknown
                    : _trinketChoiceLifecycle.Current.Context;

            var state = _extractor.Extract();
            if (state == null) return;
            if (_powerLogWatcher != null)
                _powerLogWatcher.LocalControllerId = state.PlayerId;
            bool canStartGlobalReplay = state.GameActive
                && state.Turn > 0
                && !string.IsNullOrEmpty(state.HeroPowerCardId);
            if (canStartGlobalReplay && _globalReplayRecorder == null)
            {
                try
                {
                    _globalReplayRecorder = new Engine.GlobalReplayRecorder();
                    string replayHeroId = Engine.HeroPowerRuleCatalog
                        .GetHeroCardIdForPower(state.HeroCardId, state.HeroPowerCardId);
                    _globalReplayRecorder.Start(
                        replayHeroId,
                        state.HeroName,
                        state.PlayerId.ToString(),
                        state.GameBuild,
                        state.SeasonId);
                }
                catch (Exception ex)
                {
                    Log("Global replay start error: " + ex.Message);
                    try { _globalReplayRecorder?.Dispose(); } catch { }
                    _globalReplayRecorder = null;
                }
            }
            var liveScene = BuildLiveScene(state);
            var liveSafety = LiveSafetyGate.Evaluate(
                liveScene, liveScene.RefreshCost, liveScene.MinBuyCost);

            // 只有实体刷新事件可以推进商店布局等级。购买、升本和冻结跨回合
            // 都保留上一次真实刷新的槽位宽度，避免旧商店被新等级错误扩宽。
            // 1.2.2 feedbackB: 升本后新商店可见但 ShopMinions.Count(不含法术位实体)常小于
            // ShopSlotCount(理论含法术位), ObserveShopTier 的 visible>=expected 门槛永不满足,
            // tier 停留旧等级 → 槽位按旧本渲染导致 UI 错位(T3 实测: tier2 商店 layoutTier=1)。
            // 修复: 已升本(当前tier>布局tier)且商店有可见实体时, 直接采纳当前tier(新商店已生成)。
            bool upgraded = state.TavernTier > _shopLayoutSlotPolicy.LastRefreshTier
                && state.ShopMinions.Count > 0
                && !state.FrozenShop;
            if (upgraded)
                _shopLayoutSlotPolicy.OnTavernTierChanged(state.TavernTier);
            if (_shopLayoutSlotPolicy.ObserveShopTier(
                state.TavernTier,
                state.ShopMinions.Count,
                state.ShopSlotCount))
                _renderer.SetCalcTier(_shopLayoutSlotPolicy.LastRefreshTier);

            bool shouldEnableBuddyPool = CardPoolSampler.IsBuddyRegistryCompatible()
                && BuddyCardPoolEvaluator.IsEnabled(state.EffectiveRules, state.Turn);
            if (shouldEnableBuddyPool != _buddyPoolEnabled)
            {
                _poolFiltered = false;
                ReinitPoolTracker(state.EffectiveRules);
            }

            bool openedDiscoverGate = !combatCooldown && UpdateDiscoverTriggerWindow(state);
            if (openedDiscoverGate && !initialDiscoverGate)
            {
                _extractor.DiscoverTriggerActive = true;
                state = _extractor.Extract();
                if (state == null) return;
            }

            var discoverSource = ApplyPowerLogDiscoverCandidates(state);
            MarkUpgradePrizeDiscoverObserved(state);
            var trinketSource = ApplyPowerLogTrinketCandidates(state);
            if (_trinketChoiceLifecycle.Current != null
                && _trinketChoiceLifecycle.Current.Context
                    == Engine.TrinketChoiceContext.MysteryCubeActivation
                && trinketSource != Engine.UiTargetSource.PowerLog)
            {
                trinketSource = Engine.UiTargetSource.None;
            }
            if (trinketSource == Engine.UiTargetSource.None
                && (_trinketChoiceLifecycle.Current == null
                    || _trinketChoiceLifecycle.Current.Context
                        != Engine.TrinketChoiceContext.MysteryCubeActivation))
                trinketSource = ApplyScheduledTrinketPlaceholder(state);
            AdvanceUiTargets(state, discoverSource, trinketSource);

            // ── 面板生命周期状态机每帧推进(根治闪烁/消不掉/不显示) ──
            // 必须每帧 Advance(此处在 planHash 门控之前), 否则 Fading→Expired 计时不走。
            // active=内容存在; 滞回/最大驻留/过期硬清除由状态机确定性兜底。
            // 饰品/发现: 必须先通过 UiTargetStateMachine 的 batch 稳定确认。
            bool smTrinketActive = state.TrinketOffer != null && state.TrinketOffer.Count > 0;
            // 发现 active 纯看 DiscoverOptions(zone6 扫描 + Power.log 注入都已写入此处)。
            // 不把 _discoverPanelActive flag 作电平输入: flag 卡死会让面板"消不掉"(选完无 CHOSEN 事件时)。
            bool smDiscoverActive = state.DiscoverOptions != null && state.DiscoverOptions.Count >= 2;
            Engine.PanelStateMachine.Advance(ref _trinketPanelState, smTrinketActive, state.Turn, enforceMaxActive: false);
            // 07072009: 发现面板改 enforceMaxActive:false(与饰品一致)。原 true 会~8s强制超时, 阮大师等慢选技能场景
            // 面板在玩家选完前就消失("闪现一下就没了")。现 SendChoices 完成信号可靠(chosen>0), 面板靠选完(候选清空→smDiscoverActive=false)关闭, 不再强制超时。
            // R234/R239(审查B): 发现面板超时策略按来源区分——Power.log 权威发现
            // (有 choiceId + 有来源卡)不强制超时(玩家可能慢选如阮大师);
            // 陈旧复发批次(同 id 但来源为空)不算权威 → 8秒超时兜底生效, 防面板卡死。
            bool discoverHasAuthority = _discoverBatchFromLog != null
                && _discoverBatchFromLog.ChoiceId >= 0
                && !string.IsNullOrEmpty(_discoverBatchFromLog.SourceCardId);
            Engine.PanelStateMachine.Advance(ref _discoverPanelState, smDiscoverActive,
                state.Turn, enforceMaxActive: !discoverHasAuthority);

            // ── BobsBuddy战斗预测 (异步, 仅在板面变化时更新) ──
            TryPredictCombat(state);

            // UI刷新防护: 防闪烁 — 无变化时200ms最小间隔, 有变化时80ms
            bool stateChanged = _lastRenderShopCount >= 0
                && (state.ShopMinions.Count != _lastRenderShopCount
                    || state.BoardMinions.Count != _lastRenderBoardCount
                    || state.Gold != _lastRenderGold
                    || (state.TrinketOffer?.Count ?? 0) != _lastRenderTrinketCount
                    || (state.DiscoverOptions?.Count ?? 0) != _lastRenderDiscoverCount);
            var msSinceLastRender = (DateTime.UtcNow - _lastRenderTime).TotalMilliseconds;
            // 饰品/发现活跃时跳过防抖 — 面板必须即时响应
            bool urgentPanel = _trinketPanelActive || _discoverPanelActive
                || (state.TrinketOffer?.Count ?? 0) > 0
                || (state.DiscoverOptions?.Count ?? 0) > 0;
            if (!urgentPanel && !stateChanged && state.Turn == _lastRenderTurn && msSinceLastRender < 200) return;
            if (!urgentPanel && msSinceLastRender < 50) return; // 最小间隔50ms, 防止同帧多次渲染
            _lastRenderTime = DateTime.UtcNow;
            _lastRenderShopCount = state.ShopMinions.Count;
            _lastRenderBoardCount = state.BoardMinions.Count;
            _lastRenderGold = state.Gold;
            _lastRenderTrinketCount = state.TrinketOffer?.Count ?? 0;
            _lastRenderDiscoverCount = state.DiscoverOptions?.Count ?? 0;

            // 卡池种族过滤: 当局种族首次检测到时应用
            if (!_poolFiltered && state.AvailableTribes != null && state.AvailableTribes.Count >= 3)
                ApplyPoolFilter(state.AvailableTribes);

            // ── 主动清除守卫: 状态变化时立即清理上一帧残留的UI元素 ──
            if (state.HeroPowerExhausted) { try { _renderer.ClearHeroGlow(); } catch { } }
            // 饰品清除权威已统一到 PanelState 状态机(消费端按 _trinketPanelState 滞回清除)。
            // 此处仅做 Power.log flag 的卡死清理(>2s offer 消失却没收到 CHOSEN 事件), 不再直接清面板,
            // 否则会绕过状态机 Fading 滞回, 导致"选完瞬间闪掉"。
            bool trinketOfferExists = state.TrinketOffer != null && state.TrinketOffer.Count > 0;
            if (!trinketOfferExists && _trinketPanelActive
                && _trinketPanelActivatedAt != DateTime.MinValue
                && (DateTime.UtcNow - _trinketPanelActivatedAt).TotalSeconds > 2)
            {
                _trinketPanelActive = false;
                _trinketPanelActivatedAt = DateTime.MinValue;
                Log("Trinket flag: force-reset (stuck flag, offer gone) — 清除交状态机");
            }
            // 发现面板清除权威已统一到 PanelState 状态机:
            //   - 显隐由 _discoverPanelState.IsVisible 决定(消费端)
            //   - 8s 最大驻留由 Advance(enforceMaxActive=true) 兜底, 不再在此独立清除
            //   - Power.log _discoverPanelActive 仅作补充触发源, 此处只做 8s flag 卡死清理(不清面板)
            bool hasValidDiscover = state.DiscoverOptions != null
                && state.DiscoverOptions.Count >= 2;
            if (hasValidDiscover)
            {
                _prevDiscoverCount = state.DiscoverOptions.Count;
            }
            else if (!_discoverPanelActive)
            {
                _prevDiscoverCount = 0;
            }

            // 空商店过渡态跳过渲染。招募结束→战斗开始时，
            // 商店实体先消失但IsBattlegroundsCombatPhase尚未返回true，
            // 此间隙内不渲染任何决策UI，避免短暂闪现过期推荐。
            // v2: 面板活跃时即使商店空也继续渲染(事件驱动,不依赖TTL)
            // 状态机滞回守卫: 面板 Fading 中(offer 刚消失但滞回未到)也不清, 否则绕过状态机导致闪掉
            if (state.ShopMinions.Count == 0 && state.Turn >= 1
                && state.HeroOptions.Count == 0 && state.TrinketOffer.Count == 0
                && state.DiscoverOptions.Count == 0
                && !_discoverPanelActive && !_trinketPanelActive
                && !_trinketPanelState.IsVisible && !_discoverPanelState.IsVisible)
            {
                ClearSuggestions();
                return;
            }

            // 跟踪升本回合
            if (state.TavernTier != _prevTavernTier)
            {
                _lastUpgradeTurn = state.Turn;
                _prevTavernTier = state.TavernTier;
                // 1.2.2 feedbackB: 升本后的布局tier同步统一在 EvaluateAndRender 商店可见处
                // (L1773 upgraded 分支)处理 — 升本瞬间旧商店可能还在, 立即扩宽会错位。
            }
            state.LastUpgradeTurn = _lastUpgradeTurn;

            // V1.2: 英雄技能提示（HeroPowerEngine 替代硬编码）
            // 修复4: 被动英雄不显示静态技能描述，仅主动技能有使用建议时才显示
            // R235: 条件限制技能规范化——未达到解锁酒馆等级时, 描述标注解锁条件,
            // 且不显示"使用技能"建议(牛头人酋长等级2解锁, T1 不应提示使用)。
            var heroStrategy = _heroPowerEngine.GetStrategyForPower(
                state.HeroCardId, state.HeroPowerCardId);
            bool heroPowerLockedByTier = heroStrategy.PowerType != HeroPowerType.Passive
                && state.TavernTier < heroStrategy.UnlockTier;
            string heroHint = heroStrategy.PowerType == HeroPowerType.Passive
                ? "" : HeroPowerDisplayTextResolver.Resolve(state.HeroPowerCardId);
            if (heroPowerLockedByTier && !string.IsNullOrEmpty(heroHint))
                heroHint = heroHint + " (酒馆等级" + heroStrategy.UnlockTier + "解锁)";
            // 动态技能使用建议: 根据当前金币+场面判断是否该用技能
            string heroUseSuggestion = "";
            HeroSkillFact heroSkill;
            bool heroSkillFound = _heroSkillCatalog.TryGet(
                state.HeroCardId, state.HeroPowerCardId, out heroSkill);
            if (!heroSkillFound)
            {
                var fallbackStrategy = _heroPowerEngine.GetStrategyForPower(
                    state.HeroCardId, state.HeroPowerCardId);
                if (fallbackStrategy != null)
                {
                    heroSkill = new HeroSkillFact
                    {
                        HeroCardId = fallbackStrategy.HeroCardId,
                        HeroPowerCardId = state.HeroPowerCardId ?? "",
                        PowerType = fallbackStrategy.PowerType.ToString(),
                        Cost = fallbackStrategy.PowerCost,
                        UnlockTurn = fallbackStrategy.UnlockTurn,
                        UnlockTier = fallbackStrategy.UnlockTier,
                        TextCn = fallbackStrategy.PowerHint,
                        UsePurpose = fallbackStrategy.UsePurpose.ToString(),
                        IsActive = fallbackStrategy.PowerType == HeroPowerType.Active
                            || fallbackStrategy.PowerType == HeroPowerType.Conditional,
                    };
                }
            }
            if (heroSkill != null)
            {
                int liveGoldForSkill = state.LiveGold >= 0 ? state.LiveGold : state.Gold;
                int heroPowerCost = state.HeroPowerCost >= 0
                    ? state.HeroPowerCost : heroSkill.Cost;
                int unlockTurn = state.HeroPowerUnlockTurn > 0
                    ? state.HeroPowerUnlockTurn : heroSkill.UnlockTurn;
                int unlockTier = state.HeroPowerUnlockTier > 0
                    ? state.HeroPowerUnlockTier : heroSkill.UnlockTier;
                bool hasValidTarget = state.BoardMinions.Count > 0
                    || state.ShopMinions.Count > 0
                    || heroSkill.UsePurpose == "Economy"
                    || heroSkill.UsePurpose == "Resource";
                bool heroShouldUse;
                int heroDecisionCost = heroPowerCost;
                string heroDecisionReason;
                var playbookProfile = _heroPlaybook != null
                    ? _heroPlaybook.GetProfileForPower(
                        state.HeroCardId, state.HeroPowerCardId)
                    : null;
                if (playbookProfile != null)
                {
                    var playbookDecision = HeroPlaybookDecisionService.Decide(
                        playbookProfile,
                        heroSkill.PowerType,
                        heroPowerCost,
                        liveGoldForSkill,
                        state.Turn,
                        state.TavernTier,
                        state.Health,
                        state.BoardMinions.Count,
                        state.ShopMinions.Count,
                        state.HeroPowerExhausted,
                        hasValidTarget);
                    heroShouldUse = playbookDecision.ShouldUse;
                    heroDecisionCost = playbookDecision.Cost;
                    heroDecisionReason = playbookDecision.Reason;
                }
                else if (state.HeroCardId == "BG32_HERO_001")
                {
                    // 2026-08-19 实测修复(B8/B9): 塞纳留斯(森林之王)——技能=花3金
                    // 铸币上限+1(长期收益), playbook 注册表数据缺失时通用决策不推荐;
                    // 兜底: 前6回合金币够就优先用技能(高手策略: 先技能后买/升本)
                    heroShouldUse = liveGoldForSkill >= heroPowerCost
                        && state.Turn <= 6 && !state.HeroPowerExhausted;
                    heroDecisionCost = heroPowerCost;
                    heroDecisionReason = heroShouldUse
                        ? "铸币上限+1(长期收益,前期优先)"
                        : (liveGoldForSkill < heroPowerCost ? "金币不足" : "已使用");
                }
                else
                {
                    var heroSkillDecision = HeroSkillDecisionService.Decide(
                        heroSkill.PowerType,
                        heroPowerCost,
                        unlockTurn,
                        unlockTier,
                        liveGoldForSkill,
                        state.Turn,
                        state.TavernTier,
                        state.BoardMinions.Count,
                        state.ShopMinions.Count,
                        heroSkill.UsePurpose,
                        hasValidTarget,
                        state.HeroPowerExhausted,
                        state.HeroCardId);
                    heroShouldUse = heroSkillDecision.ShouldUse;
                    heroDecisionCost = heroSkillDecision.Cost;
                    heroDecisionReason = heroSkillDecision.Reason;
                }
                if (state.HeroCardId != null
                    && state.HeroCardId.Contains("BG31_HERO_003"))
                {
                    if (string.IsNullOrEmpty(state.LastUsedTavernSpellCardId))
                    {
                        heroShouldUse = false;
                        heroDecisionReason = "需先使用酒馆法术";
                    }
                    else if (!string.IsNullOrEmpty(state.LastUsedTavernSpellName))
                    {
                        heroDecisionReason = "复制最近酒馆法术: " + state.LastUsedTavernSpellName;
                    }
                }
                // R235: 未解锁(酒馆等级不足)强制不推荐使用——防 playbook 等分支绕过。
                // 牛头人酋长(105)技能"等级2时解锁", T1(1本)不应有任何"使用技能"提示。
                if (heroShouldUse && heroPowerLockedByTier)
                {
                    heroShouldUse = false;
                    heroDecisionReason = "酒馆等级" + heroStrategy.UnlockTier + "解锁";
                }
                // P3 2026-08(用户实测: 回合4板面无怪仍提示用雷诺技能/无优质随从乱用点金):
                // 点金类(one_time_golden/变为金色)需要友方随从目标, 商店随从不是合法目标;
                // 且点金每局限一次, 板面没有优质随从(值得点金的目标)时不推荐——性价比门控。
                if (heroShouldUse && IsGoldenizeSkill(state, heroStrategy, heroSkill))
                {
                    int board = state.BoardMinions == null ? 0 : state.BoardMinions.Count;
                    if (board == 0)
                    {
                        heroShouldUse = false;
                        heroDecisionReason = "板面无随从, 点金无目标";
                    }
                    else if (!HasWorthyGoldenizeTarget(state))
                    {
                        heroShouldUse = false;
                        heroDecisionReason = "板面无优质随从, 点金性价比低";
                    }
                }
                Log(string.Format(
                    "HeroSkill: hero={0} power={1} found={2} type={3} unlock={4}/{5} gold={6} cost={7} reason={8} use={9}",
                    state.HeroCardId,
                    state.HeroPowerCardId,
                    heroSkillFound,
                    heroSkill.PowerType,
                    unlockTier,
                    unlockTurn,
                    liveGoldForSkill,
                    heroPowerCost,
                    heroDecisionReason,
                    heroShouldUse));
                if (heroShouldUse)
                {
                    heroUseSuggestion = string.Format(
                        "使用技能 · {0}费 · {1}", heroDecisionCost, heroDecisionReason);
                    // R250: Cookie tribe direction (same-tribe 3x to discover)
                    if (heroStrategy.SpecialRule == "COOKIE" && state != null)
                    {
                        string cookieTribe = PickCookieTargetTribe(state);
                        if (!string.IsNullOrEmpty(cookieTribe))
                            heroUseSuggestion += " · 集中投入" + cookieTribe + "随从(集齐3个发现)";
                    }
                }
            }

            // T1/T2 空商店：仅显示英雄提示，不跑决策引擎
            if (state.Turn <= 2 && state.ShopMinions.Count == 0 && state.Phase == "shop")
            {
                var waitingPlan = _visualizer.CreateWaitingPlan(state);
                _lastLiveSafetySuppressRefresh = liveSafety.SuppressRefresh;
                DispatchRender(state, heroHint, waitingPlan, heroUseSuggestion);
                return;
            }

            string anomalyTag = !string.IsNullOrEmpty(state.AnomalyId) ? " anomaly=" + state.AnomalyId : "";

            Log(string.Format("Decision: turn={0} gold={1} tier={2} board={3} shop={4} hand={5}{6}",
                state.Turn, state.Gold, state.TavernTier,
                state.BoardMinions.Count, state.ShopMinions.Count,
                state.HandMinions.Count, anomalyTag));

            // ── Phase0 评测数据: 招募决策点全状态转储 ──
            // 供 eval/ headless 重跑当前引擎测"引擎推荐 vs 用户实际选择"吻合率(改引擎后无需重收日志)。
            // 节流: 仅当 (turn,gold,tier,场/店/手计数) 变化时转储一次 → 每次买/卖/升本/刷新各一条。
            // 全量序列化 GameState → 反序列化即可完整重建, 免手写字段易漏。失败不影响插件。
            try
            {
                if (state.ShopMinions != null && state.ShopMinions.Count > 0)
                {
                    string dsig = state.Turn + ":" + state.Gold + ":" + state.TavernTier + ":"
                        + state.ShopMinions.Count + ":" + state.BoardMinions.Count + ":" + state.HandMinions.Count;
                    if (dsig != _lastEvalDumpSig)
                    {
                        _lastEvalDumpSig = dsig;
                        var evalJson = JsonConvert.SerializeObject(state, new JsonSerializerSettings
                        {
                            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                            NullValueHandling = NullValueHandling.Ignore,
                        });
                        EvalLog(evalJson); // 写独立 eval_dump.jsonl, 不污染人读调试日志
                    }
                }
            }
            catch (Exception exDump) { Log("EvalDump error: " + exDump.Message); }


            // Update game record
            if (_currentGameRecord != null)
            {
                if (string.IsNullOrEmpty(_currentGameRecord.HeroId))
                {
                    _currentGameRecord.HeroId = state.HeroCardId;
                    _currentGameRecord.HeroName = state.HeroName;
                }
                _currentGameRecord.TurnCount = state.Turn;

                if (state.TavernTier >= 4 && _currentGameRecord.LevelUpTurn4 <= 1)
                    _currentGameRecord.LevelUpTurn4 = state.Turn;
                if (state.TavernTier >= 5 && _currentGameRecord.LevelUpTurn5 <= 1)
                    _currentGameRecord.LevelUpTurn5 = state.Turn;
                if (state.TavernTier >= 6 && _currentGameRecord.LevelUpTurn6 <= 1)
                    _currentGameRecord.LevelUpTurn6 = state.Turn;

                foreach (var m in state.BoardMinions)
                {
                    if (!string.IsNullOrEmpty(m.Tribe))
                    {
                        int c;
                        _currentGameRecord.TribeCounts.TryGetValue(m.Tribe, out c);
                        _currentGameRecord.TribeCounts[m.Tribe] = c + 1;
                    }
                }

                var boardPower = _fe.ComputeBoardPower(state.BoardMinions);
                if (boardPower > _currentGameRecord.BoardPowerPeak)
                    _currentGameRecord.BoardPowerPeak = boardPower;
            }

            // Update pool tracker from state change
            UpdatePoolFromStateChange(state);
            _poolTracker.OnNewTurn(state.Turn, state.Opponents.FindAll(o => o.Alive).Count);

            var rankingDecision = EvaluateRankingDecision(state);
            var bestAction = rankingDecision.SelectedAction;
            if (bestAction != null
                && bestAction.Type == Engine.ActionType.SellMinion
                && !_engine.IsSellActionValid(state, bestAction))
            {
                bestAction = null;
            }
            var t0 = DateTime.UtcNow;
            var levelResult = SafeCall(() => _engine.GetLevelUpSuggestion(state), "GetLevelUpSuggestion");
            int? liveUpgradeCost = GameRuleEvaluator.GetUpgradeCost(
                state, state.EffectiveRules ?? EffectiveGameRules.Default);
            if (levelResult.Suggestion == "LEVEL_UP"
                && liveUpgradeCost.HasValue
                && GetLiveGold(state) >= liveUpgradeCost.Value
                && !liveSafety.SuppressUpgrade
                && (bestAction == null || bestAction.Type != Engine.ActionType.Upgrade))
            {
                bool keepBuyThenUpgrade = bestAction != null
                    && (bestAction.Type == Engine.ActionType.BuyMinion
                        || bestAction.Type == Engine.ActionType.BuySpell)
                    && bestAction.TargetIndex >= 0
                    && bestAction.TargetIndex < state.ShopMinions.Count
                    && GetLiveGold(state)
                        - GetShopCardCost(state, state.ShopMinions[bestAction.TargetIndex])
                        >= liveUpgradeCost.Value;
                if (!keepBuyThenUpgrade)
                {
                    bestAction = new Engine.GameAction { Type = Engine.ActionType.Upgrade };
                }
            }
            if (liveSafety.SuppressUpgrade)
            {
                levelResult = new Engine.LevelUpSuggestion("DANGER", liveSafety.Reason);
                if (bestAction != null && bestAction.Type == Engine.ActionType.Upgrade)
                    bestAction = null;
            }
            if (liveSafety.SuppressRefresh
                && bestAction != null && bestAction.Type == Engine.ActionType.Refresh)
                bestAction = null;
            if (bestAction != null
                && bestAction.Type == Engine.ActionType.Refresh
                && !CanRefreshLeaveMeaningfulBudget(state, GetLiveGold(state)))
            {
                bestAction = null;
            }
            if (liveSafety.SuppressBuy
                && bestAction != null
                && (bestAction.Type == Engine.ActionType.BuyMinion
                    || bestAction.Type == Engine.ActionType.BuySpell))
                bestAction = null;
            var cardScores = SafeCall(() => _engine.GetShopCardScores(state), "GetShopCardScores") ?? new List<ShopCardScore>();
            var compGuidance = SafeCall(() => _engine.GetCompGuidance(state), "GetCompGuidance");

            var trinketScores = _engine.EvaluateTrinkets(state);
            // gold=0/1时清除二步前瞻+抑制不必要推荐(没钱就别推荐花金操作)
            var secondStep = _engine.BestTwoStepAction;
            var secondStepState = _engine.BestTwoStepState;
            if (GetLiveGold(state) == 0) { secondStep = null; secondStepState = null; }
            // 1.2.2 feedbackA: 动态铸币约束二步前瞻 — 执行完第一步(bestAction)后剩余铸币
            // 不足承担第二步任何非零花费动作时, 清掉二步(否则 T1 3金 状态条会显示
            // "买→A→买→B" 两个购买建议, 铸币根本覆盖不了; 免费动作(打/技)允许保留)。
            if (secondStep != null && bestAction != null)
            {
                int budgetAfterStep1 = GetLiveGold(state);
                int step1Cost = 0;
                try
                {
                    if (bestAction.Type == Engine.ActionType.BuyMinion
                        || bestAction.Type == Engine.ActionType.BuySpell)
                    {
                        var step1Card = bestAction.TargetIndex >= 0
                            && bestAction.TargetIndex < state.ShopMinions.Count
                            ? state.ShopMinions[bestAction.TargetIndex] : null;
                        if (step1Card != null)
                            step1Cost = GetShopCardCost(state, step1Card);
                    }
                    else if (bestAction.Type == Engine.ActionType.Upgrade)
                    {
                        var upCost2 = GameRuleEvaluator.GetUpgradeCost(
                            state, state.EffectiveRules ?? EffectiveGameRules.Default);
                        if (upCost2.HasValue) step1Cost = upCost2.Value;
                    }
                }
                catch { }
                budgetAfterStep1 = Math.Max(0, budgetAfterStep1 - step1Cost);
                bool step2IsFree = secondStep.Type == Engine.ActionType.PlayMinion
                    || secondStep.Type == Engine.ActionType.UseHeroPower
                    || secondStep.Type == Engine.ActionType.FreezeShop;
                bool step2Affordable = step2IsFree;
                if (!step2IsFree)
                {
                    int step2Cost = 0;
                    try
                    {
                        if (secondStep.Type == Engine.ActionType.BuyMinion
                            || secondStep.Type == Engine.ActionType.BuySpell)
                        {
                            var s2State = secondStepState ?? state;
                            var step2Card = secondStep.TargetIndex >= 0
                                && secondStep.TargetIndex < s2State.ShopMinions.Count
                                ? s2State.ShopMinions[secondStep.TargetIndex] : null;
                            if (step2Card != null)
                                step2Cost = GetShopCardCost(s2State, step2Card);
                        }
                        else if (secondStep.Type == Engine.ActionType.Refresh)
                        {
                            step2Cost = GameRuleEvaluator.GetRefreshCost(
                                state, state.HeroCardId,
                                state.EffectiveRules ?? EffectiveGameRules.Default);
                        }
                        else if (secondStep.Type == Engine.ActionType.Upgrade)
                        {
                            var up2 = GameRuleEvaluator.GetUpgradeCost(
                                state, state.EffectiveRules ?? EffectiveGameRules.Default);
                            if (up2.HasValue) step2Cost = up2.Value;
                        }
                    }
                    catch { }
                    step2Affordable = step2Cost <= budgetAfterStep1;
                }
                if (!step2Affordable)
                {
                    secondStep = null;
                    secondStepState = null;
                }
            }
            if (bestAction != null && bestAction.Type == Engine.ActionType.Upgrade)
            {
                secondStep = null;
                secondStepState = null;
            }
            cardScores = FilterAffordableShopScores(state, cardScores);
            if (bestAction != null
                && (bestAction.Type == ActionType.BuyMinion || bestAction.Type == ActionType.BuySpell)
                && !IsShopActionAffordable(state, bestAction, GetLiveGold(state)))
            {
                bestAction = null;
            }
            var plan = _visualizer.CreateVisualPlan(state, bestAction, levelResult, cardScores, _heroPowerEngine, _fe, compGuidance,
                secondStep, secondStepState, trinketScores);

            // 2026-08-19 阶段规划行（长链路分点式思考）：把 MainlinePlan 的阶段/
            // 模式 + StrategySequencePlanner 的跨回合步骤 + 本回合预算整合为
            // "阶段·模式 | ①本回合 ②下回合 ③阶段目标" 分点式指导。
            // 算法侧已思考完整链路，UI 必须同步展示（用户诉求：不能算法有效
            // 但 UI 跟不上）。
            if (plan != null && plan.Status != null)
            {
                var mainlineForPlan = _engine != null ? _engine.LastMainlinePlan : null;
                var stratPlanForPlan = _engine != null ? _engine.LastStrategyPlan : null;
                string stageCn = mainlineForPlan != null ? StageCn(mainlineForPlan.Stage) : "";
                string modeCn = mainlineForPlan != null ? ModeCn(mainlineForPlan.Mode) : "";
                plan.Status.StageLabel = stageCn;
                plan.Status.ModeLabel = modeCn;
                // 2026-08-20 极简方向(用户要求): 就简单说 升本-找高本随从 / 稳战力 / 囤铸币
                string direction = "稳战力";
                if (bestAction != null)
                {
                    switch (bestAction.Type)
                    {
                        case ActionType.Upgrade: direction = "升本-找高本随从"; break;
                        case ActionType.Refresh: direction = "找核心"; break;
                        case ActionType.FreezeShop: direction = "囤铸币"; break;
                        case ActionType.UseHeroPower: direction = "技能"; break;
                    }
                }
                else
                {
                    direction = "囤铸币";
                }
                // R218: 策略方向与动作分开——PlanLine 只显示纯方向词
                // 动作(买X/升N本)由商店标记/按钮高亮表达(用户要求: 别混在一起)
                plan.Status.PlanLine = direction;
                // R219: 整回合动作链——用 MaxGold 预算生成计划(买A→买B→升), 
                // 玩家连续操作时计划不变(商店刷新才重算)
                if (state != null)
                {
                    string shopSig = ShopSignature(state);
                    int liveGoldNow = GetLiveGold(state);
                    // P1 2026-08: 金币纳入缓存签名——升本/买牌后金币变化必须重算链,
                    // 否则"升本吃光金币仍显示买随从/法术"(用户实测: 动态铸币管理缺陷)。
                    // 预算用实时铸币(非 MaxGold): 链始终反映当前可执行动作。
                    if (_turnPlanTurn != state.Turn || _turnPlanShopSig != shopSig
                        || _turnPlanGold != liveGoldNow)
                    {
                        _turnPlanTurn = state.Turn;
                        _turnPlanShopSig = shopSig;
                        _turnPlanGold = liveGoldNow;
                        _turnPlanChain = BuildTurnActionChain(state, cardScores, liveGoldNow);
                    }
                    plan.Status.HintLine = _turnPlanChain;
                }
            // 过滤——升本吃光金币时不再推荐买卡(避免"推荐升本后没铸币还推荐购买")
            if (levelResult.Suggestion == "LEVEL_UP"
                && plan != null && plan.UpgradeHint != null
                && cardScores != null && cardScores.Count > 0)
            {
                int goldAfterUpgrade = GetLiveGold(state) - plan.UpgradeHint.Cost;
                var afterUpgrade = new List<ShopCardScore>();
                if (goldAfterUpgrade > 0)
                {
                    foreach (var sc in cardScores)
                        if (IsShopScoreAffordable(state, sc, goldAfterUpgrade))
                            afterUpgrade.Add(sc);
                }
                if (afterUpgrade.Count != cardScores.Count)
                {
                    cardScores = afterUpgrade;
                    plan = _visualizer.CreateVisualPlan(state, bestAction, levelResult, cardScores, _heroPowerEngine, _fe, compGuidance,
                        secondStep, secondStepState, trinketScores);
                    // 1.2.3 Akazamzarak feedback: LEVEL_UP filtered rebuild replaces plan with the
                    // visualizer's HintLine (full, untruncated card names + action arrows), which
                    // overflows the status row. Re-apply the BuildTurnActionChain override and
                    // refresh the cache signature so later frames rebuild with the new cardScores.
                    if (state != null && plan != null && plan.Status != null)
                    {
                        _turnPlanTurn = state.Turn;
                        _turnPlanShopSig = ShopSignature(state);
                        _turnPlanGold = GetLiveGold(state);
                        _turnPlanChain = BuildTurnActionChain(state, cardScores, GetLiveGold(state));
                        plan.Status.HintLine = _turnPlanChain;
                    }
                }
            }
            if (levelResult.Suggestion == "LEVEL_UP"
                && plan.UpgradeHint != null
                && GetLiveGold(state) >= plan.UpgradeHint.Cost)
            {
                int liveGoldNow = GetLiveGold(state);
                int goldAfterUpgrade = liveGoldNow - plan.UpgradeHint.Cost;
                int heroPowerCost = state.HeroPowerCost >= 0
                    ? state.HeroPowerCost : 0;
                if (heroPowerCost > 0 && goldAfterUpgrade < heroPowerCost)
                    heroUseSuggestion = "";
            }
            // 诊断: 饰品渲染链路
            // P2修复: 畸变首购免费状态栏提示
            if (state.EffectiveRules != null
                && state.EffectiveRules.FirstMinionPurchaseCost == 0
                && !state.FirstMinionPurchaseUsedThisTurn)
                plan.Status.ShowFirstBuyFree = true;
            if (state.TrinketOffer != null && state.TrinketOffer.Count > 0)
            {
                Log(string.Format("DIAG TrinketRender: offer={0} scores={1} hints={2}",
                    state.TrinketOffer.Count, trinketScores?.Count ?? 0, plan.TrinketHints?.Count ?? 0));
                // P1.5 Phase4: 先暂存本 choiceId 的评分；匹配 SendChoices 后才写完整记录。
                if (_trinketShadowEnabled && trinketScores != null && trinketScores.Count > 0)
                    TrinketShadowCapture(state, trinketScores);
            }
            var tEval = (DateTime.UtcNow - t0).TotalMilliseconds;

            Log(string.Format("Eval:{0:F0}ms act={1} lvl={2} hand={3}/{4} shop={5} comp={6} main={7}:{8} missing={9}",
                tEval, bestAction?.Type.ToString() ?? "-", levelResult.Suggestion,
                plan.HandMarker?.Index.ToString() ?? "-", state.HandMinions.Count,
                plan.ShopMarkers.Count, plan.Status.CompDir,
                compGuidance?.Archetype ?? "", compGuidance?.Reason ?? "",
                compGuidance?.MissingCoreCardNames?.Count ?? 0));
            // 诊断(问题2/E: gold=0仍提示刷新): 选中Refresh却买不起→定位是否Refresh兜底(无更优动作)
            if (bestAction != null && bestAction.Type == Engine.ActionType.Refresh
                && state.Gold < 1 && state.FreeRefreshCount == 0)
                Log(string.Format("  DIAG RefreshNoGold: gold={0} free={1} turn={2} shop={3} board={4} → 买不起仍推荐刷新",
                    state.Gold, state.FreeRefreshCount, state.Turn, state.ShopMinions.Count, state.BoardMinions.Count));
            if (plan.HandMarker != null)
                Log(string.Format("  打: {0}", plan.HandMarker.CardName));
            foreach (var sm in plan.ShopMarkers)
            {
                Log(string.Format("  ShopMarker idx={0} level={1} score={2:F2} card={3} tier={4}",
                    sm.Index, sm.Level, sm.Score, sm.CardName, sm.Tier));
            }
            // 1.2.2 T1反馈: 状态条文字诊断 — UI 提示"买X + 拿Y"两行误导, 文字层之前不进日志,
            // 无法从回放/日志定位来源。此处打印最终上屏文字(节流: 同turn+gold只打一次, 防刷屏)。
            if (_lastUiDiagTurn != state.Turn || _lastUiDiagGold != state.Gold
                || _lastUiDiagSig != (plan.Status.HintLine ?? "") + "|" + (plan.Status.PlanLine ?? ""))
            {
                _lastUiDiagTurn = state.Turn; _lastUiDiagGold = state.Gold;
                _lastUiDiagSig = (plan.Status.HintLine ?? "") + "|" + (plan.Status.PlanLine ?? "");
                Log(string.Format("UISTATUS turn={0} gold={1} best={2} | hint=[{3}] plan=[{4}] pick=[{5}] stage={6} mode={7}",
                    state.Turn, state.Gold, bestAction != null ? bestAction.Type.ToString() : "-",
                    plan.Status.HintLine, plan.Status.PlanLine, plan.Status.PickLine,
                    plan.Status.StageLabel, plan.Status.ModeLabel));
            }

            // ── 回放快照: 记录逐回合完整状态(含随从属性/对手/手牌) ──
            if (_currentGameRecord != null)
            {
                // 推断玩家本回合操作 (对比上一帧状态)
                List<string> actions = new List<string>();
                if (_prevState != null && _prevState.Turn == state.Turn)
                {
                    var prevShopEntities = new HashSet<int>(
                        _prevState.ShopMinions.Where(m => m != null && m.EntityId > 0)
                            .Select(m => m.EntityId));
                    var prevBoardById = _prevState.BoardMinions
                        .Where(m => m != null && m.EntityId > 0)
                        .ToDictionary(m => m.EntityId);
                    var prevHandById = _prevState.HandMinions
                        .Where(m => m != null && m.EntityId > 0)
                        .ToDictionary(m => m.EntityId);
                    var currShopEntities = new HashSet<int>(
                        state.ShopMinions.Where(m => m != null && m.EntityId > 0)
                            .Select(m => m.EntityId));
                    var currBoardEntities = new HashSet<int>(
                        state.BoardMinions.Where(m => m != null && m.EntityId > 0)
                            .Select(m => m.EntityId));
                    var currHandEntities = new HashSet<int>(
                        state.HandMinions.Where(m => m != null && m.EntityId > 0)
                            .Select(m => m.EntityId));

                    // 买牌: 上一帧商店实体进入手牌或板面
                    foreach (var hm in state.HandMinions)
                    {
                        if (hm != null && hm.EntityId > 0
                            && prevShopEntities.Contains(hm.EntityId)
                            && !prevHandById.ContainsKey(hm.EntityId)
                            && !prevBoardById.ContainsKey(hm.EntityId))
                            actions.Add("buy:" + hm.CardId + "→手牌");
                    }
                    foreach (var bm in state.BoardMinions)
                    {
                        if (bm != null && bm.EntityId > 0
                            && prevShopEntities.Contains(bm.EntityId)
                            && !prevBoardById.ContainsKey(bm.EntityId)
                            && !prevHandById.ContainsKey(bm.EntityId))
                            actions.Add("buy/play:" + bm.CardId + "→板面");
                    }

                    // 卖牌: 上一帧板面实体不再在手牌或板面
                    foreach (var pm in _prevState.BoardMinions)
                    {
                        if (pm == null || pm.EntityId <= 0) continue;
                        if (!currBoardEntities.Contains(pm.EntityId)
                            && !currHandEntities.Contains(pm.EntityId))
                            actions.Add("sell:" + pm.CardId);
                    }

                    // 打出手牌: 上一帧手牌实体进入板面
                    foreach (var hm in _prevState.HandMinions)
                    {
                        if (hm == null || hm.EntityId <= 0) continue;
                        if (currBoardEntities.Contains(hm.EntityId)
                            && !currHandEntities.Contains(hm.EntityId))
                            actions.Add("play:" + hm.CardId);
                    }

                    // 金币变化
                    int goldDelta = state.Gold - _prevState.Gold;
                    if (goldDelta < 0) actions.Add("gold:" + goldDelta);
                    else if (goldDelta > 0 && goldDelta != 3) actions.Add("gold:+" + goldDelta);

                    // 法术购买检测: 法术买入即用不入板面/手牌, 需从商店消失+金币减少推断
                    var prevShopSpells = _prevState.ShopMinions
                        .Where(m => m != null && m.IsSpell
                            && m.EntityId > 0
                            && !currShopEntities.Contains(m.EntityId))
                        .ToList();
                    foreach (var spell in prevShopSpells)
                    {
                        // 法术从商店消失了 + 没有出现在手牌/板面 + 金币减少了 → 法术购买
                        bool appearedInHand = state.HandMinions.Any(m =>
                            m != null && m.EntityId == spell.EntityId);
                        bool appearedOnBoard = state.BoardMinions.Any(m =>
                            m != null && m.EntityId == spell.EntityId);
                        if (!appearedInHand && !appearedOnBoard && state.Gold < _prevState.Gold)
                            actions.Add("spell:" + spell.CardId + "(" + (spell.CardName ?? "") + ")");
                    }

                    // 刷新/冻结: 商店整体更换或冻结状态翻转时补录，避免回放时间线把这类动作漏掉。
                    bool shopReplaced = prevShopEntities.Count > 0
                        && currShopEntities.Count > 0
                        && !prevShopEntities.Any(currShopEntities.Contains);
                    bool goldSpent = state.Gold < _prevState.Gold;
                    bool freeRefreshUsed = state.FreeRefreshCount < _prevState.FreeRefreshCount;
                    if (shopReplaced && (goldSpent || freeRefreshUsed))
                        actions.Add("refresh:→新商店");
                    if (!_prevState.FrozenShop && state.FrozenShop)
                        actions.Add("freeze:→冻结");
                    if (!_prevState.HeroPowerExhausted && state.HeroPowerExhausted)
                        actions.Add("heroPower:" + (state.HeroPowerCardId ?? ""));
                }
                // 升本
                if (state.TavernTier != (_prevState != null ? _prevState.TavernTier : 1))
                    actions.Add("level:→" + state.TavernTier + "本");
                // 检测阶段
                try { if (Core.Game.IsBattlegroundsCombatPhase) actions.Add("phase:combat"); else actions.Add("phase:recruit"); } catch { }

                string featureVector = SerializeFeatureVector(state);
                string bestActionJson = SerializeRecommendedAction(bestAction);
                string actionScoresJson = SerializeActionScores(state, bestAction, cardScores);
                string shadowScoresJson = SerializeShadowActionScores(state);
                string mainlinePlanJson = SerializeMainlinePlan(_engine.LastMainlinePlan);
                var globalState = ToGlobalReplayState(state);
                if (_globalReplayRecorder != null)
                {
                    RecordInferredGlobalActions(actions, state.Turn);
                    _globalReplayRecorder.RecordState(globalState, "hdt");
                    _globalReplayRecorder.RecordRecommendation(
                        bestActionJson, actionScoresJson, shadowScoresJson, featureVector,
                        mainlinePlanJson, state.Turn);
                }

                var snap = new TurnSnapshot
                {
                    Turn = state.Turn, Gold = state.Gold,
                    LiveGold = GetLiveGold(state), MaxGold = state.MaxGold,
                    FreeRefreshCount = state.FreeRefreshCount,
                    TavernUpgradeCost = state.TavernUpgradeCost,
                    FrozenShop = state.FrozenShop,
                    Tier = state.TavernTier, Health = state.Health, Armor = state.Armor,
                    BoardSize = state.BoardMinions.Count, ShopSize = state.ShopMinions.Count,
                    HandSize = state.HandMinions.Count,
                    BoardPower = _fe.ComputeBoardPower(state.BoardMinions),
                    OpponentPower = state.Opponents.Count > 0
                        ? _fe.ComputeAvgOpponentPower(state.Opponents) : 0,
                    Board = state.BoardMinions.Select(m => new Engine.MinionSnapshot
                    {
                        CardId = m.CardId, Name = m.CardName ?? "",
                        Attack = m.Attack, Health = m.Health, Tier = m.Tier, Position = m.Position,
                        Golden = m.Golden, Taunt = m.Taunt, DivineShield = m.DivineShield,
                        Reborn = m.Reborn, Poisonous = m.Poisonous, Venomous = m.Venomous,
                        Windfury = m.Windfury, Tribe = m.Tribe ?? "", IsSpell = m.IsSpell, Cost = m.Cost,
                    }).ToList(),
                    Shop = state.ShopMinions.Select(m => new Engine.MinionSnapshot
                    {
                        CardId = m.CardId, Name = m.CardName ?? "",
                        Attack = m.Attack, Health = m.Health, Tier = m.Tier, Position = m.Position,
                        Golden = m.Golden, Taunt = m.Taunt, DivineShield = m.DivineShield,
                        Reborn = m.Reborn, Poisonous = m.Poisonous, Venomous = m.Venomous,
                        Windfury = m.Windfury, MegaWindfury = m.MegaWindfury,
                        Stealth = m.Stealth, Cleave = m.Cleave, Overkill = m.Overkill,
                        Tribe = m.Tribe ?? "", IsSpell = m.IsSpell, Cost = m.Cost,
                    }).ToList(),
                    Hand = state.HandMinions.Select(m => new Engine.MinionSnapshot
                    {
                        CardId = m.CardId, Name = m.CardName ?? "",
                        Attack = m.Attack, Health = m.Health, Tier = m.Tier, Position = m.Position,
                        Golden = m.Golden, Taunt = m.Taunt, DivineShield = m.DivineShield,
                        Reborn = m.Reborn, Poisonous = m.Poisonous, Venomous = m.Venomous,
                        Windfury = m.Windfury, MegaWindfury = m.MegaWindfury,
                        Stealth = m.Stealth, Cleave = m.Cleave, Overkill = m.Overkill,
                        Tribe = m.Tribe ?? "", IsSpell = m.IsSpell, Cost = m.Cost,
                    }).ToList(),
                    Opponents = state.Opponents.Select(o => new OpponentSnapshot
                    {
                        HeroId = o.HeroCardId ?? "", HeroName = o.HeroName ?? "",
                        Health = o.Health, TavernTier = o.TavernTier, Alive = o.Alive,
                        Board = o.BoardMinions.Select(m => new Engine.MinionSnapshot
                        {
                            CardId = m.CardId, Name = m.CardName ?? "",
                            Attack = m.Attack, Health = m.Health, Tier = m.Tier, Position = m.Position,
                            Golden = m.Golden, Taunt = m.Taunt, DivineShield = m.DivineShield,
                            Reborn = m.Reborn, Poisonous = m.Poisonous, Venomous = m.Venomous,
                            Windfury = m.Windfury, MegaWindfury = m.MegaWindfury,
                            Stealth = m.Stealth, Cleave = m.Cleave, Overkill = m.Overkill,
                            Tribe = m.Tribe ?? "", IsSpell = m.IsSpell, Cost = m.Cost,
                        }).ToList(),
                    }).ToList(),
                    AlgoRecommendation = bestAction != null ? bestAction.Type.ToString() : "null",
                    AlgoRule = bestAction != null ? "rule" : "",
                    LevelUpSuggestion = levelResult.Suggestion,
                    CompDirection = plan.Status.CompDir ?? "",
                    PlayerTimeline = actions,
                    // ── 决策引擎训练数据 ──
                    FeatureVector = featureVector,
                    RecommendedActionJson = bestActionJson,
                    ActionScoresJson = actionScoresJson,
                    ShadowRecommendedActionJson = SerializeShadowRecommendedAction(state),
                    ShadowActionScoresJson = shadowScoresJson,
                };
                _currentGameRecord.Turns.Add(snap);
                _prevState = state;
            }

            // ── Board Snapshot: 回合级板面快照 → 变阵分析数据源 ──
            var boardSnap = string.Join(",", state.BoardMinions.Select(m =>
                m.CardId + "(" + m.Attack + "/" + m.Health + ")"
                + (m.Golden ? "g" : "") + (m.DivineShield ? "d" : "")
                + (m.Taunt ? "t" : "") + (m.Reborn ? "r" : "")
                + (m.Poisonous ? "P" : "") + (m.Venomous ? "V" : "")));
            var handSnap = string.Join(",", state.HandMinions.Select(m =>
                m.CardId + (m.Golden ? "g" : "")));
            string anomalySnap = !string.IsNullOrEmpty(state.AnomalyId) ? " anomaly=" + state.AnomalyId : "";
            Log(string.Format("SNAP: turn={0} hp={1} gold={2} tier={3} comp={4} board=[{5}] hand=[{6}]{7}",
                state.Turn, state.Health, state.Gold, state.TavernTier,
                plan.Status.CompDir, boardSnap, handSnap, anomalySnap));

            // 手牌为空时清除手牌标记, 防止残留
            if (plan.HandMarker != null && state.HandMinions.Count == 0)
                plan.HandMarker = null;
            _cachedPlan = plan;    // 缓存供每帧持久UI刷新
            _cachedState = state;
            _persistentDirty = true;
            // 0金且无任何可执行/可卖/可打/可发动动作时，直接清掉旧的非面板UI。
            // 防止上一帧购买/刷新高亮在 planHash 去重后继续残留。
            if (GetLiveGold(state) <= 0
                && state.FreeRefreshCount <= 0
                && plan.ShopMarkers.Count == 0
                && plan.SellMarkers.Count == 0
                && plan.HandMarker == null
                && plan.ActivateHint == null)
            {
                _renderer.ClearNonPanel();
                Log("ZeroGoldClear: no spendable action; cleared non-panel UI");
            }
            _lastLiveSafetySuppressRefresh = liveSafety.SuppressRefresh;
            DispatchRender(state, heroHint, plan, heroUseSuggestion);
            }
        }

        // ── 训练数据序列化 ──

        private static Engine.GlobalReplayState ToGlobalReplayState(GameState state)
        {
            var result = new Engine.GlobalReplayState
            {
                GameActive = state.GameActive,
                IsDuos = state.IsDuos,
                HeroCardId = state.HeroCardId ?? "",
                HeroName = state.HeroName ?? "",
                PlayerId = state.PlayerId,
                Turn = state.Turn,
                Gold = state.Gold,
                LiveGold = state.LiveGold >= 0 ? state.LiveGold : state.Gold,
                MaxGold = state.MaxGold,
                Tier = state.TavernTier,
                Health = state.Health,
                MaxHealth = state.MaxHealth,
                Armor = state.Armor,
                FreeRefreshCount = state.FreeRefreshCount,
                TavernUpgradeCost = state.TavernUpgradeCost,
                FrozenShop = state.FrozenShop,
                LastUpgradeTurn = state.LastUpgradeTurn,
                FirstPurchaseUsedThisTurn = state.FirstPurchaseUsedThisTurn,
                FirstMinionPurchaseUsedThisTurn = state.FirstMinionPurchaseUsedThisTurn,
                NormalRecruitAvailable = state.NormalRecruitAvailable,
                PassiveChoiceActive = state.PassiveChoiceActive,
                ReplenishingShopActive = state.ReplenishingShopActive,
                HeroPowerExhausted = state.HeroPowerExhausted,
                HeroPowerCost = state.HeroPowerCost,
                HeroPowerDigsRemaining = state.HeroPowerDigsRemaining,
                HeroPowerType = state.HeroPowerType ?? "",
                HeroPowerUnlockTurn = state.HeroPowerUnlockTurn,
                HeroPowerUnlockTier = state.HeroPowerUnlockTier,
                HeroPowerSpecial = state.HeroPowerSpecial ?? "",
                HeroPowerHasDiscover = state.HeroPowerHasDiscover,
                HasSecondHeroPower = state.HasSecondHeroPower,
                ExhaustedHeroPowerCount = state.ExhaustedHeroPowerCount,
                Phase = state.Phase ?? "",
                HeroPowerCardId = state.HeroPowerCardId ?? "",
                AnomalyId = state.AnomalyId ?? "",
                AnomalyActiveEffectIds = state.AnomalyContext != null
                    ? state.AnomalyContext.ActiveGlobalEffectIds.ToList()
                    : new List<string>(),
                HasAmbiguousPrimary = state.AnomalyContext != null
                    && state.AnomalyContext.HasAmbiguousPrimary,
                AvailableTribes = state.AvailableTribes
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToList(),
                ActiveTrinkets = state.ActiveTrinkets
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToList(),
                RecentlyBoughtEntityIds = state.RecentlyBoughtEntityIds
                    .OrderBy(value => value)
                    .ToList(),
                ActivatedThisTurn = state.ActivatedThisTurn
                    .OrderBy(value => value)
                    .ToList(),
                HeroPowers = state.HeroPowers
                    .Where(power => power != null)
                    .Select(ToGlobalHeroPower)
                    .ToList(),
                LastUsedTavernSpellCardId = state.LastUsedTavernSpellCardId ?? "",
                LastUsedTavernSpellName = state.LastUsedTavernSpellName ?? "",
                Shop = state.ShopMinions.Select(ToGlobalCard).ToList(),
                Board = state.BoardMinions.Select(ToGlobalCard).ToList(),
                Hand = state.HandMinions.Select(ToGlobalCard).ToList(),
                Discover = state.DiscoverOptions
                    .Where(o => o != null && !string.IsNullOrEmpty(o.CardId))
                    .Select(o => o.CardId).ToList(),
                Trinket = state.TrinketOffer
                    .Where(o => o != null && !string.IsNullOrEmpty(o.CardId))
                    .Select(o => o.CardId).ToList(),
                TrinketDetails = state.TrinketOffer
                    .Where(o => o != null)
                    .Select(ToGlobalTrinket)
                    .ToList(),
                Opponents = state.Opponents.Select(o => new Engine.GlobalOpponentRef
                {
                    ControllerId = o.ControllerId,
                    HeroCardId = o.HeroCardId ?? "",
                    HeroName = o.HeroName ?? "",
                    Health = o.Health,
                    TavernTier = o.TavernTier,
                    Alive = o.Alive,
                    Board = o.BoardMinions.Select(ToGlobalCard).ToList(),
                }).ToList(),
            };
            return result;
        }

        private static Engine.GlobalCardRef ToGlobalCard(MinionData card)
        {
            return new Engine.GlobalCardRef
            {
                EntityId = card.EntityId,
                CardId = card.CardId ?? "",
                CardName = card.CardName ?? "",
                Attack = card.Attack,
                Health = card.Health,
                Tier = card.Tier,
                Position = card.Position,
                Cost = card.Cost,
                Golden = card.Golden,
                Taunt = card.Taunt,
                DivineShield = card.DivineShield,
                Windfury = card.Windfury,
                MegaWindfury = card.MegaWindfury,
                Stealth = card.Stealth,
                Reborn = card.Reborn,
                Poisonous = card.Poisonous,
                Venomous = card.Venomous,
                Cleave = card.Cleave,
                Overkill = card.Overkill,
                AvengeCount = card.AvengeCount,
                Spell = card.IsSpell,
                Frozen = card.IsFrozen,
                GrantsStats = card.GrantsStats,
                Tribe = card.Tribe ?? "",
                CardText = card.CardText ?? "",
            };
        }

        private static Engine.GlobalHeroPowerRef ToGlobalHeroPower(Engine.HeroPowerState power)
        {
            return new Engine.GlobalHeroPowerRef
            {
                CardId = power.CardId ?? "",
                BaseHeroCardId = power.BaseHeroCardId ?? "",
                EntityId = power.EntityId,
                ScriptData1 = power.ScriptData1,
                Cost = power.Cost,
                Exhausted = power.Exhausted,
                IsPrimary = power.IsPrimary,
                IsSecondary = power.IsSecondary,
                IsActive = power.IsActive,
                IsUnlocked = power.IsUnlocked,
                HasDiscover = power.HasDiscover,
                UnlockTurn = power.UnlockTurn,
                UnlockTier = power.UnlockTier,
                SpecialRule = power.SpecialRule ?? "",
            };
        }

        private static Engine.GlobalTrinketRef ToGlobalTrinket(Engine.TrinketOption trinket)
        {
            return new Engine.GlobalTrinketRef
            {
                CardId = trinket.CardId ?? "",
                TrinketName = trinket.TrinketName ?? "",
                EntityId = trinket.EntityId,
                IsLesser = trinket.IsLesser,
                Tier = trinket.Tier,
                Attack = trinket.Attack,
                Health = trinket.Health,
                Tribes = (trinket.Tribes ?? new List<string>())
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToList(),
            };
        }

        private void RecordInferredGlobalActions(IEnumerable<string> actions, int turn)
        {
            if (_globalReplayRecorder == null || actions == null) return;
            foreach (var raw in actions)
            {
                int colon = raw.IndexOf(':');
                if (colon <= 0) continue;
                string kind = raw.Substring(0, colon);
                if (kind == "phase" || kind == "gold") continue;
                string rest = raw.Substring(colon + 1);
                string cardId = rest;
                int arrow = rest.IndexOf("→");
                if (arrow >= 0) cardId = rest.Substring(0, arrow);
                int paren = cardId.IndexOf('(');
                if (paren >= 0) cardId = cardId.Substring(0, paren);
                _globalReplayRecorder.RecordAction(
                    kind, cardId.Trim(), -1, 0, turn, "state");
            }
        }

        /// <summary>序列化22维特征向量为逗号分隔字符串 (与FeatureExtractor.FeatureCount对齐)</summary>
        private string SerializeMainlinePlan(Engine.MainlinePlan plan)
        {
            if (plan == null) return "null";
            try
            {
                var strategyPlan = _engine != null
                    ? _engine.LastStrategyPlan : null;
                var strategyMemory = _engine != null
                    ? _engine.StrategicMemorySnapshot : null;
                return JsonConvert.SerializeObject(new
                {
                    archetype = plan.Archetype ?? "",
                    guideId = plan.GuideId ?? "",
                    tier = plan.Tier ?? "",
                    compDir = plan.CompDir ?? "",
                    reason = plan.Reason ?? "",
                    metaSource = plan.MetaSource ?? "",
                    coreCardIds = plan.CoreCardIds ?? new System.Collections.Generic.List<string>(),
                    missingCoreCards = plan.MissingCoreCardNames ?? new System.Collections.Generic.List<string>(),
                    boardCoreCount = plan.BoardCoreCount,
                    handCoreCount = plan.HandCoreCount,
                    shopCoreCount = plan.ShopCoreCount,
                    hasTripleInShop = plan.HasTripleInShop,
                    hasCoreInShop = plan.HasCoreInShop,
                    shouldBuyCoreFirst = plan.ShouldBuyCoreFirst,
                    shouldHoldForNextTurn = plan.ShouldHoldForNextTurn,
                    nodePriority = plan.NodePriority.ToString(),
                    stage = plan.Stage ?? "",
                    mode = plan.Mode ?? "",
                    currentPathScore = plan.CurrentPathScore,
                    bestAlternativeScore = plan.BestAlternativeScore,
                    pivotScore = plan.PivotScore,
                    shouldConsiderPivot = plan.ShouldConsiderPivot,
                    shouldHedge = plan.ShouldHedge,
                    pivotObservationCount = plan.PivotObservationCount,
                    alternativeCoreCardIds =
                        plan.AlternativeCoreCardIds == null
                            ? new System.Collections.Generic.List<string>()
                            : plan.AlternativeCoreCardIds.OrderBy(x => x).ToList(),
                    counterPlan = plan.CounterPlan ?? "",
                    needTradeUp = plan.NeedTradeUp,
                    strategyObjective = strategyPlan != null
                        ? strategyPlan.Objective.ToString() : "",
                    strategyPlanHash = strategyPlan != null
                        ? strategyPlan.PlanHash ?? "" : "",
                    strategyPlanSteps = strategyPlan != null
                        && strategyPlan.Steps != null
                        ? strategyPlan.Steps
                            .Where(step => step != null && step.Action != null)
                            .Select(step => step.Action.Type + ":"
                                + step.Action.TargetIndex + ":"
                                + (step.Action.CardId ?? ""))
                            .ToList()
                        : new System.Collections.Generic.List<string>(),
                    memoryObservedCoreCardIds = strategyMemory != null
                        && strategyMemory.ObservedCoreCardIds != null
                        ? strategyMemory.ObservedCoreCardIds
                            .OrderBy(cardId => cardId, StringComparer.Ordinal)
                            .ToList()
                        : new System.Collections.Generic.List<string>(),
                    memoryObservedPairCardIds = strategyMemory != null
                        && strategyMemory.ObservedPairCardIds != null
                        ? strategyMemory.ObservedPairCardIds
                            .OrderBy(cardId => cardId, StringComparer.Ordinal)
                            .ToList()
                        : new System.Collections.Generic.List<string>(),
                });
            }
            catch
            {
                return "null";
            }
        }

        /// <summary>序列化22维特征向量为逗号分隔字符串 (与FeatureExtractor.FeatureCount对齐)</summary>
        private string SerializeFeatureVector(GameState state)
        {
            if (state == null) return "";
            var fe = _engine.GetFeatureExtractor();
            if (fe == null) return "";
            var f = fe.Extract(state);
            if (f == null || f.Length == 0) return "";
            return string.Join(",", f.Select(v => v.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)));
        }

        /// <summary>序列化推荐动作为紧凑JSON</summary>
        private string SerializeRecommendedAction(GameAction action)
        {
            if (action == null) return "null";
            return string.Format("{{\"type\":\"{0}\",\"target\":{1}}}",
                action.Type, action.TargetIndex);
        }

        /// <summary>
        /// 序列化 Shadow 统一评分第一名。
        /// 可见推荐仍来自 legacy-v1.1.0；该字段仅供回放评估和训练数据审计。
        /// </summary>
        private string SerializeShadowRecommendedAction(GameState state)
        {
            try
            {
                var ranked = _engine.GetRankedCandidates(state, 1);
                if (ranked == null || ranked.Count == 0 || ranked[0] == null
                    || ranked[0].Action == null)
                    return "null";
                return ShadowScoreSerializer.SerializeRecommended(ranked[0].Action);
            }
            catch
            {
                return "null";
            }
        }

        // ── 2026-08-19 阶段规划 UI 辅助（长链路分点式思考） ──

        private static string StageCn(string stage)
        {
            switch (stage)
            {
                case "Early": return "前期";
                case "Mid": return "中期";
                case "Late": return "后期";
                default: return "";
            }
        }

        private static string ModeCn(string mode)
        {
            switch (mode)
            {
                case "Commit": return "成型";
                case "Economy": return "经济";
                case "Pivot": return "转型";
                case "Hedge": return "对冲";
                case "TradeUp": return "升级";
                case "Survival": return "保命";
                case "Flexible": return "灵活";
                default: return "";
            }
        }

        private static string ActionToShortText(
            GameState state, Engine.GameAction action, GameState stepState)
        {
            if (action == null) return "";
            var s = stepState ?? state;
            switch (action.Type)
            {
                case Engine.ActionType.BuyMinion:
                    if (s != null && action.TargetIndex >= 0
                        && action.TargetIndex < s.ShopMinions.Count)
                        return "买" + CardNameShort(s.ShopMinions[action.TargetIndex]);
                    return "买随从";
                case Engine.ActionType.BuySpell:
                    if (s != null && action.TargetIndex >= 0
                        && action.TargetIndex < s.ShopMinions.Count)
                        return "买法术" + CardNameShort(s.ShopMinions[action.TargetIndex]);
                    return "买法术";
                case Engine.ActionType.Upgrade:
                    return "升" + (state.TavernTier + 1) + "本";
                case Engine.ActionType.Refresh:
                    return "刷新";
                case Engine.ActionType.FreezeShop:
                    return "冻结";
                case Engine.ActionType.SellMinion:
                    if (s != null && action.TargetIndex >= 0
                        && action.TargetIndex < s.BoardMinions.Count)
                        return "卖" + CardNameShort(s.BoardMinions[action.TargetIndex]);
                    return "卖随从";
                case Engine.ActionType.UseHeroPower:
                    return "技能";
                case Engine.ActionType.Activate:
                    return "发动";
                case Engine.ActionType.PlayMinion:
                case Engine.ActionType.UseSpell:
                    if (s != null && action.TargetIndex >= 0
                        && action.TargetIndex < s.HandMinions.Count)
                        return (action.Type == Engine.ActionType.UseSpell ? "用" : "打")
                            + CardNameShort(s.HandMinions[action.TargetIndex]);
                    return action.Type == Engine.ActionType.UseSpell ? "用牌" : "打牌";
                default:
                    return action.Type.ToString();
            }
        }

        private static string CardNameShort(Engine.MinionData m)
        {
            if (m == null) return "";
            var name = !string.IsNullOrEmpty(m.CardName)
                ? m.CardName
                : BobCoachPlugin.GetCardNameCn(m.CardId);
            if (string.IsNullOrEmpty(name)) return "";
            // 1.2.3 反馈: 卡名过长时按"显示宽度"截断(中/全角≈2, ASCII≈1)而非字符数 —
            // 旧逻辑 Substring(0,6) 对英文名 Decoy Conjurer 截成 "Decoy "(留尾空格)+… 且
            // 14 个 ASCII 字符视觉宽度≈7 汉字, 单行文字仍超宽被遮挡。宽度上限≈10单位(5汉字)。
            if (DisplayWidth(name) <= 10) return name.TrimEnd();
            int cut = 0, w = 0;
            foreach (char ch in name)
            {
                int cw = (ch >= 0x2E80 && ch <= 0x9FFF) || ch == '→' || ch == '—' || ch == '·' || ch == '…' ? 2 : 1;
                if (w + cw > 10) break;
                w += cw; cut++;
            }
            string shortName = name.Substring(0, cut).TrimEnd();
            return shortName + "…";
        }

        private static string StageGoalText(Engine.MainlinePlan mainline, GameState state)
        {
            string stage = mainline != null ? (mainline.Stage ?? "") : "";
            string mode = mainline != null ? (mainline.Mode ?? "") : "";
            var goal = "";
            switch (stage)
            {
                case "Early": goal = "T5前升3本，建立节奏"; break;
                case "Mid": goal = "T7前升4本，找流派核心"; break;
                case "Late": goal = "T9-T11升5本，补强核心"; break;
                default: goal = "成型+针对对手"; break;
            }
            if (mode == "Survival") goal = "保名次：降本保命，反制机制";
            else if (mode == "Pivot") goal = "转型：换新流派方向";
            else if (mode == "Economy") goal = "经济：攒币为升本";
            else if (mode == "TradeUp") goal = "换血：卖弱买强";
            if (!string.IsNullOrEmpty(mode))
                goal = "阶段目标(" + ModeCn(mode) + ")：" + goal;
            return goal;
        }

        /// <summary>
        /// 序列化 Shadow 统一评分列表。
        /// 复用 GetRankedCandidates，保证评分和推荐标记来自同一候选面；
        /// 不会改变玩家可见的 legacy 策略。
        /// </summary>
        private string SerializeShadowActionScores(GameState state)
        {
            try
            {
                var ranked = _engine.GetRankedCandidates(state, int.MaxValue);
                return ShadowScoreSerializer.SerializeScores(state, ranked);
            }
            catch
            {
                return "[]";
            }
        }

        /// <summary>保留兼容字段：新快照也写入 Shadow 统一评分，旧消费者无需依赖旧重算结果。</summary>
        private string SerializeActionScores(GameState state, GameAction bestAction,
            System.Collections.Generic.List<ShopCardScore> cardScores)
        {
            return SerializeShadowActionScores(state);
        }

        /// <summary>每帧刷新持久UI：状态条 + 手牌标记(帧级更新, 消除操作延迟)</summary>
        private void RefreshPersistentUI()
        {
            if (_cachedPlan == null || _cachedState == null) return;
            if (!_persistentDirty) return;
            _persistentDirty = false;

            // 战斗阶段不刷新
            bool inCombat = false;
            try { inCombat = Core.Game.IsBattlegroundsCombatPhase; } catch { }
            if (inCombat) return;

            // 实时手牌数量(直接从HDT取, 避免cachedState过时)
            int liveHandCount = 0;
            try
            {
                foreach (var e in Core.Game.Entities.Values.ToList())
                {
                    if (e == null) continue;
                    if (e.IsInHand && e.IsControlledBy(1) && !string.IsNullOrEmpty(e.CardId))
                        liveHandCount++;
                }
            }
            catch { }

            var plan = _cachedPlan;
            bool hasHandMarker = plan.HandMarker != null && plan.HandMarker.Index >= 0
                && liveHandCount > 0 && plan.HandMarker.Index < liveHandCount;

            try
            {
                var app = System.Windows.Application.Current;
                if (app == null) return;
                int capturedVersion = _renderVersion;
                app.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render,
                    new Action(() =>
                    {
                        try
                        {
                            if (capturedVersion != _renderVersion) return;
                            bool callbackCombat = false;
                            try { callbackCombat = Core.Game.IsBattlegroundsCombatPhase; } catch { }
                            if (callbackCombat) return;
                            _renderer.ShowStatusStrip(plan.Status);
                            // 手牌高亮已静默(v3.0)
                        }
                        catch { }
                    }));
            }
            catch { }
        }

        private bool UpdateDiscoverTriggerWindow(GameState state)
        {
            if (state == null) return false;

            int curGolden = 0;
            foreach (var bm in state.BoardMinions) if (bm.Golden) curGolden++;
            int curGoldenHand = 0;
            foreach (var hm in state.HandMinions) if (hm.Golden) curGoldenHand++;

            // ── T3(07071121): Power.log 独占发现门 → zone6 启发式静默 ──
            // 路径A(OnPowerLogDiscoverOffered/ChoiceList)活跃时, B 的9个zone6启发式全部让路:
            //   ① 防 A/B 对同一次发现双触发(混淆的双 source 日志);
            //   ② 防 A 的 CHOSEN 关闭面板后 3 秒内, B 用 stale zone6 残留把面板重新开起来("消不掉"根因之一)。
            // 返回 false(不是 true): 发现门本就由 A 通过 _discoverPanelActive/_lastDiscoverTriggerTime 自开
            // (initialDiscoverGate 在调用本方法前已设), 不依赖本方法返回值; 而 IsChoiceListActive 不区分
            // 发现/饰品选择, 若 return true 会在 T6/T9 饰品选择时误开发现门让 zone6 误扫。故只静默、不强开。
            // 仍同步 _prev* 跟踪, 使 B 在 A 结束后不因跨越 A 的状态差产生误触发。
            // Power.log 不可用时 IsChoiceListActive 恒 false 且 _discoverPanelActive 不置位 → B 照常兜底。
            bool powerLogOwnsDiscover = _discoverPanelActive
                || (_powerLogWatcher != null && _powerLogWatcher.IsChoiceListActive);
            if (powerLogOwnsDiscover)
            {
                MarkDueScheduledDiscoversTriggered(state);
                _prevGoldenCount = curGolden;
                _prevGoldenHandCount = curGoldenHand;
                _prevHpExhausted = state.HeroPowerExhausted;
                _prevExhaustedHeroPowerEntityIds = new HashSet<int>(state.HeroPowers
                    .Where(power => power.Exhausted).Select(power => power.EntityId));
                _prevBoardCountForDiscover = state.BoardMinions.Count;
                _prevBoardHadDiscoverSource = BoardHasDiscoverSource(state.BoardMinions);
                return false;
            }

            bool standardTripleDiscover = Engine.TripleRuleEvaluator.GrantsStandardDiscover(
                state.EffectiveRules);
            bool tripleJustHappened = standardTripleDiscover
                && _prevGoldenCount >= 0 && curGolden > _prevGoldenCount;
            bool handTripleJustHappened = _prevGoldenHandCount >= 0
                && standardTripleDiscover
                && curGoldenHand > _prevGoldenHandCount
                && _extractor != null
                && _extractor.Zone6FreshThisFrame
                && _extractor.Zone6EntityCountThisFrame > 0;
            var newlyExhaustedDiscoverPower = state.HeroPowers
                .FirstOrDefault(power => power.HasDiscover && power.Exhausted
                    && !_prevExhaustedHeroPowerEntityIds.Contains(power.EntityId));
            bool heroPowerDiscoverUsed = newlyExhaustedDiscoverPower != null
                ? newlyExhaustedDiscoverPower.IsPrimary
                : state.HeroPowers.Count == 0 && state.HeroPowerHasDiscover
                    && state.HeroPowerExhausted && !_prevHpExhausted;
            // 第二技能必须由具体的发现技能实体发生耗尽变化；zone6仍作为候选到账事实。
            bool secondHeroPowerDiscoverUsed = newlyExhaustedDiscoverPower != null
                && newlyExhaustedDiscoverPower.IsSecondary
                && _extractor != null
                && _extractor.Zone6FreshThisFrame
                && _extractor.Zone6EntityCountThisFrame > 0;
            bool isPrize = false;
            try { var ps = _engine.EvaluatePrizeDiscovers(state); isPrize = ps != null && ps.Count > 0; } catch { }
            string scheduledDiscoverKind = GetScheduledDiscoverKind(state);
            bool scheduledPrizeDiscover = scheduledDiscoverKind == "prize_discover";
            bool scheduledMinionDiscover = scheduledDiscoverKind == "golden_minion_discover"
                || scheduledDiscoverKind == "tier_locked_minion_discover";

            // 07062242修复: 法术/战吼发现候选同样晚于手牌减少1-2帧进zone6, 同帧要求天然错过
            // (搜寻时光/三连奖励法术发现零触发的时序根因) → 手牌减少后开3秒等待窗口。
            if (_extractor != null && _extractor.HandDecreasedThisFrame)
                _handDiscoverPendingUntil = DateTime.UtcNow.AddSeconds(3);
            bool handPlayedIntoFreshZone6 = _extractor != null
                && DateTime.UtcNow <= _handDiscoverPendingUntil
                && _extractor.Zone6FreshThisFrame
                && _extractor.Zone6EntityCountThisFrame > 0;
            if (handPlayedIntoFreshZone6)
                _handDiscoverPendingUntil = DateTime.MinValue; // 消费窗口
            // 07062157修复: boardLeave 从"同帧zone6 fresh"改为"出售后3秒窗口内zone6 fresh" —
            // 发现候选实体通常晚于出售动作1-2帧进入zone6, 同帧要求天然错过(侦查员零触发的时序根因)。
            bool boardShrankWithSource = _prevBoardCountForDiscover >= 0
                && state.BoardMinions.Count < _prevBoardCountForDiscover
                && _prevBoardHadDiscoverSource;
            if (boardShrankWithSource)
                _boardLeaveDiscoverPendingUntil = DateTime.UtcNow.AddSeconds(3);
            bool boardDiscoverSourceLeft = _extractor != null
                && DateTime.UtcNow <= _boardLeaveDiscoverPendingUntil
                && _extractor.Zone6FreshThisFrame
                && _extractor.Zone6EntityCountThisFrame > 0;
            if (boardDiscoverSourceLeft)
                _boardLeaveDiscoverPendingUntil = DateTime.MinValue; // 消费窗口, 防重复触发
            // 07062157诊断: 板面减少帧无条件打各条件值 — 出售发现随从(耐心的侦查员)仍未触发,
            // 但SKIP诊断上限耗尽后归因盲区; 此日志锁定是 prevHadSource 还是 zone6 时序断链
            if (_extractor != null && _prevBoardCountForDiscover >= 0
                && state.BoardMinions.Count < _prevBoardCountForDiscover)
            {
                Log(string.Format("DIAG BoardLeaveCheck: prevBoard={0} curBoard={1} prevHadSource={2} zone6Fresh={3} zone6Count={4} zone6NewNonTrinket={5}",
                    _prevBoardCountForDiscover, state.BoardMinions.Count, _prevBoardHadDiscoverSource,
                    _extractor.Zone6FreshThisFrame, _extractor.Zone6EntityCountThisFrame,
                    _extractor.Zone6NewNonTrinketCountThisFrame));
            }
            // 兜底源(低置信度): 招募阶段 zone6 一次性新增去重候选3~4个(发现候选批次特征)。
            // 结构性保险: 触发源白名单枚举不全时(如新发现来源), 仍能开门。
            // 07062242: 按"去重候选cardId数"判定 — 实测发现批次=3候选×2实体=6个新增实体,
            // 按实体数3~4不命中; 去重后恰为3。杂项(空cardId/按钮/英雄/畸变/饰品)已在提取层排除。
            // 排除计划饰品回合(T6/T9饰品批次也走zone6), 排除战斗阶段。
            bool zone6BatchFallback = _extractor != null
                && _extractor.Zone6NewDistinctCandidateCount >= 3
                && _extractor.Zone6NewDistinctCandidateCount <= 4
                && _extractor.Zone6FreshThisFrame
                && state.Phase == "shop"
                && state.Turn != 6 && state.Turn != 9;
            bool hasRealTrigger = isPrize || scheduledPrizeDiscover || scheduledMinionDiscover
                || tripleJustHappened || handTripleJustHappened
                || heroPowerDiscoverUsed || secondHeroPowerDiscoverUsed || handPlayedIntoFreshZone6
                || boardDiscoverSourceLeft || zone6BatchFallback;
            if (hasRealTrigger)
            {
                _lastDiscoverTriggerTime = DateTime.UtcNow;
                _discoverSource = isPrize || scheduledPrizeDiscover ? "prize"
                    : scheduledMinionDiscover ? "scheduled"
                    : tripleJustHappened || handTripleJustHappened ? "triple"
                    : heroPowerDiscoverUsed ? "heroPower"
                    : secondHeroPowerDiscoverUsed ? "secondHeroPower"
                    : boardDiscoverSourceLeft ? "boardLeave"
                    : handPlayedIntoFreshZone6 ? "battlecry" : "zone6Fallback";
                try { _extractor.ClearDiscoverCache(); } catch { }
                if (scheduledMinionDiscover)
                    Log(string.Format("DiscoverTrigger: source=scheduled kind={0} turn={1}",
                        scheduledDiscoverKind, state.Turn));
                else if (scheduledPrizeDiscover)
                    Log(string.Format("DiscoverTrigger: source=prize turn={0} via=scheduled", state.Turn));
                else if (tripleJustHappened)
                    Log(string.Format("DiscoverTrigger: source=triple golden={0}→{1}", _prevGoldenCount, curGolden));
                else if (handTripleJustHappened)
                    Log(string.Format("DiscoverTrigger: source=handTriple goldenHand={0}→{1} zone6={2}",
                        _prevGoldenHandCount, curGoldenHand, _extractor.Zone6EntityCountThisFrame));
                else if (heroPowerDiscoverUsed)
                    Log("DiscoverTrigger: source=heroPower");
                else if (secondHeroPowerDiscoverUsed)
                    Log(string.Format("DiscoverTrigger: source=secondHeroPower cardId={0} zone6={1}",
                        newlyExhaustedDiscoverPower.CardId, _extractor.Zone6EntityCountThisFrame));
                else if (boardDiscoverSourceLeft)
                    Log(string.Format("DiscoverTrigger: source=boardLeave prevBoard={0} curBoard={1} zone6={2}",
                        _prevBoardCountForDiscover, state.BoardMinions.Count, _extractor.Zone6EntityCountThisFrame));
                else if (handPlayedIntoFreshZone6)
                    Log(string.Format("DiscoverTrigger: source=hand zone6={0}", _extractor.Zone6EntityCountThisFrame));
                else if (zone6BatchFallback)
                    Log(string.Format("DiscoverTrigger: source=zone6Fallback distinct={0} new={1} total={2} turn={3}",
                        _extractor.Zone6NewDistinctCandidateCount, _extractor.Zone6NewEntityCountThisFrame,
                        _extractor.Zone6EntityCountThisFrame, state.Turn));
            }

            _prevGoldenCount = curGolden;
            _prevGoldenHandCount = curGoldenHand;
            _prevHpExhausted = state.HeroPowerExhausted;
            _prevExhaustedHeroPowerEntityIds = new HashSet<int>(state.HeroPowers
                .Where(power => power.Exhausted).Select(power => power.EntityId));
            _prevBoardCountForDiscover = state.BoardMinions.Count;
            _prevBoardHadDiscoverSource = BoardHasDiscoverSource(state.BoardMinions);
            return hasRealTrigger;
        }

        // 出售/离场触发发现的已知卡牌白名单 — HearthDb 文本判定的兜底(07062157: 耐心的侦查员出售零触发)
        private static readonly HashSet<string> DiscoverSourceCardWhitelist = new HashSet<string>
        {
            "BG24_715",   // 耐心的侦查员: 出售时发现一张随从牌
            "BG24_715_G", // 金色版
        };

        private bool BoardHasDiscoverSource(List<Engine.MinionData> board)
        {
            if (board == null || board.Count == 0) return false;
            foreach (var m in board)
            {
                if (m == null) continue;
                if (!string.IsNullOrEmpty(m.CardId) && DiscoverSourceCardWhitelist.Contains(m.CardId)) return true;
                string text = (m.CardName ?? "") + " " + (m.CardId ?? "");
                if (text.Contains("发现") || text.Contains("Discover")) return true;
                try
                {
                    HearthDb.Card c;
                    if (!string.IsNullOrEmpty(m.CardId)
                        && HearthDb.Cards.All.TryGetValue(m.CardId, out c)
                        && c != null)
                    {
                        text = (c.Text ?? "") + " " + (c.Name ?? "");
                        if (text.Contains("发现") || text.Contains("Discover")) return true;
                    }
                }
                catch { }
            }
            return false;
        }

        private string GetScheduledDiscoverKind(GameState state)
        {
            if (state == null) return "";
            var due = Engine.ScheduledGrantEvaluator.GetDue(
                state, state.EffectiveRules ?? Engine.EffectiveGameRules.Default)
                .FirstOrDefault(item => item != null && item.Grant != null
                    && (item.Grant.Kind == "golden_minion_discover"
                        || item.Grant.Kind == "prize_discover"
                        || item.Grant.Kind == "tier_locked_minion_discover")
                    && !_triggeredScheduledDiscoverOccurrenceIds.Contains(item.OccurrenceId));
            if (due != null)
            {
                if (state.DiscoverOptions != null && state.DiscoverOptions.Count >= 2)
                    return "";
                _triggeredScheduledDiscoverOccurrenceIds.Add(due.OccurrenceId);
                return due.Grant.Kind;
            }
            return "";
        }

        private void SubscribePowerLogWatcherEvents()
        {
            if (_powerLogWatcher == null || _powerLogEventsSubscribed) return;

            _powerLogWatcher.PhaseChanged += OnPowerLogPhaseChanged;
            _powerLogWatcher.DiscoverOffered += OnPowerLogDiscoverOffered;
            _powerLogWatcher.TimewarpPurchaseOffered += OnPowerLogTimewarpPurchaseOffered;
            _powerLogWatcher.DarkGiftChoiceActive += OnPowerLogDarkGiftChoiceActive;
            _powerLogWatcher.DarkGiftCompleted += OnPowerLogDarkGiftCompleted;
            _powerLogWatcher.PassiveChoiceActive += OnPowerLogPassiveChoiceActive;
            _powerLogWatcher.PassiveChoiceCompleted += OnPowerLogPassiveChoiceCompleted;
            _powerLogWatcher.HeroPowerDiscoverOffered += OnPowerLogHeroPowerDiscoverOffered;
            _powerLogWatcher.DarkGiftPressableChanged += OnPowerLogDarkGiftPressableChanged;
            _powerLogWatcher.TrinketChoiceActive += OnPowerLogTrinketChoiceActive;
            _powerLogWatcher.ChoiceCompleted += OnPowerLogChoiceCompleted;
            _powerLogWatcher.TeammateGoldTransferObserved += OnPowerLogTeammateGoldTransfer;
            _powerLogWatcher.PowerLogEventDetected += OnPowerLogEventDetected;
            _powerLogWatcher.StateChanged += OnPowerLogStateChanged;
            _powerLogWatcher.BuildNumberChanged += OnPowerLogBuildNumberChanged;
            _powerLogEventsSubscribed = true;
        }

        private void UnsubscribePowerLogWatcherEvents()
        {
            if (_powerLogWatcher == null || !_powerLogEventsSubscribed) return;

            _powerLogWatcher.PhaseChanged -= OnPowerLogPhaseChanged;
            _powerLogWatcher.DiscoverOffered -= OnPowerLogDiscoverOffered;
            _powerLogWatcher.TimewarpPurchaseOffered -= OnPowerLogTimewarpPurchaseOffered;
            _powerLogWatcher.DarkGiftChoiceActive -= OnPowerLogDarkGiftChoiceActive;
            _powerLogWatcher.DarkGiftCompleted -= OnPowerLogDarkGiftCompleted;
            _powerLogWatcher.PassiveChoiceActive -= OnPowerLogPassiveChoiceActive;
            _powerLogWatcher.PassiveChoiceCompleted -= OnPowerLogPassiveChoiceCompleted;
            _powerLogWatcher.HeroPowerDiscoverOffered -= OnPowerLogHeroPowerDiscoverOffered;
            _powerLogWatcher.DarkGiftPressableChanged -= OnPowerLogDarkGiftPressableChanged;
            _powerLogWatcher.TrinketChoiceActive -= OnPowerLogTrinketChoiceActive;
            _powerLogWatcher.ChoiceCompleted -= OnPowerLogChoiceCompleted;
            _powerLogWatcher.TeammateGoldTransferObserved -= OnPowerLogTeammateGoldTransfer;
            _powerLogWatcher.PowerLogEventDetected -= OnPowerLogEventDetected;
            _powerLogWatcher.StateChanged -= OnPowerLogStateChanged;
            _powerLogWatcher.BuildNumberChanged -= OnPowerLogBuildNumberChanged;
            _powerLogEventsSubscribed = false;
        }

        private void MarkUpgradePrizeDiscoverObserved(GameState state)
        {
            if (state == null || state.PendingPrizeDiscoverExpectations == null
                || state.PendingPrizeDiscoverExpectations.Count == 0
                || state.DiscoverOptions == null || state.DiscoverOptions.Count < 2)
                return;
            var batch = _discoverBatchFromLog;
            var rule = state.EffectiveRules != null ? state.EffectiveRules.UpgradePrize : null;
            if (batch == null || batch.ChoiceId < 0 || rule == null
                || batch.SourceCardId != rule.SourceId
                || _claimedUpgradePrizeChoiceIds.Contains(batch.ChoiceId))
                return;
            List<Engine.DecisionEngine.TrinketScore> prizes = null;
            try { prizes = _engine.EvaluatePrizeDiscovers(state); } catch { }
            if (prizes == null || prizes.Count < 2) return;
            string occurrenceId = state.PendingPrizeDiscoverExpectations[0].OccurrenceId;
            if (_extractor == null || !_extractor.MarkUpgradePrizeDiscoverClaimed()) return;
            _claimedUpgradePrizeChoiceIds.Add(batch.ChoiceId);
            state.PendingPrizeDiscoverExpectations.RemoveAt(0);
            state.ClaimedPrizeDiscoverOccurrences.Add(occurrenceId);
            _discoverSource = "prize";
            Log(string.Format(
                "UpgradePrize claimed: occurrence={0} tier={1} candidates={2}",
                occurrenceId,
                Engine.UpgradePrizeEvaluator.GetPrizeTier(
                    state.EffectiveRules.UpgradePrize, state.Turn),
                state.DiscoverOptions.Count));
        }

        private void MarkDueScheduledDiscoversTriggered(GameState state)
        {
            if (state == null) return;
            foreach (var item in Engine.ScheduledGrantEvaluator.GetDue(
                state, state.EffectiveRules ?? Engine.EffectiveGameRules.Default))
            {
                if (item == null || item.Grant == null) continue;
                if (item.Grant.Kind == "golden_minion_discover"
                    || item.Grant.Kind == "prize_discover"
                    || item.Grant.Kind == "tier_locked_minion_discover")
                    _triggeredScheduledDiscoverOccurrenceIds.Add(item.OccurrenceId);
            }
        }

        /// <summary>Cookie skill target tribe: majority tribe on board+shop</summary>
        private static string PickCookieTargetTribe(GameState state)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            if (state.BoardMinions != null)
                foreach (var m in state.BoardMinions)
                    if (m != null) foreach (var t in MinionData.GetTribesArray(m.Tribe)) { int c; counts.TryGetValue(t, out c); counts[t] = c + 1; }
            if (state.ShopMinions != null)
                foreach (var m in state.ShopMinions)
                    if (m != null) foreach (var t in MinionData.GetTribesArray(m.Tribe)) { int c; counts.TryGetValue(t, out c); counts[t] = c + 1; }
            string best = null; int bestN = 0;
            foreach (var kv in counts)
                if (kv.Value > bestN) { bestN = kv.Value; best = kv.Key; }
            return best;
        }
        
        private int GetLiveGold(GameState state)
        {
            return state != null && state.LiveGold >= 0 ? state.LiveGold : (state != null ? state.Gold : 0);
        }

        private static string AppendActionHint(
            string current,
            string line,
            string duplicateAnchor)
        {
            if (string.IsNullOrEmpty(line)) return current ?? "";
            if (string.IsNullOrEmpty(current)) return line;
            if (!string.IsNullOrEmpty(duplicateAnchor)
                && current.Contains(duplicateAnchor))
                return current;
            if (current.IndexOf('\n') >= 0) return current;  // R213 动作链最多2段
return current + "\n" + line;
        }

        // P3 2026-08: 点金类技能识别——雷诺·杰克逊(每局限一次使友方随从变金色)及同类
        private static bool IsGoldenizeSkill(GameState state, Engine.HeroStrategy strategy, Engine.HeroSkillFact skill)
        {
            string hpId = state != null ? (state.HeroPowerCardId ?? "") : "";
            string text = (skill != null ? (skill.TextCn ?? "") : "")
                + " " + (strategy != null ? (strategy.PowerHint ?? "") : "");
            if (hpId.Contains("TB_BaconShop_HP_046")) return true; // 雷诺·杰克逊点金
            if (text.Contains("变为金色") || text.Contains("Golden") || text.Contains("点金")) return true;
            if ((strategy != null && strategy.SpecialRule == "one_time_golden")) return true;
            return false;
        }

        // P3: 板面是否有值得点金的目标——非金色 + 有一定价值(高本/高身材/核心卡)
        private bool HasWorthyGoldenizeTarget(GameState state)
        {
            if (state == null || state.BoardMinions == null || state.BoardMinions.Count == 0) return false;
            foreach (var m in state.BoardMinions)
            {
                if (m == null) continue;
                if (m.Golden) continue;
                int tier = m.Tier;
                int stats = (m.Attack > 0 ? m.Attack : 0) + (m.Health > 0 ? m.Health : 0);
                if (tier >= 4 || stats >= 12) return true;
                if (_engine != null && (_engine.IsResourceCard(m.CardId)
                    || _engine.IsCoreComboCard(m.CardId))) return true;
            }
            return false;
        }

        private bool HasSellableBoardMinion(GameState state)
        {
            return state != null && state.BoardMinions != null
                && state.BoardMinions.Any(m => m != null && !m.Golden);
        }

        private bool CanAffordAfterOneSell(GameState state, int cost)
        {
            if (state == null) return false;
            if (state.Turn < 3) return false;
            int liveGold = GetLiveGold(state);
            if (liveGold >= cost) return true;
            // 2026-08-19 B5: 仅允许1金币缺口(卖1凑1)且需真实可卖随从
            return cost <= liveGold + 1 && HasPotentialSellAfterTurn(state);
        }

        private bool HasPotentialSellAfterTurn(GameState state)
        {
            return state != null
                && state.BoardMinions != null
                && (state.Turn >= 4
                    ? state.BoardMinions.Any(m => m != null && !m.Golden
                        && (state.RecentlyBoughtEntityIds == null
                            || !state.RecentlyBoughtEntityIds.Contains(m.EntityId)))
                    : HasEarlyTripleSellOpportunity(state));
        }

        private bool HasEarlyTripleSellOpportunity(GameState state)
        {
            if (state == null || state.Turn < 3 || state.Turn > 4
                || state.BoardMinions == null || state.BoardMinions.Count < 4
                || state.ShopMinions == null || state.ShopMinions.Count == 0)
                return false;
            var rules = state.EffectiveRules ?? Engine.EffectiveGameRules.Default;
            bool tripleInShop = state.ShopMinions.Any(shop =>
                shop != null && !shop.IsSpell
                    && Engine.TripleRuleEvaluator.CompletesGolden(
                        state, shop.CardId, rules));
            return tripleInShop
                && state.BoardMinions.Any(m => m != null && !m.Golden);
        }

        private int GetShopCardCost(GameState state, Engine.MinionData shopCard)
        {
            if (state == null || shopCard == null) return 3;
            return Engine.GameRuleEvaluator.GetPurchaseCost(
                state, shopCard, state.HeroCardId,
                state.EffectiveRules ?? EffectiveGameRules.Default);
        }

        private Engine.MinionData ResolveShopMarkerCard(
            GameState state, Engine.ShopMarker marker)
        {
            if (state == null || marker == null) return null;
            if (marker.Index >= 0 && marker.Index < state.ShopMinions.Count)
                return state.ShopMinions[marker.Index];
            if (marker.EntityId > 0)
                return state.ShopMinions.FirstOrDefault(
                    card => card != null && card.EntityId == marker.EntityId);
            return null;
        }

        private bool IsShopCardAffordableOrAfterSell(GameState state, Engine.MinionData shopCard, int gold)
        {
            if (state == null || shopCard == null) return false;
            int cost = GetShopCardCost(state, shopCard);
            return gold >= cost || (cost <= gold + 1 && HasSellableBoardMinion(state));
        }

        // R219: 整回合动作链生成——按评分排序，预算内连续推荐(买A→买B→升N本)
        private string BuildTurnActionChain(GameState state, List<ShopCardScore> cardScores, int budget)
        {
            if (state == null || cardScores == null || budget <= 0) return "";
            var chain = new List<string>();
            int remain = budget;
            // R220: 瓦尔登·晨拥(BG22_HERO_004)——刷新触发技能复制最高级随从(免费+冻结): 高价值联动
            if ((state.HeroCardId ?? "").StartsWith("BG22_HERO_004")
                && remain >= 1
                && state.ShopMinions != null && state.ShopMinions.Count > 0)
            {
                chain.Add("刷新(复制最高级随从)");
                remain -= 1;
            }
            var sorted = cardScores.Where(s => s.Index >= 0 && s.Index < state.ShopMinions.Count)
                .OrderByDescending(s => s.Score).ToList();
            var rules = state.EffectiveRules ?? Engine.EffectiveGameRules.Default;
            // P2 2026-08(用户实测: 迷诱咒术师应先买再发动偷高攻随从, UI未推荐最佳拿法):
            // 商店发动卡联动——买得起(买费)且剩余铸币够发动费时, 优先推荐"买X→发动X"链。
            // 仅限高价值发动效果(偷取/发现/获取), 普通 buff 类不抢链位(卡标签表达)。
            if (chain.Count == 0 && _season14RulesEvaluator != null
                && state.ShopMinions != null)
            {
                for (int ai = 0; ai < state.ShopMinions.Count && chain.Count == 0; ai++)
                {
                    var shopCard = state.ShopMinions[ai];
                    if (shopCard == null || string.IsNullOrEmpty(shopCard.CardId)) continue;
                    Engine.ActivateFact act;
                    if (!_season14RulesEvaluator.TryGetActivate(shopCard.CardId, out act)) continue;
                    int buyCost = 3;
                    try { buyCost = Engine.GameRuleEvaluator.GetPurchaseCost(state, shopCard, state.HeroCardId, rules); } catch { }
                    if (buyCost <= 0 || buyCost > remain) continue;
                    if (act.ActivateCost <= 0 || act.ActivateCost > remain - buyCost) continue;
                    string actText = (act.TextZhCn ?? "") + (act.TextEnUs ?? "");
                    bool highValue = actText.Contains("偷取") || actText.Contains("Steal")
                        || actText.Contains("发现") || actText.Contains("Discover")
                        || actText.Contains("获取") || actText.Contains("gain")
                        || actText.Contains("香蕉") || actText.Contains("随机施放");
                    if (!highValue) continue;
                    chain.Add("买" + CardNameShort(shopCard) + "→发动(偷)");
                    remain -= buyCost + act.ActivateCost;
                }
            }
            // 1.2.2 牛头人T7反馈: 升本优先 — gold 够升本时应优先建议升本(链首), 而非先买随从
            // 花光钱导致末段 remain<upCost 不显示升本(用户 gold9 够升4本+技能, 算法却推买2张)。
            // 特例: 商店有可立即凑成的三连(CompletesGolden)时先买(与引擎 R-triple-timing 一致)。
            int earlyUpCost = state.TavernUpgradeCost >= 0 ? state.TavernUpgradeCost : 999;
            if (chain.Count == 0 && state.TavernTier < 6 && earlyUpCost != int.MaxValue
                && remain >= earlyUpCost)
            {
                bool shopHasImmediateTriple = state.ShopMinions != null
                    && state.ShopMinions.Any(sc => !sc.IsSpell
                        && Engine.TripleRuleEvaluator.CompletesGolden(state, sc.CardId, rules));
                if (!shopHasImmediateTriple)
                {
                    chain.Add("升" + (state.TavernTier + 1) + "本");
                    remain -= earlyUpCost;
                }
            }
            // R221: 两段式——先随从(场面优先), 预算剩余才法术
            foreach (var sc in sorted.Where(s => !s.IsSpell))
            {
                if (chain.Count >= 2) break; // R225: 买随从最多1个(其余由卡标签表达), 给卖弱换强/升级留空间
                var shopCard = state.ShopMinions[sc.Index];
                if (shopCard == null) continue;
                int cost = 3;
                try { cost = Engine.GameRuleEvaluator.GetPurchaseCost(state, shopCard, state.HeroCardId, rules); } catch { }
                if (cost <= 0 || cost > remain) continue;
                chain.Add("买" + CardNameShort(shopCard));
                remain -= cost;
            }
            // R240(第一性原理-玩家运营链): 卖弱换强——玩家每回合核心是"买→打→卖→再买"循环,
            // 算法原链只买+升, 缺卖环节(偏差实测: 玩家每回合1-3次卖牌换强)。
            // 条件: 板面≥4张 + 店有显著更强牌 + 预算够(卖1张得1金+买3金)。
            if (chain.Count > 0 && state.BoardMinions != null && state.BoardMinions.Count >= 4) {
                int weakIdx = FindWeakestBoardMinionLocal(state, null);
                if (weakIdx >= 0 && state.BoardMinions[weakIdx] != null) {
                    var weak = state.BoardMinions[weakIdx];
                    var bestShopList = sorted.Where(s => !s.IsSpell && s.Index >= 0 && s.Index < state.ShopMinions.Count).ToList();
                    if (bestShopList.Count > 0) {
                        var bestShop = bestShopList[0];
                        var strong = state.ShopMinions[bestShop.Index];
                        int strongCost = 3;
                        try { strongCost = Engine.GameRuleEvaluator.GetPurchaseCost(state, strong, state.HeroCardId, rules); } catch { }
                        bool weakProtected = IsProtectedFromSell(state, weak);
                        bool strongBetter = strong.Tier > weak.Tier || (strong.Attack + strong.Health) > (weak.Attack + weak.Health) * 1.3;
                        if (!weakProtected && strongBetter && remain + 1 >= strongCost && chain.Count < 3) {
                            chain.Add("卖" + CardNameShort(weak) + "→买" + CardNameShort(strong));
                            remain = remain + 1 - strongCost;
                        }
                    }
                }
            }
            // 法术段: 随从买完后剩余才考虑; 费用用真实值,提取失败跳过(R222 修复暴风城之力2费被当1费)
            foreach (var sc in sorted.Where(s => s.IsSpell))
            {
                if (chain.Count >= 3) break;
                var shopCard = state.ShopMinions[sc.Index];
                if (shopCard == null) continue;
                int cost = -1;
                try { cost = Engine.GameRuleEvaluator.GetPurchaseCost(state, shopCard, state.HeroCardId, rules); } catch { cost = -1; }
                if (cost <= 0 || cost == int.MaxValue || cost > remain) continue;
                chain.Add("买法术" + CardNameShort(shopCard));
                remain -= cost;
            }
            int upCost = state.TavernUpgradeCost >= 0 ? state.TavernUpgradeCost : 999;
            if (upCost != int.MaxValue && remain >= upCost && state.TavernTier < 6)
                chain.Add("升" + (state.TavernTier + 1) + "本");
            if (chain.Count == 0) return "";
            // R225: 动作链单行紧凑(用→连接) — 少而精准, 买牌最多1个(卡标签表达), 卖/升优先
            // P6 2026-08: 限制单行显示宽度(约16个汉字/行), 超出在"→"边界断行——避免单行长链横跨屏幕。
            // 1.2.3 辛达苟萨反馈: "买A→买B" 的箭头被理解为"指向/派生", 实际是预算内顺序执行。
            // 序列项用中文顿号分隔(买A、买B), "→" 仅保留在单项内部表示真派生(卖X→买Y / 买X→发动)。
            string joined = string.Join("、", chain);
            const int maxLineWidth = 32; // 显示宽度单位: 中文≈2, ASCII≈1 (16汉字≈32)
            if (DisplayWidth(joined) <= maxLineWidth) return joined;
            var segs = new List<string>();
            string cur = "";
            foreach (var seg in chain)
            {
                string piece = cur.Length == 0 ? seg : cur + "、" + seg;
                if (cur.Length > 0 && DisplayWidth(piece) > maxLineWidth)
                {
                    segs.Add(cur);
                    cur = seg;
                }
                else cur = piece;
            }
            if (cur.Length > 0) segs.Add(cur);
            return string.Join("\n", segs);

        }
        // P6 2026-08: 显示宽度统计(中文/全角≈2, 其他≈1), 用于动作链按字数断行
        private static int DisplayWidth(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int w = 0;
            foreach (char ch in s)
            {
                if (ch >= 0x2E80 && ch <= 0x9FFF) w += 2; // CJK 统一表意 + 扩展
                else if (ch == '→' || ch == '—' || ch == '·') w += 2;
                else w += 1;
            }
            return w;
        }

        // R240: 找板面最弱随从(插件自有, 攻防总和最小; 引擎版是私有方法不可达)
        private int FindWeakestBoardMinionLocal(GameState state, object _)
        {
            if (state == null || state.BoardMinions == null || state.BoardMinions.Count == 0) return -1;
            int weakest = -1;
            double minVal = double.MaxValue;
            for (int i = 0; i < state.BoardMinions.Count; i++) {
                var m = state.BoardMinions[i];
                if (m == null) continue;
                double v = (m.Attack + m.Health);
                if (m.Tier <= 2) v *= 0.6;
                if (v < minVal) { minVal = v; weakest = i; }
            }
            return weakest;
        }

        // R240: 卖弱换强保护——核心卡/对子/成长卡不卖(防误卖, 与 R231 记忆保护一致)
        private bool IsProtectedFromSell(GameState state, Engine.MinionData minion)
        {
            if (state == null || minion == null) return true;
            if (minion.Golden) return true;
            try {
                if (_engine != null) {
                    var snap = _engine.StrategicMemorySnapshot;
                    if (snap != null && !string.IsNullOrEmpty(minion.CardId)) {
                        string baseId = minion.CardId.EndsWith("_G", StringComparison.Ordinal)
                            ? minion.CardId.Substring(0, minion.CardId.Length - 2) : minion.CardId;
                        if (snap.ObservedCoreCardIds.Contains(baseId)) return true;
                        if (snap.ObservedPairCardIds.Contains(baseId)) return true;
                    }
                }
            } catch { }
            // 1.2.2 feedbackD: 手牌召唤引擎保护(与引擎层 IsProtectedSellMinion 一致)
            if (minion.CardId != null
                && (minion.CardId == "BG34_140" || minion.CardId == "BG34_140_G")
                && state.HandMinions != null
                && state.HandMinions.Any(h => h != null && !h.IsSpell))
                return true;
            if (minion.DivineShield || minion.Poisonous || minion.Venomous
                || minion.Reborn || minion.Windfury) return true;
            if (minion.Tier >= 5) return true;
            return false;
        }

        // R219: 商店签名(商店卡列表 hash, 用于计划失效判断)
        private string ShopSignature(GameState state)
        {
            if (state == null || state.ShopMinions == null) return "";
            var sb = new System.Text.StringBuilder();
            foreach (var m in state.ShopMinions) { if (m != null) sb.Append(m.CardId).Append(','); }
            return sb.ToString();
        }

        private List<ShopCardScore> FilterAffordableShopScores(GameState state, List<ShopCardScore> scores)
        {
            if (scores == null || scores.Count == 0) return scores ?? new List<ShopCardScore>();
            var filtered = new List<ShopCardScore>();
            foreach (var score in scores)
            {
                int liveGold = GetLiveGold(state);
                if (IsShopScoreAffordable(state, score, liveGold)
                    || IsShopScoreAffordableAfterSell(state, score, liveGold))
                    filtered.Add(score);
            }
            return filtered;
        }

        private bool IsShopActionAffordable(GameState state, GameAction action, int gold)
        {
            if (state == null || action == null || state.ShopMinions == null) return false;
            if (action.TargetIndex < 0 || action.TargetIndex >= state.ShopMinions.Count) return false;
            var shopCard = state.ShopMinions[action.TargetIndex];
            return IsShopCardAffordable(state, shopCard, gold);
        }

        private bool IsShopScoreAffordable(GameState state, ShopCardScore score, int gold)
        {
            if (state == null || state.ShopMinions == null) return false;
            Engine.MinionData shopCard = null;
            if (score.Index >= 0 && score.Index < state.ShopMinions.Count)
                shopCard = state.ShopMinions[score.Index];
            if (shopCard == null && score.EntityId > 0)
                shopCard = state.ShopMinions.FirstOrDefault(m => m != null && m.EntityId == score.EntityId);
            if (shopCard == null) return false;
            return IsShopCardAffordable(state, shopCard, gold);
        }

        private bool IsShopScoreAffordableAfterSell(GameState state, ShopCardScore score, int gold)
        {
            if (state == null || state.ShopMinions == null || !HasPotentialSellAfterTurn(state))
                return false;
            Engine.MinionData shopCard = null;
            if (score.Index >= 0 && score.Index < state.ShopMinions.Count)
                shopCard = state.ShopMinions[score.Index];
            if (shopCard == null && score.EntityId > 0)
                shopCard = state.ShopMinions.FirstOrDefault(m => m != null && m.EntityId == score.EntityId);
            if (shopCard == null) return false;
            int cost = GetShopCardCost(state, shopCard);
            if (gold >= cost) return true;
            if (cost > gold + 1) return false;

            int sellIndex = FindFilterSellCandidateIndex(state);
            return sellIndex >= 0
                && IsShopScoreWorthAfterSell(state, score, shopCard, cost, gold, sellIndex);
        }

        private int FindFilterSellCandidateIndex(GameState state)
        {
            if (state == null || state.BoardMinions == null)
                return -1;

            int worstIdx = -1;
            double worstScore = double.MaxValue;
            for (int i = 0; i < state.BoardMinions.Count; i++)
            {
                var m = state.BoardMinions[i];
                if (m == null || m.Golden) continue;
                if (state.RecentlyBoughtEntityIds != null
                    && state.RecentlyBoughtEntityIds.Contains(m.EntityId))
                    continue;
                if (IsFilterProtectedSellMinion(state, m)) continue;

                double score = m.Tier * 8.0 + m.Attack * 0.5 + m.Health * 0.3;
                if (score < worstScore)
                {
                    worstScore = score;
                    worstIdx = i;
                }
            }
            return worstIdx;
        }

        private bool IsFilterProtectedSellMinion(GameState state, Engine.MinionData m)
        {
            if (state == null || m == null) return true;

            var mainline = _engine != null ? _engine.LastMainlinePlan : null;
            if (mainline != null && mainline.CoreCardIds != null)
            {
                if (mainline.CoreCardIds.Contains(m.CardId)) return true;
                if (!string.IsNullOrEmpty(m.CardId)
                    && m.CardId.EndsWith("_G", StringComparison.Ordinal)
                    && mainline.CoreCardIds.Contains(
                        m.CardId.Substring(0, m.CardId.Length - 2)))
                    return true;
            }

            if (_engine != null
                && (_engine.IsCoreComboCard(m.CardId)
                    || _engine.IsResourceCard(m.CardId)))
                return true;
            if (OpponentTechAdvisor.IsSituationalTechCard(m.CardId)
                && OpponentTechAdvisor.IsTechCardWorthKeeping(
                    m.CardId, state.Opponents))
                return true;
            if (m.Tier >= 4 && (m.DivineShield || m.Poisonous
                || m.Venomous || m.Reborn))
                return true;
            if (m.Tier >= 3 && (m.DivineShield || m.Poisonous || m.Venomous))
                return true;
            if (m.Taunt)
            {
                int tauntCount = state.BoardMinions == null
                    ? 0
                    : state.BoardMinions.Count(other => other != null && other.Taunt);
                if (tauntCount <= 1) return true;
            }
            return false;
        }

        private bool IsShopScoreWorthAfterSell(
            GameState state,
            ShopCardScore score,
            Engine.MinionData shopCard,
            int cost,
            int gold,
            int sellIndex)
        {
            if (state == null || state.Turn < 3 || shopCard == null
                || sellIndex < 0 || sellIndex >= state.BoardMinions.Count)
                return false;
            var sold = state.BoardMinions[sellIndex];
            if (sold == null || sold.Golden) return false;

            var rules = state.EffectiveRules ?? Engine.EffectiveGameRules.Default;
            if (score.IsTriple
                || TripleRuleEvaluator.CompletesGolden(state, shopCard.CardId, rules))
                return true;
            if (OpponentTechAdvisor.IsSituationalTechCard(shopCard.CardId)
                && OpponentTechAdvisor.IsTechCardWorthKeeping(
                    shopCard.CardId, state.Opponents))
                return true;

            if (shopCard.IsSpell)
                return _engine != null
                    && (_engine.IsResourceCard(shopCard.CardId)
                        || _engine.IsCoreComboCard(shopCard.CardId));

            bool resourceOrCore = _engine != null
                && (_engine.IsResourceCard(shopCard.CardId)
                    || _engine.IsCoreComboCard(shopCard.CardId));
            resourceOrCore = resourceOrCore || score.IsCoreCombo
                || score.EconomyValue >= 0.35f
                || score.PrimaryRole == CardClassifier.CardRole.Economy
                || score.PrimaryRole == CardClassifier.CardRole.ValueGenerator
                || score.PrimaryRole == CardClassifier.CardRole.SpellSource
                || score.PrimaryRole == CardClassifier.CardRole.SpellCopier;

            double soldPower = _fe != null
                ? _fe.ComputeBoardPower(new List<Engine.MinionData> { sold })
                : 0;
            double shopPower = _fe != null
                ? _fe.ComputeBoardPower(new List<Engine.MinionData> { shopCard })
                : 0;
            if (resourceOrCore)
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
            double threshold = shopCard.Tier >= sold.Tier ? 1.20 : 1.50;
            return shopPower >= soldPower * threshold;
        }

        private bool IsShopMarkerAffordable(GameState state, Engine.ShopMarker marker, int gold)
        {
            if (state == null || marker == null || state.ShopMinions == null) return false;
            Engine.MinionData shopCard = null;
            if (marker.Index >= 0 && marker.Index < state.ShopMinions.Count)
                shopCard = state.ShopMinions[marker.Index];
            if (shopCard == null && marker.EntityId > 0)
                shopCard = state.ShopMinions.FirstOrDefault(m => m != null && m.EntityId == marker.EntityId);
            if (shopCard == null) return false;
            return IsShopCardAffordable(state, shopCard, gold);
        }

        private bool IsShopMarkerAffordableAfterSell(GameState state, Engine.ShopMarker marker, int gold)
        {
            if (state == null || marker == null || state.ShopMinions == null) return false;
            Engine.MinionData shopCard = null;
            if (marker.Index >= 0 && marker.Index < state.ShopMinions.Count)
                shopCard = state.ShopMinions[marker.Index];
            if (shopCard == null && marker.EntityId > 0)
                shopCard = state.ShopMinions.FirstOrDefault(m => m != null && m.EntityId == marker.EntityId);
            if (shopCard == null) return false;
            int cost = GetShopCardCost(state, shopCard);
            return cost <= gold + 1 && HasSellableBoardMinion(state);
        }

        private bool IsShopMarkerWorthAfterSell(
            GameState state,
            Engine.ShopMarker marker,
            int gold,
            int sellIndex)
        {
            if (state == null || marker == null || state.Turn < 3) return false;
            if (marker.Level != DecisionLevel.Critical
                && marker.Level != DecisionLevel.Major)
                return false;
            if (!IsShopMarkerAffordableAfterSell(state, marker, gold)) return false;
            if (state.BoardMinions == null || sellIndex < 0
                || sellIndex >= state.BoardMinions.Count)
                return false;
            var sold = state.BoardMinions[sellIndex];
            if (sold == null || sold.Golden) return false;
            if (state.RecentlyBoughtEntityIds != null
                && state.RecentlyBoughtEntityIds.Contains(sold.EntityId))
                return false;

            Engine.MinionData shop = null;
            if (marker.Index >= 0 && marker.Index < state.ShopMinions.Count)
                shop = state.ShopMinions[marker.Index];
            if (shop == null && marker.EntityId > 0)
                shop = state.ShopMinions.FirstOrDefault(
                    card => card != null && card.EntityId == marker.EntityId);
            if (shop == null) return false;

            var rules = state.EffectiveRules ?? Engine.EffectiveGameRules.Default;
            if (marker.IsTriple
                || TripleRuleEvaluator.CompletesGolden(state, shop.CardId, rules))
                return true;

            bool resourceOrCore = _engine != null
                && (_engine.IsResourceCard(shop.CardId)
                    || _engine.IsCoreComboCard(shop.CardId));
            if (shop.IsSpell) return resourceOrCore;

            double soldPower = _fe != null
                ? _fe.ComputeBoardPower(new List<Engine.MinionData> { sold })
                : 0;
            double shopPower = _fe != null
                ? _fe.ComputeBoardPower(new List<Engine.MinionData> { shop })
                : 0;
            if (resourceOrCore)
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

        private bool IsShopCardAffordable(GameState state, Engine.MinionData shopCard, int gold)
        {
            if (state == null || shopCard == null) return false;
            int cost = Engine.GameRuleEvaluator.GetPurchaseCost(
                state, shopCard, state.HeroCardId,
                state.EffectiveRules ?? EffectiveGameRules.Default);
            return gold >= cost;
        }

        private void PruneShopMarkersByLiveGold(
            GameState state, VisualPlan plan, int gold)
        {
            if (state == null || plan == null || plan.ShopMarkers == null) return;
            if (plan.ShopMarkers.Count == 0) return;
            var sellIndexes = plan.SellMarkers != null
                ? plan.SellMarkers
                    .Where(marker => marker != null && marker.BoardIndex >= 0)
                    .Select(marker => marker.BoardIndex)
                    .ToList()
                : new List<int>();
            plan.ShopMarkers.RemoveAll(marker =>
            {
                if (marker == null) return true;
                var card = ResolveShopMarkerCard(state, marker);
                if (card == null) return true;
                int cost = GetShopCardCost(state, card);
                if (gold >= cost) return false;
                if (state.Turn < 3 || cost > gold + 1 || sellIndexes.Count == 0)
                    return true;
                foreach (var sellIndex in sellIndexes)
                {
                    if (IsShopMarkerWorthAfterSell(state, marker, gold, sellIndex))
                        return false;
                }
                return true;
            });
            // 1.2.2 feedbackA: 动态铸币总量约束 — marker 不应超过当前铸币能负担的张数(+卖牌换强1张)。
            // T1 3金同时高亮2个3金购买marker会误导"两个购买建议"(用户实测)。
            if (plan.ShopMarkers.Count > 1)
            {
                bool canSellForMore = sellIndexes.Count > 0
                    && state.BoardMinions != null
                    && state.BoardMinions.Count > 0;
                // 预算: 每买一张扣其费用; 卖牌可+1张(卖1得1金近似, 买3金卡需差额)。
                // 简单稳妥: 仅按"首张买后剩余金还能负担几张同价随从"截断, 不精确模拟卖牌。
                var affordable = plan.ShopMarkers
                    .OrderByDescending(m => m.Score)
                    .ToList();
                int remaining = gold;
                int keep = 0;
                bool first = true;
                foreach (var m in affordable)
                {
                    var c2 = ResolveShopMarkerCard(state, m);
                    if (c2 == null) { keep++; continue; }
                    int c = GetShopCardCost(state, c2);
                    if (first) { if (remaining >= c) { remaining -= c; keep++; } first = false; continue; }
                    if (remaining >= c) { remaining -= c; keep++; }
                    else if (canSellForMore && keep < 2) { keep++; } // 卖弱换强最多允许多1张
                    else break;
                }
                if (keep < plan.ShopMarkers.Count)
                {
                    plan.ShopMarkers = new List<Engine.ShopMarker>(
                        affordable.Take(keep));
                }
            }
            if (plan.ShopMarkers.Count == 0
                && plan.UpgradeHint == null
                && plan.HandMarker == null
                && plan.SellMarkers != null)
            {
                plan.SellMarkers.Clear();
            }
            if (plan.ShopMarkers.Count == 0
                && (plan.RecommendedActionType == "BuyMinion"
                    || plan.RecommendedActionType == "BuySpell"))
            {
                plan.RecommendedActionType = "";
            }
        }

        private int GetMinionBuyCost(GameState state)
        {
            if (state == null) return 3;
            return Engine.GameRuleEvaluator.GetPurchaseCost(
                state, new MinionData { Tier = 1 }, state.HeroCardId,
                state.EffectiveRules ?? EffectiveGameRules.Default);
        }

        private bool CanRefreshLeaveMeaningfulBudget(GameState state, int gold)
        {
            if (state == null) return false;
            if (_engine != null) return _engine.CanRefreshLeaveMeaningfulBudget(state);
            var rules = state.EffectiveRules ?? Engine.EffectiveGameRules.Default;
            int refreshCost = GameRuleEvaluator.GetRefreshCost(
                state, state.HeroCardId, rules);
            if (refreshCost <= 0 || state.FreeRefreshCount > 0) return true;
            int remaining = gold - refreshCost;
            if (remaining < 0) return false;
            if (remaining >= GetMinionBuyCost(state)) return true;
            if (state.TavernUpgradeCost >= 0
                && remaining >= state.TavernUpgradeCost)
                return true;
            if (state.ShopMinions != null)
            {
                foreach (var shop in state.ShopMinions)
                {
                    if (shop == null) continue;
                    if (remaining >= GetShopCardCost(state, shop))
                        return true;
                }
            }
            return false;
        }

        private void DispatchRender(GameState state, string heroHint, VisualPlan plan, string heroUseSuggestion = null)
        {
            if (state == null || plan == null) return;
            int layoutTier = _shopLayoutSlotPolicy.LastRefreshTier;
            int tierSlots = state.GetShopSlotCountForTier(layoutTier);
            var layoutDecision = _shopLayoutSlotPolicy.Resolve(
                tierSlots,
                state.ShopMinions.Select(minion => minion.Position),
                state.ReplenishingShopActive);
            _shopLayoutDecision = layoutDecision;
            // 防重复渲染: plan内容未变化时跳过
            // 包含ShopMarkers的EntityId+ShopPosition，确保位置变化触发重绘
            var markerIds = plan.ShopMarkers.Count > 0
                ? "|M" + string.Join(",", plan.ShopMarkers.Select(m => m.EntityId + ":" + m.ShopPosition))
                : "";
            var recommendationPlan = Engine.RecommendationPlan.FromVisualPlan(
                plan, state.LiveGold >= 0 ? state.LiveGold : state.Gold, state.Turn);
            var recommendationErrors = recommendationPlan.Validate();
            if (recommendationErrors.Count > 0)
            {
                Log("RecommendationPlan consistency: "
                    + string.Join("; ", recommendationErrors));
            }
            plan.Recommendation = recommendationPlan;
            string planHash = state.Turn + "|" + plan.RecommendedActionType + "|"
                + plan.ShopMarkers.Count + markerIds + "|" + (plan.UpgradeHint?.Level.ToString() ?? "-") + "|"
                + (plan.HandMarker?.CardName ?? "-") + "|" + (plan.TrinketHints?.Count ?? 0) + "|"
                + (plan.FreezeHint?.Active == true ? "F" : "-") + "|"
                + (plan.Status.HintLine ?? "") + "|"
                + (plan.Status.UpgradeLine ?? "") + "|"
                + (plan.Status.PickLine ?? "") + "|"
                + (plan.Status.CombatLine ?? "") + "|"
                + plan.SellMarkers.Count
                + (plan.SellMarkers.Count > 0
                    ? ":" + string.Join(",", plan.SellMarkers.Select(m => m.BoardIndex))
                    : "") + "|"
                + (int)_trinketPanelState.Phase + "_" + (int)_discoverPanelState.Phase
                // 0711: 连续选择可能在一次 EvaluateAndRender 间隔内完成旧批次并开启新批次。
                // 此时面板 Phase/候选数均不变；批次身份必须进入 hash，否则第二批提示被去重吞掉。
                + "|" + (_trinketTargetState.State.BatchId ?? "-")
                + "|" + (_discoverTargetState.State.BatchId ?? "-")
                + "|L" + layoutDecision.LayoutCount + (layoutDecision.UseDenseSlots ? "D" : "R")
                // 07062242: 金币归零/恢复边界必须触发重绘 — gold=0后若plan其余不变则不dispatch,
                // 旧刷新光晕残留(T8末段gold=0仍见"找核心牌"提示的根因)
                + "|" + (state.LiveGold >= 0 ? "G" + state.LiveGold : "G-")
                + "|" + (state.Gold < 1 && state.FreeRefreshCount == 0 ? "G0" : "G+");
            if (planHash == _lastRenderedPlanHash) return;
            _lastRenderedPlanHash = planHash;
            // 战斗阶段禁止渲染。
            // 07062107修复: hash已在上方记录, 此处return意味着本plan从未上屏 —
            // 必须清hash让下一帧同plan重试, 否则HDT combat标志翻转滞后期间(回合开始~1.5s)
            // 产生的建议(如T2升本LEVEL_UP)会被hash去重永久吞掉。
            bool combatNow = false;
            try { combatNow = Core.Game.IsBattlegroundsCombatPhase; } catch { }
            if (combatNow) { _lastRenderedPlanHash = null; try { _renderer.Clear(); } catch { } return; }
            // 过渡期安全网: 商店实体已消失+非饰品/英雄选择→即将进战斗, 跳过渲染
            if (state.ShopMinions.Count == 0 && state.BoardMinions.Count == 0
                && state.TrinketOffer.Count == 0 && state.HeroOptions.Count == 0
                && state.Turn > 1)
            { _lastRenderedPlanHash = null; return; }

            // 修复: 不在此处同步Clear, 避免清除→重绘间的空白闪烁。
            // Dispatcher回调内的Clear(第1249行)在重绘前瞬间执行, 无可见闪烁。
            _pendingRenderState = state;
            _pendingRenderPlan = plan;
            _pendingRenderHeroHint = heroHint;
            _pendingRenderHeroUseSuggestion = heroUseSuggestion;
            _renderVersion++;
            int capturedVersion = _renderVersion;

            var app = Application.Current;
            if (app == null) return;

            bool isWaiting = plan.ShopMarkers.Count == 0 && plan.UpgradeHint == null
                && plan.SellMarkers.Count == 0;
            var status = plan.Status;

            app.Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
            {
                try
                {
                    // 现场重读金币: Dispatcher回调可能延迟执行, state.Gold已过时
                    int liveGold = GetLiveGold(state);
                    bool inCombat = false;
                    try { inCombat = Core.Game.IsBattlegroundsCombatPhase; } catch { }
                    // 07062107修复: 回调被拦截=本plan未上屏, 清hash让同plan下帧重试(见DispatchRender头部注释)
                    if (inCombat) { _lastRenderedPlanHash = null; return; }
                    if (capturedVersion != _renderVersion)
                    {
                        // 1.2.1 feedback4: 版本过期=回调排队期间又有更高版本dispatch。
                        // 原实现直接丢弃, 高频变化(辐光护手补牌/连买)下UI饿死不刷新。
                        // 合并策略: 渲染最新pending快照(非丢弃), 保证UI最终收敛到最新状态。
                        if (_pendingRenderState != null && _pendingRenderPlan != null
                            && !ReferenceEquals(_pendingRenderState, state))
                        {
                            state = _pendingRenderState;
                            plan = _pendingRenderPlan;
                            heroHint = _pendingRenderHeroHint ?? heroHint;
                            heroUseSuggestion = _pendingRenderHeroUseSuggestion ?? heroUseSuggestion;
                            liveGold = GetLiveGold(state);
                            _lastRenderedPlanHash = null;
                        }
                        else
                        {
                            _lastRenderedPlanHash = null;
                            return;
                        }
                    }
                    // 商店已空+非特殊界面 → 过渡到战斗, 不渲染(防拔线或异步回调残留)
                    bool shopGone = state.ShopMinions.Count == 0 && state.HeroOptions.Count == 0
                        && state.TrinketOffer.Count == 0 && state.DiscoverOptions.Count == 0;
                    if (shopGone && state.Turn >= 1) { _lastRenderedPlanHash = null; return; }
                    // 清除后重绘: 各Panel自行清理, Clear仅清除非面板元素(status/shop等)
                    try { _renderer.ClearNonPanel(); } catch { }

                    bool specialScreen = (state.ShopMinions.Count == 0 && state.Turn > 2)
                        || (state.ShopMinions.Count == 0 && state.HeroOptions.Count > 0)
                        || (state.ShopMinions.Count == 0 && state.TrinketOffer.Count > 0);

                    // ── 画布尺寸同步 + 位置诊断（每局首次）──
                    _renderer.RefreshCanvasSize();
                    _renderer.LogPositions();

                    // ── 英雄提示 ──
                    // 英雄提示：技能建议每回合仅首次显示，避免每次操作都触发
                    string useSug = null;
                    if (!string.IsNullOrEmpty(heroUseSuggestion) && _lastHeroHintTurn != state.Turn)
                    {
                        useSug = heroUseSuggestion;
                        _lastHeroHintTurn = state.Turn;
                    }
                    // 英雄技能提示: 绑定当前HP实体, 换技能自动跟随
                    var hpIdForType = !string.IsNullOrEmpty(state.HeroPowerCardId)
                        ? state.HeroPowerCardId : state.HeroCardId;
                    var hpStrat = _heroPowerEngine.GetStrategyForPower(state.HeroCardId, hpIdForType);
                    // 如果HP实体ID查不到策略(新技能), 用英雄本体ID回退
                    if (hpStrat == null)
                        hpStrat = _heroPowerEngine.GetStrategyForPower(
                            state.HeroCardId, state.HeroPowerCardId);
                    bool isActiveHP = hpStrat != null && hpStrat.PowerType == HeroPowerType.Active;
                    bool canAffordHP = liveGold >= state.HeroPowerCost;
                    bool hpNotUsed = !state.HeroPowerExhausted;
                    bool hasUseSuggestion = !string.IsNullOrEmpty(useSug);
                    // 技能熄灭卫士: EXHAUSTED/费用不够/被动技能→清光晕
                    bool hpShouldGlow = isActiveHP && hasUseSuggestion && canAffordHP && hpNotUsed;
                    if (hpShouldGlow)
                    {
                        // 兼容字段明确映射主技能，不再通过卡牌ID后缀猜角色。
                        string hpLabel = state.HasSecondHeroPower ? "主技能" : null;
                        _renderer.ShowHeroHint(heroHint ?? "", useSug ?? "", useSug, hpLabel);
                    }
                    else
                        _renderer.ClearHeroGlow();

                    // 手牌高亮已静默(v3.0): 保留状态栏"打"文字, 移除卡牌叠加层
                    // _renderer.ShowHandMarker / ClearHandMarker 不再调用

                    // ── 目标脉冲: 已静默禁用 (v2.72) ──
                    // 指向推荐算法精度不足, 经常选错目标, 保留代码待后续改进后重新启用
                    // if (plan.TargetHints != null && plan.TargetHints.Count > 0) { ... }
                    _renderer.ClearTargetPulse();

                    // ── 饰品推荐提示: PanelState 状态机驱动(只读 IsVisible, 不写回) ──
                    // 状态机在 EvaluateAndRender 每帧 Advance, Expired 自闭环回 Idle, 此处不再写状态机
                    // (BeginInvoke 回调延迟执行, 写回会 stale-clobber 新一帧已推进的状态)。
                    var trinketPs = _trinketPanelState;
                    bool trinketOfferExists = state.TrinketOffer != null && state.TrinketOffer.Count > 0;
                    bool trinketShouldShow = trinketPs.IsVisible || (trinketOfferExists && trinketPs.Phase == PanelPhase.Idle && state.Turn != _lastTrinketHideTurn);
                    bool trinketShouldDisplay = TrinketRecommendationsVisible && trinketShouldShow;
                    bool hasTrinketHints = plan.TrinketHints != null && plan.TrinketHints.Count > 0;

                    if (trinketShouldDisplay != _lastTrinketShowState)
                    {
                        Log(string.Format("UI Trinket: {0} (phase={1} offer={2} hints={3})",
                            trinketShouldDisplay ? "SHOW" : "HIDE", trinketPs.Phase,
                            state.TrinketOffer?.Count ?? 0, hasTrinketHints));
                        _lastTrinketShowState = trinketShouldDisplay;
                    }
                    if (trinketShouldDisplay)
                    {
                        if (HasScheduledTrinketPlaceholder(state))
                            _renderer.ShowTrinketLoading(state.Turn == 6 ? "小饰品候选读取中" : "大饰品候选读取中");
                        else if (hasTrinketHints)
                            _renderer.ShowTrinketHints(plan.TrinketHints);
                    }
                    else
                    {
                        _lastTrinketHideTurn = state.Turn;
                        _renderer.ClearTrinketHints();
                    }

                    // ── 发现渲染: 状态机单一权威(_discoverPanelActive 已折入 Advance 输入) ──
                    var discoverPs = _discoverPanelState;
                    var authoritativeDiscover = _discoverPanelActive
                        ? _discoverBatchFromLog : null;
                    bool hasAuthoritativeDiscover = authoritativeDiscover != null
                        && authoritativeDiscover.ChoiceId >= 0
                        && authoritativeDiscover.Candidates != null
                        && authoritativeDiscover.Candidates.Count >= 2;
                    // 0708: Fading 期间候选可能已清空, 仍显示会让旧推荐(含酒馆招募面板卡)残留。
                    // 消费端必须同时要求当前帧存在 >=2 个候选, 才进入 SHOW/重绘路径。
                    bool discoverShouldShow = hasAuthoritativeDiscover
                        || (discoverPs.IsVisible
                            && (state.DiscoverOptions?.Count ?? 0) >= 2);

                    if (discoverShouldShow != _lastDiscoverShowState)
                    {
                        if (hasAuthoritativeDiscover)
                        {
                            Log(string.Format(
                                "UI Discover: SHOW authoritative choice={0} options={1}",
                                authoritativeDiscover.ChoiceId,
                                authoritativeDiscover.Candidates.Count));
                        }
                        else
                        {
                            Log(string.Format("UI Discover: {0} (phase={1} options={2})",
                                discoverShouldShow ? "SHOW" : "HIDE", discoverPs.Phase,
                                state.DiscoverOptions?.Count ?? 0));
                        }
                        _lastDiscoverShowState = discoverShouldShow;
                    }
                    if (hasAuthoritativeDiscover)
                    {
                        var options = authoritativeDiscover.Candidates.Select(candidate =>
                            new Engine.TrinketOption
                            {
                                CardId = candidate.CardId,
                                TrinketName = Engine.DisplayNameResolver.Resolve(
                                    candidate.CardId, candidate.CardName,
                                    Engine.CardNameService.GetName),
                                EntityId = candidate.EntityId,
                                Tribes = ResolveCardTribes(candidate.CardId),
                            }).ToList();
                        var scoringState = new GameState { DiscoverOptions = options };
                        if (_cachedState != null)
                        {
                            scoringState.Turn = _cachedState.Turn;
                            scoringState.Gold = _cachedState.Gold;
                            scoringState.LiveGold = _cachedState.LiveGold;
                            scoringState.Health = _cachedState.Health;
                            scoringState.Armor = _cachedState.Armor;
                            scoringState.BoardMinions = _cachedState.BoardMinions
                                ?? new List<Engine.MinionData>();
                            scoringState.HandMinions = _cachedState.HandMinions
                                ?? new List<Engine.MinionData>();
                            scoringState.CurrentCompGuideTier =
                                _cachedState.CurrentCompGuideTier;
                            scoringState.Opponents = _cachedState.Opponents;
                        }
                        var discoverHints = ScoreDiscoverOptions(scoringState);
                        if (discoverHints.Count == 0)
                        {
                            for (int di = 0; di < options.Count; di++)
                            {
                                var option = options[di];
                                discoverHints.Add(new Engine.TrinketHint
                                {
                                    Index = di,
                                    Name = option.TrinketName ?? option.CardId ?? "?",
                                    Score = 1.0,
                                    Reason = "发现选项",
                                    IsTopPick = di == 0,
                                });
                            }
                        }
                        int statusLines = 1;
                        if (!string.IsNullOrEmpty(plan.Status.CompDir)) statusLines++;
                        if (!string.IsNullOrEmpty(plan.Status.HintLine)) statusLines++;
                        if (!string.IsNullOrEmpty(plan.Status.PickLine)) statusLines++;
                        _renderer.RefreshCanvasSize();
                        _renderer.ShowAuthoritativeDiscoverHints(
                            discoverHints, statusLines);
                    }
                    else if (discoverShouldShow)
                    {
                        var discoverHints = ScoreDiscoverOptions(state);
                        if (discoverHints.Count == 0 && state.DiscoverOptions.Count > 0)
                        {
                            for (int di = 0; di < state.DiscoverOptions.Count; di++)
                            {
                                var d = state.DiscoverOptions[di];
                                discoverHints.Add(new Engine.TrinketHint
                                {
                                    Index = di,
                                    Name = d.TrinketName ?? d.CardId ?? "?",
                                    Score = 1.0,
                                    Reason = "发现选项",
                                    IsTopPick = (di == 0),
                                });
                            }
                        }
                        if (discoverHints.Count > 0)
                        {
                            int statusLines = 1;
                            if (!string.IsNullOrEmpty(plan.Status.CompDir)) statusLines++;
                            if (!string.IsNullOrEmpty(plan.Status.HintLine)) statusLines++;
                            if (!string.IsNullOrEmpty(plan.Status.PickLine)) statusLines++;
                            _renderer.ShowDiscoverHints(discoverHints, statusLines);
                        }
                    }
                    else
                    {
                        _discoverSource = "";
                        _renderer.ClearDiscoverHints();
                    }

                    // 无操作场景：商店空+无发现+无饰品+无手牌推荐，或处于被动选择/无正常招募期
                    bool noActionNeeded = (state.ShopMinions.Count == 0
                        && state.DiscoverOptions.Count == 0
                        && (state.TrinketOffer?.Count ?? 0) == 0
                        && plan.HandMarker == null)
                        || state.PassiveChoiceActive
                        || !state.NormalRecruitAvailable;
                    if (noActionNeeded)
                    {
                        // 清空所有指导文字，只保留状态信息
                        plan.RecommendedActionType = "";
                        plan.Status.HintLine = "";
                        plan.Status.UpgradeLine = "";
                        plan.ShopMarkers.Clear();
                        plan.SellMarkers.Clear();
                        plan.UpgradeHint = null;
                        plan.FreezeHint = null;
                    }

                    // 特殊界面/等待状态：只显示状态条+英雄+手牌，不显示商店策略
                    if (isWaiting || specialScreen)
                    {
                        _renderer.ShowStatusStrip(status);
                        _suggestionsActive = true;
                        return;
                    }

                    // ── 金币不足时仅清除不可支付目标, 保留低费法术和首购免费 ──
                    int minBuyCost = GetMinionBuyCost(state);
                    PruneShopMarkersByLiveGold(state, plan, liveGold);
                    bool sellPath = plan.SellMarkers.Count > 0 && HasSellableBoardMinion(state);
                    bool handWaitingOnFullBoard = sellPath
                        && state.BoardMinions.Count >= state.MaxBoardSlots
                        && state.HandMinions != null
                        && state.HandMinions.Any(hm => hm != null && !hm.IsSpell);
                    bool hasUpgradeAfterSell = plan.UpgradeHint != null
                        && plan.UpgradeHint.Cost > 0
                        && CanAffordAfterOneSell(state, plan.UpgradeHint.Cost);
                    bool canBuyMinion = liveGold >= minBuyCost
                        || (sellPath && plan.ShopMarkers.Count > 0
                            && CanAffordAfterOneSell(state, minBuyCost));
                    bool hasAffordableShopMarker = plan.ShopMarkers.Any(m => IsShopMarkerAffordable(state, m, liveGold))
                        || (sellPath && plan.ShopMarkers.Any(m => IsShopMarkerWorthAfterSell(
                            state, m, liveGold, plan.SellMarkers[0].BoardIndex)));
                    bool canRefresh = liveGold >= 1 || state.FreeRefreshCount > 0;
                    if (!hasAffordableShopMarker)
                    {
                        plan.ShopMarkers.RemoveAll(m => !IsShopMarkerAffordable(state, m, liveGold)
                            && !(sellPath && IsShopMarkerWorthAfterSell(
                                state, m, liveGold, plan.SellMarkers[0].BoardIndex)));
                        plan.HandMarker = null;
                        if (plan.RecommendedActionType == "BuyMinion" || plan.RecommendedActionType == "BuySpell")
                            plan.RecommendedActionType = "";
                        if (!handWaitingOnFullBoard && !hasUpgradeAfterSell)
                            plan.SellMarkers.Clear();
                    }
                    else
                    {
                        plan.ShopMarkers.RemoveAll(m => !IsShopMarkerAffordable(state, m, liveGold)
                            && !(sellPath && IsShopMarkerWorthAfterSell(
                                state, m, liveGold, plan.SellMarkers[0].BoardIndex)));
                    }
                    if (!canRefresh && !hasAffordableShopMarker
                        && !handWaitingOnFullBoard && !hasUpgradeAfterSell)
                    {
                        // 既买不起也刷不起 → 清空所有指导, 只留冻结(如有价值)
                        plan.ShopMarkers.Clear();
                        plan.HandMarker = null;
                        plan.UpgradeHint = null;
                        plan.SellMarkers.Clear();
                        plan.Status.HintLine = state.FrozenShop ? "鲍勃的酒馆" : "";
                        plan.RecommendedActionType = state.FrozenShop ? "FreezeShop" : "";
                    }

                    // ── 铸币分配: 升本优先但需预算检查 ──
                    // 优先级: 升本 > 手牌打出 > 买牌 > 冻结 > 卖牌 > 刷新
                    bool shownPrimary = false;
                    int upgradeCost = plan.UpgradeHint != null ? plan.UpgradeHint.Cost : int.MaxValue;
                    bool levelOkViaSell = plan.UpgradeHint != null
                        && liveGold < upgradeCost
                        && sellPath
                        && CanAffordAfterOneSell(state, upgradeCost);
                    bool levelUrgent = plan.UpgradeHint != null
                        && plan.UpgradeHint.Level == DecisionLevel.Critical
                        && liveGold >= plan.UpgradeHint.Cost;
                    bool levelOk = plan.UpgradeHint != null
                        && (liveGold >= upgradeCost || levelOkViaSell);
                    int goldAfterUpgrade = levelOk
                        ? liveGold + (levelOkViaSell ? 1 : 0) - upgradeCost
                        : liveGold;
                    bool canBuyAfterUpgrade = goldAfterUpgrade >= 3;
                    bool hasBuyThenUpgrade = plan.UpgradeHint != null
                        && plan.UpgradeHint.Cost > 0
                        && plan.ShopMarkers.Any(m =>
                        {
                            var buyCard = ResolveShopMarkerCard(state, m);
                            if (buyCard == null) return false;
                            int buyCost = GetShopCardCost(state, buyCard);
                            return IsShopMarkerAffordable(state, m, liveGold)
                                && liveGold - buyCost >= plan.UpgradeHint.Cost;
                        });
                    bool upgradePrimary = plan.UpgradeHint != null
                        && plan.UpgradeHint.Cost > 0;
                    if (upgradePrimary && !hasBuyThenUpgrade)
                    {
                        plan.ShopMarkers.Clear();
                        plan.SellMarkers.Clear();
                        plan.HandMarker = null;
                        plan.Status.HintLine = "";
                        plan.RecommendedActionType = "Upgrade";
                    }
                    // 1. 升本提示 — 铸币不足时清除商店标记避免冲突指导
                    // 升级信息仅通过按钮光晕(ShowLevelUpGlow)显示, 状态栏不重复
                    if (!shownPrimary && levelUrgent)
                    {
                        shownPrimary = true;
                        if (!canBuyAfterUpgrade && !hasBuyThenUpgrade)
                        {
                            plan.ShopMarkers.Clear();
                            plan.HandMarker = null;
                            plan.Status.HintLine = "";
                            plan.RecommendedActionType = "Upgrade";
                        }
                    }
                    else if (!shownPrimary && levelOk && (!canBuyAfterUpgrade || plan.ShopMarkers.Count == 0))
                    {
                        shownPrimary = true;
                        if (!canBuyAfterUpgrade && !hasBuyThenUpgrade)
                            plan.ShopMarkers.Clear();
                    }
                    // ── 战斗预测 (独立于动作推荐, 有缓存结果就显示) ──
                    if (_lastCombatPrediction != null && _lastCombatPrediction.SimulationCount > 0)
                    {
                        double w = _lastCombatPrediction.WinRate * 100;
                        double l = _lastCombatPrediction.LossRate * 100;
                        double dmg = _lastCombatPrediction.AvgDamageTaken;
                        if (w + l > 0.01) // 有有效数据
                        {
                            if (w >= 65)
                                plan.Status.CombatLine = string.Format("下轮: 胜{0:F0}% 负{1:F0}% | 预伤{2:F0}",
                                    w, l, dmg);
                            else if (w >= 40)
                                plan.Status.CombatLine = string.Format("下轮: 胜{0:F0}% 负{1:F0}% ⚠预伤{2:F0}",
                                    w, l, dmg);
                            else
                                plan.Status.CombatLine = string.Format("下轮: 胜{0:F0}% 负{1:F0}% 危! 预伤{2:F0}",
                                    w, l, dmg);
                        }
                    }
                    // 2. 手牌打出 (目标: 状态栏"打: XXX"文字)
                    // 不阻断商店标签: 后期常有手牌+买牌双推荐, 两者都显示
                    bool hasHandAction = plan.HandMarker != null;
                    if (!shownPrimary && hasHandAction)
                    {
                        // 仅当无商店推荐时, 手牌作为主推荐
                        if (plan.ShopMarkers.Count == 0)
                        {
                            shownPrimary = true;
                            if (string.IsNullOrEmpty(plan.Status.HintLine))
                                plan.Status.HintLine = "打→" + (plan.HandMarker.CardName ?? "");
                        }
                        // 有商店推荐时: ShopMarkers优先, 手牌作为次要(不清除标签)
                    }
                    // 3. 商店买牌 (保持CreateVisualPlan已过滤的标记, 不额外缩减)
                    if (!shownPrimary && plan.ShopMarkers.Count > 0)
                    {
                        shownPrimary = true;
                        var topCard = plan.ShopMarkers.OrderByDescending(m => m.Score).First();
                        // 2026-08 对抗性审查 L2: 显式守卫 SellMarkers 非空(与 4349 一致),
                        // 消除对"Clear 必同时清 ShopMarkers"隐式不变式的依赖
                        bool sellRequired = sellPath
                            && plan.SellMarkers.Count > 0
                            && !IsShopMarkerAffordable(state, topCard, liveGold)
                            && IsShopMarkerWorthAfterSell(
                                state, topCard, liveGold, plan.SellMarkers[0].BoardIndex);
                        Engine.MinionData topShopCard = null;
                        if (topCard.Index >= 0 && topCard.Index < state.ShopMinions.Count)
                            topShopCard = state.ShopMinions[topCard.Index];
                        else if (topCard.EntityId > 0)
                            topShopCard = state.ShopMinions.FirstOrDefault(
                                m => m != null && m.EntityId == topCard.EntityId);
                        string topName = ResolveDisplayName(
                            topCard.CardName,
                            topShopCard != null ? topShopCard.CardId : "");
                        if (sellRequired
                            && plan.SellMarkers.Count > 0
                            && plan.SellMarkers[0].BoardIndex >= 0
                            && plan.SellMarkers[0].BoardIndex < state.BoardMinions.Count)
                        {
                            var sold = state.BoardMinions[plan.SellMarkers[0].BoardIndex];
                            string soldName = ResolveDisplayName(sold.CardName, sold.CardId);
                            string sellChain = "卖→" + soldName
                                + "→拿→" + topName;
                            plan.Status.HintLine = AppendActionHint(
                                plan.Status.HintLine, sellChain, soldName);
                        }
                        else if (string.IsNullOrEmpty(plan.Status.HintLine)
                            || !plan.Status.HintLine.Contains(topName))
                        {
                            plan.Status.HintLine = AppendActionHint(
                                plan.Status.HintLine, "拿→" + topName, topName);
                        }
                        if (plan.ShopMarkers.Count > 3)
                            plan.ShopMarkers = plan.ShopMarkers.OrderByDescending(m => m.Score).Take(3).ToList();
                    }
                    // 4. 冻结商店 — 仅当店里有卡且非空商店才推荐
                    if (!shownPrimary && plan.FreezeHint != null && plan.FreezeHint.Active
                        && state.ShopMinions.Count > 0)
                    {
                        shownPrimary = true;
                        plan.Status.HintLine = "鲍勃的酒馆";
                    }
                    // 5. 卖牌 — 仅当出售后有金买更强随从或升本时才推荐
                    if (!shownPrimary && plan.SellMarkers.Count > 0
                        && (canBuyMinion || levelOk))
                    {
                        shownPrimary = true;
                        if (plan.SellMarkers[0].BoardIndex >= 0
                            && plan.SellMarkers[0].BoardIndex < state.BoardMinions.Count)
                        {
                            string soldName = ResolveDisplayName(
                                state.BoardMinions[plan.SellMarkers[0].BoardIndex].CardName,
                                state.BoardMinions[plan.SellMarkers[0].BoardIndex].CardId);
                            plan.Status.HintLine = AppendActionHint(
                                plan.Status.HintLine,
                                "卖→" + soldName,
                                soldName);
                        }
                        else
                            plan.Status.HintLine = AppendActionHint(
                                plan.Status.HintLine, "卖", "");
                    }
                    // 5.5 Activate 发动 — 仅当无更高优先动作且铸币足够时显示
                    if (plan.ActivateHint != null
                        && liveGold >= plan.ActivateHint.Cost)
                    {
                        _renderer.ShowTargetPulse(
                            plan.ActivateHint.BoardIndex,
                            state.BoardMinions.Count);
                        if (!shownPrimary
                            && plan.ShopMarkers.Count == 0
                            && plan.UpgradeHint == null
                            && plan.HandMarker == null
                            && plan.SellMarkers.Count == 0)
                        {
                            shownPrimary = true;
                            plan.Status.HintLine = AppendActionHint(
                                plan.Status.HintLine,
                                "发动→" + ResolveDisplayName(
                                    plan.ActivateHint.CardName,
                                    plan.ActivateHint.CardId)
                                    + " (" + plan.ActivateHint.Cost + "费)",
                                "");
                        }
                    }
                    // 兜底: 无任何目标时, 不显示空动作
                    if (!shownPrimary && plan.ShopMarkers.Count == 0
                        && plan.UpgradeHint == null && plan.HandMarker == null
                        && plan.SellMarkers.Count == 0
                        && (plan.FreezeHint == null || !plan.FreezeHint.Active))
                    {
                        plan.RecommendedActionType = "";
                    }

                    // 状态条必须在所有门控/裁剪完成后渲染，避免文字建议与实际UI不一致。
                    _renderer.ShowStatusStrip(status);

                    // ── 商店卡片高亮（从VisualPlan读取）──
                    // 布局数量使用酒馆理论槽位, 并被 live 最大原始槽位撑开。
                    // ShopPosition 保留 ZONE_POSITION-1, 不能用实际卡数压缩成 dense index。
                    bool loggedShopDiag = false;
                    int liveCardCount = state.ShopMinions.Count > 0 ? state.ShopMinions.Count : tierSlots;
                    int maxRawShopSlot = state.ShopMinions.Count > 0 ? state.ShopMinions.Max(m => m.Position) : -1;
                    int slotCountForLayout = layoutDecision.LayoutCount;
                    var denseShopSlots = layoutDecision.UseDenseSlots
                        ? state.ShopMinions
                            .OrderBy(sm => sm.Position)
                            .ThenBy(sm => sm.EntityId)
                            .Select((sm, idx) => new { sm.EntityId, DenseSlot = idx })
                            .ToDictionary(x => x.EntityId, x => x.DenseSlot)
                        : null;
                    foreach (var m in plan.ShopMarkers)
                    {
                        float normScore = (float)m.Score;
                        string reason = "";
                        if (!loggedShopDiag)
                        {
                            Log(string.Format("DIAG ShopRender: markers={0} tier={1} layoutTier={2} liveCards={3} shopCards={4} layoutSlots={5} maxRawSlot={6} dense={7}",
                                plan.ShopMarkers.Count, state.TavernTier, layoutTier, liveCardCount, state.ShopMinions.Count,
                                slotCountForLayout, maxRawShopSlot, layoutDecision.UseDenseSlots));
                            loggedShopDiag = true;
                        }
                        _renderer.SetCalcTier(layoutTier);
                        int renderShopPosition = m.ShopPosition;
                        if (denseShopSlots != null && m.EntityId > 0)
                        {
                            int denseSlot;
                            if (denseShopSlots.TryGetValue(m.EntityId, out denseSlot))
                                renderShopPosition = denseSlot;
                        }
                        _renderer.ShowShopCardRating(renderShopPosition, slotCountForLayout,
                            normScore, m.CardName, reason, m.Level, m.Pulse,
                            m.Purpose, m.Quality, m.IsTriple, m.Tier, m.EntityId);
                    }

                    // ── 升本宝石: 现场重读金币, 铸币不足时不显示任何升本引导 ──
                    if (plan.UpgradeHint != null && liveGold >= plan.UpgradeHint.Cost)
                    {
                        bool urgent = plan.UpgradeHint.Level == DecisionLevel.Critical;
                        bool t7 = state.EffectiveRules != null && state.EffectiveRules.MaxTavernTier >= 7;
                        _renderer.ShowLevelUpGlow(urgent, plan.UpgradeHint.Reason,
                            plan.UpgradeHint.Cost, plan.UpgradeHint.CurrentTier, t7);
                    }

                    // ── 刷新建议: 店无好牌时提示刷新, 金币够就用 ──
                    var liveRules = state.EffectiveRules ?? EffectiveGameRules.Default;
                    int liveRefreshCost = GameRuleEvaluator.GetRefreshCost(state, state.HeroCardId, liveRules);
                    bool canAffordRefresh = liveRules.ManualRefreshAllowed && liveGold >= liveRefreshCost;
                    int renderGold = Math.Min(liveGold, state.Gold >= 0 ? state.Gold : int.MaxValue);
                    bool refreshAffordableByState = renderGold >= liveRefreshCost
                        && (renderGold >= 1 || state.FreeRefreshCount > 0 || liveRefreshCost == 0);
                    bool earlySuppressRefresh = state.Turn <= 5 && state.FreeRefreshCount == 0;
                    bool badShop = plan.ShopMarkers.Count == 0 && plan.UpgradeHint == null
                        && state.ShopMinions.Count > 0 && !earlySuppressRefresh;
                    bool needRefresh = plan.RecommendedActionType.Equals("Refresh", StringComparison.OrdinalIgnoreCase);
                    bool showRefresh = liveRules.ManualRefreshAllowed
                        && !_lastLiveSafetySuppressRefresh
                        && (needRefresh || badShop) && canAffordRefresh && refreshAffordableByState
                        && CanRefreshLeaveMeaningfulBudget(state, liveGold);
                    if (showRefresh)
                    {
                        // 诊断: gold=0仍提示刷新(0611 06111357 问题E)。打印决策各因子定位
                        // 是 FreeRefreshCount 误判非0, 还是 badShop/needRefresh 误触发。
                        if (state.Gold < 1)
                            Log(string.Format("DIAG RefreshShow: gold={0} freeRefresh={1} needRefresh={2} badShop={3} markers={4} (gold<1仍显示刷新)",
                                state.Gold, state.FreeRefreshCount, needRefresh, badShop, plan.ShopMarkers.Count));
                        string refreshReason = badShop ? "牌差，换一页" : "找核心牌";
                        _renderer.ShowRefreshGlow(badShop ? false : true, refreshReason);
                    }
                    else
                    {
                        _renderer.ClearRefreshGlow();
                        if ((needRefresh || badShop)
                            && !refreshAffordableByState
                            && state.Gold < 1
                            && state.FreeRefreshCount == 0)
                        {
                            Log(string.Format(
                                "DIAG RefreshBlockedZeroGold: gold={0} live={1} free={2} turn={3}",
                                state.Gold, liveGold, state.FreeRefreshCount, state.Turn));
                        }
                    }

                    // ── 冻结提示: 推荐冻结时在刷新按钮右侧显示蓝色提示框 ──
                    bool showFreeze = plan.FreezeHint != null && plan.FreezeHint.Active;
                    // 2026-08-19 实测修复(A4): 冻结与刷新互斥——冻结=店里有价值要保留,
                    // 刷新=店差要换页, 同屏同时指导是冲突操作误导用户;
                    // 推荐冻结时抑制刷新光晕(冻结优先, 保守保留价值)
                    if (showFreeze && showRefresh) { showRefresh = false; _renderer.ClearRefreshGlow(); }
                    // 2026-08-19 实测修复(B10): 刚升本后的回合抑制冻结推荐——下回合
                    // 商店自动刷新为更高等级, 冻结当前低等级牌是浪费(塞纳留斯回合5反馈)
                    if (showFreeze && state.LastUpgradeTurn == state.Turn - 1)
                    {
                        showFreeze = false;
                        _renderer.ClearFreezeGlow();
                    }
                    if (!showFreeze
                        || (DateTime.Now - _freezeGlowSince).TotalSeconds >= 10.0)
                    {
                        _freezeGlowSince = DateTime.MinValue;
                        _renderer.ClearFreezeGlow();
                    }
                    else
                    {
                        if (_freezeGlowSince == DateTime.MinValue) _freezeGlowSince = DateTime.Now;
                        _renderer.ShowFreezeGlow(plan.FreezeHint.Urgent, plan.FreezeHint.Reason);
                    }

                    // ── 卖牌标记(按cardId匹配当前位置, 应对玩家拖动) ──
                    foreach (var sm in plan.SellMarkers)
                    {
                        int actualIdx = sm.BoardIndex;
                        if (!string.IsNullOrEmpty(sm.CardId))
                        {
                            for (int bi = 0; bi < state.BoardMinions.Count; bi++)
                            {
                                if (state.BoardMinions[bi].CardId == sm.CardId)
                                { actualIdx = bi; break; }
                            }
                        }
                        _renderer.ShowBoardHighlight(actualIdx, state.BoardMinions.Count,
                            "#FF5252", "建议出售");
                    }

                    // 兜底战斗监控: 持久定时器(启动一次, 不每帧重置)
                    // 修复: 之前每帧Stop/Start导致计时器永远不触发, 战斗后UI残留1.5s+
                    EnsureCombatWatchTimer();

                    // 购买/刷新后新元素淡入(100ms), 提供平滑视觉衔接
                    if (_renderer.FadeInMode)
                        _renderer.ApplyFadeInToAll(100);

                    _suggestionsActive = true;
                }
                catch (Exception ex) { Log("Render error: " + ex); }
            }));
        }

        private Engine.UiTargetSource ApplyPowerLogDiscoverCandidates(GameState state)
        {
            var candidates = _discoverBatchFromLog?.Candidates;
            if (candidates == null || candidates.Count < 2)
                return state.DiscoverOptions != null && state.DiscoverOptions.Count >= 2
                    ? Engine.UiTargetSource.Zone6 : Engine.UiTargetSource.None;

            // EntityChoices 是带 choiceId 的权威批次。它可能在战斗结束、HDT 回合号
            // 切换之前出现，也可能包含 5 个选项（时空扭曲）；持续到 CHOSEN/空批次。
            if (candidates.Count >= 2 && _discoverPanelActive)
            {
                state.DiscoverOptions = candidates.Select(c => new Engine.TrinketOption
                {
                    CardId = c.CardId,
                    TrinketName = Engine.DisplayNameResolver.Resolve(
                        c.CardId, c.CardName, Engine.CardNameService.GetName),
                    EntityId = c.EntityId,
                    Tribes = ResolveCardTribes(c.CardId),
                }).ToList();
                return Engine.UiTargetSource.PowerLog;
            }

            return state.DiscoverOptions != null && state.DiscoverOptions.Count >= 2
                ? Engine.UiTargetSource.Zone6 : Engine.UiTargetSource.None;
        }

        private void RenderAuthoritativeDiscoverDuringCombat()
        {
            var batch = _discoverBatchFromLog;
            if (!CombatChoiceRenderPolicy.CanRenderDiscoverDuringCombat(
                batch, _discoverPanelActive)) return;

            var options = batch.Candidates.Select(candidate => new Engine.TrinketOption
            {
                CardId = candidate.CardId,
                TrinketName = Engine.DisplayNameResolver.Resolve(
                    candidate.CardId, candidate.CardName, Engine.CardNameService.GetName),
                EntityId = candidate.EntityId,
                Tribes = ResolveCardTribes(candidate.CardId),
            }).ToList();
            var scoringState = new GameState { DiscoverOptions = options };
            if (_cachedState != null)
            {
                scoringState.Turn = _cachedState.Turn;
                scoringState.Gold = _cachedState.Gold;
                scoringState.Health = _cachedState.Health;
                scoringState.Armor = _cachedState.Armor;
                scoringState.BoardMinions = _cachedState.BoardMinions
                    ?? new List<Engine.MinionData>();
            }

            var hints = ScoreDiscoverOptions(scoringState);
            if (hints.Count == 0)
            {
                for (int index = 0; index < options.Count; index++)
                {
                    var option = options[index];
                    hints.Add(new Engine.TrinketHint
                    {
                        Index = index,
                        Name = option.TrinketName ?? option.CardId ?? "?",
                        Score = 1.0,
                        Reason = "发现选项",
                        IsTopPick = index == 0,
                    });
                }
            }

            _renderer.RefreshCanvasSize();
            _renderer.ShowAuthoritativeDiscoverHints(hints);
            if (!_lastDiscoverShowState)
            {
                Log(string.Format(
                    "UI Discover: SHOW authoritative-combat choice={0} options={1}",
                    batch.ChoiceId, options.Count));
                _lastDiscoverShowState = true;
            }
        }

        private void RenderTimewarpPurchaseHint()
        {
            var batch = _timewarpPurchaseBatchFromLog;
            if (batch == null || batch.ChoiceId < 0 || batch.Candidates == null
                || batch.Candidates.Count < 2) return;

            var options = batch.Candidates.Select(candidate => new Engine.TrinketOption
            {
                CardId = candidate.CardId,
                TrinketName = ResolveDisplayName(candidate.CardName, candidate.CardId),
                EntityId = candidate.EntityId,
                Tribes = ResolveCardTribes(candidate.CardId),
            }).ToList();
            var scoringState = new GameState { DiscoverOptions = options };
            if (_cachedState != null)
            {
                scoringState.Turn = _cachedState.Turn;
                scoringState.Gold = _cachedState.Gold;
                scoringState.Health = _cachedState.Health;
                scoringState.Armor = _cachedState.Armor;
                scoringState.BoardMinions = _cachedState.BoardMinions
                    ?? new List<Engine.MinionData>();
            }

            var hints = ScoreDiscoverOptions(scoringState);
            var scoresByIndex = hints
                .GroupBy(hint => hint.Index)
                .ToDictionary(group => group.Key, group => group.First().Score);
            var selectedIndices = Engine.TimewarpPurchaseAdvisor.SelectAffordableIndices(
                batch, scoresByIndex, Math.Max(1, Math.Min(2, batch.TimeCoinCount)));
            if (selectedIndices == null || selectedIndices.Count == 0)
            {
                _renderer.ClearTimewarpPurchaseHint();
                return;
            }

            var indices = selectedIndices.ToArray();
            var costs = indices.Select(i => batch.Candidates[i].PurchaseCost).ToArray();
            var names = indices.Select(i => ResolveDisplayName(
                batch.Candidates[i].CardName, batch.Candidates[i].CardId)).ToArray();
            _renderer.RefreshCanvasSize();
            _renderer.ClearDiscoverHints();
            _lastDiscoverShowState = false;
            _renderer.ShowTimewarpPurchaseRatings(
                indices, costs, names, batch.Candidates.Count, batch.TimeCoinCount);
        }

        private Engine.UiTargetSource ApplyPowerLogTrinketCandidates(GameState state)
        {
            var before = _trinketChoiceLifecycle.Current;
            if (!_trinketChoiceLifecycle.TryGetForTurn(state.Turn, out var binding))
            {
                if (before != null && _trinketChoiceLifecycle.Current == null)
                {
                    _trinketPanelActive = false;
                    _trinketPanelActivatedAt = DateTime.MinValue;
                }
                return state.TrinketOffer != null && state.TrinketOffer.Count >= 2
                    ? Engine.UiTargetSource.Entity : Engine.UiTargetSource.None;
            }

            var candidates = binding.Batch?.Candidates;
            if (candidates != null && candidates.Count >= 2 && candidates.Count <= 4)
            {
                var localOffers = new List<Engine.TrinketOption>();
                foreach (var candidate in candidates)
                {
                    Engine.TrinketFact fact = default(Engine.TrinketFact);
                    try
                    {
                        if (_engine != null && _engine.TrinketFactSource != null
                            && _engine.TrinketFactSource.TryGet(candidate.CardId, out fact)
                            && string.Equals(fact.CardId, candidate.CardId, StringComparison.Ordinal))
                        {
                            localOffers.Add(new Engine.TrinketOption
                            {
                                CardId = fact.CardId,
                                TrinketName = !string.IsNullOrEmpty(fact.NameZhCn)
                                    ? fact.NameZhCn
                                    : !string.IsNullOrEmpty(fact.NameEnUs)
                                        ? fact.NameEnUs : fact.CardId,
                                EntityId = candidate.EntityId,
                                IsLesser = fact.IsLesser,
                            });
                            continue;
                        }
                    }
                    catch
                    {
                        fact = default(Engine.TrinketFact);
                    }
                    localOffers.Add(new Engine.TrinketOption
                    {
                        CardId = candidate.CardId,
                        TrinketName = Engine.DisplayNameResolver.Resolve(
                            candidate.CardId,
                            candidate.CardName,
                            Engine.CardNameService.GetName),
                        EntityId = candidate.EntityId,
                        IsLesser = state.Turn == 6,
                    });
                }
                state.TrinketOffer = localOffers;
                return Engine.UiTargetSource.PowerLog;
            }

            return state.TrinketOffer != null && state.TrinketOffer.Count >= 2
                ? Engine.UiTargetSource.Entity : Engine.UiTargetSource.None;
        }

        private Engine.UiTargetSource ApplyScheduledTrinketPlaceholder(GameState state)
        {
            if (state == null) return Engine.UiTargetSource.None;
            if (state.TrinketOffer != null && state.TrinketOffer.Count >= 2)
                return Engine.UiTargetSource.Entity;

            // Power.log completion can trigger an event-driven render before the extractor
            // observes the newly equipped trinket. The completion callback records this turn
            // immediately, so do not reopen the scheduled loading placeholder in that window.
            if (_lastTrinketHideTurn == state.Turn)
                return Engine.UiTargetSource.None;

            int owned = state.ActiveTrinkets != null ? state.ActiveTrinkets.Count : 0;
            // 07062107修复: owned绝对计数被英雄机制额外饰品破坏(费林大使T9 equipped=3 → owned==1
            // 条件不成立, placeholder不注入, 大饰品面板整轮缺失)。改用"计划回合+本回合未完成选取"判定。
            bool roundResolvedThisTurn = _extractor != null
                && _extractor.TrinketRoundResolvedTurn == state.Turn;
            bool lesserPending = state.Turn == 6 && !roundResolvedThisTurn;
            bool greaterPending = state.Turn == 9 && !roundResolvedThisTurn;
            if (!lesserPending && !greaterPending)
                return Engine.UiTargetSource.None;

            string kind = lesserPending ? "小饰品" : "大饰品";
            state.TrinketOffer = new List<Engine.TrinketOption>
            {
                new Engine.TrinketOption
                {
                    CardId = lesserPending ? "__TRINKET_PENDING_LESSER_1" : "__TRINKET_PENDING_GREATER_1",
                    TrinketName = kind + "候选读取中",
                    EntityId = lesserPending ? -601 : -901,
                    IsLesser = lesserPending,
                },
                new Engine.TrinketOption
                {
                    CardId = lesserPending ? "__TRINKET_PENDING_LESSER_2" : "__TRINKET_PENDING_GREATER_2",
                    TrinketName = "等待游戏候选",
                    EntityId = lesserPending ? -602 : -902,
                    IsLesser = lesserPending,
                },
            };
            Log(string.Format("DIAG TrinketPlaceholder: turn={0} kind={1} owned={2}", state.Turn, kind, owned));
            return Engine.UiTargetSource.Entity;
        }

        private void AdvanceUiTargets(GameState state, Engine.UiTargetSource discoverSource, Engine.UiTargetSource trinketSource)
        {
            var discoverSnapshot = BuildTargetSnapshot(
                Engine.UiTargetType.Discover,
                discoverSource,
                state.Turn,
                state.DiscoverOptions,
                discoverSource == Engine.UiTargetSource.PowerLog ? 0.98 : 0.75);
            var discoverState = _discoverTargetState.Advance(discoverSnapshot);
            if (!discoverState.IsConfirmed)
                state.DiscoverOptions = new List<Engine.TrinketOption>();

            var trinketSnapshot = BuildTargetSnapshot(
                Engine.UiTargetType.Trinket,
                trinketSource,
                state.Turn,
                state.TrinketOffer,
                // 07062107修复: 计划饰品回合(T6/T9)的批次先验极强, 给0.95置信首帧即确认 —
                // 旧0.80需第二帧确认, 但提取由事件驱动(状态静止时8秒无帧), T9面板延迟8秒的根因。
                trinketSource == Engine.UiTargetSource.PowerLog ? 0.98
                    : (state.Turn == 6 || state.Turn == 9) ? 0.95 : 0.80);
            var trinketState = _trinketTargetState.Advance(trinketSnapshot);
            if (!trinketState.IsConfirmed && !IsScheduledTrinketPlaceholder(state.TrinketOffer))
                state.TrinketOffer = new List<Engine.TrinketOption>();
        }

        private bool IsScheduledTrinketPlaceholder(List<Engine.TrinketOption> options)
        {
            return options != null
                && options.Count >= 2
                && options.Any(o => o != null
                    && !string.IsNullOrEmpty(o.CardId)
                    && o.CardId.StartsWith("__TRINKET_PENDING", StringComparison.Ordinal));
        }

        private bool HasScheduledTrinketPlaceholder(GameState state)
        {
            return state != null && IsScheduledTrinketPlaceholder(state.TrinketOffer);
        }

        private Engine.UiTargetSnapshot BuildTargetSnapshot(
            Engine.UiTargetType type,
            Engine.UiTargetSource source,
            int turn,
            List<Engine.TrinketOption> options,
            double confidence)
        {
            if (source == Engine.UiTargetSource.None || options == null || options.Count < 2)
                return null;

            // typed Power.log 发现可包含5项（大小时空扭曲）；实体/zone6路径仍保持
            // 4项上限，避免放宽启发式噪声。饰品当前机制同样最多4项。
            int maxOptions = type == Engine.UiTargetType.Discover
                && source == Engine.UiTargetSource.PowerLog ? 5 : 4;
            if (options.Count > maxOptions)
                return null;

            var ids = options
                .Where(o => o != null && o.EntityId > 0)
                .Select(o => o.EntityId)
                .OrderBy(id => id)
                .ToList();
            var keys = options
                .Where(o => o != null)
                .Select(o => o.EntityId > 0 ? "E" + o.EntityId : "C" + (o.CardId ?? o.TrinketName ?? "?"))
                .OrderBy(s => s)
                .ToList();

            string batchId = type + "|" + turn + "|" + source + "|" + string.Join(",", keys);
            if (source == Engine.UiTargetSource.PowerLog)
            {
                if (type == Engine.UiTargetType.Discover && _discoverBatchFromLog != null)
                    batchId = "PL|Discover|" + _discoverBatchFromLog.ChoiceId + "|" + _discoverBatchFromLog.TaskList;
                else if (type == Engine.UiTargetType.Trinket
                    && _trinketChoiceLifecycle.TryGetForTurn(turn, out var binding)
                    && binding.Batch != null)
                    batchId = "PL|Trinket|" + binding.Batch.ChoiceId + "|" + binding.Batch.TaskList;
            }

            return new Engine.UiTargetSnapshot
            {
                TargetType = type,
                Source = source,
                Turn = turn,
                EntityIds = ids,
                OptionCount = options.Count,
                Confidence = confidence,
                BatchId = batchId,
            };
        }

        /// <summary>简单评分发现选项(优先流派+高星)</summary>
        private List<Engine.TrinketHint> ScoreDiscoverOptions(GameState state)
        {
            var result = new List<Engine.TrinketHint>();
            if (state.DiscoverOptions == null || state.DiscoverOptions.Count == 0)
                return result;

            // 优先检查是否为暗月奖品发现 (奖品法术有独立评分体系)
            List<DecisionEngine.TrinketScore> prizeScores = null;
            try { prizeScores = _engine.EvaluatePrizeDiscovers(state); } catch { }
            bool isPrizeDiscover = prizeScores != null && prizeScores.Count > 0;

            string compDir = _cachedPlan?.Status?.CompDir ?? "";
            string normalizedComp = (compDir ?? "").TrimEnd('?').Trim();
            var scored = new List<(int idx, double score, string reason)>();
            for (int i = 0; i < state.DiscoverOptions.Count; i++)
            {
                var d = state.DiscoverOptions[i];
                double sc = 3.0;
                var reasonParts = new List<string>();

                // 奖品法术: 使用专属评分覆盖默认评分
                if (isPrizeDiscover)
                {
                    var ps = prizeScores.Find(p => p.Index == i);
                    if (ps.Name != null && ps.Score > 0)
                    {
                        scored.Add((i, ps.Score, "暗月奖品"));
                        continue;
                    }
                }
                int tier = d.Tier;
                if (tier <= 1)
                {
                    try
                    {
                        int ct = GetCardTier(d.CardId);
                        if (ct > 0) tier = ct;
                        if (tier <= 1)
                        {
                            HearthDb.Card c;
                            if (HearthDb.Cards.All.TryGetValue(d.CardId, out c) && c != null && c.TechLevel > 0)
                                tier = c.TechLevel;
                        }
                    }
                    catch { }
                }
                if (tier < 1) tier = 1;

                // 高星加分
                if (tier >= 5) { sc += 4; reasonParts.Add("高星"); }
                else if (tier >= 4) { sc += 2.5; reasonParts.Add("优质"); }
                else if (tier >= 3) sc += 1;

                // 流派匹配: 优先用结构化Tribes, 不依赖中文名 Contains
                bool compMatched = false;
                if (!string.IsNullOrEmpty(normalizedComp))
                {
                    var optionTribes = d.Tribes != null && d.Tribes.Count > 0
                        ? d.Tribes : ResolveCardTribes(d.CardId);
                    string compCn = TribeCnName(normalizedComp);
                    compMatched = optionTribes.Any(t =>
                        string.Equals(t, normalizedComp, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(t, compCn, StringComparison.OrdinalIgnoreCase)
                        || t == "全" || t == "全部" || t == "ALL");
                    if (!compMatched && optionTribes.Count == 0)
                    {
                        string dn = d.TrinketName ?? "";
                        compMatched = (!string.IsNullOrEmpty(normalizedComp)
                                && dn.Contains(normalizedComp))
                            || (!string.IsNullOrEmpty(compCn) && dn.Contains(compCn));
                    }
                }
                if (compMatched)
                {
                    sc += 3;
                    reasonParts.Add("匹配流派");
                }

                // 核心牌与资源牌: 前中期资源牌权重最高，后期核心引擎权重最高
                bool isCore = _engine != null && _engine.IsCoreComboCard(d.CardId);
                bool isResource = _engine != null && _engine.IsResourceCard(d.CardId);
                if (isResource)
                {
                    sc += state.Turn <= 5 ? 5.5
                        : state.Turn <= 9 ? 4.5 : 3.0;
                    reasonParts.Add("资源牌");
                }
                if (isCore)
                {
                    sc += state.Turn <= 5 ? 4.5
                        : state.Turn <= 9 ? 5.0 : 6.0;
                    reasonParts.Add("核心牌");
                }

                // 关键词加成
                string name = d.TrinketName ?? "";
                if (name.Contains("铜须") || name.Contains("达瑞尔") || name.Contains("瑞文"))
                { sc += 3; reasonParts.Add("引擎"); }

                // 发现来源差异化: 三连(+25%) > 英雄技能(+12%) > 战吼/法术(基准)
                double sourceMult = _discoverSource == "triple" ? 1.25
                    : _discoverSource == "prize" ? 1.18
                    : _discoverSource == "heroPower" ? 1.12 : 1.0;
                scored.Add((i, sc * sourceMult, string.Join("+", reasonParts)));
            }
            scored.Sort((a, b) => b.score.CompareTo(a.score));
            for (int si = 0; si < scored.Count; si++)
            {
                var s = scored[si];
                result.Add(new Engine.TrinketHint
                {
                    Index = s.idx,
                    Name = state.DiscoverOptions[s.idx].TrinketName,
                    Score = s.score,
                    Reason = s.reason,
                    IsTopPick = (si == 0),
                });
            }
            return result;
        }

        private string BuildPickListText(GameState state, List<int> indices, GameAction bestAction)
        {
            if (state == null || indices == null || indices.Count == 0) return "";

            var sb = new System.Text.StringBuilder();
            bool isSell = bestAction != null && bestAction.Type == ActionType.SellMinion;
            bool isRefresh = bestAction != null && bestAction.Type == ActionType.Refresh;
            bool isUseSpell = bestAction != null && bestAction.Type == ActionType.UseSpell;
            bool isPlayMinion = bestAction != null && bestAction.Type == ActionType.PlayMinion;

            if (isSell)
            {
                sb.Append("建议出售:\n");
                for (int i = 0; i < indices.Count && i < 3; i++)
                {
                    int idx = indices[i];
                    if (idx >= 0 && state.BoardMinions != null && idx < state.BoardMinions.Count)
                    {
                        var m = state.BoardMinions[idx];
                        if (i > 0) sb.Append("\n");
                        sb.AppendFormat("{0}. 卖 {1} T{2}", i + 1, m.CardName, m.Tier);
                    }
                }
            }
            else if (isRefresh)
            {
                if (state.ShopMinions == null || state.ShopMinions.Count == 0) return "";
                sb.Append("店中可买:\n");
                for (int i = 0; i < indices.Count && i < 3; i++)
                {
                    int idx = indices[i];
                    if (idx >= 0 && idx < state.ShopMinions.Count)
                    {
                        var m = state.ShopMinions[idx];
                        string tag = GetPairTag(state, m);
                        if (i > 0) sb.Append("\n");
                        sb.AppendFormat("{0}. {1} T{2}{3}", i + 1, m.CardName, m.Tier, tag);
                    }
                }
            }
            else if (isUseSpell || isPlayMinion)
            {
                if (state.HandMinions == null) return "";
                string prefix = isUseSpell ? "建议使用:\n" : "建议上阵:\n";
                sb.Append(prefix);
                int target = bestAction != null ? bestAction.TargetIndex : -1;
                if (target >= 0 && target < state.HandMinions.Count)
                {
                    var m = state.HandMinions[target];
                    sb.AppendFormat("{0}. {1} T{2}",
                        target + 1,
                        string.IsNullOrEmpty(m.CardName) ? m.CardId : m.CardName,
                        m.Tier);
                }
            }
            else
            {
                if (state.ShopMinions == null) return "";
                sb.Append("推荐购买:\n");
                for (int i = 0; i < indices.Count && i < 3; i++)
                {
                    int idx = indices[i];
                    if (idx >= 0 && idx < state.ShopMinions.Count)
                    {
                        var m = state.ShopMinions[idx];
                        string tag = GetPairTag(state, m);

                        if (i > 0) sb.Append("\n");
                        sb.AppendFormat("{0}. {1} T{2}{3}", i + 1, m.CardName, m.Tier, tag);
                    }
                }
            }
            return sb.ToString();
        }

        private string GetPairTag(GameState state, MinionData m)
        {
            if (state == null || m == null || m.IsSpell || string.IsNullOrEmpty(m.CardId))
                return "";
            var rules = state.EffectiveRules ?? Engine.EffectiveGameRules.Default;
            if (Engine.TripleRuleEvaluator.CompletesGolden(state, m.CardId, rules))
                return " [凑三连!]";
            int owned = Engine.TripleRuleEvaluator.CountOwnedCopies(state, m.CardId);
            if (rules.GoldenCopyRequirement > 2
                && owned == rules.GoldenCopyRequirement - 2)
            {
                // 纯经济卡单张不标对子（机制驱动）
                if (_engine.IsEconomyCard(m.CardId)) return "";
                return " [凑对子]";
            }
            return "";
        }

        private string ActionToText(ActionType type)
        {
            switch (type)
            {
                case ActionType.Upgrade: return "升";
                case ActionType.Refresh: return "刷";
                case ActionType.BuyMinion: return "选";
                case ActionType.SellMinion: return "卖";
                case ActionType.PlayMinion: return "打";
                case ActionType.UseSpell: return "用";
                default: return "稳";
            }
        }

        private string ActionToColor(ActionType type)
        {
            switch (type)
            {
                case ActionType.Upgrade: return "#FFCC00";
                case ActionType.Refresh: return "#00BFFF";
                case ActionType.BuyMinion: return "#69F0AE";
                case ActionType.SellMinion: return "#FFAB40";
                case ActionType.PlayMinion: return "#69F0AE";
                case ActionType.UseSpell: return "#B388FF";
                default: return "#FFD700";
            }
        }

        private void UpdatePoolFromStateChange(GameState state)
        {
            if (_poolTracker == null || state == null) return;

            var curBoardIds = new HashSet<int>();
            var curHandIds = new HashSet<int>();
            foreach (var m in state.BoardMinions) curBoardIds.Add(m.EntityId);
            foreach (var m in state.HandMinions) curHandIds.Add(m.EntityId);

            // 检测购买：场上/手牌新出现的 entityId
            if (_prevBoardIds.Count > 0 || _prevHandIds.Count > 0)
            {
                var allPrev = new HashSet<int>(_prevBoardIds);
                allPrev.UnionWith(_prevHandIds);
                var allCur = new HashSet<int>(curBoardIds);
                allCur.UnionWith(curHandIds);

                foreach (var id in allCur)
                {
                    if (!allPrev.Contains(id))
                    {
                        // 新卡：玩家购买了它，或从三连获得
                        var card = FindMinionById(state, id);
                        if (card != null) _poolTracker.OnBuyCard(card.CardId);
                    }
                }

                // 检测出售：消失的 entityId
                foreach (var id in allPrev)
                {
                    if (!allCur.Contains(id))
                    {
                        // 卡消失了：出售或三连消耗
                        var card = FindMinionById(state, id);
                        if (card != null) _poolTracker.OnSellCard(card.CardId);
                    }
                }

                // 检测三连：场上卡变金色
                foreach (var cur in state.BoardMinions)
                {
                    if (cur.Golden && _prevBoardIds.Contains(cur.EntityId))
                    {
                        // 之前就在场上但非金色，现在变金 → 三连
                        // 需要找到合并的另外两张卡
                    }
                }
            }

            _prevBoardIds = curBoardIds;
            _prevHandIds = curHandIds;
        }

        private MinionData FindMinionById(GameState state, int entityId)
        {
            foreach (var m in state.BoardMinions)
                if (m.EntityId == entityId) return m;
            foreach (var m in state.HandMinions)
                if (m.EntityId == entityId) return m;
            return null;
        }

        /// <summary>拔线安全: 不用时间预测, 纯靠实体消失+版本号双重拦截</summary>
        private void CheckPreCombatClear(int turn)
        {
            // 不依赖时间估算。拔线时招募时长波动20-30秒, 时间预测会导致过早清UI。
            // 改为依赖两个权威源:
            // 1. EvaluateAndRender入口: ShopMinions==0 → 立即ClearSuggestions+return
            // 2. DispatchRender回调: shopGone检测 + capturedVersion检测过期异步dispatch
            _preCombatCleared = false;
        }
        private int _learnedRecruitSamples = 0;

        // BobsBuddy战斗预测缓存
        private Engine.BobsBuddyBridge.CombatResult _lastCombatPrediction = null;
        private string _lastCombatBoardHash = "";  // 板面指纹, 避免重复模拟
        private bool _combatPredictAvailable = false;
        private int _combatPredictCheckCount = 0;

        /// <summary>异步调用BobsBuddy预测下轮战斗结果(仅板面变化时)</summary>
        private void TryPredictCombat(Engine.GameState state)
        {
            _combatPredictCheckCount++;
            // 每10次检查才查一次Availability (反射发现慢)
            if (_combatPredictCheckCount % 10 == 1)
                _combatPredictAvailable = Engine.BobsBuddyBridge.Available;

            if (!_combatPredictAvailable) return;
            if (state.BoardMinions.Count == 0) return;
            // 找第一个活着的对手
            var opp = state.Opponents.Find(o => o.Alive && o.BoardMinions.Count > 0);
            if (opp == null) return;

            // 板面指纹: CardId+身材 (buff后身材变化需要重新模拟)
            var myCards = string.Join(",", state.BoardMinions.OrderBy(m => m.CardId)
                .Select(m => string.Format("{0}({1}/{2}){3}", m.CardId, m.Attack, m.Health, m.Golden ? "g" : "")));
            var oppCards = string.Join(",", opp.BoardMinions.OrderBy(m => m.CardId)
                .Select(m => string.Format("{0}({1}/{2}){3}", m.CardId, m.Attack, m.Health, m.Golden ? "g" : "")));
            var hash = myCards + "|" + oppCards;
            if (hash == _lastCombatBoardHash) return; // 未变化, 用缓存
            _lastCombatBoardHash = hash;

            try
            {
                var atkBoard = new List<Engine.MinionData>(state.BoardMinions);
                var defBoard = new List<Engine.MinionData>(opp.BoardMinions);
                // 2026-08 对抗性审查 M2: Simulate(2000次模拟+反射)在UI线程同步执行会
                // 阻塞覆盖层(注释原称"异步"实为同步)。改后台线程跑 + Dispatcher 写回,
                // 写回前比对 hash 防 stale-clobber(板面已再变化时丢弃旧结果)。
                string capturedHash = hash;
                System.Threading.Tasks.Task.Run(() =>
                {
                    Engine.BobsBuddyBridge.CombatResult prediction = null;
                    try
                    {
                        prediction = Engine.BobsBuddyBridge.Simulate(atkBoard, defBoard, simCount: 2000);
                    }
                    catch (Exception simEx) { Log("CombatPredict sim error: " + simEx.Message); }
                    var app = System.Windows.Application.Current;
                    if (app == null) return;
                    app.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                    {
                        try
                        {
                            if (capturedHash != _lastCombatBoardHash) return; // 板面已变, 丢弃旧结果
                            _lastCombatPrediction = prediction;
                            if (prediction != null)
                            {
                                Log(string.Format("CombatPredict: win={0:F1}% tie={1:F1}% lose={2:F1}% dmg={3:F1} sims={4}",
                                    prediction.WinRate * 100,
                                    prediction.TieRate * 100,
                                    prediction.LossRate * 100,
                                    prediction.AvgDamageTaken,
                                    prediction.SimulationCount));
                            }
                        }
                        catch (Exception ex) { Log("CombatPredict writeback error: " + ex.Message); }
                    }));
                });
            }
            catch (Exception ex) { Log("CombatPredict error: " + ex.Message); }
        }

        private void ClearSuggestions()
        {
            _suggestionsActive = false;
            _renderVersion++; // 废止所有排队中的异步渲染
            _persistentDirty = true; // 标记持久UI需要刷新
            // 同步清除：战斗过渡不能等异步 dispatch
            try { if (_renderer != null) _renderer.Clear(); } catch { }
        }

        // ── State hash ──

        private string ComputeStateHash()
        {
            int turn = 1, gold = 3, boardCount = 0;
            bool combat = false, frozen = false;
            var shopIds = new List<int>();
            try
            {
                try { turn = Core.Game.GetTurnNumber(); } catch { }
                if (turn < 1 || turn > 30) turn = 1;
                try
                {
                    // 0708: HDT RESOURCES 在招募期保持回合起始满金, 用它覆盖 self-tracked gold
                    // 会让 gold=0 被哈希成旧值, 导致买空/升本后仍提示购买或3费技能。
                    // gold=0 必须保留在状态哈希中; _cachedState 是上一帧 Extract() 的可信金币。
                    gold = _cachedState?.Gold ?? 3;
                }
                catch { }
                frozen = false;
                boardCount = 0;
                try
                {
                    foreach (var e in Core.Game.Entities.Values.ToList())
                    {
                        if (e == null) continue;
                        if (e.GetTag(GameTag.BACON_FREEZE) > 0) frozen = true;
                        if (e.IsInPlay && e.IsControlledBy(1) && !string.IsNullOrEmpty(e.CardId)
                            && !e.CardId.StartsWith("TB_BaconShop_HERO") && e.Health > 0)
                            boardCount++;
                        // 收集商店实体ID用于检测刷新
                        var cardType = (CardType)(e.HasTag(GameTag.CARDTYPE)
                            ? e.GetTag(GameTag.CARDTYPE)
                            : 0);
                        if (!e.IsInPlay && !e.IsInHand
                            && Engine.ShopEntitySnapshotPolicy.IsStateHashCandidate(cardType, e.Health)
                            && !string.IsNullOrEmpty(e.CardId)
                            && (e.CardId.StartsWith("BG") || e.CardId.StartsWith("TB_Bacon"))
                            && !e.CardId.StartsWith("TB_BaconShop_HERO")
                            && e.GetTag(GameTag.BACON_TRINKET) == 0
                            && e.GetTag(GameTag.ZONE) == 5)
                        {
                            shopIds.Add(e.Id);
                        }
                    }
                }
                catch { }
            }
            catch { }
            // 计数手牌实体（检测法术使用等操作）
            int handCount = 0;
            try
            {
                foreach (var e in Core.Game.Entities.Values.ToList())
                {
                    if (e == null) continue;
                    if (e.IsInHand && e.IsControlledBy(1) && !string.IsNullOrEmpty(e.CardId))
                        handCount++;
                }
            }
            catch { }
            try { combat = Core.Game.IsBattlegroundsCombatPhase; } catch { }
            // 饰品实体计数(检测饰品选牌弹出)
            int trinketCount = 0;
            try
            {
                foreach (var e in Core.Game.Entities.Values.ToList())
                {
                    if (e == null) continue;
                    if (e.HasTag(GameTag.BACON_TRINKET) && !string.IsNullOrEmpty(e.CardId))
                        trinketCount++;
                }
            }
            catch { }
            // 加入前3个商店实体ID（避免哈希过长，足够检测刷新）
            shopIds.Sort();
            string shopFingerprint = "";
            int shopCount = shopIds.Count;
            for (int i = 0; i < shopIds.Count && i < 3; i++)
                shopFingerprint += shopIds[i].ToString() + ",";
            // 发现实体计数(检测3选1弹窗)
            int discoverCount = 0;
            try
            {
                foreach (var e in Core.Game.Entities.Values.ToList())
                {
                    if (e == null) continue;
                    if (e.GetTag(GameTag.ZONE) == 6 && !e.HasTag(GameTag.BACON_TRINKET)
                        && !string.IsNullOrEmpty(e.CardId)
                        && !e.CardId.StartsWith("TB_BaconShop_HERO"))
                        discoverCount++;
                }
            }
            catch { }
            return string.Format("{0}|{1}|{2}|{3}|{4}|{5}|{6}|{7}|{8}|{9}",
                turn, gold, combat ? "C" : "S", boardCount, frozen ? "F" : "",
                shopCount, handCount, trinketCount, discoverCount, shopFingerprint);
        }

        private static readonly Dictionary<string, string> _tribeCn = new Dictionary<string, string>
        {
            { "BEAST", "野兽" }, { "MECHANICAL", "机械" }, { "DRAGON", "龙" },
            { "ELEMENTAL", "元素" }, { "MURLOC", "鱼人" }, { "PIRATE", "海盗" },
            { "UNDEAD", "亡灵" }, { "QUILBOAR", "野猪人" }, { "NAGA", "纳迦" },
            { "DEMON", "恶魔" }, { "ALL", "全" },
        };

        private static string TribeCnName(string code)
        {
            if (string.IsNullOrEmpty(code)) return "";
            string cn;
            return _tribeCn.TryGetValue(code, out cn) ? cn : code;
        }

        /// <summary>Power.log NEXT_STEP 事件: 以Power.log为权威时钟源校准计时</summary>
        private void OnPowerLogPhaseChanged(string phase)
        {
            if (DispatchToUiThread(() => OnPowerLogPhaseChanged(phase))) return;
            _powerLogPhase = phase ?? "shop";
            _globalReplayRecorder?.RecordPhase(_powerLogPhase);
            // 修复: MAIN_ACTION(招募开始)不应清除UI, 仅在MAIN_COMBAT(战斗开始)清除
            if (phase == "MAIN_COMBAT")
            {
                try { _renderer?.Clear(); } catch { }
                _renderVersion++;
                _suggestionsActive = false;
                _persistentDirty = true;
            }
            // 自学习招募时长: MAIN_ACTION=招募开始(重置计时) → MAIN_COMBAT=战斗开始(计算时长)
            if (phase == "MAIN_ACTION")
            {
                _recruitStartTime = DateTime.Now;
                _preCombatCleared = false;
            }
            if (phase == "MAIN_COMBAT" && _recruitStartTime != DateTime.MinValue)
            {
                double actualRecruitSec = (DateTime.Now - _recruitStartTime).TotalSeconds;
                if (actualRecruitSec > 10 && actualRecruitSec < 180)
                {
                    _learnedRecruitSec = _learnedRecruitSec * 0.5 + actualRecruitSec * 0.5;
                    _learnedRecruitSamples++;
                }
                _recruitStartTime = DateTime.MinValue;
                // 持久化自学习时长, 下次启动继承
                SaveLearnedRecruitSec();
            }
        }

        private void OnPowerLogBuildNumberChanged(int build)
        {
            if (DispatchToUiThread(() => OnPowerLogBuildNumberChanged(build))) return;
            try
            {
                Log("Current game BuildNumber=" + build);
            }
            catch (Exception ex) { Log("Build update error: " + ex.Message); }
        }

        /// <summary>Power.log 发现候选事件: 来自ChoiceList或FULL_ENTITY批量检测</summary>
        private void OnPowerLogDiscoverOffered(Engine.PowerLogChoiceBatch batch)
        {
            if (DispatchToUiThread(() => OnPowerLogDiscoverOffered(batch))) return;
            var candidates = batch?.Candidates;
            if (candidates == null) return;
            if (candidates.Count > 0 && _extractor != null)
            {
                int observedTurn = _cachedState != null ? _cachedState.Turn : -1;
                _extractor.ObserveSecondHeroPowerChoiceBatch(
                    batch.ChoiceId, observedTurn,
                    candidates.Select(candidate => candidate.CardId), "power_log");
            }
            if (candidates.Count == 0)
            {
                // 空列表 = 发现结束 (ChoiceList=CHOSEN 或 实体隐藏)
                if (_discoverBatchFromLog != null && batch.ChoiceId >= 0
                    && batch.ChoiceId != _discoverBatchFromLog.ChoiceId) return;
                _discoverBatchFromLog = null;
                _discoverPanelActive = false; _discoverPanelActivatedAt = DateTime.MinValue;
                _discoverTargetState.CompleteChoice();
                Log("Discover panel: Power.log signaled end, clearing");
                OnPowerLogStateChanged();
                return;
            }
            if (_globalReplayRecorder != null)
            {
                int observedTurn = _cachedState != null ? _cachedState.Turn : -1;
                _globalReplayRecorder.RecordChoice(
                    "discover",
                    batch.ChoiceId,
                    batch.TaskList,
                    batch.SourceCardId,
                    candidates.Select(c => c.CardId),
                    observedTurn);
            }
            _discoverBatchFromLog = batch;
            _discoverPanelActive = true; _discoverPanelActivatedAt = DateTime.UtcNow;
            _lastDiscoverTriggerTime = DateTime.UtcNow;
            Log(string.Format("Discover panel: choice={0} source={1} signaled {2} candidates [{3}]",
                batch.ChoiceId, batch.SourceCardId, candidates.Count,
                string.Join(", ", candidates.Select(c =>
                    ResolveDisplayName(c.CardName, c.CardId)))));
            OnPowerLogStateChanged();
        }

        private void OnPowerLogTimewarpPurchaseOffered(Engine.PowerLogChoiceBatch batch)
        {
            if (DispatchToUiThread(() => OnPowerLogTimewarpPurchaseOffered(batch))) return;
            if (batch == null || batch.ChoiceId < 0 || batch.Candidates == null
                || batch.Candidates.Count < 2) return;
            _timewarpPurchaseBatchFromLog = batch;
            _timewarpPurchaseClearRequested = false;
            _globalReplayRecorder?.RecordChoice(
                "timewarp",
                batch.ChoiceId,
                batch.TaskList,
                batch.SourceCardId,
                batch.Candidates.Select(c => c.CardId),
                _cachedState != null ? _cachedState.Turn : -1);
            Log(string.Format(
                "Timewarp purchase: choice={0} source={1} coins={2} candidates=[{3}]",
                batch.ChoiceId, batch.SourceCardId, batch.TimeCoinCount,
                string.Join(", ", batch.Candidates.Select(candidate => string.Format(
                    "{0}:{1}", ResolveDisplayName(candidate.CardName, candidate.CardId),
                    candidate.PurchaseCost)))));
            OnPowerLogStateChanged();
        }

        private void OnPowerLogDarkGiftChoiceActive(Engine.PowerLogChoiceBatch batch)
        {
            if (DispatchToUiThread(() => OnPowerLogDarkGiftChoiceActive(batch))) return;
            if (batch == null || batch.ChoiceId < 0) return;
            _darkGiftBatchFromLog = batch;
            _darkGiftTipCoordinator.OnDarkGiftChoiceActive(DateTime.Now);
            _globalReplayRecorder?.RecordChoice(
                "darkGift",
                batch.ChoiceId,
                batch.TaskList,
                batch.SourceCardId,
                batch.Candidates.Select(c => c.CardId),
                _cachedState != null ? _cachedState.Turn : -1);
            Log("Dark Gift: choice=" + batch.ChoiceId + " source=" + batch.SourceCardId);
            OnPowerLogStateChanged();
        }

        private void OnPowerLogDarkGiftCompleted(Engine.PowerLogChoiceCompletion completion)
        {
            if (DispatchToUiThread(() => OnPowerLogDarkGiftCompleted(completion))) return;
            if (completion == null || _darkGiftBatchFromLog == null
                || _darkGiftBatchFromLog.ChoiceId != completion.ChoiceId) return;
            _darkGiftBatchFromLog = null;
            _darkGiftUsedCount = Math.Min(3, _darkGiftUsedCount + 1);
            _darkGiftUsedTurn = _cachedState != null ? _cachedState.Turn : -1;
            _darkGiftTipCoordinator.OnDarkGiftCompleted(DateTime.Now);
            _globalReplayRecorder?.RecordChoiceCompleted(
                "darkGift",
                completion.ChoiceId,
                completion.SelectedCardId,
                _darkGiftUsedTurn);
            Log("Dark Gift: choice=" + completion.ChoiceId + " completed selected=" + completion.SelectedCardId);
            OnPowerLogStateChanged();
        }

        private void OnPowerLogPassiveChoiceActive(Engine.PowerLogChoiceBatch batch)
        {
            if (DispatchToUiThread(() => OnPowerLogPassiveChoiceActive(batch))) return;
            if (batch == null || batch.ChoiceId < 0) return;
            _passiveChoiceActive = true;
            _discoverBatchFromLog = batch;
            _discoverPanelActive = true;
            _discoverPanelActivatedAt = DateTime.UtcNow;
            _lastDiscoverTriggerTime = DateTime.UtcNow;
            _globalReplayRecorder?.RecordChoice(
                "passive",
                batch.ChoiceId,
                batch.TaskList,
                batch.SourceCardId,
                batch.Candidates.Select(c => c.CardId),
                _cachedState != null ? _cachedState.Turn : -1);
            Log("Passive choice: active choice=" + batch.ChoiceId + " source=" + batch.SourceCardId);
            OnPowerLogStateChanged();
        }
        private void OnPowerLogHeroPowerDiscoverOffered(Engine.PowerLogChoiceBatch batch)
        {
            if (DispatchToUiThread(() => OnPowerLogHeroPowerDiscoverOffered(batch))) return;
            if (batch == null || batch.ChoiceId < 0) return;
            // R1.2.1: 身份揭晓(EBG_Spell_037)发现新英雄技能 — 记录 + 触发独立提示
            _globalReplayRecorder?.RecordChoice(
                "heroPowerDiscover",
                batch.ChoiceId,
                batch.TaskList,
                batch.SourceCardId,
                batch.Candidates.Select(c => c.CardId),
                _cachedState != null ? _cachedState.Turn : -1);
            Log("HeroPower discover: active choice=" + batch.ChoiceId + " source=" + batch.SourceCardId + " candidates=" + string.Join(",", batch.Candidates.Select(c => c.CardName ?? c.CardId ?? "?")));
            // 1.2.2 辛达苟萨/阮大师反馈: 英雄技能发现的 choice 有记录但 UI 不弹面板。
            // 复用普通发现的渲染链(_discoverBatchFromLog + _discoverPanelActive),
            // DispatchRender 据此绘制权威发现提示(ShowAuthoritativeDiscoverHints)。
            if (batch.Candidates != null && batch.Candidates.Count >= 2)
            {
                _discoverBatchFromLog = batch;
                _discoverPanelActive = true; _discoverPanelActivatedAt = DateTime.UtcNow;
                _lastDiscoverTriggerTime = DateTime.UtcNow;
                OnPowerLogStateChanged();
            }
            else if (batch.Candidates != null && batch.Candidates.Count == 0)
            {
                // 空 = 英雄技能发现结束, 与普通发现一致清面板
                if (_discoverBatchFromLog != null && batch.ChoiceId >= 0
                    && batch.ChoiceId != _discoverBatchFromLog.ChoiceId) return;
                _discoverBatchFromLog = null;
                _discoverPanelActive = false; _discoverPanelActivatedAt = DateTime.MinValue;
                _discoverTargetState.CompleteChoice();
                OnPowerLogStateChanged();
            }
        }

        private void OnPowerLogPassiveChoiceCompleted(Engine.PowerLogChoiceCompletion completion)
        {
            if (DispatchToUiThread(() => OnPowerLogPassiveChoiceCompleted(completion))) return;
            if (completion == null) return;
            _passiveChoiceActive = false;
            if (_discoverBatchFromLog != null
                && _discoverBatchFromLog.ChoiceId == completion.ChoiceId)
            {
                _discoverBatchFromLog = null;
                _discoverPanelActive = false;
                _discoverPanelActivatedAt = DateTime.MinValue;
                _discoverTargetState.CompleteChoice();
            }
            _globalReplayRecorder?.RecordChoiceCompleted(
                "passive",
                completion.ChoiceId,
                completion.SelectedCardId,
                _cachedState != null ? _cachedState.Turn : -1);
            Log("Passive choice: completed choice=" + completion.ChoiceId
                + " selected=" + completion.SelectedCardId);
            OnPowerLogStateChanged();
        }

        private void OnPowerLogDarkGiftPressableChanged(Engine.PowerLogDarkGiftPressable pressable)
        {
            if (DispatchToUiThread(() => OnPowerLogDarkGiftPressableChanged(pressable))) return;
            if (pressable == null || pressable.CardId != "BG36_Button_DarkGift") return;
            _darkGiftTipCoordinator.OnPressableChanged(
                pressable.Pressable, DateTime.Now, pressable.Backfilled);
            _globalReplayRecorder?.RecordAction(
                "darkGiftPressable",
                pressable.CardId,
                pressable.EntityId,
                3,
                _cachedState != null ? _cachedState.Turn : -1,
                "powerlog");
            Log(string.Format("Dark Gift: pressable={0} entity={1} backfilled={2}",
                pressable.Pressable, pressable.EntityId, pressable.Backfilled));
            OnPowerLogStateChanged();
        }

        /// <summary>Power.log 饰品选择事件: 来自ChoiceList(含MagicItem/Trinket的选项)</summary>
        private void OnPowerLogTrinketChoiceActive(Engine.PowerLogChoiceBatch batch)
        {
            if (DispatchToUiThread(() => OnPowerLogTrinketChoiceActive(batch))) return;
            var candidates = batch?.Candidates;
            if (candidates == null) return;
            if (candidates.Count == 0)
            {
                // 空列表 = 饰品选择结束 (ChoiceList=CHOSEN)
                if (_trinketChoiceLifecycle.Current != null && batch.ChoiceId >= 0
                    && _trinketChoiceLifecycle.Current.Batch?.ChoiceId != batch.ChoiceId) return;
                _trinketChoiceLifecycle.Reset();
                _trinketPanelActive = false; _trinketPanelActivatedAt = DateTime.MinValue;
                _trinketPanelState.Transition(Engine.PanelPhase.Expired);
                if (_cachedState != null) _lastTrinketHideTurn = _cachedState.Turn;
                _trinketTargetState.CompleteChoice();
                _globalReplayRecorder?.RecordChoiceCompleted(
                    "trinket",
                    batch.ChoiceId,
                    "",
                    _cachedState != null ? _cachedState.Turn : -1);
                Log("Trinket panel: Power.log signaled choice completed, clearing");
                OnPowerLogStateChanged();
                return;
            }
            int observedTurn = _cachedState != null ? _cachedState.Turn : -1;
            var binding = _trinketChoiceLifecycle.ObserveSeason14(
                batch, observedTurn, _season14RulesEvaluator);
            if (binding == null)
            {
                Log("Trinket panel: skipped by Season14 mechanic profile");
                return;
            }
            _globalReplayRecorder?.RecordChoice(
                "trinket",
                batch.ChoiceId,
                batch.TaskList,
                batch.SourceCardId,
                candidates.Select(c => c.CardId),
                observedTurn);
            _trinketPanelActive = true; _trinketPanelActivatedAt = DateTime.UtcNow;
            Log(string.Format("Trinket panel: choice={0} source={1} observedTurn={2} targetTurn={3} context={4} signaled {5} options [{6}]",
                batch.ChoiceId, batch.SourceCardId, observedTurn, binding.TargetTurn, binding.Context, candidates.Count,
                string.Join(", ", candidates.Select(c =>
                    ResolveDisplayName(c.CardName, c.CardId)))));
            OnPowerLogStateChanged();
        }

        /// <summary>Power.log 选择完成 — 只清理 choiceId 匹配的面板。</summary>
        private void OnPowerLogChoiceCompleted(Engine.PowerLogChoiceCompletion completion)
        {
            if (DispatchToUiThread(() => OnPowerLogChoiceCompleted(completion))) return;
            if (completion == null) return;
            if (_extractor != null)
                _extractor.ObserveSecondHeroPowerChoiceSelection(
                    completion.ChoiceId, completion.SelectedCardId, "power_log");
            if (_discoverPanelActive && _discoverBatchFromLog != null
                && _discoverBatchFromLog.ChoiceId == completion.ChoiceId)
            {
                _discoverPanelActive = false; _discoverPanelActivatedAt = DateTime.MinValue;
                _discoverBatchFromLog = null;
                _discoverTargetState.CompleteChoice();
                _globalReplayRecorder?.RecordChoiceCompleted(
                    "discover",
                    completion.ChoiceId,
                    completion.SelectedCardId,
                    _cachedState != null ? _cachedState.Turn : -1);
                Log(string.Format("Discover panel: choice={0} completed selected={1}",
                    completion.ChoiceId, completion.SelectedCardId));
            }
            if (_timewarpPurchaseBatchFromLog != null
                && _timewarpPurchaseBatchFromLog.ChoiceId == completion.ChoiceId)
            {
                _timewarpPurchaseBatchFromLog = null;
                _timewarpPurchaseClearRequested = true;
                _globalReplayRecorder?.RecordChoiceCompleted(
                    "timewarp",
                    completion.ChoiceId,
                    completion.SelectedCardId,
                    _cachedState != null ? _cachedState.Turn : -1);
                Log(string.Format("Timewarp purchase: choice={0} completed selected={1}",
                    completion.ChoiceId, completion.SelectedCardId));
            }
            var completedTrinket = _trinketChoiceLifecycle.Complete(completion);
            if (completedTrinket != null)
            {
                if (_trinketShadowEnabled)
                    TrinketShadowComplete(completedTrinket, completion);
                _trinketPanelActive = false; _trinketPanelActivatedAt = DateTime.MinValue;
                _renderer.ClearTrinketHints();
                _trinketPanelState = default(Engine.PanelState);
                if (_cachedState != null) _lastTrinketHideTurn = _cachedState.Turn;
                _trinketTargetState.CompleteChoice();
                _globalReplayRecorder?.RecordChoiceCompleted(
                    "trinket",
                    completion.ChoiceId,
                    completion.SelectedCardId,
                    _cachedState != null ? _cachedState.Turn : -1);
                Log(string.Format("Trinket panel: choice={0} completed selected={1} context={2}",
                    completion.ChoiceId, completion.SelectedCardId, completedTrinket.Context));
            }
            OnPowerLogStateChanged();
        }

        private void OnPowerLogTeammateGoldTransfer(Engine.PLEvent evt)
        {
            if (DispatchToUiThread(() => OnPowerLogTeammateGoldTransfer(evt))) return;
            var state = _cachedState;
            var rule = state != null && state.EffectiveRules != null
                ? state.EffectiveRules.TeammateGoldTransfer : null;
            if (_extractor == null || state == null || !state.IsDuos || rule == null
                || evt == null) return;
            int turn = evt.Turn > 0 ? evt.Turn : state.Turn;
            string evidenceId = evt.EntityId + "@" + evt.Timestamp.Ticks;
            if (_extractor.ObserveTeammateGoldTransfer(
                rule, turn, evt.CardId, evt.EntityId, evidenceId, "power_log"))
            {
                _globalReplayRecorder?.RecordAction(
                    "teammateGold",
                    evt.CardId,
                    evt.EntityId,
                    rule.GoldPerUse,
                    turn,
                    "powerlog");
                OnPowerLogStateChanged();
            }
        }

        /// <summary>Power.log 原始事件流 → 紧凑回放事件（动作/战斗，不写原始行）。</summary>
        private void OnPowerLogEventDetected(Engine.PLEvent evt)
        {
            if (DispatchToUiThread(() => OnPowerLogEventDetected(evt))) return;
            if (evt == null || _globalReplayRecorder == null) return;
            int turn = evt.Turn > 0
                ? evt.Turn
                : (_cachedState != null ? _cachedState.Turn : 0);
            string phase = string.IsNullOrEmpty(_powerLogPhase)
                ? (evt.Value ?? "")
                : _powerLogPhase;

            if (evt.PlayerId > 0)
            {
                _powerEntityPlayers[evt.EntityId] = evt.PlayerId;
                if (!string.IsNullOrEmpty(evt.Zone))
                    _powerEntityZones[evt.EntityId] = evt.Zone;
                if (!string.IsNullOrEmpty(evt.CardId))
                    _powerEntityCards[evt.EntityId] = evt.CardId;
            }

            if (evt.Type == Engine.PLEventType.Attack)
            {
                string defCard = evt.TargetCardId;
                if (string.IsNullOrEmpty(defCard) && evt.TargetId > 0)
                    _powerEntityCards.TryGetValue(evt.TargetId, out defCard);
                _globalReplayRecorder.RecordCombatEvent(
                    "attack", evt.SourceId, evt.CardId, evt.PlayerId,
                    evt.TargetId, defCard, evt.TargetPlayerId, 0, 0, turn, phase);
                return;
            }
            if (evt.Type == Engine.PLEventType.Damage)
            {
                _globalReplayRecorder.RecordCombatEvent(
                    "damage", 0, "", 0, evt.EntityId, evt.CardId, evt.PlayerId,
                    evt.NumericValue, evt.BeforeHealth, turn, phase);
                return;
            }
            if (evt.Type == Engine.PLEventType.MinionDeath)
            {
                _globalReplayRecorder.RecordCombatEvent(
                    "death", 0, "", 0, evt.EntityId, evt.CardId, evt.PlayerId,
                    0, 0, turn, phase);
                return;
            }
            if (evt.Type == Engine.PLEventType.HeroPowerUsed)
            {
                if (IsLocalPowerEvent(evt))
                    _globalReplayRecorder.RecordPowerAction(
                        "heroPower", evt.CardId, evt.EntityId, "", 0, turn, phase);
                return;
            }
            if (evt.Type == Engine.PLEventType.Triple)
            {
                if (IsLocalPowerEvent(evt))
                    _globalReplayRecorder.RecordPowerAction(
                        "triple", evt.NewCardId, evt.EntityId, evt.OldCardId,
                        0, turn, phase);
                return;
            }
            if (evt.Type == Engine.PLEventType.ZoneChange)
            {
                RecordPowerZoneEvent(evt, turn, phase);
                return;
            }
            if (evt.Type == Engine.PLEventType.PlayerAction
                && evt.BlockType == "PLAY"
                && !string.IsNullOrEmpty(evt.CardId))
            {
                RecordPowerActionBlock(evt, turn, phase);
            }
        }

        private void RecordPowerZoneEvent(Engine.PLEvent evt, int turn, string phase)
        {
            if (evt == null || string.IsNullOrEmpty(evt.Value)) return;
            int playerId = evt.PlayerId;
            if (playerId <= 0) _powerEntityPlayers.TryGetValue(evt.EntityId, out playerId);
            if (playerId > 0 && !IsLocalPowerPlayer(playerId)) return;

            string cardId = evt.CardId;
            if (string.IsNullOrEmpty(cardId))
                _powerEntityCards.TryGetValue(evt.EntityId, out cardId);
            if (string.IsNullOrEmpty(cardId)
                || cardId.StartsWith("TB_BaconShop", StringComparison.Ordinal))
                return;

            string oldZone = evt.Zone;
            if (string.IsNullOrEmpty(oldZone))
                _powerEntityZones.TryGetValue(evt.EntityId, out oldZone);
            string newZone = evt.Value;
            _powerEntityZones[evt.EntityId] = newZone;
            _powerEntityCards[evt.EntityId] = cardId;
            if (playerId > 0) _powerEntityPlayers[evt.EntityId] = playerId;

            bool combat = phase != null
                && phase.IndexOf("COMBAT", StringComparison.Ordinal) >= 0;
            if (combat) return;

            string kind = "";
            if (oldZone == "HAND" && newZone == "PLAY") kind = "play";
            else if (oldZone == "PLAY" && newZone == "SETASIDE") kind = "sell";
            if (!string.IsNullOrEmpty(kind))
            {
                if (kind == "play" && IsTavernSpellCard(cardId))
                {
                    _lastUsedTavernSpellCardId = cardId;
                    _lastUsedTavernSpellName = ResolveCardName(cardId);
                }
                _globalReplayRecorder.RecordPowerAction(
                    kind, cardId, evt.EntityId, "", 0, turn, phase);
                OnPowerLogStateChanged();
            }
        }

        private static bool IsTavernSpellCard(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return false;
            Engine.Season14MechanicCardFact fact;
            return TavernSpellLookup.Value.TryGet(cardId, out fact)
                && string.Equals(
                    fact.Type,
                    "BATTLEGROUND_SPELL",
                    StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveCardName(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return "";
            Engine.Season14MechanicCardFact fact;
            if (TavernSpellLookup.Value.TryGet(cardId, out fact))
            {
                if (!string.IsNullOrEmpty(fact.NameZhCn)) return fact.NameZhCn;
                if (!string.IsNullOrEmpty(fact.NameEnUs)) return fact.NameEnUs;
            }
            return CardNameService.GetName(cardId);
        }

        private void RecordPowerActionBlock(Engine.PLEvent evt, int turn, string phase)
        {
            int playerId = evt.PlayerId;
            if (playerId > 0 && !IsLocalPowerPlayer(playerId)) return;

            string cardId = evt.CardId ?? "";
            string targetCardId = evt.TargetCardId ?? "";
            if (string.IsNullOrEmpty(targetCardId) && evt.TargetId > 0)
                _powerEntityCards.TryGetValue(evt.TargetId, out targetCardId);

            if (cardId.StartsWith("TB_BaconShop_DragBuy", StringComparison.Ordinal))
            {
                string kind = cardId.EndsWith("_Spell", StringComparison.Ordinal)
                    ? "spell" : "buy";
                _globalReplayRecorder.RecordPowerAction(
                    kind, targetCardId, evt.TargetId, "", evt.TargetPlayerId,
                    turn, phase);
            }
            else if (cardId.StartsWith("TB_BaconShop_DragSell", StringComparison.Ordinal))
            {
                _globalReplayRecorder.RecordPowerAction(
                    "sell", targetCardId, evt.TargetId, "", evt.TargetPlayerId,
                    turn, phase);
            }
            else if (cardId.IndexOf("Reroll_Button", StringComparison.Ordinal) >= 0
                || cardId.StartsWith("TB_BaconShop_8p_Reroll_Button", StringComparison.Ordinal))
            {
                _globalReplayRecorder.RecordPowerAction(
                    "refresh", cardId, evt.EntityId, "", 0, turn, phase);
            }
            else if (cardId.IndexOf("TechUp", StringComparison.Ordinal) >= 0
                && cardId.IndexOf("_Button", StringComparison.Ordinal) >= 0)
            {
                _globalReplayRecorder.RecordPowerAction(
                    "level", cardId, evt.EntityId, "", 0, turn, phase);
            }
            else if (cardId.IndexOf("Freeze", StringComparison.Ordinal) >= 0
                || cardId.IndexOf("_Frozen_", StringComparison.Ordinal) >= 0)
            {
                _globalReplayRecorder.RecordPowerAction(
                    "freeze", cardId, evt.EntityId, "", 0, turn, phase);
            }
            else if (cardId.IndexOf("BG36_Button_DarkGift", StringComparison.Ordinal) >= 0)
            {
                _globalReplayRecorder.RecordPowerAction(
                    "darkGift", cardId, evt.EntityId, "", 0, turn, phase);
            }
            else if (IsHeroPowerCardId(cardId))
            {
                _globalReplayRecorder.RecordPowerAction(
                    "heroPower", cardId, evt.EntityId, evt.TargetCardId,
                    evt.TargetPlayerId, turn, phase);
            }

            if (cardId.StartsWith("TB_BaconShop", StringComparison.Ordinal)
                || IsHeroPowerCardId(cardId))
                OnPowerLogStateChanged();
        }

        private bool IsLocalPowerEvent(Engine.PLEvent evt)
        {
            if (evt == null) return false;
            return evt.PlayerId <= 0 || IsLocalPowerPlayer(evt.PlayerId);
        }

        private bool IsLocalPowerPlayer(int playerId)
        {
            return _powerLogWatcher == null
                || _powerLogWatcher.LocalControllerId <= 0
                || playerId == _powerLogWatcher.LocalControllerId;
        }

        private static bool IsHeroPowerCardId(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return false;
            return cardId.Contains("_HP_")
                || cardId.Contains("BACON_HERO_POWER")
                || (cardId.Contains("_HERO_") && cardId.EndsWith("p"))
                || cardId.Contains("HeroPower");
        }

        // 2026-08 对抗性审查 M1: PowerLogWatcher 事件在后台线程触发, 回调直接写共享字段/调 WPF 对象
        // (OnPowerLogPhaseChanged 的 _renderer.Clear() 在后台线程被 catch 吞 → 战斗开始 UI 残留)。
        // 所有 OnPowerLog* 回调经此 marshal 到 UI 线程执行: 共享状态读写归一到 UI 线程, 消除竞争,
        // 也让 WPF 对象调用真正生效。回调主体零改动(仅方法首行插派发行)。
        private bool DispatchToUiThread(Action action)
        {
            var app = System.Windows.Application.Current;
            if (app == null) return false; // 无 UI 上下文(单元测试/卸载中): 直接执行
            if (app.Dispatcher.CheckAccess()) return false; // 已在 UI 线程: 直接执行
            try { app.Dispatcher.BeginInvoke(DispatcherPriority.Normal, action); } catch { }
            return true; // 已派发, 调用方应 return
        }

        /// <summary>Power.log 关键状态变化 → 设置flag, UI线程OnUpdate下一帧即时评估</summary>
        private void OnPowerLogStateChanged()
        {
            if (DispatchToUiThread(() => OnPowerLogStateChanged())) return;
            try
            {
                if (!_engineReady || !_inBattlegrounds) return;
                var now = DateTime.UtcNow;
                // 每个事件都置请求；200ms UI定时器负责合并。先置位可避免候选完成事件落在
                // debounce窗口内时被完全丢掉，导致面板只能等下一次HDT OnUpdate。
                _eventEvalRequested = true;
                if ((now - _lastEventEval).TotalMilliseconds < EventEvalDebounceMs) return;
                _lastEventEval = now;
            }
            catch { }
        }

        // ── Log ──

        private static void Log(string msg)
        {
            try
            {
                var bobDir = BobCoachDataPaths.Root;
                System.IO.Directory.CreateDirectory(bobDir);
                var logPath = System.IO.Path.Combine(bobDir, "bob_coach.log");
                var line = string.Format("[{0:O}] [BobCoachPlugin] {1}\n", DateTime.UtcNow, msg);
                // 自动轮转: 超过20MB截断到10000行(后期UI调试留更长历史)
                try {
                    var fi = new System.IO.FileInfo(logPath);
                    if (fi.Exists && fi.Length > 20 * 1024 * 1024)
                    {
                        var all = System.IO.File.ReadAllLines(logPath, System.Text.Encoding.UTF8);
                        if (all.Length > 10000)
                        {
                            var keep = new string[10000];
                            System.Array.Copy(all, all.Length - 10000, keep, 0, 10000);
                            System.IO.File.WriteAllLines(logPath, keep, System.Text.Encoding.UTF8);
                        }
                    }
                } catch { }
                System.IO.File.AppendAllText(logPath, line, System.Text.Encoding.UTF8);
            }
            catch { }
        }

        // Phase0 评测数据专用写入: 独立 eval_dump.jsonl(纯 JSON 每行一条全状态)。
        // 与人读调试日志 bob_coach.log 分离 — 主日志保持精简/快打开, 本文件机器读、上限更大以容纳多局完整对局。
        // 轮转: 超 180MB 保留末尾 15000 条(每条一决策状态≈10KB, 约 45-60 局 / ~1150 买样本)。
        // 上限抬高原因(2026-07-10): 原 6000 行/20MB 只装 ~18-19 局/462 买, 与 λ 终判预登记(需 ≥400~800 全新买)
        //   容量冲突, 会把干净判定集里最老的局轮转挤出。15000 行足以容纳 80% 功效(800买)campaign + dev/holdout 边际。
        private static void EvalLog(string json)
        {
            try
            {
                var bobDir = BobCoachDataPaths.Root;
                System.IO.Directory.CreateDirectory(bobDir);
                var dumpPath = System.IO.Path.Combine(bobDir, "eval_dump.jsonl");
                try
                {
                    var fi = new System.IO.FileInfo(dumpPath);
                    if (fi.Exists && fi.Length > 180L * 1024 * 1024)
                    {
                        var all = System.IO.File.ReadAllLines(dumpPath, System.Text.Encoding.UTF8);
                        if (all.Length > 15000)
                        {
                            var keep = new string[15000];
                            System.Array.Copy(all, all.Length - 15000, keep, 0, 15000);
                            System.IO.File.WriteAllLines(dumpPath, keep, System.Text.Encoding.UTF8);
                        }
                    }
                }
                catch { }
                System.IO.File.AppendAllText(dumpPath, json + "\n", System.Text.Encoding.UTF8);
            }
            catch { }
        }

        // P1.5 Phase4 schema v2: 报价出现时暂存评分，匹配 choiceId 完成后再落盘。
        private void TrinketShadowCapture(GameState state, System.Collections.Generic.List<DecisionEngine.TrinketScore> scores)
        {
            try
            {
                if (!_trinketChoiceLifecycle.TryGetForTurn(state.Turn, out var binding)
                    || binding?.Batch == null) return;
                _trinketShadowCaptureSession.Stage(
                    binding,
                    state.TavernTier,
                    scores[0].IsLesser,
                    state.Health + state.Armor,
                    scores.Select(s => new Engine.TrinketShadowOffer
                    {
                        CardId = s.CardId,
                        Name = s.Name,
                        Score = System.Math.Round(s.Score, 3),
                        IsUnrated = s.IsUnrated,
                    }).ToList());
            }
            catch { }
        }

        private void TrinketShadowComplete(Engine.TrinketChoiceBinding binding,
            Engine.PowerLogChoiceCompletion completion)
        {
            try
            {
                var completed = _trinketShadowCaptureSession.Complete(binding, completion);
                if (completed == null) return;
                var rec = new
                {
                    schemaVersion = completed.SchemaVersion,
                    pluginVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString(),
                    choiceId = completed.ChoiceId,
                    taskList = completed.TaskList,
                    sourceCardId = completed.SourceCardId,
                    selectionContext = completed.SelectionContext,
                    eligibleForCalibration = completed.EligibleForCalibration,
                    selectedCardId = completed.SelectedCardId,
                    completionStatus = completed.CompletionStatus,
                    turn = completed.Turn,
                    tavernTier = completed.TavernTier,
                    lesser = completed.Lesser,
                    health = completed.Health,
                    offers = completed.Offers.Select(o => new
                    {
                        o.CardId,
                        o.Name,
                        score = o.Score,
                        o.IsUnrated,
                    }).ToList(),
                };
                TrinketShadowLog(JsonConvert.SerializeObject(rec));
            }
            catch { }
        }

        private static void TrinketShadowLog(string json)
        {
            try
            {
                var bobDir = BobCoachDataPaths.Root;
                System.IO.Directory.CreateDirectory(bobDir);
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(bobDir, "trinket_shadow.jsonl"), json + "\n", System.Text.Encoding.UTF8);
            }
            catch { }
        }

        private void SaveLearnedRecruitSec()
        {
            try
            {
                var dir = BobCoachDataPaths.Root;
                System.IO.Directory.CreateDirectory(dir);
                var path = System.IO.Path.Combine(dir, "recruit_timing.txt");
                System.IO.File.WriteAllText(path, _learnedRecruitSec.ToString("F1"), System.Text.Encoding.UTF8);
            }
            catch { }
        }

        private void LoadLearnedRecruitSec()
        {
            try
            {
                var path = BobCoachDataPaths.GetPath("recruit_timing.txt");
                if (System.IO.File.Exists(path))
                {
                    double val;
                    if (double.TryParse(System.IO.File.ReadAllText(path).Trim(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out val)
                        && val > 20 && val < 180)
                        _learnedRecruitSec = val;
                }
            }
            catch { }
        }

    }
}
