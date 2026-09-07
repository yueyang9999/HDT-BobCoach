using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
#if !BOBCOACH_REPLAY_CORE_ONLY
using Newtonsoft.Json.Linq;
#endif

namespace BobCoach.Engine
{
    /// <summary>
    /// 全局事件级回放记录器。
    /// 以事件增量和 JSONL 流式落盘，不把所有回合快照保存在内存中。
    /// </summary>
    public sealed class GlobalReplayRecorder : IDisposable
    {
        private const int FlushBytes = 256 * 1024;
        private const int FlushSeconds = 2;

        private readonly string _directory;
        private StreamWriter _writer;
        private StringBuilder _buffer = new StringBuilder();
        private DateTime _lastFlushUtc = DateTime.UtcNow;
        private string _gameId;
        private string _path;
        private long _sequence;
        private bool _started;
        private bool _disposed;
        private string _lastStateSignature;
        private string _lastPhase;
        private string _lastActionKey;
        private string _lastRecommendationSignature;
        private string _lastPowerActionSignature;
        private string _lastCombatSignature;
        private readonly object _sync = new object();

        public GlobalReplayRecorder()
            : this(System.IO.Path.Combine(BobCoachDataPaths.Root, "replays"))
        {
        }

        public GlobalReplayRecorder(string directory)
        {
            _directory = string.IsNullOrWhiteSpace(directory)
                ? System.IO.Path.Combine(BobCoachDataPaths.Root, "replays")
                : directory;
        }

        public string FilePath { get { return _path; } }

        public void Start(
            string heroId,
            string heroName,
            string playerId,
            string build,
            string seasonId)
        {
            if (_started) return;
            Directory.CreateDirectory(_directory);
            _gameId = Guid.NewGuid().ToString("N");
            string safeHero = SanitizeFilePart(heroId ?? "unknown");
            _path = System.IO.Path.Combine(
                _directory,
                string.Format("global_{0:yyyyMMdd_HHmmss}_{1}_{2}.jsonl",
                    DateTime.Now, safeHero, _gameId.Substring(0, 8)));
            var stream = new FileStream(
                _path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite, 65536);
            _writer = new StreamWriter(stream, new UTF8Encoding(false), 65536)
            {
                AutoFlush = false,
            };
            _started = true;
            Append("meta",
                "\"hero\":\"" + Escape(heroId ?? "") + "\""
                + ",\"heroName\":\"" + Escape(heroName ?? "") + "\""
                + ",\"playerId\":\"" + Escape(playerId ?? "") + "\""
                + ",\"build\":\"" + Escape(build ?? "") + "\""
                + ",\"seasonId\":\"" + Escape(seasonId ?? "") + "\"");
            Flush();
        }

        public void RecordState(GlobalReplayState state, string evidenceSource = "hdt")
        {
            if (!_started || state == null) return;
            string signature = BuildStateSignature(state);
            if (signature == _lastStateSignature) return;
            _lastStateSignature = signature;
            Append("state",
                "\"gameActive\":" + Bool(state.GameActive)
                + ",\"isDuos\":" + Bool(state.IsDuos)
                + ",\"hero\":\"" + Escape(state.HeroCardId ?? "") + "\""
                + ",\"heroName\":\"" + Escape(state.HeroName ?? "") + "\""
                + ",\"player\":" + state.PlayerId
                + ",\"turn\":" + state.Turn
                + ",\"gold\":" + state.Gold
                + ",\"liveGold\":" + state.LiveGold
                + ",\"maxGold\":" + state.MaxGold
                + ",\"tier\":" + state.Tier
                + ",\"health\":" + state.Health
                + ",\"maxHealth\":" + state.MaxHealth
                + ",\"armor\":" + state.Armor
                + ",\"freeRefreshCount\":" + state.FreeRefreshCount
                + ",\"tavernUpgradeCost\":" + state.TavernUpgradeCost
                + ",\"frozenShop\":" + Bool(state.FrozenShop)
                + ",\"lastUpgradeTurn\":" + state.LastUpgradeTurn
                + ",\"firstPurchaseUsed\":" + Bool(state.FirstPurchaseUsedThisTurn)
                + ",\"firstMinionPurchaseUsed\":" + Bool(state.FirstMinionPurchaseUsedThisTurn)
                + ",\"normalRecruitAvailable\":" + Bool(state.NormalRecruitAvailable)
                + ",\"passiveChoiceActive\":" + Bool(state.PassiveChoiceActive)
                + ",\"replenishingShop\":" + Bool(state.ReplenishingShopActive)
                + ",\"heroPowerExhausted\":" + Bool(state.HeroPowerExhausted)
                + ",\"heroPowerCost\":" + state.HeroPowerCost
                + ",\"heroPowerDigs\":" + state.HeroPowerDigsRemaining
                + ",\"heroPowerType\":\"" + Escape(state.HeroPowerType ?? "") + "\""
                + ",\"heroPowerUnlockTurn\":" + state.HeroPowerUnlockTurn
                + ",\"heroPowerUnlockTier\":" + state.HeroPowerUnlockTier
                + ",\"heroPowerSpecial\":\"" + Escape(state.HeroPowerSpecial ?? "") + "\""
                + ",\"heroPowerHasDiscover\":" + Bool(state.HeroPowerHasDiscover)
                + ",\"hasSecondHeroPower\":" + Bool(state.HasSecondHeroPower)
                + ",\"exhaustedHeroPowerCount\":" + state.ExhaustedHeroPowerCount
                + ",\"phase\":\"" + Escape(state.Phase ?? "") + "\""
                + ",\"heroPower\":\"" + Escape(state.HeroPowerCardId ?? "") + "\""
                + ",\"anomaly\":\"" + Escape(state.AnomalyId ?? "") + "\""
                + ",\"anomalyEffects\":" + SerializeIds(state.AnomalyActiveEffectIds)
                + ",\"anomalyAmbiguous\":" + Bool(state.HasAmbiguousPrimary)
                + ",\"tribes\":" + SerializeIds(state.AvailableTribes)
                + ",\"activeTrinkets\":" + SerializeIds(state.ActiveTrinkets)
                + ",\"recentlyBought\":" + SerializeInts(state.RecentlyBoughtEntityIds)
                + ",\"activatedThisTurn\":" + SerializeInts(state.ActivatedThisTurn)
                + ",\"heroPowers\":" + SerializeHeroPowers(state.HeroPowers)
                + ",\"lastTavernSpell\":\"" + Escape(state.LastUsedTavernSpellCardId ?? "") + "\""
                + ",\"lastTavernSpellName\":\"" + Escape(state.LastUsedTavernSpellName ?? "") + "\""
                + ",\"source\":\"" + Escape(evidenceSource ?? "") + "\""
                + ",\"shop\":" + SerializeCards(state.Shop)
                + ",\"board\":" + SerializeCards(state.Board)
                + ",\"hand\":" + SerializeCards(state.Hand)
                + ",\"discover\":" + SerializeIds(state.Discover)
                + ",\"trinket\":" + SerializeIds(state.Trinket)
                + ",\"trinketDetails\":" + SerializeTrinkets(state.TrinketDetails)
                + ",\"opponents\":" + SerializeOpponents(state.Opponents));
            FlushIfNeeded();
        }

