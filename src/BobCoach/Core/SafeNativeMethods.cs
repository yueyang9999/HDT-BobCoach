using System;
using System.Runtime.InteropServices;

namespace BobCoach.Engine
{
    /// <summary>
    /// 仅使用标准 Windows 窗口管理 API（FindWindow, GetWindowRect, SetWindowPos）。
    /// 绝不涉及内存读写或注入。符合 SAFETY_SPEC.md 安全红线。
    /// </summary>
    public static class SafeNativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_SHOWWINDOW = 0x0040;
        private const int LOGPIXELSX = 88;
        private const int LOGPIXELSY = 90;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
            public int Width { get { return Right - Left; } }
            public int Height { get { return Bottom - Top; } }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X, Y;
        }

        /// <summary>
        /// 获取炉石窗口句柄。支持中英文窗口标题。
        /// </summary>
        public static IntPtr GetHearthstoneWindow()
        {
            string[] titles = { "Hearthstone", "炉石传说", "爐石戰記" };
            foreach (var t in titles)
            {
                var hwnd = FindWindow(null, t);
                if (hwnd != IntPtr.Zero) return hwnd;
            }
            return IntPtr.Zero;
        }

        /// <summary>
        /// 获取炉石窗口矩形（含标题栏）。
        /// </summary>
        public static RECT? GetHearthstoneRect()
        {
            var hwnd = GetHearthstoneWindow();
            if (hwnd == IntPtr.Zero) return null;
            if (GetWindowRect(hwnd, out RECT rc))
                return rc;
            return null;
        }

        /// <summary>
        /// 获取炉石窗口客户区在屏幕上的物理像素坐标。
        /// 这是 Overlay 定位的基准坐标系。
        /// </summary>
        public static RECT? GetHearthstoneClientRect()
        {
            var hwnd = GetHearthstoneWindow();
            if (hwnd == IntPtr.Zero) return null;

            if (!GetClientRect(hwnd, out RECT client)) return null;

            var topLeft = new POINT { X = client.Left, Y = client.Top };
            ClientToScreen(hwnd, ref topLeft);

            return new RECT
            {
                Left = topLeft.X,
                Top = topLeft.Y,
                Right = topLeft.X + client.Width,
                Bottom = topLeft.Y + client.Height
            };
        }

        /// <summary>
        /// 获取系统 DPI 缩放比例（相对于96dpi）。
        /// </summary>
        public static double GetDpiScale()
        {
            var hdc = GetDC(IntPtr.Zero);
            if (hdc == IntPtr.Zero) return 1.0;
            int dpiX = GetDeviceCaps(hdc, LOGPIXELSX);
            ReleaseDC(IntPtr.Zero, hdc);
            return dpiX / 96.0;
        }
    }
}
