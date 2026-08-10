using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BobCoach.Engine;
using BobCoach.Engine.Decision;

namespace BobCoach.Engine
{
    public enum ActionType { BuyMinion, Refresh, Activate }

    public sealed class GameAction
    {
        public ActionType Type;
        public int TargetIndex;
        public string CardId = "";
        public string PurchaseSource = "";
        public string Description = "";
        public int Cost;
    }

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

    public sealed class GameState
    {
        public bool GameActive;
        public bool IsDuos;
        public string GameBuild;
        public string SeasonId;
        public string CardPoolIdentity;
        public string HeroCardId = "";
        public string AnomalyId = "";
        public int Gold;
        public int MaxGold;
        public int TavernTier;
        public int TavernUpgradeCost;
        public int Health;
        public int MaxHealth;
        public int Armor;
        public int Turn;
        public string Phase = "shop";
        public bool FrozenShop;
        public int FreeRefreshCount;
        public bool FirstPurchaseUsedThisTurn;
        public bool FirstMinionPurchaseUsedThisTurn;
        public string CurrentCompGuideTier;
        public HashSet<string> AvailableTribes = new HashSet<string>();
        public List<MinionData> BoardMinions = new List<MinionData>();
        public List<MinionData> HandMinions = new List<MinionData>();
        public List<MinionData> ShopMinions = new List<MinionData>();
        public List<TrinketOption> TrinketOffer = new List<TrinketOption>();
        public List<string> ActiveTrinkets = new List<string>();
    }

    public sealed class MinionData
    {
        public string CardId = "";
        public int EntityId;
        public int Attack;
        public int Health;
        public int Tier;
        public int Position;
        public bool Golden;
        public bool Taunt;
        public bool DivineShield;
        public bool Windfury;
        public bool MegaWindfury;
        public bool Stealth;
        public bool Reborn;
        public bool Poisonous;
        public bool Venomous;
        public bool Cleave;
        public bool Overkill;
        public bool IsSpell;
        public int Cost;
        public string Tribe = "";
    }

    public sealed class TrinketOption
    {
        public string CardId = "";
        public string TrinketName = "";
        public int EntityId;
        public bool IsLesser;
        public int Tier;
        public int Attack;
        public int Health;
        public List<string> Tribes = new List<string>();
    }

}

namespace BobCoach.Engine.Decision
{
    public sealed class ActionDescriptor
    {
        public string ActionKey { get; set; }

        public static string CreateKey(GameAction action)
        {
            return string.Join("|", action.Type, action.TargetIndex,
                action.CardId ?? "", action.PurchaseSource ?? "");
        }
    }
}

internal static class AlgorithmOrchestratorBehavior
{
    private static int Main()
    {
        try
        {
            ShadowPreservesLegacyOutputAndRecordsDifferences();
            LegacyOnlyAvoidsUnifiedPolicyAndComparison();
            InvalidRuntimeModeDegradesToLegacyOnly();
            UnifiedModeRemainsShadowUntilActivationApproved();
            UnifiedFailureFallsBackToLegacy();
            LegacyFailureReturnsConservativeUnavailableResult();
            NullPolicyResultsAreContainedAndFallback();
            MalformedRankedActionsAreContainedInShadowModes();
            RecorderAndDiagnosticFailuresAreContained();
            ComparisonFingerprintIsStableAndDecisionScoped();
            ProductionRecorderRejectsBlankDecisionIdAndDeduplicates();
            ShadowOutputIsDeterministicAcrossRepeatedStateClones();
            Console.WriteLine("PASS shadow mode preserves legacy output and contains unified failures");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL " + ex.Message);
            return 1;
        }
    }

