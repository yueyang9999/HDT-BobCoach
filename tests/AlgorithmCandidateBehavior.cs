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
        public string PurchaseSource = "";
        public string Description;
    }

    public sealed class MinionData
    {
        public int EntityId;
        public bool IsSpell;
        public int Cost;
    }

    public sealed class TrinketOption { public int EntityId; }
    public sealed class HeroPowerState
    {
        public string CardId;
        public int Cost;
        public bool Exhausted;
        public bool IsActive;
        public bool IsUnlocked;
    }

    public sealed class EffectiveGameRules
    {
        public static readonly EffectiveGameRules Default = new EffectiveGameRules();
        public bool ManualRefreshAllowed = true;
        public int MaxTavernTier = 6;
        public TeammateGoldTransferRule TeammateGoldTransfer;
    }

    public sealed class TeammateGoldTransferRule
    {
        public string ActionCardId;
        public int GoldPerUse;
        public int MaxPerTurn;
    }

    public sealed class GameState
    {
        public bool GameActive;
        public bool IsDuos;
        public int Gold;
        public int Turn = 1;
        public int TavernTier;
        public int TavernUpgradeCost;
        public int HeroPowerCost;
        public int MaxBoardSlots = 7;
        public int FreeRefreshCount;
        public bool FrozenShop;
        public string HeroCardId = "";
        public EffectiveGameRules EffectiveRules;
        public int TeammateGoldTransfersUsedThisTurn;
        public List<MinionData> BoardMinions = new List<MinionData>();
        public List<MinionData> HandMinions = new List<MinionData>();
        public List<MinionData> ShopMinions = new List<MinionData>();
        public List<TrinketOption> TrinketOffer = new List<TrinketOption>();
        public List<TrinketOption> DiscoverOptions = new List<TrinketOption>();
        public List<HeroPowerState> HeroPowers = new List<HeroPowerState>();
    }

    public sealed class ActionEnumerator
    {
        public List<GameAction> Actions = new List<GameAction>();

        public List<GameAction> Enumerate(GameState state, string heroCardId)
        {
            return new List<GameAction>(Actions);
        }
    }

    public static class GameRuleEvaluator
    {
        public static int GetPurchaseCost(GameState state, MinionData card,
            string heroCardId, EffectiveGameRules rules)
        {
            return card.Cost;
        }

        public static int GetRefreshCost(GameState state, string heroCardId,
            EffectiveGameRules rules)
        {
            return state.FreeRefreshCount > 0 ? 0 : 1;
        }

        public static int? GetUpgradeCost(GameState state, EffectiveGameRules rules)
        {
            return state.TavernTier >= rules.MaxTavernTier ? (int?)null : state.TavernUpgradeCost;
        }
    }

    public static class TeammateGoldTransferEvaluator
    {
        public static int GetUsedCount(GameState state, int turn)
        {
            return state.Turn == turn ? state.TeammateGoldTransfersUsedThisTurn : 0;
        }
    }
}

internal static class AlgorithmCandidateBehavior
{
    private static int Main()
    {
        try
        {
            CreatesDeterministicActionKeys();
            RejectsOnlyHardConstraints();
            ChargesAndGatesDuosGoldTransfers();
            CoversAllPublicRejectReasonCodes();
            GeneratesCandidatesInStableKeyOrder();
            Console.WriteLine("PASS candidate identities are stable and hard constraints remain strategy-neutral");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL " + ex.Message);
            return 1;
        }
    }

    private static void CreatesDeterministicActionKeys()
    {
        var state = new GameState { GameActive = true, Gold = 2, TavernTier = 1, TavernUpgradeCost = 5 };
        var refresh = new GameAction { Type = ActionType.Refresh, TargetIndex = -1, CardId = "", PurchaseSource = "" };
        var first = ActionDescriptor.From(state, refresh);
        var second = ActionDescriptor.From(state, refresh);

        AssertEqual(first.ActionKey, second.ActionKey, "deterministic key");
        AssertEqual("Refresh|-1||", first.ActionKey, "key format");
    }

    private static void RejectsOnlyHardConstraints()
    {
        var state = new GameState { GameActive = true, Gold = 2, TavernTier = 1, TavernUpgradeCost = 5 };
        var refresh = ActionDescriptor.From(state,
            new GameAction { Type = ActionType.Refresh, TargetIndex = -1, CardId = "", PurchaseSource = "" });
        var upgrade = ActionDescriptor.From(state,
            new GameAction { Type = ActionType.Upgrade, TargetIndex = -1 });
        var filter = new HardConstraintFilter();
        var result = filter.Apply(state, new[] { refresh, upgrade });

        AssertTrue(result.All(x => x.Action.Type != ActionType.Upgrade), "unaffordable upgrade rejected");
        AssertTrue(result.Any(x => x.Action.Type == ActionType.Refresh), "strategy-neutral refresh retained");
        AssertEqual("legal", filter.Evaluate(state, refresh).ReasonCode, "refresh reason");
        AssertEqual("insufficient-gold", filter.Evaluate(state, upgrade).ReasonCode, "upgrade reason");
    }

