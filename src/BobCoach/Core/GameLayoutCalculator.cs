using System;
using System.Collections.Generic;
using System.IO;

namespace BobCoach.Engine
{
    /// <summary>
    /// 基于炉石物理像素客户区的区域定位计算器。
    /// 使用LayoutConfig可配置偏移，按S保存。
    /// </summary>
    public class GameLayoutCalculator
    {
        private SafeNativeMethods.RECT _clientRect;
        private LayoutConfig _config;
        private double _dpiScale = 1.0;

        // 布局缓存: count=1~7 预计算所有商店/战场卡牌Rect，购买后O(1)查表
        private Dictionary<int, LayoutRect[]> _shopCache = new Dictionary<int, LayoutRect[]>();
        private Dictionary<int, LayoutRect[]> _boardCache = new Dictionary<int, LayoutRect[]>();
        private bool _cacheDirty = true;

        public int ClientWidth => _clientRect.Width;
        public int ClientHeight => _clientRect.Height;
        public double DpiScale => _dpiScale;
        public LayoutConfig Config => _config;   // 供渲染层读取面板位置/缩放(PanelOffsetX/Y/Scale)

        public GameLayoutCalculator() : this(LayoutConfig.Load()) { }

        public GameLayoutCalculator(LayoutConfig config)
        {
            _config = config ?? new LayoutConfig();
            SetTier(1); // 初始化默认T1, 防止首帧DispatchRender时_activeCardWidthPct=0导致标签重叠
            Refresh();
        }

        /// <summary>
        /// 获取物理像素客户区。校准/实战后续通过 RefreshWithSize 统一到 WPF DIPs。
        /// </summary>
        public void Refresh()
        {
            _dpiScale = 1.0;

            SafeNativeMethods.RECT? client = null;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                client = SafeNativeMethods.GetHearthstoneClientRect();
                if (client.HasValue && client.Value.Width > 100) break;
                if (attempt < 4) System.Threading.Thread.Sleep(100);
            }
            if (client.HasValue && client.Value.Width > 100)
            {
                _clientRect = client.Value;
            }
            else
            {
                _clientRect = new SafeNativeMethods.RECT { Left = 0, Top = 0, Right = 2560, Bottom = 1600 };
            }
            _cacheDirty = true;
            PreWarmCache();
        }

        /// <summary>用 WPF DIPs 覆盖客户区尺寸，确保与 OverlayCanvas 坐标系一致</summary>
        public void RefreshWithSize(double width, double height)
        {
            if (width > 100 && height > 100)
            {
                _clientRect = new SafeNativeMethods.RECT { Left = 0, Top = 0, Right = (int)width, Bottom = (int)height };
                _dpiScale = 1.0;
            }
            else
            {
                Refresh();
            }
            _cacheDirty = true;
            PreWarmCache();
        }

        public double ScaleX(double pixelsAtBase)
        {
            double width = _clientRect.Width > 100 ? _clientRect.Width : LayoutConfig.DefaultCalibrationWidth;
            return pixelsAtBase * width / LayoutConfig.DefaultCalibrationWidth;
        }

        public double ScaleY(double pixelsAtBase)
        {
            double height = _clientRect.Height > 100 ? _clientRect.Height : LayoutConfig.DefaultCalibrationHeight;
            return pixelsAtBase * height / LayoutConfig.DefaultCalibrationHeight;
        }

        public double ScaleUniform(double pixelsAtBase)
        {
            return Math.Min(ScaleX(pixelsAtBase), ScaleY(pixelsAtBase));
        }

        private double OffsetX(double pixels)
        {
            return _config.ScaleX(pixels, _clientRect.Width);
        }

        private double OffsetY(double pixels)
        {
            return _config.ScaleY(pixels, _clientRect.Height);
        }

