using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BobCoach.Engine;
using BobCoach.Engine.Decision;

namespace BobCoach.Engine
{
    public sealed class GameState
    {
        public bool GameActive;
        public bool IsDuos;
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
        public HashSet<string> AvailableTribes = new HashSet<string>();
        public List<MinionData> BoardMinions = new List<MinionData>();
        public List<MinionData> HandMinions = new List<MinionData>();
        public List<MinionData> ShopMinions = new List<MinionData>();
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

    internal static class BobCoachDataPaths
    {
        internal static string GetPath(params string[] segments)
        {
            return Path.Combine(Path.GetTempPath(), Path.Combine(segments));
        }
    }
}

namespace BobCoach.Engine.Decision
{
    public sealed class ActionDescriptor
    {
        public string ActionKey { get; set; }
    }

    public sealed class DecisionResult
    {
        public DecisionResult(string policyId) { PolicyId = policyId; }
        public string PolicyId { get; private set; }
    }
}

internal static class AlgorithmDecisionEventBehavior
{
    private static int Main()
    {
        try
        {
            PreservesDetachedPreActionStateAndFingerprints();
            AppendsIdempotentObservationEventsWithoutQualityLabels();
            ReusesOnlyOpenUnchangedDecisionGenerations();
            RejectsMalformedPriorEventLogs();
            RejectsTrailingContentInPriorEventLogs();
            Console.WriteLine("PASS decision events preserve pre-action state and temporal alignment");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL " + ex.Message);
            return 1;
        }
    }

    private static void PreservesDetachedPreActionStateAndFingerprints()
    {
        var before = State(5, 4);
        before.BoardMinions.Add(new MinionData { CardId = "BOARD_A", Attack = 3, Health = 4 });
        before.AvailableTribes.Add("Beast");
        var candidates = Candidates();

        var record = DecisionEventRecord.Create("decision-1", "game-1", before, candidates);
        var stateFingerprint = record.StateFingerprint;
        var candidateFingerprint = record.CandidateFingerprint;
        before.Gold = 2;
        before.BoardMinions[0].Attack = 99;
        before.BoardMinions.Add(new MinionData { CardId = "BOARD_B" });
        before.AvailableTribes.Add("Dragon");
        candidates[0].ActionKey = "Mutated|0||";

        AssertEqual(5, record.State.Gold, "snapshot must detach scalar state");
        AssertEqual(3, record.State.BoardMinions[0].Attack, "snapshot must detach minions");
        AssertEqual(1, record.State.BoardMinions.Count, "snapshot must detach lists");
        AssertEqual(1, record.State.AvailableTribes.Count, "snapshot must detach sets");
        AssertEqual(stateFingerprint, record.StateFingerprint, "state fingerprint must remain unchanged");
        AssertEqual(candidateFingerprint, record.CandidateFingerprint, "candidate fingerprint must remain unchanged");
    }

    private static void AppendsIdempotentObservationEventsWithoutQualityLabels()
    {
        var path = NewPath();
        var recorder = new DecisionEventRecorder(path);
        var record = DecisionEventRecord.Create("decision-1", "game-1", State(5, 4), Candidates());
        recorder.RecordCreated(record);
        recorder.RecordCreated(record);
        recorder.AttachObservedAction("decision-1", "BuyMinion|0|CARD_A|shop", "2026-07-31T00:00:02Z");
        AssertThrows<ArgumentException>(() => recorder.AttachRecommendation("",
            new DecisionResult("legacy-v1.1.0"), "2026-07-31T00:00:03Z"),
            "blank recommendation decision ID");
        AssertThrows<ArgumentException>(() => recorder.AttachObservedAction(" ",
            "BuyMinion|0|CARD_A|shop", "2026-07-31T00:00:03Z"),
            "blank observed-action decision ID");

        var lines = File.ReadAllLines(path);
        AssertEqual(1, lines.Count(x => x.Contains("DecisionCreated")), "created dedupe");
        AssertTrue(lines.Last().Contains("PlayerActionObserved"), "later event type");
        AssertTrue(!lines.Last().Contains("acceptedActionKeys"), "behavior is not a quality label");
        var resumed = new DecisionEventRecorder(path);
        resumed.RecordCreated(record);
        AssertEqual(2, File.ReadAllLines(path).Length, "persisted created dedupe");
    }

    private static void ReusesOnlyOpenUnchangedDecisionGenerations()
    {
        var recorder = new DecisionEventRecorder(NewPath());
        var lifecycle = new DecisionLifecycleTracker("game-1", recorder);
        var before = State(5, 4);
        var candidates = Candidates();
        var first = lifecycle.BeginOrReuse(before, candidates);
        var unchanged = lifecycle.BeginOrReuse(Clone(before), candidates);
        AssertEqual(first.DecisionId, unchanged.DecisionId, "unchanged render reuses decision");

        lifecycle.AttachObservedActions(new[] { "BuyMinion|0|CARD_A|shop", "effect:shop-refill" },
            "2026-07-31T00:00:02Z");
        var afterAction = lifecycle.BeginOrReuse(before, candidates);
        AssertTrue(first.DecisionId != afterAction.DecisionId,
            "a player action starts a new decision even when the state fingerprint later repeats");
    }

    private static void RejectsMalformedPriorEventLogs()
    {
        var path = NewPath();
        File.WriteAllText(path, "{bad json}" + Environment.NewLine);
        AssertThrows<InvalidDataException>(() => new DecisionEventRecorder(path),
            "malformed existing event log");
    }

    private static void RejectsTrailingContentInPriorEventLogs()
    {
        var path = NewPath();
        File.WriteAllText(path,
            "{\"eventKey\":\"decision-1|created\"} {\"eventKey\":\"extra\"}"
            + Environment.NewLine);
        AssertThrows<InvalidDataException>(() => new DecisionEventRecorder(path),
            "trailing content in existing event log");
    }

    private static GameState State(int gold, int turn)
    {
        return new GameState
        {
            GameActive = true,
            HeroCardId = "HERO_01",
            Gold = gold,
            MaxGold = 10,
            TavernTier = 3,
            TavernUpgradeCost = 7,
            Health = 28,
            MaxHealth = 30,
            Turn = turn,
        };
    }

    private static GameState Clone(GameState state)
    {
        var result = State(state.Gold, state.Turn);
        result.MaxGold = state.MaxGold;
        result.TavernTier = state.TavernTier;
        result.TavernUpgradeCost = state.TavernUpgradeCost;
        result.Health = state.Health;
        result.MaxHealth = state.MaxHealth;
        result.HeroCardId = state.HeroCardId;
        return result;
    }

    private static List<ActionDescriptor> Candidates()
    {
        return new List<ActionDescriptor>
        {
            new ActionDescriptor { ActionKey = "Refresh|-1||" },
            new ActionDescriptor { ActionKey = "BuyMinion|0|CARD_A|shop" },
        };
    }

    private static string NewPath()
    {
        return Path.Combine(Path.GetTempPath(), "bobcoach_decision_events_" + Guid.NewGuid().ToString("N") + ".jsonl");
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
}
