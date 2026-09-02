using System;

namespace BobCoach.Engine
{
    public static class DisplayNameResolver
    {
        public static string Resolve(string cardId, string knownName, Func<string, string> nameLookup)
        {
            if (IsUsableKnownName(knownName, cardId))
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

        private static bool IsUsableKnownName(string knownName, string cardId)
        {
            if (string.IsNullOrWhiteSpace(knownName)) return false;
            if (string.Equals(knownName, "unknown", StringComparison.OrdinalIgnoreCase))
                return false;
            if (string.Equals(knownName, cardId, StringComparison.Ordinal))
                return false;
            if (knownName.StartsWith("BG", StringComparison.Ordinal)
                || knownName.StartsWith("TB_Bacon", StringComparison.Ordinal))
                return false;
            if (!string.IsNullOrEmpty(cardId)
                && knownName.StartsWith(cardId + "(", StringComparison.Ordinal))
                return false;
            return true;
        }
    }
}
