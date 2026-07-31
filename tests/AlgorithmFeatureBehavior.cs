using System;
using System.Collections.Generic;
using System.Linq;
using BobCoach.Engine;
using BobCoach.Engine.Decision;

internal static class AlgorithmFeatureBehavior
{
    private static int Main()
    {
        try
        {
            VerifiesSchemaAndUnknownBoundary();
            VerifiesUnverifiedFactsFailClosed();
            VerifiesSeasonExtensionRequiresExactProvenance();
            VerifiesDeterministicPreferenceFreeExtraction();
            VerifiesSimulatorBranchesAreDetached();
            Console.WriteLine("PASS 45 named state-action features are deterministic and preference-free");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL " + ex.Message);
            return 1;
        }
    }

    private static void VerifiesSchemaAndUnknownBoundary()
    {
        var expectedNames = new[]
        {
            "turn", "tavern_tier", "health_armor", "gold_before", "hand_slots_free", "board_slots_free",
            "immediate_attack_delta", "immediate_health_delta", "immediate_keyword_delta", "unit_count_delta", "board_power_delta",
            "net_gold_cost", "remaining_gold", "wasted_gold", "refresh_expected_value", "freeze_expected_value",
            "legal_follow_up_count", "playable_follow_up_count", "hand_congestion", "board_congestion",
            "pair_count_delta", "triple_progress_delta", "scaling_engine_delta", "protected_component_delta",
            "expected_damage_exposure", "low_health_tempo_need", "weak_board_level_risk",
            "current_synergy", "target_compatibility", "abandoned_piece_count", "abandoned_piece_value", "transition_cost",
            "unknown_effect_count", "simulator_coverage", "semantic_coverage", "stochastic_range", "state_observation_completeness",
            "low_health_x_immediate_power", "board_full_x_buy", "level_x_damage_exposure", "pair_x_matching_shop",
            "remaining_gold_x_follow_ups", "transition_x_protected_value", "target_tier_delta", "season_extension_value"
        };
        AssertEqual(1, ActionFeatureSchema.Version, "schema version");
        AssertEqual(45, ActionFeatureSchema.Names.Count, "feature count");
        AssertEqual(45, ActionFeatureSchema.Names.Distinct(StringComparer.Ordinal).Count(), "unique names");
        AssertEqual(string.Join("|", expectedNames), string.Join("|", ActionFeatureSchema.Names), "feature order");

        var extractor = new ActionFeatureExtractor();
        var state = State(6);
        var vector = extractor.Extract(state, Buy(state), new Simulator().Simulate(state, Buy(state).Action));
        AssertEqual(vector.Values.Length, vector.Coverage.Length, "coverage per feature");
        AssertTrue(!ActionFeatureSchema.Names.Any(x => x.Contains("preference")), "no player preference");

        var unknownState = State(20);
        unknownState.GameBuild = "unknown";
        unknownState.SeasonId = "unknown";
        var unknown = extractor.Extract(unknownState, Buy(unknownState),
            new Simulator().Simulate(unknownState, Buy(unknownState).Action));
        AssertEqual(0d, unknown["refresh_expected_value"], "unknown refresh value");
        AssertEqual(0d, unknown.Coverage[ActionFeatureSchema.IndexOf("refresh_expected_value")],
            "unknown refresh coverage");
        AssertEqual(0d, unknown["season_extension_value"], "unknown season extension");
        AssertEqual(0d, unknown.Coverage[ActionFeatureSchema.IndexOf("season_extension_value")],
            "unknown season coverage");

        var unknownEntityState = State(20);
        unknownEntityState.BoardMinions.Add(new MinionData { CardId = "unknown", Attack = 1, Health = 1 });
        var unknownEntity = extractor.Extract(unknownEntityState, Buy(unknownEntityState),
            new Simulator().Simulate(unknownEntityState, Buy(unknownEntityState).Action));
        AssertEqual(0d, unknownEntity.Coverage[ActionFeatureSchema.IndexOf("pair_count_delta")],
            "unknown entity pair coverage");
    }

