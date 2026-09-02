using System;
using System.Collections.Generic;

namespace BobCoach.Engine
{
    public sealed class HeroPlaybookProfile
    {
        public string HeroCardId = "";
        public string HeroNameCn = "";
        public string HeroNameEn = "";
        public List<string> HeroPowerCardIds = new List<string>();
        public string PowerType = "unknown";
        public int PowerCost;
        public int Armor;
        public int UnlockTurn = 1;
        public int UnlockTier = 1;
        public string UsePurpose = "Generic";
        public string SpecialRule = "";
        public bool RequiresTarget;
        public string StrategyCoverage = "facts-only";
        public List<HeroPlaybookPhase> Phases = new List<HeroPlaybookPhase>();
        public List<string> Sources = new List<string>();
    }

    public sealed class HeroPlaybookPhase
    {
        public int MinTurn = 1;
        public int MaxTurn = 99;
        public string Priority = "Normal";
        public int GoldAtLeast;
        public int HealthAtLeast;
        public bool TargetRequired;
        public string Reason = "";
    }
}
