using System;
using System.Collections.Generic;
using System.Linq;
using BobCoach.Engine;
using BobCoach.Engine.Decision;

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
        public string CardId;
        public string PurchaseSource;
    }

    public sealed class GameState
    {
        public bool GameActive = true;
        public string GameBuild = "known-build";
        public string SeasonId = "known-season";
        public string CardPoolIdentity = "known-pool";
        public string Phase = "shop";
        public int Gold = 5;
        public int TavernTier = 3;
        public int MaxHealth = 30;
        public List<MinionData> ShopMinions = new List<MinionData>();
        public List<MinionData> BoardMinions = new List<MinionData>();
        public List<MinionData> HandMinions = new List<MinionData>();
        public List<object> Opponents = new List<object>();
        public List<TrinketOption> TrinketOffer = new List<TrinketOption>();
        public List<TrinketOption> DiscoverOptions = new List<TrinketOption>();
        public EffectiveGameRules EffectiveRules = EffectiveGameRules.Default;
        public string HeroCardId = "";
        public List<HeroPowerState> HeroPowers = new List<HeroPowerState>();
        public int HeroPowerCost;
    }

    public sealed class MinionData { public int EntityId; }
    public sealed class TrinketOption { public int EntityId; }
    public sealed class HeroPowerState { public string CardId; public int Cost; }
    public sealed class EffectiveGameRules
    {
        public static readonly EffectiveGameRules Default = new EffectiveGameRules();
        public TeammateGoldTransferRule TeammateGoldTransfer;
    }
    public sealed class TeammateGoldTransferRule { public string ActionCardId; public int GoldPerUse; }
    public static class GameRuleEvaluator
    {
        public static int GetPurchaseCost(GameState state, MinionData minion, string heroCardId,
            EffectiveGameRules rules) { return 0; }
        public static int GetRefreshCost(GameState state, string heroCardId,
            EffectiveGameRules rules) { return 0; }
        public static int? GetUpgradeCost(GameState state, EffectiveGameRules rules) { return 0; }
    }
}

namespace BobCoach.Engine.Decision
{
    public sealed class ActionEnumerator { }

    public sealed class CandidateGenerator
    {
        public CandidateGenerator(ActionEnumerator enumerator) { }

        public IList<ActionDescriptor> Generate(GameState state)
        {
            return new[]
            {
                ActionDescriptor.From(state, new GameAction
                {
                    Type = ActionType.PickTrinket, TargetIndex = 0,
                    CardId = "unsupported", PurchaseSource = "test",
                }),
                ActionDescriptor.From(state, new GameAction
                {
                    Type = ActionType.Refresh, TargetIndex = -1,
                    CardId = "supported", PurchaseSource = "test",
                }),
                ActionDescriptor.From(state, new GameAction
                {
                    Type = ActionType.FreezeShop, TargetIndex = -1,
                    CardId = "partial", PurchaseSource = "test",
                }),
            };
        }
    }

    public sealed class HardConstraintFilter
    {
        public IList<ActionDescriptor> Apply(GameState state, IEnumerable<ActionDescriptor> candidates)
        { return candidates.ToList(); }
    }

    public sealed class Simulator
    {
        public GameState Simulate(GameState state, GameAction action) { return state; }
    }

    public sealed class ActionFeatureExtractor
    {
        public ActionFeatureExtractor(ISeasonFeatureExtension extension) { }

        public ActionFeatureVector Extract(GameState state, ActionDescriptor action, GameState nextState)
        {
            var count = ActionFeatureSchema.Names.Count;
            var values = new double[count];
            values[ActionFeatureSchema.IndexOf("semantic_coverage")] = 1d;
            return new ActionFeatureVector(values, Enumerable.Repeat(1d, count).ToArray());
        }
    }

    public sealed class UnavailableDecisionPolicy : IDecisionPolicy
    {
        private readonly FallbackReason _reason;
        public string PolicyId { get { return "unavailable"; } }
        private UnavailableDecisionPolicy(FallbackReason reason) { _reason = reason; }
        public static UnavailableDecisionPolicy For(FallbackReason reason)
        { return new UnavailableDecisionPolicy(reason); }
        public DecisionResult Decide(GameState state)
        {
            return new DecisionResult(PolicyId, null, null, 0d, PlannerStatus.NotRequested,
                _reason, "");
        }
    }
}

