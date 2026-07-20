using System;

namespace BobCoach.Engine
{
    internal sealed class HearthDbCardIdentitySource : ICardIdentitySource
    {
        public bool TryGetCard(string cardId, out string zhCnName, out int tier)
        {
            zhCnName = "";
            tier = 0;
            if (string.IsNullOrEmpty(cardId)) return false;

            try
            {
                HearthDb.Card card;
                if (!HearthDb.Cards.All.TryGetValue(cardId, out card) || card == null)
                    return false;
                try { zhCnName = card.GetLocName(HearthDb.Enums.Locale.zhCN) ?? ""; }
                catch { zhCnName = ""; }
                tier = card.TechLevel;
                return true;
            }
            catch
            {
                zhCnName = "";
                tier = 0;
                return false;
            }
        }
    }
}