    private static void VerifiesUnverifiedFactsFailClosed()
    {
        var state = State(6);
        state.BoardMinions.Add(new MinionData { CardId = "CARD_A", Attack = 2, Health = 2, Tier = 2 });
        state.BoardMinions.Add(new MinionData { CardId = "CARD_A", Attack = 2, Health = 2, Tier = 2 });
        var action = Buy(state);
        var next = new Simulator().Simulate(state, action.Action);
        next.BoardMinions.Add(new MinionData
        {
            CardId = "CARD_A", Attack = 4, Health = 5, Tier = 2, Taunt = true,
        });
        var extractor = new ActionFeatureExtractor();
        var normalLooking = extractor.Extract(state, action, next);

        foreach (var feature in UnverifiedSemanticFeatures())
            AssertUnavailable(normalLooking, feature, "unverified " + feature);

        var unknownVersionState = State(6);
        unknownVersionState.GameBuild = "unknown";
        unknownVersionState.SeasonId = "unknown";
        unknownVersionState.CardPoolIdentity = "unknown";
        var unknownVersion = extractor.Extract(unknownVersionState, Buy(unknownVersionState),
            new Simulator().Simulate(unknownVersionState, Buy(unknownVersionState).Action));
        foreach (var feature in UnverifiedSemanticFeatures())
            AssertUnavailable(unknownVersion, feature, "unknown version " + feature);

        var unknownEntityState = State(6);
        unknownEntityState.ShopMinions[0].CardId = "unknown";
        unknownEntityState.ShopMinions[0].Taunt = true;
        var unknownEntity = extractor.Extract(unknownEntityState, Buy(unknownEntityState),
            new Simulator().Simulate(unknownEntityState, Buy(unknownEntityState).Action));
        AssertUnavailable(unknownEntity, "immediate_keyword_delta", "unknown entity keyword delta");
        AssertUnavailable(unknownEntity, "pair_x_matching_shop", "unknown entity pair matching");
        AssertUnavailable(unknownEntity, "target_tier_delta", "unknown entity target tier");
    }

    private static IEnumerable<string> UnverifiedSemanticFeatures()
    {
        return new[]
        {
            "immediate_keyword_delta", "wasted_gold", "legal_follow_up_count", "playable_follow_up_count",
            "unknown_effect_count", "simulator_coverage", "stochastic_range", "low_health_x_immediate_power",
            "pair_x_matching_shop", "remaining_gold_x_follow_ups", "target_tier_delta",
        };
    }

    private static void VerifiesSeasonExtensionRequiresExactProvenance()
    {
        var extension = new TestSeasonFeatureExtension("build-verified", "season-verified", "pool-verified", 7d);
        var extractor = new ActionFeatureExtractor(extension);

        AssertSeasonExtensionAvailable(extractor, State(20), "exact provenance");

        var buildMismatch = State(20);
        buildMismatch.GameBuild = "build-stale";
        AssertUnavailable(extractor.Extract(buildMismatch, Buy(buildMismatch),
            new Simulator().Simulate(buildMismatch, Buy(buildMismatch).Action)),
            "season_extension_value", "season extension build mismatch");

        var seasonMismatch = State(20);
        seasonMismatch.SeasonId = "season-stale";
        AssertUnavailable(extractor.Extract(seasonMismatch, Buy(seasonMismatch),
            new Simulator().Simulate(seasonMismatch, Buy(seasonMismatch).Action)),
            "season_extension_value", "season extension season mismatch");

        var cardPoolMismatch = State(20);
        cardPoolMismatch.CardPoolIdentity = "pool-stale";
        AssertUnavailable(extractor.Extract(cardPoolMismatch, Buy(cardPoolMismatch),
            new Simulator().Simulate(cardPoolMismatch, Buy(cardPoolMismatch).Action)),
            "season_extension_value", "season extension card pool mismatch");

        var unknown = State(20);
        unknown.GameBuild = "unknown";
        AssertUnavailable(extractor.Extract(unknown, Buy(unknown),
            new Simulator().Simulate(unknown, Buy(unknown).Action)),
            "season_extension_value", "season extension unknown build provenance");

        unknown = State(20);
        unknown.SeasonId = "unknown";
        AssertUnavailable(extractor.Extract(unknown, Buy(unknown),
            new Simulator().Simulate(unknown, Buy(unknown).Action)),
            "season_extension_value", "season extension unknown season provenance");

        unknown = State(20);
        unknown.CardPoolIdentity = "unknown";
        AssertUnavailable(extractor.Extract(unknown, Buy(unknown),
            new Simulator().Simulate(unknown, Buy(unknown).Action)),
            "season_extension_value", "season extension unknown card pool provenance");

        AssertSeasonExtensionUnavailable(
            new TestSeasonFeatureExtension("unknown", "season-verified", "pool-verified", 7d),
            "season extension unknown extension build provenance");
        AssertSeasonExtensionUnavailable(
            new TestSeasonFeatureExtension("build-verified", "unknown", "pool-verified", 7d),
            "season extension unknown extension season provenance");
        AssertSeasonExtensionUnavailable(
            new TestSeasonFeatureExtension("build-verified", "season-verified", "unknown", 7d),
            "season extension unknown extension card pool provenance");
    }

