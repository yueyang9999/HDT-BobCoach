using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BobCoach.Engine;
using Newtonsoft.Json;

namespace BobCoach.Engine.Decision
{
    public sealed class DecisionEventRecord
    {
        public const int SchemaVersion = 3;

        public string DecisionId { get; private set; }
        public string GameId { get; private set; }
        public GameStateSnapshot State { get; private set; }
        public IList<string> CandidateKeys { get; private set; }
        public string StateFingerprint { get; private set; }
        public string CandidateFingerprint { get; private set; }

        public static DecisionEventRecord Create(string decisionId, string gameId, GameState state,
            IEnumerable<ActionDescriptor> candidates)
        {
            if (string.IsNullOrWhiteSpace(decisionId)) throw new ArgumentException("decisionId");
            if (string.IsNullOrWhiteSpace(gameId)) throw new ArgumentException("gameId");
            if (state == null) throw new ArgumentNullException("state");

            var snapshot = GameStateSnapshot.From(state);
            var keys = (candidates ?? Enumerable.Empty<ActionDescriptor>())
                .Where(candidate => candidate != null)
                .Select(candidate => candidate.ActionKey ?? "")
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToList();
            var immutableKeys = new ReadOnlyCollection<string>(keys);

            return new DecisionEventRecord
            {
                DecisionId = decisionId,
                GameId = gameId,
                State = snapshot,
                CandidateKeys = immutableKeys,
                StateFingerprint = Fingerprint(CanonicalJson.Serialize(snapshot)),
                CandidateFingerprint = Fingerprint(CanonicalJson.Serialize(immutableKeys))
            };
        }

