using System;
using System.Collections.Generic;

namespace BobCoach.Engine
{
    public enum HeroPowerType { Passive, Active, Conditional, Aura }
    public enum HeroArchetype { General, Econ, Tempo, Scaling, Greed, Survival }
    public enum HeroUsePurpose { None, Economy, Buff, Resource, Combat, Generic }

    public class HeroStrategy
    {
        public string HeroCardId = "";
        public string HeroName = "";
        public HeroPowerType PowerType;
        public HeroArchetype Archetype;
        public int PowerCost;
        public int UnlockTurn = 1;
        public int UnlockTier = 1;
        public bool HasDiscover;
        public string PowerHint = "";
        public float LevelAggression = 1.0f;
        public float UpgradeValueBias;
        public float RefreshValueBias;
        public float BuyValueBias;
        public float PowerValueBias;
        public Dictionary<string, float> TribeAffinity
            = new Dictionary<string, float>(StringComparer.Ordinal);
        public HashSet<string> SynergyTags = new HashSet<string>(StringComparer.Ordinal);
        public HeroUsePurpose UsePurpose;
        public string SpecialRule = "";
    }

    internal sealed class HeroPowerFact
    {
        public string RequestedCardId { get; set; }
        public string HeroCardId { get; set; }
        public string PowerCardId { get; set; }
        public int HeroArmor { get; set; }
        public int PowerCost { get; set; }
        public bool HideCost { get; set; }
        public bool BaconHeroPowerActivated { get; set; }
        public string TextZhCn { get; set; }
        public int[] ScriptData { get; set; }
    }

    internal interface IHeroPowerFactSource
    {
        bool TryGet(string cardId, out HeroPowerFact fact);
    }

    internal interface IHeroStrategyRuleEvaluator
    {
        bool TryEvaluate(HeroPowerFact fact, out HeroStrategy strategy);
    }
}