        /// <summary>预计算 count=1~7 的所有商店/战场卡牌Rect，购买后O(1)查表</summary>
        private void PreWarmCache()
        {
            if (!_cacheDirty) return;
            _shopCache.Clear();
            _boardCache.Clear();
            for (int n = 1; n <= 7; n++)
            {
                _shopCache[n] = ComputeShopCardRects(n);
                _boardCache[n] = ComputeBoardCardRects(n);
            }
            _cacheDirty = false;
        }

        private LayoutRect[] ComputeShopCardRects(int count)
        {
            var area = GetShopArea();
            // 固化标识: CardW基于酒馆等级的配置数据(SetTier时已赋值), 不依赖ShopMinions。
            // 无论商店内容(实卡/法术)是否存在, 同一酒馆等级的标签宽度始终一致。
            // 这样购买/刷新造成的短暂 ShopMinions 空窗不会导致 CardW=0 坐标坍缩。
            double top = area.Top + (area.Height - CardH) / 2 + OffsetY(_config.ShopCardOffsetY);
            double unit = CardW + CardGap;
            // 微调偏移: 纯像素补偿(校准值优先)。商店实际可见几张牌,
            // 标签就按几张牌的牌组居中。06141647 辛达苟萨实测证明
            // T1/T2 少卡商店并不是固定7槽视觉布局。
            double fineTuneX = OffsetX(_activeCardOffsetX != 0 ? _activeCardOffsetX : _config.ShopCardOffsetX);
            double totalWidth = count * CardW + (count - 1) * CardGap;
            double firstCardLeft = area.Left + (area.Width - totalWidth) / 2 + fineTuneX;
            var result = new LayoutRect[count];
            for (int i = 0; i < count; i++)
                result[i] = new LayoutRect(firstCardLeft + i * unit, top, CardW, CardH);
            return result;
        }

        private LayoutRect[] ComputeBoardCardRects(int count)
        {
            var area = GetBoardArea();
            var centers = CalculateCenters(count, area.Width, CardW, CardGap);
            var result = new LayoutRect[count];
            for (int i = 0; i < count; i++)
            {
                int idx = _config.BoardLeftToRight ? i : (count - 1 - i);
                double cx = area.Left + centers[idx];
                result[i] = new LayoutRect(cx - CardW / 2, area.Top + (area.Height - CardH) / 2, CardW, CardH);
            }
            return result;
        }

        // ══════════════════════════════════
        // 区域定义
        // ══════════════════════════════════

        /// <summary>商店区域: 底部, 与战场对称同尺寸, Y中心~60%</summary>
        public LayoutRect GetShopArea()
        {
            double w = _clientRect.Width * 0.56;
            double h = _clientRect.Height * 0.16;
            double x = (_clientRect.Width - w) / 2 + OffsetX(_config.ShopOffsetX);
            double y = _clientRect.Height * 0.60 + OffsetY(_config.ShopOffsetY);
            return new LayoutRect(x, y, w, h);
        }

        /// <summary>战场区域: 中下, 与商店对称同尺寸, Y中心~36%</summary>
        public LayoutRect GetBoardArea()
        {
            double w = _clientRect.Width * 0.56;
            double h = _clientRect.Height * 0.16;
            double x = (_clientRect.Width - w) / 2 + OffsetX(_config.BoardOffsetX);
            double y = _clientRect.Height * 0.36 + OffsetY(_config.BoardOffsetY);
            return new LayoutRect(x, y, w, h);
        }

        /// <summary>升本按钮区域: 左下角酒馆等级按钮, 红框</summary>
        public LayoutRect GetTavernButtonArea()
        {
            double w = _clientRect.Width * 0.055;
            double h = _clientRect.Height * 0.14;
            double x = _clientRect.Width * 0.115 + OffsetX(_config.TavernOffsetX);
            double y = _clientRect.Height * 0.60 + OffsetY(_config.TavernOffsetY);
            return new LayoutRect(x, y, w, h);
        }

