using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace BobCoach.Engine
{
    /// <summary>
    /// Power.log 文件监控器 — 增量读取+事件解析+回放导出
    /// </summary>
    public class PowerLogWatcher : IDisposable
    {
        private readonly PowerLogParser _parser = new PowerLogParser();
        private readonly List<PLEvent> _events = new List<PLEvent>();
        private string _logPath;
        private long _lastPosition;
        private Thread _watchThread;
        private Thread _startThread;
        private volatile bool _running;
        private volatile bool _startRequested;
        private long _startGeneration;
        private readonly object _lifecycleSync = new object();

        // T2(07071121): Power.log 输入/输出普查 — StopWatching 时打一行, 仲裁 [Power]Verbose 修复
        // 是否让 DebugPrintEntityChoices/ChoiceList 选择块真正出现(gsPower活证明管线在跑; entityChoices=0=输入源缺)。
        private long _censusLines, _censusGsPower, _censusEntityChoices, _censusChoiceList;
        private int _censusRaiseDiscover, _censusRaiseTrinket, _censusRaiseChosen;

        public bool IsWatching => _running;
        public bool IsStarting => _startRequested && !_running;
        public List<PLEvent> Events => _events;
        public int EventCount => _events.Count;
        public PowerLogParser Parser => _parser;
        /// <summary>T3: 路径A(Power.log ChoiceList/EntityChoices)是否正在处理选择 — 活跃时发现门控静默 zone6 启发式</summary>
        public bool IsChoiceListActive => _parser.IsChoiceListActive();
        public LogConfigStatus ConfigStatus { get; private set; }
        public string ConfigMessage { get; private set; }

        /// <summary>从Power.log检测到阶段变化: MAIN_READY, MAIN_COMBAT 等</summary>
        public event Action<string> PhaseChanged;

        /// <summary>从Power.log检测到发现选项 (2-3个候选卡牌, 空列表=发现结束)</summary>
        public event Action<PowerLogChoiceBatch> DiscoverOffered;

        public event Action<PowerLogChoiceBatch> TimewarpPurchaseOffered;

        /// <summary>从Power.log检测到饰品选择选项 (非空=选择窗口打开, 空列表=窗口关闭)</summary>
        public event Action<PowerLogChoiceBatch> TrinketChoiceActive;

        /// <summary>饰品/发现选择完成 (ChoiceList=CHOSEN)</summary>
        public event Action<PowerLogChoiceCompletion> ChoiceCompleted;

        public event Action<PLEvent> TeammateGoldTransferObserved;

        /// <summary>关键状态变化 (ZONE/金币/技能), 驱动UI实时刷新</summary>
        public event Action StateChanged;

        /// <summary>Power.log报告的炉石客户端BuildNumber。</summary>
        public event Action<int> BuildNumberChanged;

        /// <summary>初始化监控</summary>
        public void StartWatching()
        {
            StartWatchingWithConfigInspector(LogConfigEnsurer.Inspect);
        }

        internal void StartWatchingAtConfigPath(string configPath)
        {
            StartWatchingWithConfigInspector(() => LogConfigEnsurer.InspectAtPath(configPath));
        }

        internal void StartWatchingWithConfigInspector(Func<LogConfigPlan> inspectConfig)
        {
            if (inspectConfig == null) throw new ArgumentNullException(nameof(inspectConfig));

            long generation;
            lock (_lifecycleSync)
            {
                if (_running || _startRequested) return;
                _startRequested = true;
                generation = ++_startGeneration;
            }

            // 只读检查；配置写入只能由用户主动点击插件按钮并确认后执行。
            LogConfigPlan configPlan;
            try
            {
                configPlan = inspectConfig();
            }
            catch
            {
                lock (_lifecycleSync)
                {
                    if (_startGeneration == generation) _startRequested = false;
                }
                throw;
            }

            lock (_lifecycleSync)
            {
                if (!_startRequested || _startGeneration != generation) return;

                ConfigStatus = configPlan.Status;
                switch (ConfigStatus)
                {
                    case LogConfigStatus.Missing:
                        ConfigMessage = "log.config缺失；请点击HDT插件列表中的“Bob教练”查看并确认配置";
                        break;
                    case LogConfigStatus.NeedsPatch:
                        ConfigMessage = "log.config不完整；请点击HDT插件列表中的“Bob教练”查看并确认变更";
                        break;
                    case LogConfigStatus.OK:
                        ConfigMessage = null;
                        break;
                    case LogConfigStatus.Error:
                        ConfigMessage = "无法检查log.config，Power.log功能已关闭";
                        break;
                    default:
                        ConfigMessage = "log.config尚未确认，Power.log功能已关闭";
                        break;
                }

                if (ConfigStatus != LogConfigStatus.OK)
                {
                    _startRequested = false;
                    return;
                }
            }

            // 会话目录可能晚于GameStart出现；只做一次同步探测，等待过程移出HDT事件线程。
            _logPath = FindPowerLogPath();
            if (!string.IsNullOrEmpty(_logPath) && File.Exists(_logPath))
            {
                StartResolvedPath(generation);
                return;
            }

            Thread startThread;
            lock (_lifecycleSync)
            {
                if (!_startRequested || _startGeneration != generation) return;
                startThread = new Thread(() => RetryStartWatching(generation))
                {
                    IsBackground = true,
                    Name = "PowerLogStart"
                };
                _startThread = startThread;
            }
            startThread.Start();
        }

        private void RetryStartWatching(long generation)
        {
            for (int retry = 0; retry < 28 && IsStartRequested(generation); retry++)
            {
                Thread.Sleep(500); // 后台轮询，最长约14秒，不阻塞HDT GameStart action
                if (!IsStartRequested(generation)) return;
                var path = FindPowerLogPath();
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
                _logPath = path;
                StartResolvedPath(generation);
                return;
            }
            lock (_lifecycleSync)
            {
                if (!_startRequested || _startGeneration != generation) return;
                _startRequested = false;
                Log("Power.log not found after retries. Last try: " + (_logPath ?? "null"));
                if (ConfigStatus == LogConfigStatus.OK)
                    ConfigMessage = "Power.log未找到, 请确认log.config已配置并重启炉石";
            }
        }

        private bool IsStartRequested(long generation)
        {
            lock (_lifecycleSync)
            {
                return _startRequested && _startGeneration == generation;
            }
        }

        private void StartResolvedPath(long generation)
        {
            lock (_lifecycleSync)
            {
                if (!_startRequested || _startGeneration != generation || _running) return;
                _parser.PhaseDetected += OnPhaseDetected;
                _parser.DiscoverOffered += OnDiscoverOffered;
                _parser.TimewarpPurchaseOffered += OnTimewarpPurchaseOffered;
                _parser.TrinketChoiceActive += OnTrinketChoiceActive;
                _parser.ChoiceCompleted += OnChoiceCompleted;
                _parser.TeammateGoldTransferObserved += OnTeammateGoldTransferObserved;
                _parser.RelevantTagChanged += OnRelevantTagChanged;
                _parser.BuildNumberDetected += OnBuildNumberDetected;

                // watcher通常在GameStart之后才启动，而BuildNumber位于本会话Power.log开头。
                // 只回扫首个BuildNumber，不重放旧游戏事件；随后从文件末尾增量读取。
                ScanInitialBuildNumber();
                try { _lastPosition = new FileInfo(_logPath).Length; } catch { _lastPosition = 0; }
                Log("PowerLogWatcher started: " + _logPath + " at pos " + _lastPosition);

                _running = true;
                _startRequested = false;
                _watchThread = new Thread(() => WatchLoop(generation))
                {
                    IsBackground = true,
                    Name = "PowerLogWatch"
                };
                _watchThread.Start();
            }
        }

        public void StopWatching()
        {
            Thread watchThread;
            Thread startThread;
            lock (_lifecycleSync)
            {
                _startGeneration++;
                _startRequested = false;
                _running = false;
                watchThread = _watchThread;
                startThread = _startThread;
                _parser.PhaseDetected -= OnPhaseDetected;
                _parser.DiscoverOffered -= OnDiscoverOffered;
                _parser.TimewarpPurchaseOffered -= OnTimewarpPurchaseOffered;
                _parser.TrinketChoiceActive -= OnTrinketChoiceActive;
                _parser.ChoiceCompleted -= OnChoiceCompleted;
                _parser.TeammateGoldTransferObserved -= OnTeammateGoldTransferObserved;
                _parser.RelevantTagChanged -= OnRelevantTagChanged;
                _parser.BuildNumberDetected -= OnBuildNumberDetected;
            }
            try
            {
                if (watchThread != null && watchThread != Thread.CurrentThread)
                    watchThread.Join(2000);
            }
            catch { }
            lock (_lifecycleSync)
            {
                if (ReferenceEquals(_watchThread, watchThread)) _watchThread = null;
                if (ReferenceEquals(_startThread, startThread)) _startThread = null;
            }
            Log("PowerLogWatcher stopped. Total events: " + _events.Count);
            Log(string.Format("PL census: lines={0} gsPower={1} entityChoices={2} choiceList={3} | raised: discover={4} trinket={5} chosen={6}",
                _censusLines, _censusGsPower, _censusEntityChoices, _censusChoiceList,
                _censusRaiseDiscover, _censusRaiseTrinket, _censusRaiseChosen));
        }

        private void OnPhaseDetected(string phase)
        {
            try { PhaseChanged?.Invoke(phase); } catch { }
        }

        private void OnDiscoverOffered(PowerLogChoiceBatch batch)
        {
            var candidates = batch?.Candidates;
            Log(string.Format("PowerLog discover: choice={0} source={1} {2} candidates [{3}]",
                batch?.ChoiceId ?? -1, batch?.SourceCardId ?? "",
                candidates?.Count ?? 0,
                candidates != null ? string.Join(", ", candidates.Select(c => c.CardName ?? c.CardId ?? "?")) : "null"));
            if (candidates != null && candidates.Count > 0) _censusRaiseDiscover++;
            try { DiscoverOffered?.Invoke(batch); } catch { }
        }

        private void OnTimewarpPurchaseOffered(PowerLogChoiceBatch batch)
        {
            var candidates = batch?.Candidates;
            Log(string.Format(
                "PowerLog timewarp purchase: choice={0} source={1} coins={2} candidates={3} [{4}]",
                batch?.ChoiceId ?? -1, batch?.SourceCardId ?? "",
                batch?.TimeCoinCount ?? 0, candidates?.Count ?? 0,
                candidates != null
                    ? string.Join(", ", candidates.Select(candidate => string.Format(
                        "{0}:{1}", candidate.CardName ?? candidate.CardId ?? "?",
                        candidate.PurchaseCost)))
                    : "null"));
            try { TimewarpPurchaseOffered?.Invoke(batch); } catch { }
        }

        private void OnTrinketChoiceActive(PowerLogChoiceBatch batch)
        {
            var candidates = batch?.Candidates;
            Log(string.Format("PowerLog trinket choice: choice={0} source={1} {2} candidates [{3}]",
                batch?.ChoiceId ?? -1, batch?.SourceCardId ?? "",
                candidates?.Count ?? 0,
                candidates != null ? string.Join(", ", candidates.Select(c => c.CardName ?? c.CardId ?? "?")) : "null"));
            if (candidates != null && candidates.Count > 0) _censusRaiseTrinket++;
            try { TrinketChoiceActive?.Invoke(batch); } catch { }
        }

        private void OnChoiceCompleted(PowerLogChoiceCompletion completion)
        {
            Log(string.Format("PowerLog choice completed: choice={0} selected={1}",
                completion?.ChoiceId ?? -1, completion?.SelectedCardId ?? ""));
            _censusRaiseChosen++;
            try { ChoiceCompleted?.Invoke(completion); } catch { }
        }

        private void OnTeammateGoldTransferObserved(PLEvent evt)
        {
            try { TeammateGoldTransferObserved?.Invoke(evt); } catch { }
        }

        private void OnRelevantTagChanged()
        {
            try { StateChanged?.Invoke(); } catch { }
        }

        private void OnBuildNumberDetected(int build)
        {
            Log("PowerLog BuildNumber=" + build);
            try { BuildNumberChanged?.Invoke(build); } catch { }
        }

        private void ScanInitialBuildNumber()
        {
            try
            {
                if (!PowerLogInitialBuildScanner.TryScan(_logPath, _parser))
                    Log("PowerLog initial BuildNumber not found");
            }
            catch (Exception ex) { Log("PowerLog initial BuildNumber scan failed: " + ex.Message); }
        }

        /// <summary>游戏结束时导出完整回放JSON</summary>
        public string ExportReplay(string heroId, string heroName, int finalRank, int tripleCount,
            double boardPowerPeak, List<TurnSnapshot> turnSnapshots)
        {
            try
            {
                var bobDir = BobCoachDataPaths.Root;
                var replayDir = Path.Combine(bobDir, "replays");
                Directory.CreateDirectory(replayDir);
                var path = Path.Combine(replayDir,
                    string.Format("replay_full_{0:yyyyMMdd_HHmmss}_{1}.json",
                    DateTime.Now, (heroId ?? "unknown").Replace("TB_BaconShop_HERO_", "")));

                FastJsonWriter.Write(path, new
                {
                    hero = heroId, heroName = heroName, finalRank = finalRank,
                    tripleCount = tripleCount, boardPowerPeak = boardPowerPeak,
                    timestamp = DateTime.UtcNow,
                    totalEvents = _events.Count,
                    events = _events.Select(e => new
                    {
                        type = e.Type.ToString(),
                        ts = e.Timestamp.ToString("HH:mm:ss.fff"),
                        entityId = e.EntityId, entityName = e.EntityName,
                        cardId = e.CardId, tag = e.Tag, value = e.Value,
                        sourceId = e.SourceId, targetId = e.TargetId,
                        blockType = e.BlockType, turn = e.Turn,
                        oldCardId = e.OldCardId, newCardId = e.NewCardId,
                    }),
                    turns = turnSnapshots,
                });
                Log("Replay exported: " + path + " (" + _events.Count + " events, " +
                    (turnSnapshots?.Count ?? 0) + " snapshots)");
                _events.Clear();
                return path;
            }
            catch (Exception ex) { Log("ExportReplay error: " + ex.Message); return null; }
        }

        public void Dispose() { StopWatching(); }

        // ── 内部 ──

        private void WatchLoop(long generation)
        {
            while (IsRunning(generation))
            {
                try
                {
                    if (!File.Exists(_logPath)) { Thread.Sleep(500); continue; }
                    using (var fs = new FileStream(_logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        if (fs.Length < _lastPosition) { _lastPosition = fs.Length; }
                        fs.Seek(_lastPosition, SeekOrigin.Begin);
                        using (var reader = new StreamReader(fs))
                        {
                            string line;
                            while ((line = reader.ReadLine()) != null)
                            {
                                _censusLines++;
                                if (line.IndexOf("DebugPrintPower", StringComparison.Ordinal) >= 0) _censusGsPower++;
                                if (line.IndexOf("DebugPrintEntityChoices", StringComparison.Ordinal) >= 0) _censusEntityChoices++;
                                if (line.IndexOf("ChoiceList=", StringComparison.Ordinal) >= 0) _censusChoiceList++;
                                var events = _parser.ParseLine(line);
                                lock (_events) { _events.AddRange(events); }
                            }
                            _lastPosition = fs.Position;
                        }
                    }
                }
                catch { }
                Thread.Sleep(200);
            }
        }

        private bool IsRunning(long generation)
        {
            lock (_lifecycleSync)
            {
                return _running && _startGeneration == generation;
            }
        }

        private static string FindPowerLogPath()
        {
            // 0. 通过 LogConfigEnsurer 精确定位 (现已支持团子版会话子目录)
            var directPath = LogConfigEnsurer.FindPowerLog();
            if (!string.IsNullOrEmpty(directPath) && File.Exists(directPath))
            {
                Log("Power.log found (LogConfigEnsurer): " + directPath);
                return directPath;
            }

            // 1. 读 HDT 配置获取炉石安装目录
            try
            {
                var hdtConfigPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "HearthstoneDeckTracker", "config.xml");
                if (File.Exists(hdtConfigPath))
                {
                    var xml = File.ReadAllText(hdtConfigPath);
                    var match = System.Text.RegularExpressions.Regex.Match(
                        xml, @"<HearthstoneDirectory>([^<]+)</HearthstoneDirectory>");
                    if (match.Success)
                    {
                        var hsDir = match.Groups[1].Value.Trim();
                        var logsDir = Path.Combine(hsDir, "Logs");
                        if (Directory.Exists(logsDir))
                        {
                            // 找最新 Hearthstone_* 会话子目录
                            string newestDir = null;
                            DateTime newestTime = DateTime.MinValue;
                            foreach (var dir in Directory.GetDirectories(logsDir, "Hearthstone_*"))
                            {
                                try
                                {
                                    var di = new DirectoryInfo(dir);
                                    if (di.LastWriteTime > newestTime)
                                    { newestTime = di.LastWriteTime; newestDir = dir; }
                                }
                                catch { }
                            }
                            if (newestDir != null)
                            {
                                // 优先 Power_old.log (含 DebugPrintEntityChoices)
                                var powerOldPath = Path.Combine(newestDir, "Power_old.log");
                                if (File.Exists(powerOldPath))
                                {
                                    Log("Power.log found (HDT session): " + powerOldPath);
                                    return powerOldPath;
                                }
                                var sessionPath = Path.Combine(newestDir, "Power.log");
                                if (File.Exists(sessionPath))
                                {
                                    Log("Power.log found (HDT session): " + sessionPath);
                                    return sessionPath;
                                }
                            }
                            // 回退: 直接路径
                            var direct = Path.Combine(logsDir, "Power.log");
                            if (File.Exists(direct))
                            {
                                Log("Power.log found (HDT direct): " + direct);
                                return direct;
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { Log("HDT config read error: " + ex.Message); }

            // 2. LOCALAPPDATA (国际版兼容)
            var candidates = new List<string>();
            var localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            if (!string.IsNullOrEmpty(localAppData))
                candidates.Add(Path.Combine(localAppData, @"Blizzard\Hearthstone\Logs\Power.log"));

            // 2. 直接尝试几个盘符的用户目录
            foreach (var drive in new[] { "C:", "D:", "E:", "F:" })
            {
                var usersDir = drive + @"\Users";
                if (!Directory.Exists(usersDir)) continue;
                try
                {
                    foreach (var userDir in Directory.GetDirectories(usersDir))
                    {
                        candidates.Add(Path.Combine(userDir, @"AppData\Local\Blizzard\Hearthstone\Logs\Power.log"));
                    }
                }
                catch { }
            }

            // 3. HDT安装目录附近
            try
            {
                var exeDir = AppDomain.CurrentDomain.BaseDirectory;
                if (!string.IsNullOrEmpty(exeDir))
                {
                    // 上溯查找Hearthstone目录
                    var dir = exeDir;
                    for (int i = 0; i < 5 && dir != null; i++)
                    {
                        var hsLog = Path.Combine(dir, @"Hearthstone\Logs\Power.log");
                        if (File.Exists(hsLog)) candidates.Add(hsLog);
                        dir = Path.GetDirectoryName(dir);
                    }
                }
            }
            catch { }

            // 4. 遍历所有candidates
            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    Log("Power.log found: " + candidate);
                    return candidate;
                }
            }

            Log("Power.log not found. Candidates tried: " + string.Join("; ", candidates.Take(5)));
            return null;
        }

        private static void Log(string msg)
        {
            try
            {
                var bobDir = BobCoachDataPaths.Root;
                Directory.CreateDirectory(bobDir);
                File.AppendAllText(Path.Combine(bobDir, "bob_coach.log"),
                    string.Format("[{0:O}] [PowerLog] {1}\n", DateTime.UtcNow, msg),
                    System.Text.Encoding.UTF8);
            }
            catch { }
        }
    }
}
