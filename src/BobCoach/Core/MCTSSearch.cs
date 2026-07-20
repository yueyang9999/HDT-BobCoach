using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace BobCoach.Engine
{
    /// <summary>
    /// 有限蒙特卡洛树搜索 (MCTS) — 用于1-2回合前瞻决策。
    ///
    /// 参数:
    ///   深度: 2回合 (每个回合=一次动作)
    ///   宽度: top-3动作
    ///   模拟: 20次/节点 (叶节点用V(s)估值)
    ///   时间预算: 5-10ms
    ///
    /// 当MCTS能在预算内完成时，替代贪心二步前瞻。
    /// </summary>
    public class MCTSSearch
    {
        private Simulator _sim;
        private ActionEnumerator _enumerator;
        private FeatureExtractor _fe;
        private ValueFunction _vf;

        private const int MAX_DEPTH = 2;
        private const int TOP_K = 3;
        private const int NUM_ROLLOUTS = 20;
        private const int TIME_BUDGET_MS = 8;
        private const double DISCOUNT = 0.85;

        // 搜索缓存: 避免对同一状态重复评估
        private Dictionary<string, float> _evalCache = new Dictionary<string, float>();

        public MCTSSearch(Simulator sim, ActionEnumerator enumerator, FeatureExtractor fe, ValueFunction vf)
        {
            _sim = sim;
            _enumerator = enumerator;
            _fe = fe;
            _vf = vf;
        }

        public class MCTSResult
        {
            public GameAction BestAction;
            public GameAction SecondAction;   // 第2步建议 (供HintLine显示)
            public double EstimatedValue;
            public int NodesExplored;
            public long ElapsedMs;
            public bool CompletedInBudget;
        }

        /// <summary>
        /// 执行MCTS搜索。成功返回结果，超时或搜索空间太小则返回null。
        /// </summary>
        public MCTSResult Search(GameState state)
        {
            if (state == null || !state.GameActive) return null;
            if (!CardPoolSampler.IsInitialized) return null; // 需要真实卡池采样

            var sw = Stopwatch.StartNew();
            _evalCache.Clear();

            var actions = _enumerator.Enumerate(state, state.HeroCardId);
            if (actions.Count <= 1) return null;

            // ── Step 1: V(s)贪心评估所有动作, 选出Top-K ──
            var scored = new List<(GameAction action, GameState nextState, float[] features, float value)>();
            foreach (var a in actions)
            {
                var ns = _sim.Simulate(state, a);
                var f = _fe.Extract(ns);
                float v = _vf.Evaluate(f);
                scored.Add((a, ns, f, v));
            }
            scored.Sort((a, b) => b.value.CompareTo(a.value));

            int k = Math.Min(TOP_K, scored.Count);
            if (k == 0) return null;

            // ── Step 2: 对Top-K动作, 每个做深度2的有限展开 ──
            double bestTotalValue = double.NegativeInfinity;
            GameAction bestAction = null;
            GameAction bestSecondAction = null;
            int nodesExplored = 0;

            for (int i = 0; i < k; i++)
            {
                if (sw.ElapsedMilliseconds > TIME_BUDGET_MS) break;

                var (act1, state1, _, val1) = scored[i];
                double totalValue = val1;

                // 检查step1后是否有足够金币继续
                if (state1.Gold < 1 || state1.BoardMinions.Count >= 7)
                {
                    // 无法进行第2步, 直接用第1步的估值
                    if (totalValue > bestTotalValue)
                    {
                        bestTotalValue = totalValue;
                        bestAction = act1;
                        bestSecondAction = null;
                    }
                    nodesExplored++;
                    continue;
                }

                // 枚举第2步动作
                var actions2 = _enumerator.Enumerate(state1, state1.HeroCardId);
                if (actions2.Count == 0)
                {
                    if (totalValue > bestTotalValue)
                    {
                        bestTotalValue = totalValue;
                        bestAction = act1;
                        bestSecondAction = null;
                    }
                    nodesExplored++;
                    continue;
                }

                // 对第2步也做贪心Top-3
                var scored2 = new List<(GameAction action, GameState nextState, float value)>();
                foreach (var a2 in actions2)
                {
                    if (sw.ElapsedMilliseconds > TIME_BUDGET_MS) break;
                    var ns2 = _sim.Simulate(state1, a2);
                    var f2 = _fe.Extract(ns2);
                    float v2 = _vf.Evaluate(f2);
                    scored2.Add((a2, ns2, v2));
                    nodesExplored++;
                }

                if (scored2.Count == 0) continue;

                scored2.Sort((a, b) => b.value.CompareTo(a.value));

                // Rollout: 对每个第2步候选做多次随机模拟
                int topK2 = Math.Min(3, scored2.Count);
                double maxStep2Value = double.NegativeInfinity;
                GameAction bestA2 = null;

                for (int j = 0; j < topK2; j++)
                {
                    var (act2, state2, val2) = scored2[j];
                    double rolloutValue = val2;

                    // 随机rollout: 从state2再随机走0-1步, 用V(s)估值终点
                    for (int r = 0; r < Math.Min(NUM_ROLLOUTS / topK2, 10); r++)
                    {
                        var actions3 = _enumerator.Enumerate(state2, state2.HeroCardId);
                        if (actions3.Count == 0) break;

                        var a3 = actions3[_rng.Next(actions3.Count)];
                        var ns3 = _sim.Simulate(state2, a3);
                        var f3 = _fe.Extract(ns3);
                        float v3 = _vf.Evaluate(f3);
                        rolloutValue = (rolloutValue + v3 * DISCOUNT) / 2.0;
                        nodesExplored++;
                    }

                    if (rolloutValue > maxStep2Value)
                    {
                        maxStep2Value = rolloutValue;
                        bestA2 = act2;
                    }
                }

                // 两步总价值 = val1 + DISCOUNT * maxStep2Value
                totalValue = val1 + DISCOUNT * maxStep2Value;

                if (totalValue > bestTotalValue)
                {
                    bestTotalValue = totalValue;
                    bestAction = act1;
                    bestSecondAction = bestA2;
                }
            }

            sw.Stop();

            return new MCTSResult
            {
                BestAction = bestAction,
                SecondAction = bestSecondAction,
                EstimatedValue = bestTotalValue,
                NodesExplored = nodesExplored,
                ElapsedMs = sw.ElapsedMilliseconds,
                CompletedInBudget = sw.ElapsedMilliseconds <= TIME_BUDGET_MS
            };
        }

        private static Random _rng = new Random();
    }
}
