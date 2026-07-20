using System.Collections.Generic;
using System.Linq;

namespace BobCoach.Engine
{
    /// <summary>
    /// 视觉预算：防止信息过载，硬限制各类元素的同屏数量。
    /// 超出上限的元素自动降级（边框→光束→角标→仅悬停）。
    /// </summary>
    public static class VisualBudget
    {
        public const int MAX_GLOW_BORDERS = 1;   // 同屏最多1个闭合发光边框（仅三连/核心首次）
        public const int MAX_BOTTOM_BEAMS = 3;   // 底部光束最多3条
        public const int MAX_GROUND_POOLS = 3;   // 地灯最多3个
        public const int MAX_ARROWS = 2;         // 方向箭头最多2个（买/卖各1）
        public const int MAX_CORNER_DOTS = 4;    // 角标最多4个
    }

    /// <summary>
    /// 视觉元素类型枚举（Render方法路由用）
    /// </summary>
    public enum VisualElementType
    {
        GlowBorder,    // 闭合边框（最高优先级，极少用）
        BottomBeam,    // 底部光束（日常推荐）
        GroundPool,    // 地灯（氛围强化）
        DirectionArrow, // 方向箭头（买↓卖↑）
        CornerDot,     // 角标（对子/三连等）
    }

    /// <summary>
    /// 去文字化视觉计划：决策引擎输出 → VisualPlan → Renderer(5图元)
    /// Renderer 不感知决策类型，只认识图元。
    /// </summary>
    public class VisualPlan
    {
        public const int MAX_PULSING = 1;   // 同屏最多1个脉冲动画
        public const int MAX_BORDERS = 1;   // V2: 降为1，闭合边框仅极重要时使用
        public const int MAX_MARKERS = 3;   // 同屏最多3个标记(含卖牌+灯泡)

        public StatusInfo Status = new StatusInfo();
        public UpgradeHint UpgradeHint;
        public string RecommendedActionType = "";  // 算法推荐动作类型(用于刷新按钮渲染)
        public List<ShopMarker> ShopMarkers = new List<ShopMarker>();
        public List<SellMarker> SellMarkers = new List<SellMarker>();
        public HandMarker HandMarker;  // 每屏最多1个"打"标记
        public FreezeHint FreezeHint;  // 冻结推荐提示
        public List<TrinketHint> TrinketHints = new List<TrinketHint>();
        public List<TargetHint> TargetHints = new List<TargetHint>();  // 每屏最多1个

        /// <summary>硬上限截断：按Score排序，超限丢弃。V2: 应用视觉预算</summary>
        public void EnforceLimits()
        {
            // 商店标记：最多1个GlowBorder(Critical)+2个Beam(Major/Minor)
            var sorted = ShopMarkers.OrderByDescending(m => m.Score).ToList();
            var capped = new List<ShopMarker>();
            int glowCount = 0, beamCount = 0;
            foreach (var m in sorted)
            {
                if (m.Level == DecisionLevel.Critical && glowCount < VisualBudget.MAX_GLOW_BORDERS)
                {
                    m.Pulse = true;
                    glowCount++;
                    capped.Add(m);
                }
                else if (beamCount < VisualBudget.MAX_BOTTOM_BEAMS)
                {
                    m.Pulse = false;
                    beamCount++;
                    capped.Add(m);
                }
            }
            ShopMarkers = capped;

            // 卖牌标记：最多1个
            SellMarkers = SellMarkers.Take(VisualBudget.MAX_ARROWS).ToList();
        }
    }

    /// <summary>商店卡牌视觉标记</summary>
    public class ShopMarker
    {
        public int Index;              // 商店卡牌索引(0-based, 列表序)
        public int ShopPosition;       // 游戏7槽ZonePosition (0-6, 固定网格)
        public int EntityId;           // 游戏实体ID, 用于覆盖层位置跟踪
        public DecisionLevel Level;    // Critical/Major/Minor
        public bool Pulse;             // 是否呼吸动画
        public double Score;           // 用于排序截断 (0-1)
        public string CardName = "";   // 仅悬停Tooltip，不常驻
        public string Tribe = "";
        public int Tier;
        public string Reason = "";     // 评分原因
        public bool IsSpell;           // 酒馆法术
        public bool IsTriple;          // 三连机会 (金色光)
        public bool IsHeroSynergy;     // 英雄种族匹配
        public bool IsGrowth;          // 永久成长卡
        public CardPurpose Purpose;    // 对局用途
        public QualityTier Quality;    // S/A/B质量
        public float PickRate;         // 抓取率
    }

    /// <summary>升本建议视觉标记</summary>
    public class UpgradeHint
    {
        public DecisionLevel Level;
        public string Reason = "";     // 悬停Tooltip
        public int Cost;
        public int CurrentTier;
    }

    /// <summary>卖牌视觉标记</summary>
    public class SellMarker
    {
        public int BoardIndex;         // 战场索引(初始)
        public string CardId = "";     // 卡牌ID(拖动后仍可匹配)
    }

    /// <summary>冻结提示: 推荐冻结时显示</summary>
    public class FreezeHint
    {
        public bool Active;            // 是否激活
        public string Reason = "";     // ≤5字原因
        public bool Urgent;            // 紧急(好牌多缺钱→冻结)
    }

    /// <summary>手牌标记: 仅"打"一种, 每屏最多1个, 打完出下一个</summary>
    public class HandMarker
    {
        public int Index;
        public string CardName = "";
        public string Reason = "";     // ≤5字
    }

    /// <summary>目标提示: 用法术/技能时指向的场上随从</summary>
    public class TargetHint
    {
        public int BoardIndex;           // 目标随从位置
        public string Reason = "";       // 简短的为什么(≤7字)
        public DecisionLevel Level = DecisionLevel.Minor;
    }

    /// <summary>状态条数据（图标化）</summary>
    public class StatusInfo
    {
        public int Health;
        public int MaxHealth;
        public int Gold;
        public int MaxGold;
        public bool ShowLevelUpDot;    // ● 升本建议宝石
        public bool ShowBuyDot;        // ● 购买建议宝石
        public bool IsDesperate;       // 绝望差距：需要tech pivot
        public string Phase = "E";     // E/M/L
        public string Pace = "";       // Sprint/Cruise/Conserve/AllIn
        public string CompDir = "";    // 流派方向 (如: "野兽")
        public string LockIcon = "";   // "H"=Hard锁 / "S"=Soft锁 / ""=未锁
        public string ActionSeq = "";     // 动作序列 "打铜须→技能点金"
        public string HintLine = "";      // 动作序列 (浅黄, 行3)
        public string UpgradeLine = "";   // 升级提示 (绿色, 行4)
        public bool ShowFirstBuyFree = false;  // 畸变首购免费状态栏提示
        public string CombatLine = "";    // 战斗预测 (橙色, 行5)
        public string PickLine = "";      // 饰品/发现选择 (金/天蓝, 行6)
    }

    /// <summary>饰品推荐标记</summary>
    public class TrinketHint
    {
        public int Index;                // 饰品选项索引
        public string Name = "";         // 饰品中文名
        public string Reason = "";       // 推荐原因
        public double Score;             // 评分
        public bool IsTopPick;           // 是否首推
        public bool IsUnrated;           // P1.5: 无可靠评分, 面板标"未知"
    }

    /// <summary>通用阵容锁定数据</summary>
    public class CompGuidance
    {
        public string LockIcon = "";
    }
}
