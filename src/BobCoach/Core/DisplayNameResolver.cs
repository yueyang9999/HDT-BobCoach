using System;

namespace BobCoach.Engine
{
    public static class DisplayNameResolver
    {
        public static string Resolve(string cardId, string knownName, Func<string, string> nameLookup)
        {
            if (!string.IsNullOrEmpty(knownName)
                && !string.Equals(knownName, cardId, StringComparison.Ordinal))
                return knownName;

            if (!string.IsNullOrEmpty(cardId) && nameLookup != null)
            {
                string resolvedName = nameLookup(cardId);
                if (!string.IsNullOrEmpty(resolvedName)
                    && !string.Equals(resolvedName, cardId, StringComparison.Ordinal))
                    return resolvedName;
            }

            return "unknown";
        }
    }
}
