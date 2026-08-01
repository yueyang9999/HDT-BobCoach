using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BobCoach.Engine;
using BobCoach.Engine.Decision;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BobCoach.Engine
{
    public enum ActionType
    {
        BuyMinion, BuySpell, Refresh, Upgrade, SellMinion, FreezeShop,
        UseHeroPower, PickTrinket, PickDiscover, SendGoldToTeammate,
    }

    public sealed class GameAction
    {
        public ActionType Type;
        public int TargetIndex;
        public string CardId = "";
        public string PurchaseSource = "";
        public string Description = "";
    }

    public sealed class GameState { }

    public sealed class LevelUpSuggestion
    {
        public LevelUpSuggestion(string recommendation, string reason) { }
    }

    public interface IDecisionProvider
    {
        GameAction GetBestAction(GameState state);
        List<int> GetRecommendedBuyIndices(GameState state, int topN = 3);
        LevelUpSuggestion GetLevelUpSuggestion(GameState state);
    }

    public sealed class DecisionEngine : IDecisionProvider
    {
        public GameAction GetBestAction(GameState state)
        {
            return new GameAction { Type = ActionType.Refresh, TargetIndex = -1 };
        }

        public List<int> GetRecommendedBuyIndices(GameState state, int topN = 3)
        {
            return new List<int>();
        }

        public LevelUpSuggestion GetLevelUpSuggestion(GameState state)
        {
            return new LevelUpSuggestion("", "");
        }
    }
}

namespace BobCoach.Engine.Decision
{
    public sealed class ActionDescriptor
    {
        public static string CreateKey(GameAction action)
        {
            return string.Join("|", action.Type, action.TargetIndex,
                action.CardId ?? "", action.PurchaseSource ?? "");
        }
    }

    public sealed class ActionEnumerator { }

    public sealed class CandidateGenerator
    {
        public ActionEnumerator Enumerator { get; private set; }

        public CandidateGenerator(ActionEnumerator enumerator)
        {
            Enumerator = enumerator;
        }
    }

    public sealed class PlanningResult
    {
        public PlannerStatus Status { get; private set; }

        private PlanningResult(PlannerStatus status) { Status = status; }

        public static PlanningResult NotRequested()
        {
            return new PlanningResult(PlannerStatus.NotRequested);
        }

        public static PlanningResult Completed()
        {
            return new PlanningResult(PlannerStatus.Completed);
        }
    }

    public sealed class ConfidenceCalibration
    {
        public string Status { get; private set; }
        public string GameBuild { get; private set; }
        public string SeasonId { get; private set; }
        public string CardPoolIdentity { get; private set; }
        public string TrainingSetIdentity { get; private set; }
        public string HoldoutIdentity { get; private set; }
        public string CalibrationIdentity { get; private set; }
        public string ModelId { get; private set; }
    }

    public sealed class ConfidenceCalibrationApprovalContext
    {
        public string HoldoutIdentity { get; private set; }
        public string CalibrationIdentity { get; private set; }
        public string ModelId { get; private set; }
    }

    public sealed class UnifiedRankingPolicy : IDecisionPolicy
    {
        public CandidateGenerator Candidates { get; private set; }
        public string PolicyId { get { return "unified-linear-v1"; } }

        private UnifiedRankingPolicy(CandidateGenerator candidates)
        {
            Candidates = candidates;
        }

        public static UnifiedRankingPolicy CreateProduction(RankingModelPayload model,
            CandidateGenerator candidates, ISeasonFeatureExtension seasonExtension)
        {
            if (seasonExtension is InvalidDependencyExtension)
                throw new InvalidOperationException("deliberate composition failure");
            if (!string.Equals(model.GameBuild, seasonExtension.GameBuild, StringComparison.Ordinal)
                || !string.Equals(model.SeasonId, seasonExtension.SeasonId, StringComparison.Ordinal)
                || !string.Equals(model.CardPoolIdentity, seasonExtension.CardPoolIdentity,
                    StringComparison.Ordinal))
                throw new ModelValidationException("identity mismatch");
            return new UnifiedRankingPolicy(candidates);
        }

