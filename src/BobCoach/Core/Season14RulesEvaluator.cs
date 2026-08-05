using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

namespace BobCoach.Engine
{
    /// <summary>
    /// Season14 single-player mechanic rules facade over the generated registries.
    /// All lookups fail closed until all four registries are loaded.
    /// </summary>
    internal sealed class Season14RulesEvaluator
    {
        private static readonly IReadOnlyList<string> DefaultExcludedAnomalyIds =
            new ReadOnlyCollection<string>(new[] { "BG36_Anomaly_002" });

        private ActivateRegistry _activateRegistry;
        private LockboxRegistry _lockboxRegistry;
        private FishbaitRegistry _fishbaitRegistry;
        private DarkGiftRegistry _darkGiftRegistry;
        private SeasonMechanicProfile _mechanicProfile;

        internal Season14RulesEvaluator()
        {
        }

        internal bool IsLoaded
        {
            get
            {
                return _activateRegistry != null && _activateRegistry.IsLoaded
                    && _lockboxRegistry != null && _lockboxRegistry.IsLoaded
                    && _fishbaitRegistry != null && _fishbaitRegistry.IsLoaded
                    && _darkGiftRegistry != null && _darkGiftRegistry.IsLoaded
                    && _mechanicProfile != null && _mechanicProfile.IsLoaded;
            }
        }

        internal IReadOnlyList<string> ExcludedAnomalyIds
        {
            get { return DefaultExcludedAnomalyIds; }
        }

        internal void LoadFromConfiguredRoot()
        {
            var mechanicProfile = new SeasonMechanicProfile();
            mechanicProfile.LoadFromConfiguredRoot();
            var activate = new ActivateRegistry();
            activate.LoadFromConfiguredRoot();
            var lockbox = new LockboxRegistry();
            lockbox.LoadFromConfiguredRoot();
            var fishbait = new FishbaitRegistry();
            fishbait.LoadFromConfiguredRoot();
            var darkGift = new DarkGiftRegistry();
            darkGift.LoadFromConfiguredRoot();
            LoadFromRegistries(activate, lockbox, fishbait, darkGift);
            _mechanicProfile = mechanicProfile;
        }

        internal void LoadFromRegistries(
            ActivateRegistry activateRegistry,
            LockboxRegistry lockboxRegistry,
            FishbaitRegistry fishbaitRegistry,
            DarkGiftRegistry darkGiftRegistry)
        {
            LoadFromRegistries(
                activateRegistry,
                lockboxRegistry,
                fishbaitRegistry,
                darkGiftRegistry,
                null);
        }

        internal void LoadFromRegistries(
            ActivateRegistry activateRegistry,
            LockboxRegistry lockboxRegistry,
            FishbaitRegistry fishbaitRegistry,
            DarkGiftRegistry darkGiftRegistry,
            SeasonMechanicProfile mechanicProfile)
        {
            _mechanicProfile = mechanicProfile;
            _activateRegistry = activateRegistry;
            _lockboxRegistry = lockboxRegistry;
            _fishbaitRegistry = fishbaitRegistry;
            _darkGiftRegistry = darkGiftRegistry;
        }

        internal bool IsMechanicEnabled(string mechanic)
        {
            return _mechanicProfile != null
                && _mechanicProfile.IsMechanicEnabled(mechanic);
        }

        internal bool TryGetActivate(string cardId, out ActivateFact fact)
        {
            fact = null;
            if (!IsLoaded || string.IsNullOrEmpty(cardId)) return false;
            Season14MechanicCardFact source;
            if (!_activateRegistry.TryGet(cardId, out source)) return false;
            fact = new ActivateFact
            {
                CardId = source.CardId,
                NameZhCn = source.NameZhCn,
                NameEnUs = source.NameEnUs,
                TextZhCn = source.TextZhCn,
                TextEnUs = source.TextEnUs,
                Type = source.Type,
                Cost = source.Cost,
                TechLevel = source.TechLevel,
                Attack = source.Attack,
                Health = source.Health,
                IsBattlegroundsPoolMinion = source.IsBattlegroundsPoolMinion,
            };
            return true;
        }

