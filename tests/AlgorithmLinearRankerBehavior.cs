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
        public string GameBuild = "";
        public string SeasonId = "";
        public string CardPoolIdentity = "";
        public List<MinionData> ShopMinions = new List<MinionData>();
        public List<MinionData> BoardMinions = new List<MinionData>();
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
            return new List<ActionDescriptor> { ActionDescriptor.From(state, new GameAction
            {
                Type = ActionType.Refresh, TargetIndex = -1, CardId = "diagnostic", PurchaseSource = "test",
            }) };
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
            return new ActionFeatureVector(new double[count], new double[count]);
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
            return new DecisionResult(PolicyId, null, null, 0d, PlannerStatus.NotRequested, _reason, "");
        }
    }
}

internal static class AlgorithmLinearRankerBehavior
{
    private static int Main()
    {
        try
        {
            RanksByNormalizedScoreAndExplainsDominantFactor();
            BreaksTiesByOrdinalActionKey();
            RejectsNonFiniteScores();
            RejectsUnsafeProductionComposition();
            KeepsUnifiedRankingDiagnosticOnlyAndFailClosed();
            Console.WriteLine("PASS linear ranker uses one deterministic score surface");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL " + ex.Message);
            return 1;
        }
    }

    private static void RanksByNormalizedScoreAndExplainsDominantFactor()
    {
        var model = Model("board_power_delta", 2d, 0d);
        var ranker = new LinearPairwiseRanker(model);
        var ranked = ranker.Rank(new[] { Candidate("weak", 1d), Candidate("strong", 3d) });

        AssertEqual("Refresh|-1|strong|test", ranked[0].ActionKey, "top candidate");
        AssertEqual("board_power_delta", ranked[0].Factors
            .OrderByDescending(factor => Math.Abs(factor.Contribution)).First().FeatureName, "factor");
        AssertEqual(6d, ranked[0].Score, "normalized score");
    }

    private static void BreaksTiesByOrdinalActionKey()
    {
        var ranker = new LinearPairwiseRanker(Model("board_power_delta", 0d, 0d));
        var inputs = new[] { Candidate("zeta", 1d), Candidate("alpha", 1d) };
        var expected = string.Join("|", ranker.Rank(inputs).Select(item => item.ActionKey));
        for (var index = 0; index < 100; index++)
            AssertEqual(expected, string.Join("|", ranker.Rank(inputs).Select(item => item.ActionKey)),
                "tie breaking " + index);
        AssertEqual("Refresh|-1|alpha|test|Refresh|-1|zeta|test", expected,
            "ordinal tie breaking");
    }

    private static void RejectsNonFiniteScores()
    {
        var model = Model("board_power_delta", 1d, 0d);
        model.Weights[ActionFeatureSchema.IndexOf("board_power_delta")] = double.NaN;
        AssertThrows(() => new LinearPairwiseRanker(model), "non-finite model");
    }

    private static void RejectsUnsafeProductionComposition()
    {
        var extension = new ExactSeasonExtension("known-build", "known-season", "known-pool");
        var unknown = ProductionModel();
        unknown.ModelId = "unknown";
        AssertModelValidation(() => UnifiedRankingPolicy.CreateProduction(unknown, extension),
            "unknown model identity");

        var unaccepted = ProductionModel();
        unaccepted.EffectAcceptanceStatus = "shadow";
        AssertModelValidation(() => UnifiedRankingPolicy.CreateProduction(unaccepted, extension),
            "unaccepted model");

        var synthetic = ProductionModel();
        synthetic.ModelId = "synthetic-model";
        AssertModelValidation(() => UnifiedRankingPolicy.CreateProduction(synthetic, extension),
            "synthetic model");

        var unknownTrainingSet = ProductionModel();
        unknownTrainingSet.TrainingSetIdentity = "unknown";
        AssertModelValidation(() => UnifiedRankingPolicy.CreateProduction(unknownTrainingSet, extension),
            "unknown training set identity");

        var missingSeasonRange = ProductionModel();
        missingSeasonRange.SeasonRange = " ";
        AssertModelValidation(() => UnifiedRankingPolicy.CreateProduction(missingSeasonRange, extension),
            "missing season range identity");

        var wrongFeatureSchema = ProductionModel();
        wrongFeatureSchema.FeatureSchemaVersion = ActionFeatureSchema.Version + 1;
        AssertModelValidation(() => UnifiedRankingPolicy.CreateProduction(wrongFeatureSchema, extension),
            "wrong feature schema version");

        var reorderedFeatureNames = ProductionModel();
        var firstFeatureName = reorderedFeatureNames.FeatureNames[0];
        reorderedFeatureNames.FeatureNames[0] = reorderedFeatureNames.FeatureNames[1];
        reorderedFeatureNames.FeatureNames[1] = firstFeatureName;
        AssertModelValidation(() => UnifiedRankingPolicy.CreateProduction(reorderedFeatureNames, extension),
            "reordered feature names");

        var missingFeatureName = ProductionModel();
        missingFeatureName.FeatureNames = missingFeatureName.FeatureNames.Take(
            missingFeatureName.FeatureNames.Length - 1).ToArray();
        AssertModelValidation(() => UnifiedRankingPolicy.CreateProduction(missingFeatureName, extension),
            "missing feature name");

        AssertModelValidation(() => UnifiedRankingPolicy.CreateProduction(ProductionModel(),
            new ExactSeasonExtension("other-build", "known-season", "known-pool")),
            "season extension mismatch");
    }

