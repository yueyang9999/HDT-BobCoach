using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace BobCoach.Engine
{
    /// <summary>Firestone候选拉取 + exact-build HSJSON核验 + 自动激活。</summary>
    public sealed class TrinketStatsUpdater : IDisposable
    {
        public const string FirestoneUrl =
            "https://static.zerotoheroes.com/api/bgs/trinket-stats/last-patch/overview-from-hourly.gz.json";

        private readonly TrinketStatsFetcher _fetcher;
        private readonly TrinketStatsStore _store;
        private readonly Action<string> _log;
        private readonly SemaphoreSlim _singleFlight = new SemaphoreSlim(1, 1);
        private readonly Timer _timer;
        private readonly Timer _retryTimer;
        private readonly CancellationTokenSource _disposeCts = new CancellationTokenSource();
        private volatile int _currentBuild;
        private volatile bool _disposed;
        private int _factsBuild;
        private DateTime _factsPublishedUtc;
        private HashSet<string> _factsIds;
        private int _failureCount;
        private volatile int _statusCode;
        private volatile string _statusReason;

        public TrinketStatsStatus Status { get { return (TrinketStatsStatus)_statusCode; } }
        public string StatusReason { get { return _statusReason ?? ""; } }
        public TrinketStatsSnapshot Active { get { return _store.Current; } }

        public TrinketStatsUpdater(string dataDirectory, Action<string> log)
        {
            _fetcher = new TrinketStatsFetcher();
            _store = new TrinketStatsStore(dataDirectory);
            _log = log ?? delegate { };
            _statusCode = (int)TrinketStatsStatus.Uninitialized;
            _statusReason = "build-not-seen";
            _store.LoadActive();
            _timer = new Timer(_ => RequestCheck(false), null, TimeSpan.FromHours(6), TimeSpan.FromHours(6));
            _retryTimer = new Timer(_ => RequestCheck(false), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        public void SetCurrentBuild(int build)
        {
            if (build <= 0 || _disposed) return;
            int previous = Interlocked.Exchange(ref _currentBuild, build);
            if (previous != build)
            {
                var active = _store.Current;
                if (!TrinketStatsFailoverPolicy.CanUseLastVerified(build, active))
                    SetStatus(TrinketStatsStatus.BuildMismatch, "active-snapshot-not-current-build");
                RequestCheck(true);
            }
        }

        public void RequestCheck(bool force)
        {
            if (_disposed || _currentBuild <= 0) return;
            Task.Run(() => CheckAsync(force, _disposeCts.Token));
        }

        private async Task CheckAsync(bool force, CancellationToken token)
        {
            if (!await _singleFlight.WaitAsync(0, token).ConfigureAwait(false)) return;
            var checkedAt = DateTime.UtcNow;
            try
            {
                int build = _currentBuild;
                SetStatus(TrinketStatsStatus.Checking, "checking-build-" + build);
                var previousActive = _store.Current;
                var conditional = !force && previousActive != null ? previousActive : null;
                var firestone = await _fetcher.FetchAsync(
                    FirestoneUrl,
                    conditional != null ? conditional.ETag : "",
                    conditional != null ? (DateTime?)conditional.FetchedAtUtc : null,
                    5 * 1024 * 1024, TimeSpan.FromSeconds(15), token).ConfigureAwait(false);

                TrinketStatsSnapshot candidate;
                if (firestone.NotModified)
                {
                    if (previousActive == null)
                        throw new InvalidOperationException("304-without-active-snapshot");
                    candidate = CloneForBuild(previousActive, build, DateTime.UtcNow);
                }
                else
                {
                    candidate = ParseFirestone(firestone.Content, build, firestone.ETag, DateTime.UtcNow);
                }

                await EnsureBuildFactsAsync(build, token).ConfigureAwait(false);
                var context = new TrinketStatsVerificationContext
                {
                    CurrentBuild = build,
                    HsJsonBuild = _factsBuild,
                    HsJsonPublishedUtc = _factsPublishedUtc,
                    NowUtc = DateTime.UtcNow,
                    KnownTrinketIds = _factsIds,
                    PreviousActive = previousActive != null && previousActive.GameBuild == build ? previousActive : null,
                };
                var result = TrinketStatsVerifier.Verify(candidate, context);
                if (!result.IsVerified)
                {
                    _store.PersistCandidate(result);
                    SetStatus(result.Status, result.Reason);
                    return;
                }

                _store.Activate(result.Snapshot);
                Interlocked.Exchange(ref _failureCount, 0);
                _retryTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                SetStatus(TrinketStatsStatus.Verified,
                    string.Format("build={0} rows={1} points={2}", build,
                        result.Snapshot.Stats.Count, result.Snapshot.TotalDataPoints));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                int build = _currentBuild;
                if (TrinketStatsFailoverPolicy.CanUseLastVerified(build, _store.Current))
                    SetStatus(TrinketStatsStatus.SourceUnavailable, "same-build-cache:" + ex.Message);
                else
                    SetStatus(TrinketStatsStatus.BuildMismatch, "no-current-build-cache:" + ex.Message);
                ScheduleRetry();
            }
            finally
            {
                try { _store.WriteHealth(_currentBuild, Status, StatusReason, checkedAt); } catch { }
                _singleFlight.Release();
            }
        }

        private async Task EnsureBuildFactsAsync(int build, CancellationToken token)
        {
            if (_factsBuild == build && _factsIds != null && _factsIds.Count > 0) return;
            string url = "https://api.hearthstonejson.com/v1/" + build + "/enUS/cards.json";
            var response = await _fetcher.FetchAsync(url, "", null,
                64 * 1024 * 1024, TimeSpan.FromSeconds(45), token).ConfigureAwait(false);
            if (response.NotModified || response.Content == null)
                throw new InvalidOperationException("exact-build-hsjson-empty");
            var cards = JArray.Parse(Encoding.UTF8.GetString(response.Content));
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var card in cards.OfType<JObject>())
            {
                if (!string.Equals((string)card["type"], "BATTLEGROUND_TRINKET", StringComparison.Ordinal)) continue;
                var id = (string)card["id"];
                if (!string.IsNullOrEmpty(id)) ids.Add(id);
            }
            if (ids.Count == 0) throw new InvalidOperationException("exact-build-hsjson-no-trinkets");
            _factsBuild = build;
            _factsIds = ids;
            _factsPublishedUtc = response.LastModifiedUtc;
        }

        internal static TrinketStatsSnapshot ParseFirestone(byte[] bytes, int build, string etag, DateTime fetchedAtUtc)
        {
            if (bytes == null || bytes.Length == 0) throw new InvalidOperationException("firestone-empty");
            var raw = Encoding.UTF8.GetString(bytes);
            var root = JObject.Parse(raw);
            DateTime updated;
            if (!DateTime.TryParse((string)root["lastUpdateDate"], CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out updated))
                updated = DateTime.MinValue;

            var snapshot = new TrinketStatsSnapshot
            {
                Source = "firestone",
                SourceUrl = FirestoneUrl,
                TimePeriod = (string)root["timePeriod"] ?? "",
                GameBuild = build,
                LastUpdateDateUtc = updated,
                FetchedAtUtc = fetchedAtUtc,
                TotalDataPoints = (long?)root["dataPoints"] ?? 0,
                ContentSha256 = Sha256(bytes),
                ETag = etag ?? "",
            };

            var stats = root["trinketStats"] as JArray;
            if (stats == null) return snapshot;
            foreach (var item in stats.OfType<JObject>())
            {
                var record = new TrinketStatRecord
                {
                    TrinketCardId = (string)item["trinketCardId"] ?? "",
                    DataPoints = (int?)item["dataPoints"] ?? 0,
                    PickRate = (double?)item["pickRate"] ?? double.NaN,
                    AveragePlacement = (double?)item["averagePlacement"] ?? double.NaN,
                };
                var points = new Dictionary<int, TrinketStatMmrPoint>();
                MergePlacement(points, item["averagePlacementAtMmr"] as JArray);
                MergePickRate(points, item["pickRateAtMmr"] as JArray);
                record.MmrPoints = points.Values.OrderByDescending(p => p.Mmr).ToList();
                snapshot.Stats.Add(record);
            }
            return snapshot;
        }

        private static void MergePlacement(Dictionary<int, TrinketStatMmrPoint> points, JArray values)
        {
            if (values == null) return;
            foreach (var value in values.OfType<JObject>())
            {
                int mmr = (int?)value["mmr"] ?? -1;
                if (!points.TryGetValue(mmr, out var point))
                    points[mmr] = point = new TrinketStatMmrPoint { Mmr = mmr };
                point.DataPoints = Math.Max(point.DataPoints, (int?)value["dataPoints"] ?? 0);
                point.Placement = (double?)value["placement"] ?? 0;
            }
        }

        private static void MergePickRate(Dictionary<int, TrinketStatMmrPoint> points, JArray values)
        {
            if (values == null) return;
            foreach (var value in values.OfType<JObject>())
            {
                int mmr = (int?)value["mmr"] ?? -1;
                if (!points.TryGetValue(mmr, out var point))
                    points[mmr] = point = new TrinketStatMmrPoint { Mmr = mmr };
                point.DataPoints = Math.Max(point.DataPoints, (int?)value["dataPoints"] ?? 0);
                point.PickRate = (double?)value["pickRate"] ?? 0;
            }
        }

        private static TrinketStatsSnapshot CloneForBuild(TrinketStatsSnapshot source, int build, DateTime fetchedAt)
        {
            return new TrinketStatsSnapshot
            {
                SchemaVersion = source.SchemaVersion,
                Source = source.Source,
                SourceUrl = source.SourceUrl,
                TimePeriod = source.TimePeriod,
                GameBuild = build,
                LastUpdateDateUtc = source.LastUpdateDateUtc,
                FetchedAtUtc = fetchedAt,
                TotalDataPoints = source.TotalDataPoints,
                ContentSha256 = source.ContentSha256,
                ETag = source.ETag,
                Stats = source.Stats,
            };
        }

        private static string Sha256(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
        }

        private void SetStatus(TrinketStatsStatus status, string reason)
        {
            Interlocked.Exchange(ref _statusCode, (int)status);
            _statusReason = reason ?? "";
            try { _log("TrinketStats: " + status + " " + _statusReason); } catch { }
        }

        private void ScheduleRetry()
        {
            if (_disposed) return;
            int attempt = Interlocked.Increment(ref _failureCount);
            TimeSpan delay = attempt == 1 ? TimeSpan.FromMinutes(15)
                : attempt == 2 ? TimeSpan.FromHours(1)
                : TimeSpan.FromHours(6);
            _retryTimer.Change(delay, Timeout.InfiniteTimeSpan);
            try { _log("TrinketStats retry scheduled in " + delay); } catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _disposeCts.Cancel();
            _timer.Dispose();
            _retryTimer.Dispose();
            _fetcher.Dispose();
        }
    }
}
