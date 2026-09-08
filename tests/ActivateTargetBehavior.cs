// ALG-069 behavior test: Activate friendly-race target presence gate (synthetic, minimal).
using System;
using System.Collections.Generic;
using BobCoach.Engine;

internal static class ActivateTargetBehaviorTest
{
    private static int _fail;
    private static void Check(string name, bool expected, MinionData[] board, string text)
    {
        var fact = new ActivateFact { TextZhCn = text };
        bool actual = ActionEnumerator.HasActivateTargetPresent(board, fact);
        string status = actual == expected ? "PASS" : "FAIL";
        if (actual != expected) _fail++;
        Console.WriteLine(status + " " + name + " expected=" + expected + " actual=" + actual);
    }
    public static int Main()
    {
        var dragon = new MinionData { Tribe = "龙" };
        var fish = new MinionData { Tribe = "鱼人" };
        var multi = new MinionData { Tribe = "野猪人,机械" };
        var pirate = new MinionData { Tribe = "海盗" };
        Check("dragon-target-with-dragon", true, new[] { dragon }, "发动：对一个友方龙随从造成3点伤害。");
        Check("dragon-target-no-dragon", false, new[] { fish }, "发动：对一个友方龙随从造成3点伤害。");
        Check("dragon-target-empty-board", false, new MinionData[0], "发动：对一个友方龙随从造成3点伤害。");
        Check("no-friendly-keyword-passes", true, new MinionData[0], "发动：获得2枚铸币。");
        Check("boar-target-with-multi-tribe", true, new[] { multi }, "发动：对一个友方野猪人随从使用一张鲜血宝石。");
        Check("boar-target-no-boar", false, new[] { pirate }, "发动：对一个友方野猪人随从使用一张鲜血宝石。");
        Check("empty-text-passes", true, new MinionData[0], "");
        Console.WriteLine(_fail == 0 ? "ALL_PASS" : "FAILURES=" + _fail);
        return _fail == 0 ? 0 : 1;
    }
}
