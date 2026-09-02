using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BobCoach.Engine;
using BobCoach.Engine.Decision;
using Newtonsoft.Json;

namespace BobCoach.Engine
{
    public interface IDecisionProvider
    {
        GameAction GetBestAction(GameState state);
        List<int> GetRecommendedBuyIndices(GameState state, int topN = 3);
        LevelUpSuggestion GetLevelUpSuggestion(GameState state);
    }

    public sealed class DecisionEngine : IDecisionProvider
    {
        public GameAction GetBestAction(GameState state) { return null; }
        public List<int> GetRecommendedBuyIndices(GameState state, int topN = 3)
        { return new List<int>(); }
        public LevelUpSuggestion GetLevelUpSuggestion(GameState state) { return null; }
    }

    public sealed class LevelUpSuggestion { }

    public sealed class GameState
    {
        public bool GameActive = true;
        public bool IsDuos;
        public string GameBuild = "";
        public string SeasonId = "";
        public string CardPoolIdentity = "";
        public string AnomalyId = "";
        public AnomalyContext AnomalyContext = AnomalyContext.Empty;
        public ActiveTrinketContext ActiveTrinketContext = ActiveTrinketContext.Empty;
        public string HeroCardId = "";
        public string HeroName = "";
        public string PlayerId = "";
        public string Phase = "shop";
        public int Gold;
        public int MaxGold = 10;
        public int LiveGold = -1;
        public int TavernTier = 3;
        public int TavernUpgradeCost;
        public int Health = 30;
        public int MaxHealth = 30;
        public int Armor;
        public int Turn = 1;
        public bool FrozenShop;
        public int FreeRefreshCount;
        public int LastUpgradeTurn;
        public bool FirstPurchaseUsedThisTurn;
        public bool FirstMinionPurchaseUsedThisTurn;
        public List<MinionData> ShopMinions = new List<MinionData>();
        public List<MinionData> BoardMinions = new List<MinionData>();
        public List<MinionData> HandMinions = new List<MinionData>();
        public List<HeroOption> HeroOptions = new List<HeroOption>();
        public List<OpponentData> Opponents = new List<OpponentData>();
        public List<TrinketOption> TrinketOffer = new List<TrinketOption>();
        public List<TrinketOption> DiscoverOptions = new List<TrinketOption>();
        public bool DiscoverGatePassed;
        public EffectiveGameRules EffectiveRules = EffectiveGameRules.Default;
        public List<HeroPowerState> HeroPowers = new List<HeroPowerState>();
        public int HeroPowerCost;
        public string HeroPowerCardId = "";
        public string HeroPowerType = "";
        public bool HeroPowerExhausted;
        public bool HasSecondHeroPower;
        public int ExhaustedHeroPowerCount;
        public int HeroPowerUnlockTurn;
        public int HeroPowerUnlockTier;
        public string HeroPowerSpecial = "";
        public bool HeroPowerHasDiscover;
        public string LastUsedTavernSpellCardId = "";
        public string LastUsedTavernSpellName = "";
        public bool ReplenishingShopActive;
        public bool PassiveChoiceActive;
        public bool NormalRecruitAvailable = true;
        public HashSet<int> ActivatedThisTurn = new HashSet<int>();
        public HashSet<int> RecentlyBoughtEntityIds = new HashSet<int>();
        public HashSet<string> AvailableTribes = new HashSet<string>();
        public List<string> ActiveTrinkets = new List<string>();
        public HashSet<string> ClaimedScheduledGrantOccurrences = new HashSet<string>();
        public HashSet<string> ObservedStartResourceExpectations = new HashSet<string>();
        public HashSet<string> MismatchedStartResourceExpectations = new HashSet<string>();
        public List<PurchaseRewardExpectation> PendingPurchaseRewardExpectations =
            new List<PurchaseRewardExpectation>();
        public HashSet<string> ClaimedPurchaseRewardOccurrences = new HashSet<string>();
        public List<TavernUpgradeOccurrence> TavernUpgradeOccurrences =
            new List<TavernUpgradeOccurrence>();
        public List<PrizeDiscoverExpectation> PendingPrizeDiscoverExpectations =
            new List<PrizeDiscoverExpectation>();
        public HashSet<string> ClaimedPrizeDiscoverOccurrences = new HashSet<string>();
        public List<TurnStartCardGrantExpectation> TurnStartCardGrantOccurrences =
            new List<TurnStartCardGrantExpectation>();
        public List<TurnStartCardGrantExpectation> PendingTurnStartCardGrantExpectations =
            new List<TurnStartCardGrantExpectation>();
        public HashSet<string> ClaimedTurnStartCardGrantOccurrences = new HashSet<string>();
        public List<SharedTurnEventExpectation> SharedTurnEventOccurrences =
            new List<SharedTurnEventExpectation>();
        public List<SharedTurnEventExpectation> PendingSharedTurnEvents =
            new List<SharedTurnEventExpectation>();
        public List<SharedTurnEventOutcome> ObservedSharedTurnEventOutcomes =
            new List<SharedTurnEventOutcome>();
        public List<SharedCardVoteOccurrence> SharedCardVoteOccurrences =
            new List<SharedCardVoteOccurrence>();
        public List<SharedCardVoteOccurrence> PendingSharedCardVoteSelections =
            new List<SharedCardVoteOccurrence>();
        public List<SharedCardVoteSelection> ObservedSharedCardVoteSelections =
            new List<SharedCardVoteSelection>();
        public List<SharedCardGrantExpectation> SharedCardGrantExpectations =
            new List<SharedCardGrantExpectation>();
        public List<SharedCardGrantObservation> ObservedLocalSharedCardGrants =
            new List<SharedCardGrantObservation>();
        public List<HeroIdentityExpectation> HeroIdentityExpectations =
            new List<HeroIdentityExpectation>();
        public List<SecondHeroPowerChoiceExpectation> SecondHeroPowerChoiceExpectations =
            new List<SecondHeroPowerChoiceExpectation>();
        public List<SecondHeroPowerChoiceBatchObservation> ObservedSecondHeroPowerChoiceBatches =
            new List<SecondHeroPowerChoiceBatchObservation>();
        public List<SecondHeroPowerChoiceSelection> ObservedSecondHeroPowerChoiceSelections =
            new List<SecondHeroPowerChoiceSelection>();
        public List<SecondHeroPowerEntityObservation> ObservedSecondHeroPowerEntities =
            new List<SecondHeroPowerEntityObservation>();
        public List<SimulatedTeammateGoldTransfer> SimulatedTeammateGoldTransfers =
            new List<SimulatedTeammateGoldTransfer>();
        public List<ObservedTeammateGoldTransfer> ObservedTeammateGoldTransfers =
            new List<ObservedTeammateGoldTransfer>();
        public int _nodeHandIdx;
        public string _nodeHandReason;
        public bool _nodeHeroPower;

