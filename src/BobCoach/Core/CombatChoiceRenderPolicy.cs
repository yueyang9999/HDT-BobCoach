namespace BobCoach.Engine
{
    public static class CombatChoiceRenderPolicy
    {
        public static bool CanRenderDiscoverDuringCombat(
            PowerLogChoiceBatch batch, bool panelActive)
        {
            return panelActive
                && batch != null
                && batch.ChoiceId >= 0
                && batch.Candidates != null
                && batch.Candidates.Count >= 2;
        }
    }
}
