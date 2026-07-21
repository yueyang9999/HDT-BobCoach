using System;
namespace BobCoach.Engine
{
    /// <summary>外部饰品统计源授权边界；公开版没有已授权来源。</summary>
    public sealed class TrinketStatsUpdater : IDisposable
    {
        public TrinketStatsStatus Status
        {
            get { return TrinketStatsStatus.SourceUnavailable; }
        }

        public string StatusReason
        {
            get { return "no-authorized-external-source"; }
        }

        public TrinketStatsSnapshot Active
        {
            get { return null; }
        }

        public void Dispose() { }
    }
}
