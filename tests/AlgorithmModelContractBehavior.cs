using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BobCoach.Engine.Decision;
using Newtonsoft.Json;

namespace BobCoach.Engine
{
    public enum ActionType
    {
        BuyMinion, BuySpell, Refresh, Upgrade, SellMinion,
        FreezeShop, UseHeroPower, PickTrinket, PickDiscover, SendGoldToTeammate,
    }

    public sealed class GameState { }
}

namespace BobCoach.Engine.Decision
{
    public sealed class ActionDescriptor { }
}

internal static class AlgorithmModelContractBehavior
{
    private static int Main(string[] args)
    {
        try
        {
            if (args == null || args.Length != 1) throw new ArgumentException("repository root is required");
            VerifiesSyntheticFixtureLoadsForValidation(args[0]);
            VerifiesChecksumIsRequired(args[0]);
            VerifiesFeatureSchemaIsExact(args[0]);
            VerifiesNumericValuesAreFinite(args[0]);
            VerifiesDimensionsAndBoundsAreStrict(args[0]);
            VerifiesMalformedDocumentsAreRejected();
            VerifiesRuntimeCompositionRejectsSyntheticAndUnacceptedArtifacts(args[0]);
            Console.WriteLine("PASS model loader rejects corruption, schema drift, and invalid numbers");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL " + ex.Message);
            return 1;
        }
    }

    private static void VerifiesSyntheticFixtureLoadsForValidation(string root)
    {
        var valid = RankingModelLoader.Load(Open(root));
        AssertEqual(45, valid.Weights.Length, "weight count");
        AssertEqual("synthetic-loader-fixture", valid.TrainingSetIdentity, "fixture is engineering-only");
        AssertTrue(!string.IsNullOrWhiteSpace(valid.PayloadSha256), "validated checksum is retained");
    }

    private static void VerifiesChecksumIsRequired(string root)
    {
        AssertThrows<ModelValidationException>(() => RankingModelLoader.Load(TamperPayload(root)), "checksum");
    }

    private static void VerifiesFeatureSchemaIsExact(string root)
    {
        AssertThrows<ModelValidationException>(() => RankingModelLoader.Load(EnvelopeWithFeatureVersion(root, 99)), "feature schema version");
        AssertThrows<ModelValidationException>(() => RankingModelLoader.Load(EnvelopeWithReorderedFeatures(root)), "feature schema order");
    }

    private static void VerifiesNumericValuesAreFinite(string root)
    {
        AssertThrows<ModelValidationException>(() => RankingModelLoader.Load(EnvelopeWithWeight(root, "NaN")), "finite number");
        AssertThrows<ModelValidationException>(() => RankingModelLoader.Load(EnvelopeWithWeight(root, "Infinity")), "infinite number");
    }

    private static void VerifiesDimensionsAndBoundsAreStrict(string root)
    {
        AssertThrows<ModelValidationException>(() => RankingModelLoader.Load(EnvelopeWithMutation(root, model =>
            model.StandardDeviations[0] = 0d)), "standard deviation");
        AssertThrows<ModelValidationException>(() => RankingModelLoader.Load(EnvelopeWithMutation(root, model =>
            model.OodDistanceThreshold = 0d)), "OOD threshold");
        AssertThrows<ModelValidationException>(() => RankingModelLoader.Load(EnvelopeWithMutation(root, model =>
            model.ActionTypeBiases = model.ActionTypeBiases.Take(9).ToArray())), "action bias count");
    }

    private static void VerifiesMalformedDocumentsAreRejected()
    {
        AssertThrows<ModelValidationException>(() => RankingModelLoader.Load(
            new MemoryStream(new byte[] { 1, 2, 3 })), "invalid json");
    }

    private static void VerifiesRuntimeCompositionRejectsSyntheticAndUnacceptedArtifacts(string root)
    {
        var context = new RankingModelRuntimeContext("synthetic-build", "synthetic-season",
            "synthetic-card-pool", true);
        AssertThrows<ModelValidationException>(() => RankingModelLoader.LoadForRuntime(Open(root), context),
            "unaccepted artifact");
        AssertThrows<ModelValidationException>(() => RankingModelLoader.LoadForRuntime(
            EnvelopeWithMutation(root, model => model.EffectAcceptanceStatus = "accepted"), context),
            "synthetic artifact");
        AssertThrows<ModelValidationException>(() => RankingModelLoader.LoadForRuntime(
            EnvelopeWithMutation(root, model => model.EffectAcceptanceStatus = "accepted"),
            new RankingModelRuntimeContext("unknown", "synthetic-season", "synthetic-card-pool", true)),
            "unknown runtime identity");
    }

    private static Stream Open(string root)
    {
        return File.OpenRead(Path.Combine(root, "tests", "fixtures", "algorithm", "linear-v1.valid.envelope.json"));
    }

    private static MemoryStream TamperPayload(string root)
    {
        var envelope = ReadEnvelope(root);
        envelope.PayloadBase64 = envelope.PayloadBase64.Substring(0, envelope.PayloadBase64.Length - 2) + "AA";
        return ToStream(envelope);
    }

    private static MemoryStream EnvelopeWithFeatureVersion(string root, int version)
    {
        return EnvelopeWithMutation(root, model => model.FeatureSchemaVersion = version);
    }

    private static MemoryStream EnvelopeWithReorderedFeatures(string root)
    {
        return EnvelopeWithMutation(root, model =>
        {
            var first = model.FeatureNames[0];
            model.FeatureNames[0] = model.FeatureNames[1];
            model.FeatureNames[1] = first;
        });
    }

    private static MemoryStream EnvelopeWithWeight(string root, string number)
    {
        return EnvelopeWithMutation(root, model => model.Weights[0] =
            string.Equals(number, "NaN", StringComparison.Ordinal) ? double.NaN : double.PositiveInfinity);
    }

    private static MemoryStream EnvelopeWithMutation(string root, Action<RankingModelPayload> mutation)
    {
        var envelope = ReadEnvelope(root);
        var model = JsonConvert.DeserializeObject<RankingModelPayload>(
            Encoding.UTF8.GetString(Convert.FromBase64String(envelope.PayloadBase64)));
        mutation(model);
        return EnvelopeWithPayload(envelope, JsonConvert.SerializeObject(model));
    }

    private static MemoryStream EnvelopeWithPayload(RankingModelEnvelope envelope, string payload)
    {
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        envelope.PayloadBase64 = Convert.ToBase64String(payloadBytes);
        using (var sha256 = SHA256.Create())
            envelope.PayloadSha256 = Hex(sha256.ComputeHash(payloadBytes));
        return ToStream(envelope);
    }

    private static RankingModelEnvelope ReadEnvelope(string root)
    {
        using (var stream = Open(root))
        using (var reader = new StreamReader(stream))
            return JsonConvert.DeserializeObject<RankingModelEnvelope>(reader.ReadToEnd());
    }

    private static MemoryStream ToStream(RankingModelEnvelope envelope)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(envelope)));
    }

    private static string Hex(byte[] bytes)
    {
        return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    }

    private static void AssertEqual<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(label + " expected=" + expected + " actual=" + actual);
    }

    private static void AssertTrue(bool value, string label)
    {
        if (!value) throw new InvalidOperationException(label);
    }

    private static void AssertThrows<T>(Action action, string label) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException(label + " did not throw " + typeof(T).Name);
    }
}
