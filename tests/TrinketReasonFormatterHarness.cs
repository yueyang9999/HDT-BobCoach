using System;
using System.Collections.Generic;
using BobCoach.Engine;

internal static class TrinketReasonFormatterHarness
{
    private static int Main()
    {
        string rated = TrinketReasonFormatter.Format(false, new List<string>
        {
            "scaling", "economy", "dominant_tribe",
        });
        string unknown = TrinketReasonFormatter.Format(true, new List<string>
        {
            "avenge", "golden",
        });
        if (rated != "规则匹配：成长、经济、主流派" || unknown != "未知")
            return Fail("rule reasons or unrated label were not deterministic");
        if (rated.Contains("推荐") || rated.Contains("分"))
            return Fail("reason exposed an unsupported absolute-strength claim or raw score");

        Console.WriteLine("PASS trinket reasons expose rule evidence without absolute strength labels");
        return 0;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine("FAIL " + message);
        return 1;
    }
}
