namespace BobCoach.Engine
{
    /// <summary>一次带来源的酒馆升级；静态酒馆等级本身不是该事件。</summary>
    public sealed class TavernUpgradeOccurrence
    {
        internal TavernUpgradeOccurrence(
            string occurrenceId, int turn, int fromTier, int toTier, string source)
        {
            OccurrenceId = occurrenceId ?? "";
            Turn = turn;
            FromTier = fromTier;
            ToTier = toTier;
            Source = source ?? "";
        }

        public string OccurrenceId { get; private set; }
        public int Turn { get; private set; }
        public int FromTier { get; private set; }
        public int ToTier { get; private set; }
        public string Source { get; private set; }
    }
}
