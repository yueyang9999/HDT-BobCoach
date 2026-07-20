using System;
using System.Collections.Generic;

namespace BobCoach.Engine
{
    /// <summary>按实际观察CardId查询本机派生的畸变定义。</summary>
    public class AnomalyRegistry
    {
        public class Anomaly
        {
            public string AnomalyId;
            public string Lifecycle;        // primary/primary_variant/timewarp_effect
            public string Scope;            // solo/duo
            public string Availability;     // active/inactive/not_in_build
        }

        public sealed class TypedRule
        {
            public string Type;
            public int? IntValue;
            public bool? BoolValue;
            public string StringValue;
            public int Turn;
            public int EveryTurns;
            public int InitialTier;
            public int ImprovesEveryTurns;
            public int Tier;
            public string CardId;
            public string HeroCardId;
            public string CardType;
            public int Count;
            public int? ExplicitCount;
            public int CountEach;
            public int GoldPerUse;
            public int MaxPerTurn;
            public bool? Golden;
            public bool GoldenInvalid;
            public int? UnlockTurn;
            public string CopiesPurchasedTrinket;
            public bool? GrantsTripleReward;
            public string Period;
            public string GrantAt;
            public bool? CarryToGreater;
            public List<int> Tiers = new List<int>();
            public List<int> Turns = new List<int>();
        }

        private readonly IAnomalyDefinitionSource _source;

        internal AnomalyRegistry(IAnomalyDefinitionSource source)
        {
            _source = source ?? throw new ArgumentNullException("source");
        }

        public Anomaly GetById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            AnomalyDefinition definition;
            if (!_source.TryGet(id, out definition) || definition == null) return null;
            return new Anomaly
            {
                AnomalyId = definition.AnomalyCardId,
                Lifecycle = definition.Lifecycle,
                Scope = definition.Scope,
                Availability = "active",
            };
        }

        public IList<TypedRule> GetTypedRules(string id)
        {
            if (string.IsNullOrEmpty(id)) return new List<TypedRule>();
            AnomalyDefinition definition;
            if (!_source.TryGet(id, out definition)
                || definition == null
                || definition.Rules == null)
                return new List<TypedRule>();
            var rules = new List<TypedRule>(definition.Rules.Count);
            foreach (var rule in definition.Rules)
                rules.Add(CopyRule(rule));
            return rules;
        }

        private static TypedRule CopyRule(TypedRule source)
        {
            return new TypedRule
            {
                Type = source.Type,
                IntValue = source.IntValue,
                BoolValue = source.BoolValue,
                StringValue = source.StringValue,
                Turn = source.Turn,
                EveryTurns = source.EveryTurns,
                InitialTier = source.InitialTier,
                ImprovesEveryTurns = source.ImprovesEveryTurns,
                Tier = source.Tier,
                CardId = source.CardId,
                HeroCardId = source.HeroCardId,
                CardType = source.CardType,
                Count = source.Count,
                ExplicitCount = source.ExplicitCount,
                CountEach = source.CountEach,
                GoldPerUse = source.GoldPerUse,
                MaxPerTurn = source.MaxPerTurn,
                Golden = source.Golden,
                GoldenInvalid = source.GoldenInvalid,
                UnlockTurn = source.UnlockTurn,
                CopiesPurchasedTrinket = source.CopiesPurchasedTrinket,
                GrantsTripleReward = source.GrantsTripleReward,
                Period = source.Period,
                GrantAt = source.GrantAt,
                CarryToGreater = source.CarryToGreater,
                Tiers = new List<int>(source.Tiers ?? new List<int>()),
                Turns = new List<int>(source.Turns ?? new List<int>()),
            };
        }
    }
}
