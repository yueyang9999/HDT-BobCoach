using System.Collections.Generic;

namespace BobCoach.Engine
{
    internal sealed class AnomalyFact
    {
        public string RequestedCardId { get; set; }
        public string AnomalyCardId { get; set; }
        public bool IsDuosExclusive { get; set; }
        public string EvolutionCardId { get; set; }
        public string EvolutionCardType { get; set; }
        public bool EvolutionIsGolden { get; set; }
        public string OverrideHeroCardId { get; set; }
        public int[] ScriptData { get; set; }
        public string TextZhCn { get; set; }
        public string TextEnUs { get; set; }
    }

    internal interface IAnomalyFactSource
    {
        bool TryGet(string cardId, out AnomalyFact fact);
    }

    internal sealed class AnomalyDefinition
    {
        public string AnomalyCardId { get; set; }
        public string Lifecycle { get; set; }
        public string Scope { get; set; }
        public IReadOnlyList<AnomalyRegistry.TypedRule> Rules { get; set; }
    }

    internal interface IAnomalyRuleEvaluator
    {
        bool TryEvaluate(AnomalyFact fact, out AnomalyDefinition definition);
    }

    internal interface IAnomalyDefinitionSource
    {
        bool TryGet(string cardId, out AnomalyDefinition definition);
    }
}
