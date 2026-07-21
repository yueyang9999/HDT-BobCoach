using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace BobCoach.Engine
{
    /// <summary>单帧内不可变的有效游戏规则。消费者不得直接重新解释畸变注册表。</summary>
    public sealed class EffectiveGameRules
    {
        private static readonly EffectiveGameRules DefaultRules = new EffectiveGameRules(
            null, null, true, null, false, 6, 3, "standard_discover",
            0, null, null, null, null, null, null, null, null, null,
            new StartResourceExpectation[0],
            new ScheduledGrant[0], new SecondaryHeroPowerRule[0], new TimewarpVisit[0],
            new TimewarpOfferRule[0], new TimewarpPoolMergeRule[0],
            0, true, 0, null, false,
            new string[0], new string[0]);
        private readonly ReadOnlyCollection<ScheduledGrant> _scheduledGrants;
        private readonly ReadOnlyCollection<StartResourceExpectation> _startResourceExpectations;
        private readonly ReadOnlyCollection<SecondaryHeroPowerRule> _secondaryHeroPowers;
        private readonly ReadOnlyCollection<TimewarpVisit> _timewarpVisits;
        private readonly ReadOnlyCollection<TimewarpOfferRule> _timewarpOfferRules;
        private readonly ReadOnlyCollection<string> _sourceIds;
        private readonly ReadOnlyCollection<string> _conflicts;

        internal EffectiveGameRules(
            int? minionPurchaseCostOverride,
            int? firstMinionPurchaseCost,
            bool manualRefreshAllowed,
            int? refreshCostOverride,
            bool refreshAfterPurchase,
            int maxTavernTier,
            int goldenCopyRequirement,
            string goldenRewardOverride,
            int startArmorDelta,
            FirstPurchaseExtraCopyRule firstPurchaseExtraCopy,
            UpgradePrizeRule upgradePrize,
            PortalInBottleRule portalInBottleAtTurnStart,
            SharedYoggWheelRule sharedYoggWheel,
            SharedCardVoteRule sharedCardVote,
            BuddyPoolRule buddyPool,
            AllHeroesOverrideRule allHeroesOverride,
            SecondHeroPowerDiscoverRule secondHeroPowerDiscover,
            TeammateGoldTransferRule teammateGoldTransfer,
            IList<StartResourceExpectation> startResourceExpectations,
            IList<ScheduledGrant> scheduledGrants,
            IList<SecondaryHeroPowerRule> secondaryHeroPowers,
            IList<TimewarpVisit> timewarpVisits,
            IList<TimewarpOfferRule> timewarpOfferRules,
            IList<TimewarpPoolMergeRule> timewarpPoolMergeRules,
            int unscheduledRandomTimewarpVisitCount,
            bool lesserTimewarpEnabled,
            int timewarpMarkDelta,
            int? sharedTimewarpMarkBudget,
            bool carryTimewarpMarksToGreater,
            IList<string> sourceIds,
            IList<string> conflicts,
            ActiveTrinketContext activeTrinkets = null)
        {
            MinionPurchaseCostOverride = minionPurchaseCostOverride;
            FirstMinionPurchaseCost = firstMinionPurchaseCost;
            ManualRefreshAllowed = manualRefreshAllowed;
            RefreshCostOverride = refreshCostOverride;
            RefreshAfterPurchase = refreshAfterPurchase;
            MaxTavernTier = maxTavernTier;
            GoldenCopyRequirement = goldenCopyRequirement;
            GoldenRewardOverride = goldenRewardOverride;
            StartArmorDelta = startArmorDelta;
            FirstPurchaseExtraCopy = firstPurchaseExtraCopy;
            UpgradePrize = upgradePrize;
            PortalInBottleAtTurnStart = portalInBottleAtTurnStart;
            SharedYoggWheel = sharedYoggWheel;
            SharedCardVote = sharedCardVote;
            AllHeroesOverride = allHeroesOverride;
            SecondHeroPowerDiscover = secondHeroPowerDiscover;
            TeammateGoldTransfer = teammateGoldTransfer;
            CardPoolRules = new EffectiveCardPoolRules(buddyPool, timewarpPoolMergeRules);
            _startResourceExpectations = new ReadOnlyCollection<StartResourceExpectation>(
                new List<StartResourceExpectation>(startResourceExpectations
                    ?? new StartResourceExpectation[0]));
            _scheduledGrants = new ReadOnlyCollection<ScheduledGrant>(
                new List<ScheduledGrant>(scheduledGrants ?? new ScheduledGrant[0]));
            _secondaryHeroPowers = new ReadOnlyCollection<SecondaryHeroPowerRule>(
                new List<SecondaryHeroPowerRule>(secondaryHeroPowers ?? new SecondaryHeroPowerRule[0]));
            _timewarpVisits = new ReadOnlyCollection<TimewarpVisit>(
                new List<TimewarpVisit>(timewarpVisits ?? new TimewarpVisit[0]));
            _timewarpOfferRules = new ReadOnlyCollection<TimewarpOfferRule>(
                new List<TimewarpOfferRule>(timewarpOfferRules ?? new TimewarpOfferRule[0]));
            UnscheduledRandomTimewarpVisitCount = unscheduledRandomTimewarpVisitCount;
            LesserTimewarpEnabled = lesserTimewarpEnabled;
            TimewarpMarkDelta = timewarpMarkDelta;
            SharedTimewarpMarkBudget = sharedTimewarpMarkBudget;
            CarryTimewarpMarksToGreater = carryTimewarpMarksToGreater;
            _sourceIds = new ReadOnlyCollection<string>(new List<string>(sourceIds ?? new string[0]));
            _conflicts = new ReadOnlyCollection<string>(new List<string>(conflicts ?? new string[0]));
            ActiveTrinkets = activeTrinkets ?? ActiveTrinketContext.Empty;
        }

        public int? MinionPurchaseCostOverride { get; private set; }
        public int? FirstMinionPurchaseCost { get; private set; }
        public bool ManualRefreshAllowed { get; private set; }
        public int? RefreshCostOverride { get; private set; }
        public bool RefreshAfterPurchase { get; private set; }
        public int MaxTavernTier { get; private set; }
        public int GoldenCopyRequirement { get; private set; }
        public string GoldenRewardOverride { get; private set; }
        public int StartArmorDelta { get; private set; }
        public FirstPurchaseExtraCopyRule FirstPurchaseExtraCopy { get; private set; }
        public UpgradePrizeRule UpgradePrize { get; private set; }
        public PortalInBottleRule PortalInBottleAtTurnStart { get; private set; }
        public SharedYoggWheelRule SharedYoggWheel { get; private set; }
        public SharedCardVoteRule SharedCardVote { get; private set; }
        public AllHeroesOverrideRule AllHeroesOverride { get; private set; }
        public SecondHeroPowerDiscoverRule SecondHeroPowerDiscover { get; private set; }
        public TeammateGoldTransferRule TeammateGoldTransfer { get; private set; }
        public EffectiveCardPoolRules CardPoolRules { get; private set; }
        public IList<StartResourceExpectation> StartResourceExpectations
        {
            get { return _startResourceExpectations; }
        }
        public IList<ScheduledGrant> ScheduledGrants { get { return _scheduledGrants; } }
        public IList<SecondaryHeroPowerRule> SecondaryHeroPowers { get { return _secondaryHeroPowers; } }
        public IList<TimewarpVisit> TimewarpVisits { get { return _timewarpVisits; } }
        public IList<TimewarpOfferRule> TimewarpOfferRules { get { return _timewarpOfferRules; } }
        public IList<TimewarpPoolMergeRule> TimewarpPoolMergeRules
        {
            get { return CardPoolRules.TimewarpPoolMergeRules; }
        }
        public int UnscheduledRandomTimewarpVisitCount { get; private set; }
        public bool LesserTimewarpEnabled { get; private set; }
        public int TimewarpMarkDelta { get; private set; }
        public int? SharedTimewarpMarkBudget { get; private set; }
        public bool CarryTimewarpMarksToGreater { get; private set; }
        public IList<string> SourceIds { get { return _sourceIds; } }
        public IList<string> Conflicts { get { return _conflicts; } }
        public ActiveTrinketContext ActiveTrinkets { get; private set; }
        public static EffectiveGameRules Default { get { return DefaultRules; } }

        internal EffectiveGameRules WithActiveTrinkets(ActiveTrinketContext context)
        {
            return new EffectiveGameRules(
                MinionPurchaseCostOverride, FirstMinionPurchaseCost, ManualRefreshAllowed,
                RefreshCostOverride, RefreshAfterPurchase, MaxTavernTier, GoldenCopyRequirement,
                GoldenRewardOverride, StartArmorDelta, FirstPurchaseExtraCopy, UpgradePrize,
                PortalInBottleAtTurnStart, SharedYoggWheel, SharedCardVote, CardPoolRules.BuddyPool,
                AllHeroesOverride, SecondHeroPowerDiscover, TeammateGoldTransfer,
                StartResourceExpectations, ScheduledGrants, SecondaryHeroPowers, TimewarpVisits,
                TimewarpOfferRules, TimewarpPoolMergeRules, UnscheduledRandomTimewarpVisitCount,
                LesserTimewarpEnabled, TimewarpMarkDelta, SharedTimewarpMarkBudget,
                CarryTimewarpMarksToGreater, SourceIds, Conflicts,
                context ?? ActiveTrinketContext.Empty);
        }
    }
}
