using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using BobCoach.Engine;

public sealed class ClassificationSourceRow
{
    public string str_id { get; set; }
}

public sealed class ClassificationRegistryRow
{
    public string cardId { get; set; }
    public string primaryRole { get; set; }
    public List<string> allRoles { get; set; }
    public bool isCoreCombo { get; set; }
    public bool requiresPartner { get; set; }
    public string partnerMechanic { get; set; }
    public float economyValue { get; set; }
    public float combatValue { get; set; }
    public float growthValue { get; set; }
}

public sealed class ClassificationRegistryManifest
{
    public int schemaVersion { get; set; }
    public string activation { get; set; }
    public int build { get; set; }
    public string generator { get; set; }
    public string selectionContract { get; set; }
    public int rowCount { get; set; }
    public string cardsInputSha256 { get; set; }
    public string semanticsInputSha256 { get; set; }
    public string outputSha256 { get; set; }
}

public static class CardClassificationRegistryGenerator
{
    private static string Sha256(byte[] content)
    {
        using (var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(content)).Replace("-", "");
    }

    private static ClassificationRegistryRow ToRow(
        string cardId,
        CardClassifier.CardClassification value)
    {
        return new ClassificationRegistryRow
        {
            cardId = cardId,
            primaryRole = value.PrimaryRole.ToString(),
            allRoles = value.AllRoles == null
                ? new List<string>()
                : value.AllRoles.Select(role => role.ToString())
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(role => role, StringComparer.Ordinal)
                    .ToList(),
            isCoreCombo = value.IsCoreCombo,
            requiresPartner = value.RequiresPartner,
            partnerMechanic = value.PartnerMechanic ?? "",
            economyValue = value.EconomyValue,
            combatValue = value.CombatValue,
            growthValue = value.GrowthValue,
        };
    }

    public static int Main(string[] args)
    {
        if (args.Length != 4)
        {
            Console.Error.WriteLine(
                "usage: CardClassificationRegistryGenerator <cards.json> <semantic_index.json> <registry.json> <manifest.json>");
            return 2;
        }

        var cardsBytes = File.ReadAllBytes(args[0]);
        var semanticsBytes = File.ReadAllBytes(args[1]);
        var cardsJson = Encoding.UTF8.GetString(cardsBytes);
        var semanticsJson = Encoding.UTF8.GetString(semanticsBytes);
        var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        var sourceRows = serializer.Deserialize<List<ClassificationSourceRow>>(cardsJson)
            ?? new List<ClassificationSourceRow>();

        var classifier = new CardClassifier();
        classifier.SetSemantics(SemanticIndexLoader.Load(semanticsJson));
        classifier.LoadCardData(cardsJson);

        var rows = new List<ClassificationRegistryRow>();
        foreach (var source in sourceRows
            .Where(row => row != null && !string.IsNullOrEmpty(row.str_id))
            .OrderBy(row => row.str_id, StringComparer.Ordinal))
        {
            var classification = classifier.GetClassification(source.str_id);
            if (!classification.HasValue)
                throw new InvalidDataException("missing classification for " + source.str_id);
            rows.Add(ToRow(source.str_id, classification.Value));
        }

        if (rows.Select(row => row.cardId).Distinct(StringComparer.Ordinal).Count() != rows.Count)
            throw new InvalidDataException("classification source contains duplicate CardIds");

        var utf8 = new UTF8Encoding(false);
        var registryBytes = utf8.GetBytes(serializer.Serialize(rows) + "\n");
        File.WriteAllBytes(args[2], registryBytes);

        var manifest = new ClassificationRegistryManifest
        {
            schemaVersion = 1,
            activation = "production_classification",
            build = 246003,
            generator = "scripts/generate_card_classification_registry.ps1",
            selectionContract = "pre-5B.2 cards.json str_id membership; behavior frozen at 05acd3e",
            rowCount = rows.Count,
            cardsInputSha256 = Sha256(cardsBytes),
            semanticsInputSha256 = Sha256(semanticsBytes),
            outputSha256 = Sha256(registryBytes),
        };
        File.WriteAllText(args[3], serializer.Serialize(manifest) + "\n", utf8);

        Console.WriteLine("generated classification registry rows=" + rows.Count
            + " sha256=" + manifest.outputSha256);
        return 0;
    }
}