    private static void VerifiesDeterministicPreferenceFreeExtraction()
    {
        var state = State(12);
        var action = Buy(state);
        var next = new Simulator().Simulate(state, action.Action);
        var extractor = new ActionFeatureExtractor();
        var first = extractor.Extract(state, action, next);
        var second = extractor.Extract(state, action, next);

        AssertEqual(45, first.Values.Length, "value count");
        for (var i = 0; i < first.Values.Length; i++)
        {
            AssertTrue(!Double.IsNaN(first.Values[i]) && !Double.IsInfinity(first.Values[i]),
                "finite value " + i);
            AssertTrue(first.Coverage[i] == 0d || first.Coverage[i] == 1d, "binary coverage " + i);
            AssertEqual(first.Values[i], second.Values[i], "deterministic value " + i);
            AssertEqual(first.Coverage[i], second.Coverage[i], "deterministic coverage " + i);
        }
    }

    private static void VerifiesSimulatorBranchesAreDetached()
    {
        foreach (var action in SupportedActions())
        {
            var source = State(20);
            source.BoardMinions.Add(new MinionData { CardId = "CARD_A", Attack = 3, Health = 4, Tier = 1 });
            source.HandMinions.Add(new MinionData { CardId = "HAND_A", Attack = 1, Health = 1, Tier = 1 });
            source.ShopMinions.Add(new MinionData { CardId = "SHOP_A", Attack = 2, Health = 2, Tier = 1 });
            source.HeroPowers.Add(new HeroPowerState
            {
                CardId = "POWER_A", IsActive = true, IsUnlocked = true, Cost = 0,
            });
            var fingerprint = SourceFingerprint(source);

            var simulated = new Simulator().Simulate(source, action);
            if (simulated.BoardMinions.Count > 0) simulated.BoardMinions[0].Attack = 99;
            if (simulated.HandMinions.Count > 0) simulated.HandMinions[0].Health = 99;
            if (simulated.ShopMinions.Count > 0) simulated.ShopMinions[0].Tier = 99;
            if (simulated.HeroPowers.Count > 0) simulated.HeroPowers[0].Exhausted = true;
            simulated.PendingPurchaseRewardExpectations.Clear();

            AssertEqual(fingerprint, SourceFingerprint(source), "detached " + action.Type);
            AssertEqual(3, source.BoardMinions[0].Attack, "source minion attack " + action.Type);
        }
    }

