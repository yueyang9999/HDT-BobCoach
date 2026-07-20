using System;
using System.Collections.Generic;
using BobCoach.Engine;

internal static class AnomalyFactSourceHearthDbBehavior
{
    private static int _assertions;

    private static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("usage: anomaly-fact-source <hdtDir>");
            return 2;
        }

        string hdtDir = args[0];
        AppDomain.CurrentDomain.AssemblyResolve += (sender, eventArgs) =>
        {
            string name = new System.Reflection.AssemblyName(eventArgs.Name).Name + ".dll";
            string candidate = System.IO.Path.Combine(hdtDir, name);
            return System.IO.File.Exists(candidate)
                ? System.Reflection.Assembly.LoadFrom(candidate)
                : null;
        };

        try
        {
            return Run();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL " + ex.Message);
            return 1;
        }
    }

    private static int Run()
    {
        var source = new HearthDbAnomalyFactSource();

        AnomalyFact solo;
        AssertTrue(source.TryGet("BG27_Anomaly_000", out solo), "solo anomaly resolves");
        AssertEqual("BG27_Anomaly_000", solo.RequestedCardId, "solo requested ID");
        AssertEqual("BG27_Anomaly_000", solo.AnomalyCardId, "solo anomaly ID");
        AssertFalse(solo.IsDuosExclusive, "solo scope");
        AssertEqual("", solo.EvolutionCardId, "solo empty evolution");
        AssertEqual("", solo.EvolutionCardType, "solo empty evolution type");
        AssertEqual("", solo.OverrideHeroCardId, "solo empty hero override");
        AssertEqual(6, solo.ScriptData.Length, "solo script data length");
        AssertTrue(!string.IsNullOrWhiteSpace(solo.TextZhCn), "solo zhCN text on call stack");
        AssertTrue(!string.IsNullOrWhiteSpace(solo.TextEnUs), "solo enUS text on call stack");

        AnomalyFact duo;
        AssertTrue(source.TryGet("BGDUO_Anomaly_005", out duo), "duo anomaly resolves");
        AssertTrue(duo.IsDuosExclusive, "duo scope");

        AnomalyFact secondPower;
        AssertTrue(source.TryGet("BG35_Anomaly_002", out secondPower), "second power relation resolves");
        AssertEqual("BG35_Anomaly_002t", secondPower.EvolutionCardId, "second power relation ID");
        AssertEqual("hero_power", secondPower.EvolutionCardType, "second power relation type");
        AssertFalse(secondPower.EvolutionIsGolden, "second power is not golden");

        AnomalyFact golden;
        AssertTrue(source.TryGet("BG31_Anomaly_120", out golden), "golden relation resolves");
        AssertEqual("BG24_715", golden.EvolutionCardId, "golden normalized relation ID");
        AssertEqual("minion", golden.EvolutionCardType, "golden relation type");
        AssertTrue(golden.EvolutionIsGolden, "golden relation flag");

        AnomalyFact heroOverride;
        AssertTrue(source.TryGet("BG31_Anomaly_106", out heroOverride), "hero override resolves");
        AssertEqual("BG30_HERO_304", heroOverride.OverrideHeroCardId, "hero override ID");

        AnomalyFact ignored;
        AssertFalse(source.TryGet("BG35_Anomaly_002t", out ignored), "hero power is not anomaly");
        AssertFalse(source.TryGet("BG24_715", out ignored), "minion is not anomaly");
        AssertFalse(source.TryGet("BOBCOACH_UNKNOWN_ANOMALY", out ignored), "unknown fails");
        AssertFalse(source.TryGet("", out ignored), "empty fails");
        AssertFalse(source.TryGet(null, out ignored), "null fails");

        HearthDb.Card realAnomaly;
        HearthDb.Card normalMinion;
        AssertTrue(HearthDb.Cards.All.TryGetValue("BG31_Anomaly_120", out realAnomaly),
            "real anomaly available");
        AssertTrue(HearthDb.Cards.All.TryGetValue("BG24_715", out normalMinion),
            "normal minion available");

        var wrongKeyLookup = new ControlledLookup { RequestedOverride = normalMinion };
        AssertFalse(new HearthDbAnomalyFactSource(wrongKeyLookup).TryGet(realAnomaly.Id, out ignored),
            "dictionary key/CardId mismatch fails");

        var missingDbfLookup = new ControlledLookup { FailDbfLookup = true };
        AssertFalse(new HearthDbAnomalyFactSource(missingDbfLookup).TryGet(realAnomaly.Id, out ignored),
            "missing Dbf relation fails");

        var wrongDbfLookup = new ControlledLookup { DbfOverride = realAnomaly };
        AssertFalse(new HearthDbAnomalyFactSource(wrongDbfLookup).TryGet(realAnomaly.Id, out ignored),
            "wrong Dbf relation target fails");

        var wrongGoldenLookup = new ControlledLookup { NormalOverride = "WRONG_NORMAL" };
        AssertFalse(new HearthDbAnomalyFactSource(wrongGoldenLookup).TryGet(realAnomaly.Id, out ignored),
            "golden normalization mismatch fails");

        var textExceptionLookup = new ControlledLookup { ThrowOnText = true };
        AssertFalse(new HearthDbAnomalyFactSource(textExceptionLookup).TryGet(realAnomaly.Id, out ignored),
            "text exception fails closed");

        var tagExceptionLookup = new ControlledLookup { ThrowOnTag = true };
        AssertFalse(new HearthDbAnomalyFactSource(tagExceptionLookup).TryGet(realAnomaly.Id, out ignored),
            "tag exception fails closed");

        Console.WriteLine("PASS exact local HearthDb anomaly facts assertions=" + _assertions);
        return 0;
    }

    private sealed class ControlledLookup : IAnomalyCardLookup
    {
        public HearthDb.Card RequestedOverride { get; set; }
        public HearthDb.Card DbfOverride { get; set; }
        public bool FailDbfLookup { get; set; }
        public string NormalOverride { get; set; }
        public bool ThrowOnText { get; set; }
        public bool ThrowOnTag { get; set; }

        public bool TryGetByCardId(string cardId, out HearthDb.Card card)
        {
            if (RequestedOverride != null)
            {
                card = RequestedOverride;
                return true;
            }
            return HearthDb.Cards.All.TryGetValue(cardId, out card);
        }

        public bool TryGetByDbfId(int dbfId, out HearthDb.Card card)
        {
            if (FailDbfLookup)
            {
                card = null;
                return false;
            }
            if (DbfOverride != null)
            {
                card = DbfOverride;
                return true;
            }
            return HearthDb.Cards.AllByDbfId.TryGetValue(dbfId, out card);
        }

        public bool TryGetNormalCardId(string goldenCardId, out string normalCardId)
        {
            if (NormalOverride != null)
            {
                normalCardId = NormalOverride;
                return true;
            }
            return HearthDb.Cards.TripleToNormalCardIds.TryGetValue(goldenCardId, out normalCardId);
        }

        public int GetTag(HearthDb.Card card, HearthDb.Enums.GameTag tag)
        {
            if (ThrowOnTag) throw new InvalidOperationException("controlled tag failure");
            return card.Entity.GetTag(tag);
        }

        public string GetLocText(HearthDb.Card card, HearthDb.Enums.Locale locale)
        {
            if (ThrowOnText) throw new InvalidOperationException("controlled text failure");
            return card.GetLocText(locale);
        }
    }

    private static void AssertTrue(bool value, string label)
    {
        _assertions++;
        if (!value) throw new InvalidOperationException(label);
    }

    private static void AssertFalse(bool value, string label)
    {
        AssertTrue(!value, label);
    }

    private static void AssertEqual<T>(T expected, T actual, string label)
    {
        _assertions++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(label + ": expected=" + expected + " actual=" + actual);
    }
}
