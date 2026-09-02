using System;
using System.Collections.Generic;
using System.Linq;

namespace BobCoach.Engine
{
    public sealed class StrategySequencePlanner
    {
        private const int MaxDepth = 3;
        private const int BeamWidth = 8;
        private readonly ActionEnumerator _actions = new ActionEnumerator();
        private readonly Simulator _simulator = new Simulator();

        internal StrategyPlan Build(
            GameState state,
            StrategicMemorySnapshot memory,
            MainlinePlan mainline)
        {
            if (state == null || !state.GameActive
                || mainline == null || !state.NormalRecruitAvailable)
                return EmptyPlan(mainline);

            var objective = ResolveObjective(state, mainline);
            var roots = EnumerateRoots(state);
            var best = SequenceSearchPlanner.Search(
                roots,
                (sequence, previous) => ExpandSequence(state, sequence),
                sequence => ScoreSequence(sequence, state, mainline, memory),
                MaxDepth,
                BeamWidth);

            var plan = new StrategyPlan
            {
                Objective = objective,
                CompDir = mainline.CompDir ?? "",
                GuideId = mainline.GuideId ?? "",
                Steps = best ?? new List<PlannedStep>(),
                ExpectedValue = best != null
                    ? ScoreSequence(best, state, mainline, memory)
                    : double.NegativeInfinity,
                Risk = CalculateRisk(state),
                Reason = DescribeObjective(objective, mainline),
                FallbackActionKey = Fallback(state),
            };
            plan.PlanHash = StrategyPlanText.BuildPlanHash(plan);
            return plan;
        }

        private List<PlannedStep> EnumerateRoots(GameState state)
        {
            return _actions.Enumerate(state, state.HeroCardId, false)
                .Select(action => new PlannedStep
                {
                    Action = action,
                    Reason = "candidate",
                })
                .ToList();
        }

        private IReadOnlyList<PlannedStep> ExpandSequence(
            GameState root,
            IReadOnlyList<PlannedStep> sequence)
        {
            var current = root;
            foreach (var step in sequence)
            {
                if (step == null || step.Action == null)
                    return new List<PlannedStep>();
                current = _simulator.Simulate(current, step.Action);
                if (current == null) return new List<PlannedStep>();
            }
            return _actions.Enumerate(current, current.HeroCardId, false)
                .Select(action => new PlannedStep
                {
                    Action = action,
                    Reason = "follow-up",
                })
                .ToList();
        }

        private double ScoreSequence(
            IReadOnlyList<PlannedStep> sequence,
            GameState root,
            MainlinePlan mainline,
            StrategicMemorySnapshot memory)
        {
            var current = root;
            double value = 0;
            double discount = 1.0;
            foreach (var step in sequence)
            {
                current = _simulator.Simulate(current, step.Action);
                if (current == null) return double.NegativeInfinity;
                value += discount * ScoreState(current, mainline, memory);
                discount *= 0.82;
            }
            return value;
        }

        private double ScoreState(
            GameState state,
            MainlinePlan mainline,
            StrategicMemorySnapshot memory)
        {
            double board = state.BoardMinions == null
                ? 0
                : state.BoardMinions.Sum(minion =>
                    minion == null
                        ? 0
                        : minion.Attack * 0.65
                            + minion.Health * 0.35
                            + minion.Tier * 0.25);
            double core = mainline.CoreCardIds == null
                ? 0
                : AllMinions(state).Count(minion =>
                    minion != null && IsCore(minion.CardId, mainline.CoreCardIds));
            double hand = state.HandMinions == null
                ? 0
                : state.HandMinions.Sum(minion =>
                    minion != null
                        && IsUtilityText(
                            minion.CardId, minion.CardName, minion.CardText)
                        ? 0.6 : 0);
            double remembered = MemoryEvidenceScore(state, memory);
            double futureGold = TurnProjector.Advance(state)?.Gold ?? 0;
            double risk = CalculateRisk(state);
            double tech = OpponentTechAdvisor.IsTechCardWorthKeeping(
                "BG23_318", state.Opponents) ? 0.8 : 0;
            double opponentProfile = OpponentTechAdvisor
                .AnalyzeOpponentBoard(state.Opponents).HasBoardData ? 0.2 : 0;
            return board * 0.30
                + core * 1.30
                + hand * 0.35
                + remembered * 0.22
                + futureGold * 0.12
                - risk * 0.50
                + tech
                + opponentProfile;
        }

