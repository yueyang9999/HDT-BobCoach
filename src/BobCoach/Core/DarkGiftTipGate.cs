using System;

namespace BobCoach.Engine
{
    /// <summary>
    /// Bounds the dark gift pressable tip to a short window after the button
    /// first becomes pressable. It only gates UI guidance, not dark gift capture.
    /// </summary>
    public sealed class DarkGiftTipGate
    {
        private readonly object _sync = new object();
        private readonly TimeSpan _window;
        private bool _pressableActive;
        private DateTime _shownUntil = DateTime.MinValue;

        public DarkGiftTipGate(TimeSpan window)
        {
            _window = window > TimeSpan.Zero ? window : TimeSpan.Zero;
        }

        public void OnPressableChanged(bool pressable, DateTime now, bool backfilled = false)
        {
            lock (_sync)
            {
                _pressableActive = pressable;
                if (!pressable || backfilled) return;
                if (now < _shownUntil) return;
                _shownUntil = now.Add(_window);
            }
        }

        public void OnDarkGiftChoiceActive(DateTime now)
        {
            Close();
        }

        public void OnDarkGiftCompleted(DateTime now)
        {
            Close();
        }

        public bool ShouldShow(DateTime now)
        {
            lock (_sync)
            {
                return _pressableActive && now < _shownUntil;
            }
        }

        public void Reset()
        {
            Close();
        }

        private void Close()
        {
            lock (_sync)
            {
                _pressableActive = false;
                _shownUntil = DateTime.MinValue;
            }
        }
    }
}
