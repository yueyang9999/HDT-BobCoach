using System;
using System.Collections.Generic;
using BobCoach.Engine;

internal static class HeroStrategyFacadeBehavior
{
    private static int _assertions;

    private static int Main()
    {
        try
        {
            var source = new RecordingSource();
            var engine = new HeroPowerEngine(source);

            HeroStrategy hero = engine.GetStrategy("HERO");
            AssertEqual("HERO", hero.HeroCardId, "hero query identity");
            AssertSequence(new[] { "HERO" }, source.TakeCalls(), "hero query order");
            hero.SpecialRule = "MUTATED";
            hero.TribeAffinity["DRAGON"] = 99f;
            HeroStrategy heroAgain = engine.GetStrategy("HERO");
            AssertEqual("VALID", heroAgain.SpecialRule, "hero strategy defensive scalar");
            AssertEqual(0.20f, heroAgain.TribeAffinity["DRAGON"], "hero strategy defensive map");
            AssertSequence(new[] { "HERO" }, source.TakeCalls(), "defensive query order");

            HeroStrategy power = engine.GetStrategyForPower("HERO", "POWER");
            AssertEqual("POWER", power.HeroCardId, "power query wins");
            AssertSequence(new[] { "POWER" }, source.TakeCalls(), "power query order");

            HeroStrategy fallback = engine.GetStrategyForPower("HERO", "MISSING_POWER");
            AssertEqual("HERO", fallback.HeroCardId, "missing power falls back to hero");
            AssertSequence(new[] { "MISSING_POWER", "HERO" }, source.TakeCalls(),
                "missing power fallback order");

            HeroStrategy unknown = engine.GetStrategy("UNKNOWN");
            AssertDefault(unknown, "unknown default");
            AssertSequence(new[] { "UNKNOWN" }, source.TakeCalls(), "unknown query order");
            AssertDefault(engine.GetStrategy(""), "empty default");
            AssertSequence(new string[0], source.TakeCalls(), "empty does not query source");
            AssertDefault(engine.GetStrategy(null), "null default");
            AssertSequence(new string[0], source.TakeCalls(), "null does not query source");

            unknown.SpecialRule = "MUTATED";
            unknown.TribeAffinity["DRAGON"] = 1f;
            AssertDefault(engine.GetStrategy("UNKNOWN"), "default is defensive");

            AssertEqual("拿资源 · 补强场面",
                engine.GetUseSuggestion("HERO", 2, 5, 3, false),
                "structured use suggestion");
            AssertEqual("", engine.GetUseSuggestion("PASSIVE", 10, 5, 7, false),
                "passive has no suggestion");
            AssertEqual(1.10f, engine.GetLevelAggression("HERO"), "level aggression facade");
            AssertEqual(0.20f, engine.GetTribeAffinity("HERO", "DRAGON"),
                "tribe affinity facade");
            AssertEqual(0f, engine.GetTribeAffinity("HERO", ""),
                "empty tribe fails closed");
            AssertTrue(engine.HasSpecialRule("HERO", "VALID"), "special rule facade");

            Console.WriteLine("PASS hero strategy facade query order, defaults, and consumers assertions="
                + _assertions);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL " + ex.Message);
            return 1;
        }
    }

    private static void AssertDefault(HeroStrategy value, string label)
    {
        AssertEqual("", value.HeroCardId, label + " identity");
        AssertEqual("", value.HeroName, label + " name");
        AssertEqual("", value.PowerHint, label + " text");
        AssertEqual(HeroPowerType.Passive, value.PowerType, label + " type");
        AssertEqual(HeroArchetype.General, value.Archetype, label + " archetype");
        AssertEqual(0, value.PowerCost, label + " cost");
        AssertEqual(0, value.TribeAffinity.Count, label + " affinity");
        AssertEqual(0, value.SynergyTags.Count, label + " tags");
        AssertEqual("", value.SpecialRule, label + " special rule");
    }

    private sealed class RecordingSource : IHeroStrategySource
    {
        private readonly List<string> _calls = new List<string>();

        public bool TryGet(string cardId, out HeroStrategy strategy)
        {
            _calls.Add(cardId);
            strategy = null;
            if (cardId == "MISSING_POWER" || cardId == "UNKNOWN") return false;
            if (cardId != "HERO" && cardId != "POWER" && cardId != "PASSIVE") return false;
            bool passive = cardId == "PASSIVE";
            strategy = new HeroStrategy
            {
                HeroCardId = cardId,
                HeroName = "",
                PowerType = passive ? HeroPowerType.Passive : HeroPowerType.Active,
                Archetype = HeroArchetype.General,
                PowerCost = passive ? 0 : 1,
                PowerHint = "",
                LevelAggression = passive ? 1f : 1.10f,
                UsePurpose = passive ? HeroUsePurpose.None : HeroUsePurpose.Resource,
                SpecialRule = passive ? "" : "VALID",
                TribeAffinity = new Dictionary<string, float>(StringComparer.Ordinal)
                {
                    { "DRAGON", passive ? 0f : 0.20f },
                },
                SynergyTags = new HashSet<string>(StringComparer.Ordinal),
            };
            return true;
        }

        public string[] TakeCalls()
        {
            string[] result = _calls.ToArray();
            _calls.Clear();
            return result;
        }
    }

    private static void AssertSequence(string[] expected, string[] actual, string label)
    {
        _assertions++;
        if (expected.Length != actual.Length)
            throw new InvalidOperationException(label + " length");
        for (int index = 0; index < expected.Length; index++)
        {
            if (expected[index] != actual[index])
                throw new InvalidOperationException(label + " at " + index);
        }
    }

    private static void AssertTrue(bool value, string label)
    {
        _assertions++;
        if (!value) throw new InvalidOperationException(label);
    }

    private static void AssertEqual<T>(T expected, T actual, string label)
    {
        _assertions++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(label + ": expected=" + expected + " actual=" + actual);
    }
}