        /// <summary>刷新按钮区域: 升本按钮右侧, 橙色框</summary>
        public LayoutRect GetRefreshButtonArea()
        {
            double w = _clientRect.Width * 0.055;
            double h = _clientRect.Height * 0.14;
            double x = _clientRect.Width * 0.175 + OffsetX(_config.RefreshOffsetX);
            double y = _clientRect.Height * 0.60 + OffsetY(_config.RefreshOffsetY);
            return new LayoutRect(x, y, w, h);
        }

        /// <summary>冻结按钮区域: 刷新按钮右侧约200px, 蓝色框</summary>
        public LayoutRect GetFreezeButtonArea()
        {
            double w = _clientRect.Width * 0.055;
            double h = _clientRect.Height * 0.14;
            double x = _clientRect.Width * 0.280 + OffsetX(_config.FreezeOffsetX);
            double y = _clientRect.Height * 0.60 + OffsetY(_config.FreezeOffsetY);
            return new LayoutRect(x, y, w, h);
        }

        /// <summary>英雄技能区域: Board下方, 尺寸为升本按钮150%</summary>
        public LayoutRect GetHeroPowerArea()
        {
            double w = _clientRect.Width * 0.08;
            double h = _clientRect.Height * 0.14;
            double x = _clientRect.Width * 0.10 + OffsetX(_config.HeroPowerOffsetX);
            double y = _clientRect.Height * 0.54 + OffsetY(_config.HeroPowerOffsetY);
            return new LayoutRect(x, y, w, h);
        }

        /// <summary>手牌区域: 底部居中, 青绿框 (Y中心与卡牌中心锚点对齐)</summary>
        public LayoutRect GetHandArea()
        {
            double w = _clientRect.Width * 0.48;
            double h = _clientRect.Height * 0.12;
            double x = (_clientRect.Width - w) / 2 + OffsetX(_config.HandOffsetX);
            double y = _clientRect.Height * _config.HandTopYRatio - h / 2 + OffsetY(_config.HandOffsetY);
            return new LayoutRect(x, y, w, h);
        }

        /// <summary>顶部状态条: 左上角（避免与HDT胜率条重叠）</summary>
        public LayoutPoint GetStatusStripPosition()
        {
            return new LayoutPoint(ScaleX(8), ScaleY(105));
        }

        // ══════════════════════════════════
        // 单张卡牌位置
        // ══════════════════════════════════

        public LayoutRect GetShopCardRect(int index, int totalCards)
        {
            var rects = GetShopCardRects(totalCards);
            if (index < 0 || index >= rects.Length) return new LayoutRect(0, 0, 100, 50);
            return rects[index];
        }

        public LayoutRect GetBoardCardRect(int index, int totalCount)
        {
            var rects = GetBoardCardRects(totalCount);
            if (index < 0 || index >= rects.Length) return new LayoutRect(0, 0, 100, 50);
            return rects[index];
        }

        /// <summary>N张商店/战场卡牌的基础尺寸 (从配置读取, 支持校准微调)</summary>
        public double CardW => _clientRect.Width * (_activeCardWidthPct / 100.0);
        public double CardH => _clientRect.Height * (_config.ShopCardHeightPct / 100.0);
        public double CardGap => OffsetX(_activeCardGap);
        public double ShopLabelOffsetX => OffsetX(_activeLabelOffsetX);
        private double _activeCardWidthPct;
        private double _activeCardGap;
        private double _activeLabelOffsetX;
        private double _activeCardOffsetX;
        private int _activeTier = 1;

        public void SetTier(int tier)
        {
            _activeTier = Math.Max(1, Math.Min(6, tier));
            _activeCardWidthPct = _config.GetCardWidthPct(_activeTier);
            _activeCardGap = _config.GetCardGap(_activeTier);
            _activeLabelOffsetX = _config.GetLabelOffsetX(_activeTier);
            _activeCardOffsetX = _config.GetCardOffsetX(_activeTier);
            _cacheDirty = true;
        }

