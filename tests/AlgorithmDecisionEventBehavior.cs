using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BobCoach.Engine;
using BobCoach.Engine.Decision;
using Newtonsoft.Json.Linq;

namespace BobCoach.Engine
{
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

    public interface IShadowComparisonSink
    {
        void RecordComparison(ShadowComparison comparison);
    }

    public sealed class ShadowComparison
    {
        public string DecisionId { get; set; }
        public string ComparisonId { get; set; }
    }
}

internal static class AlgorithmDecisionEventBehavior
{
    private static int Main()
    {
        try
        {
            PreservesDetachedPreActionStateAndFingerprints();
            CapturesRuleIdentitiesAndDistinguishesUnknownValues();
            CapturesSeasonMechanicDecisionState();
            CapturesSeasonMechanicFingerprintTransitions();
            AppendsIdempotentObservationEventsWithoutQualityLabels();
            WritesModelIdentityIntoEveryEnvelope();
            NormalizesMissingModelIdentityToUnknown();
            ReusesOnlyOpenUnchangedDecisionGenerations();
            AttachesGameOutcomeToLastRelevantDecision();
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

    private static void CapturesRuleIdentitiesAndDistinguishesUnknownValues()
    {
        var baseline = State(5, 4);
        baseline.GameBuild = "12345";
        baseline.SeasonId = "season-9";
        baseline.CardPoolIdentity = "pool-9";
        var baselineRecord = DecisionEventRecord.Create("identity-baseline", "game-1", baseline, Candidates());

        AssertEqual("12345", baselineRecord.State.GameBuild, "snapshot captures game build");
        AssertEqual("season-9", baselineRecord.State.SeasonId, "snapshot captures season ID");
        AssertEqual("pool-9", baselineRecord.State.CardPoolIdentity, "snapshot captures card-pool identity");

        var changedBuild = Clone(baseline);
        changedBuild.GameBuild = "12346";
        var changedBuildRecord = DecisionEventRecord.Create(
            "identity-build", "game-1", changedBuild, Candidates());
        AssertEqual("12346", changedBuildRecord.State.GameBuild,
            "game build changes the snapshot");
        AssertTrue(baselineRecord.StateFingerprint != changedBuildRecord.StateFingerprint,
            "game build changes the state fingerprint");

        var changedSeason = Clone(baseline);
        changedSeason.SeasonId = "season-10";
        var changedSeasonRecord = DecisionEventRecord.Create(
            "identity-season", "game-1", changedSeason, Candidates());
        AssertEqual("season-10", changedSeasonRecord.State.SeasonId,
            "season ID changes the snapshot");
        AssertTrue(baselineRecord.StateFingerprint != changedSeasonRecord.StateFingerprint,
            "season ID changes the state fingerprint");

        var changedCardPool = Clone(baseline);
        changedCardPool.CardPoolIdentity = "pool-10";
        var changedCardPoolRecord = DecisionEventRecord.Create(
            "identity-pool", "game-1", changedCardPool, Candidates());
        AssertEqual("pool-10", changedCardPoolRecord.State.CardPoolIdentity,
            "card-pool identity changes the snapshot");
        AssertTrue(baselineRecord.StateFingerprint != changedCardPoolRecord.StateFingerprint,
            "card-pool identity changes the state fingerprint");

        var missing = State(5, 4);
        missing.GameBuild = null;
        missing.SeasonId = " ";
        missing.CardPoolIdentity = "";
        var unknownRecord = DecisionEventRecord.Create("identity-unknown", "game-1", missing, Candidates());
        AssertEqual(GameStateSnapshot.UnknownIdentity, unknownRecord.State.GameBuild,
            "missing game build is explicit unknown");
        AssertEqual(GameStateSnapshot.UnknownIdentity, unknownRecord.State.SeasonId,
            "missing season ID is explicit unknown");
        AssertEqual(GameStateSnapshot.UnknownIdentity, unknownRecord.State.CardPoolIdentity,
            "missing card-pool identity is explicit unknown");

        AssertIdentityTransitionChangesFingerprint(
            "gameBuild", "game build", state => state.GameBuild,
            snapshot => snapshot.GameBuild, (state, value) => state.GameBuild = value, "12345");
        AssertIdentityTransitionChangesFingerprint(
            "seasonId", "season ID", state => state.SeasonId,
            snapshot => snapshot.SeasonId, (state, value) => state.SeasonId = value, "season-9");
        AssertIdentityTransitionChangesFingerprint(
            "cardPoolIdentity", "card-pool identity", state => state.CardPoolIdentity,
            snapshot => snapshot.CardPoolIdentity, (state, value) => state.CardPoolIdentity = value, "pool-9");
    }

    private static void CapturesSeasonMechanicDecisionState()
    {
        var state = State(5, 4);
        state.CurrentCompGuideTier = "T1";
        state.ActiveTrinkets.Add("TB2_001");
        state.ActiveTrinkets.Add("TB2_002");
        state.TrinketOffer.Add(new TrinketOption
        {
            CardId = "TB2_100",
            TrinketName = "Lesser Test",
            EntityId = 301,
            IsLesser = true,
            Tier = 1,
            Attack = 2,
            Health = 3,
            Tribes = new List<string> { "Beast", "Dragon" }
        });
        state.TrinketOffer.Add(new TrinketOption
        {
            CardId = "TB2_101",
            TrinketName = "Greater Test",
            EntityId = 302,
            IsLesser = false,
            Tier = 3,
            Attack = 4,
            Health = 5,
            Tribes = new List<string> { "Murloc" }
        });

        var record = DecisionEventRecord.Create("season-mechanic", "game-1", state, Candidates());
        AssertEqual(2, DecisionEventRecord.SchemaVersion,
            "decision snapshot schema version must advance with season mechanics");
        AssertEqual("T1", record.State.CurrentCompGuideTier,
            "snapshot captures current comp guide tier");
        AssertEqual(2, record.State.TrinketOffer.Count,
            "snapshot preserves every trinket offer");
        AssertEqual("TB2_100", record.State.TrinketOffer[0].CardId,
            "snapshot preserves trinket card id");
        AssertEqual("Lesser Test", record.State.TrinketOffer[0].TrinketName,
            "snapshot preserves trinket name");
        AssertEqual(301, record.State.TrinketOffer[0].EntityId,
            "snapshot preserves trinket entity id");
        AssertTrue(record.State.TrinketOffer[0].IsLesser,
            "snapshot preserves lesser flag");
        AssertEqual(1, record.State.TrinketOffer[0].Tier,
            "snapshot preserves trinket tier");
        AssertEqual(2, record.State.TrinketOffer[0].Attack,
            "snapshot preserves trinket attack");
        AssertEqual(3, record.State.TrinketOffer[0].Health,
            "snapshot preserves trinket health");
        AssertEqual(2, record.State.TrinketOffer[0].Tribes.Count,
            "snapshot preserves trinket tribes");
        AssertEqual(2, record.State.ActiveTrinkets.Count,
            "snapshot preserves active trinkets");
        AssertEqual("TB2_001", record.State.ActiveTrinkets[0],
            "snapshot preserves first active trinket");
        AssertEqual("TB2_002", record.State.ActiveTrinkets[1],
            "snapshot preserves second active trinket");

        var canonical = CanonicalJson.Serialize(record.State);
        AssertTrue(canonical.Contains("\"currentCompGuideTier\":\"T1\""),
            "comp guide tier is inside the canonical hash domain");
        AssertTrue(canonical.Contains("\"trinketOffer\":[{\"cardId\":\"TB2_100\""),
            "trinket offer is inside the canonical hash domain");
        AssertTrue(canonical.Contains("\"activeTrinkets\":[\"TB2_001\",\"TB2_002\"]"),
            "active trinkets are inside the canonical hash domain");

        var empty = State(5, 4);
        empty.TrinketOffer = null;
        empty.ActiveTrinkets = null;
        var emptyRecord = DecisionEventRecord.Create(
            "season-mechanic-empty", "game-1", empty, Candidates());
        AssertEqual(0, emptyRecord.State.TrinketOffer.Count,
            "missing trinket offer snapshots as empty");
        AssertEqual(0, emptyRecord.State.ActiveTrinkets.Count,
            "missing active trinkets snapshots as empty");
        var emptyCanonical = CanonicalJson.Serialize(emptyRecord.State);
        AssertTrue(emptyCanonical.Contains("\"trinketOffer\":[]"),
            "missing trinket offer canonicalizes as empty");
        AssertTrue(emptyCanonical.Contains("\"activeTrinkets\":[]"),
            "missing active trinkets canonicalizes as empty");

        state.CurrentCompGuideTier = "T2";
        state.TrinketOffer[0].CardId = "MUTATED";
        state.TrinketOffer.Add(new TrinketOption { CardId = "TB2_102" });
        state.ActiveTrinkets.Add("MUTATED");
        AssertEqual("T1", record.State.CurrentCompGuideTier,
            "snapshot must detach comp guide tier");
        AssertEqual("TB2_100", record.State.TrinketOffer[0].CardId,
            "snapshot must detach trinket cards");
        AssertEqual(2, record.State.TrinketOffer.Count,
            "snapshot must detach trinket offer list");
        AssertEqual(2, record.State.ActiveTrinkets.Count,
            "snapshot must detach active trinket list");
    }

    private static void CapturesSeasonMechanicFingerprintTransitions()
    {
        var baseline = State(5, 4);
        baseline.CurrentCompGuideTier = "T1";
        baseline.TrinketOffer.Add(new TrinketOption { CardId = "TB2_100", EntityId = 301 });
        baseline.ActiveTrinkets.Add("TB2_001");
        var baselineRecord = DecisionEventRecord.Create(
            "season-fingerprint-baseline", "game-1", baseline, Candidates());

        var changedTier = Clone(baseline);
        changedTier.CurrentCompGuideTier = "T2";
        var changedTierRecord = DecisionEventRecord.Create(
            "season-fingerprint-tier", "game-1", changedTier, Candidates());
        AssertTrue(baselineRecord.StateFingerprint != changedTierRecord.StateFingerprint,
            "comp guide tier changes the state fingerprint");

        var changedOffer = Clone(baseline);
        changedOffer.TrinketOffer[0].CardId = "TB2_200";
        var changedOfferRecord = DecisionEventRecord.Create(
            "season-fingerprint-offer", "game-1", changedOffer, Candidates());
        AssertTrue(baselineRecord.StateFingerprint != changedOfferRecord.StateFingerprint,
            "trinket offer changes the state fingerprint");

        var reorderedOffer = Clone(baseline);
        reorderedOffer.TrinketOffer.Add(new TrinketOption { CardId = "TB2_101", EntityId = 302 });
        reorderedOffer.TrinketOffer.Reverse();
        var reorderedOfferRecord = DecisionEventRecord.Create(
            "season-fingerprint-offer-order", "game-1", reorderedOffer, Candidates());
        AssertTrue(baselineRecord.StateFingerprint != reorderedOfferRecord.StateFingerprint,
            "trinket offer order changes the state fingerprint");

        var changedActive = Clone(baseline);
        changedActive.ActiveTrinkets.Add("TB2_002");
        var changedActiveRecord = DecisionEventRecord.Create(
            "season-fingerprint-active", "game-1", changedActive, Candidates());
        AssertTrue(baselineRecord.StateFingerprint != changedActiveRecord.StateFingerprint,
            "active trinkets change the state fingerprint");

        var empty = State(5, 4);
        empty.TrinketOffer = null;
        empty.ActiveTrinkets = null;
        var emptyRecord = DecisionEventRecord.Create(
            "season-fingerprint-empty", "game-1", empty, Candidates());
        var emptyList = State(5, 4);
        var emptyListRecord = DecisionEventRecord.Create(
            "season-fingerprint-empty-list", "game-1", emptyList, Candidates());
        AssertEqual(emptyRecord.StateFingerprint, emptyListRecord.StateFingerprint,
            "null and empty season lists produce the same fingerprint");
    }

    private static void WritesModelIdentityIntoEveryEnvelope()
    {
        var path = NewPath();
        var recorder = new DecisionEventRecorder(path, "linear-task11", "approved-sha256");
        var record = DecisionEventRecord.Create("identity-decision", "game-1", State(5, 4), Candidates());
        recorder.RecordCreated(record);
        recorder.RecordComparison(new ShadowComparison
        {
            DecisionId = "identity-decision",
            ComparisonId = "identity-comparison",
        });
        recorder.AttachRecommendation("identity-decision", new DecisionResult("legacy-v1.1.0"),
            "2026-07-31T00:00:01Z");
        recorder.AttachObservedAction("identity-decision", "Refresh|-1||",
            "2026-07-31T00:00:02Z");
        recorder.AttachCombatOutcome("identity-decision", new CombatOutcome("placement:2"),
            "2026-07-31T00:00:03Z");

        var envelopes = File.ReadAllLines(path).Select(JObject.Parse).ToList();
        AssertEqual(5, envelopes.Count, "all decision event types recorded");
        foreach (var envelope in envelopes)
        {
            AssertEqual("linear-task11", (string)envelope["modelId"], "event model identity");
            AssertEqual("approved-sha256", (string)envelope["modelPayloadSha256"],
                "event model checksum identity");
        }
    }

    private static void NormalizesMissingModelIdentityToUnknown()
    {
        var path = NewPath();
        var recorder = new DecisionEventRecorder(path, null, " ");
        recorder.RecordCreated(DecisionEventRecord.Create(
            "unknown-identity", "game-1", State(5, 4), Candidates()));

        var envelope = JObject.Parse(File.ReadAllLines(path).Single());
        AssertEqual("unknown", (string)envelope["modelId"], "missing model identity");
        AssertEqual("unknown", (string)envelope["modelPayloadSha256"],
            "blank model checksum identity");
    }

    private static void AssertIdentityTransitionChangesFingerprint(
        string canonicalProperty,
        string label,
        Func<GameState, string> stateIdentity,
        Func<GameStateSnapshot, string> snapshotIdentity,
        Action<GameState, string> setIdentity,
        string realIdentity)
    {
        var unknownState = State(5, 4);
        setIdentity(unknownState, null);
        var unknownRecord = DecisionEventRecord.Create(
            "identity-transition-unknown-" + canonicalProperty, "game-1", unknownState, Candidates());

        var realState = Clone(unknownState);
        setIdentity(realState, realIdentity);
        var realRecord = DecisionEventRecord.Create(
            "identity-transition-real-" + canonicalProperty, "game-1", realState, Candidates());

        AssertEqual(null, stateIdentity(unknownState), label + " source starts unknown");
        AssertEqual(realIdentity, stateIdentity(realState), label + " source receives real value");
        AssertEqual(GameStateSnapshot.UnknownIdentity, snapshotIdentity(unknownRecord.State),
            label + " unknown value is captured by the snapshot");
        AssertEqual(realIdentity, snapshotIdentity(realRecord.State),
            label + " real value is captured by the snapshot");

        var unknownCanonical = CanonicalJson.Serialize(unknownRecord.State);
        var realCanonical = CanonicalJson.Serialize(realRecord.State);
        var unknownField = "\"" + canonicalProperty + "\":\"" + GameStateSnapshot.UnknownIdentity + "\"";
        var realField = "\"" + canonicalProperty + "\":\"" + realIdentity + "\"";
        AssertTrue(unknownCanonical.Contains(unknownField),
            label + " unknown value is inside the canonical hash domain");
        AssertTrue(realCanonical.Contains(realField),
            label + " real value is inside the canonical hash domain");

        Func<string, string> fingerprint = canonicalJson =>
        {
            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(canonicalJson)))
                    .Replace("-", "").ToLowerInvariant();
            }
        };
        AssertEqual(fingerprint(unknownCanonical), unknownRecord.StateFingerprint,
            label + " unknown fingerprint hashes the canonical snapshot");
        AssertEqual(fingerprint(realCanonical), realRecord.StateFingerprint,
            label + " real fingerprint hashes the canonical snapshot");
        AssertTrue(unknownRecord.StateFingerprint != realRecord.StateFingerprint,
            label + " unknown-to-real transition changes the state fingerprint");
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

