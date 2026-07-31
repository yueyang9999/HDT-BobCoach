using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace BobCoach.Engine.Decision
{
    public sealed class RankingModelEnvelope
    {
        public int ArtifactSchemaVersion { get; set; }
        public string PayloadBase64 { get; set; }
        public string PayloadSha256 { get; set; }
    }

    public sealed class RankingModelPayload
    {
        public string ModelId { get; set; }
        public int FeatureSchemaVersion { get; set; }
        public string[] FeatureNames { get; set; }
        public double[] Means { get; set; }
        public double[] StandardDeviations { get; set; }
        public double[] Minimums { get; set; }
        public double[] Maximums { get; set; }
        public double[] Weights { get; set; }
        public double[] ActionTypeBiases { get; set; }
        public double Intercept { get; set; }
        public double OodDistanceThreshold { get; set; }
        public string TrainingSetIdentity { get; set; }
        public string SeasonRange { get; set; }
        public IDictionary<string, double> BenchmarkMetrics { get; set; }
        public string CreatedAtUtc { get; set; }
        public string GameBuild { get; set; }
        public string SeasonId { get; set; }
        public string CardPoolIdentity { get; set; }
        public string EffectAcceptanceStatus { get; set; }

        [JsonIgnore]
        public string PayloadSha256 { get; internal set; }
    }

    public sealed class RankingModelRuntimeContext
    {
        public string GameBuild { get; private set; }
        public string SeasonId { get; private set; }
        public string CardPoolIdentity { get; private set; }
        public bool HasEffectAcceptance { get; private set; }

        public RankingModelRuntimeContext(string gameBuild, string seasonId, string cardPoolIdentity,
            bool hasEffectAcceptance)
        {
            GameBuild = gameBuild;
            SeasonId = seasonId;
            CardPoolIdentity = cardPoolIdentity;
            HasEffectAcceptance = hasEffectAcceptance;
        }
    }

    public sealed class ModelValidationException : Exception
    {
        public ModelValidationException(string message) : base(message) { }
        public ModelValidationException(string message, Exception innerException) : base(message, innerException) { }
    }

    public static class RankingModelLoader
    {
        private const int MaximumArtifactBytes = 1024 * 1024;
        private static readonly JsonSerializerSettings StrictJson = new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Error,
        };

        // This path validates an artifact only. Runtime composition must use LoadForRuntime.
        public static RankingModelPayload Load(Stream stream)
        {
            var envelope = DeserializeEnvelopeWithSizeLimit(stream, MaximumArtifactBytes);
            if (envelope.ArtifactSchemaVersion != 1) throw Invalid("artifact schema");
            if (string.IsNullOrWhiteSpace(envelope.PayloadBase64)) throw Invalid("payload base64");
            if (string.IsNullOrWhiteSpace(envelope.PayloadSha256)) throw Invalid("payload checksum");

            byte[] payload;
            try { payload = Convert.FromBase64String(envelope.PayloadBase64); }
            catch (FormatException ex) { throw Invalid("payload base64", ex); }
            if (payload.Length > MaximumArtifactBytes) throw Invalid("payload size");

            var checksum = Sha256Hex(payload);
            if (!FixedTimeEquals(checksum, envelope.PayloadSha256)) throw Invalid("checksum");

            var model = DeserializePayload(payload);
            ValidateSchemaNamesLengthsAndFiniteNumbers(model, ActionFeatureSchema.Names);
            model.PayloadSha256 = checksum;
            return model;
        }

        public static RankingModelPayload LoadForRuntime(Stream stream, RankingModelRuntimeContext context)
        {
            var model = Load(stream);
            if (context == null || !context.HasEffectAcceptance) throw Invalid("effect acceptance");
            if (!IsResolvedIdentity(context.GameBuild) || !IsResolvedIdentity(context.SeasonId)
                || !IsResolvedIdentity(context.CardPoolIdentity)) throw Invalid("runtime identity");
            if (!string.Equals("accepted", model.EffectAcceptanceStatus, StringComparison.Ordinal))
                throw Invalid("artifact effect acceptance");
            if (IsSynthetic(model)) throw Invalid("synthetic artifact");
            if (!string.Equals(model.GameBuild, context.GameBuild, StringComparison.Ordinal)
                || !string.Equals(model.SeasonId, context.SeasonId, StringComparison.Ordinal)
                || !string.Equals(model.CardPoolIdentity, context.CardPoolIdentity, StringComparison.Ordinal))
                throw Invalid("artifact identity mismatch");
            return model;
        }

        private static RankingModelEnvelope DeserializeEnvelopeWithSizeLimit(Stream stream, int maximumBytes)
        {
            if (stream == null) throw Invalid("artifact stream");
            try
            {
                using (var copy = new MemoryStream())
                {
                    var buffer = new byte[8192];
                    var total = 0;
                    int read;
                    while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        total += read;
                        if (total > maximumBytes) throw Invalid("artifact size");
                        copy.Write(buffer, 0, read);
                    }
                    var envelope = JsonConvert.DeserializeObject<RankingModelEnvelope>(
                        Encoding.UTF8.GetString(copy.ToArray()), StrictJson);
                    if (envelope == null) throw Invalid("artifact json");
                    return envelope;
                }
            }
            catch (ModelValidationException) { throw; }
            catch (Exception ex) { throw Invalid("artifact json", ex); }
        }

        private static RankingModelPayload DeserializePayload(byte[] payload)
        {
            try
            {
                var model = JsonConvert.DeserializeObject<RankingModelPayload>(
                    Encoding.UTF8.GetString(payload), StrictJson);
                if (model == null) throw Invalid("payload json");
                return model;
            }
            catch (ModelValidationException) { throw; }
            catch (Exception ex) { throw Invalid("payload json", ex); }
        }

        private static void ValidateSchemaNamesLengthsAndFiniteNumbers(RankingModelPayload model,
            IList<string> expectedFeatureNames)
        {
            RequireText(model.ModelId, "model identity");
            RequireText(model.TrainingSetIdentity, "training set identity");
            RequireText(model.SeasonRange, "season range");
            RequireText(model.CreatedAtUtc, "creation timestamp");
            RequireText(model.GameBuild, "game build identity");
            RequireText(model.SeasonId, "season identity");
            RequireText(model.CardPoolIdentity, "card pool identity");
            RequireText(model.EffectAcceptanceStatus, "effect acceptance status");
            if (model.FeatureSchemaVersion != ActionFeatureSchema.Version) throw Invalid("feature schema");
            if (model.FeatureNames == null || model.FeatureNames.Length != expectedFeatureNames.Count)
                throw Invalid("feature names");
            for (var index = 0; index < expectedFeatureNames.Count; index++)
                if (!string.Equals(model.FeatureNames[index], expectedFeatureNames[index], StringComparison.Ordinal))
                    throw Invalid("feature names");

            ValidateFiniteArray(model.Means, expectedFeatureNames.Count, "means", false);
            ValidateFiniteArray(model.StandardDeviations, expectedFeatureNames.Count, "standard deviations", true);
            ValidateFiniteArray(model.Minimums, expectedFeatureNames.Count, "minimums", false);
            ValidateFiniteArray(model.Maximums, expectedFeatureNames.Count, "maximums", false);
            ValidateFiniteArray(model.Weights, expectedFeatureNames.Count, "weights", false);
            ValidateFiniteArray(model.ActionTypeBiases, ActionTypeBiases.OrderedTypes.Count, "action type biases", false);
            ValidateFinite(model.Intercept, "intercept");
            ValidateFinite(model.OodDistanceThreshold, "OOD distance threshold");
            if (model.OodDistanceThreshold <= 0d) throw Invalid("OOD distance threshold");
            if (model.BenchmarkMetrics == null) throw Invalid("benchmark metrics");
            foreach (var metric in model.BenchmarkMetrics)
            {
                RequireText(metric.Key, "benchmark metric name");
                ValidateFinite(metric.Value, "benchmark metric");
            }
        }

        private static void ValidateFiniteArray(double[] values, int expectedLength, string name,
            bool strictlyPositive)
        {
            if (values == null || values.Length != expectedLength) throw Invalid(name);
            for (var index = 0; index < values.Length; index++)
            {
                ValidateFinite(values[index], name);
                if (strictlyPositive && values[index] <= 0d) throw Invalid(name);
            }
        }

        private static void ValidateFinite(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) throw Invalid(name);
        }

        private static void RequireText(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value)) throw Invalid(name);
        }

        private static bool IsResolvedIdentity(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && !string.Equals(value.Trim(), "unknown", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSynthetic(RankingModelPayload model)
        {
            return HasSyntheticPrefix(model.ModelId) || HasSyntheticPrefix(model.TrainingSetIdentity)
                || HasSyntheticPrefix(model.GameBuild) || HasSyntheticPrefix(model.SeasonId)
                || HasSyntheticPrefix(model.CardPoolIdentity);
        }

        private static bool HasSyntheticPrefix(string value)
        {
            return value != null && value.StartsWith("synthetic-", StringComparison.OrdinalIgnoreCase);
        }

        private static string Sha256Hex(byte[] value)
        {
            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(value);
                var result = new StringBuilder(hash.Length * 2);
                for (var index = 0; index < hash.Length; index++)
                    result.Append(hash[index].ToString("x2", CultureInfo.InvariantCulture));
                return result.ToString();
            }
        }

        private static bool FixedTimeEquals(string expected, string actual)
        {
            var left = Encoding.ASCII.GetBytes(expected ?? "");
            var right = Encoding.ASCII.GetBytes(actual ?? "");
            var difference = left.Length ^ right.Length;
            var count = Math.Max(left.Length, right.Length);
            for (var index = 0; index < count; index++)
            {
                var leftByte = index < left.Length ? left[index] : (byte)0;
                var rightByte = index < right.Length ? right[index] : (byte)0;
                difference |= leftByte ^ rightByte;
            }
            return difference == 0;
        }

        private static ModelValidationException Invalid(string reason)
        {
            return new ModelValidationException("Invalid ranking model artifact: " + reason);
        }

        private static ModelValidationException Invalid(string reason, Exception innerException)
        {
            return new ModelValidationException("Invalid ranking model artifact: " + reason, innerException);
        }
    }
}
