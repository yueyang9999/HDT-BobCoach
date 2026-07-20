using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace BobCoach.Engine
{
    /// <summary>只读的定时/条件资源计划；不代表资源已经由游戏实际发放。</summary>
    public sealed class ScheduledGrant
    {
        internal ScheduledGrant(
            string id, string kind, int turn, int everyTurns, int tier,
            string cardId, int count, bool golden, IList<int> tiers)
        {
            Id = id ?? "";
            Kind = kind ?? "";
            Turn = turn;
            EveryTurns = everyTurns;
            Tier = tier;
            CardId = cardId ?? "";
            Count = count;
            Golden = golden;
            Tiers = new ReadOnlyCollection<int>(new List<int>(tiers ?? new int[0]));
        }

        public string Id { get; private set; }
        public string Kind { get; private set; }
        public int Turn { get; private set; }
        public int EveryTurns { get; private set; }
        public int Tier { get; private set; }
        public string CardId { get; private set; }
        public int Count { get; private set; }
        public bool Golden { get; private set; }
        public IList<int> Tiers { get; private set; }
    }
}