        public void RecordAction(
            string kind,
            string cardId,
            int target,
            int cost,
            int turn,
            string source)
        {
            if (!_started || string.IsNullOrEmpty(kind)) return;
            string key = kind + "|" + (cardId ?? "") + "|" + target + "|" + turn + "|" + (source ?? "");
            if (key == _lastActionKey) return;
            _lastActionKey = key;
            Append("action",
                "\"kind\":\"" + Escape(kind) + "\""
                + ",\"cardId\":\"" + Escape(cardId ?? "") + "\""
                + ",\"target\":" + target
                + ",\"cost\":" + cost
                + ",\"turn\":" + turn
                + ",\"source\":\"" + Escape(source ?? "") + "\"");
            FlushIfNeeded();
        }

        public void RecordPowerAction(
            string kind,
            string cardId,
            int targetId,
            string targetCardId,
            int targetPlayerId,
            int turn,
            string phase)
        {
            lock (_sync)
            {
                if (!_started || string.IsNullOrEmpty(kind)) return;
                string signature = kind + "|" + (cardId ?? "") + "|" + targetId + "|"
                    + (targetCardId ?? "") + "|" + turn + "|" + (phase ?? "");
                if (signature == _lastPowerActionSignature) return;
                _lastPowerActionSignature = signature;
                Append("powerAction",
                    "\"kind\":\"" + Escape(kind) + "\""
                    + ",\"cardId\":\"" + Escape(cardId ?? "") + "\""
                    + ",\"target\":" + targetId
                    + ",\"targetCardId\":\"" + Escape(targetCardId ?? "") + "\""
                    + ",\"targetPlayer\":" + targetPlayerId
                    + ",\"turn\":" + turn
                    + ",\"phase\":\"" + Escape(phase ?? "") + "\"");
                FlushIfNeeded();
            }
        }

        public void RecordCombatEvent(
            string kind,
            int attackerId,
            string attackerCardId,
            int attackerPlayerId,
            int defenderId,
            string defenderCardId,
            int defenderPlayerId,
            int damage,
            int health,
            int turn,
            string phase)
        {
            lock (_sync)
            {
                if (!_started || string.IsNullOrEmpty(kind)) return;
                string signature = kind + "|" + attackerId + "|" + (attackerCardId ?? "")
                    + "|" + attackerPlayerId + "|" + defenderId + "|"
                    + (defenderCardId ?? "") + "|" + defenderPlayerId + "|"
                    + damage + "|" + health + "|" + turn + "|" + (phase ?? "");
                if (signature == _lastCombatSignature) return;
                _lastCombatSignature = signature;
                Append("combat",
                    "\"kind\":\"" + Escape(kind) + "\""
                    + ",\"attacker\":" + attackerId
                    + ",\"atkCard\":\"" + Escape(attackerCardId ?? "") + "\""
                    + ",\"atkPlayer\":" + attackerPlayerId
                    + ",\"defender\":" + defenderId
                    + ",\"defCard\":\"" + Escape(defenderCardId ?? "") + "\""
                    + ",\"defPlayer\":" + defenderPlayerId
                    + ",\"dmg\":" + damage
                    + ",\"hp\":" + health
                    + ",\"turn\":" + turn
                    + ",\"phase\":\"" + Escape(phase ?? "") + "\"");
                FlushIfNeeded();
            }
        }

        public void RecordPhase(string phase)
        {
            if (!_started || string.IsNullOrEmpty(phase) || phase == _lastPhase) return;
            _lastPhase = phase;
            Append("phase", "\"phase\":\"" + Escape(phase) + "\"");
            FlushIfNeeded();
        }

        public void RecordChoice(
            string kind,
            int choiceId,
            int taskList,
            string sourceCardId,
            IEnumerable<string> candidates,
            int turn)
        {
            if (!_started || string.IsNullOrEmpty(kind)) return;
            Append("choice",
                "\"kind\":\"" + Escape(kind) + "\""
                + ",\"choiceId\":" + choiceId
                + ",\"taskList\":" + taskList
                + ",\"sourceCardId\":\"" + Escape(sourceCardId ?? "") + "\""
                + ",\"turn\":" + turn
                + ",\"candidates\":" + SerializeIds(candidates));
            FlushIfNeeded();
        }

