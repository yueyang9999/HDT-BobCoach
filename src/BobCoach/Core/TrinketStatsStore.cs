using System;
using System.IO;
using Newtonsoft.Json;

namespace BobCoach.Engine
{
    /// <summary>候选/已验证快照的原子本地仓库。运行时只暴露Current不可变引用。</summary>
    public sealed class TrinketStatsStore
    {
        private readonly object _gate = new object();
        private readonly string _dir;
        private TrinketStatsSnapshot _current;

        public TrinketStatsStore(string directory)
        {
            _dir = directory;
            Directory.CreateDirectory(_dir);
        }

        public TrinketStatsSnapshot Current
        {
            get { lock (_gate) { return _current; } }
        }

        public TrinketStatsSnapshot LoadActive()
        {
            var path = Path.Combine(_dir, "active.json");
            try
            {
                if (!File.Exists(path)) return null;
                var loaded = JsonConvert.DeserializeObject<TrinketStatsSnapshot>(
                    File.ReadAllText(path, System.Text.Encoding.UTF8));
                if (loaded == null || loaded.Status != TrinketStatsStatus.Verified) return null;
                lock (_gate) { _current = loaded; }
                return loaded;
            }
            catch { return null; }
        }

        public void Activate(TrinketStatsSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Status != TrinketStatsStatus.Verified)
                throw new InvalidOperationException("only-verified-snapshot-can-activate");
            var activePath = Path.Combine(_dir, "active.json");
            var previousPath = Path.Combine(_dir, "previous.json");
            if (File.Exists(activePath))
                File.Copy(activePath, previousPath, true);
            AtomicWrite(activePath, JsonConvert.SerializeObject(snapshot, Formatting.Indented));
            lock (_gate) { _current = snapshot; }
        }

        public void PersistCandidate(TrinketStatsVerificationResult result)
        {
            if (result == null) return;
            AtomicWrite(Path.Combine(_dir, "candidate.json"), JsonConvert.SerializeObject(new
            {
                status = result.Status.ToString(),
                reason = result.Reason,
                unknownCardIds = result.UnknownCardIds,
                snapshot = result.Snapshot,
            }, Formatting.Indented));
        }

        public void WriteHealth(int currentBuild, TrinketStatsStatus status, string reason, DateTime checkedAtUtc)
        {
            var active = Current;
            AtomicWrite(Path.Combine(_dir, "health.json"), JsonConvert.SerializeObject(new
            {
                schemaVersion = 1,
                currentBuild = currentBuild,
                activeBuild = active != null ? active.GameBuild : 0,
                activeLastUpdateUtc = active != null ? active.LastUpdateDateUtc : DateTime.MinValue,
                activeRows = active != null && active.Stats != null ? active.Stats.Count : 0,
                activeDataPoints = active != null ? active.TotalDataPoints : 0,
                status = status.ToString(),
                reason = reason ?? "",
                checkedAtUtc = checkedAtUtc,
            }, Formatting.Indented));
        }

        private static void AtomicWrite(string path, string content)
        {
            var temp = path + ".tmp";
            File.WriteAllText(temp, content, System.Text.Encoding.UTF8);
            // 写后解析，防磁盘落下截断JSON再替换active/candidate/health。
            JsonConvert.DeserializeObject(File.ReadAllText(temp, System.Text.Encoding.UTF8));
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }
    }
}