    private static void ShadowPreservesLegacyOutputAndRecordsDifferences()
    {
        var legacy = new StaticPolicy("legacy-v1.1.0", Result("legacy-v1.1.0", "Refresh|-1||", 0.8d));
        var unified = new StaticPolicy("unified-linear-v1", Result("unified-linear-v1", "BuyMinion|0|CARD_A|shop", 0.6d));
        var sink = new CollectingShadowComparisonSink();
        var visible = new DecisionOrchestrator(legacy, unified, sink, DecisionRuntimeMode.Shadow)
            .Decide(State(), "decision-1");

        AssertEqual("legacy-v1.1.0", visible.PolicyId, "shadow visible policy");
        AssertEqual(1, sink.Comparisons.Count, "difference recorded");
        AssertTrue(!sink.Comparisons[0].Agrees, "different selected action is disagreement");
    }

    private static void LegacyOnlyAvoidsUnifiedPolicyAndComparison()
    {
        var legacy = new StaticPolicy("legacy-v1.1.0", Result("legacy-v1.1.0", "Refresh|-1||", 0.8d));
        var unified = new ThrowingPolicy("unified-linear-v1");
        var sink = new CollectingShadowComparisonSink();
        var visible = new DecisionOrchestrator(legacy, unified, sink, DecisionRuntimeMode.LegacyOnly)
            .Decide(State(), "decision-1");

        AssertEqual("legacy-v1.1.0", visible.PolicyId, "legacy-only visible policy");
        AssertEqual(0, sink.Comparisons.Count, "legacy-only does not record comparison");
    }

    private static void InvalidRuntimeModeDegradesToLegacyOnly()
    {
        var legacy = new StaticPolicy("legacy-v1.1.0", Result("legacy-v1.1.0", "Refresh|-1||", 0.8d));
        var unified = new CountingPolicy("unified-linear-v1",
            Result("unified-linear-v1", "BuyMinion|0|CARD_A|shop", 0.6d));
        var sink = new CollectingShadowComparisonSink();
        var visible = new DecisionOrchestrator(legacy, unified, sink, (DecisionRuntimeMode)999)
            .Decide(State(), "invalid-mode");

        AssertEqual("legacy-v1.1.0", visible.PolicyId, "invalid mode visible policy");
        AssertEqual(0, unified.DecideCount, "invalid mode does not execute unified policy");
        AssertEqual(0, sink.Comparisons.Count, "invalid mode does not record comparison");
    }

    private static void UnifiedFailureFallsBackToLegacy()
    {
        var legacy = new StaticPolicy("legacy-v1.1.0", Result("legacy-v1.1.0", "Refresh|-1||", 0.8d));
        var failing = new ThrowingPolicy("unified-linear-v1");
        var safe = new DecisionOrchestrator(legacy, failing, new CollectingShadowComparisonSink(),
            DecisionRuntimeMode.Unified).Decide(State(), "decision-1");

        AssertEqual("legacy-v1.1.0", safe.PolicyId, "exception fallback policy");
        AssertEqual(FallbackReason.None, safe.FallbackReason,
            "unified failure preserves the legacy fallback reason");
    }

    private static void LegacyFailureReturnsConservativeUnavailableResult()
    {
        var diagnostics = new CollectingDecisionDiagnostics();
        var safe = new DecisionOrchestrator(new ThrowingPolicy("legacy-v1.1.0"),
            new ThrowingPolicy("unified-linear-v1"), new CollectingShadowComparisonSink(),
            DecisionRuntimeMode.Shadow, diagnostics).Decide(State(), "legacy-failure");

        AssertEqual("unavailable", safe.PolicyId, "legacy exception fallback policy");
        AssertEqual(FallbackReason.NoSafeAction, safe.FallbackReason,
            "legacy exception uses the conservative no-safe-action fallback");
        AssertEqual(2, diagnostics.Entries.Count,
            "legacy and unified failures are both contained as diagnostics");
    }

