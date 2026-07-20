using System;
using System.Collections.Generic;

namespace BobCoach.Engine
{
    /// <summary>
    /// 战斗事件优先级 (数字越小越优先执行)
    /// 触发顺序: 受伤时 → 亡语 → 光环修正 → 复生 → 复仇 → 战斗开始时 → 挤爆 → 默认
    /// </summary>
    public enum CombatEventPriority
    {
        WhenDamaged = 1,    // "受伤时"效果立即插入，最高优先级
        Deathrattle = 2,    // 亡语效果，左→右排队结算
        AuraUpdate = 3,     // 光环修正，优先于复生（可能把0血抬回正值）
        Reborn = 4,         // 复生在亡语完全结算后触发
        Avenge = 5,         // 复仇效果
        StartOfCombat = 6,  // 战斗开始时效果
        Cram = 7,           // 挤爆效果(召唤失败补偿)
        Default = 10        // 其他效果
    }

    /// <summary>
    /// 战斗事件 — 携带类型、优先级和处理函数。
    /// </summary>
    public class CombatEvent
    {
        public string Type;
        public CombatEventPriority Priority;
        public Action<CombatContext> Handler;
        public Dictionary<string, object> Data;
    }

    /// <summary>
    /// 战斗事件优先级队列。
    /// 事件按优先级插入(低数字=高优先级)，同优先级按FIFO顺序执行。
    /// ProcessAll 会处理队列中所有事件，包括处理过程中新产生的事件(最多500个防止死循环)。
    /// </summary>
    public class CombatEventQueue
    {
        private List<CombatEvent> _events = new List<CombatEvent>();
        private const int MaxEvents = 500;

        /// <summary>
        /// 按优先级插入事件 (低数字=高优先级)，同优先级FIFO。
        /// </summary>
        public void Push(CombatEvent evt)
        {
            if (evt == null) return;
            int idx = 0;
            while (idx < _events.Count && (int)_events[idx].Priority <= (int)evt.Priority)
                idx++;
            _events.Insert(idx, evt);
        }

        /// <summary>
        /// 处理队列中所有事件（含处理过程中新产生的事件），最多 MaxEvents 次防止死循环。
        /// </summary>
        public void ProcessAll(CombatContext ctx)
        {
            int processed = 0;
            while (_events.Count > 0 && processed < MaxEvents)
            {
                var evt = _events[0];
                _events.RemoveAt(0);
                evt.Handler?.Invoke(ctx);
                processed++;
            }
            if (processed >= MaxEvents)
            {
                _events.Clear();
            }
        }

        /// <summary>
        /// 队列是否为空。
        /// </summary>
        public bool IsEmpty
        {
            get { return _events.Count == 0; }
        }

        /// <summary>
        /// 队列中事件数量。
        /// </summary>
        public int Count
        {
            get { return _events.Count; }
        }

        /// <summary>
        /// 清除所有待处理事件。
        /// </summary>
        public void Clear()
        {
            _events.Clear();
        }

        /// <summary>
        /// 工厂方法：创建带标准优先级的事件对象。
        /// </summary>
        public static CombatEvent CreateEvent(string type, CombatEventPriority priority,
            Action<CombatContext> handler, Dictionary<string, object> data = null)
        {
            return new CombatEvent
            {
                Type = type,
                Priority = priority,
                Handler = handler,
                Data = data ?? new Dictionary<string, object>()
            };
        }
    }
}
