using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Hearthstone_Deck_Tracker.API;
using HearthDb.Enums;

namespace BobCoach
{
    /// <summary>
    /// V1.4: GameLayoutCalculator + PhaseDetector + F10校准模式
    /// </summary>
    public class OverlayRenderer
    {
        private readonly List<UIElement> _activeElements = new List<UIElement>();
        // 购买标签逐帧跟随: 记录每个标签元素+其 entityId+基准布局, 实体 ZONE_POSITION 漂移时仅平移(不重建=无闪烁)
        private readonly List<ShopTagElement> _shopTagElements = new List<ShopTagElement>();
        // 面板内容指纹: 相同内容不重建面板, 防每帧重绘导致的闪烁(0611 实测饰品/发现面板闪)
        private string _trinketPanelSig = null;
        private string _discoverPanelSig = null;
        private string _timewarpPurchaseSig = null;
        private Engine.GameLayoutCalculator _calc;
        private Engine.PhaseDetector _phase;
        private Engine.CalibrationOverlay _calib;

        private bool _positionLogged;
        public bool FadeInMode { get; set; }

        public OverlayRenderer()
        {
            _calc = new Engine.GameLayoutCalculator();
            _phase = new Engine.PhaseDetector();
            _calib = new Engine.CalibrationOverlay();
        }

        public void RefreshLayout()
        {
            _calc = new Engine.GameLayoutCalculator(Engine.LayoutConfig.Load());
            _calc.Refresh();
            RefreshCanvasSize();
            _positionLogged = false;
        }

        public void SetCalcTier(int tier)
        {
            _calc?.SetTier(tier);
        }

        /// <summary>同步画布尺寸到计算器(每局首次必须调用)</summary>
        public void RefreshCanvasSize()
        {
            try
            {
                var c = Core.OverlayCanvas;
                if (c != null && c.ActualWidth > 100)
                    _calc.RefreshWithSize(c.ActualWidth, c.ActualHeight);
            }
            catch { }
        }

        public void LogPositions()
        {
            if (_positionLogged) return;
            _positionLogged = true;
            try
            {
                var cw = _calc.ClientWidth;
                var ch = _calc.ClientHeight;
                var shop = _calc.GetShopArea();
                var board = _calc.GetBoardArea();
                var tavern = _calc.GetTavernButtonArea();
                var refresh = _calc.GetRefreshButtonArea();
                var canv = Hearthstone_Deck_Tracker.API.Core.OverlayCanvas;
                var sb = new System.Text.StringBuilder();
                sb.AppendFormat("POS: client={0}x{1} canvas={2:F0}x{3:F0} Tavern=({4:F0},{5:F0}) Refresh=({6:F0},{7:F0}) Shop=({8:F0},{9:F0}) Board=({10:F0},{11:F0})",
                    cw, ch, canv != null ? canv.ActualWidth : 0, canv != null ? canv.ActualHeight : 0,
                    tavern.Left, tavern.Top, refresh.Left, refresh.Top,
                    shop.Left, shop.Top, board.Left, board.Top);
                // 按实际卡数居中: 输出N=3和N=4的位置(最常用)
                sb.Append(" N3=");
                var r3 = _calc.GetShopCardRects(3);
                for (int i = 0; i < r3.Length; i++)
                    sb.AppendFormat("{0}{1}={2:F0}", i > 0 ? "," : "", i, r3[i].Left);
                sb.Append(" N4=");
                var r4 = _calc.GetShopCardRects(4);
                for (int i = 0; i < r4.Length; i++)
                    sb.AppendFormat("{0}{1}={2:F0}", i > 0 ? "," : "", i, r4[i].Left);
                RendererLog(sb.ToString());
            }
            catch { }
        }

