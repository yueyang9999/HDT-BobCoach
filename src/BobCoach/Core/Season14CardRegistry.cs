using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BobCoach.Engine
{
    /// <summary>Shared generated-card registry base for season14 mechanic registries.</summary>
    internal abstract class Season14CardRegistry
    {
        private readonly string _registryFileName;
        private readonly Dictionary<string, Season14MechanicCardFact> _byCardId =
            new Dictionary<string, Season14MechanicCardFact>(StringComparer.Ordinal);
        private bool _loaded;

        protected Season14CardRegistry(string registryFileName)
        {
            _registryFileName = registryFileName;
        }

        internal string ConfiguredRoot { get; private set; }

        internal bool IsLoaded { get { return _loaded; } }

        internal int Count { get { return _byCardId.Count; } }

        internal void LoadFromConfiguredRoot()
        {
            ConfiguredRoot = null;
            _byCardId.Clear();
            _loaded = false;

            string configuredRoot =
                Environment.GetEnvironmentVariable(BobCoachDataPaths.RootEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(configuredRoot)) return;

            try
            {
                var root = BobCoachDataPaths.ResolveRoot(configuredRoot, null);
                if (root == null) return;
                ConfiguredRoot = root;
                var file = BobCoachDataPaths.Combine(
                    root, "generated", "registries", _registryFileName);
                if (!File.Exists(file)) return;
                LoadFromJson(File.ReadAllText(file, Encoding.UTF8));
            }
            catch
            {
                _byCardId.Clear();
                _loaded = false;
            }
        }

        internal void LoadFromJson(string json)
        {
            _byCardId.Clear();
            _loaded = false;
            if (string.IsNullOrWhiteSpace(json)) return;

            try
            {
                var root = Season14RegistryJsonParser.ParseRootObject(json);
                if (root == null) return;
                var cards = Season14RegistryJsonParser.AsArray(root, "cards");
                if (cards == null || ContainsBattlegroundsDuoCard(cards)) return;

                foreach (var item in cards)
                {
                    var row = item as Dictionary<string, object>;
                    if (row == null) continue;
                    string cardId = Season14RegistryJsonParser.AsString(row, "cardId");
                    if (string.IsNullOrEmpty(cardId) || _byCardId.ContainsKey(cardId)) continue;
                    _byCardId[cardId] = ParseFact(cardId, row);
                }
                _loaded = _byCardId.Count > 0;
            }
            catch
            {
                _byCardId.Clear();
                _loaded = false;
            }
        }

        private static bool ContainsBattlegroundsDuoCard(List<object> cards)
        {
            foreach (var item in cards)
            {
                var row = item as Dictionary<string, object>;
                if (row == null) continue;
                string cardId = Season14RegistryJsonParser.AsString(row, "cardId");
                if (Season14RegistryJsonParser.IsBattlegroundsDuoCardId(cardId)) return true;
            }
            return false;
        }

        internal bool TryGet(string cardId, out Season14MechanicCardFact fact)
        {
            fact = null;
            if (!_loaded || string.IsNullOrEmpty(cardId)) return false;
            return _byCardId.TryGetValue(cardId, out fact);
        }

        internal List<string> VerifyCoverage(IEnumerable<string> requiredCardIds)
        {
            var missing = new List<string>();
            if (requiredCardIds == null) return missing;
            foreach (string cardId in requiredCardIds)
            {
                Season14MechanicCardFact ignored;
                if (string.IsNullOrEmpty(cardId) || !TryGet(cardId, out ignored))
                    missing.Add(cardId);
            }
            return missing;
        }

        internal void WriteDiagnostic(string message)
        {
            try
            {
                if (string.IsNullOrEmpty(ConfiguredRoot) || string.IsNullOrEmpty(message)) return;
                var line = string.Format(
                    "[{0:O}] [{1}] {2}\n", DateTime.UtcNow, GetType().Name, message);
                File.AppendAllText(
                    Path.Combine(ConfiguredRoot, "bob_coach.log"), line, Encoding.UTF8);
            }
            catch { }
        }

        private static Season14MechanicCardFact ParseFact(
            string cardId, Dictionary<string, object> row)
        {
            return new Season14MechanicCardFact
            {
                CardId = cardId,
                NameZhCn = Season14RegistryJsonParser.AsString(row, "name_cn"),
                NameEnUs = Season14RegistryJsonParser.AsString(row, "name_en"),
                Type = Season14RegistryJsonParser.AsString(row, "type"),
                Set = Season14RegistryJsonParser.AsString(row, "set"),
                CardClass = Season14RegistryJsonParser.AsString(row, "cardClass"),
                Cost = Season14RegistryJsonParser.AsInt(row, "cost"),
                TechLevel = Season14RegistryJsonParser.AsInt(row, "techLevel"),
                Attack = Season14RegistryJsonParser.AsInt(row, "attack"),
                Health = Season14RegistryJsonParser.AsInt(row, "health"),
                Armor = Season14RegistryJsonParser.AsInt(row, "armor"),
                SpellSchool = Season14RegistryJsonParser.AsString(row, "spellSchool"),
                Race = Season14RegistryJsonParser.AsString(row, "race"),
                TextZhCn = Season14RegistryJsonParser.AsString(row, "text_cn"),
                TextEnUs = Season14RegistryJsonParser.AsString(row, "text_en"),
                IsBattlegroundsPoolMinion =
                    Season14RegistryJsonParser.AsBool(row, "isBattlegroundsPoolMinion"),
            };
        }
    }

    internal sealed class Season14MechanicCardFact
    {
        internal string CardId;
        internal string NameZhCn;
        internal string NameEnUs;
        internal string Type;
        internal string Set;
        internal string CardClass;
        internal int Cost;
        internal int TechLevel;
        internal int Attack;
        internal int Health;
        internal int Armor;
        internal string SpellSchool;
        internal string Race;
        internal string TextZhCn;
        internal string TextEnUs;
        internal bool IsBattlegroundsPoolMinion;
    }
}
