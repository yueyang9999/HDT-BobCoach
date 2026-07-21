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
        private FeatureExtractor _fe;
        private OverlayRenderer _renderer;
        private CardPoolTracker _poolTracker;
        private ProbabilityCalculator _probCalc;
        private ProfileEngine _profileEngine;
        private HeroPowerEngine _heroPowerEngine;
        private DecisionVisualizer _visualizer;
        private GameRecord _currentGameRecord;
        private DecisionMode _decisionMode;
        private Engine.PowerLogWatcher _powerLogWatcher;
        private bool _powerLogEventsSubscribed;
        private bool _inBattlegrounds;
        private bool _engineReady;
        private string _lastStateHash;
        private string _lastEvalDumpSig; // Phase0评测: 全状态转储节流签名
        private readonly Engine.TrinketShadowCaptureSession _trinketShadowCaptureSession =
            new Engine.TrinketShadowCaptureSession();
        // P1.5 Phase4(路线B冻结, 被动shadow): 默认关, 环境变量 BOBCOACH_TRINKET_SHADOW=1 开启。
        // 零副作用只读采集T6/T9饰品报价+引擎评分, 为未来B解冻(~300条)积累样本。类初始化读一次, 关时零帧开销。
        private static readonly bool _trinketShadowEnabled =
            Environment.GetEnvironmentVariable("BOBCOACH_TRINKET_SHADOW") == "1";
        private const bool TrinketRecommendationsVisible = false;
        private string _lastRenderedHash;
        private string _lastRenderedPlanHash;  // 防重复dispatch
        private bool _suggestionsActive;
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
        private readonly HashSet<int> _claimedUpgradePrizeChoiceIds = new HashSet<int>();
        private readonly Engine.TrinketChoiceBatchLifecycle _trinketChoiceLifecycle =
            new Engine.TrinketChoiceBatchLifecycle();
        private int _prevDiscoverCount = -1; // 上帧发现选项数, 仅用于诊断
        // v2 事件驱动标志位: Power.log ChoiceList 事件直接控制面板显隐, 替代TTL帧计数
        private bool _discoverPanelActive = false;
        private bool _trinketPanelActive = false;
        private bool _lastTrinketShowState = false;    // 面板显隐状态变化追踪
        private int _lastTrinketHideTurn = -1;
        private bool _lastDiscoverShowState = false;
        // 面板生命周期状态机(持久持有, 跨帧存活) — GameState 每帧重建无法持有, 这是状态机曾失效的根因
        private Engine.PanelState _trinketPanelState = new Engine.PanelState(Engine.PanelPhase.Idle, 1, 1500);
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
        private string _lastShopContentHash = ""; // 上次商店内容指纹(检测刷新)
        private int _lastShopRefreshTier = 1;     // 上次刷新时的酒馆等级
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
        private int _renderVersion;
        private bool _wasInCombat = false;           // 阶段追踪
        private DateTime _lastShopChange = DateTime.MinValue;
        private DateTime _lastShopUiClearAt = DateTime.MinValue;
        private const int ShopUiClearCooldownMs = 250;
        private bool _lastFrameFrozen = false;       // 冻结跨回合UI保护
        private int _lastRenderTurn = 0;
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
        public System.Windows.Controls.MenuItem MenuItem { get { return null; } }

        public void OnLoad()
        {
            Log("BobCoach v0.2.0 loading...");

            if (!SecuritySelfCheck())
            {
                Log("Security self-check FAILED.");
                return;
            }

            try
            {
                _extractor = new GameStateExtractor();
                _engine = new DecisionEngine();
                _fe = new FeatureExtractor();
                _renderer = new OverlayRenderer();
                _poolTracker = new CardPoolTracker();
                _probCalc = new ProbabilityCalculator(_poolTracker);
                _fe.SetProbabilityCalculator(_probCalc);
                _profileEngine = new ProfileEngine();
                _heroPowerEngine = _engine.HeroPower;
                _extractor.SetHeroPowerEngine(_heroPowerEngine);
                _extractor.SetAnomalyRegistry(_engine.AnomalyReg);
                _extractor.SetTrinketFactSource(_engine.TrinketFactSource);
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
            ClearSuggestions();
            try { UnsubscribePowerLogWatcherEvents(); } catch { }
            try { _powerLogWatcher?.StopWatching(); } catch { }
            Log("BobCoach unloaded");
        }

        public void OnButtonPress()
        {
            try
            {
                Log("Power.log configuration assistant requested");
                var plan = Engine.LogConfigEnsurer.Inspect();
                if (plan.Status == Engine.LogConfigStatus.OK)
                {
                    MessageBox.Show("Power.log 所需的 log.config 已完整，无需修改。\n\n" + plan.ConfigPath,
                        "Bob教练 Power.log 配置", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                if (plan.Status == Engine.LogConfigStatus.Error)
                {
                    MessageBox.Show("无法定位或读取 Hearthstone log.config。\n\n" +
                        string.Join("\n", plan.Changes ?? new List<string>()),
                        "Bob教练 Power.log 配置", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var message = "Bob教练需要以下 Power.log 配置。只有点击“是”才会写入。\n\n" +
                    "文件：" + plan.ConfigPath + "\n\n" +
                    "变更：\n- " + string.Join("\n- ", plan.Changes) + "\n\n" +
                    "拟议完整内容：\n" + plan.ProposedContent + "\n\n是否应用？";
                var result = MessageBox.Show(message, "Bob教练 Power.log 配置",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes)
                {
                    Log("Power.log configuration declined; no file changed");
                    return;
                }

                var status = Engine.LogConfigEnsurer.Apply(plan);
                if (status == Engine.LogConfigStatus.Created || status == Engine.LogConfigStatus.Patched)
                {
                    MessageBox.Show("log.config 已按确认内容写入。请完全关闭并重新启动炉石后生效。",
                        "Bob教练 Power.log 配置", MessageBoxButton.OK, MessageBoxImage.Information);
                    Log("Power.log configuration applied: " + status);
                }
                else if (status == Engine.LogConfigStatus.Conflict)
                {
                    MessageBox.Show("确认期间 log.config 已被其他程序修改，本次未覆盖。请重新点击 Bob教练检查。",
                        "Bob教练 Power.log 配置", MessageBoxButton.OK, MessageBoxImage.Warning);
                    Log("Power.log configuration conflict; no file changed");
                }
                else
                {
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
                    _wasInCombat = true;
                    _lastStateHash = null; _lastRenderedHash = null; _lastShopIdStr = "";
                    _lastShopCount = 0; _shopStableFrames = 0;
                    if (_demoMode.Active) { _demoMode.Deactivate(); Log("DEMO OFF (combat)"); }
                    _extractor.CollectOpponentIds();
                    _updateSkippedCombat++;
                    return;
                }
                // 战斗结束检测: 记录时间, 发现扫描冷却2秒防战斗衍生物
                if (_wasInCombat) _combatEndedAt = DateTime.Now;
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
                        if (ct != 4 && ct != 5) continue;
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
                        int layoutTier = _lastShopRefreshTier > 0 ? _lastShopRefreshTier : _cachedState.TavernTier;
                        _renderer.SyncShopTagPositions(_cachedState.ShopMinions, layoutTier, _cachedState.ReplenishingShopActive);
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

            // 商店实体过渡期 ID 会连续抖动，限制全量 Clear 频率，避免标签/状态条反复闪烁。
            _renderer.Clear();
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
            catch (Exception ex) { Log("Security check error: " + ex); return true; }
        }

        // ── Game events ──

        private void OnGameStart()
        {
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
                _extractor.Reset();  // 清除上局幽灵实体累积
                _visualizer.Reset();  // 重置流派锁+手牌标记缓存
                _purchaseDetector.Reset();  // 重置购买检测
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
                _claimedUpgradePrizeChoiceIds.Clear();
                _trinketChoiceLifecycle.Reset();
                // 跨局重置面板状态机回 Idle(防止上局 Active/Fading 残留)
                _trinketPanelState = new Engine.PanelState(Engine.PanelPhase.Idle, 1, 1500);
                _discoverPanelState = new Engine.PanelState(Engine.PanelPhase.Idle, 1, 1000);
                _trinketTargetState.Reset();
                _discoverTargetState.Reset();
                _lastHeroHintTurn = 0;
                _lastShopContentHash = "";
                _lastShopRefreshTier = 1;

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

                _currentGameRecord = new GameRecord();
                _prevState = null;  // 清空上一帧状态
                // 启动Power.log监控；这里只读检查log.config，不自动修改。
                if (_powerLogWatcher == null) _powerLogWatcher = new Engine.PowerLogWatcher();
                SubscribePowerLogWatcherEvents();
                _powerLogWatcher.StartWatching();

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
            if (_currentGameRecord != null)
            {
                int placement = ExtractFinalPlacement();
                if (placement > 0 && placement <= 8)
                    _currentGameRecord.FinalRank = placement;
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
                        }).ToList()
                    });
                    Log("Turn snapshot saved: " + snapPath + " (" + _currentGameRecord.Turns.Count + " turns)");
                }
                catch (Exception ex2) { Log("Snapshot save error: " + ex2.Message); }
            }

            Log("BG game ended");
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
                        if (e.Tag == "PLAYER_LEADERBOARD_PLACE" && e.NumericValue >= 1 && e.NumericValue <= 8)
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
                    foreach (var entity in Core.Game.Entities.Values.ToList())
                    {
                        if (entity == null || !entity.IsControlledBy(1)) continue;
                        if (!entity.IsHero && !entity.CardId.StartsWith("TB_BaconShop_HERO")) continue;
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
                    if (Core.Game != null && Core.Game.IsBattlegroundsMatch)
                    {
                        _inBattlegrounds = true;
                        Log("OnTurnStart: detected BG match");
                    }
                }
                catch { }
            }
            if (_inBattlegrounds && _engineReady)
            {
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

        // ── Decision evaluation & render ──

        private void EvaluateAndRender()
        {
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

            var state = _extractor.Extract();
            if (state == null) return;

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
            if (trinketSource == Engine.UiTargetSource.None)
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
            Engine.PanelStateMachine.Advance(ref _discoverPanelState, smDiscoverActive, state.Turn, enforceMaxActive: false);

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

            // 刷新感知: 商店内容变化 → 记录刷新时的酒馆等级
            var shopHash = string.Join(",", state.ShopMinions.Select(m => m.EntityId));
            if (shopHash != _lastShopContentHash && state.ShopMinions.Count > 0)
            {
                _lastShopContentHash = shopHash;
                _lastShopRefreshTier = state.TavernTier;
                _renderer.SetCalcTier(state.TavernTier);
            }

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
            }
            state.LastUpgradeTurn = _lastUpgradeTurn;

            // V1.2: 英雄技能提示（HeroPowerEngine 替代硬编码）
            // 修复4: 被动英雄不显示静态技能描述，仅主动技能有使用建议时才显示
            var heroStrategy = _heroPowerEngine.GetStrategy(state.HeroCardId);
            string heroHint = heroStrategy.PowerType == HeroPowerType.Passive
                ? "" : HeroPowerDisplayTextResolver.Resolve(state.HeroPowerCardId);
            // 动态技能使用建议: 根据当前金币+场面判断是否该用技能
            string heroUseSuggestion = _heroPowerEngine.GetUseSuggestion(
                state.HeroCardId, state.Gold, state.Turn,
                state.BoardMinions.Count, state.BoardMinions.Count >= 7);

            // T1/T2 空商店：仅显示英雄提示，不跑决策引擎
            if (state.Turn <= 2 && state.ShopMinions.Count == 0 && state.Phase == "shop")
            {
                var waitingPlan = _visualizer.CreateWaitingPlan(state);
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

            var bestAction = SafeCall(() => _engine.GetBestAction(state), "GetBestAction");
            var t0 = DateTime.UtcNow;
            var levelResult = SafeCall(() => _engine.GetLevelUpSuggestion(state), "GetLevelUpSuggestion");
            var cardScores = SafeCall(() => _engine.GetShopCardScores(state), "GetShopCardScores") ?? new List<ShopCardScore>();
            var compGuidance = SafeCall(() => _engine.GetCompGuidance(state), "GetCompGuidance");

            var trinketScores = _engine.EvaluateTrinkets(state);
            // gold=0/1时清除二步前瞻+抑制不必要推荐(没钱就别推荐花金操作)
            var secondStep = _engine.BestTwoStepAction;
            var secondStepState = _engine.BestTwoStepState;
            if (state.Gold == 0) { secondStep = null; secondStepState = null; }
            cardScores = FilterAffordableShopScores(state, cardScores);
            if (bestAction != null
                && (bestAction.Type == ActionType.BuyMinion || bestAction.Type == ActionType.BuySpell)
                && !IsShopActionAffordable(state, bestAction, state.Gold))
            {
                bestAction = null;
            }
            var plan = _visualizer.CreateVisualPlan(state, bestAction, levelResult, cardScores, _heroPowerEngine, _fe, compGuidance,
                secondStep, secondStepState, trinketScores);
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

            Log(string.Format("Eval:{0:F0}ms act={1} lvl={2} hand={3}/{4} shop={5} comp={6}",
                tEval, bestAction?.Type.ToString() ?? "-", levelResult.Suggestion,
                plan.HandMarker?.Index.ToString() ?? "-", state.HandMinions.Count,
                plan.ShopMarkers.Count, plan.Status.CompDir));
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

            // ── 回放快照: 记录逐回合完整状态(含随从属性/对手/手牌) ──
            if (_currentGameRecord != null)
            {
                // 推断玩家本回合操作 (对比上一帧状态)
                List<string> actions = new List<string>();
                if (_prevState != null && _prevState.Turn == state.Turn)
                {
                    // 买牌: 商店出现过的卡现在在手中或板面
                    var prevShopIds = new HashSet<string>(_prevState.ShopMinions.Select(m => m.EntityId.ToString()));
                    var prevBoardIds = new HashSet<string>(_prevState.BoardMinions.Select(m => m.CardId));
                    var prevHandIds = new HashSet<string>(_prevState.HandMinions.Select(m => m.CardId));
                    var currBoardIds = state.BoardMinions.Select(m => m.CardId).ToList();
                    var currHandIds = state.HandMinions.Select(m => m.CardId).ToList();

                    // 买牌: 手牌/板面新增卡 (不在上一帧中)
                    foreach (var hm in state.HandMinions)
                        if (!prevHandIds.Contains(hm.CardId) && !prevBoardIds.Contains(hm.CardId))
                            actions.Add("buy:" + hm.CardId + "→手牌");
                    foreach (var bm in state.BoardMinions)
                        if (!prevBoardIds.Contains(bm.CardId) && !prevHandIds.Contains(bm.CardId))
                            actions.Add("buy/play:" + bm.CardId + "→板面");

                    // 卖牌: 上一帧板面有但当前没有
                    foreach (var pm in _prevState.BoardMinions)
                        if (!currBoardIds.Contains(pm.CardId) && !currHandIds.Contains(pm.CardId))
                            actions.Add("sell:" + pm.CardId);

                    // 打出手牌: 上一帧在手牌, 当前在板面
                    foreach (var hm in _prevState.HandMinions)
                        if (currBoardIds.Contains(hm.CardId) && !currHandIds.Contains(hm.CardId))
                            actions.Add("play:" + hm.CardId);

                    // 金币变化
                    int goldDelta = state.Gold - _prevState.Gold;
                    if (goldDelta < 0) actions.Add("gold:" + goldDelta);
                    else if (goldDelta > 0 && goldDelta != 3) actions.Add("gold:+" + goldDelta);

                    // 法术购买检测: 法术买入即用不入板面/手牌, 需从商店消失+金币减少推断
                    var currShopCardIds = new HashSet<string>(state.ShopMinions.Select(m => m.CardId));
                    var prevShopSpells = _prevState.ShopMinions
                        .Where(m => m.IsSpell && !currShopCardIds.Contains(m.CardId))
                        .ToList();
                    foreach (var spell in prevShopSpells)
                    {
                        // 法术从商店消失了 + 没有出现在手牌/板面 + 金币减少了 → 法术购买
                        bool appearedInHand = state.HandMinions.Any(m => m.CardId == spell.CardId);
                        bool appearedOnBoard = state.BoardMinions.Any(m => m.CardId == spell.CardId);
                        if (!appearedInHand && !appearedOnBoard)
                            actions.Add("spell:" + spell.CardId + "(" + (spell.CardName ?? "") + ")");
                    }
                }
                // 升本
                if (state.TavernTier != (_prevState != null ? _prevState.TavernTier : 1))
                    actions.Add("level:→" + state.TavernTier + "本");
                // 检测阶段
                try { if (Core.Game.IsBattlegroundsCombatPhase) actions.Add("phase:combat"); else actions.Add("phase:recruit"); } catch { }

                var snap = new TurnSnapshot
                {
                    Turn = state.Turn, Gold = state.Gold, MaxGold = state.MaxGold,
                    Tier = state.TavernTier, Health = state.Health, Armor = state.Armor,
                    BoardSize = state.BoardMinions.Count, ShopSize = state.ShopMinions.Count,
                    HandSize = state.HandMinions.Count,
                    BoardPower = _fe.ComputeBoardPower(state.BoardMinions),
                    OpponentPower = state.Opponents.Count > 0
                        ? _fe.ComputeAvgOpponentPower(state.Opponents) : 0,
                    Board = state.BoardMinions.Select(m => new MinionSnapshot
                    {
                        CardId = m.CardId, Name = m.CardName ?? "",
                        Attack = m.Attack, Health = m.Health, Tier = m.Tier, Position = m.Position,
                        Golden = m.Golden, Taunt = m.Taunt, DivineShield = m.DivineShield,
                        Reborn = m.Reborn, Poisonous = m.Poisonous, Venomous = m.Venomous,
                        Windfury = m.Windfury, Tribe = m.Tribe ?? "", IsSpell = m.IsSpell, Cost = m.Cost,
                    }).ToList(),
                    Shop = state.ShopMinions.Select(m => new MinionSnapshot
                    {
                        CardId = m.CardId, Name = m.CardName ?? "",
                        Attack = m.Attack, Health = m.Health, Tier = m.Tier, Position = m.Position,
                        Golden = m.Golden, Taunt = m.Taunt, DivineShield = m.DivineShield,
                        Reborn = m.Reborn, Poisonous = m.Poisonous, Venomous = m.Venomous,
                        Windfury = m.Windfury, MegaWindfury = m.MegaWindfury,
                        Stealth = m.Stealth, Cleave = m.Cleave, Overkill = m.Overkill,
                        Tribe = m.Tribe ?? "", IsSpell = m.IsSpell, Cost = m.Cost,
                    }).ToList(),
                    Hand = state.HandMinions.Select(m => new MinionSnapshot
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
                        Board = o.BoardMinions.Select(m => new MinionSnapshot
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
                    FeatureVector = SerializeFeatureVector(state),
                    RecommendedActionJson = SerializeRecommendedAction(bestAction),
                    ActionScoresJson = SerializeActionScores(state, bestAction, cardScores),
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
            DispatchRender(state, heroHint, plan, heroUseSuggestion);
        }

        // ── 训练数据序列化 ──

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

        /// <summary>序列化所有动作评分列表为紧凑JSON (含推荐标记)</summary>
        private string SerializeActionScores(GameState state, GameAction bestAction, System.Collections.Generic.List<ShopCardScore> cardScores)
        {
            if (state == null) return "[]";
            var items = new System.Collections.Generic.List<string>();

            var enumerator = new Engine.ActionEnumerator();
            var actions = enumerator.Enumerate(state, state.HeroCardId);
            var fe = _engine.GetFeatureExtractor();
            var sim = new Engine.Simulator();
            var vf = _engine.GetValueFunction();

            foreach (var a in actions)
            {
                float score = 0f;
                if (fe != null && vf != null)
                {
                    var ns = sim.Simulate(state, a);
                    var fv = fe.Extract(ns);
                    score = vf.Evaluate(fv);
                }
                bool isRecommended = bestAction != null
                    && a.Type == bestAction.Type
                    && a.TargetIndex == bestAction.TargetIndex;

                items.Add(string.Format("{{\"type\":\"{0}\",\"target\":{1},\"score\":{2},\"recommended\":{3}}}",
                    a.Type, a.TargetIndex,
                    score.ToString("F4", System.Globalization.CultureInfo.InvariantCulture),
                    isRecommended ? "true" : "false"));
            }

            return "[" + string.Join(",", items) + "]";
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
            _powerLogWatcher.TrinketChoiceActive += OnPowerLogTrinketChoiceActive;
            _powerLogWatcher.ChoiceCompleted += OnPowerLogChoiceCompleted;
            _powerLogWatcher.TeammateGoldTransferObserved += OnPowerLogTeammateGoldTransfer;
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
            _powerLogWatcher.TrinketChoiceActive -= OnPowerLogTrinketChoiceActive;
            _powerLogWatcher.ChoiceCompleted -= OnPowerLogChoiceCompleted;
            _powerLogWatcher.TeammateGoldTransferObserved -= OnPowerLogTeammateGoldTransfer;
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

        private List<ShopCardScore> FilterAffordableShopScores(GameState state, List<ShopCardScore> scores)
        {
            if (scores == null || scores.Count == 0) return scores ?? new List<ShopCardScore>();
            var filtered = new List<ShopCardScore>();
            foreach (var score in scores)
            {
                if (IsShopScoreAffordable(state, score, state != null ? state.Gold : 0))
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

        private bool IsShopCardAffordable(GameState state, Engine.MinionData shopCard, int gold)
        {
            if (state == null || shopCard == null) return false;
            int cost = GameRuleEvaluator.GetPurchaseCost(
                state, shopCard, state.HeroCardId,
                state.EffectiveRules ?? EffectiveGameRules.Default);
            return gold >= cost;
        }

        private int GetMinionBuyCost(GameState state)
        {
            if (state == null) return 3;
            return GameRuleEvaluator.GetPurchaseCost(
                state, new MinionData { Tier = 1 }, state.HeroCardId,
                state.EffectiveRules ?? EffectiveGameRules.Default);
        }

        private void DispatchRender(GameState state, string heroHint, VisualPlan plan, string heroUseSuggestion = null)
        {
            if (state == null || plan == null) return;
            // 防重复渲染: plan内容未变化时跳过
            // 包含ShopMarkers的EntityId+ShopPosition，确保位置变化触发重绘
            var markerIds = plan.ShopMarkers.Count > 0
                ? "|M" + string.Join(",", plan.ShopMarkers.Select(m => m.EntityId + ":" + m.ShopPosition))
                : "";
            string planHash = state.Turn + "|" + plan.RecommendedActionType + "|"
                + plan.ShopMarkers.Count + markerIds + "|" + (plan.UpgradeHint?.Level.ToString() ?? "-") + "|"
                + (plan.HandMarker?.CardName ?? "-") + "|" + (plan.TrinketHints?.Count ?? 0) + "|"
                + (plan.FreezeHint?.Active == true ? "F" : "-") + "|"
                + (int)_trinketPanelState.Phase + "_" + (int)_discoverPanelState.Phase
                // 0711: 连续选择可能在一次 EvaluateAndRender 间隔内完成旧批次并开启新批次。
                // 此时面板 Phase/候选数均不变；批次身份必须进入 hash，否则第二批提示被去重吞掉。
                + "|" + (_trinketTargetState.State.BatchId ?? "-")
                + "|" + (_discoverTargetState.State.BatchId ?? "-")
                // 07062242: 金币归零/恢复边界必须触发重绘 — gold=0后若plan其余不变则不dispatch,
                // 旧刷新光晕残留(T8末段gold=0仍见"找核心牌"提示的根因)
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
                    int liveGold = state.Gold;
                    try { var pe = Core.Game.PlayerEntity; if (pe != null) { int r = pe.GetTag(GameTag.RESOURCES); if (r >= 0 && r <= 20) liveGold = r; } } catch { }
                    bool inCombat = false;
                    try { inCombat = Core.Game.IsBattlegroundsCombatPhase; } catch { }
                    // 07062107修复: 回调被拦截=本plan未上屏, 清hash让同plan下帧重试(见DispatchRender头部注释)
                    if (inCombat) { _lastRenderedPlanHash = null; return; }
                    if (capturedVersion != _renderVersion) { _lastRenderedPlanHash = null; return; }
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

                    // ── 状态条（始终显示）──
                    _renderer.ShowStatusStrip(status);

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
                        hpStrat = _heroPowerEngine.GetStrategy(state.HeroCardId);
                    bool isActiveHP = hpStrat != null && hpStrat.PowerType == HeroPowerType.Active;
                    bool canAffordHP = liveGold >= state.HeroPowerCost;
                    bool hpNotUsed = !state.HeroPowerExhausted;
                    bool hasUseSuggestion = !string.IsNullOrEmpty(useSug);
                    // 技能熄灭卫士: EXHAUSTED/费用不够/被动技能→清光晕
                    bool hpShouldGlow = isActiveHP && hasUseSuggestion && canAffordHP && hpNotUsed
                        && !string.IsNullOrEmpty(state.HeroPowerCardId);
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
                    bool discoverShouldShow = discoverPs.IsVisible;

                    if (discoverShouldShow != _lastDiscoverShowState)
                    {
                        Log(string.Format("UI Discover: {0} (phase={1} options={2})",
                            discoverShouldShow ? "SHOW" : "HIDE", discoverPs.Phase,
                            state.DiscoverOptions?.Count ?? 0));
                        _lastDiscoverShowState = discoverShouldShow;
                    }
                    if (discoverShouldShow)
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

                    // 无操作场景：商店空+无发现+无饰品+无手牌推荐 → 不显示任何指导
                    bool noActionNeeded = state.ShopMinions.Count == 0
                        && state.DiscoverOptions.Count == 0
                        && (state.TrinketOffer?.Count ?? 0) == 0
                        && plan.HandMarker == null;
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
                        _suggestionsActive = true;
                        return;
                    }

                    // ── 金币不足时仅清除不可支付目标, 保留低费法术和首购免费 ──
                    int minBuyCost = GetMinionBuyCost(state);
                    bool canBuyMinion = liveGold >= minBuyCost;
                    bool hasAffordableShopMarker = plan.ShopMarkers.Any(m => IsShopMarkerAffordable(state, m, liveGold));
                    bool canRefresh = liveGold >= 1 || state.FreeRefreshCount > 0;
                    if (!hasAffordableShopMarker)
                    {
                        plan.ShopMarkers.RemoveAll(m => !IsShopMarkerAffordable(state, m, liveGold));
                        plan.HandMarker = null;
                        if (plan.RecommendedActionType == "BuyMinion" || plan.RecommendedActionType == "BuySpell")
                            plan.RecommendedActionType = "";
                    }
                    else
                    {
                        plan.ShopMarkers.RemoveAll(m => !IsShopMarkerAffordable(state, m, liveGold));
                    }
                    if (!canRefresh && !hasAffordableShopMarker)
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
                    bool levelUrgent = plan.UpgradeHint != null && plan.UpgradeHint.Level == DecisionLevel.Critical;
                    bool levelOk = plan.UpgradeHint != null && liveGold >= plan.UpgradeHint.Cost;
                    int goldAfterUpgrade = levelOk ? liveGold - plan.UpgradeHint.Cost : liveGold;
                    bool canBuyAfterUpgrade = goldAfterUpgrade >= 3;
                    // 1. 升本提示 — 铸币不足时清除商店标记避免冲突指导
                    // 升级信息仅通过按钮光晕(ShowLevelUpGlow)显示, 状态栏不重复
                    if (!shownPrimary && levelUrgent)
                    {
                        shownPrimary = true;
                        if (!canBuyAfterUpgrade)
                        {
                            plan.ShopMarkers.Clear();
                            plan.HandMarker = null;
                            plan.RecommendedActionType = "Upgrade";
                        }
                    }
                    else if (!shownPrimary && levelOk && (!canBuyAfterUpgrade || plan.ShopMarkers.Count == 0))
                    {
                        shownPrimary = true;
                        if (!canBuyAfterUpgrade) plan.ShopMarkers.Clear();
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
                            plan.Status.HintLine = "打→" + (plan.HandMarker.CardName ?? "");
                        }
                        // 有商店推荐时: ShopMarkers优先, 手牌作为次要(不清除标签)
                    }
                    // 3. 商店买牌 (保持CreateVisualPlan已过滤的标记, 不额外缩减)
                    if (!shownPrimary && plan.ShopMarkers.Count > 0)
                    {
                        shownPrimary = true;
                        var topCard = plan.ShopMarkers.OrderByDescending(m => m.Score).First();
                        plan.Status.HintLine = "拿→" + (topCard.CardName ?? "随从");
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
                        && (canBuyMinion || levelOk) && liveGold >= 1)
                    {
                        shownPrimary = true;
                        if (plan.SellMarkers[0].BoardIndex >= 0
                            && plan.SellMarkers[0].BoardIndex < state.BoardMinions.Count)
                            plan.Status.HintLine = "卖→" + (state.BoardMinions[plan.SellMarkers[0].BoardIndex].CardName ?? "?");
                        else
                            plan.Status.HintLine = "卖";
                    }
                    // 兜底: 无任何目标时, 不显示空动作
                    if (!shownPrimary && plan.ShopMarkers.Count == 0
                        && plan.UpgradeHint == null && plan.HandMarker == null
                        && plan.SellMarkers.Count == 0
                        && (plan.FreezeHint == null || !plan.FreezeHint.Active))
                    {
                        plan.RecommendedActionType = "";
                    }

                    // ── 商店卡片高亮（从VisualPlan读取）──
                    // 布局数量使用酒馆理论槽位, 并被 live 最大原始槽位撑开。
                    // ShopPosition 保留 ZONE_POSITION-1, 不能用实际卡数压缩成 dense index。
                    bool loggedShopDiag = false;
                    int layoutTier = _lastShopRefreshTier > 0 ? _lastShopRefreshTier : state.TavernTier;
                    int tierSlots = layoutTier <= 1 ? 4 : (layoutTier <= 3 ? 5 : (layoutTier <= 5 ? 6 : 7));
                    int liveCardCount = state.ShopMinions.Count > 0 ? state.ShopMinions.Count : tierSlots;
                    int maxRawShopSlot = state.ShopMinions.Count > 0 ? state.ShopMinions.Max(m => m.Position) : -1;
                    bool denseReplenishingShop = state.ReplenishingShopActive && state.ShopMinions.Count > 0;
                    // 07072158(用户确认B: 买卡后向中心轴靠拢=始终按实际在场卡数居中)。所有商店按实际卡数居中。
                    // ⚠偏差根因是居中【基准】而非居中逻辑: 游戏招募区中轴≠屏幕正中(偏左约一卡), 由全局 ShopOffsetX 校准一次对齐(F10 Shop 方向键←)。
                    bool denseVisibleShop = true;
                    int slotCountForLayout = Math.Max(1, Math.Min(7, state.ShopMinions.Count));
                    var denseShopSlots = state.ShopMinions
                        .OrderBy(sm => sm.Position)
                        .ThenBy(sm => sm.EntityId)
                        .Select((sm, idx) => new { sm.EntityId, DenseSlot = idx })
                        .ToDictionary(x => x.EntityId, x => x.DenseSlot);
                    foreach (var m in plan.ShopMarkers)
                    {
                        float normScore = (float)m.Score;
                        string reason = "";
                        if (!loggedShopDiag)
                        {
                            Log(string.Format("DIAG ShopRender: markers={0} tier={1} layoutTier={2} liveCards={3} shopCards={4} layoutSlots={5} maxRawSlot={6} dense={7}",
                                plan.ShopMarkers.Count, state.TavernTier, layoutTier, liveCardCount, state.ShopMinions.Count,
                                slotCountForLayout, maxRawShopSlot, denseVisibleShop));
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

                    // ── 升本宝石: UpgradeHint存在就显示(现场重读金币) ──
                    if (plan.UpgradeHint != null)
                    {
                        bool canAfford = liveGold >= plan.UpgradeHint.Cost;
                        bool urgent = plan.UpgradeHint.Level == DecisionLevel.Critical;
                        bool t7 = state.EffectiveRules != null && state.EffectiveRules.MaxTavernTier >= 7;
                        string reason = canAfford ? plan.UpgradeHint.Reason
                            : string.Format("需{0}费(差{1})", plan.UpgradeHint.Cost, plan.UpgradeHint.Cost - state.Gold);
                        _renderer.ShowLevelUpGlow(canAfford && urgent, reason,
                            plan.UpgradeHint.Cost, plan.UpgradeHint.CurrentTier, t7);
                    }

                    // ── 刷新建议: 店无好牌时提示刷新, 金币够就用 ──
                    var liveRules = state.EffectiveRules ?? EffectiveGameRules.Default;
                    int liveRefreshCost = GameRuleEvaluator.GetRefreshCost(state, state.HeroCardId, liveRules);
                    bool canAffordRefresh = liveRules.ManualRefreshAllowed && state.Gold >= liveRefreshCost;
                    bool earlySuppressRefresh = state.Turn <= 5 && state.FreeRefreshCount == 0;
                    bool badShop = plan.ShopMarkers.Count == 0 && plan.UpgradeHint == null
                        && state.ShopMinions.Count > 0 && !earlySuppressRefresh;
                    bool needRefresh = plan.RecommendedActionType.Equals("Refresh", StringComparison.OrdinalIgnoreCase);
                    bool showRefresh = liveRules.ManualRefreshAllowed
                        && (needRefresh || badShop) && canAffordRefresh;
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

                    // ── 冻结提示: 推荐冻结时在刷新按钮右侧显示蓝色提示框 ──
                    bool showFreeze = plan.FreezeHint != null && plan.FreezeHint.Active;
                    if (!showFreeze) { _freezeGlowSince = DateTime.MinValue; }
                    else
                    {
                        if (_freezeGlowSince == DateTime.MinValue) _freezeGlowSince = DateTime.Now;
                        if ((DateTime.Now - _freezeGlowSince).TotalSeconds < 10.0)
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
                    if (_combatWatchTimer == null)
                    {
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
                                bool nowInCombat = Core.Game.IsBattlegroundsCombatPhase;
                                if (nowInCombat)
                                {
                                    if (_timewarpPurchaseBatchFromLog != null)
                                    {
                                        _eventEvalRequested = false;
                                        RenderTimewarpPurchaseHint();
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
                    TrinketName = c.CardName ?? c.CardId,
                    EntityId = c.EntityId,
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
                TrinketName = candidate.CardName ?? candidate.CardId,
                EntityId = candidate.EntityId,
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
                TrinketName = candidate.CardName ?? candidate.CardId,
                EntityId = candidate.EntityId,
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
            int bestIndex = Engine.TimewarpPurchaseAdvisor.SelectBestAffordableIndex(
                batch, scoresByIndex);
            if (bestIndex < 0 || bestIndex >= batch.Candidates.Count)
            {
                _renderer.ClearTimewarpPurchaseHint();
                return;
            }

            var best = batch.Candidates[bestIndex];
            _renderer.RefreshCanvasSize();
            _renderer.ClearDiscoverHints();
            _lastDiscoverShowState = false;
            _renderer.ShowTimewarpPurchaseRating(
                bestIndex, batch.Candidates.Count,
                best.CardName ?? best.CardId ?? "?",
                best.PurchaseCost, batch.TimeCoinCount);
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
                    Engine.TrinketFact fact;
                    try
                    {
                        if (_engine == null || _engine.TrinketFactSource == null
                            || !_engine.TrinketFactSource.TryGet(candidate.CardId, out fact)
                            || !string.Equals(fact.CardId, candidate.CardId, StringComparison.Ordinal))
                            return state.TrinketOffer != null && state.TrinketOffer.Count >= 2
                                ? Engine.UiTargetSource.Entity : Engine.UiTargetSource.None;
                    }
                    catch
                    {
                        return state.TrinketOffer != null && state.TrinketOffer.Count >= 2
                            ? Engine.UiTargetSource.Entity : Engine.UiTargetSource.None;
                    }
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
            var scored = new List<(int idx, double score, string reason)>();
            for (int i = 0; i < state.DiscoverOptions.Count; i++)
            {
                var d = state.DiscoverOptions[i];
                double sc = 3.0;
                string reason = "";

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
                if (tier >= 5) { sc += 4; reason = "高星"; }
                else if (tier >= 4) { sc += 2.5; reason = "优质"; }
                else if (tier >= 3) sc += 1;

                // 流派匹配
                if (!string.IsNullOrEmpty(compDir))
                {
                    string dn = d.TrinketName ?? "";
                    if (dn.Contains(compDir))
                    { sc += 3; reason = "匹配流派"; }
                }

                // 关键词加成
                string name = d.TrinketName ?? "";
                if (name.Contains("铜须") || name.Contains("达瑞尔") || name.Contains("瑞文"))
                { sc += 3; reason = "引擎"; }

                // 发现来源差异化: 三连(+25%) > 英雄技能(+12%) > 战吼/法术(基准)
                double sourceMult = _discoverSource == "triple" ? 1.25
                    : _discoverSource == "prize" ? 1.18
                    : _discoverSource == "heroPower" ? 1.12 : 1.0;
                scored.Add((i, sc * sourceMult, reason));
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
                _lastCombatPrediction = Engine.BobsBuddyBridge.Simulate(atkBoard, defBoard, simCount: 2000);
                if (_lastCombatPrediction != null)
                {
                    Log(string.Format("CombatPredict: win={0:F1}% tie={1:F1}% lose={2:F1}% dmg={3:F1} sims={4}",
                        _lastCombatPrediction.WinRate * 100,
                        _lastCombatPrediction.TieRate * 100,
                        _lastCombatPrediction.LossRate * 100,
                        _lastCombatPrediction.AvgDamageTaken,
                        _lastCombatPrediction.SimulationCount));
                }
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
                    var pe = Core.Game.PlayerEntity;
                    if (pe != null)
                    {
                        var res = pe.GetTag(GameTag.RESOURCES);
                        // gold=0 是合法值(买完随从铸币归零): 用 >=0 门控, 否则 res=0 被当默认3,
                        // 状态哈希与买卡前同帧 → OnUpdate hash门控跳过重评 → gold=0 残留旧购买推荐UI(0708 Bug2根因)
                        if (res >= 0 && res <= 20) gold = res;
                    }
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
                        if (!e.IsInPlay && !e.IsInHand && e.Health > 0
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
            try
            {
                Log("Current game BuildNumber=" + build);
            }
            catch (Exception ex) { Log("Build update error: " + ex.Message); }
        }

        /// <summary>Power.log 发现候选事件: 来自ChoiceList或FULL_ENTITY批量检测</summary>
        private void OnPowerLogDiscoverOffered(Engine.PowerLogChoiceBatch batch)
        {
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
            _discoverBatchFromLog = batch;
            _discoverPanelActive = true; _discoverPanelActivatedAt = DateTime.UtcNow;
            _lastDiscoverTriggerTime = DateTime.UtcNow;
            Log(string.Format("Discover panel: choice={0} source={1} signaled {2} candidates [{3}]",
                batch.ChoiceId, batch.SourceCardId, candidates.Count,
                string.Join(", ", candidates.Select(c => c.CardName ?? c.CardId))));
            OnPowerLogStateChanged();
        }

        private void OnPowerLogTimewarpPurchaseOffered(Engine.PowerLogChoiceBatch batch)
        {
            if (batch == null || batch.ChoiceId < 0 || batch.Candidates == null
                || batch.Candidates.Count < 2) return;
            _timewarpPurchaseBatchFromLog = batch;
            _timewarpPurchaseClearRequested = false;
            Log(string.Format(
                "Timewarp purchase: choice={0} source={1} coins={2} candidates=[{3}]",
                batch.ChoiceId, batch.SourceCardId, batch.TimeCoinCount,
                string.Join(", ", batch.Candidates.Select(candidate => string.Format(
                    "{0}:{1}", candidate.CardName ?? candidate.CardId ?? "?",
                    candidate.PurchaseCost)))));
            OnPowerLogStateChanged();
        }

        /// <summary>Power.log 饰品选择事件: 来自ChoiceList(含MagicItem/Trinket的选项)</summary>
        private void OnPowerLogTrinketChoiceActive(Engine.PowerLogChoiceBatch batch)
        {
            var candidates = batch?.Candidates;
            if (candidates == null) return;
            if (candidates.Count == 0)
            {
                // 空列表 = 饰品选择结束 (ChoiceList=CHOSEN)
                if (_trinketChoiceLifecycle.Current != null && batch.ChoiceId >= 0
                    && _trinketChoiceLifecycle.Current.Batch?.ChoiceId != batch.ChoiceId) return;
                _trinketChoiceLifecycle.Reset();
                _trinketPanelActive = false; _trinketPanelActivatedAt = DateTime.MinValue;
                if (_cachedState != null) _lastTrinketHideTurn = _cachedState.Turn;
                _trinketTargetState.CompleteChoice();
                Log("Trinket panel: Power.log signaled choice completed, clearing");
                OnPowerLogStateChanged();
                return;
            }
            int observedTurn = _cachedState != null ? _cachedState.Turn : -1;
            var binding = _trinketChoiceLifecycle.Observe(batch, observedTurn);
            _trinketPanelActive = true; _trinketPanelActivatedAt = DateTime.UtcNow;
            Log(string.Format("Trinket panel: choice={0} source={1} observedTurn={2} targetTurn={3} context={4} signaled {5} options [{6}]",
                batch.ChoiceId, batch.SourceCardId, observedTurn, binding.TargetTurn, binding.Context, candidates.Count,
                string.Join(", ", candidates.Select(c => c.CardName ?? c.CardId))));
            OnPowerLogStateChanged();
        }

        /// <summary>Power.log 选择完成 — 只清理 choiceId 匹配的面板。</summary>
        private void OnPowerLogChoiceCompleted(Engine.PowerLogChoiceCompletion completion)
        {
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
                Log(string.Format("Discover panel: choice={0} completed selected={1}",
                    completion.ChoiceId, completion.SelectedCardId));
            }
            if (_timewarpPurchaseBatchFromLog != null
                && _timewarpPurchaseBatchFromLog.ChoiceId == completion.ChoiceId)
            {
                _timewarpPurchaseBatchFromLog = null;
                _timewarpPurchaseClearRequested = true;
                Log(string.Format("Timewarp purchase: choice={0} completed selected={1}",
                    completion.ChoiceId, completion.SelectedCardId));
            }
            var completedTrinket = _trinketChoiceLifecycle.Complete(completion);
            if (completedTrinket != null)
            {
                if (_trinketShadowEnabled)
                    TrinketShadowComplete(completedTrinket, completion);
                _trinketPanelActive = false; _trinketPanelActivatedAt = DateTime.MinValue;
                if (_cachedState != null) _lastTrinketHideTurn = _cachedState.Turn;
                _trinketTargetState.CompleteChoice();
                Log(string.Format("Trinket panel: choice={0} completed selected={1} context={2}",
                    completion.ChoiceId, completion.SelectedCardId, completedTrinket.Context));
            }
            OnPowerLogStateChanged();
        }

        private void OnPowerLogTeammateGoldTransfer(Engine.PLEvent evt)
        {
            var state = _cachedState;
            var rule = state != null && state.EffectiveRules != null
                ? state.EffectiveRules.TeammateGoldTransfer : null;
            if (_extractor == null || state == null || !state.IsDuos || rule == null
                || evt == null) return;
            int turn = evt.Turn > 0 ? evt.Turn : state.Turn;
            string evidenceId = evt.EntityId + "@" + evt.Timestamp.Ticks;
            if (_extractor.ObserveTeammateGoldTransfer(
                rule, turn, evt.CardId, evt.EntityId, evidenceId, "power_log"))
                OnPowerLogStateChanged();
        }

        /// <summary>Power.log 关键状态变化 → 设置flag, UI线程OnUpdate下一帧即时评估</summary>
        private void OnPowerLogStateChanged()
        {
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
