using System;
using System.IO;
using System.Web.Script.Serialization;

namespace BobCoach.Engine
{
    /// <summary>
    /// UI布局偏移配置，支持持久化到 %APPDATA%/bob-coach/ui_config.json
    /// 校准模式(F10)通过方向键调整偏移后按S保存
    /// </summary>
    public class LayoutConfig
    {
        public const double DefaultCalibrationWidth = 1920.0;
        public const double DefaultCalibrationHeight = 1080.0;
        private const int LegacyShopOffsetVersion = 9;
        private const double LegacyDefaultShopOffsetX = -64.5;

        // 默认以游戏客户区中轴为商店牌组中轴。F10 校准只保存用户设备上的实际差异，
        // 不把单台设备的负向校准固化为所有用户的默认值。
        public const double DefaultShopOffsetX = 0;

        public int Version { get; set; } = 10;            // v10: migrate the former default half-card shop offset
        public double CalibrationWidth { get; set; } = DefaultCalibrationWidth;
        public double CalibrationHeight { get; set; } = DefaultCalibrationHeight;
        public bool BoardLeftToRight { get; set; } = true; // false=索引0在最右(默认)
        public double ShopOffsetX { get; set; } = DefaultShopOffsetX;   // 招募区中轴微调, 用户校准覆盖
        public double ShopOffsetY { get; set; } = 0;
        public double BoardOffsetX { get; set; } = 0;
        public double BoardOffsetY { get; set; } = 0;
        public double TavernOffsetX { get; set; } = 0;
        public double TavernOffsetY { get; set; } = 0;
        public double RefreshOffsetX { get; set; } = 0;
        public double RefreshOffsetY { get; set; } = 0;
        public double FreezeOffsetX { get; set; } = 0;
        public double FreezeOffsetY { get; set; } = 0;
        public double HeroPowerOffsetX { get; set; } = 0;
        public double HeroPowerOffsetY { get; set; } = 0;
        public double HandOffsetX { get; set; } = 0;
        public double HandOffsetY { get; set; } = 0;
        public double HandCardWidthPct { get; set; } = 7.5;     // 手牌卡宽(%客户区宽)
        public double HandGap { get; set; } = 8.0;               // 手牌平铺间隙(px)
        public double HandTopYRatio { get; set; } = 0.85;        // 中心卡(角度0)中心Y(距顶比例)
        public double HandPivotYRatio { get; set; } = 1.30;      // 枢轴Y(占高比, >1.0=屏幕下方, 圆心)
        public double HandMaxTotalAngle { get; set; } = 70.0;    // 满手牌(10张)总扇形角度

        // 选择面板(饰品/发现推荐)位置与缩放 — F10校准模式 [9] 调节。基准像素(1920×1080), ScaleX/Y 自适应任意分辨率。
        public double PanelOffsetX { get; set; } = 15;    // 面板左上角X(基准px, 屏幕左侧避开中央三选一交互区)
        public double PanelOffsetY { get; set; } = 200;   // 面板左上角Y(基准px, 状态栏下方)
        public double PanelScale { get; set; } = 1.0;     // 面板整体缩放(字号+宽度), 1.0=基准

        // 商店/战场卡牌尺寸配置（基础值; 按等级分档见下方数组）
        public double ShopCardWidthPct { get; set; } = 7.5;      // 商店/战场卡宽(%客户区宽)
        public double ShopCardHeightPct { get; set; } = 17.0;    // 商店/战场卡高(%客户区高)
        public double ShopCardGap { get; set; } = 8.0;           // 商店/战场卡牌间隙(px)
        public double ShopCardOffsetX { get; set; } = 0;         // 商店卡牌组水平偏移(px), 正值=右移
        public double ShopCardOffsetY { get; set; } = 0;         // 商店卡牌组竖直偏移(px), 正值=下移
        public double ShopLabelOffsetX { get; set; } = 0;        // 商店标签水平微调(独立于卡位), 正值=右移
        // 按等级分档: 索引0=T1, 索引5=T6。非0值覆盖基础值
        public double[] TierCardWidthPct { get; set; } = new double[7];
        public double[] TierCardGap { get; set; } = new double[7];
        public double[] TierCardOffsetX { get; set; } = new double[7];
        public double[] TierLabelOffsetX { get; set; } = new double[7];

        private static readonly string ConfigPath = BobCoachDataPaths.GetPath("ui_config.json");

        public static int TierIndex(int tier) => Math.Max(0, Math.Min(5, tier - 1));

        private static double GetTierValue(double[] values, int tier)
        {
            int idx = TierIndex(tier);
            return values != null && idx < values.Length ? values[idx] : 0;
        }

