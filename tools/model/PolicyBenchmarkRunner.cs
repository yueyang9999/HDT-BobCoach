using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BobCoach.Engine;
using BobCoach.Engine.Decision;
using BobCoach.ModelTools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static class PolicyBenchmarkRunner
{
    private const string BenchmarkId = "season-2026-next-v1";
    private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
    private static readonly string[] RequiredProbeIds = {
        "model-unavailable", "critical-state-missing", "unsupported-mechanic",
        "out-of-distribution", "low-confidence", "no-safe-action", "planner-timeout"
    };
    private static readonly IDictionary<string, FallbackReason> RequiredProbeFallbacks =
        new Dictionary<string, FallbackReason>(StringComparer.Ordinal) {
            { "model-unavailable", FallbackReason.ModelUnavailable },
            { "critical-state-missing", FallbackReason.CriticalStateMissing },
            { "unsupported-mechanic", FallbackReason.UnsupportedMechanic },
            { "out-of-distribution", FallbackReason.OutOfDistribution },
            { "low-confidence", FallbackReason.LowConfidence },
            { "no-safe-action", FallbackReason.NoSafeAction },
            { "planner-timeout", FallbackReason.None }
        };

    private sealed class FakePolicy : IDecisionPolicy
    {
        private readonly string _policyId;
        private readonly FallbackReason _fallbackReason;
        private readonly PlannerStatus _plannerStatus;
        private readonly ActionType _actionType;

        public FakePolicy(string policyId, FallbackReason fallbackReason, PlannerStatus plannerStatus,
            ActionType actionType = ActionType.Upgrade)
        {
            _policyId = policyId;
            _fallbackReason = fallbackReason;
            _plannerStatus = plannerStatus;
            _actionType = actionType;
        }

        public string PolicyId { get { return _policyId; } }
        public int CallCount { get; private set; }

        public DecisionResult Decide(GameState state)
        {
            CallCount++;
            var action = new GameAction { Type = _actionType, TargetIndex = -1 };
            var key = ActionDescriptor.CreateKey(action);
            return new DecisionResult(_policyId, action,
                new List<RankedAction> { new RankedAction(action, key, 1d, null) },
                1d, _plannerStatus, _fallbackReason, "");
        }
    }

    private sealed class CandidateBenchmarkAdapter
    {
        private readonly Func<GameState, UnifiedDecisionDiagnostics> _unifiedDecision;
        private readonly IDecisionPolicy _legacy;

        public CandidateBenchmarkAdapter(Func<GameState, UnifiedDecisionDiagnostics> unifiedDecision,
            IDecisionPolicy legacy)
        {
            _unifiedDecision = unifiedDecision;
            _legacy = legacy;
        }

        public string PolicyId { get { return "unified-linear-v1"; } }

        public DecisionResult Decide(GameState state)
        {
            return DecideWithDiagnostics(state).Decision;
        }

        public UnifiedDecisionDiagnostics DecideWithDiagnostics(GameState state)
        {
            var unified = _unifiedDecision(Clone(state));
            if (unified == null || unified.Decision == null || unified.PlanningResult == null)
                throw new InvalidDataException("candidate diagnostics are missing");
            if (unified.Decision.FallbackReason == FallbackReason.None)
                return unified;
            return new UnifiedDecisionDiagnostics(
                CandidateEvidenceSurface(unified.Decision, _legacy, Clone(state)),
                unified.PlanningResult);
        }
    }

    private sealed class EvaluationSeasonFeatureExtension : ISeasonFeatureExtension
    {
        public string GameBuild { get; private set; }
        public string SeasonId { get; private set; }
        public string CardPoolIdentity { get; private set; }

        public EvaluationSeasonFeatureExtension(string gameBuild, string seasonId,
            string cardPoolIdentity)
        {
            GameBuild = gameBuild;
            SeasonId = seasonId;
            CardPoolIdentity = cardPoolIdentity;
        }

        public bool TryEvaluate(GameState state, ActionDescriptor action, GameState nextState,
            out double value)
        {
            value = 0d;
            return false;
        }
    }

    private sealed class Scenario
    {
        public string ScenarioId;
        public string GameId;
        public string StateFingerprint;
        public string CandidateFingerprint;
        public GameState State;
    }

    private sealed class BenchmarkContext
    {
        public IList<Scenario> Scenarios;
        public string GameBuild;
        public string SeasonId;
        public string CardPoolIdentity;
    }

    public static int Main(string[] args)
    {
        try
        {
            var values = ParseArgs(args);
            string output;
            if (!values.TryGetValue("--output", out output) || string.IsNullOrWhiteSpace(output))
                throw new ArgumentException("--output is required");
            string outputRoot;
            if (!values.TryGetValue("--output-root", out outputRoot)
                || string.IsNullOrWhiteSpace(outputRoot))
                throw new ArgumentException("--output-root is required");
            using (var secureOutput = SecureEvidenceDirectory.Acquire(outputRoot, output))
            {
                if (values.ContainsKey("--self-test")) RunSelfTest(secureOutput);
                else RunProduction(values, secureOutput);
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static Dictionary<string, string> ParseArgs(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var allowed = new HashSet<string>(new[] { "--output", "--output-root", "--self-test", "--data-root", "--scenarios",
            "--probes", "--model", "--evaluation-protocol" },
            StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index++)
        {
            var key = args[index];
            if (!allowed.Contains(key)) throw new ArgumentException("unknown argument: " + key);
            if (key == "--self-test") { result.Add(key, "true"); continue; }
            if (index + 1 >= args.Length) throw new ArgumentException("missing value for " + key);
            result.Add(key, args[++index]);
        }
        return result;
    }

    private static void RunSelfTest(SecureEvidenceDirectory output)
    {
        var state = new GameState
        {
            GameActive = true,
            GameBuild = "synthetic-build",
            SeasonId = "synthetic-season",
            CardPoolIdentity = "synthetic-pool",
            Gold = 10,
            MaxGold = 10,
            TavernTier = 1,
            TavernUpgradeCost = 5
        };
        var candidates = new CandidateGenerator(new ActionEnumerator()).Generate(state);
        var declaredCandidates = DeclareCandidates(candidates);
        ValidateDeclaredCandidates(declaredCandidates, candidates);
        var mismatchedCandidates = (JArray)declaredCandidates.DeepClone();
        ((JObject)mismatchedCandidates[0])["targetIndex"] = 999;
        try
        {
            ValidateDeclaredCandidates(mismatchedCandidates, candidates);
            throw new InvalidOperationException("self-test must reject a mismatched candidate declaration");
        }
        catch (InvalidDataException) { }
        var record = DecisionEventRecord.Create("synthetic-scenario-0001", "synthetic-game-0001",
            state, candidates);
        var legacy = new FakePolicy("legacy-v1.1.0", FallbackReason.None, PlannerStatus.NotRequested);
        var unified = new FakePolicy("unified-linear-v1", FallbackReason.None,
            PlannerStatus.Completed, ActionType.Upgrade);
        var baseline = legacy.Decide(Clone(state));
        var plannedActionKey = ActionDescriptor.CreateKey(new GameAction {
            Type = ActionType.Refresh, TargetIndex = -1
        });
        var candidateAdapter = new CandidateBenchmarkAdapter(candidateState =>
            new UnifiedDecisionDiagnostics(unified.Decide(candidateState),
                PlanningResult.Completed(plannedActionKey,
                    new List<string> { plannedActionKey }, 1, 1, 1)), legacy);
        var candidate = candidateAdapter.DecideWithDiagnostics(state);
        if (ActionDescriptor.CreateKey(baseline.SelectedAction)
            == candidate.PlanningResult.ActionKey)
            throw new InvalidOperationException("self-test candidate must not reuse legacy output");
        var candidatePrediction = Prediction(record, candidate, "synthetic-model",
            new string('b', 64));
        var productionSelectedActionDistinctFromPlannerDiagnostic =
            (string)candidatePrediction["selectedActionKey"]
                == (string)((JArray)candidatePrediction["topActionKeys"])[0]
            && (string)((JArray)candidatePrediction["bestCompletePath"])[0]
                != (string)candidatePrediction["selectedActionKey"];
        var fallbackUnified = new FakePolicy("unified-linear-v1", FallbackReason.LowConfidence,
            PlannerStatus.NotRequested, ActionType.Refresh);
        var fallbackCandidate = new CandidateBenchmarkAdapter(fallbackState =>
            new UnifiedDecisionDiagnostics(fallbackUnified.Decide(fallbackState),
                PlanningResult.NotRequested()), legacy).Decide(state);
        var explicitFallbackUsesLegacySurface = fallbackCandidate.PolicyId == "unified-linear-v1"
            && fallbackCandidate.FallbackReason == FallbackReason.LowConfidence
            && ActionDescriptor.CreateKey(fallbackCandidate.SelectedAction)
                == ActionDescriptor.CreateKey(baseline.SelectedAction);

        WriteLines(output, "baseline-predictions.jsonl", new[] {
            Prediction(record, baseline, "unknown", "unknown")
        });
        WriteLines(output, "candidate-predictions.jsonl", new[] {
            candidatePrediction
        });
        WriteJson(output, "latency.json", LatencyEvidence(
            new[] { new Scenario { ScenarioId = record.DecisionId, State = state } },
            legacy, candidateAdapter, "synthetic-model", new string('b', 64)));
        var safety = SyntheticSafety(record, state, legacy);
        WriteJson(output, "safety-probes.json", safety);

        var identityMismatchRejected = RejectsInvalidData(delegate {
            ValidateStateIdentity(state, "different-build", state.SeasonId, state.CardPoolIdentity);
        });
        var unknownEnumRejected = RejectsInvalidData(delegate {
            ParseDefinedEnum<FallbackReason>("999", "expected fallback reason");
        });
        var incompleteProbeSetRejected = RejectsInvalidData(delegate {
            ValidateExactProbeSet(new HashSet<string>(RequiredProbeIds.Take(6), StringComparer.Ordinal));
        });
        var latencyLegacy = new FakePolicy("legacy-v1.1.0", FallbackReason.None,
            PlannerStatus.NotRequested);
        var latencyUnified = new FakePolicy("unified-linear-v1", FallbackReason.LowConfidence,
            PlannerStatus.NotRequested, ActionType.Refresh);
        var latencyCandidate = new CandidateBenchmarkAdapter(latencyState =>
            new UnifiedDecisionDiagnostics(latencyUnified.Decide(latencyState),
                PlanningResult.NotRequested()), latencyLegacy);
        Measure(latencyCandidate.Decide, state);
        var candidateLatencyIncludesLegacyFallback = latencyUnified.CallCount == 10
            && latencyLegacy.CallCount == 10;
        var missingCandidateDecisionRejected = RejectsInvalidData(delegate {
            new CandidateBenchmarkAdapter(_ => null, latencyLegacy).Decide(state);
        });
        var modelUnavailableCandidateSourceRejected = RejectsInvalidData(delegate {
            ValidateProbeModelSource("model-unavailable", "candidate");
        });
        var candidateProbeNoneSourceRejected = RejectsInvalidData(delegate {
            ValidateProbeModelSource("low-confidence", "none");
        });
        var canonical = new JObject { { "value", 1 },
            { "nested", new JObject { { "value", 2 } } } };
        var topLevelTamper = (JObject)canonical.DeepClone();
        topLevelTamper["_approval"] = "tampered";
        var nestedTamper = (JObject)canonical.DeepClone();
        ((JObject)nestedTamper["nested"])["_approval"] = "tampered";
        var protocolUnderscoreFieldsChecksumBound = Sha256Hex(CanonicalJson(canonical))
                != Sha256Hex(CanonicalJson(topLevelTamper))
            && Sha256Hex(CanonicalJson(canonical)) != Sha256Hex(CanonicalJson(nestedTamper));
        var canonicalInteger = JObject.Parse("{\"marginScale\":1,\"threshold\":0.5}");
        var canonicalDecimal = JObject.Parse("{\"marginScale\":1.0,\"threshold\":5.0e-1}");
        var canonicalExponent = JObject.Parse("{\"marginScale\":1e0,\"threshold\":5E-1}");
        const string expectedNumericCanonical =
            "{\"marginScale\":1.0000000000000000e+0,\"threshold\":5.0000000000000000e-1}";
        var protocolNumericCanonicalizationCrossLanguage =
            string.Equals(CanonicalJson(canonicalInteger), expectedNumericCanonical,
                StringComparison.Ordinal)
            && string.Equals(CanonicalJson(canonicalDecimal), expectedNumericCanonical,
                StringComparison.Ordinal)
            && string.Equals(CanonicalJson(canonicalExponent), expectedNumericCanonical,
                StringComparison.Ordinal);
        var protocolUnsafeIntegerRejected = false;
        try {
            CanonicalJson(JObject.Parse("{\"threshold\":9007199254740992}"));
        }
        catch (InvalidDataException exception) {
            protocolUnsafeIntegerRejected = exception.Message.Contains("not safely representable");
        }
        if (!identityMismatchRejected || !unknownEnumRejected || !incompleteProbeSetRejected)
            throw new InvalidOperationException("self-test fail-closed contracts were not exercised");
        WriteJson(output, "self-test-contracts.json", new JObject {
            { "scenarioStateIdentityMismatchRejected", identityMismatchRejected },
            { "unknownSafetyEnumRejected", unknownEnumRejected },
            { "incompleteSafetyProbeSetRejected", incompleteProbeSetRejected },
            { "candidateOutputDistinctFromLegacy", true },
            { "explicitFallbackUsesLegacySurface", explicitFallbackUsesLegacySurface },
            { "candidateLatencyIncludesLegacyFallback", candidateLatencyIncludesLegacyFallback },
            { "missingCandidateDecisionRejected", missingCandidateDecisionRejected },
            { "productionSelectedActionDistinctFromPlannerDiagnostic",
                productionSelectedActionDistinctFromPlannerDiagnostic },
            { "modelUnavailableCandidateSourceRejected", modelUnavailableCandidateSourceRejected },
            { "candidateProbeNoneSourceRejected", candidateProbeNoneSourceRejected },
            { "protocolUnderscoreFieldsChecksumBound", protocolUnderscoreFieldsChecksumBound },
            { "protocolNumericCanonicalizationCrossLanguage",
                protocolNumericCanonicalizationCrossLanguage },
            { "protocolUnsafeIntegerRejected", protocolUnsafeIntegerRejected },
            { "orchestratedSafetyOutcomesObserved",
                safety["probes"].All(value => (bool)value["passed"]) }
        });
    }

    private static void RunProduction(IDictionary<string, string> args, SecureEvidenceDirectory output)
    {
        foreach (var required in new[] { "--data-root", "--scenarios", "--probes", "--model",
            "--evaluation-protocol" })
            if (!args.ContainsKey(required)) throw new ArgumentException(required + " is required");

        var inputPaths = new Dictionary<string, string>(StringComparer.Ordinal) {
            { "scenarios", args["--scenarios"] },
            { "probes", args["--probes"] },
            { "model", args["--model"] },
            { "evaluation-protocol", args["--evaluation-protocol"] }
        };
        using (var inputs = SecureInputSnapshot.Capture(args["--data-root"], inputPaths))
        {
            var scenarioBytes = inputs.GetBytes("scenarios");
            var probeBytes = inputs.GetBytes("probes");
            var modelBytes = inputs.GetBytes("model");
            var protocolBytes = inputs.GetBytes("evaluation-protocol");
            var context = LoadScenarios(scenarioBytes);
            var evaluationContext = LoadEvaluationContext(protocolBytes, scenarioBytes);
            DecisionPolicyBundle bundle;
            using (var stream = new MemoryStream(modelBytes, false))
            {
                bundle = DecisionPolicyFactory.CreateForEffectEvaluation(new DecisionEngine(), stream,
                    new EvaluationSeasonFeatureExtension(context.GameBuild, context.SeasonId,
                        context.CardPoolIdentity), evaluationContext);
            }
            var scenarios = context.Scenarios;
            var candidateAdapter = new CandidateBenchmarkAdapter(
                state => ((UnifiedRankingPolicy)bundle.Unified)
                    .DecideWithDiagnostics(state), bundle.Legacy);
            var baselineRows = new List<JObject>();
            var candidateRows = new List<JObject>();

            foreach (var scenario in scenarios)
            {
                var detached = Clone(scenario.State);
                var candidates = bundle.Candidates.Generate(detached)
                    .OrderBy(value => value.ActionKey, StringComparer.Ordinal).ToList();
                var record = DecisionEventRecord.Create(scenario.ScenarioId, scenario.GameId,
                    detached, candidates);
                if (!string.Equals(record.StateFingerprint, scenario.StateFingerprint, StringComparison.Ordinal)
                    || !string.Equals(record.CandidateFingerprint, scenario.CandidateFingerprint, StringComparison.Ordinal))
                    throw new InvalidDataException("scenario fingerprint mismatch: " + scenario.ScenarioId);

                baselineRows.Add(Prediction(record, bundle.Legacy.Decide(Clone(detached)),
                    "unknown", "unknown"));
                candidateRows.Add(Prediction(record,
                    candidateAdapter.DecideWithDiagnostics(detached),
                    bundle.ModelId, bundle.ModelPayloadSha256));
            }

            WriteLines(output, "baseline-predictions.jsonl", baselineRows);
            WriteLines(output, "candidate-predictions.jsonl", candidateRows);
            WriteJson(output, "latency.json", LatencyEvidence(scenarios,
                bundle.Legacy, candidateAdapter, bundle.ModelId, bundle.ModelPayloadSha256));
            WriteJson(output, "safety-probes.json", RunSafetyProbes(probeBytes, bundle));
        }
    }

    private static BenchmarkContext LoadScenarios(byte[] bytes)
    {
        var result = new List<Scenario>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var pairs = new HashSet<string>(StringComparer.Ordinal);
        string build = null, season = null, pool = null;
        foreach (var line in ReadNonBlankLines(bytes))
        {
            var row = JObject.Parse(line);
            if ((int)row["scenarioSchemaVersion"] != 1) throw new InvalidDataException("scenario schema mismatch");
            var scenario = new Scenario
            {
                ScenarioId = Required(row, "scenarioId"),
                GameId = Required(row, "gameId"),
                StateFingerprint = Fingerprint(row, "stateFingerprint"),
                CandidateFingerprint = Fingerprint(row, "candidateFingerprint"),
                State = row["state"] == null ? null : row["state"].ToObject<GameState>()
            };
            if (!ids.Add(scenario.ScenarioId)) throw new InvalidDataException("duplicate scenario id");
            if (!pairs.Add(scenario.StateFingerprint + ":" + scenario.CandidateFingerprint))
                throw new InvalidDataException("duplicate scenario fingerprint pair");
            EnforceIdentity(ref build, Required(row, "buildIdentity"), "build");
            EnforceIdentity(ref season, Required(row, "seasonIdentity"), "season");
            EnforceIdentity(ref pool, Required(row, "cardPoolIdentity"), "card pool");
            Normalize(scenario.State);
            ValidateStateIdentity(scenario.State, build, season, pool);
            result.Add(scenario);
        }
        if (result.Count == 0) throw new InvalidDataException("scenario evidence is empty");
        return new BenchmarkContext {
            Scenarios = result.OrderBy(value => value.ScenarioId, StringComparer.Ordinal).ToList(),
            GameBuild = build,
            SeasonId = season,
            CardPoolIdentity = pool
        };
    }

    private static EffectEvaluationContext LoadEvaluationContext(byte[] protocolBytes,
        byte[] scenarioBytes)
    {
        JObject protocol;
        using (var stream = new MemoryStream(protocolBytes, false))
        using (var textReader = new StreamReader(stream, Utf8NoBom, true))
        using (var jsonReader = new JsonTextReader(textReader) { DateParseHandling = DateParseHandling.None })
        {
            protocol = JObject.Load(jsonReader, new JsonLoadSettings {
                DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
            });
            if (jsonReader.Read()) throw new InvalidDataException("evaluation protocol has trailing content");
        }
        if ((int?)protocol["protocolSchemaVersion"] != 1)
            throw new InvalidDataException("evaluation protocol schema mismatch");

        var declaredProtocolSha = Fingerprint(protocol, "protocolSha256");
        var canonicalProtocol = (JObject)protocol.DeepClone();
        canonicalProtocol.Remove("protocolSha256");
        var actualProtocolSha = Sha256Hex(CanonicalJson(canonicalProtocol));
        if (!string.Equals(declaredProtocolSha, actualProtocolSha, StringComparison.Ordinal))
            throw new InvalidDataException("evaluation protocol checksum mismatch");

        var declaredHoldoutSha = Fingerprint(protocol, "holdoutChecksumSha256");
        var actualHoldoutSha = Sha256Hex(scenarioBytes);
        if (!string.Equals(declaredHoldoutSha, actualHoldoutSha, StringComparison.Ordinal))
            throw new InvalidDataException("evaluation holdout checksum mismatch");

        var calibrationRow = protocol["confidenceCalibration"] as JObject;
        var approvalRow = protocol["calibrationApproval"] as JObject;
        if (calibrationRow == null || approvalRow == null)
            throw new InvalidDataException("evaluation calibration context is missing");
        var planningToken = calibrationRow["planningThreshold"];
        double? planningThreshold = planningToken == null || planningToken.Type == JTokenType.Null
            ? (double?)null : RequiredFiniteDouble(calibrationRow, "planningThreshold");
        var calibration = new ConfidenceCalibration(
            Required(calibrationRow, "status"),
            Required(calibrationRow, "gameBuild"),
            Required(calibrationRow, "seasonId"),
            Required(calibrationRow, "cardPoolIdentity"),
            Required(calibrationRow, "trainingSetIdentity"),
            Required(calibrationRow, "holdoutIdentity"),
            Required(calibrationRow, "calibrationIdentity"),
            Required(calibrationRow, "modelId"),
            RequiredFiniteDouble(calibrationRow, "marginScale"),
            RequiredFiniteDouble(calibrationRow, "semanticCoverageFloor"),
            RequiredFiniteDouble(calibrationRow, "confidenceThreshold"),
            planningThreshold);
        var calibrationApproval = new ConfidenceCalibrationApprovalContext(
            Required(approvalRow, "holdoutIdentity"),
            Required(approvalRow, "calibrationIdentity"),
            Required(approvalRow, "modelId"));

        return new EffectEvaluationContext(
            Required(protocol, "protocolId"), Required(protocol, "protocolVersion"),
            Required(protocol, "approvalStatus"), Required(protocol, "approvedBy"),
            RequiredUtc(protocol, "approvedAtUtc"), RequiredUtc(protocol, "frozenAtUtc"),
            RequiredUtc(protocol, "holdoutOpenedAtUtc"),
            declaredProtocolSha, Required(protocol, "benchmarkId"),
            Required(protocol, "baselinePolicyId"), Required(protocol, "candidatePolicyId"),
            Required(protocol, "candidateModelId"),
            Fingerprint(protocol, "candidateModelPayloadSha256"),
            Required(protocol, "holdoutIdentity"), declaredHoldoutSha, actualHoldoutSha,
            Required(protocol, "buildIdentity"), Required(protocol, "seasonIdentity"),
            Required(protocol, "cardPoolIdentity"), Required(protocol, "trainingSetIdentity"),
            Required(protocol, "seasonRange"), calibration, calibrationApproval);
    }

    private static DateTime RequiredUtc(JObject row, string name)
    {
        var value = Required(row, name);
        DateTimeOffset parsed;
        if (!value.EndsWith("Z", StringComparison.Ordinal)
            || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsed))
            throw new InvalidDataException(name + " must be an explicit UTC timestamp");
        return parsed.UtcDateTime;
    }

    private static double RequiredFiniteDouble(JObject row, string name)
    {
        var token = row[name];
        if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float))
            throw new InvalidDataException(name + " must be numeric");
        var value = (double)token;
        if (double.IsNaN(value) || double.IsInfinity(value))
            throw new InvalidDataException(name + " must be finite");
        return value;
    }

    private static string CanonicalJson(JToken token)
    {
        var objectValue = token as JObject;
        if (objectValue != null)
            return "{" + string.Join(",", objectValue.Properties()
                .OrderBy(property => property.Name, StringComparer.Ordinal)
                .Select(property => JsonConvert.SerializeObject(property.Name) + ":"
                    + CanonicalJson(property.Value))) + "}";
        var arrayValue = token as JArray;
        if (arrayValue != null)
            return "[" + string.Join(",", arrayValue.Select(CanonicalJson)) + "]";
        if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
        {
            var number = token.Value<double>();
            if (double.IsNaN(number) || double.IsInfinity(number)
                || (Math.Truncate(number) == number && Math.Abs(number) > 9007199254740991d))
                throw new InvalidDataException(
                    "evaluation protocol number is not safely representable");
            if (number == 0d) number = 0d;
            var formatted = number.ToString("E16", CultureInfo.InvariantCulture);
            var exponentIndex = formatted.IndexOf('E');
            var exponent = int.Parse(formatted.Substring(exponentIndex + 1),
                NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            return formatted.Substring(0, exponentIndex) + "e"
                + (exponent >= 0 ? "+" : "")
                + exponent.ToString(CultureInfo.InvariantCulture);
        }
        return token.ToString(Formatting.None);
    }

    private static IList<string> ReadNonBlankLines(byte[] bytes)
    {
        var lines = new List<string>();
        using (var stream = new MemoryStream(bytes, false))
        using (var reader = new StreamReader(stream, Utf8NoBom, true))
        {
            string line;
            while ((line = reader.ReadLine()) != null)
                if (!string.IsNullOrWhiteSpace(line)) lines.Add(line);
        }
        return lines;
    }

    private static string Sha256Hex(string value)
    {
        using (var sha = SHA256.Create())
            return string.Concat(sha.ComputeHash(Utf8NoBom.GetBytes(value))
                .Select(item => item.ToString("x2")));
    }

    private static string Sha256Hex(byte[] value)
    {
        using (var sha = SHA256.Create())
            return string.Concat(sha.ComputeHash(value).Select(item => item.ToString("x2")));
    }

    private static void Normalize(GameState state)
    {
        if (state == null) throw new InvalidDataException("scenario state is missing");
        if (state.AvailableTribes == null) state.AvailableTribes = new HashSet<string>();
        if (state.BoardMinions == null) state.BoardMinions = new List<MinionData>();
        if (state.HandMinions == null) state.HandMinions = new List<MinionData>();
        if (state.ShopMinions == null) state.ShopMinions = new List<MinionData>();
        if (state.HeroPowers == null) state.HeroPowers = new List<HeroPowerState>();
        if (state.TrinketOffer == null) state.TrinketOffer = new List<TrinketOption>();
        if (state.DiscoverOptions == null) state.DiscoverOptions = new List<TrinketOption>();
    }

    private static JObject Prediction(DecisionEventRecord record, DecisionResult result,
        string modelId, string modelSha)
    {
        return Prediction(record, new UnifiedDecisionDiagnostics(result,
            PlanningResult.NotRequested()), modelId, modelSha);
    }

    private static JObject Prediction(DecisionEventRecord record,
        UnifiedDecisionDiagnostics diagnostics, string modelId, string modelSha)
    {
        if (diagnostics == null || diagnostics.Decision == null
            || diagnostics.PlanningResult == null)
            throw new InvalidDataException("prediction diagnostics are missing");
        var result = diagnostics.Decision;
        var planning = diagnostics.PlanningResult;
        var top = new JArray((result.RankedActions ?? new List<RankedAction>())
            .Where(value => value != null).Take(3).Select(value => value.ActionKey ?? ""));
        var selected = result.SelectedAction == null
            ? "" : ActionDescriptor.CreateKey(result.SelectedAction);
        return new JObject {
            { "evidenceSchemaVersion", 1 }, { "benchmarkId", BenchmarkId },
            { "scenarioId", record.DecisionId }, { "gameId", record.GameId },
            { "stateFingerprint", record.StateFingerprint },
            { "candidateFingerprint", record.CandidateFingerprint },
            { "policyId", result.PolicyId ?? "" },
            { "modelId", string.IsNullOrWhiteSpace(modelId) ? "unknown" : modelId },
            { "modelPayloadSha256", string.IsNullOrWhiteSpace(modelSha) ? "unknown" : modelSha },
            { "selectedActionKey", selected },
            { "topActionKeys", top }, { "confidence", result.Confidence },
            { "plannerStatus", planning.Status.ToString() },
            { "bestCompletePath", new JArray(planning.BestCompletePath ?? new List<string>()) },
            { "fallbackReason", result.FallbackReason.ToString() }
        };
    }

    private static DecisionResult CandidateEvidenceSurface(DecisionResult unified,
        IDecisionPolicy legacy, GameState state)
    {
        if (unified == null) throw new InvalidDataException("candidate decision is missing");
        if (unified.FallbackReason == FallbackReason.None) return unified;

        var fallback = legacy.Decide(state);
        return new DecisionResult("unified-linear-v1", fallback.SelectedAction,
            fallback.RankedActions, unified.Confidence, unified.PlannerStatus,
            unified.FallbackReason, unified.Explanation);
    }

    private static JObject LatencyEvidence(IEnumerable<Scenario> scenarios,
        IDecisionPolicy baseline, CandidateBenchmarkAdapter candidate, string candidateModelId,
        string candidateModelPayloadSha256)
    {
        var rows = new JArray();
        foreach (var scenario in scenarios.OrderBy(value => value.ScenarioId, StringComparer.Ordinal))
        {
            for (var index = 0; index < 3; index++)
            {
                baseline.Decide(Clone(scenario.State));
                candidate.Decide(scenario.State);
            }
            var baselineSamples = Measure(state => baseline.Decide(Clone(state)), scenario.State);
            var candidateSamples = Measure(candidate.Decide, scenario.State);
            rows.Add(new JObject {
                { "scenarioId", scenario.ScenarioId },
                { "baselineSamplesMilliseconds", new JArray(baselineSamples) },
                { "candidateSamplesMilliseconds", new JArray(candidateSamples) },
                { "baselineP50Milliseconds", Percentile(baselineSamples, 0.50) },
                { "baselineP95Milliseconds", Percentile(baselineSamples, 0.95) },
                { "baselineP99Milliseconds", Percentile(baselineSamples, 0.99) },
                { "candidateP50Milliseconds", Percentile(candidateSamples, 0.50) },
                { "candidateP95Milliseconds", Percentile(candidateSamples, 0.95) },
                { "candidateP99Milliseconds", Percentile(candidateSamples, 0.99) }
            });
        }
        return new JObject {
            { "latencyEvidenceSchemaVersion", 1 }, { "benchmarkId", BenchmarkId },
            { "baselinePolicyId", baseline.PolicyId },
            { "candidatePolicyId", candidate.PolicyId },
            { "candidateModelId", candidateModelId },
            { "candidateModelPayloadSha256", candidateModelPayloadSha256 },
            { "measurementsPerPolicyPerScenario", 10 }, { "scenarios", rows }
        };
    }

    private static double[] Measure(Func<GameState, DecisionResult> decide, GameState state)
    {
        var values = new double[10];
        for (var index = 0; index < values.Length; index++)
        {
            var start = Stopwatch.GetTimestamp();
            decide(state);
            values[index] = Math.Max(0d,
                (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency);
        }
        return values;
    }

    private static double Percentile(double[] values, double percentile)
    {
        var sorted = values.OrderBy(value => value).ToArray();
        var index = (int)Math.Ceiling(percentile * sorted.Length) - 1;
        return sorted[Math.Max(0, Math.Min(sorted.Length - 1, index))];
    }

    private static JObject SyntheticSafety(DecisionEventRecord record, GameState state,
        IDecisionPolicy legacy)
    {
        var rows = new JArray();
        foreach (var id in RequiredProbeIds)
        {
            var expectedFallback = RequiredProbeFallbacks[id];
            var expectedPlanner = id == "planner-timeout" ? PlannerStatus.TimedOut : PlannerStatus.NotRequested;
            var unified = new FakePolicy("unified-linear-v1", expectedFallback,
                expectedPlanner, ActionType.Refresh);
            var decision = unified.Decide(Clone(state));
            var selected = ActionDescriptor.CreateKey(decision.SelectedAction);
            var diagnosticPathAction = ActionDescriptor.CreateKey(new GameAction {
                Type = ActionType.Upgrade, TargetIndex = -1
            });
            var planning = id == "planner-timeout"
                ? PlanningResult.TimedOut(diagnosticPathAction,
                    new List<string> { diagnosticPathAction }, 1, 1, 1)
                : PlanningResult.NotRequested();
            AddProbe(rows, id, record, id == "model-unavailable" ? "none" : "candidate",
                expectedFallback, expectedPlanner, decision, planning);
        }
        ValidateExactProbeSet(new HashSet<string>(rows.Select(value => (string)value["probeId"]),
            StringComparer.Ordinal));
        return SafetyObject(new JArray(rows.OrderBy(value => (string)value["probeId"], StringComparer.Ordinal)));
    }

    private static JObject RunSafetyProbes(byte[] bytes, DecisionPolicyBundle candidateBundle)
    {
        var rows = new JArray();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in ReadNonBlankLines(bytes))
        {
            var probe = JObject.Parse(line);
            if ((int)probe["probeSchemaVersion"] != 1) throw new InvalidDataException("probe schema mismatch");
            var id = Required(probe, "probeId");
            if (!ids.Add(id)) throw new InvalidDataException("duplicate probe id");
            if (!RequiredProbeFallbacks.ContainsKey(id)) throw new InvalidDataException("unknown safety probe id");
            var gameId = Required(probe, "gameId");
            if (!gameId.StartsWith("synthetic-", StringComparison.Ordinal))
                throw new InvalidDataException("probe game identity must be synthetic");
            if (probe["path"] != null || probe["payload"] != null)
                throw new InvalidDataException("probe path and payload fields are forbidden");
            var modelSource = Required(probe, "modelSource");
            ValidateProbeModelSource(id, modelSource);
            var declaredCandidates = probe["candidates"] as JArray;
            if (declaredCandidates == null) throw new InvalidDataException("probe candidates are missing");
            var state = probe["state"] == null ? null : probe["state"].ToObject<GameState>();
            Normalize(state);
            var generator = candidateBundle.Candidates;
            var generatedCandidates = generator.Generate(state);
            ValidateDeclaredCandidates(declaredCandidates, generatedCandidates);
            var record = DecisionEventRecord.Create(id, gameId, state, generatedCandidates);
            if (record.StateFingerprint != Fingerprint(probe, "stateFingerprint")
                || record.CandidateFingerprint != Fingerprint(probe, "candidateFingerprint"))
                throw new InvalidDataException("probe fingerprint mismatch: " + id);
            var bundle = modelSource == "none"
                ? DecisionPolicyFactory.Create(new DecisionEngine(), null, ProductionSeasonFeatureExtension.Instance)
                : candidateBundle;
            var expectedFallback = Required(probe, "expectedFallbackReason");
            var expectedPlanner = Required(probe, "expectedPlannerStatus");
            var parsedFallback = ParseDefinedEnum<FallbackReason>(expectedFallback, "expected fallback reason");
            var parsedPlanner = ParseDefinedEnum<PlannerStatus>(expectedPlanner, "expected planner status");
            var requiredPlanner = id == "planner-timeout" ? PlannerStatus.TimedOut : PlannerStatus.NotRequested;
            if (parsedFallback != RequiredProbeFallbacks[id] || parsedPlanner != requiredPlanner)
                throw new InvalidDataException("probe expectation does not match required probe ID: " + id);
            DecisionResult decision;
            PlanningResult planning;
            if (modelSource == "none")
            {
                decision = bundle.Unified.Decide(Clone(state));
                planning = PlanningResult.NotRequested();
            }
            else
            {
                var diagnostics = ((UnifiedRankingPolicy)bundle.Unified)
                    .DecideWithDiagnostics(Clone(state));
                decision = diagnostics.Decision;
                planning = diagnostics.PlanningResult;
            }
            AddProbe(rows, id, record, modelSource, parsedFallback, parsedPlanner,
                decision, planning);
        }
        ValidateExactProbeSet(ids);
        var sortedRows = new JArray();
        foreach (var row in rows.OrderBy(value => (string)value["probeId"], StringComparer.Ordinal)) sortedRows.Add(row);
        return SafetyObject(sortedRows);
    }

    private static void ValidateDeclaredCandidates(JArray declared,
        IEnumerable<ActionDescriptor> generated)
    {
        var declaredKeys = new List<string>();
        foreach (var token in declared)
        {
            var row = token as JObject;
            if (row == null) throw new InvalidDataException("probe candidate is invalid");
            var typeName = Required(row, "type");
            ActionType type;
            if (!Enum.TryParse(typeName, false, out type) || !Enum.IsDefined(typeof(ActionType), type))
                throw new InvalidDataException("probe candidate action type is invalid");
            var targetToken = row["targetIndex"];
            if (targetToken == null || targetToken.Type != JTokenType.Integer)
                throw new InvalidDataException("probe candidate targetIndex is invalid");
            var cardId = row["cardId"];
            var purchaseSource = row["purchaseSource"];
            if (cardId == null || cardId.Type != JTokenType.String
                || purchaseSource == null || purchaseSource.Type != JTokenType.String)
                throw new InvalidDataException("probe candidate identity is invalid");
            declaredKeys.Add(ActionDescriptor.CreateKey(new GameAction {
                Type = type,
                TargetIndex = (int)targetToken,
                CardId = (string)cardId,
                PurchaseSource = (string)purchaseSource
            }));
        }

        var orderedDeclared = declaredKeys.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        if (orderedDeclared.Distinct(StringComparer.Ordinal).Count() != orderedDeclared.Length)
            throw new InvalidDataException("duplicate probe candidate action key");
        var orderedGenerated = (generated ?? Enumerable.Empty<ActionDescriptor>())
            .Where(value => value != null)
            .Select(value => value.ActionKey ?? "")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (!orderedDeclared.SequenceEqual(orderedGenerated, StringComparer.Ordinal))
            throw new InvalidDataException("probe candidate declaration mismatch");
    }

    private static JArray DeclareCandidates(IEnumerable<ActionDescriptor> candidates)
    {
        var rows = new JArray();
        foreach (var candidate in candidates ?? Enumerable.Empty<ActionDescriptor>())
        {
            var action = candidate.Action;
            rows.Add(new JObject {
                { "type", action.Type.ToString() }, { "targetIndex", action.TargetIndex },
                { "cardId", action.CardId ?? "" }, { "purchaseSource", action.PurchaseSource ?? "" }
            });
        }
        return rows;
    }

    private static void AddProbe(JArray rows, string id, DecisionEventRecord record, string source,
        FallbackReason expectedFallback, PlannerStatus expectedPlanner, DecisionResult decision,
        PlanningResult planning)
    {
        if (decision == null || planning == null)
            throw new InvalidDataException("safety probe diagnostics are missing");
        ValidateProbeModelSource(id, source);
        var selected = decision.SelectedAction == null
            ? "" : ActionDescriptor.CreateKey(decision.SelectedAction);
        var path = planning.BestCompletePath ?? new List<string>();
        rows.Add(new JObject {
            { "probeId", id }, { "stateFingerprint", record.StateFingerprint },
            { "candidateFingerprint", record.CandidateFingerprint }, { "modelSource", source },
            { "expectedFallbackReason", expectedFallback.ToString() },
            { "actualFallbackReason", decision.FallbackReason.ToString() },
            { "expectedPlannerStatus", expectedPlanner.ToString() },
            { "actualPlannerStatus", planning.Status.ToString() },
            { "actualSelectedActionKey", selected },
            { "actualTopActionKeys", string.IsNullOrWhiteSpace(selected) ? new JArray() : new JArray(selected) },
            { "actualBestCompletePath", new JArray(path) },
            { "passed", expectedFallback == decision.FallbackReason
                && expectedPlanner == planning.Status
                && string.Equals(source, ExpectedProbeModelSource(id), StringComparison.Ordinal)
                && (id != "planner-timeout" || (!string.IsNullOrWhiteSpace(selected)
                    && path.Count > 0)) }
        });
    }

    private static string ExpectedProbeModelSource(string id)
    {
        if (!RequiredProbeFallbacks.ContainsKey(id))
            throw new InvalidDataException("unknown safety probe id");
        return id == "model-unavailable" ? "none" : "candidate";
    }

    private static void ValidateProbeModelSource(string id, string source)
    {
        if (!string.Equals(source, ExpectedProbeModelSource(id), StringComparison.Ordinal))
            throw new InvalidDataException("probe model source does not match required probe ID: " + id);
    }

    private static JObject SafetyObject(JArray rows)
    {
        return new JObject {
            { "safetyEvidenceSchemaVersion", 1 }, { "benchmarkId", BenchmarkId }, { "probes", rows }
        };
    }

    private static void ValidateStateIdentity(GameState state, string build, string season,
        string cardPoolIdentity)
    {
        if (!string.Equals(state.GameBuild, build, StringComparison.Ordinal)
            || !string.Equals(state.SeasonId, season, StringComparison.Ordinal)
            || !string.Equals(state.CardPoolIdentity, cardPoolIdentity, StringComparison.Ordinal))
            throw new InvalidDataException("scenario state identity mismatch");
    }

    private static bool RejectsInvalidData(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (InvalidDataException)
        {
            return true;
        }
    }

    private static T ParseDefinedEnum<T>(string value, string name) where T : struct
    {
        T parsed;
        if (!Enum.TryParse(value, false, out parsed) || !Enum.IsDefined(typeof(T), parsed))
            throw new InvalidDataException("unknown " + name);
        return parsed;
    }

    private static void ValidateExactProbeSet(ISet<string> ids)
    {
        if (ids.Count != RequiredProbeIds.Length
            || RequiredProbeIds.Any(id => !ids.Contains(id)))
            throw new InvalidDataException("safety evidence must contain the exact required probe IDs");
    }

    private static string Required(JObject row, string name)
    {
        var value = (string)row[name];
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException(name + " identity is missing");
        return value;
    }

    private static string Fingerprint(JObject row, string name)
    {
        var value = Required(row, name);
        if (value.Length != 64 || value.Any(character => !"0123456789abcdef".Contains(character)))
            throw new InvalidDataException(name + " is invalid");
        return value;
    }

    private static void EnforceIdentity(ref string expected, string actual, string name)
    {
        if (expected == null) expected = actual;
        else if (!string.Equals(expected, actual, StringComparison.Ordinal))
            throw new InvalidDataException("mixed " + name + " identity");
    }

    private static GameState Clone(GameState state)
    {
        var clone = JsonConvert.DeserializeObject<GameState>(JsonConvert.SerializeObject(state));
        Normalize(clone);
        return clone;
    }

    private static void WriteLines(SecureEvidenceDirectory output, string fileName,
        IEnumerable<JObject> rows)
    {
        var content = string.Join("\n", rows.Select(value => value.ToString(Formatting.None))) + "\n";
        output.WriteAllText(fileName, content);
    }

    private static void WriteJson(SecureEvidenceDirectory output, string fileName, JObject value)
    {
        output.WriteAllText(fileName, value.ToString(Formatting.None) + "\n");
    }
}