    private static void GeneratesCandidatesInStableKeyOrder()
    {
        var state = new GameState { GameActive = true, Gold = 5, TavernTier = 1, TavernUpgradeCost = 5 };
        var enumerator = new ActionEnumerator();
        enumerator.Actions.Add(new GameAction { Type = ActionType.Upgrade, TargetIndex = -1 });
        enumerator.Actions.Add(new GameAction { Type = ActionType.Refresh, TargetIndex = -1 });

        var candidates = new CandidateGenerator(enumerator).Generate(state);

        AssertEqual(2, candidates.Count, "candidate count");
        AssertEqual("Refresh|-1||", candidates[0].ActionKey, "stable first candidate");
        AssertEqual("Upgrade|-1||", candidates[1].ActionKey, "stable second candidate");
    }

    private static void ChargesAndGatesDuosGoldTransfers()
    {
        var state = new GameState
        {
            GameActive = true,
            IsDuos = true,
            Gold = 2,
            Turn = 3,
            EffectiveRules = new EffectiveGameRules
            {
                TeammateGoldTransfer = new TeammateGoldTransferRule
                {
                    ActionCardId = "duos_transfer",
                    GoldPerUse = 2,
                    MaxPerTurn = 2,
                }
            }
        };
        state.HeroPowers.Add(new HeroPowerState
        {
            CardId = "duos_transfer",
            IsActive = true,
            IsUnlocked = true,
        });
        var action = new GameAction
        {
            Type = ActionType.SendGoldToTeammate,
            CardId = "duos_transfer",
        };
        var filter = new HardConstraintFilter();
        var candidate = ActionDescriptor.From(state, action);

        AssertEqual(2, candidate.GoldCost, "duos transfer gold cost");
        AssertEqual(SimulatorSupport.Partial, candidate.SimulatorSupport,
            "duos transfer simulator support");
        AssertEqual("legal", filter.Evaluate(state, candidate).ReasonCode,
            "active duos transfer is legal");

        state.Gold = 1;
        AssertEqual("insufficient-gold", filter.Evaluate(state,
            ActionDescriptor.From(state, action)).ReasonCode, "duos transfer requires gold");

        state.Gold = 2;
        state.TeammateGoldTransfersUsedThisTurn = 2;
        AssertEqual("known-invalid-irreversible", filter.Evaluate(state,
            ActionDescriptor.From(state, action)).ReasonCode, "duos transfer turn limit");

        state.TeammateGoldTransfersUsedThisTurn = 0;
        state.HeroPowers[0].Exhausted = true;
        AssertEqual("known-invalid-irreversible", filter.Evaluate(state,
            ActionDescriptor.From(state, action)).ReasonCode, "exhausted duos transfer");

        state.HeroPowers[0].Exhausted = false;
        state.HeroPowers[0].IsActive = false;
        AssertEqual("known-invalid-irreversible", filter.Evaluate(state,
            ActionDescriptor.From(state, action)).ReasonCode, "inactive duos transfer");

        state.HeroPowers[0].IsActive = true;
        state.IsDuos = false;
        AssertEqual("known-invalid-irreversible", filter.Evaluate(state,
            ActionDescriptor.From(state, action)).ReasonCode, "non-duos transfer");
    }

    private static void CoversAllPublicRejectReasonCodes()
    {
        var state = new GameState { GameActive = true, Gold = 2, TavernTier = 1, TavernUpgradeCost = 5 };
        var filter = new HardConstraintFilter();

        AssertEqual("invalid-source-index", filter.Evaluate(state,
            ActionDescriptor.From(state, new GameAction { Type = ActionType.BuyMinion, TargetIndex = 0 })).ReasonCode,
            "buy action source index");

        state.EffectiveRules = new EffectiveGameRules { ManualRefreshAllowed = false };
        AssertEqual("invalid-target", filter.Evaluate(state,
            ActionDescriptor.From(state, new GameAction { Type = ActionType.Refresh })).ReasonCode,
            "disabled refresh target");

        AssertEqual("known-invalid-irreversible", filter.Evaluate(state, null).ReasonCode,
            "missing candidate");
    }

    private static void AssertTrue(bool value, string label)
    {
        if (!value) throw new InvalidOperationException(label);
    }

    private static void AssertEqual<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(label + " expected=" + expected + " actual=" + actual);
    }
}
