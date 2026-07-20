using System.Collections.Generic;

namespace BobCoach.Engine
{
    internal enum CardClassificationCardType
    {
        Minion,
        BattlegroundSpell,
    }

    internal sealed class CardClassificationFact
    {
        public string CardId;
        public CardClassificationCardType CardType;
        public List<string> Mechanics = new List<string>();
        public string TextZhCn = "";
    }

    internal interface ICardClassificationFactSource
    {
        bool TryGet(string cardId, out CardClassificationFact fact);
    }

    internal interface ICardClassificationEvaluator
    {
        bool TryEvaluate(
            CardClassificationFact fact,
            CardSemanticsData semantics,
            out CardClassifier.CardClassification classification);
    }
}