        public int MaxBoardSlots { get { return 7; } }
    }

    public sealed class MinionData
    {
        public string CardId = "";
        public string CardName = "";
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
        public int AvengeCount;
        public string CardText = "";
        public string Tribe = "";
        public bool IsSpell;
        public bool GrantsStats;
        public int Cost;
        public bool IsFrozen;

        public static bool TribeMatches(string actual, string expected)
        { return string.Equals(actual, expected, StringComparison.Ordinal); }
    }

    public sealed class TrinketOption
    {
        public int EntityId;
        public string CardId = "";
        public string TrinketName = "";
        public int Tier;
        public int Attack;
        public int Health;
    }

    public sealed class HeroPowerState
    {
        public string CardId = "";
        public string BaseHeroCardId = "";
        public int Cost;
        public bool IsActive;
        public bool IsUnlocked;
        public bool Exhausted;
        public bool IsSecondary;
        public bool IsPrimary;
        public string SpecialRule = "";
        public bool HasDiscover;

        public HeroPowerState Copy()
        {
            return (HeroPowerState)MemberwiseClone();
        }
    }

    public sealed class EffectiveGameRules
    {
        public static readonly EffectiveGameRules Default = new EffectiveGameRules
        {
            ManualRefreshAllowed = true,
            GoldenCopyRequirement = 3,
        };
        public bool ManualRefreshAllowed;
        public TeammateGoldTransferRule TeammateGoldTransfer;
        public FirstPurchaseExtraCopyRule FirstPurchaseExtraCopy;
        public bool RefreshAfterPurchase;
        public int GoldenCopyRequirement;
        public string GoldenRewardOverride = "";
        public ActiveTrinketContext ActiveTrinkets;
        public object UpgradePrize;
    }

