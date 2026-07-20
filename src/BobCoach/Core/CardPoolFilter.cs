using System.Collections.Generic;
using System.Linq;

namespace BobCoach.Engine
{
    /// <summary>
    /// 卡池过滤规则: 法术种族限制 + 当局可用种族过滤。
    /// 部分法术仅在特定种族存在于当局时加入卡池。
    /// </summary>
    public static class CardPoolFilter
    {
        /// <summary>法术CardId → 要求当局包含的种族</summary>
        private static readonly Dictionary<string, string> SpellRequiredTribe = new Dictionary<string, string>
        {
            // 悬赏令系列 — 仅海盗局
            { "BG33_815", "PIRATE" },
            { "BG33_811", "PIRATE" },
            { "BG33_812", "PIRATE" },
            { "BG33_813", "PIRATE" },
            { "BG33_814", "PIRATE" },
            { "BG33_821", "PIRATE" },
            { "BG33_810", "PIRATE" },
            { "BG28_884", "PIRATE" },

            // 元素专属法术
            { "BG35_911", "ELEMENTAL" },   // 奥术吸收
            { "BG35_952", "ELEMENTAL" },   // 背靠背 (元素流核心)
            { "BG28_888", "ELEMENTAL" },   // 累叠雪崩
            { "BG28_169", "ELEMENTAL" },   // 燃焰
            { "BG28_573", "ELEMENTAL" },   // 优势压制
            { "BG28_827", "ELEMENTAL" },   // 快速浏览(元素形态)
            { "BG34_888", "ELEMENTAL" },   // 乘借东风

            // 鱼人专属法术
            { "BG28_500", "MURLOC" },      // 克隆螺号
            { "BG28_501", "MURLOC" },      // 深水族群

            // 纳迦专属法术
            { "BG28_502", "NAGA" },        // 恶鳞套餐
            { "BG28_503", "NAGA" },        // 变换之潮
            { "BG28_504", "NAGA" },        // 女王的命令

            // 恶魔专属法术
            { "BG28_505", "DEMON" },       // 腐化糕点

            // 亡灵专属法术
            { "BG34_888", "UNDEAD" },      // 惊扰墓穴
            { "BG28_506", "UNDEAD" },      // 宰割

            // 野猪人专属法术
            { "BG28_507", "QUILBOAR" },    // 查抄宝石
        };

        /// <summary>
        /// 判断某张卡是否适用于当前对局。
        /// cardId: 卡牌ID
        /// availableTribes: 当局可用种族集合(如 {"PIRATE", "ELEMENTAL", ...})
        /// tribe: 该卡牌的种族
        /// isSpell: 是否为法术
        /// </summary>
        public static bool IsCardInPool(string cardId, HashSet<string> availableTribes,
            string tribe, bool isSpell)
        {
            if (string.IsNullOrEmpty(cardId)) return false;

            // 法术: 检查种族限制
            if (isSpell && SpellRequiredTribe.ContainsKey(cardId))
            {
                string required = SpellRequiredTribe[cardId];
                if (availableTribes == null || availableTribes.Count == 0) return true; // 未知时不过滤
                return availableTribes.Contains(required);
            }

            // 随从: 检查种族是否在当局可用 (中立ALL始终可用)
            if (!isSpell && availableTribes != null && availableTribes.Count > 0
                && !string.IsNullOrEmpty(tribe) && tribe != "ALL")
            {
                return tribe.Split(',').Any(raw =>
                {
                    string value = raw.Trim();
                    return value == "ALL" || value == "中立" || value == "全部"
                        || availableTribes.Contains(value);
                });
            }

            return true;
        }

        /// <summary>
        /// 对全量卡牌列表按当局可用种族过滤，返回应纳入卡池的卡牌。
        /// allCards: CardId → (tier, tribe, isSpell)
        /// availableTribes: 当局可用种族
        /// </summary>
        public static Dictionary<string, int> FilterForPool(
            Dictionary<string, (int tier, string tribe, bool isSpell)> allCards,
            HashSet<string> availableTribes)
        {
            var result = new Dictionary<string, int>();
            if (allCards == null) return result;

            foreach (var kv in allCards)
            {
                if (kv.Value.tier < 1 || kv.Value.tier > 6) continue;
                if (IsCardInPool(kv.Key, availableTribes, kv.Value.tribe, kv.Value.isSpell))
                    result[kv.Key] = kv.Value.tier;
            }
            return result;
        }
    }
}