        public void RecordChoiceCompleted(
            string kind,
            int choiceId,
            string selectedCardId,
            int turn)
        {
            if (!_started || string.IsNullOrEmpty(kind)) return;
            Append("choiceCompleted",
                "\"kind\":\"" + Escape(kind) + "\""
                + ",\"choiceId\":" + choiceId
                + ",\"selectedCardId\":\"" + Escape(selectedCardId ?? "") + "\""
                + ",\"turn\":" + turn);
            FlushIfNeeded();
        }

        public void RecordRecommendation(
            string bestActionJson,
            string actionScoresJson,
            string shadowScoresJson,
            string featureVector,
            int turn)
        {
            RecordRecommendation(
                bestActionJson,
                actionScoresJson,
                shadowScoresJson,
                featureVector,
                null,
                turn);
        }

        public void RecordRecommendation(
            string bestActionJson,
            string actionScoresJson,
            string shadowScoresJson,
            string featureVector,
            string mainlinePlanJson,
            int turn,
            string shadowC1Json = null,
            string shadowC7Json = null)
        {
            if (!_started) return;
            string signature = (bestActionJson ?? "") + "|" + (actionScoresJson ?? "")
                + "|" + (mainlinePlanJson ?? "") + "|" + turn;
            if (signature == _lastRecommendationSignature) return;
            _lastRecommendationSignature = signature;
            Append("recommend",
                "\"turn\":" + turn
                + ",\"bestAction\":" + (string.IsNullOrEmpty(bestActionJson) ? "null" : bestActionJson)
                + ",\"actionScores\":" + (string.IsNullOrEmpty(actionScoresJson) ? "[]" : actionScoresJson)
                + ",\"shadowActionScores\":" + (string.IsNullOrEmpty(shadowScoresJson) ? "[]" : shadowScoresJson)
                + ",\"featureVector\":\"" + Escape(featureVector ?? "") + "\""
                + ",\"mainlinePlan\":" + (string.IsNullOrEmpty(mainlinePlanJson) ? "null" : mainlinePlanJson)
                + ",\"shadowC1\":" + (string.IsNullOrEmpty(shadowC1Json) ? "null" : shadowC1Json)
                + ",\"shadowC7\":" + (string.IsNullOrEmpty(shadowC7Json) ? "null" : shadowC7Json));
            FlushIfNeeded();
        }

        public void RecordGameEnd(
            int finalRank,
            int turnCount,
            int tripleCount,
            double boardPowerPeak)
        {
            if (!_started) return;
            Append("end",
                "\"finalRank\":" + finalRank
                + ",\"turnCount\":" + turnCount
                + ",\"tripleCount\":" + tripleCount
                + ",\"boardPowerPeak\":" + boardPowerPeak.ToString("F6", System.Globalization.CultureInfo.InvariantCulture));
            Flush();
        }

        public void Flush()
        {
            lock (_sync)
            {
                if (_writer == null || _buffer.Length == 0) return;
                _writer.Write(_buffer.ToString());
                _buffer.Clear();
                _lastFlushUtc = DateTime.UtcNow;
                _writer.Flush();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                if (_started) Flush();
            }
            catch
            {
            }
            try
            {
                if (_writer != null)
                {
                    _writer.Dispose();
                    _writer = null;
                }
            }
            catch
            {
            }
            _started = false;
        }

        private void Append(string type, string body)
        {
            lock (_sync)
            {
            if (!_started || _writer == null) return;
            _buffer.Append("{\"v\":1")
                .Append(",\"g\":\"").Append(_gameId).Append('"')
                .Append(",\"seq\":").Append(++_sequence)
                .Append(",\"t\":\"").Append(DateTime.UtcNow.ToString("O")).Append('"')
                .Append(",\"e\":\"").Append(type).Append('"');
            if (!string.IsNullOrEmpty(body))
                _buffer.Append(',').Append(body);
            _buffer.Append("}\n");
            }
        }

        private void FlushIfNeeded()
        {
            lock (_sync)
            {
                if (_buffer.Length >= FlushBytes
                    || (DateTime.UtcNow - _lastFlushUtc).TotalSeconds >= FlushSeconds)
                    Flush();
            }
        }