    private static void UnifiedModeRemainsShadowUntilActivationApproved()
    {
        var legacy = new StaticPolicy("legacy-v1.1.0", Result("legacy-v1.1.0", "Refresh|-1||", 0.8d));
        var unified = new CountingPolicy("unified-linear-v1",
            Result("unified-linear-v1", "BuyMinion|0|CARD_A|shop", 0.6d));
        var sink = new CollectingShadowComparisonSink();
        var visible = new DecisionOrchestrator(legacy, unified, sink, DecisionRuntimeMode.Unified)
            .Decide(State(), "unified-before-activation");

        AssertEqual("legacy-v1.1.0", visible.PolicyId, "unified mode keeps legacy visible");
        AssertEqual("Refresh|-1||", ActionKey(visible.SelectedAction),
            "unified mode keeps legacy selected action");
        AssertEqual(1, unified.DecideCount, "unified mode evaluates for local comparison");
        AssertEqual(1, sink.Comparisons.Count, "unified mode records local comparison");
    }

    private static void NullPolicyResultsAreContainedAndFallback()
    {
        var legacyResult = Result("legacy-v1.1.0", "Refresh|-1||", 0.8d);
        var diagnostics = new CollectingDecisionDiagnostics();
        var shadow = new DecisionOrchestrator(new StaticPolicy("legacy-v1.1.0", legacyResult),
            new NullPolicy("unified-linear-v1"), new CollectingShadowComparisonSink(),
            DecisionRuntimeMode.Shadow, diagnostics).Decide(State(), "null-unified");
        AssertEqual("legacy-v1.1.0", shadow.PolicyId, "null unified keeps shadow legacy output");
        AssertEqual(1, diagnostics.Entries.Count, "null unified is diagnostic input");

        var legacyFallback = new DecisionOrchestrator(new NullPolicy("legacy-v1.1.0"),
            new StaticPolicy("unified-linear-v1", Result("unified-linear-v1", "BuyMinion|0|CARD_A|shop", 0.6d)),
            new CollectingShadowComparisonSink(), DecisionRuntimeMode.LegacyOnly)
            .Decide(State(), "null-legacy");
        AssertEqual("unavailable", legacyFallback.PolicyId, "null legacy uses unavailable policy");
        AssertEqual(FallbackReason.NoSafeAction, legacyFallback.FallbackReason,
            "null legacy uses the existing conservative fallback");
    }

    private static void MalformedRankedActionsAreContainedInShadowModes()
    {
        foreach (var mode in new[] { DecisionRuntimeMode.Shadow, DecisionRuntimeMode.Unified })
        {
            var diagnostics = new CollectingDecisionDiagnostics();
            var sink = new CollectingShadowComparisonSink();
            var visible = new DecisionOrchestrator(
                new StaticPolicy("legacy-v1.1.0", Result("legacy-v1.1.0", "Refresh|-1||", 0.8d)),
                new StaticPolicy("unified-linear-v1", MalformedRankedResult()), sink, mode, diagnostics)
                .Decide(State(), "malformed-ranked-" + mode);

            AssertEqual("legacy-v1.1.0", visible.PolicyId,
                "malformed comparison keeps legacy visible " + mode);
            AssertEqual(0, sink.Comparisons.Count, "malformed comparison is not recorded " + mode);
            AssertEqual(1, diagnostics.Entries.Count, "malformed comparison is diagnostic input " + mode);
            AssertEqual("shadow-comparison", diagnostics.Entries[0],
                "malformed comparison diagnostic source " + mode);
        }
    }

    private static void RecorderAndDiagnosticFailuresAreContained()
    {
        var legacy = new StaticPolicy("legacy-v1.1.0", Result("legacy-v1.1.0", "Refresh|-1||", 0.8d));
        var unified = new StaticPolicy("unified-linear-v1", Result("unified-linear-v1", "BuyMinion|0|CARD_A|shop", 0.6d));
        var diagnostics = new CollectingDecisionDiagnostics();
        var contained = new DecisionOrchestrator(legacy, unified, new ThrowingShadowComparisonSink(),
            DecisionRuntimeMode.Shadow, diagnostics).Decide(State(), "decision-1");
        AssertEqual("legacy-v1.1.0", contained.PolicyId, "recorder failure keeps visible output");
        AssertEqual(1, diagnostics.Entries.Count, "recorder failure is diagnostic input");

        var fullyContained = new DecisionOrchestrator(legacy, unified, new ThrowingShadowComparisonSink(),
            DecisionRuntimeMode.Shadow, new ThrowingDecisionDiagnostics()).Decide(State(), "decision-1");
        AssertEqual("legacy-v1.1.0", fullyContained.PolicyId,
            "diagnostic failure cannot escape UI call path");
    }