    private static IEnumerable<GameAction> SupportedActions()
    {
        return new[]
        {
            new GameAction { Type = ActionType.BuyMinion, TargetIndex = 0 },
            new GameAction { Type = ActionType.BuySpell, TargetIndex = 0 },
            new GameAction { Type = ActionType.Refresh },
            new GameAction { Type = ActionType.Upgrade },
            new GameAction { Type = ActionType.SellMinion, TargetIndex = 0 },
            new GameAction { Type = ActionType.FreezeShop },
            new GameAction { Type = ActionType.UseHeroPower, CardId = "POWER_A" },
            new GameAction { Type = ActionType.PickDiscover, TargetIndex = 0 },
            new GameAction { Type = ActionType.PickTrinket, TargetIndex = 0 },
            new GameAction { Type = ActionType.SendGoldToTeammate, CardId = "POWER_A" },
        };
    }

    private static ActionDescriptor Buy(GameState state)
    {
        return ActionDescriptor.From(state, new GameAction
        {
            Type = ActionType.BuyMinion, TargetIndex = 0, CardId = state.ShopMinions[0].CardId,
            PurchaseSource = "tavern_shop",
        });
    }

    private static GameState State(int health)
    {
        var state = new GameState
        {
            GameActive = true, GameBuild = "build-verified", SeasonId = "season-verified",
            CardPoolIdentity = "pool-verified", Turn = 5, TavernTier = 2, Gold = 6,
            TavernUpgradeCost = 5, Health = health, MaxHealth = 30, Armor = 0, Phase = "shop",
        };
        state.ShopMinions.Add(new MinionData
        {
            CardId = "CARD_A", EntityId = 101, Attack = 4, Health = 5, Tier = 2, Cost = 3,
        });
        return state;
    }

    private static string SourceFingerprint(GameState state)
    {
        return string.Join("|", new[]
        {
            state.Gold.ToString(),
            string.Join(",", state.BoardMinions.Select(x => x.CardId + ":" + x.Attack + ":" + x.Health)),
            string.Join(",", state.HandMinions.Select(x => x.CardId + ":" + x.Attack + ":" + x.Health)),
            string.Join(",", state.ShopMinions.Select(x => x.CardId + ":" + x.Attack + ":" + x.Health + ":" + x.Tier)),
            string.Join(",", state.HeroPowers.Select(x => x.CardId + ":" + x.Exhausted)),
            state.PendingPurchaseRewardExpectations.Count.ToString(),
        });
    }

    private static void AssertTrue(bool value, string label)
    {
        if (!value) throw new InvalidOperationException(label);
    }

    private static void AssertUnavailable(ActionFeatureVector vector, string feature, string label)
    {
        AssertEqual(0d, vector[feature], label + " value");
        AssertEqual(0d, vector.Coverage[ActionFeatureSchema.IndexOf(feature)], label + " coverage");
    }

    private static void AssertSeasonExtensionAvailable(ActionFeatureExtractor extractor, GameState state, string label)
    {
        var vector = extractor.Extract(state, Buy(state), new Simulator().Simulate(state, Buy(state).Action));
        AssertEqual(7d, vector["season_extension_value"], label + " value");
        AssertEqual(1d, vector.Coverage[ActionFeatureSchema.IndexOf("season_extension_value")], label + " coverage");
    }

    private static void AssertSeasonExtensionUnavailable(ISeasonFeatureExtension extension, string label)
    {
        var state = State(20);
        AssertUnavailable(new ActionFeatureExtractor(extension).Extract(
            state, Buy(state), new Simulator().Simulate(state, Buy(state).Action)),
            "season_extension_value", label);
    }

    private sealed class TestSeasonFeatureExtension : ISeasonFeatureExtension
    {
        private readonly double _value;

        public TestSeasonFeatureExtension(string gameBuild, string seasonId, string cardPoolIdentity, double value)
        {
            GameBuild = gameBuild;
            SeasonId = seasonId;
            CardPoolIdentity = cardPoolIdentity;
            _value = value;
        }

        public string GameBuild { get; private set; }
        public string SeasonId { get; private set; }
        public string CardPoolIdentity { get; private set; }

        public bool TryEvaluate(GameState state, ActionDescriptor action, GameState nextState, out double value)
        {
            value = _value;
            return true;
        }
    }

    private static void AssertEqual<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(label + " expected=" + expected + " actual=" + actual);
    }
}
