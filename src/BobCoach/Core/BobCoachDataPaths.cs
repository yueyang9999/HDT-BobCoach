using System;
using System.IO;

namespace BobCoach.Engine
{
    internal static class BobCoachDataPaths
    {
        internal const string RootEnvironmentVariable = "BOB_COACH_DATA_ROOT";

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

        internal static string Combine(string root, params string[] segments)
        {
            var path = root;
            foreach (var segment in segments) path = Path.Combine(path, segment);
            return path;
        }
    }
}
