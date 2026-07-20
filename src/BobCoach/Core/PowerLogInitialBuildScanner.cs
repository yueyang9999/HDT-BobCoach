using System;
using System.IO;

namespace BobCoach.Engine
{
    /// <summary>
    /// 从仍由炉石写入的 Power.log 中回扫当前客户端构建号。
    /// </summary>
    internal static class PowerLogInitialBuildScanner
    {
        internal static bool TryScan(string logPath, PowerLogParser parser)
        {
            if (string.IsNullOrEmpty(logPath) || parser == null) return false;

            using (var stream = new FileStream(
                logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.IndexOf("BuildNumber=", StringComparison.Ordinal) < 0) continue;
                    parser.ParseLine(line);
                    if (parser.CurrentBuildNumber > 0) return true;
                }
            }

            return false;
        }
    }
}
