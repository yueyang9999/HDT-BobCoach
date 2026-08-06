using System;

namespace BobCoach.Engine
{
    internal sealed class Season14TavernSpellRegistry : Season14CardRegistry
    {
        internal Season14TavernSpellRegistry()
            : base("tavern_spell_registry.json")
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

        internal bool TryGetPoolSpell(
            string cardId, out Season14MechanicCardFact fact)
        {
            fact = null;
            if (!TryGet(cardId, out fact))
                return false;
            if (!string.Equals(
                fact.Type, "BATTLEGROUND_SPELL", StringComparison.OrdinalIgnoreCase))
                return false;
            if (!fact.IsBattlegroundsPoolSpell)
                return false;
            if (fact.Cost < 0)
                return false;
            return true;
        }
    }
}
