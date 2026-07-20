using System;
using System.Collections.Generic;
using BobCoach.Engine;

internal static class HeroPowerFactSourceHearthDbBehavior
{
    private static int _assertions;

    private static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("usage: hearthdb-behavior <hdtDir>");
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
        var source = new HearthDbHeroPowerFactSource();
        HeroPowerFact skin;
        AssertTrue(source.TryGet("BG20_HERO_100_SKIN_A4", out skin), "skin resolves");
        AssertEqual("BG20_HERO_100_SKIN_A4", skin.RequestedCardId, "skin requested identity");
        AssertTrue(!string.IsNullOrEmpty(skin.HeroCardId), "skin base hero identity");
        AssertTrue(!string.IsNullOrEmpty(skin.PowerCardId), "skin power identity");
        AssertTrue(!string.IsNullOrEmpty(skin.TextZhCn), "skin power text exists on call stack");
        AssertEqual(6, skin.ScriptData.Length, "skin script data length");

        HeroPowerFact hero;
        HeroPowerFact power;
        AssertTrue(source.TryGet(skin.HeroCardId, out hero), "base hero resolves");
        AssertTrue(source.TryGet(skin.PowerCardId, out power), "primary power resolves");
        AssertEquivalent(hero, skin, "hero/skin");
        AssertEquivalent(hero, power, "hero/power");

        foreach (string cardId in new[]
        {
            "BG22_HERO_002", "BG22_HERO_003", "BG23_HERO_306",
            "BG31_HERO_006", "TB_BaconShop_HERO_35",
        })
        {
            HeroPowerFact fact;
            AssertTrue(source.TryGet(cardId, out fact), "unlock fact resolves " + cardId);
            AssertTrue(!string.IsNullOrEmpty(fact.TextZhCn), "unlock fact text exists " + cardId);
        }

        HeroPowerFact ignored;
        AssertTrue(!source.TryGet("BOBCOACH_UNKNOWN_CARD", out ignored), "unknown fails");
        AssertTrue(!source.TryGet("BG35_601", out ignored), "normal minion fails");
        AssertTrue(!source.TryGet("", out ignored), "empty fails");
        AssertTrue(!source.TryGet(null, out ignored), "null fails");

        HearthDb.Card realHero;
        HearthDb.Card realPower;
        AssertTrue(HearthDb.Cards.All.TryGetValue(hero.HeroCardId, out realHero), "real hero available");
        AssertTrue(HearthDb.Cards.All.TryGetValue(hero.PowerCardId, out realPower), "real power available");
        var wrongCardIdLookup = new ControlledLookup(realHero, realPower)
        {
            RequestedOverride = realPower,
        };
        var wrongCardIdSource = new HearthDbHeroPowerFactSource(wrongCardIdLookup);
        AssertTrue(!wrongCardIdSource.TryGet(realHero.Id, out ignored), "dictionary key/CardId mismatch fails");

        var wrongDbfLookup = new ControlledLookup(realHero, realPower)
        {
            DbfOverride = realHero,
        };
        var wrongDbfSource = new HearthDbHeroPowerFactSource(wrongDbfLookup);
        AssertTrue(!wrongDbfSource.TryGet(realHero.Id, out ignored), "wrong relation target fails");

        Console.WriteLine("PASS exact local HearthDb hero, power, skin, and failure relations assertions="
            + _assertions);
        return 0;
        }

    private static void AssertEquivalent(HeroPowerFact expected, HeroPowerFact actual, string label)
    {
        AssertEqual(expected.HeroCardId, actual.HeroCardId, label + " hero");
        AssertEqual(expected.PowerCardId, actual.PowerCardId, label + " power");
        AssertEqual(expected.HeroArmor, actual.HeroArmor, label + " armor");
        AssertEqual(expected.PowerCost, actual.PowerCost, label + " cost");
        AssertEqual(expected.HideCost, actual.HideCost, label + " hide cost");
        AssertEqual(expected.BaconHeroPowerActivated, actual.BaconHeroPowerActivated,
            label + " activated");
        AssertEqual(expected.TextZhCn, actual.TextZhCn, label + " transient text");
    }

    private sealed class ControlledLookup : IHeroPowerCardLookup
    {
        private readonly HearthDb.Card _hero;
        private readonly HearthDb.Card _power;

        public ControlledLookup(HearthDb.Card hero, HearthDb.Card power)
        {
            _hero = hero;
            _power = power;
        }

        public HearthDb.Card RequestedOverride { get; set; }
        public HearthDb.Card DbfOverride { get; set; }

        public bool TryGetByCardId(string cardId, out HearthDb.Card card)
        {
            if (RequestedOverride != null)
            {
                card = RequestedOverride;
                return true;
            }
            if (cardId == _hero.Id) { card = _hero; return true; }
            if (cardId == _power.Id) { card = _power; return true; }
            card = null;
            return false;
        }

        public bool TryGetByDbfId(int dbfId, out HearthDb.Card card)
        {
            if (DbfOverride != null)
            {
                card = DbfOverride;
                return true;
            }
            if (dbfId == _hero.DbfId) { card = _hero; return true; }
            if (dbfId == _power.DbfId) { card = _power; return true; }
            card = null;
            return false;
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
