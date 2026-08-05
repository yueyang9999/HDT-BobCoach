using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BobCoach.Engine
{
    /// <summary>
    /// Read-only resolver for the season data selector manifest. It never
    /// falls back to APPDATA and never writes selector state.
    /// </summary>
    internal sealed class SeasonDataSelector
    {
        private const string DefaultSelectorPath =
            @"E:/HDT-BobCoach-evidence/season_selector.json";
        private const int ExpectedSchemaVersion = 1;

        internal string SelectorPath { get; private set; }
        internal string DefaultSeason { get; private set; }
        internal string SelectedSeasonId { get; private set; }
        internal string GameBuild { get; private set; }
        internal string CardPoolIdentity { get; private set; }

        internal string ResolveConfiguredRoot()
        {
            Clear();
            try
            {
                string selectorPath = ResolveSelectorPath();
                if (string.IsNullOrEmpty(selectorPath) || !File.Exists(selectorPath))
                    return null;

                string json = File.ReadAllText(selectorPath, Encoding.UTF8);
                var document = Season14RegistryJsonParser.ParseRootObject(json);
                if (document == null || ContainsBattlegroundsDuo(document))
                    return null;

                var meta = Season14RegistryJsonParser.AsObject(document, "_meta");
                if (meta == null
                    || Season14RegistryJsonParser.AsInt(meta, "schemaVersion")
                        != ExpectedSchemaVersion)
                    return null;

                string defaultSeason =
                    Season14RegistryJsonParser.AsString(document, "defaultSeason");
                if (string.IsNullOrWhiteSpace(defaultSeason))
                    return null;

                var seasons = Season14RegistryJsonParser.AsObject(document, "seasons");
                object rawSeason;
                if (seasons == null || !seasons.TryGetValue(defaultSeason, out rawSeason))
                    return null;

                var season = rawSeason as Dictionary<string, object>;
                if (season == null) return null;

                string rootText = Season14RegistryJsonParser.AsString(season, "root");
                string gameBuild = Season14RegistryJsonParser.AsString(season, "gameBuild");
                string cardPoolIdentity =
                    Season14RegistryJsonParser.AsString(season, "cardPoolIdentity");
                var files = Season14RegistryJsonParser.AsArray(season, "files");
                if (string.IsNullOrWhiteSpace(rootText)
                    || string.IsNullOrWhiteSpace(gameBuild)
                    || string.IsNullOrWhiteSpace(cardPoolIdentity)
                    || files == null)
                    return null;

                string root = Path.GetFullPath(rootText);
                if (!Directory.Exists(root)) return null;
                foreach (var rawFile in files)
                {
                    var relative = rawFile as string;
                    if (string.IsNullOrWhiteSpace(relative)) return null;

                    string filePath = Path.Combine(root, relative);
                    if (!File.Exists(filePath)) return null;
                    if (!relative.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string fileJson = File.ReadAllText(filePath, Encoding.UTF8);
                    var fileRoot = Season14RegistryJsonParser.ParseRootObject(fileJson);
                    if (fileRoot == null || ContainsBattlegroundsDuo(fileRoot))
                        return null;
                }

                SelectorPath = Path.GetFullPath(selectorPath);
                DefaultSeason = defaultSeason;
                SelectedSeasonId = defaultSeason;
                GameBuild = gameBuild;
                CardPoolIdentity = cardPoolIdentity;
                return root;
            }
            catch
            {
                Clear();
                return null;
            }
        }

        private static string ResolveSelectorPath()
        {
            string explicitSelector = Environment.GetEnvironmentVariable(
                BobCoachDataPaths.SeasonSelectorEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(explicitSelector))
                return Path.GetFullPath(explicitSelector.Trim());

            string configuredRoot = Environment.GetEnvironmentVariable(
                BobCoachDataPaths.RootEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(configuredRoot))
                return DefaultSelectorPath;

            if (!Path.IsPathRooted(configuredRoot))
                return null;

            string root = Path.GetFullPath(configuredRoot);
            string candidate = Path.Combine(
                Path.GetDirectoryName(root), "season_selector.json");
            return File.Exists(candidate) ? candidate : DefaultSelectorPath;
        }

        private static bool ContainsBattlegroundsDuo(object value)
        {
            if (value is string)
                return ((string)value).IndexOf("bgduo", StringComparison.OrdinalIgnoreCase) >= 0;

            if (value is Dictionary<string, object>)
            {
                foreach (var pair in (Dictionary<string, object>)value)
                {
                    // Validation reports expose an audit flag named noBgDuo;
                    // the flag is metadata, not a card pool violation.
                    if ((!string.Equals(pair.Key, "noBgDuo", StringComparison.OrdinalIgnoreCase)
                            && ContainsBattlegroundsDuo(pair.Key))
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

        private void Clear()
        {
            SelectorPath = null;
            DefaultSeason = null;
            SelectedSeasonId = null;
            GameBuild = null;
            CardPoolIdentity = null;
        }
    }
}
