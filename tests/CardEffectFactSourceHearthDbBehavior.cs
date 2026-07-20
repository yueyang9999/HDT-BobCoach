using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BobCoach.Engine;

internal static class CardEffectFactSourceHearthDbBehavior
{
    private static int Main(string[] args)
    {
        if (args.Length != 1) return Fail("expected HDT directory");
        string hdtDir = args[0];
        AppDomain.CurrentDomain.AssemblyResolve += (sender, eventArgs) =>
        {
            string path = System.IO.Path.Combine(
                hdtDir, new AssemblyName(eventArgs.Name).Name + ".dll");
            return System.IO.File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        try
        {
            return Run();
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    private static int Run()
    {
        var facts = new HearthDbCardEffectFactSource();
        CardEffectFact fact;
        if (!facts.TryGet("BG35_601", out fact)
            || fact.CardType != CardEffectCardType.Minion
            || fact.ScriptData.Count != 6
            || fact.ScriptData[0] != 3 || fact.ScriptData[1] != 3)
            return Fail("BG35_601 script-data facts were not exact");
        if (!facts.TryGet("BG20_100", out fact)
            || fact.CardType != CardEffectCardType.Minion
            || fact.Attack != 2 || fact.Health != 1 || fact.Tier != 1)
            return Fail("BG20_100 minion facts were not exact");
        if (facts.TryGet("BG20_HERO_100", out fact)
            || facts.TryGet("BOBCOACH_UNKNOWN_CARD", out fact))
            return Fail("wrong or unknown HearthDb type did not fail closed");

        var normalizer = new HearthDbCardEffectCardIdNormalizer();
        var mapping = HearthDb.Cards.NormalToTripleCardIds
            .First(pair =>
            {
                CardEffectFact normal;
                return facts.TryGet(pair.Key, out normal)
                    && HearthDb.Cards.All.ContainsKey(pair.Value)
                    && normal.CardType == CardEffectCardType.Minion
                    && !string.IsNullOrEmpty(pair.Value);
            });
        string normalId;
        if (!normalizer.TryNormalize(mapping.Value, out normalId) || normalId != mapping.Key)
            return Fail("golden card did not normalize through exact HearthDb mapping");
        if (!normalizer.TryNormalize(mapping.Key, out normalId) || normalId != mapping.Key)
            return Fail("normal card identity did not remain exact");
        if (normalizer.TryNormalize("BOBCOACH_UNKNOWN_CARD", out normalId))
            return Fail("unknown card normalized without local identity");

        var derived = new CachedCardEffectSource(
            facts, new CardEffectRuleEvaluator(), normalizer);
        IReadOnlyList<CardEffectDefinition> normalEffects;
        IReadOnlyList<CardEffectDefinition> goldenEffects;
        if (!derived.TryGet(mapping.Key, out normalEffects)
            || !derived.TryGet(mapping.Value, out goldenEffects)
            || Signature(normalEffects) != Signature(goldenEffects))
            return Fail("normal/golden local derived signatures differ");

        Console.WriteLine("PASS exact local HearthDb card-effect facts and golden normalization");
        return 0;
    }

    private static string Signature(IEnumerable<CardEffectDefinition> effects)
    {
        return string.Join(";", effects.Select(effect =>
            effect.Type + "=" + effect.ValueGold + "/" + effect.Per));
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine("FAIL " + message);
        return 1;
    }
}
