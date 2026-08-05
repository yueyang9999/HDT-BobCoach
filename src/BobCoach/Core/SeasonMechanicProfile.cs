using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BobCoach.Engine
{
    /// <summary>
    /// Read-only Season14 mechanic profile loaded from the configured season
    /// evidence root. Missing, invalid, inconsistent, or unknown profile data
    /// fails closed.
    /// </summary>
    internal sealed class SeasonMechanicProfile
    {
        private const int ExpectedSchemaVersion = 1;
        private const string ProfileFileName = "season14_mechanic_profile.json";

        private static readonly string[] RequiredMechanisms =
        {
            "activate",
            "lockbox",
            "fishbait",
            "darkGift",
            "trinket",
            "anomaly",
        };

        private Dictionary<string, bool> _mechanisms;

        internal string ConfiguredRoot { get; private set; }

        internal bool IsLoaded
        {
            get { return _mechanisms != null; }
        }

        internal void LoadFromConfiguredRoot()
        {
            _mechanisms = null;
            ConfiguredRoot = null;

            var selector = new SeasonDataSelector();
            string configuredRoot = selector.ResolveConfiguredRoot();
            if (string.IsNullOrWhiteSpace(configuredRoot)) return;

            ConfiguredRoot = Path.GetFullPath(configuredRoot);
            try
            {
                string profileFile = Path.Combine(
                    ConfiguredRoot,
                    "generated",
                    "registries",
                    ProfileFileName);
                if (!File.Exists(profileFile)) return;

                string json = File.ReadAllText(profileFile, Encoding.UTF8);
                LoadFromJson(
                    json,
                    selector.SelectedSeasonId,
                    selector.GameBuild,
                    selector.CardPoolIdentity);
            }
            catch
            {
                _mechanisms = null;
            }
        }

        internal void LoadFromJson(
            string json,
            string season,
            string gameBuild,
            string cardPoolIdentity)
        {
            _mechanisms = null;
            if (string.IsNullOrWhiteSpace(json)
                || string.IsNullOrWhiteSpace(season)
                || string.IsNullOrWhiteSpace(gameBuild)
                || string.IsNullOrWhiteSpace(cardPoolIdentity))
                return;

            var root = Season14RegistryJsonParser.ParseRootObject(json);
            if (root == null || ContainsBattlegroundsDuo(root)) return;

            var meta = Season14RegistryJsonParser.AsObject(root, "_meta");
            if (meta == null
                || Season14RegistryJsonParser.AsInt(meta, "schemaVersion")
                    != ExpectedSchemaVersion
                || !string.Equals(
                    Season14RegistryJsonParser.AsString(meta, "season"),
                    season,
                    StringComparison.Ordinal)
                || !string.Equals(
                    Season14RegistryJsonParser.AsString(meta, "gameBuild"),
                    gameBuild,
                    StringComparison.Ordinal)
                || !string.Equals(
                    Season14RegistryJsonParser.AsString(meta, "cardPoolIdentity"),
                    cardPoolIdentity,
                    StringComparison.Ordinal))
                return;

            var mechanisms = Season14RegistryJsonParser.AsObject(root, "mechanisms");
            if (mechanisms == null
                || mechanisms.Count != RequiredMechanisms.Length
                || ContainsBattlegroundsDuo(mechanisms))
                return;

            var parsed = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (string mechanism in RequiredMechanisms)
            {
                object rawStatus;
                if (!mechanisms.TryGetValue(mechanism, out rawStatus))
                    return;

                string status = rawStatus as string;
                if (string.Equals(status, "active", StringComparison.Ordinal))
                    parsed[mechanism] = true;
                else if (string.Equals(status, "inactive", StringComparison.Ordinal))
                    parsed[mechanism] = false;
                else
                    return;
            }

            _mechanisms = parsed;
        }

        internal bool IsMechanicEnabled(string mechanic)
        {
            if (_mechanisms == null || string.IsNullOrWhiteSpace(mechanic)) return false;
            bool enabled;
            return _mechanisms.TryGetValue(mechanic, out enabled) && enabled;
        }

        private static bool ContainsBattlegroundsDuo(object value)
        {
            if (value is string)
                return ((string)value).IndexOf("bgduo", StringComparison.OrdinalIgnoreCase) >= 0;

            if (value is Dictionary<string, object>)
            {
                foreach (var pair in (Dictionary<string, object>)value)
                {
                    // noBgDuo is an audit flag, not a card pool violation.
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
    }
}