        internal static UnifiedRankingPolicy CreateForEffectEvaluation(
            RankingModelPayload model, CandidateGenerator candidates,
            ISeasonFeatureExtension seasonExtension, ConfidenceCalibration calibration,
            ConfidenceCalibrationApprovalContext approvalContext)
        {
            return new UnifiedRankingPolicy(candidates);
        }

        public DecisionResult Decide(GameState state)
        {
            return UnavailableDecisionPolicy.For(FallbackReason.ModelUnavailable).Decide(state);
        }
    }

    internal sealed class ValidSeasonExtension : ISeasonFeatureExtension
    {
        public string GameBuild { get { return "build-task11"; } }
        public string SeasonId { get { return "season-task11"; } }
        public string CardPoolIdentity { get { return "pool-task11"; } }

        public bool TryEvaluate(GameState state, ActionDescriptor action, GameState nextState,
            out double value)
        {
            value = 0d;
            return false;
        }
    }

    internal sealed class InvalidDependencyExtension : ISeasonFeatureExtension
    {
        public string GameBuild { get { return "build-task11"; } }
        public string SeasonId { get { return "season-task11"; } }
        public string CardPoolIdentity { get { return "pool-task11"; } }

        public bool TryEvaluate(GameState state, ActionDescriptor action, GameState nextState,
            out double value)
        {
            value = 0d;
            return false;
        }
    }

    internal sealed class MismatchedSeasonExtension : ISeasonFeatureExtension
    {
        public string GameBuild { get { return "wrong-build"; } }
        public string SeasonId { get { return "season-task11"; } }
        public string CardPoolIdentity { get { return "pool-task11"; } }

        public bool TryEvaluate(GameState state, ActionDescriptor action, GameState nextState,
            out double value)
        {
            value = 0d;
            return false;
        }
    }

    internal sealed class ApprovedSeasonExtension : ISeasonFeatureExtension,
        IRankingModelRuntimeContextProvider
    {
        private readonly RankingModelRuntimeContext _runtimeContext;

        public string GameBuild { get { return "build-task11"; } }
        public string SeasonId { get { return "season-task11"; } }
        public string CardPoolIdentity { get { return "pool-task11"; } }

        public ApprovedSeasonExtension(RankingModelRuntimeContext runtimeContext)
        {
            _runtimeContext = runtimeContext;
        }

        public RankingModelRuntimeContext GetRuntimeContext()
        {
            return _runtimeContext;
        }

        public bool TryEvaluate(GameState state, ActionDescriptor action, GameState nextState,
            out double value)
        {
            value = 0d;
            return false;
        }
    }

    internal sealed class ThrowingRuntimeContextExtension : ISeasonFeatureExtension,
        IRankingModelRuntimeContextProvider
    {
        private readonly InvalidOperationException _failure;

        public string GameBuild { get { return "build-task11"; } }
        public string SeasonId { get { return "season-task11"; } }
        public string CardPoolIdentity { get { return "pool-task11"; } }

        public ThrowingRuntimeContextExtension(InvalidOperationException failure)
        {
            _failure = failure;
        }

        public RankingModelRuntimeContext GetRuntimeContext()
        {
            throw _failure;
        }

        public bool TryEvaluate(GameState state, ActionDescriptor action, GameState nextState,
            out double value)
        {
            value = 0d;
            return false;
        }
    }
}

internal static class AlgorithmExplanationBehavior
{
    private static string _fixturePath;

    private static int Main(string[] args)
    {
        try
        {
            _fixturePath = args[0];
            ExplanationUsesOnlyLargestKnownContributions();
            ExplanationReflectsContributionDirection();
            ExplanationIgnoresZeroAndUnknownContributions();
            ExplanationIgnoresPlanningResult();
            FactorySharesCandidateGeneratorAndReportsIdentity();
            FactoryFailsClosedForExpectedModelAbsence();
            FactoryRequiresExplicitRuntimeApproval();
            FactoryThreeParameterEntryUsesIndependentRuntimeApproval();
            FactoryPreservesProgrammerAndStreamStateErrors();
            Console.WriteLine("PASS explanations reflect score contributions only");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL " + ex);
            return 1;
        }
    }

