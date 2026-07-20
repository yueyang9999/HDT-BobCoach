using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace BobCoach.Engine
{
    /// <summary>
    /// 控制同屏提示数量，强制执行渐进式提示规则。
    /// Critical 同时最多 2 个，Major 最多 3 个，Minor 不限。
    /// </summary>
    public class VisualScheduler
    {
        private readonly Dictionary<string, FrameworkElement> _activeElements = new Dictionary<string, FrameworkElement>();
        private const int MaxCritical = 2;
        private const int MaxMajor = 3;

        public void ShowOrUpdate(string elementId, FrameworkElement control, DecisionLevel level, Panel parent)
        {
            if (parent == null || control == null) return;

            // 清理旧的同 ID 元素
            if (_activeElements.TryGetValue(elementId, out var old))
            {
                parent.Children.Remove(old);
                _activeElements.Remove(elementId);
            }

            // 数量管控
            int currentCritical = _activeElements.Count(kv => GetLevel(kv.Value) == DecisionLevel.Critical);
            int currentMajor = _activeElements.Count(kv => GetLevel(kv.Value) == DecisionLevel.Major);

            if (level == DecisionLevel.Critical && currentCritical >= MaxCritical) return;
            if (level == DecisionLevel.Major && currentMajor >= MaxMajor) return;

            // 在控件上标记等级
            control.Tag = level;

            _activeElements[elementId] = control;
            parent.Children.Add(control);
        }

        public void Hide(string elementId, Panel parent)
        {
            if (_activeElements.TryGetValue(elementId, out var element))
            {
                if (parent != null) parent.Children.Remove(element);
                _activeElements.Remove(elementId);
            }
        }

        public void ClearLevel(DecisionLevel level, Panel parent)
        {
            var toRemove = _activeElements
                .Where(kv => GetLevel(kv.Value) == level)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var key in toRemove)
            {
                if (parent != null && _activeElements.ContainsKey(key))
                    parent.Children.Remove(_activeElements[key]);
                _activeElements.Remove(key);
            }
        }

        public void ClearAll(Panel parent)
        {
            foreach (var kv in _activeElements)
                if (parent != null) parent.Children.Remove(kv.Value);
            _activeElements.Clear();
        }

        public int ActiveCount { get { return _activeElements.Count; } }

        private DecisionLevel GetLevel(FrameworkElement e)
        {
            return e.Tag is DecisionLevel lvl ? lvl : DecisionLevel.Minor;
        }
    }
}
