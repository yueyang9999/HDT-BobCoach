using System.Collections.Generic;

namespace BobCoach.Engine
{
    public static class TrinketEffectResolver
    {
        private static readonly TrinketEffectRegistry Registry = new TrinketEffectRegistry();

        public static ActiveTrinketContext Resolve(IEnumerable<string> activeCardIds)
        {
            return Registry.Resolve(activeCardIds);
        }
    }
}
