using System;
using System.Collections.Generic;
using BobCoach.Engine;

internal static class HeroPlaybookRegistryBehavior
{
    private static int _assertions;

    private static int Main()
    {
        try
        {
            RegistryLoadsFromSeason14EvidenceRoot();
            AllProfilesHaveExplicitCoverage();
            CenariusProfileHasEarlyEconomyPhase();
            Official36_2HeroChangesArePresent();
            UnknownHeroFailsClosed();
            Console.WriteLine("PASS hero playbook registry assertions=" + _assertions);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL " + ex.Message);
            return 1;
        }
    }

    private static void RegistryLoadsFromSeason14EvidenceRoot()
    {
        var registry = new HeroPlaybookRegistry();
        registry.LoadFromConfiguredRoot();
        AssertTrue(registry.IsLoaded, "hero playbook registry loaded");
        AssertEqual(116, registry.Count,
            "hero playbook covers all single-player hero registry entries");
    }

    private static void AllProfilesHaveExplicitCoverage()
    {
        var registry = new HeroPlaybookRegistry();
        registry.LoadFromConfiguredRoot();
        foreach (var profile in registry.Profiles)
        {
            AssertTrue(
                profile.StrategyCoverage == "facts-only"
                    || profile.StrategyCoverage == "curated",
                "profile coverage status must be explicit: " + profile.HeroCardId);
            AssertTrue(
                !string.IsNullOrEmpty(profile.HeroCardId),
                "profile hero id must not be empty");
            AssertTrue(
                profile.HeroPowerCardIds != null
                    && profile.HeroPowerCardIds.Count > 0,
                "profile hero power ids missing: " + profile.HeroCardId);
        }
    }

    private static void CenariusProfileHasEarlyEconomyPhase()
    {
        var registry = new HeroPlaybookRegistry();
        registry.LoadFromConfiguredRoot();
        HeroPlaybookProfile profile;
        AssertTrue(
            registry.TryGet("BG32_HERO_001", out profile),
            "Cenarius profile resolved");
        AssertEqual("CENARIUS", profile.SpecialRule,
            "Cenarius special rule preserved");
        AssertEqual("Economy", profile.UsePurpose,
            "Cenarius use purpose is economy");
        AssertEqual("curated", profile.StrategyCoverage,
            "Cenarius has curated strategy coverage");
        bool hasEarly = false;
        foreach (var phase in profile.Phases)
        {
            if (phase.MinTurn <= 1 && phase.MaxTurn >= 5
                && phase.Priority == "Critical"
                && phase.GoldAtLeast == 3)
            {
                hasEarly = true;
                break;
            }
        }
        AssertTrue(hasEarly, "Cenarius early economy phase is present");
    }

    private static void Official36_2HeroChangesArePresent()
    {
        var registry = new HeroPlaybookRegistry();
        registry.LoadFromConfiguredRoot();
        var expected = new[]
        {
            "BG21_HERO_000",
            "BG24_HERO_204",
            "TB_BaconShop_HERO_01",
            "BG20_HERO_102",
            "TB_BaconShop_HERO_11",
            "TB_BaconShop_HERO_75",
            "BG28_HERO_800",
        };
        foreach (string heroCardId in expected)
        {
            HeroPlaybookProfile profile;
            AssertTrue(
                registry.TryGet(heroCardId, out profile),
                "official 36.2 changed hero mapped: " + heroCardId);
            AssertTrue(
                profile.Sources != null && profile.Sources.Count > 0,
                "official 36.2 changed hero has provenance: " + heroCardId);
        }
    }

    private static void UnknownHeroFailsClosed()
    {
        var registry = new HeroPlaybookRegistry();
        registry.LoadFromConfiguredRoot();
        HeroPlaybookProfile profile;
        AssertFalse(
            registry.TryGet("BGDUO_NOT_A_SOLO_HERO", out profile),
            "unknown and duo hero ids fail closed");
    }

    private static void AssertTrue(bool condition, string label)
    {
        _assertions++;
        if (!condition)
            throw new InvalidOperationException(label);
    }

    private static void AssertFalse(bool condition, string label)
    {
        _assertions++;
        if (condition)
            throw new InvalidOperationException(label);
    }

    private static void AssertEqual<T>(
        T expected,
        T actual,
        string label)
    {
        _assertions++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(
                label + " expected=" + expected + " actual=" + actual);
    }
}