    private static void AttachesGameOutcomeToLastRelevantDecision()
    {
        var path = NewPath();
        var recorder = new DecisionEventRecorder(path);
        var lifecycle = new DecisionLifecycleTracker("game-1", recorder);
        var decision = lifecycle.BeginOrReuse(State(5, 4), Candidates());
        lifecycle.AttachObservedActions(new[] { "BuyMinion|0|CARD_A|shop" },
            "2026-07-31T00:00:02Z");

        lifecycle.AttachCombatOutcome(new CombatOutcome("placement:3"),
            "2026-07-31T00:00:03Z");
        lifecycle.AttachCombatOutcome(new CombatOutcome("placement:3"),
            "2026-07-31T00:00:04Z");

        var lines = File.ReadAllLines(path);
        AssertEqual(1, lines.Count(x => x.Contains("CombatOutcomeObserved")),
            "game outcome attaches once to the last decision");
        AssertTrue(lines.Last().Contains(decision.DecisionId),
            "game outcome keeps the last decision ID");
        AssertTrue(lines.Last().Contains("placement:3"),
            "game outcome records the observed placement");

        lifecycle.Reset();
        lifecycle.AttachCombatOutcome(new CombatOutcome("placement:2"),
            "2026-07-31T00:00:05Z");
        AssertEqual(lines.Length, File.ReadAllLines(path).Length,
            "reset lifecycle does not fabricate an outcome target");
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
        result.GameBuild = state.GameBuild;
        result.SeasonId = state.SeasonId;
        result.CardPoolIdentity = state.CardPoolIdentity;
        result.CurrentCompGuideTier = state.CurrentCompGuideTier;
        result.TrinketOffer = state.TrinketOffer != null
            ? new List<TrinketOption>(state.TrinketOffer.Select(option => new TrinketOption
            {
                CardId = option.CardId,
                TrinketName = option.TrinketName,
                EntityId = option.EntityId,
                IsLesser = option.IsLesser,
                Tier = option.Tier,
                Attack = option.Attack,
                Health = option.Health,
                Tribes = option.Tribes != null
                    ? new List<string>(option.Tribes)
                    : new List<string>()
            }))
            : new List<TrinketOption>();
        result.ActiveTrinkets = state.ActiveTrinkets != null
            ? new List<string>(state.ActiveTrinkets)
            : new List<string>();
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
