using System;
using System.Collections.Generic;
using System.Linq;

namespace BobCoach.Engine
{
    public class CardSynergyScore
    {
        public string CardId = "";
        public float TribeScore;
        public float MechanicScore;
        public float HeroScore;
        public float TotalScore;
        public string Reason = "";
    }

    /// <summary>
    /// 通用三维协同引擎。只消费场面种族、本机HearthDb派生语义和Bob英雄策略，
    /// 不依赖外部流派模板或卡牌编排。
    /// </summary>
    public class SynergyEngine
    {
        private SemanticSynergyEvaluator _semanticSynergy;
        private HeroPowerEngine _heroPower;

        internal void SetCardSemanticSource(ICardSemanticSource source)
        {
            _semanticSynergy = source == null ? null : new SemanticSynergyEvaluator(source);
        }

        public void SetHeroPowerEngine(HeroPowerEngine heroPower)
        {
            _heroPower = heroPower;
        }

        /// <summary>
        /// 计算商店卡牌与当前场面和英雄的通用协同分。
        /// 原流派维度退出后不重新分配权重，其分量按零处理。
        /// </summary>
        public CardSynergyScore ScoreCard(string shopCardId, string shopTribe,
            GameState state, string heroCardId)
        {
            var result = new CardSynergyScore { CardId = shopCardId };
            if (state == null || string.IsNullOrEmpty(shopCardId))
                return result;

            result.TribeScore = ComputeTribeScore(shopTribe, state);
            result.MechanicScore = ComputeMechanicScore(shopCardId, state);
            result.HeroScore = ComputeHeroScore(shopTribe, heroCardId);

            float wTribe, wMech, wHero;
            if (state.Turn <= 5)
            {
                wTribe = 0.40f;
                wMech = 0.20f;
                wHero = 0.20f;
            }
            else if (state.Turn <= 9)
            {
                wTribe = 0.25f;
                wMech = 0.30f;
                wHero = 0.15f;
            }
            else
            {
                wTribe = 0.20f;
                wMech = 0.25f;
                wHero = 0.15f;
            }

            result.TotalScore = result.TribeScore * wTribe
                              + result.MechanicScore * wMech
                              + result.HeroScore * wHero;
            result.Reason = BuildReason(result);
            return result;
        }

        private static float ComputeTribeScore(string shopTribe, GameState state)
        {
            if (string.IsNullOrEmpty(shopTribe)) return 0.5f;

            var tribeCounts = new Dictionary<string, int>();
            if (state.BoardMinions != null)
            {
                foreach (var minion in state.BoardMinions)
                {
                    if (minion == null || string.IsNullOrEmpty(minion.Tribe)) continue;
                    foreach (var tribe in MinionData.GetTribesArray(minion.Tribe))
                    {
                        int count;
                        tribeCounts.TryGetValue(tribe, out count);
                        tribeCounts[tribe] = count + 1;
                    }
                }
            }

            int maxCount = 0;
            string dominantTribe = "";
            foreach (var pair in tribeCounts)
            {
                if (pair.Value <= maxCount) continue;
                maxCount = pair.Value;
                dominantTribe = pair.Key;
            }

            if (string.IsNullOrEmpty(dominantTribe)) return 0.5f;
            return shopTribe == dominantTribe ? 1.0f : 0.3f;
        }

        private float ComputeMechanicScore(string shopCardId, GameState state)
        {
            if (_semanticSynergy == null || state == null || state.BoardMinions == null)
                return 0f;
            return _semanticSynergy.ComputeMatchRatio(
                state.BoardMinions.Where(card => card != null).Select(card => card.CardId),
                shopCardId);
        }

        private float ComputeHeroScore(string shopTribe, string heroCardId)
        {
            if (_heroPower == null || string.IsNullOrEmpty(heroCardId)
                || string.IsNullOrEmpty(shopTribe))
                return 0f;

            float affinity = _heroPower.GetTribeAffinity(heroCardId, shopTribe);
            return Math.Min(1f, Math.Max(0f, affinity));
        }

        private static string BuildReason(CardSynergyScore score)
        {
            if (score == null || score.TotalScore < 0.08f) return "无协同";
            if (score.TotalScore < 0.18f) return "场协同低";

            var parts = new List<string>();
            if (score.TribeScore >= 0.6f) parts.Add("同族");
            else if (score.TribeScore >= 0.3f) parts.Add("半同族");
            if (score.MechanicScore >= 0.25f) parts.Add("机制合");
            if (score.HeroScore >= 0.12f) parts.Add("英雄偏好");
            if (parts.Count == 0) parts.Add("一般");
            return string.Join(" ", parts);
        }
    }
}
