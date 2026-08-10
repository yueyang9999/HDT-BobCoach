using System;

namespace BobCoach.Engine
{
    internal sealed class ActivateFact
    {
        internal string CardId;
        internal string NameZhCn;
        internal string NameEnUs;
        internal string TextZhCn;
        internal string TextEnUs;
        internal string Type;
        internal int Cost;
        internal int ActivateCost;
        internal int TechLevel;
        internal int Attack;
        internal int Health;
        internal bool IsBattlegroundsPoolMinion;
    }

    internal interface IActivateFactSource
    {
        bool TryGet(string cardId, out ActivateFact fact);
    }

    internal sealed class ActivateFactSource : IActivateFactSource
    {
        private readonly ActivateRegistry _registry;

        internal ActivateFactSource()
        {
            _registry = new ActivateRegistry();
            _registry.LoadFromConfiguredRoot();
        }

        internal ActivateFactSource(ActivateRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException("registry");
        }

        public bool TryGet(string cardId, out ActivateFact fact)
        {
            fact = new ActivateFact();
            Season14MechanicCardFact source;
            if (!_registry.TryGet(cardId, out source)
                || !string.Equals(source.CardId, cardId, StringComparison.Ordinal))
                return false;

            fact = new ActivateFact
            {
                CardId = source.CardId,
                NameZhCn = source.NameZhCn,
                NameEnUs = source.NameEnUs,
                TextZhCn = source.TextZhCn,
                TextEnUs = source.TextEnUs,
                Type = source.Type,
                Cost = source.Cost,
                ActivateCost = ParseActivateCost(source.TextEnUs, source.TextZhCn),
                TechLevel = source.TechLevel,
                Attack = source.Attack,
                Health = source.Health,
                IsBattlegroundsPoolMinion = source.IsBattlegroundsPoolMinion,
            };
            return true;
        }

        internal static int ParseActivateCost(string textEnUs, string textCn)
        {
            int value;
            if (TryParseEnglish(textEnUs, out value) || TryParseChinese(textCn, out value))
                return value;
            return 0;
        }

        private static bool TryParseEnglish(string text, out int cost)
        {
            cost = 0;
            if (string.IsNullOrEmpty(text)) return false;
            const string marker = "Activate (";
            int start = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (start < 0) return false;
            start += marker.Length;
            int end = start;
            while (end < text.Length && char.IsDigit(text[end])) end++;
            if (end == start || end >= text.Length || text[end] != ')') return false;
            return int.TryParse(text.Substring(start, end - start), out cost) && cost >= 0;
        }

        private static bool TryParseChinese(string text, out int cost)
        {
            cost = 0;
            if (string.IsNullOrEmpty(text)) return false;
            const string marker = "发动（";
            int start = text.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) return false;
            start += marker.Length;
            int end = start;
            while (end < text.Length && char.IsDigit(text[end])) end++;
            if (end == start || end >= text.Length || text[end] != '）') return false;
            return int.TryParse(text.Substring(start, end - start), out cost) && cost >= 0;
        }
    }
}
