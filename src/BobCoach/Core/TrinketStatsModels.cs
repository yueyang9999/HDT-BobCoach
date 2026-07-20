using System;
using System.Collections.Generic;

namespace BobCoach.Engine
{
    public enum TrinketStatsStatus
    {
        Uninitialized,
        Checking,
        Verified,
        SourceUnavailable,
        Quarantined,
        BuildMismatch,
    }

    public sealed class TrinketStatMmrPoint
    {
        public int Mmr;
        public int DataPoints;
        public double Placement;
        public double PickRate;
    }

    public sealed class TrinketStatRecord
    {
        public string TrinketCardId = "";
        public int DataPoints;
        public double PickRate;
        public double AveragePlacement;
        public List<TrinketStatMmrPoint> MmrPoints = new List<TrinketStatMmrPoint>();
    }

    public sealed class TrinketStatsSnapshot
    {
        public int SchemaVersion = 1;
        public string Source = "";
        public string SourceUrl = "";
        public string TimePeriod = "";
        public int GameBuild;
        public DateTime LastUpdateDateUtc;
        public DateTime FetchedAtUtc;
        public DateTime VerifiedAtUtc;
        public long TotalDataPoints;
        public string ContentSha256 = "";
        public string ETag = "";
        public TrinketStatsStatus Status = TrinketStatsStatus.Uninitialized;
        public string StatusReason = "";
        public List<TrinketStatRecord> Stats = new List<TrinketStatRecord>();
    }

    public sealed class TrinketStatsVerificationContext
    {
        public int CurrentBuild;
        public int HsJsonBuild;
        public DateTime HsJsonPublishedUtc;
        public DateTime NowUtc;
        public HashSet<string> KnownTrinketIds = new HashSet<string>(StringComparer.Ordinal);
        public TrinketStatsSnapshot PreviousActive;
    }

    public sealed class TrinketStatsVerificationResult
    {
        public TrinketStatsStatus Status;
        public string Reason = "";
        public TrinketStatsSnapshot Snapshot;
        public List<string> UnknownCardIds = new List<string>();
        public bool IsVerified { get { return Status == TrinketStatsStatus.Verified; } }
    }
}