    public sealed class TeammateGoldTransferRule
    {
        public string ActionCardId = "";
        public int GoldPerUse;
        public int MaxPerTurn;
        public string SourceId = "";
    }

    public sealed class FirstPurchaseExtraCopyRule
    {
        public string SourceId = "";
        public int ExtraCopyCount;
    }

    public static class GameRuleEvaluator
    {
        public static int GetPurchaseCost(GameState state, MinionData minion, string heroCardId,
            EffectiveGameRules rules) { return 3; }
        public static int GetRefreshCost(GameState state, string heroCardId,
            EffectiveGameRules rules) { return 1; }
        public static int? GetUpgradeCost(GameState state, EffectiveGameRules rules) { return null; }
    }

    public static class TeammateGoldTransferEvaluator
    {
        public static int GetUsedCount(GameState state, int turn) { return 0; }
    }

    public static class CardPoolSampler
    {
        public static bool IsInitialized { get { return false; } }
        public static List<MinionData> SampleShop(int tavernTier, HashSet<string> availableTribes,
            EffectiveGameRules rules, int turn) { return new List<MinionData>(); }
        public static MinionData SampleTierOneReplacement(
            EffectiveGameRules rules, int turn)
        {
            return new MinionData { CardId = "sim_tier1", CardName = "随机1星随从", Tier = 1 };
        }
    }

    public sealed class ActiveTrinketContext
    {
        public static readonly ActiveTrinketContext Empty = new ActiveTrinketContext();
        public int GetGoldenCopyRequirement(MinionData minion, int defaultRequirement)
        { return defaultRequirement; }
    }

    public sealed class AnomalyContext
    {
        public static readonly AnomalyContext Empty = new AnomalyContext();
    }

    public static class TripleRuleEvaluator
    {
        public static bool GrantsStandardDiscover(EffectiveGameRules rules) { return true; }
        public static int CountOwnedCopies(GameState state, string cardId) { return 0; }
    }

    public static class UpgradePrizeEvaluator
    {
        public static TavernUpgradeOccurrence CreateOccurrence(object rule, int turn,
            int fromTier, int toTier, string source) { return null; }
        public static PrizeDiscoverExpectation CreateExpectation(object rule,
            TavernUpgradeOccurrence occurrence) { return null; }
    }

    public sealed class PurchaseRewardExpectation
    {
        public PurchaseRewardExpectation(string eventKey, string cardId, int count,
            bool isSpell, bool golden, string sourceId) { }
    }

    public sealed class SimulatedTeammateGoldTransfer
    {
        public SimulatedTeammateGoldTransfer(string eventKey, int turn, int gold,
            string sourceId) { }
    }

    public sealed class HeroOption { }
    public sealed class OpponentData { }
    public sealed class TavernUpgradeOccurrence { }
    public sealed class PrizeDiscoverExpectation { }
    public sealed class TurnStartCardGrantExpectation { }
    public sealed class SharedTurnEventExpectation { }
    public sealed class SharedTurnEventOutcome { }
    public sealed class SharedCardVoteOccurrence { }
    public sealed class SharedCardVoteSelection { }
    public sealed class SharedCardGrantExpectation { }
    public sealed class SharedCardGrantObservation { }
    public sealed class HeroIdentityExpectation { }
    public sealed class SecondHeroPowerChoiceExpectation { }
    public sealed class SecondHeroPowerChoiceBatchObservation { }
    public sealed class SecondHeroPowerChoiceSelection { }
    public sealed class SecondHeroPowerEntityObservation { }
    public sealed class ObservedTeammateGoldTransfer { }
}

