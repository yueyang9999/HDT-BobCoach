using System;
using System.Collections.Generic;
using System.Linq;

namespace BobCoach.Engine
{
    public static class SequenceSearchPlanner
    {
        public static List<PlannedStep> Search(
            IReadOnlyList<PlannedStep> roots,
            Func<PlannedStep, IReadOnlyList<PlannedStep>> expand,
            Func<IReadOnlyList<PlannedStep>, double> score,
            int maxDepth,
            int beamWidth)
        {
            return SearchCore(
                roots,
                sequence => expand != null
                    ? expand(sequence[sequence.Count - 1])
                    : null,
                score,
                maxDepth,
                beamWidth);
        }

        public static List<PlannedStep> Search(
            IReadOnlyList<PlannedStep> roots,
            Func<IReadOnlyList<PlannedStep>, PlannedStep,
                IReadOnlyList<PlannedStep>> expand,
            Func<IReadOnlyList<PlannedStep>, double> score,
            int maxDepth,
            int beamWidth)
        {
            return SearchCore(
                roots,
                sequence => expand != null
                    ? expand(sequence, sequence[sequence.Count - 1])
                    : null,
                score,
                maxDepth,
                beamWidth);
        }

        private static List<PlannedStep> SearchCore(
            IReadOnlyList<PlannedStep> roots,
            Func<List<PlannedStep>, IReadOnlyList<PlannedStep>> expand,
            Func<IReadOnlyList<PlannedStep>, double> score,
            int maxDepth,
            int beamWidth)
        {
            if (roots == null || roots.Count == 0 || score == null)
                return new List<PlannedStep>();
            if (maxDepth < 1) maxDepth = 1;
            if (beamWidth < 1) beamWidth = 1;

            var beam = roots
                .Where(step => step != null && step.Action != null)
                .Select(step => new List<PlannedStep> { step })
                .OrderByDescending(sequence => NormalizeScore(score(sequence)))
                .ThenBy(sequence => SequenceKey(sequence), StringComparer.Ordinal)
                .Take(beamWidth)
                .ToList();

            for (int depth = 1; depth < maxDepth; depth++)
            {
                var candidates = new List<List<PlannedStep>>(beam);
                if (expand != null)
                {
                    foreach (var sequence in beam)
                    {
                        var last = sequence[sequence.Count - 1];
                        foreach (var next in expand(sequence) ?? new PlannedStep[0])
                        {
                            if (next == null || next.Action == null) continue;
                            var copy = new List<PlannedStep>(sequence) { next };
                            candidates.Add(copy);
                        }
                    }
                }
                if (candidates.Count == 0) break;
                beam = candidates
                    .OrderByDescending(sequence => NormalizeScore(score(sequence)))
                    .ThenBy(sequence => SequenceKey(sequence), StringComparer.Ordinal)
                    .Take(beamWidth)
                    .ToList();
            }

            return beam.Count == 0
                ? new List<PlannedStep>()
                : beam[0];
        }

        private static double NormalizeScore(double value)
        {
            if (double.IsNaN(value)) return double.NegativeInfinity;
            if (double.IsPositiveInfinity(value)) return double.MaxValue;
            if (double.IsNegativeInfinity(value)) return double.MinValue;
            return value;
        }

        private static string SequenceKey(IEnumerable<PlannedStep> sequence)
        {
            return string.Join(";", sequence.Select(step =>
                step == null || step.Action == null
                    ? "-"
                    : step.Action.Type + ":" + step.Action.TargetIndex
                        + ":" + (step.Action.CardId ?? "")));
        }
    }
}
