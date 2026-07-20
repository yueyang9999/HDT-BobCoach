using System;
using System.Collections.Generic;
using System.Linq;

namespace BobCoach.Engine
{
    public enum UiTargetType
    {
        Discover,
        Trinket,
        ShopCard
    }

    public enum UiTargetSource
    {
        None,
        PowerLog,
        Zone6,
        Entity,
        Decision
    }

    public enum UiTargetPhase
    {
        Searching,
        Candidate,
        Stable,
        Bound,
        Lost,
        Expired
    }

    public enum UiTargetExpireReason
    {
        None,
        MissingSignal,
        InvalidBatch,
        NewBatch,
        ChoiceCompleted,
        Timeout
    }

    public sealed class UiTargetSnapshot
    {
        public UiTargetType TargetType;
        public UiTargetSource Source;
        public string BatchId = "";
        public int Turn;
        public List<int> EntityIds = new List<int>();
        public int OptionCount;
        public double Confidence;

        public bool IsValid
        {
            get { return !string.IsNullOrEmpty(BatchId) && OptionCount >= 2; }
        }
    }

    public sealed class UiTargetState
    {
        public UiTargetPhase Phase = UiTargetPhase.Searching;
        public UiTargetType TargetType;
        public UiTargetSource Source = UiTargetSource.None;
        public string BatchId = "";
        public int Turn;
        public List<int> EntityIds = new List<int>();
        public long FirstSeenTicks;
        public long LastSeenTicks;
        public long LostAtTicks;
        public double Confidence;
        public UiTargetExpireReason ExpireReason = UiTargetExpireReason.None;
        public int StableFrames;

        public bool IsConfirmed
        {
            get { return Phase == UiTargetPhase.Stable || Phase == UiTargetPhase.Bound; }
        }
    }

    public sealed class UiTargetStateMachine
    {
        private readonly long _stableTicks;
        private readonly long _lostTicks;
        private readonly int _stableFrames;
        private string _completedBatchId = "";
        private UiTargetType _completedType;
        private int _completedTurn;

        public UiTargetState State { get; private set; }

        public UiTargetStateMachine(int stableMs = 120, int lostMs = 1000, int stableFrames = 2)
        {
            _stableTicks = stableMs * TimeSpan.TicksPerMillisecond;
            _lostTicks = lostMs * TimeSpan.TicksPerMillisecond;
            _stableFrames = Math.Max(1, stableFrames);
            State = new UiTargetState();
        }

        public void Reset()
        {
            State = new UiTargetState();
            _completedBatchId = "";
            _completedTurn = 0;
        }

        public void CompleteChoice()
        {
            if (!string.IsNullOrEmpty(State.BatchId))
            {
                _completedBatchId = State.BatchId;
                _completedType = State.TargetType;
                _completedTurn = State.Turn;
            }
            Expire(UiTargetExpireReason.ChoiceCompleted, DateTime.UtcNow.Ticks);
        }

        public UiTargetState Advance(UiTargetSnapshot snapshot)
        {
            return Advance(snapshot, DateTime.UtcNow.Ticks);
        }

        public UiTargetState Advance(UiTargetSnapshot snapshot, long nowTicks)
        {
            if (snapshot == null || !snapshot.IsValid)
                return AdvanceMissing(nowTicks);

            if (IsCompletedBatch(snapshot))
            {
                Expire(UiTargetExpireReason.ChoiceCompleted, nowTicks);
                return State;
            }

            if (State.Phase == UiTargetPhase.Searching || State.Phase == UiTargetPhase.Expired)
                return StartCandidate(snapshot, nowTicks);

            if (!SameBatch(snapshot))
                return StartCandidate(snapshot, nowTicks);

            State.LastSeenTicks = nowTicks;
            State.Confidence = Math.Max(State.Confidence, snapshot.Confidence);
            State.StableFrames++;
            State.ExpireReason = UiTargetExpireReason.None;

            switch (State.Phase)
            {
                case UiTargetPhase.Candidate:
                    if (IsStableEnough(nowTicks, snapshot))
                        State.Phase = UiTargetPhase.Stable;
                    break;
                case UiTargetPhase.Stable:
                    State.Phase = UiTargetPhase.Bound;
                    break;
                case UiTargetPhase.Lost:
                    State.Phase = UiTargetPhase.Bound;
                    State.LostAtTicks = 0;
                    break;
            }

            return State;
        }

