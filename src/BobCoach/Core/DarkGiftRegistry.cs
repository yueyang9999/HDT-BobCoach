using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BobCoach.Engine
{
    /// <summary>Season14 Dark Gift registry loaded from the generated dark_gift_registry.json.</summary>
    internal sealed class DarkGiftRegistry
    {
        internal sealed class DarkGiftEffectFact
        {
            internal string CardId;
            internal string NameZhCn;
            internal string NameEnUs;
            internal string Type;
            internal string TextZhCn;
            internal string TextEnUs;
        }

        internal sealed class DarkGiftOfficialGift
        {
            internal string Name;
            internal string MinimumTurn;
            internal string MaximumTurn;
            internal string Effect;
            internal int SourceRow;
        }

        internal sealed class DarkGiftOfferingRow
        {
            internal string Turn;
            internal string MinionTier;
            internal int SourceRow;
        }

        internal sealed class DarkGiftRuleSection
        {
            internal string Title;
            internal List<string> Items = new List<string>();
        }

        internal sealed class DarkGiftRelatedCard
        {
            internal string CardId;
            internal int DbfId;
            internal string NameEnUs;
            internal string NameZhCn;
            internal string Type;
            internal string Set;
            internal string CardClass;
            internal int Cost;
            internal int TechLevel;
            internal int? Attack;
            internal int? Health;
            internal int? Armor;
            internal string SpellSchool;
            internal string Race;
            internal List<string> Races = new List<string>();
            internal List<string> Mechanics = new List<string>();
            internal List<string> ReferencedTags = new List<string>();
            internal string TextEnUs;
            internal string TextZhCn;
            internal bool IsBattlegroundsPoolSpell;
        }

        private readonly Dictionary<string, DarkGiftEffectFact> _byCardId =
            new Dictionary<string, DarkGiftEffectFact>(StringComparer.Ordinal);
        private readonly List<DarkGiftOfficialGift> _officialGifts =
            new List<DarkGiftOfficialGift>();
        private readonly List<DarkGiftOfferingRow> _offeringRows =
            new List<DarkGiftOfferingRow>();
        private readonly List<DarkGiftRuleSection> _rules =
            new List<DarkGiftRuleSection>();
        private readonly List<DarkGiftRelatedCard> _relatedCards =
            new List<DarkGiftRelatedCard>();
        private bool _loaded;

        internal string ConfiguredRoot { get; private set; }

        internal bool IsLoaded { get { return _loaded; } }

        internal int Count { get { return _byCardId.Count; } }

        internal IReadOnlyList<DarkGiftOfficialGift> OfficialGifts
        {
            get { return _officialGifts; }
        }

        internal IReadOnlyList<DarkGiftOfferingRow> OfferingRows
        {
            get { return _offeringRows; }
        }

        internal IReadOnlyList<DarkGiftRuleSection> Rules
        {
            get { return _rules; }
        }

        internal IReadOnlyList<DarkGiftRelatedCard> RelatedCards
        {
            get { return _relatedCards; }
        }

        internal void LoadFromConfiguredRoot()
        {
            ConfiguredRoot = null;
            _byCardId.Clear();
            _officialGifts.Clear();
            _offeringRows.Clear();
            _rules.Clear();
            _relatedCards.Clear();
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
                    root, "generated", "registries", "dark_gift_registry.json");
                if (!File.Exists(file)) return;
                LoadFromJson(File.ReadAllText(file, Encoding.UTF8));
            }
            catch
            {
                Clear();
                _loaded = false;
            }
        }

        internal void LoadFromJson(string json)
        {
            Clear();
            _loaded = false;
            if (string.IsNullOrWhiteSpace(json)) return;

            try
            {
                var root = Season14RegistryJsonParser.ParseRootObject(json);
                if (root == null) return;
                var official = Season14RegistryJsonParser.AsObject(root, "official");
                if (official == null) return;
                var offeringTable = Season14RegistryJsonParser.AsObject(official, "offeringTable");
                var gifts = Season14RegistryJsonParser.AsArray(official, "gifts");
                var offeringRows = offeringTable == null
                    ? null : Season14RegistryJsonParser.AsArray(offeringTable, "rows");
                var rules = Season14RegistryJsonParser.AsArray(official, "rules");
                var rulesByKey = Season14RegistryJsonParser.AsObject(official, "rules");
                var rawEffects = Season14RegistryJsonParser.AsArray(root, "rawEffects");
                var relatedCards = Season14RegistryJsonParser.AsArray(root, "relatedCards");
                if (gifts == null || offeringRows == null
                    || (rules == null && rulesByKey == null)
                    || rawEffects == null || relatedCards == null
                    || ContainsBattlegroundsDuoCard(rawEffects)
                    || ContainsBattlegroundsDuoCard(relatedCards))
                    return;

                foreach (var item in rawEffects)
                {
                    var row = item as Dictionary<string, object>;
                    if (row == null) continue;
                    string cardId = Season14RegistryJsonParser.AsString(row, "cardId");
                    if (string.IsNullOrEmpty(cardId) || _byCardId.ContainsKey(cardId)) continue;
                    _byCardId[cardId] = ParseEffectFact(cardId, row);
                }

                foreach (var item in gifts)
                {
                    var row = item as Dictionary<string, object>;
                    if (row == null) continue;
                    _officialGifts.Add(ParseOfficialGift(row));
                }

                foreach (var item in offeringRows)
                {
                    var row = item as Dictionary<string, object>;
                    if (row == null) continue;
                    _offeringRows.Add(ParseOfferingRow(row));
                }

                if (rules != null)
                {
                    foreach (var item in rules)
                    {
                        var row = item as Dictionary<string, object>;
                        if (row == null) continue;
                        _rules.Add(ParseRuleSection(row));
                    }
                }
                else
                {
                    var ruleKeys = new List<string>(rulesByKey.Keys);
                    ruleKeys.Sort(StringComparer.Ordinal);
                    foreach (string key in ruleKeys)
                    {
                        var row = rulesByKey[key] as Dictionary<string, object>;
                        if (row == null) continue;
                        _rules.Add(ParseRuleSection(row));
                    }
                }

                foreach (var item in relatedCards)
                {
                    var row = item as Dictionary<string, object>;
                    if (row == null) continue;
                    _relatedCards.Add(ParseRelatedCard(row));
                }

                _loaded = _byCardId.Count > 0
                    && _officialGifts.Count > 0
                    && _offeringRows.Count > 0
                    && _rules.Count > 0
                    && _relatedCards.Count > 0;
            }
            catch
            {
                Clear();
                _loaded = false;
            }
        }

        internal bool TryGet(string cardId, out DarkGiftEffectFact fact)
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
                DarkGiftEffectFact ignored;
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
                    "[{0:O}] [DarkGiftRegistry] {1}\n", DateTime.UtcNow, message);
                File.AppendAllText(
                    Path.Combine(ConfiguredRoot, "bob_coach.log"), line, Encoding.UTF8);
            }
            catch { }
        }

        private static bool ContainsBattlegroundsDuoCard(List<object> rows)
        {
            foreach (var item in rows)
            {
                var row = item as Dictionary<string, object>;
                if (row == null) continue;
                string cardId = Season14RegistryJsonParser.AsString(row, "cardId");
                if (Season14RegistryJsonParser.IsBattlegroundsDuoCardId(cardId)) return true;
            }
            return false;
        }

        private static DarkGiftEffectFact ParseEffectFact(
            string cardId, Dictionary<string, object> row)
        {
            return new DarkGiftEffectFact
            {
                CardId = cardId,
                NameZhCn = Season14RegistryJsonParser.AsString(row, "name_cn"),
                NameEnUs = Season14RegistryJsonParser.AsString(row, "name_en"),
                Type = Season14RegistryJsonParser.AsString(row, "type"),
                TextZhCn = Season14RegistryJsonParser.AsString(row, "text_cn"),
                TextEnUs = Season14RegistryJsonParser.AsString(row, "text_en"),
            };
        }

        private static DarkGiftOfficialGift ParseOfficialGift(Dictionary<string, object> row)
        {
            return new DarkGiftOfficialGift
            {
                Name = Season14RegistryJsonParser.AsString(row, "name"),
                MinimumTurn = Season14RegistryJsonParser.AsString(row, "minimumTurn"),
                MaximumTurn = Season14RegistryJsonParser.AsString(row, "maximumTurn"),
                Effect = Season14RegistryJsonParser.AsString(row, "effect"),
                SourceRow = Season14RegistryJsonParser.AsInt(row, "sourceRow"),
            };
        }

        private static DarkGiftOfferingRow ParseOfferingRow(Dictionary<string, object> row)
        {
            return new DarkGiftOfferingRow
            {
                Turn = Season14RegistryJsonParser.AsString(row, "turn"),
                MinionTier = Season14RegistryJsonParser.AsString(row, "minionTier"),
                SourceRow = Season14RegistryJsonParser.AsInt(row, "sourceRow"),
            };
        }

        private static DarkGiftRuleSection ParseRuleSection(Dictionary<string, object> row)
        {
            return new DarkGiftRuleSection
            {
                Title = Season14RegistryJsonParser.AsString(row, "title"),
                Items = Season14RegistryJsonParser.AsStringList(row, "items"),
            };
        }

        private static DarkGiftRelatedCard ParseRelatedCard(Dictionary<string, object> row)
        {
            return new DarkGiftRelatedCard
            {
                CardId = Season14RegistryJsonParser.AsString(row, "cardId"),
                DbfId = Season14RegistryJsonParser.AsInt(row, "dbfId"),
                NameEnUs = Season14RegistryJsonParser.AsString(row, "name_en"),
                NameZhCn = Season14RegistryJsonParser.AsString(row, "name_cn"),
                Type = Season14RegistryJsonParser.AsString(row, "type"),
                Set = Season14RegistryJsonParser.AsString(row, "set"),
                CardClass = Season14RegistryJsonParser.AsString(row, "cardClass"),
                Cost = Season14RegistryJsonParser.AsInt(row, "cost"),
                TechLevel = Season14RegistryJsonParser.AsInt(row, "techLevel"),
                Attack = Season14RegistryJsonParser.AsNullableInt(row, "attack"),
                Health = Season14RegistryJsonParser.AsNullableInt(row, "health"),
                Armor = Season14RegistryJsonParser.AsNullableInt(row, "armor"),
                SpellSchool = Season14RegistryJsonParser.AsString(row, "spellSchool"),
                Race = Season14RegistryJsonParser.AsString(row, "race"),
                Races = Season14RegistryJsonParser.AsStringList(row, "races"),
                Mechanics = Season14RegistryJsonParser.AsStringList(row, "mechanics"),
                ReferencedTags = Season14RegistryJsonParser.AsStringList(row, "referencedTags"),
                TextEnUs = Season14RegistryJsonParser.AsString(row, "text_en"),
                TextZhCn = Season14RegistryJsonParser.AsString(row, "text_cn"),
                IsBattlegroundsPoolSpell =
                    Season14RegistryJsonParser.AsBool(row, "isBattlegroundsPoolSpell"),
            };
        }

        private void Clear()
        {
            _byCardId.Clear();
            _officialGifts.Clear();
            _offeringRows.Clear();
            _rules.Clear();
            _relatedCards.Clear();
        }
    }
}