        internal bool TryGetLockbox(string cardId, out LockboxFact fact)
        {
            fact = null;
            if (!IsLoaded || string.IsNullOrEmpty(cardId)) return false;
            Season14MechanicCardFact source;
            if (!_lockboxRegistry.TryGet(cardId, out source)) return false;
            fact = new LockboxFact
            {
                CardId = source.CardId,
                NameZhCn = source.NameZhCn,
                NameEnUs = source.NameEnUs,
                TextZhCn = source.TextZhCn,
                TextEnUs = source.TextEnUs,
                Type = source.Type,
                Cost = source.Cost,
                TechLevel = source.TechLevel,
                Attack = source.Attack,
                Health = source.Health,
                IsBattlegroundsPoolMinion = source.IsBattlegroundsPoolMinion,
            };
            return true;
        }

        internal bool TryGetFishbait(string cardId, out FishbaitFact fact)
        {
            fact = null;
            if (!IsLoaded || string.IsNullOrEmpty(cardId)) return false;
            Season14MechanicCardFact source;
            if (!_fishbaitRegistry.TryGet(cardId, out source)) return false;
            fact = new FishbaitFact
            {
                CardId = source.CardId,
                NameZhCn = source.NameZhCn,
                NameEnUs = source.NameEnUs,
                TextZhCn = source.TextZhCn,
                TextEnUs = source.TextEnUs,
                Type = source.Type,
                Cost = source.Cost,
                TechLevel = source.TechLevel,
                Attack = source.Attack,
                Health = source.Health,
                IsBattlegroundsPoolMinion = source.IsBattlegroundsPoolMinion,
            };
            return true;
        }

        internal bool TryGetDarkGift(string cardId, out DarkGiftFact fact)
        {
            fact = null;
            if (!IsLoaded || string.IsNullOrEmpty(cardId)) return false;
            DarkGiftRegistry.DarkGiftEffectFact source;
            if (!_darkGiftRegistry.TryGet(cardId, out source)) return false;
            fact = new DarkGiftFact
            {
                CardId = source.CardId,
                NameZhCn = source.NameZhCn,
                NameEnUs = source.NameEnUs,
                Type = source.Type,
                TextZhCn = source.TextZhCn,
                TextEnUs = source.TextEnUs,
            };
            return true;
        }

        internal bool TryGetOfferingTier(int turn, out string tier)
        {
            tier = null;
            if (!IsLoaded || turn <= 0) return false;
            foreach (var row in _darkGiftRegistry.OfferingRows)
            {
                int rowTurn;
                if (!int.TryParse(
                        row.Turn,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out rowTurn)
                    || rowTurn != turn)
                    continue;
                tier = row.MinionTier;
                return !string.IsNullOrEmpty(tier);
            }
            return false;
        }

        internal bool TryGetEligibleGifts(int turn, out IList<string> gifts)
        {
            gifts = null;
            if (!IsLoaded || turn <= 0) return false;
            var eligible = new List<string>();
            foreach (var gift in _darkGiftRegistry.OfficialGifts)
            {
                if (string.IsNullOrEmpty(gift.Name)) continue;

                int minimumTurn;
                if (!int.TryParse(
                        gift.MinimumTurn,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out minimumTurn)
                    || turn < minimumTurn)
                    continue;

                if (!string.Equals(gift.MaximumTurn, "-", StringComparison.Ordinal))
                {
                    int maximumTurn;
                    if (!int.TryParse(
                            gift.MaximumTurn,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out maximumTurn)
                        || turn > maximumTurn)
                        continue;
                }

                eligible.Add(gift.Name);
            }
            gifts = eligible;
            return true;
        }
    }
}
