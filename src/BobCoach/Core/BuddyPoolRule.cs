namespace BobCoach.Engine
{
    /// <summary>伙伴普通版进入共享公共池的只读规则。</summary>
    public sealed class BuddyPoolRule
    {
        internal BuddyPoolRule(string sourceId)
        {
            EnterTurn = 1;
            SharedPool = true;
            NormalOnly = true;
            SourceId = sourceId ?? "";
        }

        public int EnterTurn { get; private set; }
        public bool SharedPool { get; private set; }
        public bool NormalOnly { get; private set; }
        public string SourceId { get; private set; }
    }
}
