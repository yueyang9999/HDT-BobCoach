using System;
using System.IO;

namespace BobCoach.Engine
{
    /// <summary>
    /// 线性加权价值函数 V(s) = sum(weights[i] * features[i])。
    /// 权重使用内置生产基线，并支持进程内显式更新。
    /// </summary>
    public class ValueFunction
    {
        private float[] _weights;

        public bool IsLoaded { get; private set; }
        public float[] Weights { get { return _weights; } }

        public ValueFunction()
        {
            _weights = GetDefaultWeights();
        }

        /// <summary>
        /// 评估状态价值。返回归一化得分（越高越好）。
        /// </summary>
        public float Evaluate(float[] features)
        {
            return Evaluate(features, _weights);
        }

        /// <summary>
        /// 使用自定义权重评估（如 TurnPhaseEngine 动态调整的权重）。
        /// </summary>
        public float Evaluate(float[] features, float[] customWeights)
        {
            if (features == null || customWeights == null) return 0f;
            if (features.Length < FeatureExtractor.FeatureCount)
                VfLog(string.Format("Evaluate: features={0} < FeatureCount={1}", features.Length, FeatureExtractor.FeatureCount));
            int n = Math.Min(features.Length, customWeights.Length);
            float score = 0f;
            for (int i = 0; i < n; i++)
                score += customWeights[i] * features[i];

            // 额外惩罚：血量低于最大生命值的50%
            if (features.Length > FeatureExtractor.F_HEALTH && features[FeatureExtractor.F_HEALTH] < 0.5f)
                score -= 0.2f;

            // 防御NaN/Infinity传播
            if (float.IsNaN(score) || float.IsInfinity(score))
                return 0f;

            return score;
        }

        /// <summary>
        /// 允许外部热更新权重（如 ProfileEngine 个性化调整）。
        /// </summary>
        public void UpdateWeights(float[] newWeights)
        {
            if (newWeights != null && newWeights.Length > 0)
            {
                if (newWeights.Length < FeatureExtractor.FeatureCount)
                    VfLog(string.Format("ValueFunction.UpdateWeights: {0} < FeatureCount {1}, 缺失特征权重",
                        newWeights.Length, FeatureExtractor.FeatureCount));
                _weights = (float[])newWeights.Clone();
            }
        }

        // ── 默认权重（MVP阶段手工设计） ──

        // 当前生产权重基线。前21项来自历史生产资源，第22项是原加载器补齐值。
        private static float[] GetDefaultWeights()
        {
            return new float[]
            {
                0.2148f, 0.1172f, 0.0488f, 0.0293f, 0.2100f, 0.0488f,
                0.1367f, 0.1465f, 0.0488f, 0.2441f, 0.0800f, 0.0977f,
                0.0488f, 0.0293f, 0.0977f, 0.1500f, 0.0293f, 0.0500f,
                0.0977f, 0.0391f, 0.0586f, 0.0600f,
            };
        }

        private static void VfLog(string msg)
        {
            try
            {
                var dir = BobCoachDataPaths.Root;
                Directory.CreateDirectory(dir);
                var line = string.Format("[{0:O}] [ValueFunction] {1}\n", DateTime.UtcNow, msg);
                File.AppendAllText(Path.Combine(dir, "bob_coach.log"), line, System.Text.Encoding.UTF8);
            }
            catch { }
        }
    }
}