    private static void ComparisonFingerprintIsStableAndDecisionScoped()
    {
        var state = State();
        var legacy = Result("legacy-v1.1.0", "Refresh|-1||", 0.8d);
        var unified = Result("unified-linear-v1", "BuyMinion|0|CARD_A|shop", 0.6d);
        var first = ShadowComparison.Create("decision-1", state, legacy, unified);
        var repeat = ShadowComparison.Create("decision-1", Clone(state), legacy, unified);
        var next = ShadowComparison.Create("decision-2", Clone(state), legacy, unified);

        AssertEqual(first.ComparisonId, repeat.ComparisonId, "stable comparison fingerprint");
        AssertTrue(first.ComparisonId != next.ComparisonId, "decision id scopes comparison fingerprint");
        AssertTrue(!string.IsNullOrWhiteSpace(first.StateFingerprint),
            "unknown state identities remain within the state fingerprint");
    }

    private static void ProductionRecorderRejectsBlankDecisionIdAndDeduplicates()
    {
        var path = NewPath();
        var recorder = new DecisionEventRecorder(path);
        var state = State();
        var legacy = Result("legacy-v1.1.0", "Refresh|-1||", 0.8d);
        var unified = Result("unified-linear-v1", "BuyMinion|0|CARD_A|shop", 0.6d);
        recorder.RecordComparison(ShadowComparison.Create("decision-1", state, legacy, unified));
        recorder.RecordComparison(ShadowComparison.Create("decision-1", state, legacy, unified));
        recorder.RecordComparison(ShadowComparison.Create("decision-2", state, legacy, unified));
        AssertThrows<ArgumentException>(() => recorder.RecordComparison(
            ShadowComparison.Create("", state, legacy, unified)), "production recorder rejects blank decision ID");

        AssertEqual(2, File.ReadAllLines(path).Length,
            "same decision dedupes and later identical decision remains distinct");
    }

    private static void ShadowOutputIsDeterministicAcrossRepeatedStateClones()
    {
        var legacy = new StaticPolicy("legacy-v1.1.0", Result("legacy-v1.1.0", "Refresh|-1||", 0.8d));
        var unified = new StaticPolicy("unified-linear-v1", Result("unified-linear-v1", "BuyMinion|0|CARD_A|shop", 0.6d));
        var sink = new CollectingShadowComparisonSink();
        var orchestrator = new DecisionOrchestrator(legacy, unified, sink, DecisionRuntimeMode.Shadow);
        var expected = orchestrator.Decide(State(), "deterministic-0");
        var expectedUnifiedActionKey = sink.Comparisons[0].UnifiedSelectedActionKey;
        for (var index = 1; index <= 100; index++)
        {
            var actual = orchestrator.Decide(Clone(State()), "deterministic-" + index);
            var actualUnifiedActionKey = sink.Comparisons[index].UnifiedSelectedActionKey;
            AssertEqual(expected.PolicyId, actual.PolicyId, "deterministic policy " + index);
            AssertEqual(ActionKey(expected.SelectedAction), ActionKey(actual.SelectedAction), "deterministic action " + index);
            AssertEqual(expectedUnifiedActionKey, actualUnifiedActionKey,
                "deterministic unified action " + index);
            AssertEqual(expected.Confidence, actual.Confidence, "deterministic confidence " + index);
            AssertEqual(expected.PlannerStatus, actual.PlannerStatus, "deterministic planner " + index);
            AssertEqual(expected.FallbackReason, actual.FallbackReason, "deterministic fallback " + index);
        }
    }