        public double GetCardWidthPct(int tier)
        {
            double value = GetTierValue(TierCardWidthPct, tier);
            return value > 0 ? value : ShopCardWidthPct;
        }

        public double GetCardGap(int tier)
        {
            double value = GetTierValue(TierCardGap, tier);
            return value != 0 ? value : ShopCardGap;
        }

        public double GetCardOffsetX(int tier)
        {
            double value = GetTierValue(TierCardOffsetX, tier);
            return value != 0 ? value : ShopCardOffsetX;
        }

        public double GetLabelOffsetX(int tier)
        {
            double value = GetTierValue(TierLabelOffsetX, tier);
            return value != 0 ? value : ShopLabelOffsetX;
        }

        public void SetCalibrationSize(double width, double height)
        {
            if (width > 100 && height > 100)
            {
                double oldWidth = CalibrationWidth > 100 ? CalibrationWidth : DefaultCalibrationWidth;
                double oldHeight = CalibrationHeight > 100 ? CalibrationHeight : DefaultCalibrationHeight;
                if (Math.Abs(width - oldWidth) > 0.1 || Math.Abs(height - oldHeight) > 0.1)
                {
                    RebasePixelValues(width / oldWidth, height / oldHeight);
                }
                CalibrationWidth = width;
                CalibrationHeight = height;
            }
        }

        private void RebasePixelValues(double scaleX, double scaleY)
        {
            ShopOffsetX *= scaleX; ShopOffsetY *= scaleY;
            BoardOffsetX *= scaleX; BoardOffsetY *= scaleY;
            TavernOffsetX *= scaleX; TavernOffsetY *= scaleY;
            RefreshOffsetX *= scaleX; RefreshOffsetY *= scaleY;
            FreezeOffsetX *= scaleX; FreezeOffsetY *= scaleY;
            HeroPowerOffsetX *= scaleX; HeroPowerOffsetY *= scaleY;
            HandOffsetX *= scaleX; HandOffsetY *= scaleY;
            PanelOffsetX *= scaleX; PanelOffsetY *= scaleY; // PanelScale 是比例, 不随分辨率 rebase
            HandGap *= scaleX;
            ShopCardGap *= scaleX;
            ShopCardOffsetX *= scaleX;
            ShopCardOffsetY *= scaleY;
            ShopLabelOffsetX *= scaleX;
            ScaleArray(TierCardGap, scaleX);
            ScaleArray(TierCardOffsetX, scaleX);
            ScaleArray(TierLabelOffsetX, scaleX);
        }

        private static void ScaleArray(double[] values, double scale)
        {
            if (values == null) return;
            for (int i = 0; i < values.Length; i++)
                values[i] *= scale;
        }

        public double ScaleX(double pixels, double currentWidth)
        {
            double baseWidth = CalibrationWidth > 100 ? CalibrationWidth : DefaultCalibrationWidth;
            double targetWidth = currentWidth > 100 ? currentWidth : DefaultCalibrationWidth;
            return pixels * targetWidth / baseWidth;
        }

        public double ScaleY(double pixels, double currentHeight)
        {
            double baseHeight = CalibrationHeight > 100 ? CalibrationHeight : DefaultCalibrationHeight;
            double targetHeight = currentHeight > 100 ? currentHeight : DefaultCalibrationHeight;
            return pixels * targetHeight / baseHeight;
        }

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var json = new JavaScriptSerializer().Serialize(this);
                File.WriteAllText(ConfigPath, json);
            }
            catch { }
        }

        public static LayoutConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    var loaded = new JavaScriptSerializer().Deserialize<LayoutConfig>(json);
                    return NormalizeLoadedConfig(loaded);
                }
            }
            catch { }
            return new LayoutConfig();
        }

        internal static LayoutConfig NormalizeLoadedConfig(LayoutConfig loaded)
        {
            var current = new LayoutConfig();
            if (loaded == null) return current;
            if (loaded.Version == current.Version) return loaded;

            if (loaded.Version == LegacyShopOffsetVersion)
            {
                double calibrationWidth = loaded.CalibrationWidth > 100
                    ? loaded.CalibrationWidth
                    : DefaultCalibrationWidth;
                double legacyDefault = LegacyDefaultShopOffsetX
                    * calibrationWidth / DefaultCalibrationWidth;
                if (Math.Abs(loaded.ShopOffsetX - legacyDefault) <= 0.5)
                    loaded.ShopOffsetX = DefaultShopOffsetX;
                loaded.Version = current.Version;
                return loaded;
            }

            // Unknown older schemas may contain incompatible raw-pixel offsets.
            return current;
        }
    }
}