        public LayoutRect[] GetShopCardRects(int count)
        {
            if (count < 1 || count > 7) return new LayoutRect[0];
            if (_cacheDirty) PreWarmCache();
            LayoutRect[] cached;
            if (_shopCache.TryGetValue(count, out cached)) return cached;
            return ComputeShopCardRects(count);
        }

        /// <summary>N张战场卡牌的所有位置（含方向翻转）</summary>
        public LayoutRect[] GetBoardCardRects(int count)
        {
            if (count < 1 || count > 7) return new LayoutRect[0];
            if (_cacheDirty) PreWarmCache();
            LayoutRect[] cached;
            if (_boardCache.TryGetValue(count, out cached)) return cached;
            return ComputeBoardCardRects(count);
        }

        // ══════════════════════════════════
        // 手牌: 中心锚点圆弧扇形 (Center-Pivot Fan)
        //
        // 枢轴点(圆心): 屏幕中轴线下方屏幕外 (pivotY > clientHeight)
        // 所有卡牌中心到圆心距离 = 半径R, 中心落在以枢轴为圆心的圆弧上
        // 旋转中心为卡牌中心(RotationCenterY=Height/2)
        // 圆弧∪形: 中心卡最高, 边缘卡沿弧线下降
        // 旋转方向: 左卡逆时针右卡顺时针, 中线沿半径方向, 所有中线交于O
        // ≤3张: 平铺(角度=0)
        // ≥4张: 等角度圆弧扇形, 总角60°-80°
        // ══════════════════════════════════

        private double HandCardW => _clientRect.Width * (_config.HandCardWidthPct / 100.0);
        private double HandCardH => HandCardW / 0.72;

        /// <summary>枢轴X = 屏幕垂直中轴线</summary>
        private double HandPivotX => _clientRect.Width / 2.0 + OffsetX(_config.HandOffsetX);

        /// <summary>枢轴Y = 屏幕高×比例 (>1.0=屏幕下方, 圆心)</summary>
        private double HandPivotY => _clientRect.Height * _config.HandPivotYRatio + OffsetY(_config.HandOffsetY);

        /// <summary>半径R = 枢轴Y - 中心卡牌中心Y (圆心到卡牌正中心距离)</summary>
        private double HandRadius => HandPivotY - HandCardCenterY;

        /// <summary>中心卡牌中心Y (角度=0时, 圆弧最高点)</summary>
        private double HandCardCenterY => _clientRect.Height * _config.HandTopYRatio + OffsetY(_config.HandOffsetY);

        public LayoutRect GetHandCardRect(int index, int totalCount)
        {
            var transforms = GetHandCardTransforms(totalCount);
            if (index < 0 || index >= transforms.Length) return new LayoutRect(0, 0, 80, 50);
            var t = transforms[index];
            return new LayoutRect(t.CanvasLeft, t.CanvasTop, t.Width, t.Height);
        }

        public HandCardTransform[] GetHandCardTransforms(int count)
        {
            if (count <= 0) return new HandCardTransform[0];

            double cardW = HandCardW;
            double cardH = HandCardH;
            double pivotX = HandPivotX;
            double pivotY = HandPivotY;
            double R = HandRadius;

            double totalAngle; // 总扇形角度(度)
            if (count <= 3)
                totalAngle = 0;
            else
                // 4张起启用扇形, 线性增长到10张=HandMaxTotalAngle
                totalAngle = Math.Min(_config.HandMaxTotalAngle,
                                      (count - 3) * (_config.HandMaxTotalAngle / 7.0));

            var result = new HandCardTransform[count];

            if (count <= 3)
            {
                // 平铺模式: 卡牌中心共线, 无旋转, 间距=cardW+gap
                double handGap = OffsetX(_config.HandGap);
                double span = count * cardW + (count - 1) * handGap;
                double step = cardW + handGap;
                double centerY = HandCardCenterY;
                for (int i = 0; i < count; i++)
                {
                    double centerX = pivotX - span / 2.0 + cardW / 2.0 + i * step;
                    result[i] = new HandCardTransform
                    {
                        CenterX = centerX, CenterY = centerY,
                        Angle = 0, Width = cardW, Height = cardH,
                    };
                }
            }
            else
            {
                // 圆弧扇形模式: 卡牌中心在圆弧上, 允许自然重叠
                double halfAngle = totalAngle / 2.0;
                for (int i = 0; i < count; i++)
                {
                    // 角度均分: [-halfAngle, +halfAngle]
                    double angleDeg = count == 1 ? 0
                        : -halfAngle + i * (totalAngle / (count - 1));
                    double angleRad = angleDeg * Math.PI / 180.0;

                    // 圆弧: pivot + R*(sinθ, -cosθ), 中心卡最高
                    double centerX = pivotX + R * Math.Sin(angleRad);
                    double centerY = pivotY - R * Math.Cos(angleRad);

                    result[i] = new HandCardTransform
                    {
                        CenterX = centerX, CenterY = centerY,
                        Angle = angleDeg,
                        Width = cardW, Height = cardH,
                    };
                }
            }
            return result;
        }