    private static void KeepsUnifiedRankingDiagnosticOnlyAndFailClosed()
    {
        var policy = UnifiedRankingPolicy.CreateProduction(ProductionModel(),
            new ExactSeasonExtension("known-build", "known-season", "known-pool"));
        var unavailable = policy.Decide(new GameState
        {
            GameBuild = "unknown", SeasonId = "known-season", CardPoolIdentity = "known-pool",
        });
        AssertEqual(FallbackReason.ModelUnavailable, unavailable.FallbackReason,
            "unknown state identity fallback");
        AssertEqual(0d, unavailable.Confidence, "unavailable confidence");

        var diagnostic = policy.Decide(new GameState
        {
            GameBuild = "known-build", SeasonId = "known-season", CardPoolIdentity = "known-pool",
        });
        AssertEqual(FallbackReason.None, diagnostic.FallbackReason, "diagnostic fallback");
        AssertEqual(0d, diagnostic.Confidence, "diagnostic confidence");
        AssertTrue(diagnostic.Explanation.IndexOf("shadow-only", StringComparison.Ordinal) >= 0,
            "diagnostic explanation");
    }

    private static LinearRankedCandidate Candidate(string key, double boardPowerDelta)
    {
        var values = new double[ActionFeatureSchema.Names.Count];
        var coverage = new double[ActionFeatureSchema.Names.Count];
        values[ActionFeatureSchema.IndexOf("board_power_delta")] = boardPowerDelta;
        return new LinearRankedCandidate(Descriptor(key), new ActionFeatureVector(values, coverage));
    }

    private static ActionDescriptor Descriptor(string actionKey)
    {
        return ActionDescriptor.From(new GameState(), new GameAction
        {
            Type = ActionType.Refresh, TargetIndex = -1, CardId = actionKey, PurchaseSource = "test",
        });
    }

    private static RankingModelPayload Model(string featureName, double weight, double intercept)
    {
        var count = ActionFeatureSchema.Names.Count;
        var model = new RankingModelPayload
        {
            FeatureSchemaVersion = ActionFeatureSchema.Version,
            FeatureNames = ActionFeatureSchema.Names.ToArray(),
            Means = new double[count], StandardDeviations = Enumerable.Repeat(1d, count).ToArray(),
            Minimums = new double[count], Maximums = new double[count], Weights = new double[count],
            ActionTypeBiases = new double[ActionTypeBiases.OrderedTypes.Count], Intercept = intercept,
            OodDistanceThreshold = 1d,
            BenchmarkMetrics = new Dictionary<string, double>(),
            CreatedAtUtc = "2026-01-01T00:00:00Z",
        };
        model.Weights[ActionFeatureSchema.IndexOf(featureName)] = weight;
        return model;
    }

    private static RankingModelPayload ProductionModel()
    {
        var model = Model("board_power_delta", 0d, 0d);
        model.ModelId = "known-model";
        model.GameBuild = "known-build";
        model.SeasonId = "known-season";
        model.CardPoolIdentity = "known-pool";
        model.TrainingSetIdentity = "known-training-set";
        model.SeasonRange = "known-season";
        model.EffectAcceptanceStatus = "accepted";
        return model;
    }

    private static void AssertEqual<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(label + " expected=" + expected + " actual=" + actual);
    }

    private static void AssertThrows(Action action, string label)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException(label);
    }


    private static void AssertModelValidation(Action action, string label)
    {
        try { action(); }
        catch (ModelValidationException) { return; }
        throw new InvalidOperationException(label);
    }

    private static void AssertTrue(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
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
