using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace BobCoach.Engine
{
    /// <summary>
    /// F10校准模式: 方向键微调偏移
    /// 1-7=选区域 ↑↓←→=微调(4px) S=保存 R=重置
    /// 手牌区专属: []=间隙 -=卡宽 Shift+↑↓=枢Y Shift+←→=总角 Ctrl+↑↓=中心Y
    /// </summary>
    public class CalibrationOverlay
    {
        private readonly List<UIElement> _elements = new List<UIElement>();
        private string _selectedZone = "Shop";
        private double _step = 4.0;
        private LayoutConfig _config;
        private GameLayoutCalculator _calc;
        private double _canvasW, _canvasH;
        private readonly Dictionary<Key, DateTime> _keyCooldowns = new Dictionary<Key, DateTime>();

        public bool Active { get; private set; } = false;
        public LayoutConfig Config => _config;
        private string _flashMsg = "";
        private DateTime _flashTime = DateTime.MinValue;
        private int _previewCardCount = 3;  // 校准预览卡牌数, 默认3张
        private int _activeTier = 1;        // 当前校准的酒馆等级(1-6), 影响保存到哪个分档

        public CalibrationOverlay()
        {
            _config = LayoutConfig.Load();
            _calc = new GameLayoutCalculator(_config);
        }

        public void Activate()
        {
            Active = true;
            _calc.Refresh();
            // 旧版本兼容由LayoutConfig.Load()的Version检查处理，此处不做自动重置
            LogCalib("校准模式开启 | 1-8=选区域 9=推荐面板 ↑↓←→=微调 G=步长(4/2/1/0.5px) S=保存 R=重置 F10=退出 | Panel: ↑↓←→=移动 +/-=缩放 | Shop/Board: Shift+↑↓=卡高 Shift+←→=卡宽 Ctrl+↑↓=隙 Ctrl+←→=偏移");
        }

        public void Deactivate()
        {
            Active = false;
            Clear();
            LogCalib("校准模式关闭");
        }

        public bool HandleKey(Key key)
        {
            if (!Active) return false;

            // 去抖: 同键200ms内重复触发忽略, 防止按住方向键时每帧累积偏移
            DateTime last;
            if (_keyCooldowns.TryGetValue(key, out last) && (DateTime.Now - last).TotalMilliseconds < 200)
                return false;
            _keyCooldowns[key] = DateTime.Now;

            switch (key)
            {
                case Key.D1: _selectedZone = "Status"; LogCalib("选中: Status(状态条)"); return true;
                case Key.D2: _selectedZone = "Shop";   LogCalib("选中: Shop(商店)"); return true;
                case Key.D3: _selectedZone = "Board";  LogCalib("选中: Board(战场)"); return true;
                case Key.D4: _selectedZone = "Tavern"; LogCalib("选中: Tavern(升本按钮)"); return true;
                case Key.D5: _selectedZone = "Refresh"; LogCalib("选中: Refresh(刷新按钮)"); return true;
                case Key.D6: _selectedZone = "HeroPower"; LogCalib("选中: HeroPower(英雄技能)"); return true;
                case Key.D7: _selectedZone = "Hand"; LogCalib("选中: Hand(手牌)"); return true;
                case Key.D8: _selectedZone = "Freeze"; LogCalib("选中: Freeze(冻结按钮)"); return true;
                case Key.D9: _selectedZone = "Panel"; LogCalib("选中: Panel(饰品/发现推荐面板) | ↑↓←→=移动 +/-=缩放"); return true;
                case Key.G:
                    // 微调步长循环: 4→2→1→0.5px (对齐游戏内卡槽时切细粒度)
                    _step = _step >= 4.0 ? 2.0 : _step >= 2.0 ? 1.0 : _step >= 1.0 ? 0.5 : 4.0;
                    LogCalib(string.Format("微调步长: {0}px", _step));
                    return true;
                case Key.Up:    HandleArrow(0, -_step); return true;
                case Key.Down:  HandleArrow(0, +_step); return true;
                case Key.Left:  HandleArrow(-_step, 0); return true;
                case Key.Right: HandleArrow(+_step, 0); return true;
                case Key.S:
                    UpdateCalibrationSize();
                    _config.Save();
                    _flashMsg = "✓ 已保存到 ui_config.json";
                    _flashTime = DateTime.Now;
                    LogCalib("配置已保存到 ui_config.json");
                    return true;
                case Key.R:
                    ResetCurrentZone();
                    _calc.SetTier(_activeTier);
                    { double w2 = _canvasW, h2 = _canvasH;
                      if (w2 < 100) { try { var c2 = Hearthstone_Deck_Tracker.API.Core.OverlayCanvas; if (c2 != null && c2.ActualWidth > 100) { w2 = c2.ActualWidth; h2 = c2.ActualHeight; } } catch { } }
                      if (w2 > 100) _calc.RefreshWithSize(w2, h2); }
                    return true;
                // 手牌区专属: []=隙 -=宽 Shift+↑↓=枢轴Y Shift+←→=总角度 Ctrl+↑↓=中心Y
                case Key.OemOpenBrackets:
                    if (_selectedZone == "Hand") { _config.HandGap -= 1; Recalc(); LogCalib(string.Format("手牌间隙:{0}px", _config.HandGap)); }
                    else if (_selectedZone == "Shop" || _selectedZone == "Board") { _previewCardCount = Math.Max(1, _previewCardCount - 1); Recalc(); LogCalib(string.Format("预览卡牌数:{0}", _previewCardCount)); }
                    return true;
                case Key.OemCloseBrackets:
                    if (_selectedZone == "Hand") { _config.HandGap += 1; Recalc(); LogCalib(string.Format("手牌间隙:{0}px", _config.HandGap)); }
                    else if (_selectedZone == "Shop" || _selectedZone == "Board") { _previewCardCount = Math.Min(7, _previewCardCount + 1); Recalc(); LogCalib(string.Format("预览卡牌数:{0}", _previewCardCount)); }
                    return true;
                case Key.OemMinus:
                    if (_selectedZone == "Hand") { _config.HandCardWidthPct -= 0.25; Recalc(); LogCalib(string.Format("手牌卡宽:{0:F2}%", _config.HandCardWidthPct)); }
                    else if (_selectedZone == "Panel") { _config.PanelScale = Math.Max(0.4, _config.PanelScale - 0.05); LogCalib(string.Format("面板缩放:{0:F2}", _config.PanelScale)); }
                    return true;
                case Key.OemPlus:
                    if (_selectedZone == "Hand") { _config.HandCardWidthPct += 0.25; Recalc(); LogCalib(string.Format("手牌卡宽:{0:F2}%", _config.HandCardWidthPct)); }
                    else if (_selectedZone == "Panel") { _config.PanelScale = Math.Min(3.0, _config.PanelScale + 0.05); LogCalib(string.Format("面板缩放:{0:F2}", _config.PanelScale)); }
                    return true;
                // F1-F6: 切换校准酒馆等级(影响保存到哪个分档数组)
                case Key.F1: _activeTier = 1; Recalc(); LogCalib("校准等级: T1 (4槽)"); return true;
                case Key.F2: _activeTier = 2; Recalc(); LogCalib("校准等级: T2 (5槽)"); return true;
                case Key.F3: _activeTier = 3; Recalc(); LogCalib("校准等级: T3 (5槽)"); return true;
                case Key.F4: _activeTier = 4; Recalc(); LogCalib("校准等级: T4 (6槽)"); return true;
                case Key.F5: _activeTier = 5; Recalc(); LogCalib("校准等级: T5 (6槽)"); return true;
                case Key.F6: _activeTier = 6; Recalc(); LogCalib("校准等级: T6 (7槽)"); return true;
            }
            return false;
        }

        private void HandleArrow(double dx, double dy)
        {
            bool shift = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
            bool ctrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);

            if (_selectedZone == "Hand")
            {
                if (ctrl)
                {
                    // Ctrl+↑↓: 调整中心卡中心Y位置
                    if (dy != 0) _config.HandTopYRatio += dy > 0 ? 0.01 : -0.01;
                }
                else if (shift)
                {
                    // Shift+↑↓: 调整枢轴Y位置, Shift+←→: 调整满手总角度
                    if (dy != 0) _config.HandPivotYRatio += dy > 0 ? 0.02 : -0.02;
                    if (dx != 0) _config.HandMaxTotalAngle += dx > 0 ? 2.0 : -2.0;
                }
                else
                {
                    ApplyOffset(dx, dy);
                }
            }
            else if (_selectedZone == "Shop" || _selectedZone == "Board")
            {
                int tierIdx = LayoutConfig.TierIndex(_activeTier);
                if (shift)
                {
                    // Shift+↑↓: 调卡高(全局), Shift+←→: 调卡宽(全局) — 跟随G步长精调
                    double ws = _step / 4.0;
                    if (dy != 0) _config.ShopCardHeightPct = Math.Max(1.0, _config.ShopCardHeightPct + (dy > 0 ? 0.5 : -0.5) * ws);
                    if (dx != 0)
                    {
                        // 07072158: 卡宽改全局(游戏商店卡尺寸不随酒馆等级变, 分档要调6次且T6等档默认值不符→偏差)。
                        // 调一次全tier生效; 清分档数组避免其覆盖全局。下限0.5%。
                        _config.ShopCardWidthPct = Math.Max(0.5, _config.ShopCardWidthPct + (dx > 0 ? 0.25 : -0.25) * ws);
                        for (int k = 0; k < _config.TierCardWidthPct.Length; k++) _config.TierCardWidthPct[k] = 0;
                    }
                    LogCalib(string.Format("卡宽:{0:F3}% 卡高:{1:F3}% (全局, 步长{2})", _config.ShopCardWidthPct, _config.ShopCardHeightPct, _step));
                }
                else if (ctrl && !shift)
                {
                    // Ctrl+↑↓: 调间隙(全局, 跟随G步长), Ctrl+←→: 调卡牌组水平偏移(当前分档, 跟随G步长)
                    if (dy != 0)
                    {
                        // 07072158: 间隙改全局(同卡宽理由), 调一次全tier生效; 清分档。下限0.5px。
                        _config.ShopCardGap = Math.Max(0.5, _config.ShopCardGap + (dy > 0 ? 1 : -1) * (_step / 4.0));
                        for (int k = 0; k < _config.TierCardGap.Length; k++) _config.TierCardGap[k] = 0;
                        LogCalib(string.Format("间隙:{0:F2}px (全局, 步长{1})", _config.ShopCardGap, _step));
                    }
                    if (dx != 0) { _config.TierCardOffsetX[tierIdx] += dx > 0 ? _step : -_step; LogCalib(string.Format("T{1} 卡牌偏移X:{0:F1}px (步长{2})", _config.TierCardOffsetX[tierIdx], _activeTier, _step)); }
                }
                else if (ctrl && shift)
                {
                    // Ctrl+Shift+↑↓: 调卡牌组竖直偏移(全局), Ctrl+Shift+←→: 调标签水平偏移(当前分档)
                    if (dy != 0) { _config.ShopCardOffsetY += dy > 0 ? 2 : -2; LogCalib(string.Format("卡牌偏移Y:{0:F0}px (全局)", _config.ShopCardOffsetY)); }
                    if (dx != 0) { _config.TierLabelOffsetX[tierIdx] += dx > 0 ? 2 : -2; LogCalib(string.Format("T{1} 标签偏移X:{0:F0}px", _config.TierLabelOffsetX[tierIdx], _activeTier)); }
                }
                else
                {
                    ApplyOffset(dx, dy);
                }
            }
            else
            {
                ApplyOffset(dx, dy);
            }
            // 配置已通过引用修改, 刷新活动值(含分档偏移)并重建缓存
            _calc.SetTier(_activeTier);
            double w = _canvasW, h = _canvasH;
            if (w < 100)
            {
                try
                {
                    var c = Hearthstone_Deck_Tracker.API.Core.OverlayCanvas;
                    if (c != null && c.ActualWidth > 100) { w = c.ActualWidth; h = c.ActualHeight; }
                }
                catch { }
            }
            if (w > 100) _calc.RefreshWithSize(w, h);
        }

        private void ResetCurrentZone()
        {
            var def = new LayoutConfig();
            switch (_selectedZone)
            {
                case "Shop":
                case "Board":
                    _config.ShopOffsetX = def.ShopOffsetX; _config.ShopOffsetY = def.ShopOffsetY;
                    _config.BoardOffsetX = def.BoardOffsetX; _config.BoardOffsetY = def.BoardOffsetY;
                    _config.ShopCardWidthPct = def.ShopCardWidthPct;
                    _config.ShopCardHeightPct = def.ShopCardHeightPct;
                    _config.ShopCardGap = def.ShopCardGap;
                    _config.ShopCardOffsetX = def.ShopCardOffsetX;
                    _config.ShopCardOffsetY = def.ShopCardOffsetY;
                    _config.ShopLabelOffsetX = def.ShopLabelOffsetX;
                    break;
                case "Tavern":
                    _config.TavernOffsetX = def.TavernOffsetX; _config.TavernOffsetY = def.TavernOffsetY;
                    break;
                case "Refresh":
                    _config.RefreshOffsetX = def.RefreshOffsetX; _config.RefreshOffsetY = def.RefreshOffsetY;
                    break;
                case "Freeze":
                    _config.FreezeOffsetX = def.FreezeOffsetX; _config.FreezeOffsetY = def.FreezeOffsetY;
                    break;
                case "HeroPower":
                    _config.HeroPowerOffsetX = def.HeroPowerOffsetX; _config.HeroPowerOffsetY = def.HeroPowerOffsetY;
                    break;
                case "Hand":
                    _config.HandOffsetX = def.HandOffsetX;
                    _config.HandOffsetY = def.HandOffsetY;
                    _config.HandCardWidthPct = def.HandCardWidthPct;
                    _config.HandGap = def.HandGap;
                    _config.HandTopYRatio = def.HandTopYRatio;
                    _config.HandPivotYRatio = def.HandPivotYRatio;
                    _config.HandMaxTotalAngle = def.HandMaxTotalAngle;
                    break;
                case "Panel":
                    _config.PanelOffsetX = def.PanelOffsetX;
                    _config.PanelOffsetY = def.PanelOffsetY;
                    _config.PanelScale = def.PanelScale;
                    break;
                default:
                    break;
            }
            LogCalib(string.Format("{0} 参数已重置为默认值", _selectedZone));
        }

        private void ApplyOffset(double dx, double dy)
        {
            switch (_selectedZone)
            {
                case "Status":
                    break;
                case "Shop":
                    _config.ShopOffsetX += dx; _config.ShopOffsetY += dy;
                    break;
                case "Board":
                    _config.BoardOffsetX += dx; _config.BoardOffsetY += dy;
                    break;
                case "Tavern":
                    _config.TavernOffsetX += dx; _config.TavernOffsetY += dy;
                    break;
                case "Refresh":
                    _config.RefreshOffsetX += dx; _config.RefreshOffsetY += dy;
                    break;
                case "Freeze":
                    _config.FreezeOffsetX += dx; _config.FreezeOffsetY += dy;
                    break;
                case "HeroPower":
                    _config.HeroPowerOffsetX += dx; _config.HeroPowerOffsetY += dy;
                    break;
                case "Hand":
                    _config.HandOffsetX += dx; _config.HandOffsetY += dy;
                    break;
                case "Panel":
                    // 面板偏移与其他区域同基准(校准画布像素, 保存时记录 CalibrationWidth, 换分辨率经 ScaleX/Rebase 自适应)
                    _config.PanelOffsetX += dx; _config.PanelOffsetY += dy;
                    break;
            }
        }

        private void Recalc()
        {
            _calc.SetTier(_activeTier);
            double w = _canvasW, h = _canvasH;
            if (w < 100)
            {
                try
                {
                    var c = Hearthstone_Deck_Tracker.API.Core.OverlayCanvas;
                    if (c != null && c.ActualWidth > 100) { w = c.ActualWidth; h = c.ActualHeight; }
                }
                catch { }
            }
            if (w > 100) _calc.RefreshWithSize(w, h);
        }

        private void UpdateCalibrationSize()
        {
            double w = _canvasW, h = _canvasH;
            if (w < 100 || h < 100)
            {
                try
                {
                    var c = Hearthstone_Deck_Tracker.API.Core.OverlayCanvas;
                    if (c != null && c.ActualWidth > 100)
                    {
                        w = c.ActualWidth;
                        h = c.ActualHeight;
                    }
                }
                catch { }
            }
            _config.SetCalibrationSize(w, h);
        }

        public void Render(Canvas canvas)
        {
            if (!Active || canvas == null) return;

            _canvasW = canvas.ActualWidth; _canvasH = canvas.ActualHeight;
            // 直接用画布尺寸, 不调用Refresh()(其内部GetHearthstoneClientRect会Sleep阻塞UI)
            _calc.RefreshWithSize(_canvasW, _canvasH);
            if (_calc.ClientWidth < 400) return;

            // Clear必须在所有early return之后, 否则画布被清空却不重绘
            Clear();

            var snap = _calc.GetDebugSnapshot();

            // 区域框
            DrawZone(canvas, snap.ShopArea, "#00BCD4", "SHOP [2]");
            DrawZone(canvas, snap.BoardArea, "#FFEB3B", "BOARD [3]");
            DrawZone(canvas, snap.TavernButton, "#F44336", "TAVERN [4]");
            DrawZone(canvas, snap.RefreshButton, "#FF9800", "REFRESH [5]");
            DrawZone(canvas, _calc.GetFreezeButtonArea(), "#03A9F4", "FREEZE [8]");
            DrawZone(canvas, snap.HeroPower, "#9C27B0", "HERO [6]");
            DrawZone(canvas, snap.Hand, "#00BCD4", "HAND [7]");

            // 推荐面板预览框(饰品/发现) — 显示当前 PanelOffset/Scale 位置, 与实战 OverlayRenderer 一致
            double psc = _config.PanelScale > 0.3 ? _config.PanelScale : 1.0;
            var panelRect = new LayoutRect(
                _config.ScaleX(_config.PanelOffsetX, _calc.ClientWidth),
                _config.ScaleY(_config.PanelOffsetY, _calc.ClientHeight),
                _calc.ScaleX(360) * psc,
                _calc.ScaleY(160) * psc);
            DrawZone(canvas, panelRect, "#FFD700", "PANEL [9]");

            // 商店标签按酒馆等级展示槽数定位；辛达苟萨少随从不改变展示槽锚点。
            int shopCnt = _selectedZone == "Shop" ? _previewCardCount : GetShopLayoutCardCount(_activeTier);
            var shopRects = _calc.GetShopCardRects(shopCnt);
            for (int i = 0; i < shopRects.Length; i++)
            {
                DrawCardSlot(canvas, shopRects[i], "#00FF00", "S" + i);
                DrawLabelBar(canvas, shopRects[i], _calc.ShopLabelOffsetX, "#00FF00");
            }

            // 战场卡牌测试位置(cnt张)
            int cnt = _previewCardCount;
            var boardRects = _calc.GetBoardCardRects(cnt);
            for (int i = 0; i < boardRects.Length; i++)
                DrawCardSlot(canvas, boardRects[i], "#FF9800", "B" + i);

            // 手牌卡牌测试位置(10张, 扇形旋转) — 色阶 + Z序堆叠
            var handTransforms = _calc.GetHandCardTransforms(10);
            for (int i = 0; i < handTransforms.Length; i++)
            {
                double t = handTransforms.Length > 1 ? i / (double)(handTransforms.Length - 1) : 0.5;
                string colorHex = LerpColorHex("#2196F3", "#4CAF50", "#FF9800", t); // 蓝→绿→橙
                var elem = DrawHandCardSlot(canvas, handTransforms[i], colorHex, "H" + i);

                // 中间卡牌在上层, 边缘卡牌在下层 (模拟搓牌物理)
                double distFromCenter = Math.Abs(i - (handTransforms.Length - 1) / 2.0);
                int zIndex = 900 + (int)((handTransforms.Length / 2.0 - distFromCenter) * 20);
                Panel.SetZIndex(elem, zIndex);
            }

            // 手牌选中时: 绘制径向引导线 + 圆心标记
            if (_selectedZone == "Hand")
            {
                DrawHandRadialGuides(canvas, handTransforms);
                DrawPivotMarker(canvas);
            }

            // 信息面板
            DrawInfoPanel(canvas, snap);
        }

        public void Clear()
        {
            var canvas = Hearthstone_Deck_Tracker.API.Core.OverlayCanvas;
            if (canvas == null) return;
            foreach (var el in _elements) canvas.Children.Remove(el);
            _elements.Clear();
        }

        private void DrawZone(Canvas canvas, LayoutRect rect, string colorHex, string label)
        {
            var b = ParseBrush(colorHex);
            var r = new Rectangle
            {
                Width = rect.Width, Height = rect.Height,
                Stroke = b,
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 4, 2 },
                Fill = new SolidColorBrush(Color.FromArgb(25, b.Color.R, b.Color.G, b.Color.B)),
                IsHitTestVisible = false,
            };
            Add(canvas, r, rect.Left, rect.Top);

            var t = new TextBlock
            {
                Text = label, FontSize = 12, FontWeight = FontWeights.Bold,
                Foreground = b, IsHitTestVisible = false,
            };
            Add(canvas, t, rect.Left + 3, rect.Top - 16);
        }

        private void DrawCardSlot(Canvas canvas, LayoutRect rect, string colorHex, string label)
        {
            var b = ParseBrush(colorHex);
            var box = new Rectangle
            {
                Width = rect.Width, Height = rect.Height,
                Stroke = b, StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 2, 2 }, IsHitTestVisible = false,
            };
            Add(canvas, box, rect.Left, rect.Top);

            double cx = rect.Left + rect.Width / 2, cy = rect.Top + rect.Height / 2;
            AddLine(canvas, cx - 6, cy, cx + 6, cy, b);
            AddLine(canvas, cx, cy - 6, cx, cy + 6, b);

            var t = new TextBlock
            {
                Text = label, FontSize = 10, Foreground = b, IsHitTestVisible = false,
            };
            Add(canvas, t, cx + 5, cy - 14);
        }

        /// <summary>手牌卡槽: Canvas定位 + RenderTransformOrigin中心旋转, 返回Grid元素用于Z序设置</summary>
        private Grid DrawHandCardSlot(Canvas canvas, Engine.HandCardTransform xf, string colorHex, string label)
        {
            var b = ParseBrush(colorHex);

            var grid = new Grid
            {
                Width = xf.Width, Height = xf.Height,
                IsHitTestVisible = false,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new RotateTransform(xf.Angle),
            };
            Canvas.SetLeft(grid, xf.CanvasLeft);
            Canvas.SetTop(grid, xf.CanvasTop);

            // 卡牌边框
            var box = new Rectangle
            {
                Width = xf.Width, Height = xf.Height,
                Stroke = b, StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 2, 2 },
                IsHitTestVisible = false,
            };
            grid.Children.Add(box);

            // 十字准星(相对坐标, 中心点)
            double cwx = xf.Width / 2, cwy = xf.Height / 2;
            var hLine = new Line { X1 = cwx - 8, Y1 = cwy, X2 = cwx + 8, Y2 = cwy, Stroke = b, StrokeThickness = 1, IsHitTestVisible = false };
            var vLine = new Line { X1 = cwx, Y1 = cwy - 8, X2 = cwx, Y2 = cwy + 8, Stroke = b, StrokeThickness = 1, IsHitTestVisible = false };
            grid.Children.Add(hLine); grid.Children.Add(vLine);

            // 标签(相对坐标, 左上角)
            var t = new TextBlock
            {
                Text = label, FontSize = 9, Foreground = b, IsHitTestVisible = false,
            };
            Canvas.SetLeft(t, 3); Canvas.SetTop(t, 2);
            grid.Children.Add(t);

            canvas.Children.Add(grid); _elements.Add(grid);
            return grid;
        }

        /// <summary>三色线性插值: 0.0→hex1, 0.5→hex2, 1.0→hex3</summary>
        private static string LerpColorHex(string hex1, string hex2, string hex3, double t)
        {
            Color c1 = (Color)ColorConverter.ConvertFromString(hex1);
            Color c2 = (Color)ColorConverter.ConvertFromString(hex2);
            Color c3 = (Color)ColorConverter.ConvertFromString(hex3);
            Color result;
            if (t <= 0.5)
            {
                double s = t / 0.5;
                result = Color.FromArgb(
                    (byte)(c1.A + (c2.A - c1.A) * s), (byte)(c1.R + (c2.R - c1.R) * s),
                    (byte)(c1.G + (c2.G - c1.G) * s), (byte)(c1.B + (c2.B - c1.B) * s));
            }
            else
            {
                double s = (t - 0.5) / 0.5;
                result = Color.FromArgb(
                    (byte)(c2.A + (c3.A - c2.A) * s), (byte)(c2.R + (c3.R - c2.R) * s),
                    (byte)(c2.G + (c3.G - c2.G) * s), (byte)(c2.B + (c3.B - c2.B) * s));
            }
            return string.Format("#{0:X2}{1:X2}{2:X2}", result.R, result.G, result.B);
        }

        /// <summary>绘制手牌径向引导线(从卡牌中心指向圆心)</summary>
        private void DrawHandRadialGuides(Canvas canvas, Engine.HandCardTransform[] xfs)
        {
            var b = ParseBrush("#FFD700") ?? Brushes.Gold;
            double pivotX = _calc.ClientWidth / 2.0 + _config.HandOffsetX;
            double pivotY = _calc.ClientHeight * _config.HandPivotYRatio + _config.HandOffsetY;
            double canvasH = canvas.ActualHeight > 0 ? canvas.ActualHeight : _calc.ClientHeight;

            foreach (var xf in xfs)
            {
                // 卡牌中心 = (CanvasLeft + W/2, CanvasTop + H/2) 但已旋转...
                // 简化: 用未旋转的中心点 (CenterX, CenterY)
                double cx = xf.CenterX, cy = xf.CenterY;

                // 从中心画线到可见区域底部 (或到圆心如果在可见区域内)
                double endX = pivotX, endY = Math.Min(pivotY, canvasH);
                // 如果圆心完全不可见, 延长线到画布底部
                if (pivotY > canvasH)
                {
                    double t = (canvasH - cy) / (pivotY - cy);
                    endX = cx + (pivotX - cx) * t;
                    endY = canvasH;
                }

                var line = new Line
                {
                    X1 = cx, Y1 = cy, X2 = endX, Y2 = endY,
                    Stroke = b, StrokeThickness = 0.5,
                    StrokeDashArray = new DoubleCollection { 3, 3 },
                    Opacity = 0.4,
                    IsHitTestVisible = false,
                };
                canvas.Children.Add(line); _elements.Add(line);
            }
        }

        /// <summary>绘制圆心标记(屏幕底部中轴线上)</summary>
        private void DrawPivotMarker(Canvas canvas)
        {
            var gold = ParseBrush("#FFD700") ?? Brushes.Gold;
            double pivotX = _calc.ClientWidth / 2.0 + _config.HandOffsetX;
            double pivotY = _calc.ClientHeight * _config.HandPivotYRatio + _config.HandOffsetY;
            double canvasH = canvas.ActualHeight > 0 ? canvas.ActualHeight : _calc.ClientHeight;
            double canvasW = canvas.ActualWidth > 0 ? canvas.ActualWidth : _calc.ClientWidth;

            // 如果圆心在可见区域内, 画十字圆
            if (pivotY <= canvasH)
            {
                var circle = new Ellipse
                {
                    Width = 10, Height = 10,
                    Stroke = gold, StrokeThickness = 2,
                    IsHitTestVisible = false,
                };
                Add(canvas, circle, pivotX - 5, pivotY - 5);

                // O 标签
                var label = new TextBlock
                {
                    Text = "O", FontSize = 12, FontWeight = FontWeights.Bold,
                    Foreground = gold, IsHitTestVisible = false,
                };
                Add(canvas, label, pivotX + 8, pivotY - 8);
            }
            else
            {
                // 圆心在屏幕外下方, 在底部画指示器
                double indicatorY = canvasH - 4;
                // 向下箭头
                var arrow = new TextBlock
                {
                    Text = "▼", FontSize = 16, FontWeight = FontWeights.Bold,
                    Foreground = gold, IsHitTestVisible = false,
                };
                arrow.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                Add(canvas, arrow, pivotX - arrow.DesiredSize.Width / 2, indicatorY - 16);

                // 坐标信息
                double below = pivotY - canvasH;
                var info = new TextBlock
                {
                    Text = string.Format("O({0:F0},{1:F0}) 屏下{2:F0}px", pivotX, pivotY, below),
                    FontSize = 10, Foreground = gold,
                    IsHitTestVisible = false,
                };
                info.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                Add(canvas, info, pivotX - info.DesiredSize.Width / 2, indicatorY + 2);
            }
        }

        /// <summary>绘制标签条预览(校准用), 与ShowShopCardRating布局一致</summary>
        private void DrawLabelBar(Canvas canvas, LayoutRect cardRect, double labelOffX, string colorHex)
        {
            var b = ParseBrush(colorHex);
            double cw = cardRect.Width, ch = cardRect.Height;
            double barW = cw * 0.96;
            double barH = 14;
            double barLeft = cardRect.Left + (cw - barW) / 2 + labelOffX;
            double barTop = cardRect.Top + ch + 2;

            var bar = new Rectangle
            {
                Width = barW, Height = barH,
                RadiusX = 3, RadiusY = 3,
                Fill = new SolidColorBrush(Color.FromArgb(180, 12, 12, 14)),
                Stroke = b, StrokeThickness = 1,
                IsHitTestVisible = false,
            };
            Add(canvas, bar, barLeft, barTop);

            // 标签文字 (示意)
            var label = new TextBlock
            {
                Text = string.Format("X{0:F0}", labelOffX),
                FontSize = 9, Foreground = b, IsHitTestVisible = false,
            };
            label.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            Add(canvas, label, barLeft + barW / 2 - label.DesiredSize.Width / 2, barTop + 1);

            // 向下小箭头
            double cx = cardRect.Left + cw / 2 + labelOffX;
            double arrowW = 6, arrowH = 4;
            var arrow = new Polygon
            {
                Points = new PointCollection { new Point(0, 0), new Point(arrowW, 0), new Point(arrowW / 2, arrowH) },
                Fill = b, Opacity = 0.7, IsHitTestVisible = false,
            };
            Add(canvas, arrow, cx - arrowW / 2, barTop + barH + 1);
        }

        private void DrawInfoPanel(Canvas canvas, DebugSnapshot snap)
        {
            // 2秒内显示保存反馈
            string saveHint = "";
            if ((DateTime.Now - _flashTime).TotalSeconds < 2)
                saveHint = "\n" + _flashMsg;

            string extraParams = "";
            if (_selectedZone == "Hand")
            {
                var xfs = _calc.GetHandCardTransforms(10);
                double belowPx = _calc.ClientHeight * (_config.HandPivotYRatio - 1.0);
                extraParams = string.Format(
                    "\n手牌: 宽{0:F2}% 中心Y{1:F2} 枢Y{2:F2}(屏下{6:F0}px) 总角{3:F0}° R={4:F0}px\n" +
                    "H0 中心({7:F0},{8:F0}) ∠{9:F1}° | H9 中心({10:F0},{11:F0}) ∠{12:F1}°\n" +
                    "隙{5:F0}px | []=隙 -=宽 Ctrl+↑↓=中心Y Shift+↑↓=枢Y Shift+←→=角",
                    _config.HandCardWidthPct, _config.HandTopYRatio,
                    _config.HandPivotYRatio, _config.HandMaxTotalAngle,
                    _calc.ClientHeight * (_config.HandPivotYRatio - _config.HandTopYRatio),
                    _config.HandGap, belowPx,
                    xfs[0].CenterX, xfs[0].CenterY, xfs[0].Angle,
                    xfs[9].CenterX, xfs[9].CenterY, xfs[9].Angle);
            }
            else if (_selectedZone == "Shop" || _selectedZone == "Board")
            {
                var tw = _config.GetCardWidthPct(_activeTier);
                var tg = _config.GetCardGap(_activeTier);
                var tx = _config.GetCardOffsetX(_activeTier);
                var tl = _config.GetLabelOffsetX(_activeTier);
                extraParams = string.Format(
                    "\nT{5}({6}槽) | 预览{4}张 | []=减 ]=加 | 标签偏X:{3:F0}px" +
                    "\nShift+↑↓=卡高(全局) Shift+←→=卡宽 Ctrl+↑↓=隙 Ctrl+←→=卡偏X Ctrl+Shift+←→=标签X",
                    tw, _config.ShopCardHeightPct, tg, tl, _previewCardCount, _activeTier,
                    _activeTier <= 1 ? 4 : _activeTier <= 3 ? 5 : _activeTier <= 5 ? 6 : 7);
            }

            string info = string.Format(
                "F10校准 | 选中:{0} | 步长:{1}px\n" +
                "Shop偏移:({2:F0},{3:F0}) Board:({4:F0},{5:F0})\n" +
                "Tavern:({6:F0},{7:F0}) Refresh:({8:F0},{9:F0}) Hero:({10:F0},{11:F0})\n" +
                "Hand:({12:F0},{13:F0}){17}\n" +
                "客户区:{14:F0}x{15:F0} | S=保存 R=重置当前区{16}",
                _selectedZone, _step,
                _config.ShopOffsetX, _config.ShopOffsetY,
                _config.BoardOffsetX, _config.BoardOffsetY,
                _config.TavernOffsetX, _config.TavernOffsetY,
                _config.RefreshOffsetX, _config.RefreshOffsetY,
                _config.HeroPowerOffsetX, _config.HeroPowerOffsetY,
                _config.HandOffsetX, _config.HandOffsetY,
                snap.GameWidth, snap.GameHeight,
                saveHint,
                extraParams);

            var panel = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(210, 0, 0, 0)),
                BorderBrush = ParseBrush("#FFD700"), BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 4, 6, 4), IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = info, FontSize = 11, Foreground = Brushes.White,
                    FontFamily = new FontFamily("Consolas"),
                }
            };
            Add(canvas, panel, snap.GameWidth / 2 - 200, 6);
        }

        private void AddLine(Canvas canvas, double x1, double y1, double x2, double y2, SolidColorBrush b)
        {
            var line = new Line
            {
                X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
                Stroke = b, StrokeThickness = 1, IsHitTestVisible = false,
            };
            canvas.Children.Add(line); _elements.Add(line);
        }

        private void Add(Canvas canvas, UIElement elem, double x, double y)
        {
            Canvas.SetLeft(elem, x); Canvas.SetTop(elem, y);
            canvas.Children.Add(elem); _elements.Add(elem);
        }

        private static SolidColorBrush ParseBrush(string hex) => BrushHelper.ParseBrush(hex) ?? Brushes.White;

        private static int GetShopLayoutCardCount(int tavernTier)
        {
            int tier = Math.Max(1, Math.Min(6, tavernTier));
            return tier <= 1 ? 4 : (tier <= 3 ? 5 : (tier <= 5 ? 6 : 7));
        }

        private static void LogCalib(string msg)
        {
            try
            {
                var logPath = BobCoachDataPaths.GetPath("bob_coach.log");
                System.IO.File.AppendAllText(logPath,
                    string.Format("[{0:O}] [Calibration] {1}\n", DateTime.UtcNow, msg),
                    System.Text.Encoding.UTF8);
            }
            catch { }
        }
    }
}
