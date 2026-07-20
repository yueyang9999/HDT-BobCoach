namespace BobCoach.Engine
{
    /// <summary>Power.log明确完成的第二技能选择结果。</summary>
    public sealed class SecondHeroPowerChoiceSelection
    {
        public SecondHeroPowerChoiceSelection(
            string occurrenceId,
            int choiceId,
            string selectedCardId,
            string evidenceSource)
        {
            OccurrenceId = occurrenceId ?? "";
            ChoiceId = choiceId;
            SelectedCardId = selectedCardId ?? "";
            EvidenceSource = evidenceSource ?? "";
        }

        public string OccurrenceId { get; private set; }
        public int ChoiceId { get; private set; }
        public string SelectedCardId { get; private set; }
        public string EvidenceSource { get; private set; }
    }
}