        private static string BuildStateSignature(GlobalReplayState state)
        {
            var sb = new StringBuilder();
            sb.Append(state.GameActive).Append('|').Append(state.IsDuos).Append('|')
                .Append(state.HeroCardId ?? "").Append('|')
                .Append(state.HeroName ?? "").Append('|').Append(state.PlayerId).Append('|')
                .Append(state.Turn).Append('|').Append(state.Gold).Append('|')
                .Append(state.LiveGold).Append('|').Append(state.MaxGold).Append('|')
                .Append(state.Tier).Append('|').Append(state.Health).Append('|')
                .Append(state.MaxHealth).Append('|').Append(state.Armor).Append('|')
                .Append(state.FreeRefreshCount).Append('|')
                .Append(state.TavernUpgradeCost).Append('|').Append(state.FrozenShop).Append('|')
                .Append(state.LastUpgradeTurn).Append('|')
                .Append(state.FirstPurchaseUsedThisTurn).Append('|')
                .Append(state.FirstMinionPurchaseUsedThisTurn).Append('|')
                .Append(state.NormalRecruitAvailable).Append('|')
                .Append(state.PassiveChoiceActive).Append('|')
                .Append(state.ReplenishingShopActive).Append('|')
                .Append(state.HeroPowerExhausted).Append('|')
                .Append(state.HeroPowerCost).Append('|').Append(state.HeroPowerDigsRemaining).Append('|')
                .Append(state.HeroPowerType ?? "").Append('|')
                .Append(state.HeroPowerUnlockTurn).Append('|').Append(state.HeroPowerUnlockTier).Append('|')
                .Append(state.HeroPowerSpecial ?? "").Append('|').Append(state.HeroPowerHasDiscover).Append('|')
                .Append(state.HasSecondHeroPower).Append('|').Append(state.ExhaustedHeroPowerCount).Append('|')
                .Append(state.Phase ?? "").Append('|')
                .Append(state.HeroPowerCardId ?? "").Append('|')
                .Append(state.AnomalyId ?? "").Append('|')
                .Append(SerializeIds(state.AnomalyActiveEffectIds)).Append('|')
                .Append(state.HasAmbiguousPrimary).Append('|')
                .Append(SerializeIds(state.AvailableTribes)).Append('|')
                .Append(SerializeIds(state.ActiveTrinkets)).Append('|')
                .Append(SerializeInts(state.RecentlyBoughtEntityIds)).Append('|')
                .Append(SerializeInts(state.ActivatedThisTurn)).Append('|')
                .Append(SerializeHeroPowers(state.HeroPowers)).Append('|')
                .Append(state.LastUsedTavernSpellCardId ?? "").Append('|')
                .Append(state.LastUsedTavernSpellName ?? "").Append('|')
                .Append(SerializeCards(state.Shop)).Append('|')
                .Append(SerializeCards(state.Board)).Append('|')
                .Append(SerializeCards(state.Hand)).Append('|')
                .Append(SerializeIds(state.Discover)).Append('|')
                .Append(SerializeIds(state.Trinket)).Append('|')
                .Append(SerializeTrinkets(state.TrinketDetails)).Append('|')
                .Append(SerializeOpponents(state.Opponents));
            return sb.ToString();
        }

        private static string SerializeCards(IEnumerable<GlobalCardRef> cards)
        {
            if (cards == null) return "[]";
            var parts = new List<string>();
            foreach (var card in cards)
            {
                if (card == null) continue;
                parts.Add("{\"eid\":" + card.EntityId
                    + ",\"id\":\"" + Escape(card.CardId ?? "") + "\""
                    + ",\"name\":\"" + Escape(card.CardName ?? "") + "\""
                    + ",\"atk\":" + card.Attack
                    + ",\"hp\":" + card.Health
                    + ",\"tier\":" + card.Tier
                    + ",\"pos\":" + card.Position
                    + ",\"cost\":" + card.Cost
                    + ",\"golden\":" + Bool(card.Golden)
                    + ",\"taunt\":" + Bool(card.Taunt)
                    + ",\"divine\":" + Bool(card.DivineShield)
                    + ",\"windfury\":" + Bool(card.Windfury)
                    + ",\"megaWindfury\":" + Bool(card.MegaWindfury)
                    + ",\"stealth\":" + Bool(card.Stealth)
                    + ",\"reborn\":" + Bool(card.Reborn)
                    + ",\"poison\":" + Bool(card.Poisonous)
                    + ",\"venom\":" + Bool(card.Venomous)
                    + ",\"cleave\":" + Bool(card.Cleave)
                    + ",\"overkill\":" + Bool(card.Overkill)
                    + ",\"avenge\":" + card.AvengeCount
                    + ",\"spell\":" + Bool(card.Spell)
                    + ",\"frozen\":" + Bool(card.Frozen)
                    + ",\"grantsStats\":" + Bool(card.GrantsStats)
                    + ",\"tribe\":\"" + Escape(card.Tribe ?? "") + "\""
                    + ",\"text\":\"" + Escape(card.CardText ?? "") + "\"}");
            }
            return "[" + string.Join(",", parts) + "]";
        }

        private static string SerializeIds(IEnumerable<string> ids)
        {
            if (ids == null) return "[]";
            var parts = new List<string>();
            foreach (var id in ids)
            {
                if (string.IsNullOrEmpty(id)) continue;
                parts.Add("\"" + Escape(id) + "\"");
            }
            return "[" + string.Join(",", parts) + "]";
        }

        private static string SerializeOpponents(IEnumerable<GlobalOpponentRef> opponents)
        {
            if (opponents == null) return "[]";
            var parts = new List<string>();
            foreach (var opp in opponents)
            {
                if (opp == null) continue;
                parts.Add("{\"ctrl\":" + opp.ControllerId
                    + ",\"hero\":\"" + Escape(opp.HeroCardId ?? "") + "\""
                    + ",\"heroName\":\"" + Escape(opp.HeroName ?? "") + "\""
                    + ",\"hp\":" + opp.Health
                    + ",\"tier\":" + opp.TavernTier
                    + ",\"alive\":" + Bool(opp.Alive)
                    + ",\"board\":" + SerializeCards(opp.Board) + "}");
            }
            return "[" + string.Join(",", parts) + "]";
        }

        private static string SerializeInts(IEnumerable<int> values)
        {
            if (values == null) return "[]";
            return "[" + string.Join(",", values) + "]";
        }

