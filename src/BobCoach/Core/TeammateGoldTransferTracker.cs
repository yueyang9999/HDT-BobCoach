using System.Collections.Generic;
using System.Linq;

namespace BobCoach.Engine
{
    /// <summary>Power.log金币发送动作的线程安全去重与逐回合上限追踪。</summary>
    public sealed class TeammateGoldTransferTracker
    {
        private readonly object _sync = new object();
        private readonly HashSet<string> _evidenceIds = new HashSet<string>();
        private readonly List<ObservedTeammateGoldTransfer> _observations =
            new List<ObservedTeammateGoldTransfer>();

        public IList<ObservedTeammateGoldTransfer> Observations
        {
            get { lock (_sync) return new List<ObservedTeammateGoldTransfer>(_observations); }
        }

        public bool Observe(
            TeammateGoldTransferRule rule,
            int turn,
            string actionCardId,
            int actionEntityId,
            string evidenceId,
            string evidenceSource)
        {
            lock (_sync)
            {
                if (rule == null || turn <= 0 || actionEntityId <= 0
                    || actionCardId != rule.ActionCardId
                    || string.IsNullOrEmpty(evidenceId)
                    || evidenceSource != "power_log"
                    || _evidenceIds.Contains(evidenceId))
                    return false;
                int used = _observations.Count(item => item.Turn == turn);
                if (used >= rule.MaxPerTurn) return false;

                int ordinal = used + 1;
                _evidenceIds.Add(evidenceId);
                _observations.Add(new ObservedTeammateGoldTransfer(
                    rule.SourceId + ":send_gold_to_teammate@" + turn + "#" + ordinal,
                    turn, rule.GoldPerUse, actionEntityId, evidenceId, rule.SourceId));
                return true;
            }
        }

        public void Reset()
        {
            lock (_sync)
            {
                _evidenceIds.Clear();
                _observations.Clear();
            }
        }
    }
}
