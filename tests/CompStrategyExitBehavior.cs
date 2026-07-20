using System;
using System.Collections.Generic;
using BobCoach.Engine;

namespace BobCoach.Engine
{
    public sealed class GameState
    {
        public int Turn;
        public List<MinionData> BoardMinions = new List<MinionData>();
        public List<MinionData> HandMinions = new List<MinionData>();
        public List<string> AvailableTribes = new List<string>();
    }

    public sealed class MinionData
    {
        public string CardId = "";
        public string Tribe = "";

        public static string[] GetTribesArray(string tribe)
        {
            return string.IsNullOrEmpty(tribe)
                ? new string[0]
                : tribe.Split(new[] { ',', '|' }, StringSplitOptions.RemoveEmptyEntries);
        }
    }

    public sealed class HeroStrategy
    {
        public HashSet<string> SynergyTags = new HashSet<string>(StringComparer.Ordinal);
    }

    public sealed class HeroPowerEngine
    {
        public HeroStrategy GetStrategy(string heroCardId)
        {
            return new HeroStrategy();
        }

        public float GetTribeAffinity(string heroCardId, string tribe)
        {
            return heroCardId == "HERO" && tribe == "BEAST" ? 0.25f : 0f;
        }
    }
}

internal static class CompStrategyExitBehavior
{
    private sealed class FakeSemanticSource : ICardSemanticSource
    {
        private readonly Dictionary<string, CardSemanticsData> _rows
            = new Dictionary<string, CardSemanticsData>(StringComparer.Ordinal);

        public void Add(string cardId, CardSemanticsData semantics)
        {
            _rows[cardId] = semantics;
        }

        public bool TryGet(string cardId, out CardSemanticsData semantics)
        {
            return _rows.TryGetValue(cardId, out semantics);
        }
    }

    private static int Main()
    {
        var semanticSource = new FakeSemanticSource();
        semanticSource.Add("PROVIDER", new CardSemanticsData(
            new string[0], new CardSemanticCombo[0], new[] { "MATCH" }));
        semanticSource.Add("SHOP", new CardSemanticsData(
            new string[0],
            new[]
            {
                new CardSemanticCombo("MATCH", 1.0),
                new CardSemanticCombo("MISSING", 1.0),
            },
            new string[0]));

        var engine = new SynergyEngine();
        engine.SetCardSemanticSource(semanticSource);
        engine.SetHeroPowerEngine(new HeroPowerEngine());

        AssertScore(engine.ScoreCard("SHOP", "BEAST", State(3), "HERO"),
            1.0f, 0.5f, 0.25f,
            1.0f * 0.40f + 0.5f * 0.20f + 0.25f * 0.20f,
            "early");
        AssertScore(engine.ScoreCard("SHOP", "BEAST", State(7), "HERO"),
            1.0f, 0.5f, 0.25f,
            1.0f * 0.25f + 0.5f * 0.30f + 0.25f * 0.15f,
            "middle");
        AssertScore(engine.ScoreCard("SHOP", "BEAST", State(10), "HERO"),
            1.0f, 0.5f, 0.25f,
            1.0f * 0.20f + 0.5f * 0.25f + 0.25f * 0.15f,
            "late");

        var unknown = engine.ScoreCard("UNKNOWN", "", State(3), "");
        AssertNear(unknown.TribeScore, 0.5f, "unknown tribe");
        AssertNear(unknown.MechanicScore, 0f, "unknown mechanic");
        AssertNear(unknown.HeroScore, 0f, "unknown hero");
        if (typeof(CardSynergyScore).GetField("CompScore") != null)
            return Fail("CardSynergyScore still exposes retired CompScore");
        if (unknown.Reason == null)
            return Fail("reason must never be null");

        Console.WriteLine("PASS local synergy scoring is independent of comp strategies");
        return 0;
    }

    private static GameState State(int turn)
    {
        var state = new GameState { Turn = turn };
        state.BoardMinions.Add(new MinionData { CardId = "PROVIDER", Tribe = "BEAST" });
        return state;
    }

    private static void AssertScore(CardSynergyScore score, float tribe, float mechanic,
        float hero, float total, string label)
    {
        AssertNear(score.TribeScore, tribe, label + " tribe");
        AssertNear(score.MechanicScore, mechanic, label + " mechanic");
        AssertNear(score.HeroScore, hero, label + " hero");
        AssertNear(score.TotalScore, total, label + " total");
        if (!score.Reason.Contains("同族") || !score.Reason.Contains("机制合")
            || !score.Reason.Contains("英雄偏好"))
            throw new InvalidOperationException(label + " reason missing generic evidence: " + score.Reason);
    }

    private static void AssertNear(float actual, float expected, string label)
    {
        if (Math.Abs(actual - expected) > 0.000001f)
            throw new InvalidOperationException(label + " expected=" + expected + " actual=" + actual);
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine("FAIL " + message);
        return 1;
    }
}
