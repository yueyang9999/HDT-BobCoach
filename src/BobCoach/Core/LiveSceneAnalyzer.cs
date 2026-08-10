using System.Collections.Generic;

namespace BobCoach.Engine
{
    public sealed class LiveSceneSnapshot
    {
        public int Turn;
        public int TavernTier;
        public int Health;
        public int MaxHealth;
        public int BoardMinionCount;
        public int HandMinionCount;
        public int ShopMinionCount;
        public int BoardPower;
        public int OpponentAveragePower;
        public bool NormalRecruitAvailable;
        public EconomySnapshot Economy;
        public BoardCompositionProfile Board = new BoardCompositionProfile();
        public SurvivalRisk Survival = new SurvivalRisk();
        public UnknownCoverage Unknown = new UnknownCoverage();
    }

    public sealed class BoardCompositionProfile
    {
        public Dictionary<string, int> TribeCounts = new Dictionary<string, int>();
        public int GoldenCount;
        public int TripleProgressCount;
        public int TauntCount;
        public int DivineShieldCount;
        public int ActivateCount;
    }

    public sealed class SurvivalRisk
    {
        public double PowerRatio;
        public bool LowHealth;
        public bool Desperate;
        public string Level = "Normal";
    }

    public sealed class UnknownCoverage
    {
        public int UnknownCardIds;
        public int MissingMechanicFacts;
        public IList<string> UnknownSamples = new List<string>();
    }

    /// <summary>
    /// 实时局面分析入口。当前版本只负责稳定局面摘要，
    /// 后续统一动作评分必须消费本对象而不是各自扫描 GameState。
    /// </summary>
    public static class LiveSceneAnalyzer
    {
        public static LiveSceneSnapshot Analyze(
            int turn,
            int tavernTier,
            int health,
            int maxHealth,
            int boardMinionCount,
            int handMinionCount,
            int shopMinionCount,
            int boardPower,
            int opponentAveragePower,
            bool normalRecruitAvailable,
            int liveGold,
            int maxGold,
            int? upgradeCost,
            int refreshCost,
            bool manualRefreshAllowed,
            bool canBuyAny)
        {
            var economy = EconomySnapshot.Build(
                liveGold,
                maxGold,
                upgradeCost,
                refreshCost,
                manualRefreshAllowed,
                canBuyAny);

            double powerRatio = opponentAveragePower > 0
                ? (double)boardPower / opponentAveragePower
                : 1.0;
            bool lowHealth = maxHealth > 0 && health <= 15;
            bool desperate = lowHealth && powerRatio < 0.7;

            return new LiveSceneSnapshot
            {
                Turn = turn,
                TavernTier = tavernTier,
                Health = health,
                MaxHealth = maxHealth,
                BoardMinionCount = boardMinionCount,
                HandMinionCount = handMinionCount,
                ShopMinionCount = shopMinionCount,
                BoardPower = boardPower,
                OpponentAveragePower = opponentAveragePower,
                NormalRecruitAvailable = normalRecruitAvailable,
                Economy = economy,
                Survival = new SurvivalRisk
                {
                    PowerRatio = powerRatio,
                    LowHealth = lowHealth,
                    Desperate = desperate,
                    Level = desperate ? "Desperate" : lowHealth ? "LowHealth" : "Normal",
                },
            };
        }
    }
}