        private static void RendererLog(string msg)
        {
            try
            {
                var dir = Engine.BobCoachDataPaths.Root;
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "bob_coach.log"),
                    string.Format("[{0:O}] [Renderer] {1}\n", DateTime.UtcNow, msg),
                    System.Text.Encoding.UTF8);
            }
            catch { }
        }

        /// <summary>每帧更新阶段检测</summary>
        public void UpdatePhase(int turn) { _phase.Update(turn); }
        public bool CanRender => _phase.CanRender;
        public bool CalibrationActive => _calib.Active;

        public void ActivateCalibration() { _calib.Activate(); }
        public void DeactivateCalibration() { _calib.Deactivate(); }
        public bool HandleCalibKey(Key key) { return _calib.HandleKey(key); }

        // ═══════════════════════════════════════
        // 商店卡片评分高亮 V2: 底部光束+地灯+▼箭头 (替代闭合边框)
        // ═══════════════════════════════════════

        public void ShowShopCardRating(int index, int actualCards, float score, string cardName, string reason,
            Engine.DecisionLevel level = Engine.DecisionLevel.Major, bool pulse = false,
            Engine.CardPurpose purpose = Engine.CardPurpose.Combat,
            Engine.QualityTier quality = Engine.QualityTier.None,
            bool isTriple = false, int tier = 1, int entityId = 0)
        {
            if (IsCombat()) return;
            var canvas = Core.OverlayCanvas;
            if (canvas == null || actualCards <= 0) return;
            // 诊断日志: 标签调用 + entityId追踪
            RendererLog(string.Format("ShopRating: idx={0} card={1} eid={2} actualCards={3} tier={4}",
                index, cardName, entityId, actualCards, tier));

            // index is the raw ShopPosition (ZONE_POSITION - 1). Layout count is the tavern slot count
            // widened by live max raw slot when necessary; do not compress to dense indices here.
            int livePosition = index;
            if (entityId > 0)
            {
                try
                {
                    if (Core.Game.Entities.TryGetValue(entityId, out var entity))
                    {
                        int curZone = entity.HasTag(GameTag.ZONE) ? entity.GetTag(GameTag.ZONE) : -1;
                        if (curZone != 1 && curZone != 5)
                            RendererLog(string.Format("ShopRating: eid={0} left zone (curZone={1}), keep idx={2}",
                                entityId, curZone, index));
                    }
                }
                catch { }
            }
            // 防御: index 越界时夹紧, 绝不传越界值给 GetShopCardRect(避免(0,0)兜底)
            if (livePosition < 0) livePosition = 0;
            if (livePosition >= actualCards) livePosition = actualCards - 1;

            // actualCards张卡居中排列: 奇数→中卡对中轴, 偶数→两中卡内边分列中轴两侧
            var r = _calc.GetShopCardRect(livePosition, actualCards);
            double left = r.Left, top = r.Top, cw = r.Width, ch = r.Height;
            double cx = left + cw / 2;
            // 诊断(问题1: 标签右偏2卡宽): 打印实际渲染坐标+卡宽+偏移, 下局定位偏移来源
            RendererLog(string.Format("ShopRectDiag: idx={0} livePos={1} actualCards={2} rectLeft={3:F0} cw={4:F0} cx={5:F0} labelOffX={6:F0}",
                index, livePosition, actualCards, left, cw, cx, _calc.ShopLabelOffsetX));
            int tagStartIdx = _activeElements.Count;  // 本次调用新增元素起点(用于逐帧跟随注册)

            // 质量评级配色: S=金, A=绿, B=蓝
            string qColorHex;
            if (quality == Engine.QualityTier.S) qColorHex = "#FFD700";
            else if (quality == Engine.QualityTier.A) qColorHex = "#00E676";
            else if (quality == Engine.QualityTier.B) qColorHex = "#64B5F6";
            else qColorHex = "#90A4AE";
            var qColor = ParseBrush(qColorHex);
            var qc = (Color?)ColorConverter.ConvertFromString(qColorHex) ?? Colors.Gray;

            double barW = cw * 0.96;
            double barH = 18;
            double labelOffX = _calc.ShopLabelOffsetX;
            double barLeft = left + (cw - barW) / 2 + labelOffX;
            double barTop = top + ch + 4;

            // ── 底部条背景 (深色半透明) ──
            var bgBar = new System.Windows.Shapes.Rectangle
            {
                Width = barW, Height = barH,
                RadiusX = 4, RadiusY = 4,
                Fill = new SolidColorBrush(Color.FromArgb(210, 12, 12, 14)),
                Stroke = new SolidColorBrush(Color.FromArgb(120, qc.R, qc.G, qc.B)),
                StrokeThickness = 1,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(bgBar, barLeft);
            Canvas.SetTop(bgBar, barTop);
            Panel.SetZIndex(bgBar, 995);
            canvas.Children.Add(bgBar); _activeElements.Add(bgBar);
            if (pulse) { AnimatePulse(bgBar, 0.70, 0.95, 1.5); }
            AnimateFadeOut(bgBar, 25.0);

            // ── 左侧: 用途图标 ──
            double iconSize = 12;
            double iconLeft = barLeft + 6;
            double iconTop = barTop + (barH - iconSize) / 2;
            var purposeIcon = CreatePurposeIcon(purpose, qColor, iconSize);
            if (purposeIcon != null)
            {
                Canvas.SetLeft(purposeIcon, iconLeft);
                Canvas.SetTop(purposeIcon, iconTop);
                Panel.SetZIndex(purposeIcon, 997);
                canvas.Children.Add(purposeIcon); _activeElements.Add(purposeIcon);
                AnimateFadeOut(purposeIcon, 25.0);
            }

            // ── 左侧: 推荐原因标签(细化) ──
            string tagText;
            string tagColor;
            if (isTriple) { tagText = "三连"; tagColor = "#FFD700"; }
            else if (purpose == Engine.CardPurpose.Core) { tagText = "核心"; tagColor = qColorHex; }
            else if (purpose == Engine.CardPurpose.Economy) { tagText = "资源"; tagColor = qColorHex; }
            else if (!string.IsNullOrEmpty(reason) && reason.Length <= 6) { tagText = reason; tagColor = qColorHex; }
            else if (tier >= 3) { tagText = "战力"; tagColor = qColorHex; }
            else { tagText = "过渡"; tagColor = qColorHex; }
            var tagBrush = ParseBrush(tagColor) ?? qColor;
            var purposeLabel = new TextBlock
            {
                Text = tagText, FontSize = 12, FontWeight = FontWeights.SemiBold,
                Foreground = tagBrush, IsHitTestVisible = false,
            };
            Canvas.SetLeft(purposeLabel, iconLeft + iconSize + 2);
            Canvas.SetTop(purposeLabel, barTop + 1);
            Panel.SetZIndex(purposeLabel, 997);
            canvas.Children.Add(purposeLabel); _activeElements.Add(purposeLabel);
            AnimateFadeOut(purposeLabel, 25.0);

            // ── 右侧: 质量评级 S/A/B ──
            if (quality != Engine.QualityTier.None)
            {
                string gradeText = quality.ToString();
                var gradeBgColor = new SolidColorBrush(Color.FromArgb(180, qc.R, qc.G, qc.B));

                var gradeBg = new System.Windows.Shapes.Rectangle
                {
                    Width = 18, Height = 15,
                    RadiusX = 3, RadiusY = 3,
                    Fill = gradeBgColor,
                    IsHitTestVisible = false,
                };
                Canvas.SetLeft(gradeBg, barLeft + barW - 22);
                Canvas.SetTop(gradeBg, barTop + 1.5);
                Panel.SetZIndex(gradeBg, 997);
                canvas.Children.Add(gradeBg); _activeElements.Add(gradeBg);
                AnimateFadeOut(gradeBg, 25.0);

                var gradeLabel = new TextBlock
                {
                    Text = gradeText, FontSize = 11, FontWeight = FontWeights.Bold,
                    Foreground = Brushes.Black, IsHitTestVisible = false,
                    TextAlignment = TextAlignment.Center,
                    Width = 18,
                };
                Canvas.SetLeft(gradeLabel, barLeft + barW - 22);
                Canvas.SetTop(gradeLabel, barTop + 1);
                Panel.SetZIndex(gradeLabel, 998);
                canvas.Children.Add(gradeLabel); _activeElements.Add(gradeLabel);
                AnimateFadeOut(gradeLabel, 25.0);
            }

            // ── 方向箭头(▼向下) ──
            double arrowW = 8, arrowH = 5;
            var arrow = new System.Windows.Shapes.Polygon
            {
                Points = new PointCollection { new Point(0, 0), new Point(arrowW, 0), new Point(arrowW / 2, arrowH) },
                Fill = qColor, Opacity = 0.80, IsHitTestVisible = false,
            };
            Canvas.SetLeft(arrow, cx - arrowW / 2);
            Canvas.SetTop(arrow, barTop + barH + 2);
            Panel.SetZIndex(arrow, 996);
            canvas.Children.Add(arrow); _activeElements.Add(arrow);
            AnimateFadeOut(arrow, 25.0);

            // ── 逐帧跟随注册: 本次新增的标签元素绑定 entityId+基准Left, OnUpdate 调 SyncShopTagPositions 平移 ──
            if (entityId > 0)
            {
                for (int ei = tagStartIdx; ei < _activeElements.Count; ei++)
                {
                    var el = _activeElements[ei];
                    _shopTagElements.Add(new ShopTagElement
                    {
                        EntityId = entityId,
                        El = el,
                        BaseLeft = Canvas.GetLeft(el),
                        BaseTop = Canvas.GetTop(el),
                        OffsetLeft = Canvas.GetLeft(el) - left,
                        OffsetTop = Canvas.GetTop(el) - top,
                        BasePosition = livePosition,
                        ActualCards = actualCards,
                    });
                }
            }
        }

        internal void ShowTimewarpPurchaseRating(int index, int candidateCount,
            string cardName, int purchaseCost, int timeCoinCount)
        {
            var canvas = Core.OverlayCanvas;
            if (canvas == null || candidateCount < 2 || index < 0 || index >= candidateCount)
                return;

            string signature = string.Format("{0}|{1}|{2}|{3}|{4}",
                index, candidateCount, cardName ?? "", purchaseCost, timeCoinCount);
            bool markerPresent = _activeElements.Any(element =>
                (element as FrameworkElement)?.Tag as string == "timewarp_purchase");
            if (signature == _timewarpPurchaseSig && markerPresent) return;
            ClearTimewarpPurchaseHint();
            _timewarpPurchaseSig = signature;

            var cardRect = _calc.GetShopCardRect(index, candidateCount);
            double barWidth = cardRect.Width * 0.96;
            double barHeight = 22;
            double barLeft = cardRect.Left + (cardRect.Width - barWidth) / 2;
            double barTop = cardRect.Top + cardRect.Height + 4;
            var accent = ParseBrush("#FFD700") ?? Brushes.Gold;

            var marker = new Border
            {
                Width = barWidth,
                Height = barHeight,
                CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(Color.FromArgb(225, 12, 12, 14)),
                BorderBrush = accent,
                BorderThickness = new Thickness(1.5),
                IsHitTestVisible = false,
                Tag = "timewarp_purchase",
                Child = new TextBlock
                {
                    Text = string.Format("★首选  {0}/{1}时空币", purchaseCost, timeCoinCount),
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Foreground = accent,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    IsHitTestVisible = false,
                },
            };
            Canvas.SetLeft(marker, barLeft);
            Canvas.SetTop(marker, barTop);
            Panel.SetZIndex(marker, 1004);
            canvas.Children.Add(marker);
            _activeElements.Add(marker);

            var arrow = new System.Windows.Shapes.Polygon
            {
                Points = new PointCollection
                {
                    new Point(0, 0), new Point(10, 0), new Point(5, 6),
                },
                Fill = accent,
                Opacity = 0.9,
                IsHitTestVisible = false,
                Tag = "timewarp_purchase",
            };
            Canvas.SetLeft(arrow, cardRect.Left + cardRect.Width / 2 - 5);
            Canvas.SetTop(arrow, barTop + barHeight + 2);
            Panel.SetZIndex(arrow, 1005);
            canvas.Children.Add(arrow);
            _activeElements.Add(arrow);

            RendererLog(string.Format(
                "TimewarpPurchaseRating: idx={0}/{1} card={2} cost={3} coins={4} rect=({5:F0},{6:F0})",
                index, candidateCount, cardName, purchaseCost, timeCoinCount,
                cardRect.Left, cardRect.Top));
        }

        internal void ClearTimewarpPurchaseHint()
        {
            ClearTaggedElements("timewarp_purchase");
            _timewarpPurchaseSig = null;
        }

        /// <summary>
        /// 逐帧位置同步: 遍历已注册的购买标签, 按提取层 ShopMinions 快照中的原始槽位平移(不重建)。
        /// 实体离店(被买/刷新)→淡出该组并移除注册。由 OnUpdate 末尾(非战斗+商店阶段)调用。
        /// 与 planHash 重绘互补: planHash 管"内容变化", 本方法管"纯位置漂移"。
        /// </summary>
        public void SyncShopTagPositions(List<Engine.MinionData> liveShopMinions, int tavernTier, bool denseReplenishingShop = false)
        {
            if (_shopTagElements.Count == 0) return;
            if (IsCombat()) return;
            var canvas = Core.OverlayCanvas;
            if (canvas == null) return;
            if (liveShopMinions == null || liveShopMinions.Count == 0) return;

            var stale = new List<ShopTagElement>();
            var positions = liveShopMinions
                .Where(m => m != null && m.EntityId > 0 && m.Position >= 0)
                .GroupBy(m => m.EntityId)
                .ToDictionary(g => g.Key, g => g.First().Position);
            // 07072158(确认B): 始终按实际在场卡数居中(与ShopRender一致); 设备差异由用户 ShopOffsetX 校准
            int layoutCount = Math.Max(1, Math.Min(7, liveShopMinions.Count));
            var denseSlots = liveShopMinions
                .OrderBy(m => m.Position)
                .ThenBy(m => m.EntityId)
                .Select((m, idx) => new { m.EntityId, DenseSlot = idx })
                .ToDictionary(x => x.EntityId, x => x.DenseSlot);

            foreach (var tag in _shopTagElements)
            {
                if (!_activeElements.Contains(tag.El)) { stale.Add(tag); continue; }
                int rawSlot;
                if (!positions.TryGetValue(tag.EntityId, out rawSlot))
                {
                    stale.Add(tag);
                    continue;
                }

                try
                {
                    if (denseSlots != null)
                    {
                        int denseSlot;
                        if (denseSlots.TryGetValue(tag.EntityId, out denseSlot))
                            rawSlot = denseSlot;
                    }
                    if (rawSlot < 0) rawSlot = 0;
                    if (rawSlot >= layoutCount) rawSlot = layoutCount - 1;
                    var target = _calc.GetShopCardRect(rawSlot, layoutCount);
                    Canvas.SetLeft(tag.El, target.Left + tag.OffsetLeft);
                    Canvas.SetTop(tag.El, target.Top + tag.OffsetTop);
                    tag.BasePosition = rawSlot;
                    tag.ActualCards = layoutCount;
                }
                catch { stale.Add(tag); }
            }

            foreach (var s in stale)
            {
                try { AnimateFadeOut(s.El, 0.10); } catch { }
                _shopTagElements.Remove(s);
            }
        }

        /// <summary>创建用途图标: 战力=双剑交叉, 资源=钱袋, 核心=钻石</summary>
        private System.Windows.Shapes.Path CreatePurposeIcon(Engine.CardPurpose purpose, Brush color, double size)
        {
            string data;
            switch (purpose)
            {
                case Engine.CardPurpose.Combat:
                    // 双剑交叉: 两条交叉对角线
                    data = string.Format("M{0},{1} L{2},{3} M{2},{1} L{0},{3}",
                        2, 2, size - 2, size - 2);
                    break;
                case Engine.CardPurpose.Economy:
                    // 钱袋: 圆形+顶部系绳
                    data = string.Format("M{0},{1} A{2},{3} 0 1,1 {4},{5} A{2},{3} 0 0,1 {0},{1} M{6},{1} L{7},{1}",
                        size / 2 - 3, 5, 3, 3, size / 2 - 3, size - 3, size / 2 - 2, size / 2 + 2);
                    break;
                default:
                    // 钻石: 菱形
                    data = string.Format("M{0},{1} L{2},{3} L{0},{4} L{5},{3} Z",
                        size / 2, 1, size - 1, size / 2, size - 1, 1);
                    break;
            }
            var geo = System.Windows.Media.Geometry.Parse(data);
            return new System.Windows.Shapes.Path
            {
                Data = geo, Stroke = color, StrokeThickness = 1.8,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                IsHitTestVisible = false,
            };
        }

        /// <summary>脉冲呼吸动画辅助</summary>
        private void AnimatePulse(UIElement element, double from, double to, double durationSec)
        {
            var a = new DoubleAnimation
            {
                From = from, To = to,
                Duration = new Duration(TimeSpan.FromSeconds(durationSec)),
                AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
            };
            element.BeginAnimation(UIElement.OpacityProperty, a);
        }

        // ═══════════════════════════════════════
        // 升本按钮光晕
        // ═══════════════════════════════════════

        public void ShowLevelUpGlow(bool urgent, string reason, int cost, int currentTier, bool tier7Enabled = false)
        {
            if (IsCombat()) return;
            var canvas = Core.OverlayCanvas;
            if (canvas == null) return;

            ClearTaggedElements("levelup_glow");

            var btn = _calc.GetTavernButtonArea();
            double badgeW = _calc.ClientWidth * 0.052, badgeH = badgeW * 0.80;
            double x = btn.Left + btn.Width / 2 - badgeW / 2, y = btn.Top - badgeH, w = badgeW, h = badgeH;

            string color = urgent ? "#FFD700" : "#90CAF9";
            double border = urgent ? 3 : 1.5;

            var panel = new Border
            {
                Width = w, Height = h, CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromArgb(urgent ? (byte)60 : (byte)25, 0, 0, 0)),
                BorderBrush = ParseBrush(color), BorderThickness = new Thickness(border), IsHitTestVisible = false,
            };
            var tb = new TextBlock
            {
                Text = urgent ? ("Lv" + (tier7Enabled && currentTier >= 6 ? 7 : currentTier + 1)) : (tier7Enabled && currentTier >= 6 ? "Lv7" : "Lv^"),
                FontSize = urgent ? 20 : 14, FontWeight = FontWeights.Bold,
                Foreground = ParseBrush(color),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false,
            };
            var costLabel = new TextBlock
            {
                Text = cost + "费", FontSize = 12, Foreground = ParseBrush("#BDBDBD"),
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 2),
                IsHitTestVisible = false,
            };

            var stack = new StackPanel { IsHitTestVisible = false };
            stack.Children.Add(tb); stack.Children.Add(costLabel);

            var wrapper = new Border
            {
                Width = w, Height = h, CornerRadius = panel.CornerRadius,
                Background = panel.Background, BorderBrush = panel.BorderBrush,
                BorderThickness = panel.BorderThickness,
                Child = stack,
                IsHitTestVisible = true,
                Cursor = Cursors.Help,
                Tag = "levelup_glow",
            };
            Canvas.SetLeft(wrapper, x); Canvas.SetTop(wrapper, y);
            Panel.SetZIndex(wrapper, 1002);
            canvas.Children.Add(wrapper); _activeElements.Add(wrapper);

            // 升本原因文字直接显示在下方
            if (!string.IsNullOrEmpty(reason))
            {
                var reasonLabel = new TextBlock
                {
                    Text = reason, FontSize = 11,
                    Foreground = ParseBrush(color),
                    Background = new SolidColorBrush(Color.FromArgb(160, 10, 10, 10)),
                    Padding = new Thickness(2, 1, 2, 1),
                    IsHitTestVisible = false,
                    MaxWidth = _calc.ScaleX(200),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                Canvas.SetLeft(reasonLabel, x - 40);
                Canvas.SetTop(reasonLabel, y + h + 2);
                Panel.SetZIndex(reasonLabel, 1003);
                canvas.Children.Add(reasonLabel); _activeElements.Add(reasonLabel);
                AnimateFadeOut(reasonLabel, 8.0); // 文字随灯塔一起淡出
            }

            if (urgent)
            {
                var a = new DoubleAnimation
                {
                    From = 0.6, To = 1.0, Duration = new Duration(TimeSpan.FromSeconds(1.2)),
                    AutoReverse = true, RepeatBehavior = new RepeatBehavior(5),
                };
                wrapper.BeginAnimation(UIElement.OpacityProperty, a);
                a.Completed += (s, e) => { wrapper.Opacity = 0.8; };
            }
            // 8秒自动淡出, 避免金币花完后提示残留
            AnimateFadeOut(wrapper, 8.0);
        }

        /// <summary>刷新按钮提示</summary>
        public void ShowRefreshGlow(bool urgent, string reason, bool noRefresh = false)
        {
            if (IsCombat() || noRefresh) return;
            var canvas = Core.OverlayCanvas;
            if (canvas == null) return;

            ClearTaggedElements("refresh_glow");

            var btn = _calc.GetRefreshButtonArea();
            double badgeW = _calc.ClientWidth * 0.042, badgeH = badgeW * 0.80;
            double x = btn.Left + btn.Width / 2 - badgeW / 2, y = btn.Top - badgeH, w = badgeW, h = badgeH;

            var wrapper = new Border
            {
                Width = w, Height = h, CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromArgb(25, 0, 0, 0)),
                BorderBrush = ParseBrush("#FF9800"), BorderThickness = new Thickness(1.5),
                Child = new TextBlock
                {
                    Text = urgent ? "找!" : "刷",
                    FontSize = urgent ? 20 : 16,
                    FontWeight = FontWeights.Bold,
                    Foreground = ParseBrush(urgent ? "#FF5722" : "#FF9800"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
                IsHitTestVisible = false,
                Tag = "refresh_glow",
            };
            Canvas.SetLeft(wrapper, x); Canvas.SetTop(wrapper, y);
            Panel.SetZIndex(wrapper, 1001);
            canvas.Children.Add(wrapper); _activeElements.Add(wrapper);

            // 刷新原因文字（加大字号）
            if (!string.IsNullOrEmpty(reason))
            {
                var reasonLabel = new TextBlock
                {
                    Text = reason, FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Foreground = ParseBrush("#FF9800"),
                    Background = new SolidColorBrush(Color.FromArgb(180, 10, 10, 10)),
                    Padding = new Thickness(4, 2, 4, 2),
                    IsHitTestVisible = false,
                    MaxWidth = _calc.ScaleX(200),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                Canvas.SetLeft(reasonLabel, x - 30);
                Canvas.SetTop(reasonLabel, y + h + 2);
                Panel.SetZIndex(reasonLabel, 1002);
                canvas.Children.Add(reasonLabel); _activeElements.Add(reasonLabel);
            }
        }

        /// <summary>冻结提示: 推荐冻结商店时显示, 位置在刷新按钮右侧, 蓝色调</summary>
        public void ShowFreezeGlow(bool urgent, string reason)
        {
            if (IsCombat()) return;
            var canvas = Core.OverlayCanvas;
            if (canvas == null) return;

            ClearTaggedElements("freeze_glow");

            var btn = _calc.GetFreezeButtonArea();
            double badgeW = _calc.ClientWidth * 0.042, badgeH = badgeW * 0.80;
            double x = btn.Left + btn.Width / 2 - badgeW / 2, y = btn.Top - badgeH, w = badgeW, h = badgeH;

            var wrapper = new Border
            {
                Width = w, Height = h, CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromArgb(25, 0, 0, 0)),
                BorderBrush = ParseBrush("#03A9F4"), BorderThickness = new Thickness(1.5),
                Child = new TextBlock
                {
                    Text = urgent ? "锁!" : "冻",
                    FontSize = urgent ? 20 : 16,
                    FontWeight = FontWeights.Bold,
                    Foreground = ParseBrush(urgent ? "#00BCD4" : "#03A9F4"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
                IsHitTestVisible = false,
                Tag = "freeze_glow",
            };
            Canvas.SetLeft(wrapper, x); Canvas.SetTop(wrapper, y);
            Panel.SetZIndex(wrapper, 1001);
            canvas.Children.Add(wrapper); _activeElements.Add(wrapper);

            if (!string.IsNullOrEmpty(reason))
            {
                var reasonLabel = new TextBlock
                {
                    Text = reason, FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Foreground = ParseBrush("#03A9F4"),
                    Background = new SolidColorBrush(Color.FromArgb(180, 10, 10, 10)),
                    Padding = new Thickness(4, 2, 4, 2),
                    IsHitTestVisible = false,
                    MaxWidth = _calc.ScaleX(200),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                Canvas.SetLeft(reasonLabel, x - 30);
                Canvas.SetTop(reasonLabel, y + h + 2);
                Panel.SetZIndex(reasonLabel, 1002);
                canvas.Children.Add(reasonLabel); _activeElements.Add(reasonLabel);
            }
        }

        // ═══════════════════════════════════════
        // 顶部状态条
        // ═══════════════════════════════════════

        private bool IsCombat()
        {
            // 双保险: HDT API + PhaseDetector, 任一判定为战斗/非招募即屏蔽渲染
            try
            {
                if (Hearthstone_Deck_Tracker.API.Core.Game != null
                    && Hearthstone_Deck_Tracker.API.Core.Game.IsBattlegroundsCombatPhase)
                    return true;
            }
            catch { }
            return !_phase.CanRender;
        }

        public void ShowStatusStrip(Engine.StatusInfo s)
        {
            if (IsCombat()) return;
            var canvas = Core.OverlayCanvas;
            if (canvas == null) return;

            // 清除旧状态栏防重叠: 多路径(RefreshPersistentUI/DispatchRender)可能重复调用
            ClearTaggedElements("status_strip");

            string paceLabel = s.Pace == "Sprint" ? "冲" : s.Pace == "Cruise" ? "稳" : s.Pace == "Conserve" ? "守" : s.Pace == "AllIn" ? "赌" : s.Pace;
            string turnStr = int.TryParse(s.Phase, out var t) ? ("回合" + t) : s.Phase;
            string compStr = "";
            if (!string.IsNullOrEmpty(s.LockIcon) && !string.IsNullOrEmpty(s.CompDir))
                compStr = "推荐" + s.CompDir;

            string line1 = string.IsNullOrEmpty(compStr)
                ? string.Format("{0} | {1}", turnStr, paceLabel)
                : string.Format("{0} | {1} | {2}", turnStr, paceLabel, compStr);

            var stack = new StackPanel { IsHitTestVisible = false, Tag = "status_strip" };
            var bgBrush = new SolidColorBrush(Color.FromArgb(160, 10, 10, 10));
            double statusMaxWidth = _calc.ScaleX(260);
            double statusWideMaxWidth = _calc.ScaleX(280);

            var row1 = new TextBlock
            {
                Text = line1, FontSize = 13, FontWeight = FontWeights.Medium,
                Foreground = s.IsDesperate ? ParseBrush("#FF7043") : ParseBrush("#E0E0E0"),
                Background = bgBrush,
                Padding = new Thickness(6, 1, 6, 0), IsHitTestVisible = false,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = statusMaxWidth,
                LineHeight = 18,
            };
            stack.Children.Add(row1);

            // 畸变首购免费提示
            if (s.ShowFirstBuyFree)
            {
                var rowFF = new TextBlock
                {
                    Text = "首购免费", FontSize = 12, FontWeight = FontWeights.Medium,
                    Foreground = ParseBrush("#FFD700"),
                    Background = bgBrush,
                    Padding = new Thickness(6, 0, 6, 0), IsHitTestVisible = false,
                };
                stack.Children.Add(rowFF);
            }

            // 绝望警告
            if (s.IsDesperate)
            {
                var warnRow = new TextBlock
                {
                    Text = "战力鸿沟! 找科技卡!",
                    FontSize = 12, FontWeight = FontWeights.Bold,
                    Foreground = ParseBrush("#FF7043"),
                    Background = bgBrush,
                    Padding = new Thickness(6, 0, 6, 0), IsHitTestVisible = false,
                    TextWrapping = TextWrapping.Wrap, MaxWidth = statusMaxWidth,
                };
                stack.Children.Add(warnRow);
            }

            // 行3: 动作序列 (浅黄)
            if (!string.IsNullOrEmpty(s.HintLine))
            {
                var row3 = new TextBlock
                {
                    Text = s.HintLine, FontSize = 13, FontWeight = FontWeights.Bold,
                    Foreground = ParseBrush("#FFFACD"),
                    Background = new SolidColorBrush(Color.FromArgb(180, 10, 10, 10)),
                    Padding = new Thickness(6, 0, 6, 1), IsHitTestVisible = false,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = statusWideMaxWidth,
                    LineHeight = 19,
                };
                stack.Children.Add(row3);
            }
            // 行4: 升级提示 (独立一行, 绿/金色, 不与动作序列重叠)
            if (!string.IsNullOrEmpty(s.UpgradeLine))
            {
                var upgradeRow = new TextBlock
                {
                    Text = s.UpgradeLine, FontSize = 13, FontWeight = FontWeights.Bold,
                    Foreground = ParseBrush("#69F0AE"),  // 绿色
                    Background = new SolidColorBrush(Color.FromArgb(180, 10, 10, 10)),
                    Padding = new Thickness(6, 0, 6, 1), IsHitTestVisible = false,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = statusWideMaxWidth,
                    LineHeight = 19,
                };
                stack.Children.Add(upgradeRow);
            }
            // 行5: 战斗预测 (橙色 — BobsBuddy胜率)
            if (!string.IsNullOrEmpty(s.CombatLine))
            {
                var combatRow = new TextBlock
                {
                    Text = s.CombatLine, FontSize = 12, FontWeight = FontWeights.Medium,
                    Foreground = ParseBrush("#FFAB40"),
                    Background = new SolidColorBrush(Color.FromArgb(180, 10, 10, 10)),
                    Padding = new Thickness(6, 0, 6, 1), IsHitTestVisible = false,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = statusWideMaxWidth,
                    LineHeight = 18,
                };
                stack.Children.Add(combatRow);
            }
            // 行6: 饰品/发现 (金/天蓝)
            if (!string.IsNullOrEmpty(s.PickLine))
            {
                bool isTrinket = s.PickLine.StartsWith("饰品:");
                string pickColor = isTrinket ? "#FFD700" : "#87CEEB";
                var row4 = new TextBlock
                {
                    Text = s.PickLine, FontSize = 13, FontWeight = FontWeights.Bold,
                    Foreground = ParseBrush(pickColor),
                    Background = new SolidColorBrush(Color.FromArgb(180, 10, 10, 10)),
                    Padding = new Thickness(6, 0, 6, 2), IsHitTestVisible = false,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = statusWideMaxWidth,
                    LineHeight = 19,
                };
                stack.Children.Add(row4);
            }

            var pos = _calc.GetStatusStripPosition();
            Canvas.SetLeft(stack, pos.X);
            Canvas.SetTop(stack, pos.Y);
            Panel.SetZIndex(stack, 997);
            canvas.Children.Add(stack); _activeElements.Add(stack);
        }

        // ═══════════════════════════════════════
        // 饰品推荐提示 (右侧浮动面板)
        // ═══════════════════════════════════════

        public void ShowTrinketHints(System.Collections.Generic.List<Engine.TrinketHint> hints)
        {
            if (IsCombat()) return;
            var canvas = Core.OverlayCanvas;
            if (canvas == null || hints == null || hints.Count == 0) return;

            // 幂等防闪: 内容指纹不变 且 面板仍在画布上 → 跳过重建(每帧重绘不再闪)
            string sig = string.Join("|", hints.Select(h => (h.Name ?? "?") + (h.IsTopPick ? "★" : "") + (h.Reason ?? "")));
            bool panelPresent = _activeElements.Any(el => (el as FrameworkElement)?.Tag as string == "trinket_panel");
            if (sig == _trinketPanelSig && panelPresent) return;
            // 内容变了或面板丢失 → 先清旧面板再重建(防叠加)
            ClearTrinketHints();
            _trinketPanelSig = sig;
            // 07071644: 面板位置/缩放由 F10 校准[9]配置(PanelOffsetX/Y/Scale), 屏幕左侧避开中央三选一交互区。
            // 基准坐标(1920×1080)经 ScaleX/Y 自适应任意分辨率; PanelScale 由 stack 的 ScaleTransform 统一缩放(字号+宽度)。
            double sc = _calc.Config.PanelScale > 0.3 ? _calc.Config.PanelScale : 1.0;
            double panelTextWidth = _calc.ScaleX(360);
            double x = _calc.Config.ScaleX(_calc.Config.PanelOffsetX, _calc.ClientWidth);
            double y = _calc.Config.ScaleY(_calc.Config.PanelOffsetY, _calc.ClientHeight);
            RendererLog(string.Format("ShowTrinketHints: {0} hints at ({1:F0},{2:F0})", hints.Count, x, y));

            var stack = new StackPanel { IsHitTestVisible = false, Tag = "trinket_panel" };
            stack.RenderTransform = new ScaleTransform(sc, sc); // 07071644: PanelScale 统一缩放(左上角为原点)
            var header = new TextBlock
            {
                Text = "⚑ Bob教练 · 饰品推荐",
                FontSize = 16, FontWeight = FontWeights.Bold,
                Foreground = ParseBrush("#FFD700"),
                Background = new SolidColorBrush(Color.FromArgb(200, 10, 10, 10)),
                Padding = new Thickness(10, 4, 10, 3),
                IsHitTestVisible = false,
                TextAlignment = TextAlignment.Center,
                Width = panelTextWidth,
            };
            stack.Children.Add(header);

            for (int i = 0; i < hints.Count; i++)
            {
                var h = hints[i];
                // 序号=推荐排名, 非游戏槽位
                string line = string.Format("{0}. {1}", i + 1, h.Name ?? "?");
                if (h.IsTopPick) line += " ★首选";
                if (!string.IsNullOrEmpty(h.Reason)) line += "  [" + h.Reason + "]";
                var row = new TextBlock
                {
                    Text = line,
                    FontSize = h.IsTopPick ? 18 : 15,
                    FontWeight = h.IsTopPick ? FontWeights.Bold : FontWeights.Normal,
                    Foreground = h.IsTopPick ? ParseBrush("#FFD700") : ParseBrush("#CFCFCF"),
                    Background = new SolidColorBrush(Color.FromArgb(185, 10, 10, 10)),
                    Padding = new Thickness(10, 2, 10, 2),
                    IsHitTestVisible = false,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = panelTextWidth,
                    Width = panelTextWidth,
                    TextAlignment = TextAlignment.Center,
                };
                stack.Children.Add(row);
            }

            Canvas.SetLeft(stack, x);
            Canvas.SetTop(stack, y);
            Panel.SetZIndex(stack, 996);
            canvas.Children.Add(stack);
            _activeElements.Add(stack);
            RendererLog(string.Format(
                "PanelPresence: tag=trinket_panel attached={0} visible={1} opacity={2:F2} z={3} bounds=({4:F0},{5:F0},{6:F0})",
                canvas.Children.Contains(stack), stack.Visibility, stack.Opacity,
                Panel.GetZIndex(stack), x, y, panelTextWidth * sc));
        }

        public void ShowTrinketLoading(string message)
        {
            if (IsCombat()) return;
            var canvas = Core.OverlayCanvas;
            if (canvas == null) return;

            string text = string.IsNullOrEmpty(message) ? "饰品候选读取中" : message;
            string sig = "loading|" + text;
            bool panelPresent = _activeElements.Any(el => (el as FrameworkElement)?.Tag as string == "trinket_panel");
            if (sig == _trinketPanelSig && panelPresent) return;

            ClearTrinketHints();
            _trinketPanelSig = sig;
            // 07071644: 与 ShowTrinketHints 同位/同缩放(F10校准[9]配置)
            double sc = _calc.Config.PanelScale > 0.3 ? _calc.Config.PanelScale : 1.0;
            double panelTextWidth = _calc.ScaleX(300);
            double x = _calc.Config.ScaleX(_calc.Config.PanelOffsetX, _calc.ClientWidth);
            double y = _calc.Config.ScaleY(_calc.Config.PanelOffsetY, _calc.ClientHeight);
            RendererLog(string.Format("ShowTrinketLoading: {0} at ({1:F0},{2:F0})", text, x, y));

            var stack = new StackPanel { IsHitTestVisible = false, Tag = "trinket_panel" };
            stack.RenderTransform = new ScaleTransform(sc, sc); // 07071644: PanelScale 统一缩放
            stack.Children.Add(new TextBlock
            {
                Text = "⚑ Bob教练 · 饰品选择",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = ParseBrush("#FFD700"),
                Background = new SolidColorBrush(Color.FromArgb(200, 10, 10, 10)),
                Padding = new Thickness(10, 4, 10, 3),
                IsHitTestVisible = false,
                TextAlignment = TextAlignment.Center,
                Width = panelTextWidth,
            });
            stack.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 15,
                Foreground = ParseBrush("#CFCFCF"),
                Background = new SolidColorBrush(Color.FromArgb(185, 10, 10, 10)),
                Padding = new Thickness(10, 2, 10, 2),
                IsHitTestVisible = false,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = panelTextWidth,
                Width = panelTextWidth,
                TextAlignment = TextAlignment.Center,
            });

            Canvas.SetLeft(stack, x);
            Canvas.SetTop(stack, y);
            Panel.SetZIndex(stack, 996);
            canvas.Children.Add(stack);
            _activeElements.Add(stack);
        }

        // ═══════════════════════════════════════
        // 发现选择推荐 (3选1提示)
        // ═══════════════════════════════════════

        public void ShowDiscoverHints(System.Collections.Generic.List<Engine.TrinketHint> hints,
            int statusBarLines = 0)
        {
            if (IsCombat()) return;
            RenderDiscoverHints(hints, statusBarLines);
        }

        internal void ShowAuthoritativeDiscoverHints(
            System.Collections.Generic.List<Engine.TrinketHint> hints,
            int statusBarLines = 0)
        {
            RenderDiscoverHints(hints, statusBarLines);
        }

        private void RenderDiscoverHints(System.Collections.Generic.List<Engine.TrinketHint> hints,
            int statusBarLines)
        {
            var canvas = Core.OverlayCanvas;
            if (canvas == null || hints == null || hints.Count == 0) return;

            // 幂等防闪: 内容指纹不变 且 面板仍在 → 跳过重建
            string sig = string.Join("|", hints.Select(h => (h.Name ?? "?") + (h.IsTopPick ? "★" : "") + (h.Reason ?? "")));
            bool panelPresent = _activeElements.Any(el => (el as FrameworkElement)?.Tag as string == "discover_panel");
            if (sig == _discoverPanelSig && panelPresent) return;
            ClearDiscoverHints();
            _discoverPanelSig = sig;

            // 07071644: 发现面板同走 F10校准[9]配置(PanelOffsetX/Y/Scale), 屏幕左侧
            double sc = _calc.Config.PanelScale > 0.3 ? _calc.Config.PanelScale : 1.0;
            double panelTextWidth = _calc.ScaleX(360);
            double x = _calc.Config.ScaleX(_calc.Config.PanelOffsetX, _calc.ClientWidth);
            double y = _calc.Config.ScaleY(_calc.Config.PanelOffsetY, _calc.ClientHeight);
            if (statusBarLines >= 4) y += (statusBarLines - 3) * _calc.ScaleY(22); // 防覆盖状态栏

            RendererLog(string.Format("ShowDiscoverHints: {0} hints at ({1},{2})",
                hints.Count, (int)x, (int)y));

            var stack = new StackPanel { IsHitTestVisible = false, Tag = "discover_panel" };
            stack.RenderTransform = new ScaleTransform(sc, sc); // 07071644: PanelScale 统一缩放
            var header = new TextBlock
            {
                Text = "⚑ Bob教练 · 发现推荐",
                FontSize = 16, FontWeight = FontWeights.Bold,
                Foreground = ParseBrush("#87CEEB"),
                Background = new SolidColorBrush(Color.FromArgb(200, 10, 10, 10)),
                Padding = new Thickness(10, 4, 10, 3),
                IsHitTestVisible = false,
                TextAlignment = TextAlignment.Center,
                Width = panelTextWidth,
            };
            stack.Children.Add(header);

            for (int i = 0; i < hints.Count; i++)
            {
                var h = hints[i];
                // 序号=推荐排名
                string line = string.Format("{0}. {1}", i + 1, h.Name ?? "?");
                if (h.IsTopPick) line += " ★首选";
                if (!string.IsNullOrEmpty(h.Reason)) line += "  [" + h.Reason + "]";
                var row = new TextBlock
                {
                    Text = line,
                    FontSize = h.IsTopPick ? 18 : 15,
                    FontWeight = h.IsTopPick ? FontWeights.Bold : FontWeights.Normal,
                    Foreground = h.IsTopPick ? ParseBrush("#87CEEB") : ParseBrush("#CFCFCF"),
                    Background = new SolidColorBrush(Color.FromArgb(185, 10, 10, 10)),
                    Padding = new Thickness(10, 2, 10, 2),
                    IsHitTestVisible = false,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = panelTextWidth,
                    Width = panelTextWidth,
                    TextAlignment = TextAlignment.Center,
                };
                stack.Children.Add(row);
            }

            Canvas.SetLeft(stack, x);
            Canvas.SetTop(stack, y);
            Panel.SetZIndex(stack, 996);
            canvas.Children.Add(stack);
            _activeElements.Add(stack);
        }

        public void ClearTrinketHints()
        {
            var canvas = Core.OverlayCanvas;
            if (canvas == null) return;
            int removed = 0;
            var snapshot = new List<UIElement>(_activeElements);
            foreach (var el in snapshot)
            {
                if (el is StackPanel sp && (sp.Tag as string) == "trinket_panel")
                {
                    canvas.Children.Remove(el);
                    _activeElements.Remove(el);
                    removed++;
                }
            }
            _trinketPanelSig = null;  // 清除后指纹归零, 下次同内容也会重建
            if (removed > 0) RendererLog(string.Format("ClearTrinketHints: removed={0}", removed));
        }

        public void ClearDiscoverHints()
        {
            var canvas = Core.OverlayCanvas;
            if (canvas == null) return;
            int removed = 0;
            var snapshot = new List<UIElement>(_activeElements);
            foreach (var el in snapshot)
            {
                if (el is StackPanel sp && (sp.Tag as string) == "discover_panel")
                {
                    canvas.Children.Remove(el);
                    _activeElements.Remove(el);
                    removed++;
                }
            }
            _discoverPanelSig = null;
            if (removed > 0) RendererLog(string.Format("ClearDiscoverHints: removed={0}", removed));
        }

        // ═══════════════════════════════════════
        // 卖牌标记 V2: 红色底部光束+▲向上箭头 (暗示"向上拖拽出售")
        // ═══════════════════════════════════════

        public void ShowBoardHighlight(int boardIndex, int boardCount, string colorHex, string tooltip)
        {
            if (IsCombat()) return;
            var canvas = Core.OverlayCanvas;
            if (canvas == null || boardCount <= 0) return;

            var r = _calc.GetBoardCardRect(boardIndex, boardCount);
            double left = r.Left, top = r.Top, cw = r.Width, ch = r.Height;
            double cx = left + cw / 2;
            string color = colorHex ?? "#FF5252";
            var brush = ParseBrush(color) ?? Brushes.Red;

            // ── 底部光束(红色): 随从下方 ──
            var beam = new Rectangle
            {
                Width = cw * 0.7, Height = 3,
                RadiusX = 1.5, RadiusY = 1.5,
                Fill = brush,
                Opacity = 0.75,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(beam, cx - cw * 0.35);
            Canvas.SetTop(beam, top + ch + 4);
            Panel.SetZIndex(beam, 996);
            canvas.Children.Add(beam); _activeElements.Add(beam);
            AnimateFadeOut(beam, 25.0);

            // ── 方向箭头(▲向上): 光束上方, 暗示"向上拖拽出售" ──
            double arrowW = 10, arrowH = 6;
            var arrow = new System.Windows.Shapes.Polygon
            {
                Points = new PointCollection { new Point(arrowW / 2, 0), new Point(arrowW, arrowH), new Point(0, arrowH) },
                Fill = brush,
                Opacity = 0.85,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(arrow, cx - arrowW / 2);
            Canvas.SetTop(arrow, top + ch + 9);
            Panel.SetZIndex(arrow, 997);
            canvas.Children.Add(arrow); _activeElements.Add(arrow);
            AnimateFadeOut(arrow, 25.0);

            // ── 文字标签 ──
            if (!string.IsNullOrEmpty(tooltip))
            {
                var label = new TextBlock
                {
                    Text = tooltip, FontSize = 12,
                    Foreground = brush,
                    Background = new SolidColorBrush(Color.FromArgb(140, 10, 10, 10)),
                    Padding = new Thickness(3, 1, 3, 1),
                    IsHitTestVisible = false,
                };
                Canvas.SetLeft(label, left - 5);
                Canvas.SetTop(label, top + ch + 17);
                Panel.SetZIndex(label, 997);
                canvas.Children.Add(label); _activeElements.Add(label);
                AnimateFadeOut(label, 25.0);
            }
        }

        // ═══════════════════════════════════════
        // 校准模式渲染
        // ═══════════════════════════════════════

        public void RenderCalibration()
        {
            var canvas = Core.OverlayCanvas;
            if (canvas == null) return;
            if (_calib.Active)
                _calib.Render(canvas);
            else
                _calib.Clear();
        }

        // ═══════════════════════════════════════
        // 降级/兜底
        // ═══════════════════════════════════════

        public void ShowSuggestionBadge(string text, string colorHex, string tooltip)
        {
            if (IsCombat()) return;
            var canvas = Core.OverlayCanvas;
            if (canvas == null) return;
            double w = canvas.ActualWidth > 0 ? canvas.ActualWidth : 1920;
            double rightMargin = _calc.ScaleX(16);
            double availableWidth = Math.Max(0, w - rightMargin * 2);
            var tb = new TextBlock
            {
                Text = text, FontSize = 14, FontWeight = FontWeights.Bold,
                Foreground = ParseBrush(colorHex) ?? Brushes.LimeGreen, IsHitTestVisible = false,
                MaxWidth = availableWidth,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
            };
            if (!string.IsNullOrEmpty(tooltip)) tb.ToolTip = new ToolTip { Content = tooltip };
            tb.Measure(new Size(availableWidth, double.PositiveInfinity));
            Canvas.SetLeft(tb, Math.Max(0, w - tb.DesiredSize.Width - rightMargin));
            Canvas.SetTop(tb, _calc.ScaleY(50));
            Panel.SetZIndex(tb, 996);
            canvas.Children.Add(tb); _activeElements.Add(tb);
            AnimateFadeOut(tb, 8.0);
        }

        private List<UIElement> _heroGlowElements = new List<UIElement>();
        private UIElement _handMarkerElement;  // 单独跟踪手牌标记元素以支持清除

        /// <summary>英雄技能提示: 仅主动技能+建议使用时显示浅黄脉冲环</summary>
        public void ShowHeroHint(string hintText, string reason, string useSuggestion = null, string hpLabel = null)
        {
            if (IsCombat()) return;
            ClearHeroGlow();
            if (string.IsNullOrEmpty(useSuggestion)) return;
            var canvas = Core.OverlayCanvas;
            if (canvas == null) return;

            var hpArea = _calc.GetHeroPowerArea();
            double cx = hpArea.Left + hpArea.Width / 2;
            double cy = hpArea.Top + hpArea.Height / 2;
            double r = Math.Max(hpArea.Width, hpArea.Height) * 0.6;

            // 文字标签: 仅第二技能畸变时显示"主技能"/"第二技能"区分
            if (!string.IsNullOrEmpty(hpLabel))
            {
                var label = new TextBlock
                {
                    Text = hpLabel,
                    FontSize = 11, FontWeight = FontWeights.Bold,
                    Foreground = ParseBrush("#FFFACD"),
                    Background = new SolidColorBrush(Color.FromArgb(200, 10, 10, 14)),
                    Padding = new Thickness(4, 1, 4, 1),
                    IsHitTestVisible = false,
                };
                Canvas.SetLeft(label, cx - 28);
                Canvas.SetTop(label, hpArea.Top - 20);
                Panel.SetZIndex(label, 1001);
                canvas.Children.Add(label); _activeElements.Add(label);
                _heroGlowElements.Add(label);
            }

            var ring = new System.Windows.Shapes.Ellipse
            {
                Width = r * 2, Height = r * 2,
                Stroke = ParseBrush("#FFFACD"), StrokeThickness = 3,
                Fill = null, Opacity = 0.9,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(ring, cx - r);
            Canvas.SetTop(ring, cy - r);
            Panel.SetZIndex(ring, 1000);
            canvas.Children.Add(ring); _activeElements.Add(ring);
            _heroGlowElements.Add(ring);
            AnimateHeroPulse(ring);
        }

        public void ClearHeroGlow()
        {
            var canvas = Core.OverlayCanvas;
            foreach (var e in _heroGlowElements)
            {
                try { var sb = (e as System.Windows.FrameworkElement)?.Tag as Storyboard; if (sb != null) sb.Stop(); }
                catch { }
                try { if (canvas != null) canvas.Children.Remove(e); } catch { }
                _activeElements.Remove(e);
            }
            _heroGlowElements.Clear();
        }

        private void AnimateHeroPulse(UIElement elem)
        {
            var sb = new Storyboard();
            var anim = new DoubleAnimation
            {
                From = 0.4, To = 1.0, Duration = new Duration(TimeSpan.FromSeconds(0.6)),
                AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
            };
            Storyboard.SetTarget(anim, elem);
            Storyboard.SetTargetProperty(anim, new PropertyPath(UIElement.OpacityProperty));
            sb.Children.Add(anim);
            sb.Begin();
            (elem as System.Windows.FrameworkElement).Tag = sb;
        }

        // ═══════════════════════════════════════
        // 手牌策略标记（扇形旋转版 + 多类型）
        // ═══════════════════════════════════════

        /// <summary>手牌策略标记（推荐打出/出售/保留等）</summary>
        public void ShowHandMarker(Engine.HandMarker marker, int totalCount)
        {
            if (IsCombat()) return;
            var canvas = Core.OverlayCanvas;
            if (canvas == null || totalCount <= 0) return;
            var xf = _calc.GetHandCardTransforms(totalCount);
            if (marker.Index < 0 || marker.Index >= xf.Length) return;
            var t = xf[marker.Index];

            var container = new System.Windows.Controls.Grid
            {
                Width = t.Width, Height = t.Height,
                IsHitTestVisible = false,
                RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
                RenderTransform = new RotateTransform(t.Angle),
            };
            System.Windows.Controls.Canvas.SetLeft(container, t.CanvasLeft);
            System.Windows.Controls.Canvas.SetTop(container, t.CanvasTop);

            // 仅"打"标记: 绿色三角▲
            DrawHandPlayIndicator(container, t, marker);

            Panel.SetZIndex(container, 1001);
            canvas.Children.Add(container); _activeElements.Add(container);
            _handMarkerElement = container;  // 记录以便单独清除
            // 手牌标记持久显示，仅在Clear时移除
        }

        /// <summary>PlayNow: 金色大三角▲+顶部横线+底部光晕 在卡牌顶端居中</summary>
        private void DrawHandPlayIndicator(System.Windows.Controls.Grid container,
            Engine.HandCardTransform t, Engine.HandMarker marker)
        {
            var inner = new Canvas { Width = t.Width, Height = t.Height, IsHitTestVisible = false };
            double aw = 20, ah = 14;
            double cx = t.Width / 2;
            // 顶部横线: 横跨卡牌顶部80%宽度, 辅助视觉对齐
            double barW = t.Width * 0.78;
            var topBar = new System.Windows.Shapes.Rectangle
            {
                Width = barW, Height = 3,
                RadiusX = 1.5, RadiusY = 1.5,
                Fill = new SolidColorBrush(Color.FromArgb(180, 255, 179, 0)),
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(topBar, cx - barW / 2);
            Canvas.SetTop(topBar, -6);
            inner.Children.Add(topBar);
            // 底部光晕
            var glow = new Ellipse
            {
                Width = aw + 10, Height = ah + 10,
                Fill = new SolidColorBrush(Color.FromArgb(100, 255, 179, 0)),
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(glow, cx - (aw + 10) / 2);
            Canvas.SetTop(glow, -ah - 8);
            inner.Children.Add(glow);
            // 主三角
            var arrow = new System.Windows.Shapes.Polygon
            {
                Points = new PointCollection { new Point(0, ah), new Point(aw, ah), new Point(aw / 2, 0) },
                Fill = ParseBrush("#FFB300"), Opacity = 0.95, IsHitTestVisible = false,
            };
            Canvas.SetLeft(arrow, cx - aw / 2);
            Canvas.SetTop(arrow, -ah - 4);
            inner.Children.Add(arrow);
            container.Children.Add(inner);
        }

        /// <summary>清除手牌标记(手牌为空或无需标记时调用)</summary>
        public void ClearHandMarker()
        {
            if (_handMarkerElement != null)
            {
                var canvas = Core.OverlayCanvas;
                try { if (canvas != null) canvas.Children.Remove(_handMarkerElement); } catch { }
                try { _activeElements.Remove(_handMarkerElement); } catch { }
                _handMarkerElement = null;
            }
        }

        /// <summary>ConsiderSell: 红色小三角▼在卡牌顶端居中</summary>
        private void DrawHandSellIndicator(System.Windows.Controls.Grid container,
            Engine.HandCardTransform t, Engine.HandMarker marker)
        {
            var inner = new Canvas { Width = t.Width, Height = t.Height, IsHitTestVisible = false };
            double aw = 10, ah = 7;
            var arrow = new System.Windows.Shapes.Polygon
            {
                Points = new PointCollection { new Point(0, 0), new Point(aw, 0), new Point(aw / 2, ah) },
                Fill = ParseBrush("#FF5252"), Opacity = 0.9, IsHitTestVisible = false,
            };
            System.Windows.Controls.Canvas.SetLeft(arrow, t.Width / 2 - aw / 2);
            System.Windows.Controls.Canvas.SetTop(arrow, -ah - 2);
            inner.Children.Add(arrow);
            container.Children.Add(inner);
        }

        /// <summary>Hold: 蓝紫色小圆点●在卡牌顶端居中</summary>
        private void DrawHandHoldIndicator(System.Windows.Controls.Grid container,
            Engine.HandCardTransform t, Engine.HandMarker marker)
        {
            var inner = new Canvas { Width = t.Width, Height = t.Height, IsHitTestVisible = false };
            var dot = new Ellipse
            {
                Width = 8, Height = 8, Fill = ParseBrush("#7C4DFF"), Opacity = 0.9,
                IsHitTestVisible = false,
            };
            System.Windows.Controls.Canvas.SetLeft(dot, t.Width / 2 - 4);
            System.Windows.Controls.Canvas.SetTop(dot, -6);
            inner.Children.Add(dot);
            container.Children.Add(inner);
        }

        /// <summary>清除非面板元素(保留trinket/discover面板防闪烁)</summary>
        /// <summary>清除指定Tag的所有元素(防重复渲染堆叠)</summary>
        private void ClearTaggedElements(string tag)
        {
            var canvas = Core.OverlayCanvas;
            if (canvas == null) return;
            var snapshot = new List<UIElement>(_activeElements);
            foreach (var el in snapshot)
            {
                if ((el as FrameworkElement)?.Tag as string == tag)
                {
                    try { canvas.Children.Remove(el); } catch { }
                    _activeElements.Remove(el);
                }
            }
        }

        public void ClearNonPanel()
        {
            var canvas = Core.OverlayCanvas;
            if (canvas == null) return;
            var snapshot = new List<UIElement>(_activeElements);
            _handMarkerElement = null;
            int removed = 0;
            foreach (var el in snapshot)
            {
                // 保留饰品/发现面板(它们自行管理生命周期)
                var tag = (el as FrameworkElement)?.Tag as string;
                if (tag == "trinket_panel" || tag == "discover_panel"
                    || tag == "timewarp_purchase") continue;
                try { el.BeginAnimation(UIElement.OpacityProperty, null); } catch { }
                try { canvas.Children.Remove(el); } catch { }
                _activeElements.Remove(el);
                removed++;
            }
            // 同步摘除已删元素的购买标签跟随注册(防悬空累积膨胀)
            _shopTagElements.RemoveAll(t => !_activeElements.Contains(t.El));
            if (removed > 0) RendererLog(string.Format("ClearNonPanel: removed={0}", removed));
        }

        public void Clear([System.Runtime.CompilerServices.CallerMemberName] string caller = "",
            [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
        {
            var canvas = Core.OverlayCanvas;
            if (canvas == null) return;
            // 防御并发修改: 复制列表再遍历
            var snapshot = new List<UIElement>(_activeElements);
            _activeElements.Clear();
            _shopTagElements.Clear();  // 清空购买标签跟随注册
            _trinketPanelSig = null; _discoverPanelSig = null;
            _timewarpPurchaseSig = null;
            _handMarkerElement = null;  // 防止悬空引用
            foreach (var el in snapshot)
            {
                try { el.BeginAnimation(UIElement.OpacityProperty, null); } catch { }
                try { canvas.Children.Remove(el); } catch { }
            }
            if (snapshot.Count > 0) RendererLog(string.Format("Clear: removed={0} ← {1}:{2}", snapshot.Count, caller, line));
        }

        /// <summary>购买/刷新后调用: 淡出当前所有元素(80ms)，动画完成后自动移除</summary>
        public void FadeOutActiveElements(int durationMs = 80)
        {
            var canvas = Core.OverlayCanvas;
            if (canvas == null || _activeElements.Count == 0) return;

            var elements = new List<UIElement>(_activeElements);
            _activeElements.Clear();

            foreach (var el in elements)
            {
                // 取消已有动画
                el.BeginAnimation(UIElement.OpacityProperty, null);

                var fadeOut = new DoubleAnimation(1.0, 0.0,
                    new Duration(TimeSpan.FromMilliseconds(durationMs)));
                fadeOut.Completed += (s, e) =>
                {
                    try { if (canvas != null) canvas.Children.Remove(el); } catch { }
                };
                el.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            }
        }

        /// <summary>新元素渲染后调用: 从透明淡入(100ms), 提供平滑视觉衔接</summary>
        public void ApplyFadeIn(UIElement elem, int durationMs = 100)
        {
            if (elem == null) return;
            elem.BeginAnimation(UIElement.OpacityProperty, null);
            elem.Opacity = 0;
            var fadeIn = new DoubleAnimation(0.0, 1.0,
                new Duration(TimeSpan.FromMilliseconds(durationMs)));
            elem.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        }

        /// <summary>对所有当前活跃元素批量应用淡入</summary>
        public void ApplyFadeInToAll(int durationMs = 100)
        {
            foreach (var el in _activeElements)
                ApplyFadeIn(el, durationMs);
        }

        private void AnimateFadeOut(UIElement elem, double visible)
        {
            var a = new DoubleAnimation
            {
                From = 1.0, To = 0.0, Duration = new Duration(TimeSpan.FromSeconds(0.8)),
                BeginTime = TimeSpan.FromSeconds(visible),
            };
            a.Completed += (s, e) =>
            {
                try { _activeElements.Remove(elem); var c = Core.OverlayCanvas; if (c != null) c.Children.Remove(elem); } catch { }
            };
            elem.BeginAnimation(UIElement.OpacityProperty, a);
        }

        private static SolidColorBrush ParseBrush(string hex) => Engine.BrushHelper.ParseBrush(hex);

        /// <summary>目标脉冲光环: 天蓝色呼吸光圈, 标记指向性卡牌的推荐目标</summary>
        public void ShowTargetPulse(int boardIndex, int boardCount)
        {
            ShowTargetPulses(new[] { boardIndex }, boardCount);
        }

        public void ShowTargetPulses(int[] boardIndices, int boardCount)
        {
            if (IsCombat()) return;
            var canvas = Core.OverlayCanvas;
            if (canvas == null || boardCount <= 0 || boardIndices == null || boardIndices.Length == 0) return;

            foreach (var boardIndex in boardIndices)
            {
                if (boardIndex < 0 || boardIndex >= boardCount) continue;
                var r = _calc.GetBoardCardRect(boardIndex, boardCount);
                // 辉光比卡牌大12px/边, 渐变从中心向外衰减
                double pad = 12;
                double gw = r.Width + pad * 2, gh = r.Height + pad * 2;

                var glowBrush = new RadialGradientBrush
                {
                    GradientOrigin = new System.Windows.Point(0.5, 0.5),
                    Center = new System.Windows.Point(0.5, 0.5),
                    RadiusX = 0.5, RadiusY = 0.5,
                };
                glowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(90, 135, 206, 235), 0.0));   // 中心: 半透明天蓝
                glowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(40, 135, 206, 235), 0.5));   // 中间: 淡出
                glowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 135, 206, 235), 1.0));     // 边缘: 全透明

                var glow = new System.Windows.Shapes.Rectangle
                {
                    Width = gw, Height = gh,
                    RadiusX = 6, RadiusY = 6,
                    Fill = glowBrush,
                    Stroke = null,
                    IsHitTestVisible = false,
                };
                Canvas.SetLeft(glow, r.Left - pad);
                Canvas.SetTop(glow, r.Top - pad);
                Panel.SetZIndex(glow, 999); // 低于手牌/标记, 高于卡面
                canvas.Children.Add(glow); _activeElements.Add(glow);
                _targetRings.Add(glow);

                AnimateTargetPulse(glow);
            }
        }

        private void AnimateTargetPulse(UIElement elem)
        {
            var storyboard = new System.Windows.Media.Animation.Storyboard();
            var anim = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 0.4, To = 1.0, Duration = new Duration(TimeSpan.FromSeconds(0.8)),
                AutoReverse = true, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
            };
            System.Windows.Media.Animation.Storyboard.SetTarget(anim, elem);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(anim, new System.Windows.PropertyPath(UIElement.OpacityProperty));
            storyboard.Children.Add(anim);
            storyboard.Begin();
            // 保存引用以便后续停止
            (elem as System.Windows.FrameworkElement).Tag = storyboard;
        }

        private List<UIElement> _targetRings = new List<UIElement>();

        public void ClearTargetPulse()
        {
            var canvas = Core.OverlayCanvas;
            foreach (var e in _targetRings)
            {
                try
                {
                    var sb = (e as System.Windows.FrameworkElement)?.Tag as System.Windows.Media.Animation.Storyboard;
                    if (sb != null) sb.Stop();
                    if (canvas != null) canvas.Children.Remove(e);
                }
                catch { }
                _activeElements.Remove(e);
            }
            _targetRings.Clear();
        }
    }

    /// <summary>购买标签跟随单元: 绑定 entityId 的单个 UI 元素 + 其基准布局, 供 SyncShopTagPositions 平移。</summary>
    internal class ShopTagElement
    {
        public int EntityId;
        public UIElement El;
        public double BaseLeft;
        public double BaseTop;
        public double OffsetLeft;
        public double OffsetTop;
        public int BasePosition;
        public int ActualCards;
    }
}