    private static void ExplanationUsesOnlyLargestKnownContributions()
    {
        var factors = new[]
        {
            new DecisionFactor("board_power_delta", 1.2d),
            new DecisionFactor("remaining_gold", 0.8d),
            new DecisionFactor("unknown_effect_count", -0.6d),
            new DecisionFactor("turn", 9.0d),
            new DecisionFactor("not_in_schema", 100d),
        };
        var text = new ExplanationBuilder().Build(Ranked(factors), PlanningResult.NotRequested());

        AssertContains(text, "board power", "largest positive factor");
        AssertContains(text, "remaining gold", "second factor");
        AssertContains(text, "uncertain effect", "largest risk factor");
        AssertNotContains(text, "turn", "non-player-relevant factor");
        AssertNotContains(text, "not_in_schema", "unknown factor");
        AssertNotContains(text, "1.2", "raw model number");
        AssertEqual(2, text.Count(ch => ch == ';'), "at most three reasons");
    }

    private static void ExplanationIgnoresPlanningResult()
    {
        var withSpace = new ExplanationBuilder().Build(Ranked(new[]
        {
            new DecisionFactor("board_power_delta", 2d),
            new DecisionFactor("remaining_gold", 1d),
        }), PlanningResult.Completed());
        AssertNotContains(withSpace, "follow-up path", "planning is not a score contribution");

        var full = new ExplanationBuilder().Build(Ranked(new[]
        {
            new DecisionFactor("board_power_delta", 3d),
            new DecisionFactor("remaining_gold", 2d),
            new DecisionFactor("unknown_effect_count", -1d),
        }), PlanningResult.Completed());
        AssertNotContains(full, "follow-up path", "planning cannot displace a top contribution");
        AssertEqual("", new ExplanationBuilder().Build(null, PlanningResult.Completed()),
            "missing selection has no invented reason");
    }

    private static void ExplanationReflectsContributionDirection()
    {
        var negative = new ExplanationBuilder().Build(Ranked(new[]
        {
            new DecisionFactor("unknown_effect_count", -1d),
        }), PlanningResult.NotRequested());
        AssertContains(negative, "uncertain effect", "negative factor identity");
        AssertContains(negative, "weighs against", "negative contribution direction");
        AssertNotContains(negative, "limits exposure", "negative contribution is not a benefit");

        var positive = new ExplanationBuilder().Build(Ranked(new[]
        {
            new DecisionFactor("unknown_effect_count", 1d),
        }), PlanningResult.NotRequested());
        AssertContains(positive, "uncertain effect", "positive factor identity");
        AssertContains(positive, "supports", "positive contribution direction");
        AssertNotContains(positive, "weighs against", "positive contribution is not a penalty");
    }

    private static void ExplanationIgnoresZeroAndUnknownContributions()
    {
        var zero = new ExplanationBuilder().Build(Ranked(new[]
        {
            new DecisionFactor("board_power_delta", 0d),
            new DecisionFactor("remaining_gold", 0d),
            new DecisionFactor("unknown_effect_count", 0d),
        }), PlanningResult.Completed());
        AssertEqual("", zero, "zero contributions");

        var unknown = new ExplanationBuilder().Build(Ranked(new[]
        {
            new DecisionFactor("not_in_schema", 100d),
        }), PlanningResult.Completed());
        AssertEqual("", unknown, "unknown contributions");
        AssertEqual("", new ExplanationBuilder().Build(Ranked(new DecisionFactor[0]),
            PlanningResult.Completed()), "empty factors");
    }

    private static void FactorySharesCandidateGeneratorAndReportsIdentity()
    {
        using (var stream = AcceptedEnvelope())
        {
            var bundle = DecisionPolicyFactory.Create(new DecisionEngine(), stream,
                new ValidSeasonExtension(), ApprovedRuntimeContext());
            var unified = (UnifiedRankingPolicy)bundle.Unified;
            AssertSame(bundle.Candidates, unified.Candidates, "single production candidate generator");
            AssertEqual("linear-task11", bundle.ModelId, "model identity");
            AssertTrue(bundle.ModelPayloadSha256.Length == 64, "payload checksum identity");
            AssertEqual("legacy-v1.1.0", bundle.Legacy.PolicyId, "visible legacy policy");
        }
    }