        private static string Fingerprint(string canonicalJson)
        {
            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(canonicalJson)))
                    .Replace("-", "").ToLowerInvariant();
            }
        }
    }

    public sealed class GameStateSnapshot
    {
        public const string UnknownIdentity = "unknown";

        public bool GameActive { get; private set; }
        public bool IsDuos { get; private set; }
        public string GameBuild { get; private set; }
        public string SeasonId { get; private set; }
        public string CardPoolIdentity { get; private set; }
        public string HeroCardId { get; private set; }
        public string AnomalyId { get; private set; }
        public int Gold { get; private set; }
        public int LiveGold { get; private set; }
        public int MaxGold { get; private set; }
        public int TavernTier { get; private set; }
        public int TavernUpgradeCost { get; private set; }
        public int Health { get; private set; }
        public int MaxHealth { get; private set; }
        public int Armor { get; private set; }
        public int Turn { get; private set; }
        public string Phase { get; private set; }
        public bool FrozenShop { get; private set; }
        public int FreeRefreshCount { get; private set; }
        public bool FirstPurchaseUsedThisTurn { get; private set; }
        public bool FirstMinionPurchaseUsedThisTurn { get; private set; }
        public string CurrentCompGuideTier { get; private set; }
        public IList<string> AvailableTribes { get; private set; }
        public IList<MinionSnapshot> BoardMinions { get; private set; }
        public IList<MinionSnapshot> HandMinions { get; private set; }
        public IList<MinionSnapshot> ShopMinions { get; private set; }
        public IList<TrinketSnapshot> TrinketOffer { get; private set; }
        public IList<string> ActiveTrinkets { get; private set; }

        public static GameStateSnapshot From(GameState state)
        {
            if (state == null) throw new ArgumentNullException("state");

            return new GameStateSnapshot
            {
                GameActive = state.GameActive,
                IsDuos = state.IsDuos,
                GameBuild = NormalizeIdentity(state.GameBuild),
                SeasonId = NormalizeIdentity(state.SeasonId),
                CardPoolIdentity = NormalizeIdentity(state.CardPoolIdentity),
                HeroCardId = state.HeroCardId ?? "",
                AnomalyId = state.AnomalyId ?? "",
                Gold = state.Gold,
                LiveGold = state.LiveGold,
                MaxGold = state.MaxGold,
                TavernTier = state.TavernTier,
                TavernUpgradeCost = state.TavernUpgradeCost,
                Health = state.Health,
                MaxHealth = state.MaxHealth,
                Armor = state.Armor,
                Turn = state.Turn,
                Phase = state.Phase ?? "",
                FrozenShop = state.FrozenShop,
                FreeRefreshCount = state.FreeRefreshCount,
                FirstPurchaseUsedThisTurn = state.FirstPurchaseUsedThisTurn,
                FirstMinionPurchaseUsedThisTurn = state.FirstMinionPurchaseUsedThisTurn,
                CurrentCompGuideTier = NormalizeIdentity(state.CurrentCompGuideTier),
                AvailableTribes = ReadOnlyStrings(state.AvailableTribes),
                BoardMinions = ReadOnlyMinions(state.BoardMinions),
                HandMinions = ReadOnlyMinions(state.HandMinions),
                ShopMinions = ReadOnlyMinions(state.ShopMinions),
                TrinketOffer = ReadOnlyTrinkets(state.TrinketOffer),
                ActiveTrinkets = ReadOnlyStrings(state.ActiveTrinkets)
            };
        }

        private static IList<string> ReadOnlyStrings(IEnumerable<string> values)
        {
            var copy = (values ?? Enumerable.Empty<string>())
                .Select(value => value ?? "")
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToList();
            return new ReadOnlyCollection<string>(copy);
        }

        private static string NormalizeIdentity(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? UnknownIdentity : value.Trim();
        }

        private static IList<MinionSnapshot> ReadOnlyMinions(IEnumerable<MinionData> values)
        {
            var copy = (values ?? Enumerable.Empty<MinionData>())
                .Where(value => value != null)
                .Select(MinionSnapshot.From)
                .ToList();
            return new ReadOnlyCollection<MinionSnapshot>(copy);
        }

        private static IList<TrinketSnapshot> ReadOnlyTrinkets(IEnumerable<TrinketOption> values)
        {
            var copy = (values ?? Enumerable.Empty<TrinketOption>())
                .Where(value => value != null)
                .Select(TrinketSnapshot.From)
                .ToList();
            return new ReadOnlyCollection<TrinketSnapshot>(copy);
        }
    }

    public sealed class MinionSnapshot
    {
        public string CardId { get; private set; }
        public int EntityId { get; private set; }
        public int Attack { get; private set; }
        public int Health { get; private set; }
        public int Tier { get; private set; }
        public int Position { get; private set; }
        public bool Golden { get; private set; }
        public bool Taunt { get; private set; }
        public bool DivineShield { get; private set; }
        public bool Windfury { get; private set; }
        public bool MegaWindfury { get; private set; }
        public bool Stealth { get; private set; }
        public bool Reborn { get; private set; }
        public bool Poisonous { get; private set; }
        public bool Venomous { get; private set; }
        public bool Cleave { get; private set; }
        public bool Overkill { get; private set; }
        public bool IsSpell { get; private set; }
        public int Cost { get; private set; }
        public string Tribe { get; private set; }

        internal static MinionSnapshot From(MinionData minion)
        {
            return new MinionSnapshot
            {
                CardId = minion.CardId ?? "",
                EntityId = minion.EntityId,
                Attack = minion.Attack,
                Health = minion.Health,
                Tier = minion.Tier,
                Position = minion.Position,
                Golden = minion.Golden,
                Taunt = minion.Taunt,
                DivineShield = minion.DivineShield,
                Windfury = minion.Windfury,
                MegaWindfury = minion.MegaWindfury,
                Stealth = minion.Stealth,
                Reborn = minion.Reborn,
                Poisonous = minion.Poisonous,
                Venomous = minion.Venomous,
                Cleave = minion.Cleave,
                Overkill = minion.Overkill,
                IsSpell = minion.IsSpell,
                Cost = minion.Cost,
                Tribe = minion.Tribe ?? ""
            };
        }
    }

    public sealed class TrinketSnapshot
    {
        public string CardId { get; private set; }
        public string TrinketName { get; private set; }
        public int EntityId { get; private set; }
        public bool IsLesser { get; private set; }
        public int Tier { get; private set; }
        public int Attack { get; private set; }
        public int Health { get; private set; }
        public IList<string> Tribes { get; private set; }

        internal static TrinketSnapshot From(TrinketOption trinket)
        {
            return new TrinketSnapshot
            {
                CardId = trinket.CardId ?? "",
                TrinketName = trinket.TrinketName ?? "",
                EntityId = trinket.EntityId,
                IsLesser = trinket.IsLesser,
                Tier = trinket.Tier,
                Attack = trinket.Attack,
                Health = trinket.Health,
                Tribes = new ReadOnlyCollection<string>(
                    (trinket.Tribes ?? Enumerable.Empty<string>())
                    .Select(value => value ?? "")
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToList())
            };
        }
    }

    internal static class CanonicalJson
    {
        internal static string Serialize(GameStateSnapshot state)
        {
            var builder = new StringBuilder();
            builder.Append('{');
            WriteProperty(builder, "gameActive", state.GameActive);
            WriteProperty(builder, "isDuos", state.IsDuos);
            WriteProperty(builder, "gameBuild", state.GameBuild);
            WriteProperty(builder, "seasonId", state.SeasonId);
            WriteProperty(builder, "cardPoolIdentity", state.CardPoolIdentity);
            WriteProperty(builder, "heroCardId", state.HeroCardId);
            WriteProperty(builder, "anomalyId", state.AnomalyId);
            WriteProperty(builder, "gold", state.Gold);
            WriteProperty(builder, "liveGold", state.LiveGold);
            WriteProperty(builder, "maxGold", state.MaxGold);
            WriteProperty(builder, "tavernTier", state.TavernTier);
            WriteProperty(builder, "tavernUpgradeCost", state.TavernUpgradeCost);
            WriteProperty(builder, "health", state.Health);
            WriteProperty(builder, "maxHealth", state.MaxHealth);
            WriteProperty(builder, "armor", state.Armor);
            WriteProperty(builder, "turn", state.Turn);
            WriteProperty(builder, "phase", state.Phase);
            WriteProperty(builder, "frozenShop", state.FrozenShop);
            WriteProperty(builder, "freeRefreshCount", state.FreeRefreshCount);
            WriteProperty(builder, "firstPurchaseUsedThisTurn", state.FirstPurchaseUsedThisTurn);
            WriteProperty(builder, "firstMinionPurchaseUsedThisTurn", state.FirstMinionPurchaseUsedThisTurn);
            WriteProperty(builder, "currentCompGuideTier", state.CurrentCompGuideTier);
            WriteProperty(builder, "availableTribes", state.AvailableTribes);
            WriteProperty(builder, "boardMinions", state.BoardMinions);
            WriteProperty(builder, "handMinions", state.HandMinions);
            WriteProperty(builder, "shopMinions", state.ShopMinions, false);
            WriteProperty(builder, "trinketOffer", state.TrinketOffer);
            WriteProperty(builder, "activeTrinkets", state.ActiveTrinkets, false);
            builder.Append('}');
            return builder.ToString();
        }

        internal static string Serialize(IEnumerable<string> values)
        {
            var builder = new StringBuilder();
            WriteStrings(builder, values);
            return builder.ToString();
        }

        private static void WriteProperty(StringBuilder builder, string name, object value,
            bool trailingComma = true)
        {
            builder.Append(JsonConvert.ToString(name));
            builder.Append(':');
            var strings = value as IEnumerable<string>;
            var minions = value as IEnumerable<MinionSnapshot>;
            var trinkets = value as IEnumerable<TrinketSnapshot>;
            if (strings != null) WriteStrings(builder, strings);
            else if (minions != null) WriteMinions(builder, minions);
            else if (trinkets != null) WriteTrinkets(builder, trinkets);
            else if (value is string) builder.Append(JsonConvert.ToString((string)value));
            else if (value is bool) builder.Append((bool)value ? "true" : "false");
            else builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
            if (trailingComma) builder.Append(',');
        }

        private static void WriteStrings(StringBuilder builder, IEnumerable<string> values)
        {
            builder.Append('[');
            var first = true;
            foreach (var value in values ?? Enumerable.Empty<string>())
            {
                if (!first) builder.Append(',');
                builder.Append(JsonConvert.ToString(value ?? ""));
                first = false;
            }
            builder.Append(']');
        }

        private static void WriteMinions(StringBuilder builder, IEnumerable<MinionSnapshot> minions)
        {
            builder.Append('[');
            var first = true;
            foreach (var minion in minions ?? Enumerable.Empty<MinionSnapshot>())
            {
                if (!first) builder.Append(',');
                builder.Append('{');
                WriteProperty(builder, "cardId", minion.CardId);
                WriteProperty(builder, "entityId", minion.EntityId);
                WriteProperty(builder, "attack", minion.Attack);
                WriteProperty(builder, "health", minion.Health);
                WriteProperty(builder, "tier", minion.Tier);
                WriteProperty(builder, "position", minion.Position);
                WriteProperty(builder, "golden", minion.Golden);
                WriteProperty(builder, "taunt", minion.Taunt);
                WriteProperty(builder, "divineShield", minion.DivineShield);
                WriteProperty(builder, "windfury", minion.Windfury);
                WriteProperty(builder, "megaWindfury", minion.MegaWindfury);
                WriteProperty(builder, "stealth", minion.Stealth);
                WriteProperty(builder, "reborn", minion.Reborn);
                WriteProperty(builder, "poisonous", minion.Poisonous);
                WriteProperty(builder, "venomous", minion.Venomous);
                WriteProperty(builder, "cleave", minion.Cleave);
                WriteProperty(builder, "overkill", minion.Overkill);
                WriteProperty(builder, "isSpell", minion.IsSpell);
                WriteProperty(builder, "cost", minion.Cost);
                WriteProperty(builder, "tribe", minion.Tribe, false);
                builder.Append('}');
                first = false;
            }
            builder.Append(']');
        }

        private static void WriteTrinkets(StringBuilder builder, IEnumerable<TrinketSnapshot> trinkets)
        {
            builder.Append('[');
            var first = true;
            foreach (var trinket in trinkets ?? Enumerable.Empty<TrinketSnapshot>())
            {
                if (!first) builder.Append(',');
                builder.Append('{');
                WriteProperty(builder, "cardId", trinket.CardId);
                WriteProperty(builder, "trinketName", trinket.TrinketName);
                WriteProperty(builder, "entityId", trinket.EntityId);
                WriteProperty(builder, "isLesser", trinket.IsLesser);
                WriteProperty(builder, "tier", trinket.Tier);
                WriteProperty(builder, "attack", trinket.Attack);
                WriteProperty(builder, "health", trinket.Health);
                WriteProperty(builder, "tribes", trinket.Tribes, false);
                builder.Append('}');
                first = false;
            }
            builder.Append(']');
        }
    }
}
