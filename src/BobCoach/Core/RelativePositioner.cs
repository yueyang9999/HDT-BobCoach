using System;
using System.Windows;

namespace BobCoach.Engine
{
    /// <summary>
    /// UI元素定位器 — 统一使用 GameLayoutCalculator 计算坐标,
    /// 确保校准(F10)和实际渲染使用同一套定位系统。
    /// </summary>
    public class RelativePositioner
    {
        private Rect _gameRect;
        private GameLayoutCalculator _layoutCalc;

        public RelativePositioner(Rect gameRect)
        {
            _gameRect = gameRect;
            _layoutCalc = new GameLayoutCalculator();
        }

        public void UpdateRect(Rect gameRect)
        {
            _gameRect = gameRect;
            _layoutCalc.Refresh();
        }

        // ── 酒馆定位: 使用 GameLayoutCalculator ──

        public Point GetShopCardPosition(int index, int totalCards)
        {
            var rect = _layoutCalc.GetShopCardRect(index, Math.Max(1, totalCards));
            // 标记中心 = 卡牌中心偏上 (标注框在卡牌上方)
            return new Point(rect.Left + rect.Width / 2.0, rect.Top);
        }

        public Size GetShopCardSize()
        {
            var area = _layoutCalc.GetShopArea();
            // 从总面积反推单卡尺寸
            double cardW = _gameRect.Width * 0.050;
            double cardH = _gameRect.Height * 0.120;
            return new Size(cardW, cardH);
        }

        /// <summary>升本按钮: 按钮中心</summary>
        public Point GetLevelUpBeaconPosition()
        {
            var rect = _layoutCalc.GetTavernButtonArea();
            return new Point(rect.Left + rect.Width / 2.0, rect.Top + rect.Height / 2.0);
        }

        /// <summary>刷新按钮: 按钮中心</summary>
        public Point GetRefreshBeaconPosition()
        {
            var rect = _layoutCalc.GetRefreshButtonArea();
            return new Point(rect.Left + rect.Width / 2.0, rect.Top + rect.Height / 2.0);
        }

        /// <summary>场面第 index 个随从的屏幕坐标</summary>
        public Point GetBoardMinionPosition(int index, int totalCount)
        {
            var rect = _layoutCalc.GetBoardCardRect(index, Math.Max(1, totalCount));
            return new Point(rect.Left + rect.Width / 2.0, rect.Top);
        }

        public Size GetBoardCardSize()
        {
            double cardW = _gameRect.Width * 0.050;
            double cardH = _gameRect.Height * 0.120;
            return new Size(cardW, cardH);
        }

        /// <summary>手牌第 index 张的位置</summary>
        public Point GetHandCardPosition(int index, int totalCount)
        {
            var rect = _layoutCalc.GetHandCardRect(index, Math.Max(1, totalCount));
            return new Point(rect.Left + rect.Width / 2.0, rect.Top);
        }

        public Size GetHandCardSize()
        {
            double cardW = _gameRect.Width * 0.050;
            double cardH = cardW / 0.72;
            return new Size(cardW, cardH);
        }

        /// <summary>通用提示徽标位置（右上角）</summary>
        public Point GetBadgePosition()
        {
            return new Point(_gameRect.Width * 0.97, _gameRect.Height * 0.05);
        }

        // ── 坐标转换 ──

        private Point ToAbsolute(double nx, double ny)
        {
            return new Point(nx * _gameRect.Width, ny * _gameRect.Height);
        }

        public static Rect? GetGameRect()
        {
            var fb = SafeNativeMethods.GetHearthstoneRect();
            if (fb.HasValue)
            {
                var rc = fb.Value;
                return new Rect(rc.Left, rc.Top, rc.Width, rc.Height);
            }
            return null;
        }
    }
}
