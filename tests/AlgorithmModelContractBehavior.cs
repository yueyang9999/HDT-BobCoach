using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BobCoach.Engine.Decision;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
            VerifiesRequiredJsonFieldsArePresent(args[0]);
            VerifiesInvalidUtf8IsRejected(args[0]);
            VerifiesCoreFailClosedBoundaries(args[0]);
            VerifiesRuntimeRequiresResolvedArtifactIdentities(args[0]);
            VerifiesRuntimeApprovalBindsExactArtifact(args[0]);
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

    private static void VerifiesRequiredJsonFieldsArePresent(string root)
    {
        AssertThrows<ModelValidationException>(() => RankingModelLoader.Load(
            EnvelopeWithoutProperty(root, "PayloadSha256")), "missing envelope checksum", "artifact json");
        AssertThrows<ModelValidationException>(() => RankingModelLoader.Load(
            PayloadWithoutProperty(root, "Intercept")), "missing payload intercept", "payload json");
    }

    private static void VerifiesInvalidUtf8IsRejected(string root)
    {
        AssertThrows<ModelValidationException>(() => RankingModelLoader.Load(
            EnvelopeWithInvalidUtf8(root)), "invalid envelope UTF-8", "artifact utf-8");
        AssertThrows<ModelValidationException>(() => RankingModelLoader.Load(
            PayloadWithInvalidUtf8(root)), "invalid payload UTF-8", "payload utf-8");
    }

    private static void VerifiesCoreFailClosedBoundaries(string root)
    {
        AssertThrows<ModelValidationException>(() => RankingModelLoader.Load(
            EnvelopeWithArtifactSchemaVersion(root, 2)), "artifact schema version", "artifact schema");
        AssertThrows<ModelValidationException>(() => RankingModelLoader.Load(
            EnvelopeWithInvalidBase64(root)), "invalid Base64", "payload base64");
        AssertThrows<ModelValidationException>(() => RankingModelLoader.Load(
            new MemoryStream(new byte[(1024 * 1024) + 1])), "artifact size", "artifact size");
        AssertThrows<ModelValidationException>(() => RankingModelLoader.Load(
            EnvelopeWithUnexpectedProperty(root)), "unknown envelope property", "artifact json");
        AssertThrows<ModelValidationException>(() => RankingModelLoader.Load(
            PayloadWithUnexpectedProperty(root)), "unknown payload property", "payload json");
        AssertThrows<ModelValidationException>(() => RankingModelLoader.Load(
            EnvelopeWithDuplicateProperty(root, "PayloadSha256")), "duplicate envelope property", "artifact json");
        AssertThrows<ModelValidationException>(() => RankingModelLoader.Load(
            PayloadWithDuplicateProperty(root, "ModelId")), "duplicate payload property", "payload json");
        AssertThrows<ModelValidationException>(() => RankingModelLoader.Load(
            PayloadWithoutProperty(root, "ModelId")), "missing payload identity", "payload json");

        AssertPayloadRejects(root, model => model.FeatureNames = model.FeatureNames.Take(44).ToArray(),
            "feature names");
        AssertPayloadRejects(root, model => model.Means = model.Means.Take(44).ToArray(), "means");
        AssertPayloadRejects(root, model => model.StandardDeviations = model.StandardDeviations.Take(44).ToArray(),
            "standard deviations");
        AssertPayloadRejects(root, model => model.Minimums = model.Minimums.Take(44).ToArray(), "minimums");
        AssertPayloadRejects(root, model => model.Maximums = model.Maximums.Take(44).ToArray(), "maximums");
        AssertPayloadRejects(root, model => model.Weights = model.Weights.Take(44).ToArray(), "weights");
        AssertPayloadRejects(root, model => model.ActionTypeBiases = model.ActionTypeBiases.Take(9).ToArray(),
            "action type biases");
        AssertPayloadRejects(root, model => model.StandardDeviations[0] = -1d, "standard deviations");
        AssertPayloadRejects(root, model => model.OodDistanceThreshold = -1d, "OOD distance threshold");
    }

    private static void VerifiesRuntimeRequiresResolvedArtifactIdentities(string root)
    {
        using (var baseline = ResolvedRuntimeEnvelope(root, model => { }))
        {
            var approval = RankingModelLoader.Load(baseline);
            baseline.Position = 0;
            var context = ApprovedRuntimeContext(approval);
            var loaded = RankingModelLoader.LoadForRuntime(baseline, context);
            AssertEqual("test-model-v1", loaded.ModelId, "resolved runtime baseline");

            AssertRuntimeRejects(root, context, model => model.ModelId = "unknown",
                "artifact model identity");
            AssertRuntimeRejects(root, context, model => model.TrainingSetIdentity = "unknown",
                "artifact training set identity");
            AssertRuntimeRejects(root, context, model => model.SeasonRange = "unknown",
                "artifact season range");
            AssertRuntimeRejects(root, context, model => model.GameBuild = "unknown",
                "artifact game build identity");
            AssertRuntimeRejects(root, context, model => model.SeasonId = "unknown",
                "artifact season identity");
            AssertRuntimeRejects(root, context, model => model.CardPoolIdentity = "unknown",
                "artifact card pool identity");
            AssertRuntimeRejects(root, context, model => model.EffectAcceptanceStatus = "unknown",
                "artifact effect acceptance");
            AssertRuntimeRejects(root, context, model => model.GameBuild = "test-build-v2",
                "artifact game build mismatch");
            AssertRuntimeRejects(root, context, model => model.SeasonId = "test-season-v2",
                "artifact season identity mismatch");
            AssertRuntimeRejects(root, context, model => model.CardPoolIdentity = "test-card-pool-v2",
                "artifact card pool identity mismatch");
            AssertRuntimeRejects(root, context, model => model.EffectAcceptanceStatus = "accepted",
                "synthetic artifact", model => model.ModelId = "synthetic-model");
            using (var unknownContext = ResolvedRuntimeEnvelope(root, model => { }))
            {
                AssertThrows<ModelValidationException>(() => RankingModelLoader.LoadForRuntime(unknownContext,
                    new RankingModelRuntimeContext("unknown", "test-season-v1", "test-card-pool-v1", true,
                        approval.PayloadSha256, approval.ModelId, approval.TrainingSetIdentity, approval.SeasonRange)),
                    "unknown runtime identity", "runtime identity");
            }
        }
    }

    private static void VerifiesRuntimeApprovalBindsExactArtifact(string root)
    {
        AssertRuntimeApprovalRejects(root, model => model.CreatedAtUtc = "2026-07-31T00:00:00Z",
            approval => ApprovedRuntimeContext(approval, payloadSha256: new string('0', 64)),
            "approved payload checksum");
        AssertRuntimeApprovalRejects(root, model => { },
            approval => ApprovedRuntimeContext(approval, modelId: "test-model-v2"),
            "approved model identity");
        AssertRuntimeApprovalRejects(root, model => { },
            approval => ApprovedRuntimeContext(approval, trainingSetIdentity: "test-training-v2"),
            "approved training set identity");
        AssertRuntimeApprovalRejects(root, model => { },
            approval => ApprovedRuntimeContext(approval, seasonRange: "test-season-range-v2"),
            "approved season range");
        AssertRuntimeApprovalRejects(root, model => { },
            approval => ApprovedRuntimeContext(approval, payloadSha256: approval.PayloadSha256.ToUpperInvariant()),
            "case-variant approved payload checksum", "approved payload checksum");
        AssertRuntimeApprovalRejects(root, model => { },
            approval => ApprovedRuntimeContext(approval, modelId: approval.ModelId.ToUpperInvariant()),
            "case-variant approved model identity", "approved model identity");
        AssertRuntimeApprovalRejects(root, model => { },
            approval => ApprovedRuntimeContext(approval, trainingSetIdentity: approval.TrainingSetIdentity.ToUpperInvariant()),
            "case-variant approved training set identity", "approved training set identity");
        AssertRuntimeApprovalRejects(root, model => { },
            approval => ApprovedRuntimeContext(approval, seasonRange: approval.SeasonRange.ToUpperInvariant()),
            "case-variant approved season range", "approved season range");
        AssertRuntimeApprovalRejects(root, model => { },
            approval => ApprovedRuntimeContext(approval, payloadSha256: "unknown"),
            "unknown approved payload checksum", "approved payload checksum");
        AssertRuntimeApprovalRejects(root, model => { },
            approval => ApprovedRuntimeContext(approval, modelId: "unknown"),
            "unknown approved model identity", "approved model identity");
        AssertRuntimeApprovalRejects(root, model => { },
            approval => ApprovedRuntimeContext(approval, trainingSetIdentity: "unknown"),
            "unknown approved training set identity", "approved training set identity");
        AssertRuntimeApprovalRejects(root, model => { },
            approval => ApprovedRuntimeContext(approval, seasonRange: "unknown"),
            "unknown approved season range", "approved season range");
        AssertRuntimeApprovalRejects(root, model => { },
            approval => ApprovedRuntimeContext(approval, payloadSha256: string.Empty),
            "empty approved payload checksum", "approved payload checksum");
        AssertRuntimeApprovalRejects(root, model => { },
            approval => ApprovedRuntimeContext(approval, modelId: string.Empty),
            "empty approved model identity", "approved model identity");
        AssertRuntimeApprovalRejects(root, model => { },
            approval => ApprovedRuntimeContext(approval, trainingSetIdentity: string.Empty),
            "empty approved training set identity", "approved training set identity");
        AssertRuntimeApprovalRejects(root, model => { },
            approval => ApprovedRuntimeContext(approval, seasonRange: string.Empty),
            "empty approved season range", "approved season range");
    }

    private static void AssertRuntimeApprovalRejects(string root, Action<RankingModelPayload> mutation,
        Func<RankingModelPayload, RankingModelRuntimeContext> contextFactory, string label,
        string expectedMessage = null)
    {
        using (var stream = ResolvedRuntimeEnvelope(root, mutation))
        {
            var approval = RankingModelLoader.Load(stream);
            stream.Position = 0;
            AssertThrows<ModelValidationException>(() => RankingModelLoader.LoadForRuntime(stream,
                contextFactory(approval)), label, expectedMessage ?? label);
        }
    }

    private static RankingModelRuntimeContext ApprovedRuntimeContext(RankingModelPayload approval,
        string payloadSha256 = null, string modelId = null, string trainingSetIdentity = null,
        string seasonRange = null)
    {
        return new RankingModelRuntimeContext(approval.GameBuild, approval.SeasonId,
            approval.CardPoolIdentity, true, payloadSha256 ?? approval.PayloadSha256,
            modelId ?? approval.ModelId, trainingSetIdentity ?? approval.TrainingSetIdentity,
            seasonRange ?? approval.SeasonRange);
    }

    private static void AssertRuntimeRejects(string root, RankingModelRuntimeContext context,
        Action<RankingModelPayload> mutation, string expectedReason,
        Action<RankingModelPayload> setup = null)
    {
        using (var stream = ResolvedRuntimeEnvelope(root, model =>
        {
            if (setup != null) setup(model);
            mutation(model);
        }))
        {
            AssertThrows<ModelValidationException>(() => RankingModelLoader.LoadForRuntime(stream, context),
                expectedReason, expectedReason);
        }
    }

    private static MemoryStream ResolvedRuntimeEnvelope(string root, Action<RankingModelPayload> mutation)
    {
        return EnvelopeWithMutation(root, model =>
        {
            model.ModelId = "test-model-v1";
            model.TrainingSetIdentity = "test-training-v1";
            model.SeasonRange = "test-season-range-v1";
            model.GameBuild = "test-build-v1";
            model.SeasonId = "test-season-v1";
            model.CardPoolIdentity = "test-card-pool-v1";
            model.EffectAcceptanceStatus = "accepted";
            mutation(model);
        });
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

    private static MemoryStream EnvelopeWithArtifactSchemaVersion(string root, int version)
    {
        var envelope = ReadEnvelope(root);
        envelope.ArtifactSchemaVersion = version;
        return ToStream(envelope);
    }

    private static MemoryStream EnvelopeWithInvalidBase64(string root)
    {
        var envelope = ReadEnvelope(root);
        envelope.PayloadBase64 = "not-valid-base64!";
        return ToStream(envelope);
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

    private static MemoryStream EnvelopeWithoutProperty(string root, string propertyName)
    {
        var document = JObject.Parse(File.ReadAllText(Path.Combine(root, "tests", "fixtures", "algorithm",
            "linear-v1.valid.envelope.json"), Encoding.UTF8));
        document.Remove(propertyName);
        return new MemoryStream(Encoding.UTF8.GetBytes(document.ToString(Formatting.None)));
    }

    private static MemoryStream PayloadWithoutProperty(string root, string propertyName)
    {
        var envelope = ReadEnvelope(root);
        var payload = JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(envelope.PayloadBase64)));
        payload.Remove(propertyName);
        return EnvelopeWithPayload(envelope, payload.ToString(Formatting.None));
    }

    private static MemoryStream EnvelopeWithUnexpectedProperty(string root)
    {
        var document = JObject.Parse(File.ReadAllText(Path.Combine(root, "tests", "fixtures", "algorithm",
            "linear-v1.valid.envelope.json"), Encoding.UTF8));
        document.Add("Unexpected", true);
        return new MemoryStream(Encoding.UTF8.GetBytes(document.ToString(Formatting.None)));
    }

    private static MemoryStream PayloadWithUnexpectedProperty(string root)
    {
        var envelope = ReadEnvelope(root);
        var payload = JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(envelope.PayloadBase64)));
        payload.Add("Unexpected", true);
        return EnvelopeWithPayload(envelope, payload.ToString(Formatting.None));
    }

    private static MemoryStream EnvelopeWithDuplicateProperty(string root, string propertyName)
    {
        var json = File.ReadAllText(Path.Combine(root, "tests", "fixtures", "algorithm",
            "linear-v1.valid.envelope.json"), Encoding.UTF8);
        var closingBrace = json.LastIndexOf("}", StringComparison.Ordinal);
        if (closingBrace < 0) throw new InvalidOperationException("envelope closing brace not found");
        var value = JObject.Parse(json)[propertyName];
        if (value == null) throw new InvalidOperationException("envelope property not found");
        return new MemoryStream(Encoding.UTF8.GetBytes(json.Insert(closingBrace,
            ",\"" + propertyName + "\":" + JsonConvert.SerializeObject(value))));
    }

    private static MemoryStream PayloadWithDuplicateProperty(string root, string propertyName)
    {
        var envelope = ReadEnvelope(root);
        var payload = Encoding.UTF8.GetString(Convert.FromBase64String(envelope.PayloadBase64));
        var closingBrace = payload.LastIndexOf("}", StringComparison.Ordinal);
        if (closingBrace < 0) throw new InvalidOperationException("payload closing brace not found");
        var value = JObject.Parse(payload)[propertyName];
        if (value == null) throw new InvalidOperationException("payload property not found");
        return EnvelopeWithPayload(envelope, payload.Insert(closingBrace,
            ",\"" + propertyName + "\":" + JsonConvert.SerializeObject(value)));
    }

    private static MemoryStream EnvelopeWithInvalidUtf8(string root)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(ReadEnvelope(root)));
        var checksumPrefix = Encoding.ASCII.GetBytes("\"PayloadSha256\":\"");
        var index = IndexOf(bytes, checksumPrefix);
        if (index < 0) throw new InvalidOperationException("checksum prefix not found");
        bytes[index + checksumPrefix.Length] = 0xff;
        return new MemoryStream(bytes);
    }

    private static MemoryStream PayloadWithInvalidUtf8(string root)
    {
        var envelope = ReadEnvelope(root);
        var bytes = Convert.FromBase64String(envelope.PayloadBase64);
        var modelPrefix = Encoding.ASCII.GetBytes("synthetic-linear-v1");
        var index = IndexOf(bytes, modelPrefix);
        if (index < 0) throw new InvalidOperationException("model identity not found");
        bytes[index] = 0xff;
        return EnvelopeWithPayloadBytes(envelope, bytes);
    }

    private static MemoryStream EnvelopeWithPayloadBytes(RankingModelEnvelope envelope, byte[] payloadBytes)
    {
        envelope.PayloadBase64 = Convert.ToBase64String(payloadBytes);
        using (var sha256 = SHA256.Create())
            envelope.PayloadSha256 = Hex(sha256.ComputeHash(payloadBytes));
        return ToStream(envelope);
    }

    private static int IndexOf(byte[] value, byte[] pattern)
    {
        for (var start = 0; start <= value.Length - pattern.Length; start++)
        {
            var matches = true;
            for (var offset = 0; offset < pattern.Length; offset++)
            {
                if (value[start + offset] == pattern[offset]) continue;
                matches = false;
                break;
            }
            if (matches) return start;
        }
        return -1;
    }

    private static void AssertPayloadRejects(string root, Action<RankingModelPayload> mutation,
        string expectedReason)
    {
        using (var stream = EnvelopeWithMutation(root, mutation))
        {
            AssertThrows<ModelValidationException>(() => RankingModelLoader.Load(stream), expectedReason,
                expectedReason);
        }
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

    private static void AssertThrows<T>(Action action, string label, string expectedMessage = null) where T : Exception
    {
        try { action(); }
        catch (T ex)
        {
            if (expectedMessage != null && ex.Message.IndexOf(expectedMessage, StringComparison.Ordinal) < 0)
                throw new InvalidOperationException(label + " expected message=" + expectedMessage + " actual=" + ex.Message);
            return;
        }
        throw new InvalidOperationException(label + " did not throw " + typeof(T).Name);
    }
}
