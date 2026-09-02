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

        public bool IsPressableActive
        {
            get
            {
                lock (_sync)
                {
                    return _pressableActive;
                }
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
        private readonly DarkGiftTipAdvisor _advisor;
        private bool _choiceActive;

        public DarkGiftTipRenderCoordinator(TimeSpan window)
        {
            _gate = new DarkGiftTipGate(window);
            _advisor = new DarkGiftTipAdvisor();
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
            return GetStatus(now, null);
        }

        public string GetStatus(DateTime now, DarkGiftTipContext context)
        {
            lock (_sync)
            {
                if (_choiceActive)
                {
                    if (context != null
                        && ((context.Gold >= 0 && context.Gold < 3)
                            || (!string.IsNullOrEmpty(context.Phase)
                                && context.Phase != "shop")))
                        return null;
                    return "\u6697\u8d50\u9009\u62e9\u4e2d";
                }
                if (!_gate.ShouldShow(now)) return null;

                var effectiveContext = context ?? new DarkGiftTipContext();
                if (effectiveContext.Gold >= 0 && effectiveContext.Gold < 3)
                    return null;
                effectiveContext.Pressable = true;
                effectiveContext.Backfilled = false;
                var advice = _advisor.Evaluate(effectiveContext);
                return advice.ShouldShow ? advice.StatusText : null;
            }
        }

        public void RefreshPressable(DateTime now)
        {
            lock (_sync)
            {
                if (!_choiceActive && _gate.IsPressableActive && _gate.ShouldShow(now))
                    _gate.OnPressableChanged(true, now, false);
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

    /// <summary>
    /// Serializable-style context used by the dark gift tip advisor. The plugin
    /// fills it from the latest GameState snapshot so the tip is gated by the
    /// same board, shop, hand, gold, turn, and tier inputs the rest of the
    /// algorithm uses.
    /// </summary>
    public sealed class DarkGiftTipContext
    {
        public bool Pressable;
        public bool Backfilled;
        public bool ChoiceActive;
        public bool StateAvailable;
        public bool GameActive;
        public string Phase = "";
        public int Turn = -1;
        public int TavernTier = -1;
        public int Gold = -1;
        public int BoardMinionCount = -1;
        public int ShopMinionCount = -1;
        public int HandMinionCount = -1;
        public int Health = -1;
        public int BoardPower = -1;
        public int OpponentPower = -1;
        public int UsesRemaining = 3;
        public bool AlreadyUsedThisTurn;
        public double OfferedExpectedValue;
        public bool HasCoreSynergy;
    }

    public sealed class DarkGiftTipAdvice
    {
        public bool ShouldShow;
        public string StatusText = "";
        public string Reason = "";
    }

    /// <summary>
    /// Decides when the dark gift pressable hint is worth showing. It is a
    /// transparent context gate, not a card-effect scorer: unknown state stays
    /// closed, and the tip text stays minimal so it does not distract gameplay.
    /// </summary>
    public sealed class DarkGiftTipAdvisor
    {
        private const string ChoiceActiveText = "\u6697\u8d50\u9009\u62e9\u4e2d";
        private const string PressableText = "\u5efa\u8bae\u70b9\u51fb\u9ed1\u6697\u53d1\u73b0";

        public DarkGiftTipAdvice Evaluate(DarkGiftTipContext context)
        {
            if (context == null) return Create(false, null, "context_null");
            if (context.ChoiceActive)
                return Create(true, ChoiceActiveText, "choice_active");
            if (!context.Pressable) return Create(false, null, "not_pressable");
            if (context.Backfilled) return Create(false, null, "backfilled");
            if (!context.StateAvailable) return Create(false, null, "state_unknown");
            if (!context.GameActive) return Create(false, null, "game_inactive");
            if (!string.Equals(context.Phase, "shop", StringComparison.Ordinal))
                return Create(false, null, "phase_not_shop");
            if (context.Turn <= 0 || context.TavernTier <= 0 || context.Gold < 0
                || context.BoardMinionCount < 0
                || context.ShopMinionCount < 0
                || context.HandMinionCount < 0)
                return Create(false, null, "state_incomplete");
            if (context.Gold < 3)
                return Create(false, null, "insufficient-gold");

            int opportunityCount = context.BoardMinionCount
                + context.ShopMinionCount
                + context.HandMinionCount;
            if (opportunityCount <= 0 && context.Gold <= 0)
                return Create(false, null, "no_opportunity_context");

            var decision = DarkGiftDecisionService.Decide(
                context.Turn,
                context.Gold,
                context.UsesRemaining,
                context.AlreadyUsedThisTurn,
                context.BoardPower,
                context.OpponentPower,
                context.Health,
                context.OfferedExpectedValue,
                context.HasCoreSynergy);
            if (!decision.ShouldOpen)
                return Create(false, null, decision.Reason);

            return Create(true, PressableText, "open");
        }

        private static DarkGiftTipAdvice Create(bool shouldShow, string statusText, string reason)
        {
            return new DarkGiftTipAdvice
            {
                ShouldShow = shouldShow,
                StatusText = statusText ?? "",
                Reason = reason ?? "",
            };
        }
    }
}
