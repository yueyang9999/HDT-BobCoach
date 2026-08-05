using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace BobCoach.Engine.Decision
{
    public sealed class CombatOutcome
    {
        public string ResultSummary { get; private set; }

        public CombatOutcome(string resultSummary)
        {
            ResultSummary = resultSummary ?? "";
        }
    }

    public sealed class DecisionEventRecorder : IShadowComparisonSink
    {
        private readonly string _path;
        private readonly string _modelId;
        private readonly string _modelPayloadSha256;
        private readonly object _sync = new object();
        private readonly HashSet<string> _writtenKeys = new HashSet<string>(StringComparer.Ordinal);

        public DecisionEventRecorder(string path)
            : this(path, null, null)
        {
        }

        public DecisionEventRecorder(string path, string modelId, string modelPayloadSha256)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path");
            _path = path;
            _modelId = NormalizeIdentity(modelId);
            _modelPayloadSha256 = NormalizeIdentity(modelPayloadSha256);
            LoadExistingEventKeys(_writtenKeys, path);
        }

        public static DecisionEventRecorder CreateForLocalReplay()
        {
            return CreateForLocalReplay(null, null);
        }

        public static DecisionEventRecorder CreateForLocalReplay(string modelId,
            string modelPayloadSha256)
        {
            return new DecisionEventRecorder(BobCoachDataPaths.GetTrustedDecisionEventsPath(),
                modelId, modelPayloadSha256);
        }

        public void RecordCreated(DecisionEventRecord record)
        {
            if (record == null) throw new ArgumentNullException("record");
            RequireDecisionId(record.DecisionId);
            AppendOnce(record.DecisionId + "|created", "DecisionCreated", record);
        }

        public void RecordComparison(ShadowComparison comparison)
        {
            if (comparison == null) throw new ArgumentNullException("comparison");
            RequireDecisionId(comparison.DecisionId);
            AppendOnce(comparison.ComparisonId, "ShadowComparison", comparison);
        }

        public void AttachRecommendation(string decisionId, DecisionResult result, string observedAtUtc)
        {
            RequireDecisionId(decisionId);
            if (result == null) throw new ArgumentNullException("result");
            AppendOnce(decisionId + "|recommendation", "RecommendationIssued",
                new { decisionId, observedAtUtc, result });
        }

        public void AttachObservedAction(string decisionId, string actionKey, string observedAtUtc)
        {
            AttachObservedAction(decisionId, actionKey, observedAtUtc, 0);
        }

        public void AttachCombatOutcome(string decisionId, CombatOutcome outcome, string observedAtUtc)
        {
            RequireDecisionId(decisionId);
            if (outcome == null) throw new ArgumentNullException("outcome");
            AppendOnce(decisionId + "|combat", "CombatOutcomeObserved",
                new { decisionId, observedAtUtc, outcome });
        }

        internal void AttachObservedAction(string decisionId, string actionKey, string observedAtUtc,
            int occurrenceOrdinal)
        {
            RequireDecisionId(decisionId);
            if (string.IsNullOrWhiteSpace(actionKey)) throw new ArgumentException("actionKey");
            if (occurrenceOrdinal < 0) throw new ArgumentOutOfRangeException("occurrenceOrdinal");
            AppendOnce(decisionId + "|action|" + occurrenceOrdinal + "|" + actionKey,
                "PlayerActionObserved", new { decisionId, observedAtUtc, actionKey });
        }

        private void AppendOnce(string key, string eventType, object payload)
        {
            lock (_sync)
            {
                if (_writtenKeys.Contains(key)) return;
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                var line = JsonConvert.SerializeObject(new
                {
                    eventKey = key,
                    eventType,
                    modelId = _modelId,
                    modelPayloadSha256 = _modelPayloadSha256,
                    payload
                }, Formatting.None) + Environment.NewLine;
                var bytes = new UTF8Encoding(false).GetBytes(line);
                using (var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                _writtenKeys.Add(key);
            }
        }

        private static void LoadExistingEventKeys(ISet<string> keys, string path)
        {
            if (!File.Exists(path)) return;
            using (var reader = new StreamReader(path, Encoding.UTF8, true))
            {
                string line;
                var lineNumber = 0;
                while ((line = reader.ReadLine()) != null)
                {
                    lineNumber++;
                    if (string.IsNullOrWhiteSpace(line))
                        throw new InvalidDataException("Malformed decision event line " + lineNumber);
                    keys.Add(ReadEventKey(line, lineNumber));
                }
            }
        }

        private static string ReadEventKey(string line, int lineNumber)
        {
            try
            {
                using (var text = new StringReader(line))
                using (var reader = new JsonTextReader(text))
                {
                    if (!reader.Read() || reader.TokenType != JsonToken.StartObject)
                        throw new InvalidDataException("Malformed decision event line " + lineNumber);

                    string eventKey = null;
                    while (reader.Read() && reader.TokenType != JsonToken.EndObject)
                    {
                        if (reader.TokenType != JsonToken.PropertyName)
                            throw new InvalidDataException("Malformed decision event line " + lineNumber);
                        var propertyName = (string)reader.Value;
                        if (!reader.Read())
                            throw new InvalidDataException("Malformed decision event line " + lineNumber);
                        if (string.Equals(propertyName, "eventKey", StringComparison.Ordinal))
                        {
                            if (reader.TokenType != JsonToken.String)
                                throw new InvalidDataException("Malformed decision event line " + lineNumber);
                            eventKey = (string)reader.Value;
                        }
                        else reader.Skip();
                    }
                    if (string.IsNullOrWhiteSpace(eventKey))
                        throw new InvalidDataException("Malformed decision event line " + lineNumber);
                    if (reader.Read())
                        throw new InvalidDataException("Malformed decision event line " + lineNumber);
                    return eventKey;
                }
            }
            catch (JsonReaderException ex)
            {
                throw new InvalidDataException("Malformed decision event line " + lineNumber, ex);
            }
        }

        private static void RequireDecisionId(string decisionId)
        {
            if (string.IsNullOrWhiteSpace(decisionId)) throw new ArgumentException("decisionId");
        }

        private static string NormalizeIdentity(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "unknown" : value;
        }
    }
}