internal static class AlgorithmConfidenceBehavior
{
    private static int Main()
    {
        try
        {
            UnacceptedCalibrationFailsClosed();
            AppliesFallbackReasonPrecedence();
            RejectsCriticalStateAndUnsupportedCoverage();
            DetectsDistributionShiftAndReportsFiniteDistance();
            UsesAcceptedMarginCalibrationWithoutInventingPlanningThresholds();
            ProductionPolicyPreservesRankingDuringConservativeFallback();
            ProductionPolicyUsesAuthorityBoundCalibrationAndRejectsPartialSimulation();
            Console.WriteLine("PASS confidence floors force explicit safe fallback");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL " + ex.Message);
            return 1;
        }
    }

    private static void UnacceptedCalibrationFailsClosed()
    {
        var assessment = new DecisionConfidenceEvaluator().Evaluate(
            State(), Ranked(2d, 0.2d), CompleteVector(), Model());
        AssertFallback(assessment, FallbackReason.ModelUnavailable, "missing calibration");
        AssertEqual(0d, assessment.Score, "uncalibrated confidence");
        AssertTrue(!assessment.ShouldPlan, "uncalibrated planning threshold stays disabled");

        var missingApproval = new DecisionConfidenceEvaluator(Calibration()).Evaluate(
            State(), Ranked(2d, 0.2d), CompleteVector(), Model());
        AssertFallback(missingApproval, FallbackReason.ModelUnavailable,
            "missing calibration approval");

        var mismatch = new DecisionConfidenceEvaluator(Calibration(gameBuild: "other-build"),
            Approval())
            .Evaluate(State(), Ranked(2d, 0.2d), CompleteVector(), Model());
        AssertFallback(mismatch, FallbackReason.ModelUnavailable, "calibration identity mismatch");

        var holdoutMismatch = new DecisionConfidenceEvaluator(Calibration(),
            Approval(holdoutIdentity: "other-holdout")).Evaluate(
                State(), Ranked(2d, 0.2d), CompleteVector(), Model());
        AssertFallback(holdoutMismatch, FallbackReason.ModelUnavailable,
            "approved holdout identity mismatch");

        var calibrationMismatch = new DecisionConfidenceEvaluator(Calibration(),
            Approval(calibrationIdentity: "other-calibration")).Evaluate(
                State(), Ranked(2d, 0.2d), CompleteVector(), Model());
        AssertFallback(calibrationMismatch, FallbackReason.ModelUnavailable,
            "approved calibration identity mismatch");

        var unknown = Model();
        unknown.EffectAcceptanceStatus = "unknown";
        var unknownAssessment = Evaluator().Evaluate(
            State(build: "unknown"), Ranked(2d, 0.2d), CompleteVector(), unknown);
        AssertFallback(unknownAssessment, FallbackReason.ModelUnavailable,
            "unknown model and state identity");
    }

    private static void AppliesFallbackReasonPrecedence()
    {
        var evaluator = Evaluator();
        var model = Model();
        model.EffectAcceptanceStatus = "unknown";
        var allFailures = evaluator.Evaluate(State(gameActive: false), Ranked(0.01d, 0d),
            Vector(semanticCoverage: 0d, boardPowerDelta: 40d), model);
        AssertFallback(allFailures, FallbackReason.ModelUnavailable, "model precedence");

        AssertFallback(evaluator.Evaluate(State(gameActive: false), Ranked(0.01d, 0d),
            Vector(semanticCoverage: 0d, boardPowerDelta: 40d), Model()),
            FallbackReason.CriticalStateMissing, "critical state precedence");
        AssertFallback(evaluator.Evaluate(State(), Ranked(0.01d, 0d),
            Vector(semanticCoverage: 0d, boardPowerDelta: 40d), Model()),
            FallbackReason.UnsupportedMechanic, "coverage precedence");
        AssertFallback(evaluator.Evaluate(State(), Ranked(0.01d, 0d),
            Vector(semanticCoverage: 1d, boardPowerDelta: 40d), Model()),
            FallbackReason.OutOfDistribution, "OOD precedence");
        AssertFallback(evaluator.Evaluate(State(), Ranked(0.01d, 0d), CompleteVector(), Model()),
            FallbackReason.LowConfidence, "confidence precedence");
    }