        private static double CalculateRisk(GameState state)
        {
            double missingHealth = Math.Max(0, 12 - state.Health - state.Armor);
            double opponent = OpponentTechAdvisor
                .AnalyzeOpponentBoard(state.Opponents).MaxStats;
            double board = state.BoardMinions == null
                ? 0
                : state.BoardMinions.Sum(minion =>
                    minion == null ? 0 : minion.Attack + minion.Health);
            return missingHealth * 0.45
                + (opponent > board * 0.65 ? 0.8 : 0);
        }

        private static StrategyObjective ResolveObjective(
            GameState state, MainlinePlan mainline)
        {
            if (state.Health + state.Armor <= 12) return StrategyObjective.Survival;
            if (state.Turn <= 5) return StrategyObjective.Economy;
            if (mainline.MissingCoreCardNames.Count > 0)
                return StrategyObjective.SearchCore;
            var profile = OpponentTechAdvisor.AnalyzeOpponentBoard(state.Opponents);
            if (profile.BigThreatCount >= 2) return StrategyObjective.TechPivot;
            return StrategyObjective.Flexible;
        }

        private static double MemoryEvidenceScore(
            GameState state,
            StrategicMemorySnapshot memory)
        {
            if (state == null || memory == null) return 0;
            var observedCores = memory.ObservedCoreCardIds
                ?? new HashSet<string>(StringComparer.Ordinal);
            var observedPairs = memory.ObservedPairCardIds
                ?? new HashSet<string>(StringComparer.Ordinal);
            double evidence = 0;
            foreach (var minion in AllMinions(state))
            {
                if (minion == null || string.IsNullOrEmpty(minion.CardId)) continue;
                string baseId = NormalizeGolden(minion.CardId);
                if (observedCores.Contains(baseId)) evidence += 0.35;
                if (observedPairs.Contains(baseId)) evidence += 0.60;
            }
            return evidence;
        }

        private static string DescribeObjective(
            StrategyObjective objective, MainlinePlan mainline)
        {
            switch (objective)
            {
                case StrategyObjective.Survival:
                    return "health-and-board-survival";
                case StrategyObjective.Economy:
                    return "future-gold-and-hand-resource";
                case StrategyObjective.SearchCore:
                    return "find-missing-core";
                case StrategyObjective.TechPivot:
                    return "opponent-tech-pressure";
                case StrategyObjective.Stabilize:
                    return "stabilize-board";
                default:
                    return "flexible-mainline";
            }
        }

        private static string Fallback(GameState state)
        {
            if (state == null) return "-";
            return state.Gold >= 1
                ? "Refresh|-1|"
                : "FreezeShop|0|";
        }

        private static StrategyPlan EmptyPlan(MainlinePlan mainline)
        {
            return new StrategyPlan
            {
                CompDir = mainline != null ? mainline.CompDir ?? "" : "",
                GuideId = mainline != null ? mainline.GuideId ?? "" : "",
                Reason = "state-unavailable",
                PlanHash = "none",
            };
        }

        private static IEnumerable<MinionData> AllMinions(GameState state)
        {
            if (state.BoardMinions != null)
                foreach (var minion in state.BoardMinions)
                    yield return minion;
            if (state.HandMinions != null)
                foreach (var minion in state.HandMinions)
                    yield return minion;
        }

        private static bool IsCore(
            string cardId, ICollection<string> coreCardIds)
        {
            if (string.IsNullOrEmpty(cardId) || coreCardIds == null) return false;
            return coreCardIds.Contains(cardId)
                || (cardId.EndsWith("_G", StringComparison.Ordinal)
                    && coreCardIds.Contains(
                        cardId.Substring(0, cardId.Length - 2)));
        }

        private static string NormalizeGolden(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return "";
            return cardId.EndsWith("_G", StringComparison.Ordinal)
                ? cardId.Substring(0, cardId.Length - 2)
                : cardId;
        }

        private static bool IsUtilityText(
            string cardId, string cardName, string cardText)
        {
            string text = (cardId ?? "") + " "
                + (cardName ?? "") + " " + (cardText ?? "");
            return text.Contains("铸币")
                || text.Contains("金币")
                || text.Contains("刷新")
                || text.Contains("发现")
                || text.Contains("塑造法术");
        }
    }
}