        private UiTargetState AdvanceMissing(long nowTicks)
        {
            switch (State.Phase)
            {
                case UiTargetPhase.Candidate:
                    Expire(UiTargetExpireReason.InvalidBatch, nowTicks);
                    break;
                case UiTargetPhase.Stable:
                case UiTargetPhase.Bound:
                    State.Phase = UiTargetPhase.Lost;
                    State.LostAtTicks = nowTicks;
                    State.ExpireReason = UiTargetExpireReason.MissingSignal;
                    break;
                case UiTargetPhase.Lost:
                    if (nowTicks - State.LostAtTicks >= _lostTicks)
                        Expire(UiTargetExpireReason.Timeout, nowTicks);
                    break;
                case UiTargetPhase.Expired:
                    State = new UiTargetState();
                    break;
            }
            return State;
        }

        private UiTargetState StartCandidate(UiTargetSnapshot snapshot, long nowTicks)
        {
            if (!IsCompletedBatch(snapshot))
            {
                _completedBatchId = "";
                _completedTurn = 0;
            }

            State = new UiTargetState
            {
                Phase = UiTargetPhase.Candidate,
                TargetType = snapshot.TargetType,
                Source = snapshot.Source,
                BatchId = snapshot.BatchId,
                Turn = snapshot.Turn,
                EntityIds = snapshot.EntityIds != null ? new List<int>(snapshot.EntityIds) : new List<int>(),
                FirstSeenTicks = nowTicks,
                LastSeenTicks = nowTicks,
                Confidence = snapshot.Confidence,
                StableFrames = 1,
                ExpireReason = UiTargetExpireReason.None
            };

            if (IsStableEnough(nowTicks, snapshot))
                State.Phase = UiTargetPhase.Stable;

            return State;
        }

        private bool IsStableEnough(long nowTicks, UiTargetSnapshot snapshot)
        {
            // 高置信批次(PowerLog权威 / 计划饰品回合类型匹配批次)首帧即稳定 —
            // 提取由事件驱动, 状态静止时可能8秒无帧, 多帧确认会让面板延迟到用户选完(07062107)。
            if (snapshot.Confidence >= 0.95)
                return true;
            return State.StableFrames >= _stableFrames
                || (nowTicks - State.FirstSeenTicks) >= _stableTicks;
        }

        private bool SameBatch(UiTargetSnapshot snapshot)
        {
            if (State.TargetType != snapshot.TargetType) return false;
            if (State.Turn != snapshot.Turn) return false;
            if (!string.Equals(State.BatchId, snapshot.BatchId, StringComparison.Ordinal)) return false;
            return SameIds(State.EntityIds, snapshot.EntityIds);
        }

        private bool IsCompletedBatch(UiTargetSnapshot snapshot)
        {
            return !string.IsNullOrEmpty(_completedBatchId)
                && _completedType == snapshot.TargetType
                && _completedTurn == snapshot.Turn
                && string.Equals(_completedBatchId, snapshot.BatchId, StringComparison.Ordinal);
        }

        private static bool SameIds(List<int> a, List<int> b)
        {
            if (a == null) a = new List<int>();
            if (b == null) b = new List<int>();
            if (a.Count != b.Count) return false;
            var aa = a.OrderBy(x => x).ToList();
            var bb = b.OrderBy(x => x).ToList();
            for (int i = 0; i < aa.Count; i++)
                if (aa[i] != bb[i]) return false;
            return true;
        }

        private void Expire(UiTargetExpireReason reason, long nowTicks)
        {
            State.Phase = UiTargetPhase.Expired;
            State.LastSeenTicks = nowTicks;
            State.ExpireReason = reason;
        }
    }
}
