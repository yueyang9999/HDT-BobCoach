using System;
using System.Collections.Generic;

namespace BobCoach.Engine
{
    /// <summary>纯函数门禁：只有能证明与当前Build兼容的外部饰品统计才可激活。</summary>
    public static class TrinketStatsVerifier
    {
        public static TrinketStatsVerificationResult Verify(
            TrinketStatsSnapshot candidate,
            TrinketStatsVerificationContext context)
        {
            if (candidate == null) return Reject(TrinketStatsStatus.Quarantined, "candidate-null", null);
            if (context == null) return Reject(TrinketStatsStatus.Quarantined, "context-null", candidate);
            if (context.CurrentBuild <= 0 || context.HsJsonBuild != context.CurrentBuild)
                return Reject(TrinketStatsStatus.BuildMismatch, "exact-build-hsjson-unavailable", candidate);
            if (candidate.GameBuild != context.CurrentBuild)
                return Reject(TrinketStatsStatus.BuildMismatch, "candidate-build-mismatch", candidate);
            if (string.IsNullOrWhiteSpace(candidate.Source))
                return Reject(TrinketStatsStatus.Quarantined, "source-missing", candidate);
            if (string.IsNullOrWhiteSpace(candidate.TimePeriod))
                return Reject(TrinketStatsStatus.Quarantined, "time-period-missing", candidate);
            if (candidate.LastUpdateDateUtc == DateTime.MinValue)
                return Reject(TrinketStatsStatus.Quarantined, "last-update-missing", candidate);

            var now = context.NowUtc == DateTime.MinValue ? DateTime.UtcNow : context.NowUtc;
            if (candidate.LastUpdateDateUtc > now.AddHours(24))
                return Reject(TrinketStatsStatus.Quarantined, "last-update-in-future", candidate);
            if (context.HsJsonPublishedUtc == DateTime.MinValue)
                return Reject(TrinketStatsStatus.Quarantined, "hsjson-published-missing", candidate);
            if (candidate.LastUpdateDateUtc < context.HsJsonPublishedUtc)
                return Reject(TrinketStatsStatus.Quarantined, "stats-predate-build", candidate);

            var previous = context.PreviousActive;
            if (previous != null && previous.GameBuild == context.CurrentBuild)
            {
                if (candidate.LastUpdateDateUtc < previous.LastUpdateDateUtc)
                    return Reject(TrinketStatsStatus.Quarantined, "same-build-time-rollback", candidate);
                if (candidate.LastUpdateDateUtc == previous.LastUpdateDateUtc
                    && !string.IsNullOrEmpty(previous.ContentSha256)
                    && !string.IsNullOrEmpty(candidate.ContentSha256)
                    && !string.Equals(previous.ContentSha256, candidate.ContentSha256, StringComparison.OrdinalIgnoreCase))
                    return Reject(TrinketStatsStatus.Quarantined, "same-time-content-changed", candidate);
            }

            if (candidate.Stats == null || candidate.Stats.Count == 0)
                return Reject(TrinketStatsStatus.Quarantined, "stats-empty", candidate);
            if (candidate.TotalDataPoints <= 0)
                return Reject(TrinketStatsStatus.Quarantined, "total-data-points-invalid", candidate);

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var unknown = new List<string>();
            foreach (var stat in candidate.Stats)
            {
                if (stat == null || string.IsNullOrWhiteSpace(stat.TrinketCardId))
                    return Reject(TrinketStatsStatus.Quarantined, "card-id-missing", candidate);
                if (!ids.Add(stat.TrinketCardId))
                    return Reject(TrinketStatsStatus.Quarantined, "duplicate-card-id:" + stat.TrinketCardId, candidate);
                if (context.KnownTrinketIds == null || !context.KnownTrinketIds.Contains(stat.TrinketCardId))
                    unknown.Add(stat.TrinketCardId);
                if (stat.DataPoints < 0 || !IsProbability(stat.PickRate)
                    || !IsPlacement(stat.AveragePlacement))
                    return Reject(TrinketStatsStatus.Quarantined, "stat-range-invalid:" + stat.TrinketCardId, candidate);
                if (stat.MmrPoints == null) continue;
                foreach (var point in stat.MmrPoints)
                {
                    if (point == null || point.DataPoints < 0 || point.Mmr < 0 || point.Mmr > 100
                        || (!IsZero(point.Placement) && !IsPlacement(point.Placement))
                        || (!IsZero(point.PickRate) && !IsProbability(point.PickRate)))
                        return Reject(TrinketStatsStatus.Quarantined, "mmr-range-invalid:" + stat.TrinketCardId, candidate);
                }
            }

            if (unknown.Count > 0)
            {
                var rejected = Reject(TrinketStatsStatus.Quarantined,
                    "unknown-card-ids:" + unknown.Count, candidate);
                rejected.UnknownCardIds.AddRange(unknown);
                return rejected;
            }

            candidate.Status = TrinketStatsStatus.Verified;
            candidate.StatusReason = "verified";
            candidate.VerifiedAtUtc = now;
            return new TrinketStatsVerificationResult
            {
                Status = TrinketStatsStatus.Verified,
                Reason = "verified",
                Snapshot = candidate,
            };
        }

        private static bool IsProbability(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0 && value <= 1;
        }

        private static bool IsPlacement(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 1 && value <= 8;
        }

        private static bool IsZero(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && Math.Abs(value) < 0.0000001;
        }

        private static TrinketStatsVerificationResult Reject(
            TrinketStatsStatus status, string reason, TrinketStatsSnapshot snapshot)
        {
            return new TrinketStatsVerificationResult
            {
                Status = status,
                Reason = reason,
                Snapshot = snapshot,
            };
        }
    }

    public static class TrinketStatsFailoverPolicy
    {
        public static bool CanUseLastVerified(int currentBuild, TrinketStatsSnapshot active)
        {
            return currentBuild > 0 && active != null
                && active.Status == TrinketStatsStatus.Verified
                && active.GameBuild == currentBuild;
        }
    }
}
