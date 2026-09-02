using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BobCoach.Engine
{
    /// <summary>
    /// Read-only baseline for the current environment. It can override guide
    /// tiers and core card IDs without regenerating the whole guide registry.
    /// The baseline is optional: when absent, CompGuideCatalog keeps using the
    /// generated guide weights.
    /// </summary>
    internal sealed class SeasonMetaBaseline
    {
        private readonly Dictionary<string, string> _tierByGuideId =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> _coreIdsByGuideId =
            new Dictionary<string, List<string>>(StringComparer.Ordinal);

        internal bool IsLoaded { get; private set; }
        internal string Source { get; private set; }
        internal string CapturedAt { get; private set; }
        internal string GameBuild { get; private set; }

        internal int GuideOverridesCount
        {
            get { return _tierByGuideId.Count + _coreIdsByGuideId.Count; }
        }

        internal void LoadFromFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                Clear();
                return;
            }
            LoadFromJson(File.ReadAllText(path, Encoding.UTF8));
        }

        internal void LoadFromJson(string json)
        {
            Clear();
            if (string.IsNullOrWhiteSpace(json)) return;

            var root = Season14RegistryJsonParser.ParseRootObject(json);
            if (root == null) return;

            var meta = Season14RegistryJsonParser.AsObject(root, "_meta");
            if (meta != null)
            {
                Source = Season14RegistryJsonParser.AsString(meta, "source");
                CapturedAt = Season14RegistryJsonParser.AsString(meta, "capturedAt");
                GameBuild = Season14RegistryJsonParser.AsString(meta, "gameBuild");
            }

            var guides = Season14RegistryJsonParser.AsObject(root, "guides");
            if (guides == null || guides.Count == 0) return;

            foreach (var pair in guides)
            {
                var row = pair.Value as Dictionary<string, object>;
                if (row == null) continue;

                string tier = Season14RegistryJsonParser.AsString(row, "tier");
                if (!string.IsNullOrWhiteSpace(tier))
                    _tierByGuideId[pair.Key] = tier;

                var coreIds = Season14RegistryJsonParser.AsStringList(row, "coreCardIds");
                if (coreIds != null && coreIds.Count > 0)
                    _coreIdsByGuideId[pair.Key] = new List<string>(coreIds);
            }

            IsLoaded = _tierByGuideId.Count > 0 || _coreIdsByGuideId.Count > 0;
        }

        internal bool TryGetTier(string guideId, out string tier)
        {
            tier = null;
            if (!IsLoaded || string.IsNullOrEmpty(guideId)) return false;
            return _tierByGuideId.TryGetValue(guideId, out tier);
        }

        internal bool TryGetCoreCardIds(string guideId, out List<string> coreIds)
        {
            coreIds = null;
            if (!IsLoaded || string.IsNullOrEmpty(guideId)) return false;
            List<string> stored;
            if (!_coreIdsByGuideId.TryGetValue(guideId, out stored)
                || stored == null || stored.Count == 0)
                return false;
            coreIds = new List<string>(stored);
            return true;
        }

        internal void Clear()
        {
            _tierByGuideId.Clear();
            _coreIdsByGuideId.Clear();
            IsLoaded = false;
            Source = null;
            CapturedAt = null;
            GameBuild = null;
        }
    }
}
