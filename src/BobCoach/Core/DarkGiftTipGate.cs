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

    /// <summary>
    /// Centralizes the UI status for the dark gift tip so combat/first-frame
    /// early returns and the watchdog use the same rendered state.
    /// </summary>
    public sealed class DarkGiftTipRenderCoordinator
    {
        private readonly object _sync = new object();
        private readonly DarkGiftTipGate _gate;
        private bool _choiceActive;

        public DarkGiftTipRenderCoordinator(TimeSpan window)
        {
            _gate = new DarkGiftTipGate(window);
        }

        public void OnPressableChanged(bool pressable, DateTime now, bool backfilled = false)
        {
            lock (_sync)
            {
                _gate.OnPressableChanged(pressable, now, backfilled);
            }
        }

        public void OnDarkGiftChoiceActive(DateTime now)
        {
            lock (_sync)
            {
                _choiceActive = true;
                _gate.OnDarkGiftChoiceActive(now);
            }
        }

        public void OnDarkGiftCompleted(DateTime now)
        {
            lock (_sync)
            {
                _choiceActive = false;
                _gate.OnDarkGiftCompleted(now);
            }
        }

        public string GetStatus(DateTime now)
        {
            lock (_sync)
            {
                if (_choiceActive) return "\u6697\u8d50\u9009\u62e9\u4e2d";
                return _gate.ShouldShow(now) ? "\u6697\u8d50\u53ef\u53d1\u73b0" : null;
            }
        }

        public void Reset()
        {
            lock (_sync)
            {
                _choiceActive = false;
                _gate.Reset();
            }
        }
    }
}
