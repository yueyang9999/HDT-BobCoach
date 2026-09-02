using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace BobCoach.Engine
{
    /// <summary>
    /// Read-only versioned hero playbook. Base facts come from the current
    /// generated hero registries; optional curated overlays add source-backed
    /// phase guidance without duplicating HearthDb card data.
    /// </summary>
    public sealed class HeroPlaybookRegistry
    {
        private const int ExpectedSchemaVersion = 1;
        private const string SkillRegistryFileName = "hero_skill_registry.json";
        private const string HeroRegistryFileName = "hero_registry.json";
        private const string PlaybookRegistryFileName = "hero_playbook_registry.json";

        private readonly Dictionary<string, HeroPlaybookProfile> _byHero =
            new Dictionary<string, HeroPlaybookProfile>(StringComparer.Ordinal);
        private readonly Dictionary<string, HeroPlaybookProfile> _byPower =
            new Dictionary<string, HeroPlaybookProfile>(StringComparer.Ordinal);

        public string ConfiguredRoot { get; private set; }
        public bool IsLoaded { get; private set; }
        public int Count { get { return _byHero.Count; } }

        public IEnumerable<HeroPlaybookProfile> Profiles
        {
            get { return _byHero.Values; }
        }

        public void LoadFromConfiguredRoot()
        {
            Clear();
            var selector = new SeasonDataSelector();
            string configuredRoot = selector.ResolveConfiguredRoot();
            if (string.IsNullOrWhiteSpace(configuredRoot)) return;

            try
            {
                ConfiguredRoot = Path.GetFullPath(configuredRoot);
                string registryDir = Path.Combine(
                    ConfiguredRoot, "generated", "registries");
                string skillPath = Path.Combine(registryDir, SkillRegistryFileName);
                string heroPath = Path.Combine(registryDir, HeroRegistryFileName);
                if (!File.Exists(skillPath) || !File.Exists(heroPath)) return;

                string overlayJson = null;
                string overlayPath = Path.Combine(
                    registryDir, PlaybookRegistryFileName);
                if (File.Exists(overlayPath))
                    overlayJson = File.ReadAllText(overlayPath, Encoding.UTF8);

                LoadFromJson(
                    File.ReadAllText(skillPath, Encoding.UTF8),
                    File.ReadAllText(heroPath, Encoding.UTF8),
                    overlayJson,
                    selector.SelectedSeasonId,
                    selector.GameBuild,
                    selector.CardPoolIdentity);
            }
            catch
            {
                Clear();
            }
        }

        public void LoadFromJson(
            string skillJson,
            string heroJson,
            string playbookJson,
            string season,
            string gameBuild,
            string cardPoolIdentity)
        {
            Clear();
            if (string.IsNullOrWhiteSpace(skillJson)
                || string.IsNullOrWhiteSpace(heroJson)
                || string.IsNullOrWhiteSpace(season)
                || string.IsNullOrWhiteSpace(gameBuild)
                || string.IsNullOrWhiteSpace(cardPoolIdentity))
                return;

            try
            {
                var skillRoot = Season14RegistryJsonParser.ParseRootObject(skillJson);
                var heroRoot = Season14RegistryJsonParser.ParseRootObject(heroJson);
                if (skillRoot == null || heroRoot == null
                    || ContainsBattlegroundsDuo(skillRoot)
                    || ContainsBattlegroundsDuo(heroRoot))
                    return;

                ValidateMeta(skillRoot, season, gameBuild, cardPoolIdentity);
                var skillRows = Season14RegistryJsonParser.AsArray(skillRoot, "cards");
                var heroRows = Season14RegistryJsonParser.AsArray(heroRoot, "cards");
                var skillCounts = Season14RegistryJsonParser.AsObject(skillRoot, "counts");
                if (skillRows == null || skillRows.Count != 116
                    || heroRows == null || heroRows.Count != 116
                    || skillCounts == null
                    || Season14RegistryJsonParser.AsInt(skillCounts, "total") != 116)
                    return;

                var armorById = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var rawHero in heroRows)
                {
                    var heroRow = rawHero as Dictionary<string, object>;
                    if (heroRow == null) return;
                    string heroCardId =
                        Season14RegistryJsonParser.AsString(heroRow, "cardId");
                    if (string.IsNullOrWhiteSpace(heroCardId)) return;
                    armorById[heroCardId] =
                        Season14RegistryJsonParser.AsInt(heroRow, "armor");
                }

                foreach (var rawSkill in skillRows)
                {
                    var row = rawSkill as Dictionary<string, object>;
                    if (row == null) return;
                    var profile = ParseBaseProfile(row, armorById);
                    if (profile == null || profile.HeroPowerCardIds.Count == 0)
                        return;
                    _byHero[profile.HeroCardId] = profile;
                    foreach (string powerId in profile.HeroPowerCardIds)
                    {
                        if (!string.IsNullOrWhiteSpace(powerId))
                            _byPower[powerId] = profile;
                    }
                }

                if (_byHero.Count != 116) return;

                if (!string.IsNullOrWhiteSpace(playbookJson))
                    ApplyOverlay(
                        playbookJson,
                        season,
                        gameBuild,
                        cardPoolIdentity);

                IsLoaded = _byHero.Count == 116;
            }
            catch
            {
                Clear();
            }
        }

        public bool TryGet(
            string heroCardId,
            out HeroPlaybookProfile profile)
        {
            profile = null;
            if (!IsLoaded || string.IsNullOrWhiteSpace(heroCardId)) return false;
            return _byHero.TryGetValue(heroCardId, out profile);
        }

        public HeroPlaybookProfile GetProfileForPower(
            string heroCardId,
            string heroPowerCardId)
        {
            if (!IsLoaded || string.IsNullOrWhiteSpace(heroPowerCardId)) return null;
            HeroPlaybookProfile profile;
            if (_byPower.TryGetValue(heroPowerCardId, out profile))
                return profile;
            if (!string.IsNullOrWhiteSpace(heroCardId)
                && _byHero.TryGetValue(heroCardId, out profile))
                return profile;
            return null;
        }

        private void ApplyOverlay(
            string playbookJson,
            string season,
            string gameBuild,
            string cardPoolIdentity)
        {
            var root = Season14RegistryJsonParser.ParseRootObject(playbookJson);
            if (root == null || ContainsBattlegroundsDuo(root)) return;
            ValidateMeta(root, season, gameBuild, cardPoolIdentity);

            var rows = Season14RegistryJsonParser.AsArray(root, "heroes");
            if (rows == null || rows.Count == 0) return;

            foreach (var rawRow in rows)
            {
                var row = rawRow as Dictionary<string, object>;
                if (row == null) return;

                string heroCardId =
                    Season14RegistryJsonParser.AsString(row, "heroCardId");
                HeroPlaybookProfile profile;
                if (!_byHero.TryGetValue(heroCardId, out profile)) return;

                if (Has(row, "heroNameCn"))
                    profile.HeroNameCn =
                        Season14RegistryJsonParser.AsString(row, "heroNameCn");
                if (Has(row, "heroNameEn"))
                    profile.HeroNameEn =
                        Season14RegistryJsonParser.AsString(row, "heroNameEn");

                var powerIds = Season14RegistryJsonParser.AsStringList(
                    row, "heroPowerCardIds");
                if (powerIds.Count > 0)
                {
                    profile.HeroPowerCardIds = powerIds;
                    foreach (string powerId in powerIds)
                        _byPower[powerId] = profile;
                }

                if (Has(row, "powerType"))
                    profile.PowerType =
                        Season14RegistryJsonParser.AsString(row, "powerType");
                if (Has(row, "powerCost"))
                    profile.PowerCost =
                        Season14RegistryJsonParser.AsInt(row, "powerCost");
                if (Has(row, "armor"))
                    profile.Armor =
                        Season14RegistryJsonParser.AsInt(row, "armor");
                if (Has(row, "unlockTurn"))
                    profile.UnlockTurn =
                        Season14RegistryJsonParser.AsInt(row, "unlockTurn");
                if (Has(row, "unlockTier"))
                    profile.UnlockTier =
                        Season14RegistryJsonParser.AsInt(row, "unlockTier");
                if (Has(row, "usePurpose"))
                    profile.UsePurpose =
                        Season14RegistryJsonParser.AsString(row, "usePurpose");
                if (Has(row, "specialRule"))
                    profile.SpecialRule =
                        Season14RegistryJsonParser.AsString(row, "specialRule");
                if (Has(row, "requiresTarget"))
                    profile.RequiresTarget =
                        Season14RegistryJsonParser.AsBool(row, "requiresTarget");

                string coverage =
                    Season14RegistryJsonParser.AsString(row, "strategyCoverage");
                if (!string.Equals(coverage, "facts-only", StringComparison.Ordinal)
                    && !string.Equals(coverage, "curated", StringComparison.Ordinal))
                    return;
                profile.StrategyCoverage = coverage;

                var sources = Season14RegistryJsonParser.AsStringList(row, "sources");
                if (sources.Count > 0)
                    profile.Sources = sources;

                var phases = ParsePhases(row);
                if (phases != null)
                    profile.Phases = phases;
            }
        }

        private static List<HeroPlaybookPhase> ParsePhases(
            Dictionary<string, object> row)
        {
            if (!Has(row, "phases")) return null;
            var rows = Season14RegistryJsonParser.AsArray(row, "phases");
            if (rows == null) return null;
            var result = new List<HeroPlaybookPhase>();
            foreach (var rawPhase in rows)
            {
                var phaseRow = rawPhase as Dictionary<string, object>;
                if (phaseRow == null) return null;
                string priority =
                    Season14RegistryJsonParser.AsString(phaseRow, "priority");
                if (!IsValidPriority(priority)) return null;
                result.Add(new HeroPlaybookPhase
                {
                    MinTurn =
                        Season14RegistryJsonParser.AsInt(phaseRow, "minTurn"),
                    MaxTurn =
                        Season14RegistryJsonParser.AsInt(phaseRow, "maxTurn"),
                    Priority = priority,
                    GoldAtLeast =
                        Season14RegistryJsonParser.AsInt(phaseRow, "goldAtLeast"),
                    HealthAtLeast =
                        Season14RegistryJsonParser.AsInt(phaseRow, "healthAtLeast"),
                    TargetRequired =
                        Season14RegistryJsonParser.AsBool(phaseRow, "targetRequired"),
                    Reason =
                        Season14RegistryJsonParser.AsString(phaseRow, "reason"),
                });
            }
            return result;
        }

        private static HeroPlaybookProfile ParseBaseProfile(
            Dictionary<string, object> row,
            Dictionary<string, int> armorById)
        {
            string heroCardId =
                Season14RegistryJsonParser.AsString(row, "heroCardId");
            string powerCardId =
                Season14RegistryJsonParser.AsString(row, "heroPowerCardId");
            if (string.IsNullOrWhiteSpace(heroCardId)
                || string.IsNullOrWhiteSpace(powerCardId)
                || HeroPowerRuleCatalog.IsUtilityPowerCardId(powerCardId))
                return null;

            string textCn =
                Season14RegistryJsonParser.AsString(row, "textCn");
            string textEn =
                Season14RegistryJsonParser.AsString(row, "textEn");
            string normalizedCn = Normalize(textCn);
            string normalizedEn = Normalize(textEn);
            string rawPowerType =
                Season14RegistryJsonParser.AsString(row, "powerType");
            int cost =
                Season14RegistryJsonParser.AsInt(row, "cost");
            int unlockTurn =
                Season14RegistryJsonParser.AsInt(row, "unlockTurn");
            int unlockTier =
                Season14RegistryJsonParser.AsInt(row, "unlockTier");
            if (unlockTurn <= 0) unlockTurn = 1;
            if (unlockTier <= 0) unlockTier = 1;

            string powerType = DerivePowerType(
                rawPowerType, cost, normalizedCn, normalizedEn);
            bool hasDiscover = normalizedCn.Contains("发现");
            string specialRule = InferSpecialRule(
                heroCardId, powerCardId, normalizedCn);
            bool requiresTarget = HeroPowerRuleCatalog.RequiresTarget(
                heroCardId, specialRule, hasDiscover);

            int armor;
            armorById.TryGetValue(heroCardId, out armor);
            var profile = new HeroPlaybookProfile
            {
                HeroCardId = heroCardId,
                HeroNameCn =
                    Season14RegistryJsonParser.AsString(row, "nameCn"),
                HeroNameEn =
                    Season14RegistryJsonParser.AsString(row, "nameEn"),
                HeroPowerCardIds = new List<string> { powerCardId },
                PowerType = powerType,
                PowerCost = powerType == "Passive" ? 0 : cost,
                Armor = armor,
                UnlockTurn = unlockTurn,
                UnlockTier = unlockTier,
                UsePurpose = InferUsePurpose(powerType, normalizedCn),
                SpecialRule = specialRule,
                RequiresTarget = requiresTarget,
                StrategyCoverage = "facts-only",
                Sources = new List<string>(),
            };

            if (powerType == "Active")
            {
                profile.Phases.Add(new HeroPlaybookPhase
                {
                    MinTurn = unlockTurn,
                    MaxTurn = 99,
                    Priority = "Normal",
                    GoldAtLeast = profile.PowerCost,
                    HealthAtLeast = 0,
                    TargetRequired = requiresTarget,
                    Reason = "active-power",
                });
            }
            return profile;
        }

        private static string DerivePowerType(
            string rawPowerType,
            int cost,
            string textCn,
            string textEn)
        {
            if (string.Equals(rawPowerType, "Active", StringComparison.Ordinal))
                return "Active";
            if (string.Equals(rawPowerType, "Passive", StringComparison.Ordinal))
                return "Passive";
            if (cost > 0) return "Active";
            return string.IsNullOrEmpty(PassiveTrigger(textCn, textEn))
                ? "Active"
                : "Passive";
        }

        private static string PassiveTrigger(string textCn, string textEn)
        {
            string[] cnTriggers =
            {
                "每回合一次",
                "战斗开始时",
                "在每个回合开始时",
                "对战开始时",
                "每一个回合",
                "当你",
                "在友方",
                "在你的回合开始时",
            };
            foreach (string trigger in cnTriggers)
            {
                if (textCn.StartsWith(trigger, StringComparison.Ordinal))
                    return trigger;
            }
            if (textCn.StartsWith("每局", StringComparison.Ordinal)
                || textCn.StartsWith("在每局", StringComparison.Ordinal))
                return "";
            if ((textCn.StartsWith("每", StringComparison.Ordinal)
                    || textCn.StartsWith("在每", StringComparison.Ordinal))
                && textCn.Contains("时"))
                return "recurring-time-trigger";

            string[] enTriggers =
            {
                "Start of Combat",
                "At the start of each turn",
                "When you",
                "After you",
                "After a friendly",
            };
            foreach (string trigger in enTriggers)
            {
                if (textEn.StartsWith(trigger, StringComparison.Ordinal))
                    return trigger;
            }
            return "";
        }

        private static string InferUsePurpose(
            string powerType,
            string textCn)
        {
            if (powerType == "Passive" || string.IsNullOrWhiteSpace(textCn))
                return "None";
            if (textCn.Contains("铸币") || textCn.Contains("金币")
                || (textCn.Contains("刷新") && textCn.Contains("费"))
                || textCn.Contains("省钱"))
                return "Economy";
            if (textCn.Contains("发现") || textCn.Contains("获取")
                || textCn.Contains("给") || textCn.Contains("锁"))
                return "Resource";
            if (textCn.Contains("buff") || textCn.Contains("+")
                || textCn.Contains("成长") || textCn.Contains("身材"))
                return "Buff";
            if (textCn.Contains("伤害") || textCn.Contains("攻击")
                || textCn.Contains("战斗"))
                return "Combat";
            return "Generic";
        }

        private static string InferSpecialRule(
            string heroCardId,
            string powerCardId,
            string textCn)
        {
            if (string.Equals(powerCardId, "BG32_HERO_001p", StringComparison.Ordinal)
                || textCn.Contains("铸币上限"))
                return "CENARIUS";
            if (textCn.Contains("刷新") && textCn.Contains("法术牌"))
                return "CHROMIE";
            if (textCn.Contains("刷新酒馆") && textCn.Contains("高一级"))
                return "TOCKI";
            if (textCn.Contains("发现一个英雄技能"))
                return "FINLEY";
            if (textCn.Contains("发现两个") || textCn.Contains("替换本技能"))
                return "GENN";
            if (textCn.Contains("上一场战斗") && textCn.Contains("死亡")
                && textCn.Contains("发现"))
                return "SYLVANAS";
            if (textCn.Contains("两个任务") || textCn.Contains("完成后获得"))
                return "DENATHRIUS";
            if ((textCn.Contains("刷新") && textCn.Contains("添加"))
                || textCn.Contains("战列巡航舰"))
                return "LIFTOFF";
            if (textCn.Contains("升级") && textCn.Contains("发现"))
                return "ELISE";
            if (textCn.Contains("酒馆法术") && textCn.Contains("额外获得"))
                return "LAKANISHU";
            if (textCn.Contains("每局对战限一次")
                && textCn.Contains("金色"))
                return "RENO";
            return "";
        }

        private static bool IsValidPriority(string priority)
        {
            return string.Equals(priority, "Critical", StringComparison.Ordinal)
                || string.Equals(priority, "High", StringComparison.Ordinal)
                || string.Equals(priority, "Normal", StringComparison.Ordinal)
                || string.Equals(priority, "Low", StringComparison.Ordinal);
        }

        private static bool Has(
            Dictionary<string, object> row,
            string key)
        {
            return row != null && row.ContainsKey(key);
        }

        private static void ValidateMeta(
            Dictionary<string, object> root,
            string season,
            string gameBuild,
            string cardPoolIdentity)
        {
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
                throw new InvalidOperationException("hero playbook metadata mismatch");
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Replace('\u3000', ' ')
                .Trim();
        }

        private static bool ContainsBattlegroundsDuo(object value)
        {
            if (value is string)
                return ((string)value).IndexOf(
                    "bgduo", StringComparison.OrdinalIgnoreCase) >= 0;
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

        private void Clear()
        {
            _byHero.Clear();
            _byPower.Clear();
            ConfiguredRoot = null;
            IsLoaded = false;
        }
    }
}
