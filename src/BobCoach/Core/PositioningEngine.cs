using System;
using System.Collections.Generic;
using System.Linq;

namespace BobCoach.Engine
{
    public class PositionRecommendation
    {
        public List<int> RecommendedOrder;  // 新的场面位置排列 (按旧index)
        public string Reason = "";
    }

    /// <summary>
    /// 站位优化引擎。基于 BG 标准站位规则推荐最优排列：
    ///   1. 嘲讽放最右侧（吸收自由攻击，保护后排）
    ///   2. 圣盾放左侧（高效吸收首轮伤害）
    ///   3. 高价值辅助(铜须/瑞文)放最左侧（防顺劈/猎妈人）
    ///   4. 防顺劈：不将重要随从相邻放置
    /// </summary>
    public class PositioningEngine
    {
        // 高价值辅助卡（铜须/瑞文/达卡莱等 — 不靠攻击力吃饭）
        private static readonly HashSet<string> HighValueSupport = new HashSet<string>
        {
            "BGS_041",     // 奥术守护者卡雷苟斯 (虽然攻击高但需要保护)
            "BG_ICC_026",  // 布莱恩·铜须
            "BG25_354",    // 提图斯·瑞文戴尔
            "BG26_ICC_901",// 达卡莱附魔师
            "TB_BaconUps_135", // 瑞文戴尔(旧)
            "BG27_518",    // 铜须(变体)
            "BG35_883",    // 巴琳达·斯通赫尔斯
        };

        // 顺劈/狂战斧卡（会打相邻目标）
        private static readonly HashSet<string> CleaveCards = new HashSet<string>
        {
            "BG20_201",    // 巨狼鹦鹉 (狂战相关)
            // 狂战斧系列通过名/文本检测，此处通过MinionData无法识别；
            // 实际检测由 CombatSimulator._detectCleave 完成
        };

        /// <summary>
        /// 推荐最优站位排列。返回新的 index 顺序。
        /// </summary>
        public PositionRecommendation Optimize(List<MinionData> board)
        {
            var result = new PositionRecommendation();
            if (board == null || board.Count < 2)
            {
                result.RecommendedOrder = board != null
                    ? Enumerable.Range(0, board.Count).ToList()
                    : new List<int>();
                result.Reason = "随从不足，无需调整";
                return result;
            }

            int n = board.Count;
            var indexed = board.Select((m, i) => new { Minion = m, OriginalIndex = i }).ToList();

            // 按优先级分组
            var supports = indexed.Where(x => IsHighValueSupport(x.Minion)).ToList();
            var taunts = indexed.Where(x => x.Minion.Taunt && !IsHighValueSupport(x.Minion)).ToList();
            var divineShields = indexed.Where(x => x.Minion.DivineShield
                && !x.Minion.Taunt && !IsHighValueSupport(x.Minion)).ToList();
            var others = indexed.Where(x => !x.Minion.Taunt && !x.Minion.DivineShield
                && !IsHighValueSupport(x.Minion)).ToList();

            // 构建新排列: [辅助] + [圣盾] + [普通] + [嘲讽]
            var newOrder = new List<int>();

            // 1. 高价值辅助放最左侧
            foreach (var s in supports)
                newOrder.Add(s.OriginalIndex);

            // 2. 圣盾随从（吸收早期攻击）
            divineShields.Sort((a, b) => b.Minion.Attack.CompareTo(a.Minion.Attack)); // 高攻圣盾优先
            foreach (var ds in divineShields)
            {
                // 防顺劈：如果前一个也是高价值卡，插入一个普通卡
                if (newOrder.Count > 0 && newOrder.Count < n - 1)
                {
                    var prev = indexed[newOrder[newOrder.Count - 1]];
                    if (IsHighValueSupport(prev.Minion) && others.Count > 0)
                    {
                        newOrder.Add(others[0].OriginalIndex);
                        others.RemoveAt(0);
                    }
                }
                newOrder.Add(ds.OriginalIndex);
            }

            // 3. 普通随从
            // 按攻击力降序（高攻在前吸收伤害/快速输出）
            others.Sort((a, b) => b.Minion.Attack.CompareTo(a.Minion.Attack));
            foreach (var o in others)
                newOrder.Add(o.OriginalIndex);

            // 4. 嘲讽放最右侧（BG标准：保护后排）
            taunts.Sort((a, b) => b.Minion.Health.CompareTo(a.Minion.Health)); // 高血嘲讽靠右
            foreach (var t in taunts)
                newOrder.Add(t.OriginalIndex);

            // 确保所有随从都被排列
            var allIndices = indexed.Select(x => x.OriginalIndex).ToHashSet();
            var placedIndices = newOrder.ToHashSet();
            foreach (var idx in allIndices)
                if (!placedIndices.Contains(idx))
                    newOrder.Add(idx);

            result.RecommendedOrder = newOrder;
            result.Reason = BuildReason(supports.Count, taunts.Count, divineShields.Count);
            return result;
        }

        /// <summary>
        /// 检查当前站位是否需要调整。返回 null 表示无需调整。
        /// </summary>
        public PositionRecommendation CheckAndOptimize(List<MinionData> board)
        {
            if (board == null || board.Count < 2)
                return null;

            var opt = Optimize(board);
            // 检查当前站位是否与推荐一致
            bool needsChange = false;
            for (int i = 0; i < board.Count; i++)
            {
                if (opt.RecommendedOrder[i] != i)
                {
                    needsChange = true;
                    break;
                }
            }

            return needsChange ? opt : null;
        }

        private bool IsHighValueSupport(MinionData m)
        {
            if (string.IsNullOrEmpty(m.CardId)) return false;
            // 已知高价值辅助卡
            if (HighValueSupport.Contains(m.CardId)) return true;
            // 低攻击力的非嘲讽卡也可能是辅助（攻击力<3 且 非嘲讽）
            if (m.Attack <= 2 && !m.Taunt && m.Tier >= 4)
                return true;
            return false;
        }

        private string BuildReason(int supportCount, int divineShieldCount, int tauntCount)
        {
            var parts = new List<string>();
            if (supportCount > 0) parts.Add("辅助左移");
            if (divineShieldCount > 0) parts.Add("圣盾前置");
            if (tauntCount > 0) parts.Add("嘲讽右置");
            return parts.Count > 0 ? string.Join("+", parts) : "标准排列";
        }
    }
}
