using System;
using System.Collections.Generic;
using BobCoach.Engine;

internal static class EffectValueTableBehavior
{
    private static int Main()
    {
        try
        {
            AssertKeywordValues();
            AssertStatTempo();
            AssertGoldenAndDiscount();
            AssertTribeGates();
            AssertCapsAndGev();
            AssertFailuresClose();
            Console.WriteLine("PASS EffectValueTable preserves runtime formulas with injected local facts");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL " + ex.Message);
            return 1;
        }
    }

    private static void AssertKeywordValues()
    {
        var card = Card("KEYWORDS");
        card.DivineShield = true;
        card.Poisonous = true;
        card.Venomous = true;
        card.Taunt = true;
        card.Reborn = true;
        card.Windfury = true;
        card.MegaWindfury = true;
        AssertNear(2.0, Table().ComputeEffectValue(card, State()), "keyword value");
    }

    private static void AssertStatTempo()
    {
        var table = Table();
        var card = Card("STATS");
        card.Attack = 5;
        card.Health = 4;
        AssertNear(3.0, table.ComputeStatTempo(card, 3), "stat tempo");
        card.Attack = 20;
        card.Health = 20;
        AssertNear(6.0, table.ComputeStatTempo(card, 3), "stat cap");
        AssertNear(5.0, table.StatBaseline(0), "tier lower clamp");
        AssertNear(18.0, table.StatBaseline(9), "tier upper clamp");
    }

    private static void AssertGoldenAndDiscount()
    {
        var source = new FakeEffectSource();
        source.Add("GOLDEN", new CardEffectDefinition("generate_gold", 1, "once"));
        source.Add("TURN", new CardEffectDefinition("generate_gold", 1, "turn"));
        var table = Table(source);
        var golden = Card("GOLDEN");
        golden.Golden = true;
        AssertNear(1.75, table.ComputeEffectValue(golden, State()), "golden multiplier");
        var turnState = State();
        turnState.Turn = 8;
        AssertNear(2.533, table.ComputeEffectValue(Card("TURN"), turnState), "turn discount");
        turnState.Health = 10;
        AssertNear(1.7, table.ComputeEffectValue(Card("TURN"), turnState), "low-health discount");
    }

    private static void AssertTribeGates()
    {
        var source = new FakeEffectSource();
        source.Add("BUFF", new CardEffectDefinition("tribe_buff", 1, "once", "鱼人"));
        source.Add("GENERATE", new CardEffectDefinition("generate_card", 2, "once", "鱼人"));
        var table = Table(source);
        var state = State();
        state.BoardMinions.Add(new MinionData { CardId = "B", Tribe = "野兽" });
        AssertNear(0, table.ComputeEffectValue(Card("BUFF"), state, "鱼人"), "tribe buff closed");
        state.BoardMinions.Add(new MinionData { CardId = "M", Tribe = "鱼人" });
        AssertNear(1, table.ComputeEffectValue(Card("BUFF"), state, "鱼人"), "tribe buff open");
        AssertNear(1.2, table.ComputeEffectValue(Card("GENERATE"), state, "野兽"), "off-tribe generation");
        AssertNear(2, table.ComputeEffectValue(Card("GENERATE"), state, ""), "unlocked generation");
    }

    private static void AssertCapsAndGev()
    {
        var source = new FakeEffectSource();
        source.Add("CAP",
            new CardEffectDefinition("generate_card", 4, "once"),
            new CardEffectDefinition("discover", 4, "once"));
        var table = Table(source);
        AssertNear(6, table.ComputeEffectValue(Card("CAP"), State()), "effect cap");
        var zero = Card("NONE");
        zero.Attack = 0;
        zero.Health = 0;
        AssertNear(7.5, table.ComputeGEV(zero, State(), 10, 10), "GEV component caps");
        AssertNear(0, table.ComputeGEV(zero, State(), -1, -1), "GEV negative clamps");
    }

    private static void AssertFailuresClose()
    {
        var failedBaseline = new FakeBaselineProvider(false);
        var failedEffects = new FakeEffectSource { FailAll = true };
        var table = new EffectValueTable(failedEffects, failedBaseline);
        var card = Card("UNKNOWN");
        card.Attack = 10;
        card.Health = 10;
        card.DivineShield = true;
        AssertNear(0, table.StatBaseline(3), "missing baseline");
        AssertNear(0, table.ComputeStatTempo(card, 3), "missing baseline tempo");
        AssertNear(0.5, table.ComputeEffectValue(card, State()), "failed effect source keyword fallback");
    }

    private static EffectValueTable Table(FakeEffectSource source = null)
    {
        return new EffectValueTable(source ?? new FakeEffectSource(), new FakeBaselineProvider(true));
    }

    private static GameState State()
    {
        return new GameState { Turn = 8, Health = 30, TavernTier = 3 };
    }

    private static MinionData Card(string cardId)
    {
        return new MinionData { CardId = cardId, Attack = 0, Health = 0 };
    }

    private static void AssertNear(double expected, double actual, string label)
    {
        if (Math.Abs(expected - actual) > 0.0001)
            throw new InvalidOperationException(label + " expected=" + expected + " actual=" + actual);
    }

    private sealed class FakeEffectSource : ICardEffectSource
    {
        private readonly Dictionary<string, IReadOnlyList<CardEffectDefinition>> _effects
            = new Dictionary<string, IReadOnlyList<CardEffectDefinition>>(StringComparer.Ordinal);
        public bool FailAll;

        public void Add(string cardId, params CardEffectDefinition[] effects)
        {
            _effects[cardId] = Array.AsReadOnly(effects);
        }

        public bool TryGet(string cardId, out IReadOnlyList<CardEffectDefinition> effects)
        {
            effects = Array.AsReadOnly(new CardEffectDefinition[0]);
            return !FailAll && _effects.TryGetValue(cardId, out effects);
        }
    }

    private sealed class FakeBaselineProvider : ICardEffectBaselineProvider
    {
        private readonly bool _success;
        public FakeBaselineProvider(bool success) { _success = success; }

        public bool TryGet(out IReadOnlyList<double> baseline)
        {
            baseline = _success
                ? Array.AsReadOnly(new double[] { 0, 5, 7, 9, 9, 12, 12, 18 })
                : Array.AsReadOnly(new double[0]);
            return _success;
        }
    }
}
