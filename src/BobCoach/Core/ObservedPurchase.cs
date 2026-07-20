namespace BobCoach.Engine
{
    public sealed class ObservedPurchase
    {
        public ObservedPurchase(
            int turn, int entityId, string cardId, bool isSpell,
            int cost, string source, bool succeeded, bool golden = false)
        {
            Turn = turn;
            EntityId = entityId;
            CardId = cardId ?? "";
            IsSpell = isSpell;
            Cost = cost < 0 || cost == int.MaxValue ? -1 : cost;
            Source = source ?? "unknown";
            Succeeded = succeeded;
            Golden = golden;
        }

        public int Turn { get; private set; }
        public int EntityId { get; private set; }
        public string CardId { get; private set; }
        public bool IsSpell { get; private set; }
        public int Cost { get; private set; }
        public string Source { get; private set; }
        public bool Succeeded { get; private set; }
        public bool Golden { get; private set; }
    }
}