namespace HearthDb
{
    public sealed class Card
    {
        public List<string> Mechanics = new List<string>();
        public string Text = "";
        public int TechLevel;
    }

    public static class Cards
    {
        public static readonly Dictionary<string, Card> All = new Dictionary<string, Card>();
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
            NormalizesBlankBundleModelIdentity();
            PreservesProgrammerErrorsFromRealProductionComposition();
            ComposesFactoryForPreAcceptanceEffectEvaluation();
            RejectsUnsafePreAcceptanceEffectEvaluationComposition();
            KeepsProductionFactoryFailClosedForPreAcceptanceArtifact();
            ComposesFactoryWithRealProductionPipeline();
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
            Gold = 2,
            EffectiveRules = new EffectiveGameRules { ManualRefreshAllowed = true },
        });
        AssertEqual(FallbackReason.ModelUnavailable, diagnostic.FallbackReason,
            "missing confidence calibration fallback");
        AssertEqual(0d, diagnostic.Confidence, "diagnostic confidence");
        AssertTrue(diagnostic.Explanation.IndexOf("remaining gold", StringComparison.Ordinal) >= 0,
            "contribution-derived diagnostic explanation");
        AssertTrue(diagnostic.Explanation.IndexOf("shadow-only", StringComparison.Ordinal) < 0,
            "legacy placeholder explanation removed");
    }

    private static void ComposesFactoryWithRealProductionPipeline()
    {
        var artifact = CreateAcceptedArtifact();
        RankingModelPayload approved;
        using (var approvalStream = new MemoryStream(artifact))
            approved = RankingModelLoader.Load(approvalStream);
        var runtimeContext = new RankingModelRuntimeContext(
            approved.GameBuild, approved.SeasonId, approved.CardPoolIdentity, true,
            approved.PayloadSha256, approved.ModelId, approved.TrainingSetIdentity,
            approved.SeasonRange);
        var extension = new ExactSeasonExtension(
            approved.GameBuild, approved.SeasonId, approved.CardPoolIdentity);

        DecisionPolicyBundle bundle;
        using (var factoryStream = new MemoryStream(artifact))
            bundle = DecisionPolicyFactory.Create(
                new DecisionEngine(), factoryStream, extension, runtimeContext);

        UnifiedRankingPolicy direct;
        using (var directStream = new MemoryStream(artifact))
            direct = UnifiedRankingPolicy.CreateProduction(
                RankingModelLoader.LoadForRuntime(directStream, runtimeContext), extension);

        var factoryResult = bundle.Unified.Decide(RefreshOnlyState(approved));
        var directResult = direct.Decide(RefreshOnlyState(approved));
        AssertEqual("Refresh|-1||", ActionKeys(factoryResult), "factory real pipeline action keys");
        AssertEqual(ActionKeys(directResult), ActionKeys(factoryResult), "factory/direct action keys");
        AssertEqual(directResult.Confidence, factoryResult.Confidence, "factory/direct confidence");
        AssertEqual(directResult.PlannerStatus, factoryResult.PlannerStatus,
            "factory/direct planner status");
        AssertEqual(directResult.FallbackReason, factoryResult.FallbackReason,
            "factory/direct fallback reason");
        AssertEqual(approved.ModelId, bundle.ModelId, "factory model identity");
        AssertEqual(approved.PayloadSha256, bundle.ModelPayloadSha256,
            "factory model checksum");
    }

    private static void ComposesFactoryForPreAcceptanceEffectEvaluation()
    {
        var artifact = CreateEvaluationArtifact();
        RankingModelPayload model;
        using (var modelStream = new MemoryStream(artifact))
            model = RankingModelLoader.Load(modelStream);
        var extension = new ExactSeasonExtension(
            model.GameBuild, model.SeasonId, model.CardPoolIdentity);

        DecisionPolicyBundle bundle;
        using (var factoryStream = new MemoryStream(artifact))
            bundle = DecisionPolicyFactory.CreateForEffectEvaluation(
                new DecisionEngine(), factoryStream, extension, EvaluationContext(model));

        AssertEqual(model.ModelId, bundle.ModelId, "evaluation factory model identity");
        AssertEqual(model.PayloadSha256, bundle.ModelPayloadSha256,
            "evaluation factory model checksum");
        AssertEqual("Refresh|-1||", ActionKeys(bundle.Unified.Decide(RefreshOnlyState(model))),
            "evaluation factory candidate surface");
    }

    private static void RejectsUnsafePreAcceptanceEffectEvaluationComposition()
    {
        AssertEvaluationFactoryRejects(
            CreateAcceptedArtifact(), new ExactSeasonExtension(
                "known-build", "known-season", "known-pool"),
            "evaluation factory accepted artifact");
        AssertEvaluationFactoryRejects(
            CreateArtifact("blocked-by-season-data", "synthetic-model", "synthetic-build",
                "synthetic-season", "synthetic-pool"),
            new ExactSeasonExtension("synthetic-build", "synthetic-season", "synthetic-pool"),
            "evaluation factory synthetic artifact");
        AssertEvaluationFactoryRejects(
            CreateArtifact("blocked-by-season-data", "unknown", "known-build",
                "known-season", "known-pool"),
            new ExactSeasonExtension("known-build", "known-season", "known-pool"),
            "evaluation factory unknown artifact identity");
        AssertEvaluationFactoryRejects(
            CreateEvaluationArtifact(),
            new ExactSeasonExtension("other-build", "known-season", "known-pool"),
            "evaluation factory season extension mismatch");
        AssertEvaluationFactoryRejects(CreateEvaluationArtifact(),
            new ExactSeasonExtension("known-build", "known-season", "known-pool"),
            "evaluation factory protocol mismatch", model => EvaluationContext(model,
                protocolId: "unknown"));
        AssertEvaluationFactoryRejects(CreateEvaluationArtifact(),
            new ExactSeasonExtension("known-build", "known-season", "known-pool"),
            "evaluation factory protocol version mismatch", model => EvaluationContext(model,
                protocolVersion: "v1"));
        AssertEvaluationFactoryRejects(CreateEvaluationArtifact(),
            new ExactSeasonExtension("known-build", "known-season", "known-pool"),
            "evaluation factory protocol version leading zero", model => EvaluationContext(model,
                protocolVersion: "01.0.0"));
        AssertEvaluationFactoryRejects(CreateEvaluationArtifact(),
            new ExactSeasonExtension("known-build", "known-season", "known-pool"),
            "evaluation factory benchmark mismatch", model => EvaluationContext(model,
                benchmarkId: "other-benchmark"));
        AssertEvaluationFactoryRejects(CreateEvaluationArtifact(),
            new ExactSeasonExtension("known-build", "known-season", "known-pool"),
            "evaluation factory approval after freeze", model => EvaluationContext(model,
                approvedAtUtc: new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
                frozenAtUtc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        AssertEvaluationFactoryRejects(CreateEvaluationArtifact(),
            new ExactSeasonExtension("known-build", "known-season", "known-pool"),
            "evaluation factory checksum mismatch", model => EvaluationContext(model,
                candidateModelPayloadSha256: new string('a', 64)));
        AssertEvaluationFactoryRejects(CreateEvaluationArtifact(),
            new ExactSeasonExtension("known-build", "known-season", "known-pool"),
            "evaluation factory model mismatch", model => EvaluationContext(model,
                candidateModelId: "other-model"));
        AssertEvaluationFactoryRejects(CreateEvaluationArtifact(),
            new ExactSeasonExtension("known-build", "known-season", "known-pool"),
            "evaluation factory calibration mismatch", model => EvaluationContext(model,
                calibrationModelId: "other-model"));
        AssertEvaluationFactoryRejects(CreateEvaluationArtifact(),
            new ExactSeasonExtension("known-build", "known-season", "known-pool"),
            "evaluation factory unresolved context build", model => EvaluationContext(model,
                contextGameBuild: "unknown"));
        AssertEvaluationFactoryRejects(CreateEvaluationArtifact(),
            new ExactSeasonExtension("known-build", "known-season", "known-pool"),
            "evaluation factory unresolved context season", model => EvaluationContext(model,
                contextSeasonId: "synthetic-season"));
        AssertEvaluationFactoryRejects(CreateEvaluationArtifact(),
            new ExactSeasonExtension("known-build", "known-season", "known-pool"),
            "evaluation factory unresolved context card pool", model => EvaluationContext(model,
                contextCardPoolIdentity: " \t"));
        AssertEvaluationFactoryRejects(CreateEvaluationArtifact(),
            new ExactSeasonExtension("known-build", "known-season", "known-pool"),
            "evaluation factory unresolved context training set", model => EvaluationContext(model,
                contextTrainingSetIdentity: "unknown"));
        AssertEvaluationFactoryRejects(CreateEvaluationArtifact(),
            new ExactSeasonExtension("known-build", "known-season", "known-pool"),
            "evaluation factory unresolved context season range", model => EvaluationContext(model,
                contextSeasonRange: "synthetic-range"));
        AssertEvaluationFactoryRejects(CreateEvaluationArtifact(),
            new ExactSeasonExtension("known-build", "known-season", "known-pool"),
            "evaluation factory unresolved calibration identity", model => EvaluationContext(model,
                calibrationIdentity: "unknown", approvalCalibrationIdentity: "unknown"));
        AssertEvaluationFactoryRejects(CreateEvaluationArtifact(),
            new ExactSeasonExtension("known-build", "known-season", "known-pool"),
            "evaluation factory unresolved calibration holdout", model => EvaluationContext(model,
                calibrationHoldoutIdentity: "synthetic-holdout",
                approvalHoldoutIdentity: "synthetic-holdout"));
        AssertEvaluationFactoryRejects(CreateEvaluationArtifact(),
            new ExactSeasonExtension("known-build", "known-season", "known-pool"),
            "evaluation factory non-finite margin scale", model => EvaluationContext(model,
                marginScale: double.NaN));
        AssertEvaluationFactoryRejects(CreateEvaluationArtifact(),
            new ExactSeasonExtension("known-build", "known-season", "known-pool"),
            "evaluation factory non-positive margin scale", model => EvaluationContext(model,
                marginScale: 0d));
        AssertEvaluationFactoryRejects(CreateEvaluationArtifact(),
            new ExactSeasonExtension("known-build", "known-season", "known-pool"),
            "evaluation factory invalid semantic coverage floor", model => EvaluationContext(model,
                semanticCoverageFloor: double.PositiveInfinity));
        AssertEvaluationFactoryRejects(CreateEvaluationArtifact(),
            new ExactSeasonExtension("known-build", "known-season", "known-pool"),
            "evaluation factory invalid confidence threshold", model => EvaluationContext(model,
                confidenceThreshold: -0.01d));
        AssertEvaluationFactoryRejects(CreateEvaluationArtifact(),
            new ExactSeasonExtension("known-build", "known-season", "known-pool"),
            "evaluation factory invalid planning threshold", model => EvaluationContext(model,
                planningThreshold: 1.01d));

        RankingModelPayload nullablePlanningModel;
        var nullablePlanningArtifact = CreateEvaluationArtifact();
        using (var nullablePlanningStream = new MemoryStream(nullablePlanningArtifact))
            nullablePlanningModel = RankingModelLoader.Load(nullablePlanningStream);
        using (var nullablePlanningStream = new MemoryStream(nullablePlanningArtifact))
            DecisionPolicyFactory.CreateForEffectEvaluation(new DecisionEngine(),
                nullablePlanningStream, new ExactSeasonExtension("known-build", "known-season",
                    "known-pool"), EvaluationContext(nullablePlanningModel,
                    planningThreshold: null));
    }

    private static void AssertEvaluationFactoryRejects(byte[] artifact,
        ISeasonFeatureExtension extension, string label)
    {
        AssertEvaluationFactoryRejects(artifact, extension, label,
            model => EvaluationContext(model));
    }

    private static void AssertEvaluationFactoryRejects(byte[] artifact,
        ISeasonFeatureExtension extension, string label,
        Func<RankingModelPayload, EffectEvaluationContext> contextFactory)
    {
        AssertModelValidation(() =>
        {
            RankingModelPayload model;
            using (var modelStream = new MemoryStream(artifact))
                model = RankingModelLoader.Load(modelStream);
            using (var stream = new MemoryStream(artifact))
                DecisionPolicyFactory.CreateForEffectEvaluation(
                    new DecisionEngine(), stream, extension, contextFactory(model));
        }, label);
    }

    private static void KeepsProductionFactoryFailClosedForPreAcceptanceArtifact()
    {
        var artifact = CreateEvaluationArtifact();
        DecisionPolicyBundle bundle;
        using (var stream = new MemoryStream(artifact))
            bundle = DecisionPolicyFactory.Create(new DecisionEngine(), stream,
                new ExactSeasonExtension("known-build", "known-season", "known-pool"));

        AssertEqual("unknown", bundle.ModelId, "production factory blocks evaluation model");
        AssertEqual(FallbackReason.ModelUnavailable,
            bundle.Unified.Decide(RefreshOnlyState(ProductionModel())).FallbackReason,
            "production factory evaluation fallback");
    }

    private static EffectEvaluationContext EvaluationContext(RankingModelPayload model,
        string protocolId = "known-protocol", string protocolVersion = "1.0.0",
        string benchmarkId = "season-2026-next-v1",
        string candidateModelId = null, string candidateModelPayloadSha256 = null,
        string calibrationModelId = null, DateTime? approvedAtUtc = null,
        DateTime? frozenAtUtc = null, string contextGameBuild = null,
        string contextSeasonId = null, string contextCardPoolIdentity = null,
        string contextTrainingSetIdentity = null, string contextSeasonRange = null,
        string calibrationHoldoutIdentity = "known-holdout",
        string calibrationIdentity = "known-calibration",
        string approvalHoldoutIdentity = "known-holdout",
        string approvalCalibrationIdentity = "known-calibration",
        double marginScale = 1d, double semanticCoverageFloor = 0.5d,
        double confidenceThreshold = 0.5d, double? planningThreshold = 0.1d)
    {
        var modelId = candidateModelId ?? model.ModelId;
        var calibrationId = calibrationModelId ?? model.ModelId;
        var gameBuild = contextGameBuild ?? model.GameBuild;
        var seasonId = contextSeasonId ?? model.SeasonId;
        var cardPoolIdentity = contextCardPoolIdentity ?? model.CardPoolIdentity;
        var trainingSetIdentity = contextTrainingSetIdentity ?? model.TrainingSetIdentity;
        var seasonRange = contextSeasonRange ?? model.SeasonRange;
        var calibration = new ConfidenceCalibration("accepted", gameBuild, seasonId,
            cardPoolIdentity, trainingSetIdentity, calibrationHoldoutIdentity,
            calibrationIdentity, calibrationId, marginScale, semanticCoverageFloor,
            confidenceThreshold, planningThreshold);
        var approval = new ConfidenceCalibrationApprovalContext(approvalHoldoutIdentity,
            approvalCalibrationIdentity, model.ModelId);
        return new EffectEvaluationContext(protocolId, protocolVersion, "owner-approved", "owner",
            approvedAtUtc ?? new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            frozenAtUtc ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), new string('c', 64),
            benchmarkId, "legacy-v1.1.0", "unified-linear-v1", modelId,
            candidateModelPayloadSha256 ?? model.PayloadSha256, "known-holdout",
            new string('d', 64), new string('d', 64), gameBuild, seasonId,
            cardPoolIdentity, trainingSetIdentity, seasonRange,
            calibration, approval);
    }

    private static void NormalizesBlankBundleModelIdentity()
    {
        var bundle = new DecisionPolicyBundle(null, null, null, " \t", "\r\n");

        AssertEqual("unknown", bundle.ModelId, "blank bundle model identity");
        AssertEqual("unknown", bundle.ModelPayloadSha256, "blank bundle model checksum");
    }

    private static void PreservesProgrammerErrorsFromRealProductionComposition()
    {
        var artifact = CreateAcceptedArtifact();
        RankingModelPayload approved;
        using (var approvalStream = new MemoryStream(artifact))
            approved = RankingModelLoader.Load(approvalStream);
        var runtimeContext = new RankingModelRuntimeContext(
            approved.GameBuild, approved.SeasonId, approved.CardPoolIdentity, true,
            approved.PayloadSha256, approved.ModelId, approved.TrainingSetIdentity,
            approved.SeasonRange);
        var expected = new InvalidOperationException("deliberate production composition failure");

        try
        {
            using (var factoryStream = new MemoryStream(artifact))
                DecisionPolicyFactory.Create(new DecisionEngine(), factoryStream,
                    new ThrowingSeasonExtension(expected), runtimeContext);
        }
        catch (InvalidOperationException actual)
        {
            AssertTrue(object.ReferenceEquals(expected, actual),
                "programmer error exception identity");
            return;
        }

        throw new InvalidOperationException("production composition programmer error was swallowed");
    }

    private static byte[] CreateAcceptedArtifact()
    {
        return CreateArtifact("accepted", "known-model", "known-build",
            "known-season", "known-pool");
    }

    private static byte[] CreateEvaluationArtifact()
    {
        return CreateArtifact("blocked-by-season-data", "known-model", "known-build",
            "known-season", "known-pool");
    }

    private static byte[] CreateArtifact(string effectAcceptanceStatus, string modelId,
        string gameBuild, string seasonId, string cardPoolIdentity)
    {
        var fixturePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
            "linear-v1.valid.envelope.json");
        RankingModelPayload model;
        using (var fixture = File.OpenRead(fixturePath))
            model = RankingModelLoader.Load(fixture);
        model.ModelId = modelId;
        model.TrainingSetIdentity = "known-training-set";
        model.SeasonRange = "known-season";
        model.GameBuild = gameBuild;
        model.SeasonId = seasonId;
        model.CardPoolIdentity = cardPoolIdentity;
        model.EffectAcceptanceStatus = effectAcceptanceStatus;

        var payload = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(model));
        string checksum;
        using (var sha256 = SHA256.Create())
            checksum = string.Concat(sha256.ComputeHash(payload)
                .Select(value => value.ToString("x2")));
        var envelope = new RankingModelEnvelope
        {
            ArtifactSchemaVersion = 1,
            PayloadBase64 = Convert.ToBase64String(payload),
            PayloadSha256 = checksum,
        };
        return Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(envelope));
    }

    private static GameState RefreshOnlyState(RankingModelPayload model)
    {
        return new GameState
        {
            GameActive = true,
            GameBuild = model.GameBuild,
            SeasonId = model.SeasonId,
            CardPoolIdentity = model.CardPoolIdentity,
            Gold = 1,
            ShopMinions = new List<MinionData>(),
            BoardMinions = new List<MinionData>(),
            HandMinions = new List<MinionData>(),
            HeroPowers = new List<HeroPowerState>(),
            EffectiveRules = new EffectiveGameRules { ManualRefreshAllowed = true },
        };
    }

    private static string ActionKeys(DecisionResult result)
    {
        return string.Join(";", result.RankedActions.Select(action => action.ActionKey));
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
        var model = Model("remaining_gold", 1d, 0d);
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

    private sealed class ThrowingSeasonExtension : ISeasonFeatureExtension
    {
        private readonly InvalidOperationException _failure;

        public string GameBuild { get { throw _failure; } }
        public string SeasonId { get { return "known-season"; } }
        public string CardPoolIdentity { get { return "known-pool"; } }

        public ThrowingSeasonExtension(InvalidOperationException failure)
        {
            _failure = failure;
        }

        public bool TryEvaluate(GameState state, ActionDescriptor action, GameState nextState,
            out double value)
        {
            value = 0d;
            return false;
        }
    }
}
