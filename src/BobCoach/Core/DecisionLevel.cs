using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace BobCoach.Engine
{
    /// <summary>
    /// 决策三级视觉体系：Critical(特级) / Major(一级) / Minor(二级)
    /// </summary>
    public enum DecisionLevel
    {
        /// <summary>特级：改变全局走势，必须强感知（升本、核心卡、生存危机）</summary>
        Critical,

        /// <summary>一级：明显正向操作（战力提升、凑对子、关键饰品）</summary>
        Major,

        /// <summary>二级：信息补充与过渡参考（次选卡牌、微调提示）</summary>
        Minor
    }

    public static class DecisionVisualRules
    {
        public static double GetBorderThickness(DecisionLevel lvl)
        {
            switch (lvl)
            {
                case DecisionLevel.Critical: return 3.5;
                case DecisionLevel.Major: return 2.0;
                case DecisionLevel.Minor: return 0.0;
                default: return 0.0;
            }
        }

        /// <summary>
        /// 根据决策等级返回对应的发光效果（从 HearthstoneStyles.xaml 资源字典加载）。
        /// </summary>
        public static DropShadowEffect GetGlow(DecisionLevel lvl, ResourceDictionary res)
        {
            if (res == null) return null;
            switch (lvl)
            {
                case DecisionLevel.Critical: return res["GlowCritical"] as DropShadowEffect;
                case DecisionLevel.Major: return res["GlowMajor"] as DropShadowEffect;
                default: return null;
            }
        }

        /// <summary>
        /// 根据决策等级返回对应的画刷颜色。
        /// </summary>
        public static Brush GetAccentBrush(DecisionLevel lvl, ResourceDictionary res)
        {
            if (res == null) return Brushes.LimeGreen;
            switch (lvl)
            {
                case DecisionLevel.Critical: return res["BrushCritical"] as Brush ?? Brushes.Gold;
                case DecisionLevel.Major: return res["BrushMajorPos"] as Brush ?? Brushes.LimeGreen;
                case DecisionLevel.Minor: return res["BrushMinor"] as Brush ?? Brushes.LightBlue;
                default: return Brushes.White;
            }
        }
    }
}