    private static DecisionResult Result(string policyId, string actionKey, double confidence)
    {
        var action = Action(actionKey);
        return new DecisionResult(policyId, action,
            new List<RankedAction> { new RankedAction(action, actionKey, 1d, null) }, confidence,
            PlannerStatus.Completed, FallbackReason.None, policyId);
    }

    private static DecisionResult MalformedRankedResult()
    {
        return new DecisionResult("unified-linear-v1", Action("BuyMinion|0|CARD_A|shop"),
            new List<RankedAction> { null }, 0.6d, PlannerStatus.Completed,
            FallbackReason.None, "malformed ranked list");
    }

    private static GameAction Action(string actionKey)
    {
        var values = actionKey.Split('|');
        return new GameAction
        {
            Type = values[0] == "Refresh" ? ActionType.Refresh : ActionType.BuyMinion,
            TargetIndex = int.Parse(values[1]),
            CardId = values[2],
            PurchaseSource = values[3]
        };
    }

    private static string ActionKey(GameAction action)
    {
        return action == null ? "" : ActionDescriptor.CreateKey(action);
    }

    private static GameState State()
    {
        return new GameState { GameActive = true, Gold = 5, MaxGold = 10, TavernTier = 3,
            TavernUpgradeCost = 7, Health = 28, MaxHealth = 30, Turn = 6 };
    }

    private static GameState Clone(GameState state)
    {
        return new GameState { GameActive = state.GameActive, Gold = state.Gold, MaxGold = state.MaxGold,
            TavernTier = state.TavernTier, TavernUpgradeCost = state.TavernUpgradeCost,
            Health = state.Health, MaxHealth = state.MaxHealth, Turn = state.Turn,
            GameBuild = state.GameBuild, SeasonId = state.SeasonId,
            CardPoolIdentity = state.CardPoolIdentity };
    }

    private static string NewPath()
    {
        return Path.Combine(Path.GetTempPath(), "bobcoach_shadow_comparisons_" + Guid.NewGuid().ToString("N") + ".jsonl");
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

    private sealed class StaticPolicy : IDecisionPolicy
    {
        private readonly DecisionResult _result;
        public string PolicyId { get; private set; }
        public StaticPolicy(string policyId, DecisionResult result) { PolicyId = policyId; _result = result; }
        public DecisionResult Decide(GameState state) { return _result; }
    }

    private sealed class ThrowingPolicy : IDecisionPolicy
    {
        public string PolicyId { get; private set; }
        public ThrowingPolicy(string policyId) { PolicyId = policyId; }
        public DecisionResult Decide(GameState state) { throw new InvalidOperationException("policy failure"); }
    }

    private sealed class NullPolicy : IDecisionPolicy
    {
        public string PolicyId { get; private set; }
        public NullPolicy(string policyId) { PolicyId = policyId; }
        public DecisionResult Decide(GameState state) { return null; }
    }

    private sealed class CountingPolicy : IDecisionPolicy
    {
        private readonly DecisionResult _result;
        public string PolicyId { get; private set; }
        public int DecideCount { get; private set; }
        public CountingPolicy(string policyId, DecisionResult result) { PolicyId = policyId; _result = result; }
        public DecisionResult Decide(GameState state) { DecideCount++; return _result; }
    }

    private sealed class CollectingShadowComparisonSink : IShadowComparisonSink
    {
        public readonly List<ShadowComparison> Comparisons = new List<ShadowComparison>();
        public void RecordComparison(ShadowComparison comparison) { Comparisons.Add(comparison); }
    }

    private sealed class ThrowingShadowComparisonSink : IShadowComparisonSink
    {
        public void RecordComparison(ShadowComparison comparison) { throw new IOException("recorder failure"); }
    }

    private sealed class CollectingDecisionDiagnostics : IDecisionDiagnostics
    {
        public readonly List<string> Entries = new List<string>();
        public void RecordRateLimited(string source, Exception exception) { Entries.Add(source); }
    }

    private sealed class ThrowingDecisionDiagnostics : IDecisionDiagnostics
    {
        public void RecordRateLimited(string source, Exception exception) { throw new IOException("diagnostic failure"); }
    }
}