    private static void RejectsCriticalStateAndUnsupportedCoverage()
    {
        var evaluator = Evaluator();
        AssertFallback(evaluator.Evaluate(State(gameActive: false), Ranked(2d, 0.2d),
            CompleteVector(), Model()), FallbackReason.CriticalStateMissing, "inactive game");
        AssertFallback(evaluator.Evaluate(State(), Ranked(2d, 0.2d),
            Vector(semanticCoverage: 0.2d), Model()), FallbackReason.UnsupportedMechanic,
            "semantic coverage floor");

        var criticalCoverage = CompleteVector();
        var model = Model();
        var index = ActionFeatureSchema.IndexOf("board_power_delta");
        criticalCoverage.Coverage[index] = 0d;
        model.Weights[index] = 1d;
        AssertFallback(evaluator.Evaluate(State(), Ranked(2d, 0.2d), criticalCoverage, model),
            FallbackReason.UnsupportedMechanic, "decision-critical feature coverage");
        var criticalAssessment = evaluator.Evaluate(State(), Ranked(2d, 0.2d),
            criticalCoverage, model);
        AssertEqual(0d, criticalAssessment.Score, "critical zero coverage confidence");

        var unsupportedSimulator = CompleteVector();
        unsupportedSimulator.Values[ActionFeatureSchema.IndexOf("simulator_coverage")] = 0d;
        var simulatorAssessment = evaluator.Evaluate(State(), Ranked(2d, 0.2d),
            unsupportedSimulator, Model());
        AssertFallback(simulatorAssessment, FallbackReason.UnsupportedMechanic,
            "unsupported simulator");
        AssertEqual(0d, simulatorAssessment.Score, "unsupported simulator confidence");
    }

    private static void DetectsDistributionShiftAndReportsFiniteDistance()
    {
        var evaluator = Evaluator();
        var shifted = evaluator.Evaluate(State(), Ranked(2d, 0.2d),
            Vector(semanticCoverage: 1d, boardPowerDelta: 40d), Model());
        AssertFallback(shifted, FallbackReason.OutOfDistribution, "distribution shift");
        AssertTrue(shifted.DistributionDistance >= 0d
            && !double.IsNaN(shifted.DistributionDistance)
            && !double.IsInfinity(shifted.DistributionDistance), "finite distance");

        var extensionShift = CompleteVector();
        extensionShift.Values[ActionFeatureSchema.IndexOf("season_extension_value")] = 40d;
        AssertFallback(evaluator.Evaluate(State(), Ranked(2d, 0.2d), extensionShift, Model()),
            FallbackReason.OutOfDistribution, "last schema feature distribution shift");

        var invalid = CompleteVector();
        invalid.Values[0] = double.NaN;
        var invalidDistance = evaluator.DistributionDistance(invalid, Model());
        AssertTrue(!double.IsNaN(invalidDistance) && !double.IsInfinity(invalidDistance),
            "non-finite input returns finite conservative distance");
    }

    private static void UsesAcceptedMarginCalibrationWithoutInventingPlanningThresholds()
    {
        var evaluator = Evaluator();
        var close = evaluator.Evaluate(State(), Ranked(1d, 0.99d), CompleteVector(), Model());
        var clear = evaluator.Evaluate(State(), Ranked(2d, 0.2d), CompleteVector(), Model());
        AssertTrue(close.ShouldPlan, "small calibrated margin requests planning");
        AssertTrue(clear.Score > close.Score, "margin raises confidence");
        AssertTrue(!clear.ShouldFallback, "clear in-distribution ranking passes accepted gate");

        var noPlanning = new DecisionConfidenceEvaluator(Calibration(planningThreshold: null),
            Approval())
            .Evaluate(State(), Ranked(1d, 0.99d), CompleteVector(), Model());
        AssertTrue(!noPlanning.ShouldPlan, "missing planning calibration stays disabled");
    }

    private static void ProductionPolicyPreservesRankingDuringConservativeFallback()
    {
        var policy = UnifiedRankingPolicy.CreateProduction(Model(),
            new ExactSeasonExtension("known-build", "known-season", "known-pool"));
        var result = policy.Decide(State());
        AssertFallback(result.FallbackReason, FallbackReason.ModelUnavailable,
            "production has no accepted confidence calibration");
        AssertEqual(3, result.RankedActions.Count, "fallback retains ranked list");
        AssertEqual("Refresh|-1|supported|test", result.RankedActions[0].ActionKey,
            "unsupported simulation cannot be promoted");
    }

