using System.Collections.Generic;
using System.Linq;

namespace BobCoach.Engine
{
    public static class TimewarpPurchaseAdvisor
    {
        public static int SelectBestAffordableIndex(
            PowerLogChoiceBatch batch,
            IReadOnlyDictionary<int, double> scoresByIndex)
        {
            if (batch == null || batch.Candidates == null
                || batch.TimeCoinCount <= 0 || scoresByIndex == null)
                return -1;

            int bestIndex = -1;
            double bestScore = double.MinValue;
            for (int index = 0; index < batch.Candidates.Count; index++)
            {
                var candidate = batch.Candidates[index];
                if (candidate == null || candidate.PurchaseCost <= 0
                    || candidate.PurchaseCost > batch.TimeCoinCount)
                    continue;
                double score;
                if (!scoresByIndex.TryGetValue(index, out score) || score <= bestScore)
                    continue;
                bestIndex = index;
                bestScore = score;
            }
            return bestIndex;
        }

        public static List<int> SelectAffordableIndices(
            PowerLogChoiceBatch batch,
            IReadOnlyDictionary<int, double> scoresByIndex,
            int maxIndices = 2)
        {
            var result = new List<int>();
            if (batch == null || batch.Candidates == null
                || batch.TimeCoinCount <= 0 || scoresByIndex == null
                || maxIndices <= 0)
                return result;

            var affordable = batch.Candidates
                .Select((candidate, index) => new { candidate, index })
                .Where(item => item.candidate != null
                    && item.candidate.PurchaseCost > 0
                    && item.candidate.PurchaseCost <= batch.TimeCoinCount)
                .OrderByDescending(item =>
                {
                    double score;
                    return scoresByIndex.TryGetValue(item.index, out score)
                        ? score : 0.0;
                })
                .ToList();

            int remaining = batch.TimeCoinCount;
            foreach (var item in affordable)
            {
                if (result.Count >= maxIndices) break;
                if (item.candidate.PurchaseCost > remaining) continue;
                result.Add(item.index);
                remaining -= item.candidate.PurchaseCost;
            }
            return result;
        }
    }
}
