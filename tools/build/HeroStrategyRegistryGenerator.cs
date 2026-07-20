using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using BobCoach.LegacyHeroStrategy;

public sealed class HeroStrategySourceCard
{
    public string str_id { get; set; }
    public string card_type { get; set; }
    public List<string> hp_ids { get; set; }
}

public sealed class HeroStrategyRegistryDocument
{
    public int build { get; set; }
    public List<HeroStrategyRegistryRow> heroes { get; set; }
}

public sealed class HeroStrategyRegistryRow
{
    public string heroCardId { get; set; }
    public string primaryPowerCardId { get; set; }
    public List<string> powerAliases { get; set; }
    public string powerType { get; set; }
    public string archetype { get; set; }
    public int powerCost { get; set; }
    public int unlockTurn { get; set; }
    public int unlockTier { get; set; }
    public bool hasDiscover { get; set; }
    public string usePurpose { get; set; }
    public List<string> synergyTags { get; set; }
    public float levelAggression { get; set; }
    public float upgradeValueBias { get; set; }
    public float refreshValueBias { get; set; }
    public float buyValueBias { get; set; }
    public float powerValueBias { get; set; }
    public SortedDictionary<string, float> tribeAffinity { get; set; }
    public string specialRule { get; set; }
}

public sealed class HeroStrategyRegistryManifest
{
    public int schemaVersion { get; set; }
    public string activation { get; set; }
    public int build { get; set; }
    public string generator { get; set; }
    public string selectionContract { get; set; }
    public int rowCount { get; set; }
    public int queryKeyCount { get; set; }
    public string cardsInputSha256 { get; set; }
    public string legacyRegistryInputSha256 { get; set; }
    public string outputSha256 { get; set; }
}

public static class HeroStrategyRegistryGenerator
{
    private static string Sha256(byte[] content)
    {
        using (var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(content)).Replace("-", "");
    }

    private static string GetUsePurpose(HeroStrategy strategy)
    {
        return strategy == null
            ? "none"
            : strategy.UsePurpose.ToString().ToLowerInvariant();
    }

    private static List<string> GetSynergyTags(HeroStrategy strategy)
    {
        return strategy == null || strategy.SynergyTags == null
            ? new List<string>()
            : strategy.SynergyTags.Distinct(StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.Ordinal).ToList();
    }

    private static HeroStrategyRegistryRow ToRow(
        HeroStrategySourceCard source,
        HeroStrategy strategy)
    {
        var primaryPowerId = source.hp_ids[0];
        var affinity = new SortedDictionary<string, float>(StringComparer.Ordinal);
        if (strategy.TribeAffinity != null)
        {
            foreach (var pair in strategy.TribeAffinity.OrderBy(x => x.Key, StringComparer.Ordinal))
                affinity[pair.Key] = pair.Value;
        }

        return new HeroStrategyRegistryRow
        {
            heroCardId = source.str_id,
            primaryPowerCardId = primaryPowerId,
            powerAliases = new List<string> { primaryPowerId },
            powerType = strategy.PowerType.ToString(),
            archetype = strategy.Archetype.ToString(),
            powerCost = strategy.PowerCost,
            unlockTurn = strategy.UnlockTurn,
            unlockTier = strategy.UnlockTier,
            hasDiscover = strategy.HasDiscover,
            usePurpose = GetUsePurpose(strategy),
            synergyTags = GetSynergyTags(strategy),
            levelAggression = strategy.LevelAggression,
            upgradeValueBias = strategy.UpgradeValueBias,
            refreshValueBias = strategy.RefreshValueBias,
            buyValueBias = strategy.BuyValueBias,
            powerValueBias = strategy.PowerValueBias,
            tribeAffinity = affinity,
            specialRule = strategy.SpecialRule ?? "",
        };
    }

    public static int Main(string[] args)
    {
        if (args.Length != 4)
        {
            Console.Error.WriteLine(
                "usage: HeroStrategyRegistryGenerator <cards.json> <legacy_registry.json> <registry.json> <manifest.json>");
            return 2;
        }

        var cardsBytes = File.ReadAllBytes(args[0]);
        var legacyBytes = File.ReadAllBytes(args[1]);
        var cardsJson = Encoding.UTF8.GetString(cardsBytes);
        var legacyJson = Encoding.UTF8.GetString(legacyBytes);
        var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        var cards = serializer.Deserialize<List<HeroStrategySourceCard>>(cardsJson)
            ?? new List<HeroStrategySourceCard>();

        var heroes = cards
            .Where(card => card != null && card.card_type == "hero"
                && !string.IsNullOrEmpty(card.str_id)
                && card.hp_ids != null && card.hp_ids.Count > 0
                && !string.IsNullOrEmpty(card.hp_ids[0]))
            .OrderBy(card => card.str_id, StringComparer.Ordinal)
            .ToList();

        var engine = new LegacyHeroStrategySnapshot();
        engine.LoadFromCardsDb(cardsJson);
        engine.LoadPowerRegistry(legacyJson);
        if (!engine.IsLoadedFromResource)
            throw new InvalidDataException("legacy hero strategy source failed to load");

        var rows = heroes.Select(source => ToRow(source, engine.GetStrategy(source.str_id))).ToList();
        if (rows.Count != 119)
            throw new InvalidDataException("expected 119 hero strategies, got " + rows.Count);
        if (rows.Select(row => row.heroCardId).Distinct(StringComparer.Ordinal).Count() != rows.Count)
            throw new InvalidDataException("hero strategy source contains duplicate heroCardIds");
        if (rows.SelectMany(row => row.powerAliases).Distinct(StringComparer.Ordinal).Count() != rows.Count)
            throw new InvalidDataException("hero strategy source contains duplicate power aliases");

        var document = new HeroStrategyRegistryDocument { build = 246003, heroes = rows };
        var utf8 = new UTF8Encoding(false);
        var registryBytes = utf8.GetBytes(serializer.Serialize(document) + "\n");
        File.WriteAllBytes(args[2], registryBytes);

        var manifest = new HeroStrategyRegistryManifest
        {
            schemaVersion = 1,
            activation = "production_hero_strategy",
            build = 246003,
            generator = "scripts/generate_hero_strategy_registry.ps1",
            selectionContract = "119 heroes and first hp_ids alias; behavior frozen at cda0402",
            rowCount = rows.Count,
            queryKeyCount = rows.Count + rows.Sum(row => row.powerAliases.Count),
            cardsInputSha256 = Sha256(cardsBytes),
            legacyRegistryInputSha256 = Sha256(legacyBytes),
            outputSha256 = Sha256(registryBytes),
        };
        File.WriteAllText(args[3], serializer.Serialize(manifest) + "\n", utf8);

        Console.WriteLine("generated hero strategy registry rows=" + rows.Count
            + " keys=" + manifest.queryKeyCount + " sha256=" + manifest.outputSha256);
        return 0;
    }
}
