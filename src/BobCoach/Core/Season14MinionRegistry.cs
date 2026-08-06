using System;

namespace BobCoach.Engine
{
    internal sealed class Season14MinionRegistry : Season14CardRegistry
    {
        internal Season14MinionRegistry()
            : base("minion_registry.json")
        {
        }

        protected override string ResolveConfiguredDataRoot()
        {
            string configuredRoot = Environment.GetEnvironmentVariable(
                BobCoachDataPaths.RootEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(configuredRoot))
                return configuredRoot;
            return new SeasonDataSelector().ResolveConfiguredRoot();
        }

        internal bool TryGetPoolMinion(
            string cardId, out Season14MechanicCardFact fact)
        {
            fact = null;
            if (!TryGet(cardId, out fact))
                return false;
            if (!string.Equals(fact.Type, "MINION", StringComparison.OrdinalIgnoreCase))
                return false;
            if (!fact.IsBattlegroundsPoolMinion)
                return false;
            if (fact.Cost != 0)
                return false;
            if (fact.TechLevel < 1 || fact.TechLevel > 6)
                return false;
            return true;
        }
    }
}