        private static string SerializeHeroPowers(IEnumerable<GlobalHeroPowerRef> powers)
        {
            if (powers == null) return "[]";
            var parts = new List<string>();
            foreach (var power in powers)
            {
                if (power == null) continue;
                parts.Add("{\"id\":\"" + Escape(power.CardId ?? "") + "\""
                    + ",\"baseHero\":\"" + Escape(power.BaseHeroCardId ?? "") + "\""
                    + ",\"eid\":" + power.EntityId
                    + ",\"script\":" + power.ScriptData1
                    + ",\"cost\":" + power.Cost
                    + ",\"exhausted\":" + Bool(power.Exhausted)
                    + ",\"primary\":" + Bool(power.IsPrimary)
                    + ",\"secondary\":" + Bool(power.IsSecondary)
                    + ",\"active\":" + Bool(power.IsActive)
                    + ",\"unlocked\":" + Bool(power.IsUnlocked)
                    + ",\"discover\":" + Bool(power.HasDiscover)
                    + ",\"unlockTurn\":" + power.UnlockTurn
                    + ",\"unlockTier\":" + power.UnlockTier
                    + ",\"special\":\"" + Escape(power.SpecialRule ?? "") + "\"}");
            }
            return "[" + string.Join(",", parts) + "]";
        }

        private static string SerializeTrinkets(IEnumerable<GlobalTrinketRef> trinkets)
        {
            if (trinkets == null) return "[]";
            var parts = new List<string>();
            foreach (var trinket in trinkets)
            {
                if (trinket == null) continue;
                parts.Add("{\"id\":\"" + Escape(trinket.CardId ?? "") + "\""
                    + ",\"name\":\"" + Escape(trinket.TrinketName ?? "") + "\""
                    + ",\"eid\":" + trinket.EntityId
                    + ",\"lesser\":" + Bool(trinket.IsLesser)
                    + ",\"tier\":" + trinket.Tier
                    + ",\"atk\":" + trinket.Attack
                    + ",\"hp\":" + trinket.Health
                    + ",\"tribes\":" + SerializeIds(trinket.Tribes) + "}");
            }
            return "[" + string.Join(",", parts) + "]";
        }

        private static string Bool(bool value)
        {
            return value ? "true" : "false";
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");
        }

        private static string SanitizeFilePart(string value)
        {
            if (string.IsNullOrEmpty(value)) return "unknown";
            var sb = new StringBuilder();
            foreach (var ch in value)
            {
                if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_')
                    sb.Append(ch);
            }
            return sb.Length > 0 ? sb.ToString() : "unknown";
        }
    }

    public sealed class GlobalReplayState
    {
        public bool GameActive = true;
        public bool IsDuos;
        public string HeroCardId = "";
        public string HeroName = "";
        public int PlayerId = 1;
        public int Turn;
        public int Gold;
        public int LiveGold = -1;
        public int MaxGold;
        public int Tier = 1;
        public int Health = 30;
        public int MaxHealth = 30;
        public int Armor;
        public int FreeRefreshCount;
        public int TavernUpgradeCost = -1;
        public bool FrozenShop;
        public int LastUpgradeTurn = 1;
        public bool FirstPurchaseUsedThisTurn;
        public bool FirstMinionPurchaseUsedThisTurn;
        public bool NormalRecruitAvailable = true;
        public bool PassiveChoiceActive;
        public bool ReplenishingShopActive;
        public bool HeroPowerExhausted;
        public int HeroPowerCost = 1;
        public int HeroPowerDigsRemaining = -1;
        public string HeroPowerType = "Passive";
        public int HeroPowerUnlockTurn = 1;
        public int HeroPowerUnlockTier = 1;
        public string HeroPowerSpecial = "";
        public bool HeroPowerHasDiscover;
        public bool HasSecondHeroPower;
        public int ExhaustedHeroPowerCount;
        public string Phase = "shop";
        public string HeroPowerCardId = "";
        public string AnomalyId = "";
        public List<string> AnomalyActiveEffectIds = new List<string>();
        public bool HasAmbiguousPrimary;
        public List<string> AvailableTribes = new List<string>();
        public List<string> ActiveTrinkets = new List<string>();
        public List<int> RecentlyBoughtEntityIds = new List<int>();
        public List<int> ActivatedThisTurn = new List<int>();
        public List<GlobalHeroPowerRef> HeroPowers = new List<GlobalHeroPowerRef>();
        public string LastUsedTavernSpellCardId = "";
        public string LastUsedTavernSpellName = "";
        public List<GlobalCardRef> Shop = new List<GlobalCardRef>();
        public List<GlobalCardRef> Board = new List<GlobalCardRef>();
        public List<GlobalCardRef> Hand = new List<GlobalCardRef>();
        public List<string> Discover = new List<string>();
        public List<string> Trinket = new List<string>();
        public List<GlobalTrinketRef> TrinketDetails = new List<GlobalTrinketRef>();
        public List<GlobalOpponentRef> Opponents = new List<GlobalOpponentRef>();

#if !BOBCOACH_REPLAY_CORE_ONLY
        public static GlobalReplayState FromJson(string json)
        {
            var root = JObject.Parse(json ?? "{}");
            var state = new GlobalReplayState
            {
                GameActive = Bool(root, "gameActive", true),
                IsDuos = Bool(root, "isDuos", false),
                HeroCardId = FirstString(root, "heroCardId", "hero"),
                HeroName = FirstString(root, "heroName"),
                PlayerId = Int(root, "player", 1),
                Turn = Int(root, "turn", 1),
                Gold = Int(root, "gold", 0),
                LiveGold = Int(root, "liveGold", -1),
                MaxGold = Int(root, "maxGold", 3),
                Tier = Int(root, "tier", 1),
                Health = Int(root, "health", 30),
                MaxHealth = Int(root, "maxHealth", 30),
                Armor = Int(root, "armor", 0),
                FreeRefreshCount = Int(root, "freeRefreshCount", 0),
                TavernUpgradeCost = Int(root, "tavernUpgradeCost", -1),
                FrozenShop = Bool(root, "frozenShop", false),
                LastUpgradeTurn = Int(root, "lastUpgradeTurn", 1),
                FirstPurchaseUsedThisTurn = Bool(root, "firstPurchaseUsed", false),
                FirstMinionPurchaseUsedThisTurn = Bool(root, "firstMinionPurchaseUsed", false),
                NormalRecruitAvailable = Bool(root, "normalRecruitAvailable", true),
                PassiveChoiceActive = Bool(root, "passiveChoiceActive", false),
                ReplenishingShopActive = Bool(root, "replenishingShop", false),
                HeroPowerExhausted = Bool(root, "heroPowerExhausted", false),
                HeroPowerCost = Int(root, "heroPowerCost", 1),
                HeroPowerDigsRemaining = Int(root, "heroPowerDigs", -1),
                HeroPowerType = FirstString(root, "heroPowerType", "Passive"),
                HeroPowerUnlockTurn = Int(root, "heroPowerUnlockTurn", 1),
                HeroPowerUnlockTier = Int(root, "heroPowerUnlockTier", 1),
                HeroPowerSpecial = FirstString(root, "heroPowerSpecial"),
                HeroPowerHasDiscover = Bool(root, "heroPowerHasDiscover", false),
                HasSecondHeroPower = Bool(root, "hasSecondHeroPower", false),
                ExhaustedHeroPowerCount = Int(root, "exhaustedHeroPowerCount", 0),
                Phase = FirstString(root, "phase", "shop"),
                HeroPowerCardId = FirstString(root, "heroPower"),
                AnomalyId = FirstString(root, "anomaly"),
                HasAmbiguousPrimary = Bool(root, "anomalyAmbiguous", false),
            };
            state.AnomalyActiveEffectIds = ReadStrings(root, "anomalyEffects");
            state.AvailableTribes = ReadStrings(root, "tribes");
            state.ActiveTrinkets = ReadStrings(root, "activeTrinkets");
            state.RecentlyBoughtEntityIds = ReadInts(root, "recentlyBought");
            state.ActivatedThisTurn = ReadInts(root, "activatedThisTurn");
            state.HeroPowers = ReadHeroPowers(root, "heroPowers");
            state.Shop = ReadCards(root, "shop");
            state.Board = ReadCards(root, "board");
            state.Hand = ReadCards(root, "hand");
            state.Discover = ReadStrings(root, "discover");
            state.Trinket = ReadStrings(root, "trinket");
            state.TrinketDetails = ReadTrinkets(root, "trinketDetails");
            state.Opponents = ReadOpponents(root, "opponents");
            state.LastUsedTavernSpellCardId = FirstString(root, "lastTavernSpell");
            state.LastUsedTavernSpellName = FirstString(root, "lastTavernSpellName");
            return state;
        }

