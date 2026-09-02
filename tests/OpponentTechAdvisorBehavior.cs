using System;
using System.Collections.Generic;
using BobCoach.Engine;

internal static class OpponentTechAdvisorBehavior
{
    private static int Main()
    {
        try
        {
            BigBoardsMakeOneForOneRemovalValuable();
            MidStatThreatBoardsStillProtectRemoval();
            FrequencyBoardsMakeOneForOneRemovalWeak();
            UnknownBoardsStayPermissive();
            Console.WriteLine("PASS opponent tech advisor assertions=" + _assertions);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL " + ex.Message);
            return 1;
        }
    }

    private static void BigBoardsMakeOneForOneRemovalValuable()
    {
        var opponents = new List<OpponentData>
        {
            new OpponentData
            {
                Alive = true,
                BoardMinions = new List<MinionData>
                {
                    new MinionData { Attack = 40, Health = 40, Tier = 6 },
                    new MinionData { Attack = 25, Health = 20, Tier = 5 },
                },
            },
        };
        AssertTrue(OpponentTechAdvisor.IsTechCardWorthKeeping(
            "BG23_318", opponents), "big-stat board keeps removal");
        AssertEqual(1.8,
            OpponentTechAdvisor.GetSituationalTechMultiplier("BG23_318", opponents),
            "big-stat buy multiplier");
    }

    private static void MidStatThreatBoardsStillProtectRemoval()
    {
        var opponents = new List<OpponentData>
        {
            new OpponentData
            {
                Alive = true,
                BoardMinions = new List<MinionData>
                {
                    new MinionData { Attack = 9, Health = 9, Tier = 5 },
                    new MinionData { Attack = 9, Health = 9, Tier = 6 },
                    new MinionData { Attack = 9, Health = 6, Tier = 5 },
                    new MinionData { Attack = 6, Health = 5, Tier = 1 },
                    new MinionData { Attack = 7, Health = 6, Tier = 2 },
                    new MinionData { Attack = 5, Health = 9, Tier = 3 },
                    new MinionData { Attack = 0, Health = 0, Tier = 4 },
                },
            },
        };
        AssertTrue(OpponentTechAdvisor.IsTechCardWorthKeeping(
            "BG23_318", opponents),
            "multiple mid-stat threats still protect one-for-one removal");
        AssertEqual(1.8,
            OpponentTechAdvisor.GetSituationalTechMultiplier("BG23_318", opponents),
            "mid-stat board buy multiplier");
    }

    private static void FrequencyBoardsMakeOneForOneRemovalWeak()
    {
        var opponents = new List<OpponentData>
        {
            new OpponentData
            {
                Alive = true,
                BoardMinions = new List<MinionData>
                {
                    new MinionData { Attack = 2, Health = 2, Tier = 1 },
                    new MinionData { Attack = 1, Health = 1, Tier = 1, Reborn = true },
                    new MinionData { Attack = 3, Health = 3, Tier = 2 },
                    new MinionData { Attack = 2, Health = 4, Tier = 2 },
                    new MinionData { Attack = 1, Health = 1, Tier = 1, Reborn = true },
                },
            },
        };
        AssertFalse(OpponentTechAdvisor.IsTechCardWorthKeeping(
            "BG23_318", opponents), "wide frequency board weakens removal");
        AssertEqual(0.55,
            OpponentTechAdvisor.GetSituationalTechMultiplier("BG23_318", opponents),
            "frequency buy multiplier");
    }

    private static void UnknownBoardsStayPermissive()
    {
        AssertFalse(OpponentTechAdvisor.IsTechCardWorthKeeping(
            "BG23_318", new List<OpponentData>()), "no opponent data is not protected");
        AssertEqual(1.0,
            OpponentTechAdvisor.GetSituationalTechMultiplier(
                "BG23_318", new List<OpponentData>()),
            "unknown board stays neutral");
        AssertTrue(OpponentTechAdvisor.IsSituationalTechCard("BG23_318"),
            "normal train crew is situational");
        AssertTrue(OpponentTechAdvisor.IsSituationalTechCard("BG23_318_G"),
            "golden train crew is situational");
    }

    private static void AssertTrue(bool condition, string label)
    {
        _assertions++;
        if (!condition) throw new InvalidOperationException(label);
    }

    private static void AssertFalse(bool condition, string label)
    {
        _assertions++;
        if (condition) throw new InvalidOperationException(label);
    }

    private static void AssertEqual(double expected, double actual, string label)
    {
        _assertions++;
        if (Math.Abs(expected - actual) > 0.0001)
            throw new InvalidOperationException(
                label + " expected=" + expected + " actual=" + actual);
    }

    private static int _assertions;
}

namespace BobCoach.Engine
{
    public class MinionData
    {
        public int Attack;
        public int Health;
        public int Tier;
        public bool Golden;
        public bool DivineShield;
        public bool Poisonous;
        public bool Venomous;
        public bool Reborn;
    }

    public class OpponentData
    {
        public bool Alive = true;
        public List<MinionData> BoardMinions = new List<MinionData>();
    }
}
