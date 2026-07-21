using System;
using System.Collections.Generic;

namespace BobCoach.Engine
{
    /// <summary>Versioned registry of deterministic local equipped-trinket rules.</summary>
    public sealed class TrinketEffectRegistry
    {
        public const string RuleSetVersion = "hdt-1.53.5-hearthdb-2026-07-22-r2";
        public const string DesignerEyepatchCardId = "BG30_MagicItem_439";
        public const string CowrieNecklaceCardId = "BG35_MagicItem_921";
        public const string IronforgeAnvilCardId = "BG30_MagicItem_403";
        public const string SlammaStickerCardId = "BG30_MagicItem_540";
        public const string EmeraldDreamcatcherCardId = "BG30_MagicItem_542";
        public const string StegodonPortraitCardId = "BG35_MagicItem_702";
        public const string TinyfinOnesieCardId = "BG30_MagicItem_441";
        public const string DramalocStickerCardId = "BG35_MagicItem_754";
        public const string EternalPortraitCardId = "BG30_MagicItem_301";
        public const string RivendarePortraitCardId = "BG30_MagicItem_310";
        public const string HolyMalletCardId = "BG30_MagicItem_902";
        public const string TrainingCertificateCardId = "BG30_MagicItem_962";
        public const string ValorousMedallionCardId = "BG30_MagicItem_970";
        public const string GreaterValorousMedallionCardId = "BG30_MagicItem_970t";
        public const string BalefulIncenseCardId = "BG32_MagicItem_360";

        // Audited against the HDT 1.53.5 HearthDb snapshot. Cowrie's hard cost rule
        // must fail closed: localized text is never used to infer resource changes.
        private static readonly HashSet<string> StatGrantingTavernSpellCardIds =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "BG28_168", "BG28_169", "BG28_503", "BG28_520", "BG28_825",
                "BG28_845", "BG28_885", "BG28_888", "BG28_897", "BG28_966",
                "BG31_881", "BG31_886", "BG32_337", "BG32_814", "BG32_815",
                "BG33_248", "BG33_811", "BG33_812", "BG33_813", "BG33_817",
                "BG33_898", "BG34_272", "BG34_990", "BG35_149", "BG35_910",
                "BG35_911", "BG35_922", "BG35_951", "BG35_952",
            };

        public static bool IsStatGrantingTavernSpell(string cardId)
        {
            return !string.IsNullOrEmpty(cardId)
                && StatGrantingTavernSpellCardIds.Contains(cardId);
        }

        public ActiveTrinketContext Resolve(IEnumerable<string> activeCardIds)
        {
            if (activeCardIds == null) return ActiveTrinketContext.Empty;

            var resolved = new List<string>();
            var unknown = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            bool eyepatch = false, cowrie = false, anvil = false, slamma = false;
            bool dreamcatcher = false, stegodon = false, tinyfin = false, dramaloc = false;
            bool eternal = false, rivendare = false, mallet = false, training = false;
            bool valorous = false, greaterValorous = false, incense = false;

            foreach (string cardId in activeCardIds)
            {
                if (string.IsNullOrEmpty(cardId) || !seen.Add(cardId)) continue;
                switch (cardId)
                {
                    case DesignerEyepatchCardId: eyepatch = true; resolved.Add(cardId); break;
                    case CowrieNecklaceCardId: cowrie = true; resolved.Add(cardId); break;
                    case IronforgeAnvilCardId: anvil = true; resolved.Add(cardId); break;
                    case SlammaStickerCardId: slamma = true; resolved.Add(cardId); break;
                    case EmeraldDreamcatcherCardId: dreamcatcher = true; resolved.Add(cardId); break;
                    case StegodonPortraitCardId: stegodon = true; resolved.Add(cardId); break;
                    case TinyfinOnesieCardId: tinyfin = true; resolved.Add(cardId); break;
                    case DramalocStickerCardId: dramaloc = true; resolved.Add(cardId); break;
                    case EternalPortraitCardId: eternal = true; resolved.Add(cardId); break;
                    case RivendarePortraitCardId: rivendare = true; resolved.Add(cardId); break;
                    case HolyMalletCardId: mallet = true; resolved.Add(cardId); break;
                    case TrainingCertificateCardId: training = true; resolved.Add(cardId); break;
                    case ValorousMedallionCardId: valorous = true; resolved.Add(cardId); break;
                    case GreaterValorousMedallionCardId: greaterValorous = true; resolved.Add(cardId); break;
                    case BalefulIncenseCardId: incense = true; resolved.Add(cardId); break;
                    default: unknown.Add(cardId); break;
                }
            }

            if (resolved.Count == 0 && unknown.Count == 0) return ActiveTrinketContext.Empty;
            return new ActiveTrinketContext(
                resolved, unknown, eyepatch, cowrie, anvil, slamma,
                dreamcatcher, stegodon, tinyfin, dramaloc, eternal, rivendare,
                mallet, training, valorous, greaterValorous, incense);
        }
    }

    /// <summary>Per-game exact-ID rate limiter for unknown equipped-trinket diagnostics.</summary>
    public sealed class UnknownTrinketDiagnosticTracker
    {
        private readonly HashSet<string> _reportedCardIds =
            new HashSet<string>(StringComparer.Ordinal);

        public bool ShouldReport(string cardId)
        {
            return !string.IsNullOrEmpty(cardId) && _reportedCardIds.Add(cardId);
        }

        public void Reset()
        {
            _reportedCardIds.Clear();
        }
    }
}