        private static int Int(JObject root, string name, int fallback)
        {
            var token = root != null ? root[name] : null;
            if (token == null || token.Type == JTokenType.Null) return fallback;
            try { return token.Value<int>(); }
            catch { return fallback; }
        }

        private static bool Bool(JObject root, string name, bool fallback)
        {
            var token = root != null ? root[name] : null;
            if (token == null || token.Type == JTokenType.Null) return fallback;
            try { return token.Value<bool>(); }
            catch { return fallback; }
        }

        private static string FirstString(JObject root, params string[] names)
        {
            if (root == null) return "";
            foreach (var name in names)
            {
                var token = root[name];
                if (token != null && token.Type == JTokenType.String)
                    return token.Value<string>() ?? "";
            }
            return "";
        }

        private static List<string> ReadStrings(JObject root, string name)
        {
            var result = new List<string>();
            var token = root != null ? root[name] : null;
            if (token == null || token.Type != JTokenType.Array) return result;
            foreach (var item in token.Values<string>())
            {
                if (!string.IsNullOrEmpty(item)) result.Add(item);
            }
            return result;
        }

        private static List<int> ReadInts(JObject root, string name)
        {
            var result = new List<int>();
            var token = root != null ? root[name] : null;
            if (token == null || token.Type != JTokenType.Array) return result;
            foreach (var item in token.Values<int>()) result.Add(item);
            return result;
        }

        private static List<GlobalCardRef> ReadCards(JObject root, string name)
        {
            var result = new List<GlobalCardRef>();
            var token = root != null ? root[name] : null;
            if (token == null || token.Type != JTokenType.Array) return result;
            foreach (var item in token.OfType<JObject>())
            {
                result.Add(new GlobalCardRef
                {
                    EntityId = Int(item, "eid", 0),
                    CardId = FirstString(item, "id", "cardId"),
                    CardName = FirstString(item, "name", "cardName"),
                    Attack = Int(item, "atk", 0),
                    Health = Int(item, "hp", 0),
                    Tier = Int(item, "tier", 1),
                    Position = Int(item, "pos", 0),
                    Cost = Int(item, "cost", -1),
                    Golden = Bool(item, "golden", false),
                    Taunt = Bool(item, "taunt", false),
                    DivineShield = Bool(item, "divine", false),
                    Windfury = Bool(item, "windfury", false),
                    MegaWindfury = Bool(item, "megaWindfury", false),
                    Stealth = Bool(item, "stealth", false),
                    Reborn = Bool(item, "reborn", false),
                    Poisonous = Bool(item, "poison", false),
                    Venomous = Bool(item, "venom", false),
                    Cleave = Bool(item, "cleave", false),
                    Overkill = Bool(item, "overkill", false),
                    AvengeCount = Int(item, "avenge", 0),
                    Spell = Bool(item, "spell", false),
                    Frozen = Bool(item, "frozen", false),
                    GrantsStats = Bool(item, "grantsStats", false),
                    Tribe = FirstString(item, "tribe"),
                    CardText = FirstString(item, "text", "cardText"),
                });
            }
            return result;
        }

