using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace BobCoach.Engine
{
    /// <summary>Power.log观察到的真实第二技能候选批次。</summary>
    public sealed class SecondHeroPowerChoiceBatchObservation
    {
        private readonly ReadOnlyCollection<string> _candidateCardIds;

        public SecondHeroPowerChoiceBatchObservation(
            string occurrenceId,
            int choiceId,
            IList<string> candidateCardIds,
            string evidenceSource)
        {
            OccurrenceId = occurrenceId ?? "";
            ChoiceId = choiceId;
            _candidateCardIds = new ReadOnlyCollection<string>(
                new List<string>(candidateCardIds ?? new string[0]));
            EvidenceSource = evidenceSource ?? "";
        }

        public string OccurrenceId { get; private set; }
        public int ChoiceId { get; private set; }
        public IList<string> CandidateCardIds { get { return _candidateCardIds; } }
        public string EvidenceSource { get; private set; }
    }
}
