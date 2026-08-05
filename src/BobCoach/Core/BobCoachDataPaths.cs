using System;
using System.IO;

namespace BobCoach.Engine
{
    internal static class BobCoachDataPaths
    {
        internal const string RootEnvironmentVariable = "BOB_COACH_DATA_ROOT";

        internal const string SeasonSelectorEnvironmentVariable = "BOB_COACH_SEASON_SELECTOR";

        internal static string Root
        {
            get
            {
                var fallback = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "bob-coach");
                return ResolveRoot(
                    Environment.GetEnvironmentVariable(RootEnvironmentVariable),
                    fallback);
            }
        }

        internal static string ResolveRoot(string configuredRoot, string fallbackRoot)
        {
            if (string.IsNullOrWhiteSpace(configuredRoot)) return fallbackRoot;
            try
            {
                var value = configuredRoot.Trim();
                return Path.IsPathRooted(value) ? Path.GetFullPath(value) : fallbackRoot;
            }
            catch
            {
                return fallbackRoot;
            }
        }

        internal static string GetPath(params string[] segments)
        {
            return Combine(Root, segments);
        }

        internal static string GetTrustedDecisionEventsPath()
        {
            return GetTrustedDecisionEventsPath(FindRepositoryRoot());
        }

        internal static string GetTrustedDecisionEventsPath(string repositoryRoot)
        {
            var root = ResolveTrustedDecisionEventRoot(
                Environment.GetEnvironmentVariable(RootEnvironmentVariable),
                repositoryRoot);
            return Combine(root, "decision-events.jsonl");
        }

        internal static string ResolveTrustedDecisionEventRoot(
            string configuredRoot, string repositoryRoot)
        {
            if (string.IsNullOrWhiteSpace(configuredRoot))
                throw new InvalidOperationException(
                    RootEnvironmentVariable + " is required for trusted decision-event recording");

            var value = configuredRoot.Trim();
            if (!Path.IsPathRooted(value))
                throw new InvalidOperationException(
                    RootEnvironmentVariable + " must be an absolute path");

            string resolved;
            try
            {
                resolved = Path.GetFullPath(value);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    RootEnvironmentVariable + " is not a valid path", ex);
            }

            var repoRoot = NormalizePath(repositoryRoot);
            if (!IsRepositoryRoot(repoRoot))
                throw new InvalidOperationException(
                    "repository root must contain a .git marker for trusted decision-event recording");

            var appDataBobCoach = NormalizePath(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "bob-coach"));
            if (IsPathWithinOrEqual(appDataBobCoach, resolved))
                throw new InvalidOperationException(
                    "Trusted decision-event root must not be under %APPDATA%\\bob-coach");

            var systemRoot = Path.GetPathRoot(
                Environment.GetFolderPath(Environment.SpecialFolder.System));
            if (string.Equals(Path.GetPathRoot(resolved), systemRoot,
                StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Trusted decision-event root must not be on the system drive");

            if (IsPathWithinOrEqual(repoRoot, resolved) ||
                IsPathWithinOrEqual(resolved, repoRoot))
                throw new InvalidOperationException(
                    "Trusted decision-event root must be outside the repository");

            if (!Directory.Exists(resolved))
                throw new InvalidOperationException(
                    "Trusted decision-event root does not exist: " + resolved);

            RequireNoReparsePoint(resolved);
            return resolved;
        }

        internal static string Combine(string root, params string[] segments)
        {
            var path = root;
            foreach (var segment in segments) path = Path.Combine(path, segment);
            return path;
        }

        private static string FindRepositoryRoot()
        {
            foreach (var start in new[]
            {
                AppDomain.CurrentDomain.BaseDirectory,
                Environment.CurrentDirectory
            })
            {
                var current = new DirectoryInfo(start);
                while (current != null)
                {
                    if (IsRepositoryRoot(current.FullName)) return current.FullName;
                    current = current.Parent;
                }
            }
            throw new InvalidOperationException(
                "repository root not found for trusted decision-event recording");
        }

        private static bool IsRepositoryRoot(string path)
        {
            return Directory.Exists(Path.Combine(path, ".git")) ||
                File.Exists(Path.Combine(path, ".git"));
        }

        private static string NormalizePath(string path)
        {
            var resolved = Path.GetFullPath(path);
            var root = Path.GetPathRoot(resolved);
            if (string.Equals(resolved, root, StringComparison.OrdinalIgnoreCase))
                return root;
            return resolved.TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static bool IsPathWithinOrEqual(string parent, string candidate)
        {
            var normalizedParent = NormalizePath(parent);
            var normalizedCandidate = NormalizePath(candidate);
            var separator = Path.DirectorySeparatorChar.ToString();
            var parentHasSeparator =
                normalizedParent.EndsWith(separator, StringComparison.OrdinalIgnoreCase) ||
                normalizedParent.EndsWith(
                    Path.AltDirectorySeparatorChar.ToString(),
                    StringComparison.OrdinalIgnoreCase);
            var prefix = parentHasSeparator
                ? normalizedParent
                : normalizedParent + separator;
            return string.Equals(
                    normalizedCandidate, normalizedParent, StringComparison.OrdinalIgnoreCase) ||
                normalizedCandidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static void RequireNoReparsePoint(string path)
        {
            var resolved = NormalizePath(path);
            var pathRoot = Path.GetPathRoot(resolved);
            var current = pathRoot;
            var relative = resolved.Substring(pathRoot.Length);
            foreach (var segment in relative.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                if (!File.Exists(current) && !Directory.Exists(current)) continue;
                var attributes = File.GetAttributes(current);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException(
                        "Trusted decision-event root contains a reparse point: " + current);
            }
        }
    }
}
