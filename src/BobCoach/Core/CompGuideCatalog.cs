using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BobCoach.Engine
{
    /// <summary>
    /// Read-only catalog for generated season14 comp guides and their
    /// algorithm weight reference.
    /// </summary>
    internal sealed class CompGuideCatalog
    {
        private readonly List<CompGuide> _guides = new List<CompGuide>();
        private readonly Dictionary<string, CompGuide> _guidesById =
            new Dictionary<string, CompGuide>(StringComparer.Ordinal);
        private readonly Dictionary<string, CompGuideCardWeight> _coreWeights =
            new Dictionary<string, CompGuideCardWeight>(StringComparer.Ordinal);
        private readonly Dictionary<string, CompGuideCardWeight> _trinketWeights =
            new Dictionary<string, CompGuideCardWeight>(StringComparer.Ordinal);
        private bool _loaded;
        private int _coreCardEntries;
        private int _trinketEntries;

        internal string ConfiguredRoot { get; private set; }

        internal bool IsLoaded { get { return _loaded; } }

        internal int GuidesCount { get { return _guides.Count; } }

        internal int CoreCardsCount { get { return _coreCardEntries; } }

        internal int TrinketsCount { get { return _trinketEntries; } }

        internal List<CompGuide> Guides { get { return _guides; } }

        internal void LoadFromConfiguredRoot()
        {
            ClearData();
            ConfiguredRoot = null;

            string configuredRoot = new SeasonDataSelector().ResolveConfiguredRoot();
            if (string.IsNullOrWhiteSpace(configuredRoot)) return;

            try
            {
                ConfiguredRoot = Path.GetFullPath(configuredRoot);

                string guidesFile = Path.Combine(
                    ConfiguredRoot, "generated", "registries", "season14_comp_guides.json");
                string weightsFile = Path.Combine(
                    ConfiguredRoot, "generated", "season14_guide_weight_reference.json");
                if (!File.Exists(guidesFile) || !File.Exists(weightsFile)) return;

                LoadFromJson(
                    File.ReadAllText(guidesFile, Encoding.UTF8),
                    File.ReadAllText(weightsFile, Encoding.UTF8));
            }
            catch
            {
                ClearData();
            }
        }

        internal void LoadFromJson(string guidesJson, string weightsJson)
        {
            ClearData();
            if (string.IsNullOrWhiteSpace(guidesJson) || string.IsNullOrWhiteSpace(weightsJson))
                return;

            try
            {
                var guidesRoot = Season14RegistryJsonParser.ParseRootObject(guidesJson);
                var weightsRoot = Season14RegistryJsonParser.ParseRootObject(weightsJson);
                if (guidesRoot == null || weightsRoot == null) return;
                if (ContainsBattlegroundsDuo(guidesRoot)
                    || ContainsBattlegroundsDuo(weightsRoot))
                    return;

                var guideRows = Season14RegistryJsonParser.AsArray(guidesRoot, "guides");
                var counts = Season14RegistryJsonParser.AsObject(weightsRoot, "counts");
                var coreRows = Season14RegistryJsonParser.AsObject(weightsRoot, "coreCards");
                var trinketRows = Season14RegistryJsonParser.AsObject(weightsRoot, "trinkets");
                if (guideRows == null || guideRows.Count == 0
                    || counts == null || counts.Count == 0
                    || coreRows == null || coreRows.Count == 0
                    || trinketRows == null || trinketRows.Count == 0)
                    return;

                var parsedGuides = new List<CompGuide>();
                var parsedGuidesById =
                    new Dictionary<string, CompGuide>(StringComparer.Ordinal);
                foreach (var item in guideRows)
                {
                    var row = item as Dictionary<string, object>;
                    if (row == null) return;
                    var guide = ParseGuide(row);
                    if (string.IsNullOrEmpty(guide.Id)
                        || string.IsNullOrEmpty(guide.Name)
                        || parsedGuidesById.ContainsKey(guide.Id))
                        return;
                    parsedGuides.Add(guide);
                    parsedGuidesById[guide.Id] = guide;
                }

                var parsedCore = ParseWeights(coreRows);
                var parsedTrinkets = ParseWeights(trinketRows);
                if (parsedCore == null || parsedTrinkets == null
                    || parsedCore.Count == 0 || parsedTrinkets.Count == 0)
                    return;

                _coreCardEntries =
                    Season14RegistryJsonParser.AsInt(counts, "coreEntries");
                _trinketEntries =
                    Season14RegistryJsonParser.AsInt(counts, "trinketEntries");
                if (_coreCardEntries <= 0 || _trinketEntries <= 0) return;

                _guides.AddRange(parsedGuides);
                foreach (var pair in parsedGuidesById)
                    _guidesById[pair.Key] = pair.Value;
                foreach (var pair in parsedCore)
                    _coreWeights[pair.Key] = pair.Value;
                foreach (var pair in parsedTrinkets)
                    _trinketWeights[pair.Key] = pair.Value;
                _loaded = true;
            }
            catch
            {
                ClearData();
            }
        }

        internal bool TryGetGuide(string id, out CompGuide guide)
        {
            guide = null;
            if (!_loaded || string.IsNullOrEmpty(id)) return false;
            return _guidesById.TryGetValue(id, out guide);
        }

        internal bool TryGetWeight(
            CompGuideWeightKind kind, string name, out CompGuideCardWeight weight)
        {
            weight = null;
            if (!_loaded || string.IsNullOrEmpty(name)) return false;
            if (kind == CompGuideWeightKind.Core)
                return _coreWeights.TryGetValue(name, out weight);
            if (kind == CompGuideWeightKind.Trinket)
                return _trinketWeights.TryGetValue(name, out weight);
            return false;
        }

        internal bool TryGetTierWeight(string tier, out int weight)
        {
            weight = 0;
            if (!_loaded || string.IsNullOrEmpty(tier)) return false;
            if (string.Equals(tier, "T0", StringComparison.OrdinalIgnoreCase))
            {
                weight = 3;
                return true;
            }
            if (string.Equals(tier, "T1", StringComparison.OrdinalIgnoreCase))
            {
                weight = 2;
                return true;
            }
            if (string.Equals(tier, "T2", StringComparison.OrdinalIgnoreCase))
            {
                weight = 1;
                return true;
            }
            if (string.Equals(tier, "T3", StringComparison.OrdinalIgnoreCase))
            {
                weight = 0;
                return true;
            }
            return false;
        }

        private static CompGuide ParseGuide(Dictionary<string, object> row)
        {
            return new CompGuide
            {
                Id = Season14RegistryJsonParser.AsString(row, "id"),
                Name = Season14RegistryJsonParser.AsString(row, "name"),
                Tier = Season14RegistryJsonParser.AsString(row, "tier"),
                Tribes = Season14RegistryJsonParser.AsStringList(row, "tribes"),
                CoreCards = Season14RegistryJsonParser.AsStringList(row, "coreCards"),
                Trinkets = Season14RegistryJsonParser.AsStringList(row, "trinkets"),
                Strategy = Season14RegistryJsonParser.AsString(row, "strategy"),
            };
        }

        private static Dictionary<string, CompGuideCardWeight> ParseWeights(
            Dictionary<string, object> rows)
        {
            var result = new Dictionary<string, CompGuideCardWeight>(StringComparer.Ordinal);
            foreach (var pair in rows)
            {
                var row = pair.Value as Dictionary<string, object>;
                if (row == null) return null;
                var weight = new CompGuideCardWeight
                {
                    Name = Season14RegistryJsonParser.AsString(row, "name"),
                    OfficialName = Season14RegistryJsonParser.AsString(row, "officialName"),
                    OfficialCardId = Season14RegistryJsonParser.AsString(row, "officialCardId"),
                    Occurrences = Season14RegistryJsonParser.AsInt(row, "occurrences"),
                    UniqueGuides = Season14RegistryJsonParser.AsInt(row, "uniqueGuides"),
                    MultiCopyGuides =
                        Season14RegistryJsonParser.AsStringList(row, "multiCopyGuides"),
                    DuplicateBonus = Season14RegistryJsonParser.AsInt(row, "duplicateBonus"),
                    RawWeight = Season14RegistryJsonParser.AsNullableInt(row, "rawWeight"),
                    HeuristicWeight =
                        Season14RegistryJsonParser.AsNullableInt(row, "heuristicWeight"),
                    TierCounts = ParseTierCounts(row),
                };
                if (string.IsNullOrEmpty(weight.Name)
                    || result.ContainsKey(weight.Name))
                    return null;
                result[weight.Name] = weight;
            }
            return result;
        }

        private static Dictionary<string, int> ParseTierCounts(
            Dictionary<string, object> row)
        {
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            var tierCounts = Season14RegistryJsonParser.AsObject(row, "tierCounts");
            if (tierCounts == null) return result;
            foreach (var pair in tierCounts)
                result[pair.Key] = Season14RegistryJsonParser.AsInt(tierCounts, pair.Key);
            return result;
        }

        private static bool ContainsBattlegroundsDuo(object value)
        {
            if (value is string)
                return ((string)value).IndexOf("bgduo", StringComparison.OrdinalIgnoreCase) >= 0;
            if (value is Dictionary<string, object>)
            {
                foreach (var pair in (Dictionary<string, object>)value)
                {
                    if (ContainsBattlegroundsDuo(pair.Key)
                        || ContainsBattlegroundsDuo(pair.Value))
                        return true;
                }
                return false;
            }
            if (value is List<object>)
            {
                foreach (var item in (List<object>)value)
                {
                    if (ContainsBattlegroundsDuo(item)) return true;
                }
            }
            return false;
        }

        private void ClearData()
        {
            _guides.Clear();
            _guidesById.Clear();
            _coreWeights.Clear();
            _trinketWeights.Clear();
            _coreCardEntries = 0;
            _trinketEntries = 0;
            _loaded = false;
        }
    }

    internal sealed class CompGuide
    {
        internal string Id;
        internal string Name;
        internal string Tier;
        internal List<string> Tribes;
        internal List<string> CoreCards;
        internal List<string> Trinkets;
        internal string Strategy;
    }

    internal sealed class CompGuideCardWeight
    {
        internal string Name;
        internal string OfficialName;
        internal string OfficialCardId;
        internal int Occurrences;
        internal int UniqueGuides;
        internal List<string> MultiCopyGuides;
        internal int DuplicateBonus;
        internal int? RawWeight;
        internal int? HeuristicWeight;
        internal Dictionary<string, int> TierCounts;
    }

    internal enum CompGuideWeightKind
    {
        Core,
        Trinket,
    }
}
