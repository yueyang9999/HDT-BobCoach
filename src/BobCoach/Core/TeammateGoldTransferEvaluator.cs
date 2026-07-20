using System.Linq;

namespace BobCoach.Engine
{
    public static class TeammateGoldTransferEvaluator
    {
        public static int GetUsedCount(GameState state, int turn)
        {
            if (state == null || turn <= 0) return 0;
            int observed = state.ObservedTeammateGoldTransfers == null ? 0
                : state.ObservedTeammateGoldTransfers.Count(item => item != null
                    && item.Turn == turn);
            int simulated = state.SimulatedTeammateGoldTransfers == null ? 0
                : state.SimulatedTeammateGoldTransfers.Count(item => item != null
                    && item.Turn == turn);
            return observed + simulated;
        }
    }
}