    private static void FactoryFailsClosedForExpectedModelAbsence()
    {
        AssertUnavailable(DecisionPolicyFactory.Create(new DecisionEngine(), null,
            new ValidSeasonExtension(), ApprovedRuntimeContext()), "missing stream");
        AssertUnavailable(DecisionPolicyFactory.Create(new DecisionEngine(),
            Utf8("not-json"), new ValidSeasonExtension(), ApprovedRuntimeContext()), "malformed JSON");
        AssertUnavailable(DecisionPolicyFactory.Create(new DecisionEngine(),
            Utf8("{\"ArtifactSchemaVersion\":2,\"PayloadBase64\":\"e30=\",\"PayloadSha256\":\"bad\"}"),
            new ValidSeasonExtension(), ApprovedRuntimeContext()), "schema incompatibility");
        AssertUnavailable(DecisionPolicyFactory.Create(new DecisionEngine(),
            new ThrowingReadStream(new IOException("deterministic read failure")),
            new ValidSeasonExtension(), ApprovedRuntimeContext()), "read IOException");
        using (var stream = AcceptedEnvelope())
            AssertUnavailable(DecisionPolicyFactory.Create(new DecisionEngine(), stream, null,
                ApprovedRuntimeContext()),
                "missing season extension");
    }

    private static void FactoryRequiresExplicitRuntimeApproval()
    {
        using (var stream = AcceptedEnvelope())
            AssertUnavailable(DecisionPolicyFactory.Create(new DecisionEngine(), stream,
                new ValidSeasonExtension(), null), "missing runtime approval");

        using (var stream = AcceptedEnvelope())
            AssertUnavailable(DecisionPolicyFactory.Create(new DecisionEngine(), stream,
                new ValidSeasonExtension(), new RankingModelRuntimeContext(
                    "build-task11", "season-task11", "pool-task11", true,
                    "wrong-checksum", "linear-task11", "engineering-fixture-task11",
                    "season-task11")), "mismatched runtime approval");
    }

    private static void FactoryThreeParameterEntryUsesIndependentRuntimeApproval()
    {
        using (var stream = AcceptedEnvelope())
            AssertUnavailable(DecisionPolicyFactory.Create(new DecisionEngine(), stream,
                new ValidSeasonExtension()), "three-parameter entry without runtime approval");

        using (var stream = AcceptedEnvelope())
        {
            var bundle = DecisionPolicyFactory.Create(new DecisionEngine(), stream,
                new ApprovedSeasonExtension(ApprovedRuntimeContext()));
            var unified = (UnifiedRankingPolicy)bundle.Unified;
            AssertSame(bundle.Candidates, unified.Candidates,
                "three-parameter entry candidate generator");
            AssertEqual("linear-task11", bundle.ModelId,
                "three-parameter entry model identity");
            AssertTrue(bundle.ModelPayloadSha256.Length == 64,
                "three-parameter entry checksum identity");
        }

        AssertThrows<ArgumentNullException>(() => DecisionPolicyFactory.Create(null, null,
            new ValidSeasonExtension()), "three-parameter null engine");
    }

    private static void FactoryPreservesProgrammerAndStreamStateErrors()
    {
        AssertThrows<ArgumentNullException>(() => DecisionPolicyFactory.Create(null, null,
            new ValidSeasonExtension(), ApprovedRuntimeContext()), "null engine");
        using (var stream = AcceptedEnvelope())
            AssertThrows<InvalidOperationException>(() => DecisionPolicyFactory.Create(
                new DecisionEngine(), stream, new InvalidDependencyExtension(),
                ApprovedRuntimeContext()),
                "invalid internal dependency");
        using (var stream = AcceptedEnvelope())
            AssertThrows<ModelValidationException>(() => DecisionPolicyFactory.Create(
                new DecisionEngine(), stream, new MismatchedSeasonExtension(),
                ApprovedRuntimeContext()),
                "composition identity mismatch");

        var disposed = new MemoryStream();
        disposed.Dispose();
        AssertThrows<ObjectDisposedException>(() => DecisionPolicyFactory.Create(
            new DecisionEngine(), disposed, new ValidSeasonExtension(), ApprovedRuntimeContext()),
            "disposed stream");
        AssertThrows<InvalidOperationException>(() => DecisionPolicyFactory.Create(
            new DecisionEngine(), new ThrowingReadStream(
                new InvalidOperationException("deliberate stream state failure")),
            new ValidSeasonExtension(), ApprovedRuntimeContext()), "unexpected stream state failure");

        var providerFailure = new InvalidOperationException("deliberate approval provider failure");
        using (var stream = AcceptedEnvelope())
            AssertThrows<InvalidOperationException>(() => DecisionPolicyFactory.Create(
                new DecisionEngine(), stream,
                new ThrowingRuntimeContextExtension(providerFailure)),
                "runtime approval provider failure");
    }

