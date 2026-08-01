using BobCoach.Engine;

namespace BobCoach.Engine.Decision
{
    public sealed class ProductionSeasonFeatureExtension : ISeasonFeatureExtension,
        IRankingModelRuntimeContextProvider
    {
        public static readonly ProductionSeasonFeatureExtension Instance =
            new ProductionSeasonFeatureExtension();

        private readonly RankingModelRuntimeContext _runtimeContext;

        public string GameBuild
        {
            get { return _runtimeContext == null ? "unknown" : _runtimeContext.GameBuild; }
        }

        public string SeasonId
        {
            get { return _runtimeContext == null ? "none" : _runtimeContext.SeasonId; }
        }

        public string CardPoolIdentity
        {
            get { return _runtimeContext == null ? "unknown" : _runtimeContext.CardPoolIdentity; }
        }

        private ProductionSeasonFeatureExtension()
        {
            _runtimeContext = ProductionRankingModelApprovalCatalog.GetApprovedContext();
        }

        public RankingModelRuntimeContext GetRuntimeContext()
        {
            return _runtimeContext;
        }

        public bool TryEvaluate(GameState state, ActionDescriptor action, GameState nextState,
            out double value)
        {
            value = 0d;
            return false;
        }
    }

    internal static class ProductionRankingModelApprovalCatalog
    {
        private static readonly RankingModelRuntimeContext[] ApprovedContexts =
            new RankingModelRuntimeContext[0];

        public static RankingModelRuntimeContext GetApprovedContext()
        {
            return ApprovedContexts.Length == 1 ? ApprovedContexts[0] : null;
        }
    }
}
