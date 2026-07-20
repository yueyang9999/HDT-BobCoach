namespace BobCoach.Engine
{
    internal interface ICardPoolFactSource
    {
        bool TryGetCard(string cardId, out CardPoolFact fact);
    }

    internal struct CardPoolFact
    {
        public string CardId;
        public int Tier;
        public string TribeCn;
        public bool IsSpell;
        public int Cost;
        public int Attack;
        public int Health;
    }
}
