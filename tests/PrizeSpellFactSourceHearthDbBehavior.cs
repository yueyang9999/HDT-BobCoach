using System;
using BobCoach.Engine;

internal static class PrizeSpellFactSourceHearthDbBehavior
{
    public static void Run()
    {
        var source = new HearthDbPrizeSpellFactSource();
        PrizeSpellFact fact;

        True(source.TryGet("BGS_Treasures_004", out fact), "tier1 prize");
        Equal("BGS_Treasures_004", fact.CardId, "tier1 CardId");
        Equal(PrizeSpellCardType.Spell, fact.CardType, "tier1 type");
        Equal(1, fact.PrizeTier, "tier1 prize tier");
        Equal(1, fact.TechLevel, "tier1 tech level");
        Equal(6, fact.ScriptData.Count, "tier1 script count");

        True(source.TryGet("BGS_Treasures_023", out fact), "tier4 refresh prize");
        Equal(PrizeSpellCardType.Spell, fact.CardType, "refresh type");
        Equal(4, fact.PrizeTier, "refresh tier");
        Equal(5, fact.ScriptData[0], "refresh script data 1");

        True(source.TryGet("BG33_300", out fact), "tier4 minion prize");
        Equal(PrizeSpellCardType.Minion, fact.CardType, "minion type");
        Equal(4, fact.PrizeTier, "minion prize tier");
        Equal(4, fact.TechLevel, "minion tech level");

        False(source.TryGet("BGS_Treasures_001", out fact), "untagged treasure");
        False(source.TryGet("BG35_601", out fact), "ordinary minion");
        False(source.TryGet("BOBCOACH_UNKNOWN_CARD", out fact), "unknown id");
        False(source.TryGet("", out fact), "empty id");
    }

    private static void True(bool value, string label)
    {
        if (!value) throw new Exception(label + " expected true");
    }

    private static void False(bool value, string label)
    {
        if (value) throw new Exception(label + " expected false");
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        PrizeSpellFactSourceBehavior.Equal(expected, actual, label);
    }
}
