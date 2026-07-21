using System;
using System.Collections.Generic;
using BobCoach.Engine;

internal static class TrinketStatsVerifierHarness
{
    private const int Build = 12345;
    private static readonly DateTime Now = new DateTime(2026, 7, 21, 12, 0, 0, DateTimeKind.Utc);

    private static int Main()
    {
        if (!Verify(CreateCandidate(), CreateContext()).IsVerified)
            return Fail("valid source-independent synthetic data was rejected");

        var buildMismatch = CreateCandidate();
        buildMismatch.GameBuild = Build + 1;
        if (!Rejects(buildMismatch, CreateContext(), TrinketStatsStatus.BuildMismatch, "candidate-build-mismatch"))
            return Fail("candidate Build mismatch was not rejected");

        var duplicate = CreateCandidate();
        duplicate.Stats.Add(CreateRecord("SYNTH_TRINKET_A"));
        if (!Rejects(duplicate, CreateContext(), TrinketStatsStatus.Quarantined, "duplicate-card-id:SYNTH_TRINKET_A"))
            return Fail("duplicate trinket ID was not quarantined");

        var unknown = CreateCandidate();
        unknown.Stats[0].TrinketCardId = "SYNTH_UNKNOWN";
        var unknownResult = Verify(unknown, CreateContext());
        if (unknownResult.IsVerified || unknownResult.Reason != "unknown-card-ids:1"
            || unknownResult.UnknownCardIds.Count != 1
            || unknownResult.UnknownCardIds[0] != "SYNTH_UNKNOWN")
            return Fail("unknown trinket ID was not quarantined with diagnostics");

        var invalidStat = CreateCandidate();
        invalidStat.Stats[0].PickRate = 1.01;
        if (!Rejects(invalidStat, CreateContext(), TrinketStatsStatus.Quarantined, "stat-range-invalid:SYNTH_TRINKET_A"))
            return Fail("invalid aggregate range was not quarantined");

        var invalidMmr = CreateCandidate();
        invalidMmr.Stats[0].MmrPoints[0].Placement = 9;
        if (!Rejects(invalidMmr, CreateContext(), TrinketStatsStatus.Quarantined, "mmr-range-invalid:SYNTH_TRINKET_A"))
            return Fail("invalid MMR range was not quarantined");

        var changedContent = CreateCandidate();
        var rollbackContext = CreateContext();
        rollbackContext.PreviousActive = CreateCandidate();
        rollbackContext.PreviousActive.ContentSha256 = "PREVIOUS_HASH";
        changedContent.ContentSha256 = "CHANGED_HASH";
        if (!Rejects(changedContent, rollbackContext, TrinketStatsStatus.Quarantined, "same-time-content-changed"))
            return Fail("same-timestamp content change was not quarantined");

        var missingSource = CreateCandidate();
        missingSource.Source = "";
        if (!Rejects(missingSource, CreateContext(), TrinketStatsStatus.Quarantined, "source-missing"))
            return Fail("missing source metadata was not quarantined");

        var missingPeriod = CreateCandidate();
        missingPeriod.TimePeriod = "";
        if (!Rejects(missingPeriod, CreateContext(), TrinketStatsStatus.Quarantined, "time-period-missing"))
            return Fail("missing time-period metadata was not quarantined");

        Console.WriteLine("PASS source-independent synthetic trinket statistics validation");
        return 0;
    }

    private static TrinketStatsSnapshot CreateCandidate()
    {
        return new TrinketStatsSnapshot
        {
            Source = "synthetic-test-source",
            SourceUrl = "https://example.invalid/synthetic-only",
            TimePeriod = "synthetic-window",
            GameBuild = Build,
            LastUpdateDateUtc = Now.AddHours(-1),
            FetchedAtUtc = Now,
            TotalDataPoints = 30,
            ContentSha256 = "SYNTHETIC_HASH",
            Stats = new List<TrinketStatRecord>
            {
                CreateRecord("SYNTH_TRINKET_A"),
                CreateRecord("SYNTH_TRINKET_B"),
            },
        };
    }

    private static TrinketStatRecord CreateRecord(string id)
    {
        return new TrinketStatRecord
        {
            TrinketCardId = id,
            DataPoints = 15,
            PickRate = 0.5,
            AveragePlacement = 4.25,
            MmrPoints = new List<TrinketStatMmrPoint>
            {
                new TrinketStatMmrPoint
                {
                    Mmr = 50,
                    DataPoints = 10,
                    Placement = 4.5,
                    PickRate = 0.4,
                },
            },
        };
    }

    private static TrinketStatsVerificationContext CreateContext()
    {
        return new TrinketStatsVerificationContext
        {
            CurrentBuild = Build,
            HsJsonBuild = Build,
            HsJsonPublishedUtc = Now.AddHours(-2),
            NowUtc = Now,
            KnownTrinketIds = new HashSet<string>(StringComparer.Ordinal)
            {
                "SYNTH_TRINKET_A",
                "SYNTH_TRINKET_B",
            },
        };
    }

    private static TrinketStatsVerificationResult Verify(
        TrinketStatsSnapshot candidate,
        TrinketStatsVerificationContext context)
    {
        return TrinketStatsVerifier.Verify(candidate, context);
    }

    private static bool Rejects(
        TrinketStatsSnapshot candidate,
        TrinketStatsVerificationContext context,
        TrinketStatsStatus status,
        string reason)
    {
        var result = Verify(candidate, context);
        return !result.IsVerified && result.Status == status && result.Reason == reason;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine("FAIL " + message);
        return 1;
    }
}
