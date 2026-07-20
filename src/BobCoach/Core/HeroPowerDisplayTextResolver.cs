using System;

namespace BobCoach.Engine
{
    /// <summary>
    /// Resolves localized hero-power text from the user's HDT-provided HearthDb at runtime.
    /// The result is display-only and is never persisted by this component.
    /// </summary>
    public static class HeroPowerDisplayTextResolver
    {
        public static string Resolve(string heroPowerCardId)
        {
            if (string.IsNullOrEmpty(heroPowerCardId)) return "";
            try
            {
                HearthDb.Card card;
                if (!HearthDb.Cards.All.TryGetValue(heroPowerCardId, out card) || card == null)
                    return "";
                return card.GetLocText(HearthDb.Enums.Locale.zhCN) ?? "";
            }
            catch
            {
                return "";
            }
        }
    }
}
