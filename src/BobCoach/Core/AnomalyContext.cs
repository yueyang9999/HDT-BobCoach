using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace BobCoach.Engine
{
    public sealed class AnomalyContext
    {
        private static readonly AnomalyContext EmptyContext = new AnomalyContext(
            "", new List<string>(), false);

        private readonly ReadOnlyCollection<string> _activeGlobalEffectIds;

        private AnomalyContext(string primaryAnomalyId, List<string> activeIds, bool ambiguous)
        {
            PrimaryAnomalyId = primaryAnomalyId ?? "";
            _activeGlobalEffectIds = new ReadOnlyCollection<string>(activeIds ?? new List<string>());
            HasAmbiguousPrimary = ambiguous;
        }

        public static AnomalyContext Empty { get { return EmptyContext; } }
        public string PrimaryAnomalyId { get; private set; }
        public IList<string> ActiveGlobalEffectIds { get { return _activeGlobalEffectIds; } }
        public bool HasAmbiguousPrimary { get; private set; }

        public static AnomalyContext Resolve(
            IEnumerable<string> observedCardIds,
            AnomalyRegistry registry,
            bool isDuos)
        {
            if (observedCardIds == null || registry == null) return Empty;

            var active = new HashSet<string>(StringComparer.Ordinal);
            var primary = new HashSet<string>(StringComparer.Ordinal);
            foreach (var observedId in observedCardIds)
            {
                var anomaly = registry.GetById(observedId);
                if (anomaly == null) continue;
                if (string.Equals(anomaly.Availability, "inactive", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(anomaly.Availability, "not_in_build", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (string.Equals(anomaly.Scope, "duo", StringComparison.OrdinalIgnoreCase) && !isDuos)
                    continue;

                if (string.Equals(anomaly.Lifecycle, "primary", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(anomaly.Lifecycle, "primary_variant", StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrEmpty(anomaly.Lifecycle))
                    primary.Add(anomaly.AnomalyId);
                else if (string.Equals(anomaly.Lifecycle, "timewarp_effect", StringComparison.OrdinalIgnoreCase))
                    active.Add(anomaly.AnomalyId);
            }

            var activeList = active.OrderBy(id => id, StringComparer.Ordinal).ToList();
            bool ambiguous = primary.Count > 1;
            string primaryId = primary.Count == 1 ? primary.First() : "";
            return new AnomalyContext(primaryId, activeList, ambiguous);
        }
    }
}
