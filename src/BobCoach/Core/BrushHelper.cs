using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace BobCoach.Engine
{
    /// <summary>
    /// 线程安全的Brush缓存工具。所有UI渲染共用此缓存，避免重复创建SolidColorBrush。
    /// </summary>
    public static class BrushHelper
    {
        private static readonly Dictionary<string, SolidColorBrush> _brushCache = new Dictionary<string, SolidColorBrush>();
        private static readonly object _lock = new object();
        private const int BRUSH_CACHE_MAX = 32;

        /// <summary>
        /// 从HEX字符串获取SolidColorBrush（带线程安全缓存）。
        /// 缓存命中时直接返回，未命中时解析并缓存。
        /// </summary>
        public static SolidColorBrush ParseBrush(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return null;

            lock (_lock)
            {
                if (_brushCache.TryGetValue(hex, out var cached))
                    return cached;
            }

            try
            {
                var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex);
                if (brush != null)
                {
                    lock (_lock)
                    {
                        if (_brushCache.Count >= BRUSH_CACHE_MAX)
                            _brushCache.Clear();
                        _brushCache[hex] = brush;
                    }
                }
                return brush;
            }
            catch { return null; }
        }

        /// <summary>
        /// 清空Brush缓存。用于内存紧张时或单元测试后清理。
        /// </summary>
        public static void ClearCache()
        {
            lock (_lock)
            {
                _brushCache.Clear();
            }
        }
    }
}