        // ══════════════════════════════════
        // 奇偶居中算法
        // ══════════════════════════════════

        private double[] CalculateCenters(int count, double areaWidth, double itemWidth, double gap)
        {
            double unit = itemWidth + gap;
            double[] centers = new double[count];
            // 左边缘对齐: 卡牌组总宽度居中, 第一张卡中心 = 左边缘 + 半卡宽
            double totalWidth = count * itemWidth + (count - 1) * gap;
            double startX = (areaWidth - totalWidth) / 2 + itemWidth / 2;
            for (int i = 0; i < count; i++)
                centers[i] = startX + i * unit;
            return centers;
        }

        // ══════════════════════════════════
        // 调试快照
        // ══════════════════════════════════

        public DebugSnapshot GetDebugSnapshot()
        {
            return new DebugSnapshot
            {
                ShopArea = GetShopArea(),
                BoardArea = GetBoardArea(),
                TavernButton = GetTavernButtonArea(),
                RefreshButton = GetRefreshButtonArea(),
                FreezeButton = GetFreezeButtonArea(),
                HeroPower = GetHeroPowerArea(),
                Hand = GetHandArea(),
                GameWidth = _clientRect.Width,
                GameHeight = _clientRect.Height,
                DpiScale = _dpiScale,
            };
        }
    }

    public struct LayoutRect
    {
        public double Left, Top, Width, Height;
        public LayoutRect(double x, double y, double w, double h)
        { Left = x; Top = y; Width = w; Height = h; }
    }

    public struct LayoutPoint
    {
        public double X, Y;
        public LayoutPoint(double x, double y) { X = x; Y = y; }
    }

    /// <summary>手牌扇形变换: 卡牌中心锚点+旋转角度</summary>
    public class HandCardTransform
    {
        public double CenterX;      // 卡牌中心X(屏幕像素, 圆弧上的锚点)
        public double CenterY;      // 卡牌中心Y(所有卡中心在圆弧上)
        public double Angle;        // 旋转角度(度, 中间0, 左负右正, 左逆时针右顺时针)
        public double Width;        // 卡牌宽
        public double Height;       // 卡牌高

        /// <summary>WPF Canvas.SetLeft 位置 (中心锚定)</summary>
        public double CanvasLeft => CenterX - Width / 2.0;
        /// <summary>WPF Canvas.SetTop 位置 (中心锚定)</summary>
        public double CanvasTop => CenterY - Height / 2.0;
        /// <summary>旋转中心X = 卡宽一半(卡牌正中心)</summary>
        public double RotationCenterX => Width / 2.0;
        /// <summary>旋转中心Y = 卡高一半(卡牌正中心)</summary>
        public double RotationCenterY => Height / 2.0;
    }

    public class DebugSnapshot
    {
        public LayoutRect ShopArea, BoardArea, TavernButton, RefreshButton, FreezeButton, HeroPower, Hand;
        public double GameWidth, GameHeight, DpiScale;
    }
}
