using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BobCoach.Engine
{
    /// <summary>Power.log 事件类型</summary>
    public enum PLEventType
    {
        TagChange, FullEntity, ShowEntity, ChangeEntity, HideEntity,
        BlockStart, BlockEnd, CreateGame,
        PlayerAction, Attack, Battlecry, Deathrattle, Trigger,
        Damage, Heal, ZoneChange, Transform, Triple,
        GoldChange, TavernUpgrade, PhaseChange, TurnStart,
        ArmorChange, HealthChange, MinionDeath, CardRevealed,
        HeroPowerUsed, TrinketOffered, TrinketSelected, HeroPowerOffered,
    }

    /// <summary>Power.log 解析后的事件</summary>
    public class PLEvent
    {
        public PLEventType Type;
        public DateTime Timestamp;
        public int EntityId; public string EntityName = ""; public string CardId = "";
        public string Tag = ""; public string Value = "";
        public int SourceId; public int TargetId;
        public string BlockType = "";
        public string OldCardId = ""; public string NewCardId = "";
        public int Turn; public int NumericValue;
        public string RawLine = "";
    }

    /// <summary>Power.log 一次完整选择的身份与候选快照。</summary>
    public sealed class PowerLogChoiceBatch
    {
        public int ChoiceId = -1;
        public int TaskList;
        public string PlayerName = "";
        public string ChoiceType = "";
        public string SourceCardId = "";
        public string SourceName = "";
        public int SourcePlayerId;
        public int TimeCoinCount;
        public DateTime Timestamp;
        public List<DiscoverCandidate> Candidates = new List<DiscoverCandidate>();
    }

    /// <summary>SendChoices 对某一选择批次的确定完成结果。</summary>
    public sealed class PowerLogChoiceCompletion
    {
        public int ChoiceId = -1;
        public string ChoiceType = "";
        public int SelectedEntityId;
        public string SelectedCardId = "";
        public string SelectedName = "";
        public int SelectedPlayerId;
        public DateTime Timestamp;
    }

    /// <summary>
    /// Power.log 逐行解析器 — 提取所有 TAG_CHANGE / BLOCK / ENTITY 事件
    /// </summary>
    public class PowerLogParser
    {
        private readonly Dictionary<int, PLEntityState> _entities = new Dictionary<int, PLEntityState>();
        private readonly Dictionary<string, int> _alternateCurrencyByPlayer =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private int _currentTurn;
        private string _currentPhase = "shop";
        private int _currentBuildNumber;

        // 发现检测: 缓冲FULL_ENTITY候选, 2-3个同批次触发
        private readonly List<DiscoverCandidate> _discoverBuffer = new List<DiscoverCandidate>();
        private DateTime _lastDiscoverCandidateTime = DateTime.MinValue;
        private string _currentBlockType = null; // 当前BLOCK类型(POWER/RITUAL/TRIGGER), 用于发现门控
        private static readonly TimeSpan DiscoverBatchWindow = TimeSpan.FromMilliseconds(2000);

        // ChoiceList解析: Power.log原生的发现/选择信号, 精确度远高于zone6扫描
        private readonly List<int> _pendingChoiceEntityIds = new List<int>();
        private int _pendingChoiceId = -1;
        private bool _inChoiceBlock = false;
        private DateTime _lastChoiceContextAt = DateTime.MinValue;
        private int _pendingSendChoiceId = -1;
        private string _pendingSendChoiceType = "";
        private DateTime _pendingSendChoiceAt = DateTime.MinValue;

        /// <summary>从Power.log检测到阶段变化时触发 (MAIN_READY=招募, MAIN_COMBAT=战斗)</summary>
        public event Action<string> PhaseDetected;

        /// <summary>检测到发现选项时触发 (2-3个候选卡牌, 空列表=发现结束)</summary>
        public event Action<PowerLogChoiceBatch> DiscoverOffered;

        /// <summary>时空扭曲替代货币购买批次。</summary>
        public event Action<PowerLogChoiceBatch> TimewarpPurchaseOffered;

        /// <summary>检测到饰品选择选项时触发 (非空=选择窗口打开, 空列表=窗口关闭)</summary>
        public event Action<PowerLogChoiceBatch> TrinketChoiceActive;

        /// <summary>饰品/发现选择完成时触发 (ChoiceList=CHOSEN)</summary>
        public event Action<PowerLogChoiceCompletion> ChoiceCompleted;

        /// <summary>资源汇集技能的精确POWER块；只表示本地观察到一次真实发送动作。</summary>
        public event Action<PLEvent> TeammateGoldTransferObserved;

        /// <summary>关键状态变化时触发 (ZONE/RESOURCES等, 用于驱动实时UI刷新)</summary>
        public event Action RelevantTagChanged;

        /// <summary>DebugPrintGame中的客户端构建号；变化时触发一次。</summary>
        public event Action<int> BuildNumberDetected;

        public int CurrentBuildNumber { get { return _currentBuildNumber; } }

        public List<PLEvent> ParseLine(string line)
        {
            var results = new List<PLEvent>();
            if (string.IsNullOrWhiteSpace(line)) return results;
            // HDT 35.6+ 使用 GameState.DebugPrintPower() 承载 FULL_ENTITY/TAG_CHANGE/BLOCK_START 等游戏事件
            // GameState.DebugPrintEntityChoices() 承载发现/选择事件（比ChoiceList=更可靠，因为不需要log.config特定模块）
            // PowerTaskList.DebugPrintPower() 承载实体属性初始化
            if (!line.Contains("PowerTaskList") && !line.Contains("PowerProcessor")
                && !line.Contains("DebugPrintPower") && !line.Contains("DebugPrintEntityChoices")
                && !line.Contains("DebugPrintGame") && !line.Contains("SendChoices")) return results;

            var ts = ExtractTimestamp(line);
            string content = ExtractContent(line);

            var buildMatch = Regex.Match(content, @"\bBuildNumber=(\d+)\b");
            if (buildMatch.Success && int.TryParse(buildMatch.Groups[1].Value, out var build)
                && build > 0 && build != _currentBuildNumber)
            {
                _currentBuildNumber = build;
                try { BuildNumberDetected?.Invoke(build); } catch { }
                return results;
            }

            if (content.StartsWith("TAG_CHANGE"))
                results.Add(ParseTagChange(content, ts));
            else if (content.StartsWith("FULL_ENTITY"))
            {
                var fe = ParseFullEntity(content, ts);
                results.Add(fe);
                // 发现候选检测: 实体创建后检查是否属于发现批次
                TryDetectDiscover(fe, ts);
            }
            else if (content.StartsWith("SHOW_ENTITY"))
                results.Add(ParseShowEntity(content, ts));
            else if (content.StartsWith("CHANGE_ENTITY"))
                results.Add(ParseChangeEntity(content, ts));
            else if (content.StartsWith("HIDE_ENTITY"))
            {
                var he = ParseHideEntity(content, ts);
                results.Add(he);
                // 实体隐藏 → 可能清除了发现候选
                if (he.EntityId > 0) CheckDiscoverCleanup(he.EntityId);
            }
            else if (content.StartsWith("BLOCK_START"))
            {
                var bs = ParseBlockStart(content, ts);
                PLEntityState blockEntity;
                if (bs.BlockType == "POWER" && bs.EntityId > 0)
                {
                    if (string.IsNullOrEmpty(bs.CardId)
                        && _entities.TryGetValue(bs.EntityId, out blockEntity))
                        bs.CardId = blockEntity.CardId ?? "";
                    bs.Turn = _currentTurn;
                    if (bs.CardId == "BGDUO_Anomaly_007t")
                    {
                        bs.Type = PLEventType.HeroPowerUsed;
                        try { TeammateGoldTransferObserved?.Invoke(bs); } catch { }
                    }
                }
                results.Add(bs);
                _currentBlockType = bs.BlockType; // 追踪当前BLOCK类型用于发现门控
                // 超时清除过期缓冲
                if (_discoverBuffer.Count > 0
                    && (ts - _lastDiscoverCandidateTime) > DiscoverBatchWindow)
                    _discoverBuffer.Clear();
            }
            else if (content.StartsWith("BLOCK_END"))
            {
                results.Add(new PLEvent { Type = PLEventType.BlockEnd, Timestamp = ts });
                // Choice block结束: 发现选择完成, 清除缓冲
                if (_inChoiceBlock && _discoverBuffer.Count > 0)
                {
                    _inChoiceBlock = false;
                    _pendingChoiceEntityIds.Clear();
                    _pendingChoiceId = -1;
                }
                _currentBlockType = null;
            }
            else if (content.Contains("ChoiceList="))
            {
                var ch = ParseChoiceLine(content, ts);
                if (ch != null) results.Add(ch);
            }
            else if (line.Contains("DebugPrintEntityChoices()"))
            {
                // 07071603根因: ExtractContent 用 LastIndexOf("- ") 剥掉了 "...DebugPrintEntityChoices() - " 前缀,
                // 故分发判断必须用【原始 line】(剥离后的 content 已不含此标识 → 旧的 content.Contains 恒 false,
                // 整条 EntityChoices 解析链从分发层就断了: 83条选择行读到却 0 raise)。
                // GameState.DebugPrintEntityChoices: 发现/选择的核心信号
                // 格式: DebugPrintEntityChoices() - id=N Player=XXX TaskList=N ChoiceType=GENERAL CountMin=N CountMax=N
                //   Source=... Entities[N]=[entityName=XXX id=N zone=SETASIDE cardId=BGXX_X player=N]
                // 比ChoiceList=更可靠，因为它在Power_old.log中默认启用，不依赖log.config特定模块
                var ec = ParseEntityChoices(content, ts);
                if (ec != null) results.Add(ec);
            }
            else if (line.Contains("SendChoices()"))
            {
                // 07071806根因: EntityChoices 格式无 ChoiceList=CHOSEN, 选择完成信号是 GameState.SendChoices() - id=N ChoiceType=XXX。
                // 之前行过滤未放行 SendChoices → chosen=0 → 面板只能靠状态机超时慢关(残留数秒, 与后续面板同位置重叠, 看似"发现里混饰品")。
                // 入口行(content 已剥前缀)="id=N ChoiceType=XXX" → 玩家已选 → raise ChoiceCompleted 立即清饰品+发现面板。
                var scm = System.Text.RegularExpressions.Regex.Match(content, @"^id=(\d+) ChoiceType=(\w+)");
                if (scm.Success && scm.Groups[2].Value != "MULLIGAN")
                {
                    _currentEntityChoice = null;
                    _pendingChoiceEntityIds.Clear();
                    _pendingSendChoiceId = int.Parse(scm.Groups[1].Value);
                    _pendingSendChoiceType = scm.Groups[2].Value;
                    _pendingSendChoiceAt = ts;
                }
                else if (_pendingSendChoiceId >= 0)
                {
                    var chosen = System.Text.RegularExpressions.Regex.Match(content,
                        @"^m_chosenEntities\[\d+\]=\[entityName=(.+) id=(\d+) zone=(\w+) zonePos=(\d+) cardId=(\w*) player=(\d+)\]");
                    if (chosen.Success)
                    {
                        var completion = new PowerLogChoiceCompletion
                        {
                            ChoiceId = _pendingSendChoiceId,
                            ChoiceType = _pendingSendChoiceType,
                            SelectedName = chosen.Groups[1].Value,
                            SelectedEntityId = int.Parse(chosen.Groups[2].Value),
                            SelectedCardId = chosen.Groups[5].Value,
                            SelectedPlayerId = int.Parse(chosen.Groups[6].Value),
                            Timestamp = _pendingSendChoiceAt,
                        };
                        _pendingSendChoiceId = -1;
                        _pendingSendChoiceType = "";
                        _pendingSendChoiceAt = DateTime.MinValue;
                        try { ChoiceCompleted?.Invoke(completion); } catch { }
                        results.Add(new PLEvent { Type = PLEventType.PlayerAction, Timestamp = ts,
                            EntityId = completion.SelectedEntityId, CardId = completion.SelectedCardId,
                            BlockType = "CHOSEN" });
                    }
                }
            }

            foreach (var e in results) e.RawLine = line;
            return results;
        }

        private PLEvent ParseTagChange(string content, DateTime ts)
        {
            var evt = new PLEvent { Type = PLEventType.TagChange, Timestamp = ts };
            evt.EntityId = ExtractInt(content, "Entity=[", "id=");
            if (evt.EntityId == 0)
            {
                var numericEntity = Regex.Match(content, @"^TAG_CHANGE Entity=(\d+)\s");
                if (numericEntity.Success) int.TryParse(numericEntity.Groups[1].Value, out evt.EntityId);
            }
            evt.EntityName = ExtractString(content, "entityName=", " ");
            evt.Tag = ExtractString(content, "tag=", " ");
            evt.Value = ExtractString(content, "value=", " ");
            int.TryParse(evt.Value, out evt.NumericValue);

            // 更新实体状态
            if (!_entities.ContainsKey(evt.EntityId))
                _entities[evt.EntityId] = new PLEntityState { Id = evt.EntityId };
            var entity = _entities[evt.EntityId];
            if (!string.IsNullOrEmpty(evt.EntityName)) entity.Name = evt.EntityName;

            // 分类标签
            switch (evt.Tag)
            {
                case "ZONE":
                    entity.Zone = evt.Value;
                    evt.Type = PLEventType.ZoneChange;
                    // 发现候选离开展示区 → 从缓冲区移除
                    if (_discoverBuffer.Count > 0
                        && _discoverBuffer.Any(c => c.EntityId == evt.EntityId))
                    {
                        if (evt.Value == "HAND" || evt.Value == "REMOVEDFROMGAME"
                            || evt.Value == "GRAVEYARD" || evt.Value == "DECK")
                        {
                            _discoverBuffer.RemoveAll(c => c.EntityId == evt.EntityId);
                            if (_discoverBuffer.Count == 0 && HasActiveChoiceContext())
                            {
                                EmitDiscoverOffered(new List<DiscoverCandidate>(), ts);
                            }
                        }
                    }
                    // 发现候选进入展示区 → 补充检测(FULL_ENTITY时zone未设置, 补在这里)
                    // SETASIDE(6) + PLAY(1, 对手侧可能是商店/发现实体) 都可能包含发现选项
                    var zoneVal = evt.Value;
                    bool isDiscoverZone = zoneVal == "SETASIDE" || zoneVal == "6"
                        || zoneVal == "PLAY" || zoneVal == "1";
                    if (HasActiveChoiceContext()
                        && isDiscoverZone
                        && !string.IsNullOrEmpty(entity.CardId)
                        && IsDiscoverCandidateCardId(entity.CardId))
                    {
                        // 时间窗口重置
                        if (_discoverBuffer.Count > 0
                            && (ts - _lastDiscoverCandidateTime) > DiscoverBatchWindow)
                            _discoverBuffer.Clear();
                        _lastDiscoverCandidateTime = ts;
                        // 去重
                        if (!_discoverBuffer.Any(c => c.EntityId == evt.EntityId))
                        {
                            _discoverBuffer.Add(new DiscoverCandidate
                            {
                                CardId = entity.CardId,
                                CardName = entity.Name ?? entity.CardId,
                                EntityId = evt.EntityId,
                            });
                            if (_discoverBuffer.Count == 2 || _discoverBuffer.Count == 3)
                            {
                                EmitDiscoverOffered(new List<DiscoverCandidate>(_discoverBuffer), ts);
                            }
                            else if (_discoverBuffer.Count > 3)
                                _discoverBuffer.Clear();
                        }
                    }
                    // 饰品被选中: 饰品实体进入玩家区域
                    if (evt.Value == "PLAY" || evt.Value == "HAND")
                        if (IsTrinketEntity(entity)) evt.Type = PLEventType.TrinketSelected;
                    break;
                case "ATK": entity.Attack = evt.NumericValue; break;
                case "HEALTH": entity.Health = evt.NumericValue;
                    if (evt.NumericValue <= 0 && entity.Health > 0)
                        evt.Type = PLEventType.MinionDeath;
                    break;
                case "DAMAGE": evt.Type = PLEventType.Damage; break;
                case "ARMOR": evt.Type = PLEventType.ArmorChange; break;
                case "STEP": _currentPhase = evt.Value; evt.Type = PLEventType.PhaseChange; break;
                case "NEXT_STEP":
                    _currentPhase = evt.Value;
                    evt.Type = PLEventType.PhaseChange;
                    try { PhaseDetected?.Invoke(evt.Value); } catch { }
                    break;
                case "TURN": _currentTurn = evt.NumericValue; evt.Turn = evt.NumericValue;
                    evt.Type = PLEventType.TurnStart;
                    _diagCount = 0; // T2(07071121): 每回合重置诊断预算, 防后期发现被30条上限致盲(参照07062157 SKIP教训)
                    break;
                case "RESOURCES": evt.Type = PLEventType.GoldChange; break;
                case "COST": entity.Cost = evt.NumericValue; break;
                case "BACON_OVERRIDE_BG_COST": entity.AlternateCost = evt.NumericValue; break;
                case "BACON_ALT_TAVERN_COIN":
                    var playerMatch = Regex.Match(content,
                        @"^TAG_CHANGE Entity=(.+?)\s+tag=BACON_ALT_TAVERN_COIN\s+value=");
                    if (playerMatch.Success)
                    {
                        _alternateCurrencyByPlayer[playerMatch.Groups[1].Value] = evt.NumericValue;
                        if (_currentEntityChoice != null
                            && IsTimewarpPurchaseSource(_currentEntityChoice.SourceCardId)
                            && string.Equals(_currentEntityChoice.PlayerName,
                                playerMatch.Groups[1].Value, StringComparison.Ordinal))
                            EmitTimewarpPurchaseOffered(_currentEntityChoice, evt.NumericValue);
                    }
                    break;
                case "ZONE_POSITION": entity.ZonePos = evt.NumericValue; break;
                case "PREMIUM": entity.IsGolden = evt.NumericValue > 0; break;
                case "CARDRACE": entity.Race = evt.Value; break;
                case "EXHAUSTED":
                    if (evt.NumericValue > 0 && IsHeroPowerEntity(entity))
                        evt.Type = PLEventType.HeroPowerUsed;
                    break;
                case "DIVINE_SHIELD": entity.HasDivineShield = evt.NumericValue > 0; break;
                case "REBORN": entity.HasReborn = evt.NumericValue > 0; break;
                case "TAUNT": entity.HasTaunt = evt.NumericValue > 0; break;
                case "POISONOUS": entity.IsPoisonous = evt.NumericValue > 0; break;
                case "WINDFURY": entity.HasWindfury = evt.NumericValue > 0; break;
            }
            // 关键状态变化 → 驱动UI实时刷新 (zone/金币/位置/英雄技能)
            if (evt.Type == PLEventType.ZoneChange || evt.Type == PLEventType.GoldChange
                || evt.Type == PLEventType.HeroPowerUsed)
            {
                try { RelevantTagChanged?.Invoke(); } catch { }
            }
            return evt;
        }

        private PLEvent ParseFullEntity(string content, DateTime ts)
        {
            var evt = new PLEvent { Type = PLEventType.FullEntity, Timestamp = ts };
            // Power.log FULL_ENTITY: "Creating ID=27 CardID=BG34_630" — ID/CardID都是大写
            evt.EntityId = ExtractInt(content, "ID=");
            if (evt.EntityId == 0) evt.EntityId = ExtractInt(content, "id=");
            evt.CardId = ExtractString(content, "CardID=", " ");
            if (string.IsNullOrEmpty(evt.CardId))
                evt.CardId = ExtractString(content, "cardId=", " ");
            evt.EntityName = ExtractString(content, "entityName=", " ");
            int zonePos = -1; int.TryParse(ExtractString(content, "zonePos=", " "), out zonePos);

            var entity = new PLEntityState
            {
                Id = evt.EntityId, Name = evt.EntityName, CardId = evt.CardId,
                Zone = ExtractString(content, "zone=", " "), ZonePos = zonePos,
            };
            _entities[evt.EntityId] = entity;

            // 检测饰品实体
            if (IsTrinketEntity(entity)) evt.Type = PLEventType.TrinketOffered;
            // 检测英雄技能实体
            if (IsHeroPowerEntity(entity)) evt.Type = PLEventType.HeroPowerOffered;

            return evt;
        }

        private PLEvent ParseShowEntity(string content, DateTime ts)
        {
            string cardId = ExtractString(content, "CardID=", " ");
            if (string.IsNullOrEmpty(cardId))
                cardId = ExtractString(content, "cardId=", " ");
            int entityId = ExtractInt(content, "ID=");
            if (entityId == 0) entityId = ExtractInt(content, "id=");

            var evt = new PLEvent
            {
                Type = PLEventType.CardRevealed, Timestamp = ts,
                EntityId = entityId, CardId = cardId,
            };

            // 饰品翻开
            if (!string.IsNullOrEmpty(cardId) && (cardId.Contains("MagicItem") || cardId.Contains("Trinket")))
                evt.Type = PLEventType.TrinketOffered;
            // 英雄技能翻开
            if (!string.IsNullOrEmpty(cardId) && IsHeroPowerCardId(cardId))
                evt.Type = PLEventType.HeroPowerOffered;

            // 发现选项检测: SHOW_ENTITY="向玩家展示实体"恰好是发现选项出现的语义
            // 比FULL_ENTITY更精确(FULL_ENTITY创建实体原因很多, SHOW_ENTITY只表示展示)
            if (HasActiveChoiceContext()
                && entityId > 0 && !string.IsNullOrEmpty(cardId)
                && IsDiscoverCandidateCardId(cardId)
                && !cardId.Contains("MagicItem") && !cardId.Contains("Trinket"))
            {
                if (_discoverBuffer.Count > 0
                    && (ts - _lastDiscoverCandidateTime) > DiscoverBatchWindow)
                    _discoverBuffer.Clear();
                _lastDiscoverCandidateTime = ts;
                if (!_discoverBuffer.Any(c => c.EntityId == entityId))
                {
                    string name = "";
                    if (_entities.TryGetValue(entityId, out var se))
                        name = se.Name ?? "";
                    _discoverBuffer.Add(new DiscoverCandidate
                    {
                        CardId = cardId,
                        CardName = string.IsNullOrEmpty(name) ? cardId : name,
                        EntityId = entityId,
                    });
                    if (_discoverBuffer.Count >= 2 && _discoverBuffer.Count <= 3)
                    {
                        var snapshot = new List<DiscoverCandidate>(_discoverBuffer);
                        DiagLog(string.Format("Discover via SHOW_ENTITY: {0} candidates [{1}]",
                            snapshot.Count, string.Join(", ", snapshot.Select(c => c.CardName ?? c.CardId ?? "?"))));
                        EmitDiscoverOffered(snapshot, ts);
                    }
                    else if (_discoverBuffer.Count > 3)
                        _discoverBuffer.Clear();
                }
            }

            return evt;
        }

        private PLEvent ParseChangeEntity(string content, DateTime ts)
        {
            int id = ExtractInt(content, "ID=");
            if (id == 0) id = ExtractInt(content, "id=");
            string newCardId = ExtractString(content, "CardID=", " ");
            if (string.IsNullOrEmpty(newCardId)) newCardId = ExtractString(content, "cardId=", " ");
            string oldCardId = "";
            if (_entities.ContainsKey(id)) { oldCardId = _entities[id].CardId; _entities[id].CardId = newCardId; }

            bool isTriple = newCardId.Contains("Golden") || newCardId.EndsWith("_G")
                || newCardId.Contains("_G_");
            return new PLEvent
            {
                Type = isTriple ? PLEventType.Triple : PLEventType.Transform,
                Timestamp = ts, EntityId = id,
                OldCardId = oldCardId, NewCardId = newCardId,
            };
        }

        private PLEvent ParseHideEntity(string content, DateTime ts)
        {
            int eid = ExtractInt(content, "ID=");
            if (eid == 0) eid = ExtractInt(content, "id=");
            return new PLEvent
            {
                Type = PLEventType.CardRevealed, Timestamp = ts,
                EntityId = eid,
            };
        }

        private PLEvent ParseBlockStart(string content, DateTime ts)
        {
            var evt = new PLEvent { Timestamp = ts };
            evt.BlockType = ExtractString(content, "BlockType=", " ");
            evt.EntityId = ExtractInt(content, "Entity=[", "id=");
            evt.TargetId = ExtractInt(content, "Target=[", "id=");
            evt.EntityName = ExtractString(content, "entityName=", " ");
            evt.CardId = ExtractString(content, "cardId=", " ");

            switch (evt.BlockType)
            {
                case "ATTACK": evt.Type = PLEventType.Attack; evt.SourceId = evt.EntityId; break;
                case "POWER": evt.Type = PLEventType.Battlecry; break;
                case "TRIGGER": evt.Type = PLEventType.Trigger; break;
                case "DEATHRATTLE": evt.Type = PLEventType.Deathrattle; break;
                case "RITUAL": evt.Type = PLEventType.Triple; break;
                default: evt.Type = PLEventType.PlayerAction; break;
            }
            return evt;
        }

        public PLEntityState GetEntity(int id) => _entities.ContainsKey(id) ? _entities[id] : null;
        public int CurrentTurn => _currentTurn;
        public string CurrentPhase => _currentPhase;

        // ── 实体类型检测 ──

        private static bool IsHeroPowerEntity(PLEntityState e)
        {
            if (e == null || string.IsNullOrEmpty(e.CardId)) return false;
            return IsHeroPowerCardId(e.CardId) || IsHeroPowerCardId(e.Name);
        }

        private static bool IsHeroPowerCardId(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            return id.Contains("_HP_") || id.Contains("_HERO_") && id.EndsWith("p")
                || id.Contains("HeroPower") || id.Contains("_pt")
                || id.Contains("BACON_HERO_POWER");
        }

        private static bool IsTrinketEntity(PLEntityState e)
        {
            if (e == null || string.IsNullOrEmpty(e.CardId)) return false;
            return IsTrinketCardId(e.CardId) || IsTrinketCardId(e.Name);
        }

        // ══ ChoiceList 解析: Power.log原生的发现/选择信号 ══
        // 参照HDT ChoicesHandler: ChoiceList=CHOICE→ENTITY→CHOSEN 序列精确标记选择选项
        // 比zone6实体扫描可靠得多(zone6包含大量过渡实体)
        // v2: 区分饰品Choice(MagicItem/Trinket)和发现Choice(随从/法术), 分别驱动对应面板

        /// <summary>
        /// 解析 GameState.DebugPrintEntityChoices() 事件
        /// 格式示例:
        ///   DebugPrintEntityChoices() - id=4 Player=XXX TaskList=1334 ChoiceType=GENERAL CountMin=1 CountMax=1
        ///   Source=[entityName=XXX id=2790 zone=HAND zonePos=2 cardId=BG26_525 player=4]
        ///   Entities[0]=[entityName=XXX id=2977 zone=SETASIDE zonePos=0 cardId=BG34_500 player=4]
        ///
        /// 入口行解析id/ChoiceType, 后续行解析Source/Entities[N]通过状态机累积
        /// 比ChoiceList=更可靠, 因为DebugPrintEntityChoices在Power_old.log中默认启用, 不依赖log.config特定模块
        /// </summary>
        private PLEvent ParseEntityChoices(string content, DateTime ts)
        {
            // 入口行(content 已被 ExtractContent 剥掉 "DebugPrintEntityChoices() - " 前缀): id=N Player=XXX TaskList=N ChoiceType=XXX CountMin=N CountMax=N
            // 07071603根因: 正则原含 "DebugPrintEntityChoices\(\) - " 前缀, 但 content 已无此前缀 → 恒不匹配 → _currentEntityChoice 永不设置 → 后续 Source/Entities 因守卫全跳过。
            // ^ 锚定: 入口行剥离后以 "id=" 开头; Source/"Entities[" 行不会误匹配本正则。
            var headerMatch = System.Text.RegularExpressions.Regex.Match(content,
                @"^id=(\d+) Player=(.+?) TaskList=(\d*) ChoiceType=(\w+) CountMin=(\d+) CountMax=(\d+)");
            if (headerMatch.Success)
            {
                int taskList;
                int.TryParse(headerMatch.Groups[3].Value, out taskList);
                _currentEntityChoice = new EntityChoiceState
                {
                    Id = int.Parse(headerMatch.Groups[1].Value),
                    TaskList = taskList,
                    PlayerName = headerMatch.Groups[2].Value,
                    ChoiceType = headerMatch.Groups[4].Value,
                    CountMin = int.Parse(headerMatch.Groups[5].Value),
                    CountMax = int.Parse(headerMatch.Groups[6].Value),
                    Timestamp = ts,
                };
                _lastChoiceContextAt = ts;
                return null;
            }

            // Source行
            var sourceMatch = System.Text.RegularExpressions.Regex.Match(content,
                @"Source=\[entityName=(.+) id=(\d+) zone=(\w+) zonePos=(\d+) cardId=(\w*) player=(\d+)\]");
            if (sourceMatch.Success && _currentEntityChoice != null)
            {
                _currentEntityChoice.SourceCardId = sourceMatch.Groups[5].Value;
                _currentEntityChoice.SourceName = sourceMatch.Groups[1].Value;
                int.TryParse(sourceMatch.Groups[6].Value, out _currentEntityChoice.SourcePlayerId);
                return null;
            }

            // Entities[N]行
            var entityMatch = System.Text.RegularExpressions.Regex.Match(content,
                @"Entities\[(\d+)\]=\[entityName=(.+) id=(\d+) zone=(\w+) zonePos=(\d+) cardId=(\w+)");
            if (entityMatch.Success && _currentEntityChoice != null)
            {
                _lastChoiceContextAt = ts;
                int idx = int.Parse(entityMatch.Groups[1].Value);
                var candidate = new DiscoverCandidate
                {
                    CardId = entityMatch.Groups[6].Value,
                    CardName = entityMatch.Groups[2].Value,
                    EntityId = int.Parse(entityMatch.Groups[3].Value),
                };
                PLEntityState candidateEntity;
                if (_entities.TryGetValue(candidate.EntityId, out candidateEntity))
                    candidate.PurchaseCost = candidateEntity.AlternateCost > 0
                        ? candidateEntity.AlternateCost : candidateEntity.Cost;
                while (_currentEntityChoice.Candidates.Count <= idx)
                    _currentEntityChoice.Candidates.Add(null);
                _currentEntityChoice.Candidates[idx] = candidate;

                var validCandidates = _currentEntityChoice.Candidates.Where(c => c != null).ToList();
                // CountMax 是“需要选择几个”, 不是候选总数。3选1 常见 CountMax=1,
                // 不能在第一个 Entities[0] 就 Fired, 否则后续两个候选会被拦掉。
                if (validCandidates.Count >= 2)
                {
                    _currentEntityChoice.Fired = true;

                    if (_currentEntityChoice.ChoiceType == "MULLIGAN") return null;

                    bool isTrinket = validCandidates.Any(c =>
                        c.CardId != null && (c.CardId.Contains("MagicItem") || c.CardId.Contains("Trinket")));

                    if (isTrinket)
                    {
                        DiagLog(string.Format("TrinketChoice via EntityChoices: {0} candidates [{1}]",
                            validCandidates.Count,
                            string.Join(", ", validCandidates.Select(c => c.CardName ?? c.CardId ?? "?"))));
                        try
                        {
                            TrinketChoiceActive?.Invoke(new PowerLogChoiceBatch
                            {
                                ChoiceId = _currentEntityChoice.Id,
                                TaskList = _currentEntityChoice.TaskList,
                                PlayerName = _currentEntityChoice.PlayerName,
                                ChoiceType = _currentEntityChoice.ChoiceType,
                                SourceCardId = _currentEntityChoice.SourceCardId,
                                SourceName = _currentEntityChoice.SourceName,
                                SourcePlayerId = _currentEntityChoice.SourcePlayerId,
                                Timestamp = _currentEntityChoice.Timestamp,
                                Candidates = new List<DiscoverCandidate>(validCandidates),
                            });
                        }
                        catch { }
                    }
                    else if (IsTimewarpPurchaseSource(_currentEntityChoice.SourceCardId))
                        EmitTimewarpPurchaseOffered(_currentEntityChoice);
                    else
                    {
                        DiagLog(string.Format("Discover via EntityChoices: {0} candidates [{1}]",
                            validCandidates.Count,
                            string.Join(", ", validCandidates.Select(c => c.CardName ?? c.CardId ?? "?"))));
                        EmitDiscoverOffered(validCandidates, ts, _currentEntityChoice);
                    }
                }
                return null;
            }

            return null;
        }

        private EntityChoiceState _currentEntityChoice;

        private void EmitDiscoverOffered(List<DiscoverCandidate> candidates, DateTime ts,
            EntityChoiceState entityChoice = null)
        {
            try
            {
                DiscoverOffered?.Invoke(new PowerLogChoiceBatch
                {
                    ChoiceId = entityChoice != null ? entityChoice.Id : _pendingChoiceId,
                    TaskList = entityChoice != null ? entityChoice.TaskList : 0,
                    PlayerName = entityChoice != null ? entityChoice.PlayerName : "",
                    ChoiceType = entityChoice != null ? entityChoice.ChoiceType : "GENERAL",
                    SourceCardId = entityChoice != null ? entityChoice.SourceCardId : "",
                    SourceName = entityChoice != null ? entityChoice.SourceName : "",
                    SourcePlayerId = entityChoice != null ? entityChoice.SourcePlayerId : 0,
                    Timestamp = entityChoice != null ? entityChoice.Timestamp : ts,
                    Candidates = candidates != null
                        ? new List<DiscoverCandidate>(candidates)
                        : new List<DiscoverCandidate>(),
                });
            }
            catch { }
        }

        private void EmitTimewarpPurchaseOffered(EntityChoiceState entityChoice,
            int? explicitTimeCoins = null)
        {
            if (entityChoice == null) return;
            var candidates = entityChoice.Candidates
                .Where(candidate => candidate != null).ToList();
            if (candidates.Count < 2) return;
            int timeCoins = explicitTimeCoins ?? 0;
            if (!explicitTimeCoins.HasValue)
                _alternateCurrencyByPlayer.TryGetValue(
                    entityChoice.PlayerName ?? "", out timeCoins);
            try
            {
                TimewarpPurchaseOffered?.Invoke(new PowerLogChoiceBatch
                {
                    ChoiceId = entityChoice.Id,
                    TaskList = entityChoice.TaskList,
                    PlayerName = entityChoice.PlayerName,
                    ChoiceType = entityChoice.ChoiceType,
                    SourceCardId = entityChoice.SourceCardId,
                    SourceName = entityChoice.SourceName,
                    SourcePlayerId = entityChoice.SourcePlayerId,
                    TimeCoinCount = timeCoins,
                    Timestamp = entityChoice.Timestamp,
                    Candidates = new List<DiscoverCandidate>(candidates),
                });
            }
            catch { }
        }

        private class EntityChoiceState
        {
            public int Id;
            public int TaskList;
            public string PlayerName;
            public string ChoiceType;
            public int CountMin;
            public int CountMax;
            public string SourceCardId;
            public string SourceName;
            public int SourcePlayerId;
            public DateTime Timestamp;
            public List<DiscoverCandidate> Candidates = new List<DiscoverCandidate>();
            public bool Fired;
        }
        // v2: 区分饰品Choice(MagicItem/Trinket)和发现Choice(随从/法术), 分别驱动对应面板
        private bool _isTrinketChoice = false;

        private PLEvent ParseChoiceLine(string content, DateTime ts)
        {
            // ChoiceList=CHOICE id=X choiceType=GENERAL → 新选择开始
            if (content.Contains("ChoiceList=CHOICE"))
            {
                _pendingChoiceEntityIds.Clear();
                _pendingChoiceId = ExtractInt(content, "id=");
                _inChoiceBlock = true;
                _lastChoiceContextAt = ts;
                _isTrinketChoice = false;
                // 清空旧的发现缓冲, 准备接收ChoiceList=ENTITY
                _discoverBuffer.Clear();
                _lastDiscoverCandidateTime = ts;
                return new PLEvent { Type = PLEventType.PlayerAction, Timestamp = ts,
                    EntityId = _pendingChoiceId, BlockType = "CHOICE" };
            }
            // ChoiceList=ENTITY id=X → 一个选择选项实体
            if (content.Contains("ChoiceList=ENTITY") && _inChoiceBlock)
            {
                _lastChoiceContextAt = ts;
                int entityId = ExtractInt(content, "id=");
                if (entityId > 0 && !_pendingChoiceEntityIds.Contains(entityId))
                {
                    _pendingChoiceEntityIds.Add(entityId);
                    if (_entities.TryGetValue(entityId, out var entity)
                        && !string.IsNullOrEmpty(entity.CardId))
                    {
                        // 区分饰品Choice vs 发现Choice: 按CardId判断
                        if (IsTrinketCardId(entity.CardId))
                        {
                            _isTrinketChoice = true;
                            var trinketCandidates = new List<DiscoverCandidate>();
                            // 收集所有已出现的饰品选项实体
                            foreach (var eid in _pendingChoiceEntityIds)
                            {
                                if (_entities.TryGetValue(eid, out var te)
                                    && !string.IsNullOrEmpty(te.CardId)
                                    && IsTrinketCardId(te.CardId))
                                {
                                    trinketCandidates.Add(new DiscoverCandidate
                                    {
                                        CardId = te.CardId,
                                        CardName = te.Name ?? te.CardId,
                                        EntityId = eid,
                                    });
                                }
                            }
                            if (trinketCandidates.Count >= 1)
                            {
                                DiagLog(string.Format("TrinketChoice via ChoiceList: {0} candidates [{1}]",
                                    trinketCandidates.Count,
                                    string.Join(", ", trinketCandidates.Select(c => c.CardName ?? c.CardId ?? "?"))));
                                try
                                {
                                    TrinketChoiceActive?.Invoke(new PowerLogChoiceBatch
                                    {
                                        ChoiceId = _pendingChoiceId,
                                        ChoiceType = "GENERAL",
                                        Timestamp = ts,
                                        Candidates = new List<DiscoverCandidate>(trinketCandidates),
                                    });
                                }
                                catch { }
                            }
                        }
                        else if (IsDiscoverCandidateCardId(entity.CardId))
                        {
                            if (!_discoverBuffer.Any(c => c.EntityId == entityId))
                            {
                                _discoverBuffer.Add(new DiscoverCandidate
                                {
                                    CardId = entity.CardId,
                                    CardName = entity.Name ?? entity.CardId,
                                    EntityId = entityId,
                                });
                                _lastDiscoverCandidateTime = ts;
                            }
                            if (_discoverBuffer.Count >= 2 && _discoverBuffer.Count <= 3)
                            {
                                var snapshot = new List<DiscoverCandidate>(_discoverBuffer);
                                DiagLog(string.Format("Discover via ChoiceList: {0} candidates [{1}]",
                                    snapshot.Count, string.Join(", ", snapshot.Select(c => c.CardName ?? c.CardId ?? "?"))));
                                EmitDiscoverOffered(snapshot, ts);
                            }
                        }
                    }
                }
                return new PLEvent { Type = PLEventType.CardRevealed, Timestamp = ts, EntityId = entityId };
            }
            // ChoiceList=CHOSEN id=X → 玩家做出了选择
            if (content.Contains("ChoiceList=CHOSEN") && _inChoiceBlock)
            {
                _inChoiceBlock = false;
                // 通知对应面板关闭
                if (_isTrinketChoice)
                {
                    try
                    {
                        TrinketChoiceActive?.Invoke(new PowerLogChoiceBatch
                        {
                            ChoiceId = _pendingChoiceId,
                            ChoiceType = "GENERAL",
                            Timestamp = ts,
                            Candidates = new List<DiscoverCandidate>(),
                        });
                    }
                    catch { }
                }
                else
                {
                    EmitDiscoverOffered(new List<DiscoverCandidate>(), ts);
                }
                try
                {
                    ChoiceCompleted?.Invoke(new PowerLogChoiceCompletion
                    {
                        ChoiceId = _pendingChoiceId,
                        ChoiceType = "GENERAL",
                        Timestamp = ts,
                    });
                }
                catch { }
                _pendingChoiceEntityIds.Clear();
                _discoverBuffer.Clear();
                _pendingChoiceId = -1;
                _isTrinketChoice = false;
                return new PLEvent { Type = PLEventType.PlayerAction, Timestamp = ts, BlockType = "CHOSEN" };
            }
            return null;
        }

        private static bool IsTrinketCardId(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            return id.Contains("MagicItem") || id.Contains("Trinket")
                || id.Contains("_Trinket_");
        }

        // ── 辅助解析 ──
        private static readonly Regex TimestampRegex = new Regex(@"(\d{2}:\d{2}:\d{2}\.\d+)", RegexOptions.Compiled);

        public static DateTime ExtractTimestamp(string line)
        {
            var m = TimestampRegex.Match(line);
            if (m.Success && DateTime.TryParse(m.Value, out var dt)) return dt;
            return DateTime.UtcNow;
        }

        private static string ExtractContent(string line)
        {
            int idx = line.LastIndexOf("- ");
            return idx >= 0 ? line.Substring(idx + 1).TrimStart() : line;
        }

        private static int ExtractInt(string text, string key)
        {
            var match = Regex.Match(text, Regex.Escape(key) + @"(\d+)");
            return match.Success ? int.Parse(match.Groups[1].Value) : 0;
        }
        private static int ExtractInt(string text, string prefix, string key)
        {
            int start = text.IndexOf(prefix);
            if (start < 0) return 0;
            return ExtractInt(text.Substring(start), key);
        }
        private static string ExtractString(string text, string key, string delimiter)
        {
            int start = text.IndexOf(key);
            if (start < 0) return "";
            start += key.Length;
            int end = text.IndexOf(delimiter, start);
            if (end < 0) end = text.Length;
            return text.Substring(start, end - start).Trim();
        }

        // ── 发现检测 ──

        private void TryDetectDiscover(PLEvent fe, DateTime ts)
        {
            if (fe.Type != PLEventType.FullEntity) return;
            if (!HasActiveChoiceContext()) return;
            if (string.IsNullOrEmpty(fe.CardId)) return;
            // 仅关注BG随从/法术, 排除英雄/技能/饰品
            if (!IsDiscoverCandidateCardId(fe.CardId)) return;

            // 获取实体信息 — zone可能在FULL_ENTITY时尚未设置(TAG_CHANGE稍后设置)
            if (!_entities.TryGetValue(fe.EntityId, out var entity)) return;
            string zone = entity.Zone ?? "";

            // zone过滤(替代BLOCK门控): 仅接受已进入SETASIDE(6)或zone尚未设置的实体
            // zone=""→允许(TAG_CHANGE会补设); zone="6"/"SETASIDE"→明确是发现区
            if (!string.IsNullOrEmpty(zone)
                && zone != "6" && zone != "SETASIDE"
                && zone != "PLAY" && zone != "1")  // PLAY可能是过渡态
                return;

            // 时间窗口重置: 超过批次窗口 → 清空旧缓冲
            if (_discoverBuffer.Count > 0
                && (ts - _lastDiscoverCandidateTime) > DiscoverBatchWindow)
                _discoverBuffer.Clear();

            _lastDiscoverCandidateTime = ts;

            // 去重: 同EntityId不重复加入
            if (_discoverBuffer.Any(c => c.EntityId == fe.EntityId)) return;

            string name = fe.EntityName ?? entity.Name ?? "";
            _discoverBuffer.Add(new DiscoverCandidate
            {
                CardId = fe.CardId,
                CardName = string.IsNullOrEmpty(name) ? fe.CardId : name,
                EntityId = fe.EntityId,
            });

            // 2~3个候选 → 触发发现事件
            if (_discoverBuffer.Count >= 2 && _discoverBuffer.Count <= 3)
            {
                var snapshot = new List<DiscoverCandidate>(_discoverBuffer);
                DiagLog(string.Format("Discover buffer filled: {0} candidates [{1}]",
                    snapshot.Count, string.Join(", ", snapshot.Select(c => c.CardName ?? c.CardId ?? "?"))));
                EmitDiscoverOffered(snapshot, ts);
            }
            else if (_discoverBuffer.Count > 3)
            {
                DiagLog(string.Format("Discover buffer overflow: {0} candidates, clearing", _discoverBuffer.Count));
                _discoverBuffer.Clear();
            }
        }

        private void CheckDiscoverCleanup(int entityId)
        {
            if (_discoverBuffer.Count == 0) return;
            // 移除被隐藏的实体
            _discoverBuffer.RemoveAll(c => c.EntityId == entityId);
            // 如果全部清除 → 通知发现结束
            if (_discoverBuffer.Count == 0 && HasActiveChoiceContext())
            {
                EmitDiscoverOffered(new List<DiscoverCandidate>(), DateTime.UtcNow);
            }
        }

        private static int _diagCount = 0;
        private static void DiagLog(string msg)
        {
            if (_diagCount++ >= 30) return;
            try
            {
                var dir = BobCoachDataPaths.Root;
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "bob_coach.log"),
                    string.Format("[{0:O}] [PowerLog] {1}\n", DateTime.UtcNow, msg),
                    System.Text.Encoding.UTF8);
            }
            catch { }
        }

        private static bool IsDiscoverCandidateCardId(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return false;
            // BG卡牌前缀
            if (!cardId.StartsWith("BG") && !cardId.StartsWith("TB_Bacon")
                && !cardId.StartsWith("EBG_")) return false;
            // 排除非随从/法术实体 (Trinket/MagicItem保留: 饰品发现需要这些)
            if (cardId.Contains("_HERO_") || cardId.Contains("_SKIN")
                || cardId.Contains("_HP") || cardId.Contains("HeroPower")
                || cardId.Contains("token") || cardId.Contains("Token")
                || cardId.EndsWith("_PH") || cardId.Contains("_PH_"))
                return false;
            return true;
        }

        public static bool IsTimewarpPurchaseSource(string sourceCardId)
        {
            return sourceCardId == "BG34_HERO_004p"
                || sourceCardId == "BG34_HERO_000p";
        }

        /// <summary>
        /// 检查ChoiceList解析是否处于活跃状态（近30秒内收到过CHOICE或ENTITY）
        /// </summary>
        private bool HasActiveChoiceContext()
        {
            if (_inChoiceBlock) return true;
            if (_currentEntityChoice != null) return true;
            if (_lastChoiceContextAt == DateTime.MinValue) return false;
            return (DateTime.UtcNow - _lastChoiceContextAt).TotalSeconds < 3
                || (DateTime.Now - _lastChoiceContextAt).TotalSeconds < 3;
        }

        /// <summary>ChoiceList/EntityChoices 解析是否活跃(近3秒内有 CHOICE/ENTITY 或正处于选择块)。
        /// T3: 发现门控用它判断"路径A(Power.log)是否在处理本次选择", 活跃则 zone6 启发式静默。</summary>
        public bool IsChoiceListActive()
        {
            return HasActiveChoiceContext();
        }
    }

    /// <summary>发现候选卡牌</summary>
    public class DiscoverCandidate
    {
        public string CardId;
        public string CardName;
        public int EntityId;
        public int PurchaseCost;
    }

    /// <summary>Power.log 实体状态追踪</summary>
    public class PLEntityState
    {
        public int Id; public string Name = ""; public string CardId = "";
        public string Zone = ""; public int ZonePos; public int Attack; public int Health;
        public int Cost; public bool IsGolden; public bool HasDivineShield;
        public int AlternateCost;
        public bool HasReborn; public bool HasTaunt; public bool IsPoisonous;
        public bool HasWindfury; public string Race = "";
    }
}