        private static List<GlobalHeroPowerRef> ReadHeroPowers(JObject root, string name)
        {
            var result = new List<GlobalHeroPowerRef>();
            var token = root != null ? root[name] : null;
            if (token == null || token.Type != JTokenType.Array) return result;
            foreach (var item in token.OfType<JObject>())
            {
                result.Add(new GlobalHeroPowerRef
                {
                    CardId = FirstString(item, "id", "cardId"),
                    BaseHeroCardId = FirstString(item, "baseHero", "baseHeroCardId"),
                    EntityId = Int(item, "eid", 0),
                    ScriptData1 = Int(item, "script", -1),
                    Cost = Int(item, "cost", 0),
                    Exhausted = Bool(item, "exhausted", false),
                    IsPrimary = Bool(item, "primary", false),
                    IsSecondary = Bool(item, "secondary", false),
                    IsActive = Bool(item, "active", false),
                    IsUnlocked = Bool(item, "unlocked", true),
                    HasDiscover = Bool(item, "discover", false),
                    UnlockTurn = Int(item, "unlockTurn", 1),
                    UnlockTier = Int(item, "unlockTier", 1),
                    SpecialRule = FirstString(item, "special"),
                });
            }
            return result;
        }

        private static List<GlobalTrinketRef> ReadTrinkets(JObject root, string name)
        {
            var result = new List<GlobalTrinketRef>();
            var token = root != null ? root[name] : null;
            if (token == null || token.Type != JTokenType.Array) return result;
            foreach (var item in token.OfType<JObject>())
            {
                result.Add(new GlobalTrinketRef
                {
                    CardId = FirstString(item, "id", "cardId"),
                    TrinketName = FirstString(item, "name", "trinketName"),
                    EntityId = Int(item, "eid", 0),
                    IsLesser = Bool(item, "lesser", false),
                    Tier = Int(item, "tier", 0),
                    Attack = Int(item, "atk", 0),
                    Health = Int(item, "hp", 0),
                    Tribes = ReadStrings(item, "tribes"),
                });
            }
            return result;
        }

        private static List<GlobalOpponentRef> ReadOpponents(JObject root, string name)
        {
            var result = new List<GlobalOpponentRef>();
            var token = root != null ? root[name] : null;
            if (token == null || token.Type != JTokenType.Array) return result;
            foreach (var item in token.OfType<JObject>())
            {
                result.Add(new GlobalOpponentRef
                {
                    ControllerId = Int(item, "ctrl", 0),
                    HeroCardId = FirstString(item, "hero", "heroCardId"),
                    HeroName = FirstString(item, "heroName"),
                    Health = Int(item, "hp", 30),
                    TavernTier = Int(item, "tier", 1),
                    Alive = Bool(item, "alive", true),
                    Board = ReadCards(item, "board"),
                });
            }
            return result;
        }

        public GameState ToGameState()
        {
            var state = new GameState
            {
                GameActive = GameActive,
                IsDuos = IsDuos,
                HeroCardId = HeroCardId ?? "",
                HeroName = HeroName ?? "",
                PlayerId = PlayerId,
                Turn = Turn,
                Gold = Gold,
                LiveGold = LiveGold,
                MaxGold = MaxGold,
                TavernTier = Tier,
                TavernUpgradeCost = TavernUpgradeCost,
                Health = Health,
                MaxHealth = MaxHealth,
                Armor = Armor,
                FreeRefreshCount = FreeRefreshCount,
                FrozenShop = FrozenShop,
                LastUpgradeTurn = LastUpgradeTurn,
                FirstPurchaseUsedThisTurn = FirstPurchaseUsedThisTurn,
                FirstMinionPurchaseUsedThisTurn = FirstMinionPurchaseUsedThisTurn,
                NormalRecruitAvailable = NormalRecruitAvailable,
                PassiveChoiceActive = PassiveChoiceActive,
                ReplenishingShopActive = ReplenishingShopActive,
                Phase = Phase ?? "shop",
                HeroPowerExhausted = HeroPowerExhausted,
                HeroPowerCost = HeroPowerCost,
                HeroPowerDigsRemaining = HeroPowerDigsRemaining,
                HeroPowerType = HeroPowerType ?? "Passive",
                HeroPowerUnlockTurn = HeroPowerUnlockTurn,
                HeroPowerUnlockTier = HeroPowerUnlockTier,
                HeroPowerSpecial = HeroPowerSpecial ?? "",
                HeroPowerHasDiscover = HeroPowerHasDiscover,
                HasSecondHeroPower = HasSecondHeroPower,
                ExhaustedHeroPowerCount = ExhaustedHeroPowerCount,
                HeroPowerCardId = HeroPowerCardId ?? "",
                AnomalyId = AnomalyId ?? "",
                AnomalyContext = AnomalyContext.FromSnapshot(
                    AnomalyId ?? "",
                    AnomalyActiveEffectIds ?? new List<string>(),
                    HasAmbiguousPrimary),
                AvailableTribes = new HashSet<string>(
                    (AvailableTribes ?? new List<string>())
                    .Where(value => !string.IsNullOrEmpty(value))
                    .Select(value => value),
                    StringComparer.Ordinal),
                ActiveTrinkets = new List<string>(
                    (ActiveTrinkets ?? new List<string>())
                    .Where(value => !string.IsNullOrEmpty(value))),
                RecentlyBoughtEntityIds = new HashSet<int>(
                    RecentlyBoughtEntityIds ?? new List<int>()),
                ActivatedThisTurn = new HashSet<int>(
                    ActivatedThisTurn ?? new List<int>()),
                HeroPowers = (HeroPowers ?? new List<GlobalHeroPowerRef>())
                    .Where(power => power != null)
                    .Select(ToHeroPowerState)
                    .ToList(),
                LastUsedTavernSpellCardId = LastUsedTavernSpellCardId ?? "",
                LastUsedTavernSpellName = LastUsedTavernSpellName ?? "",
                BoardMinions = (Board ?? new List<GlobalCardRef>())
                    .Where(card => card != null)
                    .Select(ToMinionData)
                    .ToList(),
                HandMinions = (Hand ?? new List<GlobalCardRef>())
                    .Where(card => card != null)
                    .Select(ToMinionData)
                    .ToList(),
                ShopMinions = (Shop ?? new List<GlobalCardRef>())
                    .Where(card => card != null)
                    .Select(ToMinionData)
                    .ToList(),
                TrinketOffer = ToTrinketOptions(),
                DiscoverOptions = (Discover ?? new List<string>())
                    .Where(value => !string.IsNullOrEmpty(value))
                    .Select(value => new TrinketOption { CardId = value })
                    .ToList(),
                Opponents = (Opponents ?? new List<GlobalOpponentRef>())
                    .Where(opponent => opponent != null)
                    .Select(ToOpponentData)
                    .ToList(),
                EffectiveRules = EffectiveGameRules.Default,
            };
            return state;
        }

