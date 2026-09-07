using System;
using BobCoach.Engine;

internal static class MinionTextUtilBehavior
{
    private static int _assertions;

    private static int Main()
    {
        try
        {
            NullAndEmptyAreFalse();
            ChineseBattlecryDetected();
            EnglishBattlecryDetected();
            OtherKeywordsAreFalse();
            BattlecrySubstringInLongText();
            Console.WriteLine("PASS minion text util assertions=" + _assertions);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL " + ex.Message);
            return 1;
        }
    }

    private static void Assert(bool cond, string what)
    {
        _assertions++;
        if (!cond) throw new Exception("MinionTextUtil: " + what);
    }

    private static void NullAndEmptyAreFalse()
    {
        Assert(!MinionTextUtil.TextHasBattlecry(null), "null");
        Assert(!MinionTextUtil.TextHasBattlecry(""), "empty");
    }

    // I-20260907-19: 战吼文本(中文)命中
    private static void ChineseBattlecryDetected()
    {
        Assert(MinionTextUtil.TextHasBattlecry("战吼：获得+2/+2。"), "zh");
        Assert(MinionTextUtil.TextHasBattlecry("战吼：随机获取一个鱼人"), "zh2");
    }

    private static void EnglishBattlecryDetected()
    {
        Assert(MinionTextUtil.TextHasBattlecry("Battlecry: Gain +2/+2."), "en");
        Assert(MinionTextUtil.TextHasBattlecry("battlecry: Discover a minion"), "en-lower");
    }

    // 沙德沃克误提示根因: 无战吼关键词(嘲讽/圣盾/亡语) → false → 不提示
    private static void OtherKeywordsAreFalse()
    {
        Assert(!MinionTextUtil.TextHasBattlecry("嘲讽"), "taunt");
        Assert(!MinionTextUtil.TextHasBattlecry("圣盾"), "ds");
        Assert(!MinionTextUtil.TextHasBattlecry("亡语：召唤一个随从。"), "deathrattle");
        Assert(!MinionTextUtil.TextHasBattlecry("战歌"), "not battlecry (战歌)");
    }

    private static void BattlecrySubstringInLongText()
    {
        Assert(MinionTextUtil.TextHasBattlecry("亡语：召唤一个具有战吼的随从。"), "substring in long text");
    }
}
