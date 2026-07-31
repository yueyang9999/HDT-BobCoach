using System;
using System.Collections.Generic;
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
        public string PurchaseSource = "";
        public string Description;
    }

    public sealed class GameState
    {
        public bool GameActive;
        public int HeroPowerCost;
        public string HeroCardId = "";
        public EffectiveGameRules EffectiveRules;
        public List<MinionData> BoardMinions = new List<MinionData>();
        public List<MinionData> ShopMinions = new List<MinionData>();
        public List<TrinketOption> TrinketOffer = new List<TrinketOption>();
        public List<TrinketOption> DiscoverOptions = new List<TrinketOption>();
        public List<HeroPowerState> HeroPowers = new List<HeroPowerState>();
    }

    public sealed class MinionData { public int EntityId; }
    public sealed class TrinketOption { public int EntityId; }
    public sealed class HeroPowerState { public string CardId; public int Cost; }

    public sealed class EffectiveGameRules
    {
        public static readonly EffectiveGameRules Default = new EffectiveGameRules();
        public TeammateGoldTransferRule TeammateGoldTransfer;
    }

    public sealed class TeammateGoldTransferRule
    {
        public string ActionCardId;
        public int GoldPerUse;
    }

    public static class GameRuleEvaluator
    {
        public static int GetPurchaseCost(GameState state, MinionData minion,
            string heroCardId, EffectiveGameRules rules) { return 0; }
        public static int GetRefreshCost(GameState state, string heroCardId,
            EffectiveGameRules rules) { return 0; }
        public static int? GetUpgradeCost(GameState state,
            EffectiveGameRules rules) { return 0; }
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
}

internal static class AlgorithmPolicyContractBehavior
{
    private static int Main()
    {
        try
        {
            PreservesFrozenLegacyAction();
            ReportsUnavailablePolicyFallback();
            RejectsUnavailablePolicyWithoutFallbackReason();
            Console.WriteLine("PASS legacy policy preserves v1.1.0 action and decision contracts");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL " + ex.Message);
            return 1;
        }
    }

    private static void PreservesFrozenLegacyAction()
    {
        var expected = new GameAction { Type = ActionType.Refresh, TargetIndex = -1 };
        var policy = new LegacyDecisionPolicy(new FakeProvider(expected));
        var result = policy.Decide(new GameState { GameActive = true });

        AssertEqual("legacy-v1.1.0", policy.PolicyId, "policy id");
        AssertSame(expected, result.SelectedAction, "selected action");
        AssertEqual(1, result.RankedActions.Count, "ranked count");
        AssertEqual("Refresh|-1||", result.RankedActions[0].ActionKey, "legacy action key");
        AssertEqual(FallbackReason.None, result.FallbackReason, "fallback");
        AssertEqual(PlannerStatus.NotRequested, result.PlannerStatus, "planner status");
    }

    private static void ReportsUnavailablePolicyFallback()
    {
        var result = UnavailableDecisionPolicy.For(FallbackReason.ModelUnavailable)
            .Decide(new GameState { GameActive = true });

        AssertEqual("unavailable", result.PolicyId, "unavailable policy id");
        AssertEqual(null, result.SelectedAction, "unavailable selected action");
        AssertEqual(0, result.RankedActions.Count, "unavailable ranked count");
        AssertEqual(0d, result.Confidence, "unavailable confidence");
        AssertEqual(FallbackReason.ModelUnavailable, result.FallbackReason, "unavailable fallback");
    }

    private static void RejectsUnavailablePolicyWithoutFallbackReason()
    {
        try
        {
            UnavailableDecisionPolicy.For(FallbackReason.None);
        }
        catch (ArgumentException)
        {
            return;
        }

        throw new InvalidOperationException("unavailable policy accepts no fallback reason");
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

    private sealed class FakeProvider : IDecisionProvider
    {
        private readonly GameAction _action;

        public FakeProvider(GameAction action) { _action = action; }
        public GameAction GetBestAction(GameState state) { return _action; }
        public List<int> GetRecommendedBuyIndices(GameState state, int topN = 3) { return new List<int>(); }
        public LevelUpSuggestion GetLevelUpSuggestion(GameState state) { return new LevelUpSuggestion("", ""); }
    }
}