        private List<TrinketOption> ToTrinketOptions()
        {
            var details = TrinketDetails ?? new List<GlobalTrinketRef>();
            var result = details
                .Where(trinket => trinket != null)
                .Select(trinket => new TrinketOption
                {
                    CardId = trinket.CardId ?? "",
                    TrinketName = trinket.TrinketName ?? "",
                    EntityId = trinket.EntityId,
                    IsLesser = trinket.IsLesser,
                    Tier = trinket.Tier,
                    Attack = trinket.Attack,
                    Health = trinket.Health,
                    Tribes = trinket.Tribes ?? new List<string>(),
                })
                .ToList();
            if (result.Count > 0) return result;
            foreach (var cardId in Trinket ?? new List<string>())
            {
                if (!string.IsNullOrEmpty(cardId))
                    result.Add(new TrinketOption { CardId = cardId });
            }
            return result;
        }

        private static HeroPowerState ToHeroPowerState(GlobalHeroPowerRef power)
        {
            return new HeroPowerState
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

        private static MinionData ToMinionData(GlobalCardRef card)
        {
            return new MinionData
            {
                CardId = card.CardId ?? "",
                CardName = card.CardName ?? "",
                EntityId = card.EntityId,
                Attack = card.Attack,
                Health = card.Health,
                Tier = card.Tier,
                Position = card.Position,
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
                IsSpell = card.Spell,
                IsFrozen = card.Frozen,
                GrantsStats = card.GrantsStats,
                Tribe = card.Tribe ?? "",
                CardText = card.CardText ?? "",
                Cost = card.Cost,
            };
        }

        private static OpponentData ToOpponentData(GlobalOpponentRef opponent)
        {
            return new OpponentData
            {
                ControllerId = opponent.ControllerId,
                HeroCardId = opponent.HeroCardId ?? "",
                HeroName = opponent.HeroName ?? "",
                Health = opponent.Health,
                TavernTier = opponent.TavernTier,
                Alive = opponent.Alive,
                BoardMinions = (opponent.Board ?? new List<GlobalCardRef>())
                    .Where(card => card != null)
                    .Select(ToMinionData)
                    .ToList(),
            };
        }
#endif
    }

    public sealed class GlobalCardRef
    {
        public int EntityId;
        public string CardId = "";
        public string CardName = "";
        public int Attack;
        public int Health;
        public int Tier;
        public int Position;
        public int Cost = -1;
        public bool Golden;
        public bool Taunt;
        public bool DivineShield;
        public bool Windfury;
        public bool MegaWindfury;
        public bool Stealth;
        public bool Reborn;
        public bool Poisonous;
        public bool Venomous;
        public bool Cleave;
        public bool Overkill;
        public int AvengeCount;
        public bool Spell;
        public bool Frozen;
        public bool GrantsStats;
        public string Tribe = "";
        public string CardText = "";
    }

    public sealed class GlobalOpponentRef
    {
        public int ControllerId;
        public string HeroCardId = "";
        public string HeroName = "";
        public int Health = 30;
        public int TavernTier = 1;
        public bool Alive = true;
        public List<GlobalCardRef> Board = new List<GlobalCardRef>();
    }

    public sealed class GlobalHeroPowerRef
    {
        public string CardId = "";
        public string BaseHeroCardId = "";
        public int EntityId;
        public int ScriptData1 = -1;
        public int Cost;
        public bool Exhausted;
        public bool IsPrimary;
        public bool IsSecondary;
        public bool IsActive;
        public bool IsUnlocked;
        public bool HasDiscover;
        public int UnlockTurn = 1;
        public int UnlockTier = 1;
        public string SpecialRule = "";
    }

    public sealed class GlobalTrinketRef
    {
        public string CardId = "";
        public string TrinketName = "";
        public int EntityId;
        public bool IsLesser;
        public int Tier;
        public int Attack;
        public int Health;
        public List<string> Tribes = new List<string>();
    }
}
