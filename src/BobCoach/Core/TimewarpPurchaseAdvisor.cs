using System.Collections.Generic;

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
    }
}