    private static void ProductionPolicyUsesAuthorityBoundCalibrationAndRejectsPartialSimulation()
    {
        var policy = UnifiedRankingPolicy.CreateProduction(Model(),
            new ExactSeasonExtension("known-build", "known-season", "known-pool"),
            Calibration(), Approval());
        var result = policy.Decide(State());
        AssertFallback(result.FallbackReason, FallbackReason.None,
            "authority-bound production calibration");
        AssertTrue(result.Confidence >= 0.5d, "accepted production confidence");
        AssertEqual("Refresh|-1|supported|test", result.RankedActions[0].ActionKey,
            "partial simulation cannot tie complete simulation");
        var partial = result.RankedActions.First(action => action.Action.Type == ActionType.FreezeShop);
        AssertTrue(partial.Score < result.RankedActions[0].Score,
            "partial simulation carries unknown-effect penalty");
    }

    private static DecisionConfidenceEvaluator Evaluator()
    {
        return new DecisionConfidenceEvaluator(Calibration(), Approval());
    }

    private static ConfidenceCalibrationApprovalContext Approval(
        string holdoutIdentity = "known-holdout",
        string calibrationIdentity = "known-calibration")
    {
        return new ConfidenceCalibrationApprovalContext(holdoutIdentity, calibrationIdentity);
    }

    private static ConfidenceCalibration Calibration(string gameBuild = "known-build",
        double? planningThreshold = 0.1d)
    {
        return new ConfidenceCalibration("accepted", gameBuild, "known-season", "known-pool",
            "known-training-set", "known-holdout", "known-calibration", 1d, 0.5d, 0.5d,
            planningThreshold);
    }

    private static RankingModelPayload Model()
    {
        var count = ActionFeatureSchema.Names.Count;
        var model = new RankingModelPayload
        {
            ModelId = "known-model", FeatureSchemaVersion = ActionFeatureSchema.Version,
            FeatureNames = ActionFeatureSchema.Names.ToArray(), Means = new double[count],
            StandardDeviations = Enumerable.Repeat(1d, count).ToArray(),
            Minimums = Enumerable.Repeat(-5d, count).ToArray(),
            Maximums = Enumerable.Repeat(5d, count).ToArray(), Weights = new double[count],
            ActionTypeBiases = new double[ActionTypeBiases.OrderedTypes.Count], Intercept = 0d,
            OodDistanceThreshold = 1d, TrainingSetIdentity = "known-training-set",
            SeasonRange = "known-season", BenchmarkMetrics = new Dictionary<string, double>(),
            CreatedAtUtc = "2026-01-01T00:00:00Z", GameBuild = "known-build",
            SeasonId = "known-season", CardPoolIdentity = "known-pool",
            EffectAcceptanceStatus = "accepted",
        };
        model.Weights[ActionFeatureSchema.IndexOf("unknown_effect_count")] = -1d;
        return model;
    }

    private static GameState State(bool gameActive = true, string build = "known-build")
    {
        return new GameState { GameActive = gameActive, GameBuild = build };
    }

    private static IList<RankedAction> Ranked(params double[] scores)
    {
        return scores.Select((score, index) => new RankedAction(new GameAction
        {
            Type = ActionType.Refresh, TargetIndex = index, CardId = "ranked-" + index,
            PurchaseSource = "test",
        }, "ranked-" + index, score, null)).ToList();
    }

    private static ActionFeatureVector CompleteVector()
    {
        return Vector(semanticCoverage: 1d);
    }

    private static ActionFeatureVector Vector(double semanticCoverage, double boardPowerDelta = 0d)
    {
        var count = ActionFeatureSchema.Names.Count;
        var values = new double[count];
        values[ActionFeatureSchema.IndexOf("semantic_coverage")] = semanticCoverage;
        values[ActionFeatureSchema.IndexOf("simulator_coverage")] = 1d;
        values[ActionFeatureSchema.IndexOf("board_power_delta")] = boardPowerDelta;
        return new ActionFeatureVector(values, Enumerable.Repeat(1d, count).ToArray());
    }

    private static void AssertFallback(ConfidenceAssessment assessment, FallbackReason expected,
        string label)
    {
        AssertTrue(assessment.ShouldFallback, label + " should fallback");
        AssertFallback(assessment.FallbackReason, expected, label);
    }

    private static void AssertFallback(FallbackReason actual, FallbackReason expected, string label)
    {
        AssertEqual(expected, actual, label + " reason");
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

    private sealed class ExactSeasonExtension : ISeasonFeatureExtension
    {
        public string GameBuild { get; private set; }
        public string SeasonId { get; private set; }
        public string CardPoolIdentity { get; private set; }

        public ExactSeasonExtension(string gameBuild, string seasonId, string cardPoolIdentity)
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
}