    private static RankedAction Ranked(IEnumerable<DecisionFactor> factors)
    {
        var action = new GameAction { Type = ActionType.BuyMinion, TargetIndex = 0,
            CardId = "CARD_A", PurchaseSource = "shop" };
        return new RankedAction(action, ActionDescriptor.CreateKey(action), 1d, factors.ToList());
    }

    private static MemoryStream AcceptedEnvelope()
    {
        var envelope = JObject.Parse(File.ReadAllText(_fixturePath));
        var payloadBytes = Convert.FromBase64String((string)envelope["PayloadBase64"]);
        var payload = JObject.Parse(Encoding.UTF8.GetString(payloadBytes));
        payload["ModelId"] = "linear-task11";
        payload["TrainingSetIdentity"] = "engineering-fixture-task11";
        payload["SeasonRange"] = "season-task11";
        payload["GameBuild"] = "build-task11";
        payload["SeasonId"] = "season-task11";
        payload["CardPoolIdentity"] = "pool-task11";
        payload["EffectAcceptanceStatus"] = "accepted";
        payloadBytes = Encoding.UTF8.GetBytes(payload.ToString(Formatting.None));
        envelope["PayloadBase64"] = Convert.ToBase64String(payloadBytes);
        using (var sha = SHA256.Create())
            envelope["PayloadSha256"] = BitConverter.ToString(sha.ComputeHash(payloadBytes))
                .Replace("-", "").ToLowerInvariant();
        return Utf8(envelope.ToString(Formatting.None));
    }

    private static RankingModelRuntimeContext ApprovedRuntimeContext()
    {
        using (var stream = AcceptedEnvelope())
        using (var reader = new StreamReader(stream))
        {
            var envelope = JObject.Parse(reader.ReadToEnd());
            return new RankingModelRuntimeContext(
                "build-task11", "season-task11", "pool-task11", true,
                (string)envelope["PayloadSha256"], "linear-task11",
                "engineering-fixture-task11", "season-task11");
        }
    }

    private static MemoryStream Utf8(string value)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(value));
    }

    private static void AssertUnavailable(DecisionPolicyBundle bundle, string label)
    {
        AssertEqual("unavailable", bundle.Unified.PolicyId, label + " policy");
        AssertEqual(FallbackReason.ModelUnavailable,
            bundle.Unified.Decide(new GameState()).FallbackReason, label + " fallback");
        AssertEqual("unknown", bundle.ModelId, label + " model identity");
        AssertEqual("unknown", bundle.ModelPayloadSha256, label + " checksum identity");
    }

    private static void AssertContains(string text, string value, string label)
    {
        AssertTrue(text != null && text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0,
            label + " missing '" + value + "' from '" + text + "'");
    }

    private static void AssertNotContains(string text, string value, string label)
    {
        AssertTrue(text == null || text.IndexOf(value, StringComparison.OrdinalIgnoreCase) < 0,
            label + " exposed '" + value + "' in '" + text + "'");
    }

    private static void AssertEqual<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(label + " expected=" + expected + " actual=" + actual);
    }

    private static void AssertSame(object expected, object actual, string label)
    {
        if (!object.ReferenceEquals(expected, actual))
            throw new InvalidOperationException(label + " reference changed");
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

    private sealed class ThrowingReadStream : Stream
    {
        private readonly Exception _exception;
        public ThrowingReadStream(Exception exception) { _exception = exception; }
        public override bool CanRead { get { return true; } }
        public override bool CanSeek { get { return false; } }
        public override bool CanWrite { get { return false; } }
        public override long Length { get { throw new NotSupportedException(); } }
        public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) { throw _exception; }
        public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
        public override void SetLength(long value) { throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
    }
}
