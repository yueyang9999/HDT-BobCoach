using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace BobCoach.Engine
{
    /// <summary>log.config 检查和修复状态</summary>
    public enum LogConfigStatus
    {
        OK,             // 已存在且完整
        Missing,        // 文件缺失，仅报告，不写入
        NeedsPatch,     // 文件不完整，仅报告拟议补丁
        Conflict,       // 检查后文件已变化，拒绝覆盖
        Created,        // 新建, 需要重启炉石
        Patched,        // 补全了缺失模块, 需要重启炉石
        Error,          // 操作失败
    }

    public sealed class LogConfigPlan
    {
        public LogConfigStatus Status { get; internal set; }
        public string ConfigPath { get; internal set; }
        public string ProposedContent { get; internal set; }
        public string OriginalContentSha256 { get; internal set; }
        public List<string> Changes { get; internal set; } = new List<string>();
    }

    /// <summary>
    /// 检查炉石 log.config，并仅通过已检查的计划执行显式写入。
    /// </summary>
    public static class LogConfigEnsurer
    {
        // BobCoach 必需的日志模块
        private static readonly Dictionary<string, Dictionary<string, string>> RequiredModules = new Dictionary<string, Dictionary<string, string>>
        {
            ["Power"] = new Dictionary<string, string>
            {
                ["LogLevel"] = "1",
                ["FilePrinting"] = "True",
                ["ConsolePrinting"] = "False",
                ["ScreenPrinting"] = "False",
                // Verbose=True 是 GameState.DebugPrintEntityChoices() 输出的开关(HDT官方配置同为True)。
                // 关闭时游戏不写发现/饰品选择块 → Power.log 选择信号链路全成死代码(07071121根因)。
                ["Verbose"] = "True",
            },
            ["Zone"] = new Dictionary<string, string>
            {
                ["LogLevel"] = "1",
                ["FilePrinting"] = "True",
                ["ConsolePrinting"] = "False",
                ["ScreenPrinting"] = "False",
                ["Verbose"] = "False",
            },
            ["BobsBag"] = new Dictionary<string, string>
            {
                ["LogLevel"] = "1",
                ["FilePrinting"] = "True",
                ["ConsolePrinting"] = "False",
                ["ScreenPrinting"] = "False",
                ["Verbose"] = "False",
            },
            ["Rachelle"] = new Dictionary<string, string>
            {
                ["LogLevel"] = "1",
                ["FilePrinting"] = "True",
                ["ConsolePrinting"] = "False",
                ["ScreenPrinting"] = "False",
                ["Verbose"] = "False",
            },
        };

        public static string HearthstoneConfigDir { get; private set; }
        public static string LogConfigPath { get; private set; }
        public static List<string> MissingModules { get; private set; } = new List<string>();

        public static LogConfigPlan Inspect()
        {
            MissingModules.Clear();
            try
            {
                FindHearthstoneDir();
                if (string.IsNullOrEmpty(HearthstoneConfigDir))
                {
                    return new LogConfigPlan
                    {
                        Status = LogConfigStatus.Error,
                        ConfigPath = "",
                        Changes = new List<string> { "未找到炉石安装目录" },
                    };
                }

                LogConfigPath = Path.Combine(HearthstoneConfigDir, "log.config");
                return InspectAtPath(LogConfigPath);
            }
            catch (Exception ex)
            {
                PluginLog("LogConfig inspect error: " + ex.Message);
                return new LogConfigPlan
                {
                    Status = LogConfigStatus.Error,
                    ConfigPath = LogConfigPath ?? "",
                    Changes = new List<string> { ex.Message },
                };
            }
        }

        public static LogConfigPlan InspectAtPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return new LogConfigPlan { Status = LogConfigStatus.Error, ConfigPath = path ?? "" };

            if (!File.Exists(path))
            {
                return new LogConfigPlan
                {
                    Status = LogConfigStatus.Missing,
                    ConfigPath = path,
                    ProposedContent = BuildDefaultContent(),
                    OriginalContentSha256 = "",
                    Changes = RequiredModules.Keys.Select(module => "新增 [" + module + "] 段").ToList(),
                };
            }

            try
            {
                MissingModules.Clear();
                var original = File.ReadAllText(path, System.Text.Encoding.UTF8);
                List<string> changes;
                var proposedLines = BuildPatchedLines(File.ReadAllLines(path, System.Text.Encoding.UTF8), out changes);
                return new LogConfigPlan
                {
                    Status = changes.Count == 0 ? LogConfigStatus.OK : LogConfigStatus.NeedsPatch,
                    ConfigPath = path,
                    ProposedContent = changes.Count == 0 ? original : string.Join("\r\n", proposedLines),
                    OriginalContentSha256 = Sha256(original),
                    Changes = changes,
                };
            }
            catch
            {
                return new LogConfigPlan { Status = LogConfigStatus.Error, ConfigPath = path };
            }
        }

        public static LogConfigStatus Apply(LogConfigPlan plan)
        {
            if (plan == null || string.IsNullOrWhiteSpace(plan.ConfigPath))
                return LogConfigStatus.Error;
            if (plan.Status == LogConfigStatus.Missing && File.Exists(plan.ConfigPath))
                return LogConfigStatus.Conflict;
            if (plan.Status == LogConfigStatus.NeedsPatch && !File.Exists(plan.ConfigPath))
                return LogConfigStatus.Conflict;
            if (plan.Status != LogConfigStatus.Missing && plan.Status != LogConfigStatus.NeedsPatch)
                return LogConfigStatus.Error;

            try
            {
                if (plan.Status == LogConfigStatus.NeedsPatch)
                {
                    var current = File.ReadAllText(plan.ConfigPath, System.Text.Encoding.UTF8);
                    if (!string.Equals(Sha256(current), plan.OriginalContentSha256, StringComparison.Ordinal))
                        return LogConfigStatus.Conflict;
                }
                var directory = Path.GetDirectoryName(plan.ConfigPath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(plan.ConfigPath, plan.ProposedContent ?? "", System.Text.Encoding.UTF8);
                return plan.Status == LogConfigStatus.Missing
                    ? LogConfigStatus.Created
                    : LogConfigStatus.Patched;
            }
            catch
            {
                return LogConfigStatus.Error;
            }
        }

        private static string Sha256(string content)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(content ?? ""));
                return BitConverter.ToString(bytes).Replace("-", "");
            }
        }

        /// <summary>检查 Power.log 是否应该存在（log.config 已配置且炉石已重启过）</summary>
        public static string FindPowerLog()
        {
            if (string.IsNullOrEmpty(HearthstoneConfigDir))
                FindHearthstoneDir();

            if (string.IsNullOrEmpty(HearthstoneConfigDir))
                return null;

            var logsDir = Path.Combine(HearthstoneConfigDir, "Logs");
            if (!Directory.Exists(logsDir))
                return null;

            // 优先查找最新的会话子目录 (团子版/新版本: Logs/Hearthstone_TIMESTAMP/Power_old.log)
            // Power_old.log 包含 GameState.DebugPrintEntityChoices() 事件（默认启用）
            try
            {
                string newestDir = null;
                DateTime newestTime = DateTime.MinValue;
                foreach (var dir in Directory.GetDirectories(logsDir, "Hearthstone_*"))
                {
                    var dirInfo = new DirectoryInfo(dir);
                    if (dirInfo.LastWriteTime > newestTime)
                    {
                        newestTime = dirInfo.LastWriteTime;
                        newestDir = dir;
                    }
                }
                if (newestDir != null)
                {
                    // 优先 Power_old.log（含DebugPrintEntityChoices）
                    var powerOldPath = Path.Combine(newestDir, "Power_old.log");
                    if (File.Exists(powerOldPath))
                        return powerOldPath;
                    var sessionPath = Path.Combine(newestDir, "Power.log");
                    if (File.Exists(sessionPath))
                        return sessionPath;
                }
            }
            catch { }

            // 1. 直接路径 (国际版/旧版)
            var directPath = Path.Combine(logsDir, "Power.log");
            if (File.Exists(directPath))
                return directPath;

            return null;
        }

        // ── 内部 ──

        private static void FindHearthstoneDir()
        {
            // 1. 读 HDT 配置获取安装目录 (团子版炉石日志路径在这里)
            try
            {
                var hdtConfigPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "HearthstoneDeckTracker", "config.xml");
                if (File.Exists(hdtConfigPath))
                {
                    var xml = File.ReadAllText(hdtConfigPath);
                    var match = System.Text.RegularExpressions.Regex.Match(
                        xml, @"<HearthstoneDirectory>([^<]+)</HearthstoneDirectory>");
                    if (match.Success)
                    {
                        var dir = match.Groups[1].Value.Trim();
                        if (Directory.Exists(dir))
                        {
                            HearthstoneConfigDir = dir;
                            return;
                        }
                    }
                }
            }
            catch { }

            // 2. AppData (国际版兼容)
            var localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            if (!string.IsNullOrEmpty(localAppData))
            {
                var dir = Path.Combine(localAppData, @"Blizzard\Hearthstone");
                if (Directory.Exists(dir))
                {
                    HearthstoneConfigDir = dir;
                    return;
                }
            }
        }

        private static string BuildDefaultContent()
        {
            var lines = new List<string>
            {
                "# Hearthstone log config — generated after explicit BobCoach consent",
                "# 重启炉石后生效",
                "",
            };
            foreach (var module in RequiredModules)
            {
                lines.Add("[" + module.Key + "]");
                foreach (var setting in module.Value)
                    lines.Add(setting.Key + "=" + setting.Value);
                lines.Add("");
            }
            return string.Join("\r\n", lines);
        }

        private static List<string> BuildPatchedLines(IEnumerable<string> originalLines, out List<string> changes)
        {
            var lines = originalLines.ToList();
            var sections = ParseConfigLines(lines);
            changes = new List<string>();

            foreach (var required in RequiredModules)
            {
                if (!sections.ContainsKey(required.Key))
                {
                    if (!MissingModules.Contains(required.Key)) MissingModules.Add(required.Key);
                    lines.Add("");
                    lines.Add("[" + required.Key + "]");
                    foreach (var setting in required.Value)
                        lines.Add(setting.Key + "=" + setting.Value);
                    changes.Add("新增 [" + required.Key + "] 段");
                    continue;
                }

                var section = sections[required.Key];
                if (EnsureFunctionalKey(lines, section, required.Key, "FilePrinting", "True", requireExact: true))
                    changes.Add(required.Key + ".FilePrinting=True");
                if (EnsureFunctionalKey(lines, section, required.Key, "LogLevel", "1", requireExact: false))
                    changes.Add(required.Key + ".LogLevel=1");
                string requiredVerbose;
                if (required.Value.TryGetValue("Verbose", out requiredVerbose)
                    && EnsureFunctionalKey(lines, section, required.Key, "Verbose", requiredVerbose, requireExact: true))
                    changes.Add(required.Key + ".Verbose=" + requiredVerbose);
            }

            return lines;
        }

        /// <summary>
        /// 校验并(必要时)就地改写指定模块的功能键。
        /// requireExact=true: 值须精确匹配(FilePrinting/Verbose)。
        /// requireExact=false: 只要存在且非"0"即合格(LogLevel, 不强制降级已有更高等级); 缺失或"0"则写入 value。
        /// </summary>
        private static bool EnsureFunctionalKey(List<string> lines,
            Dictionary<string, string> section, string module, string key, string value, bool requireExact)
        {
            string cur;
            bool present = section.TryGetValue(key, out cur);
            bool ok = requireExact
                ? (present && cur.Equals(value, StringComparison.OrdinalIgnoreCase))
                : (present && cur != "0");
            if (ok) return false;
            if (!MissingModules.Contains(module)) MissingModules.Add(module);
            return EnsureKeyInSection(lines, module, key, value);
        }

        /// <summary>在指定 [section] 内确保 key=value: 存在则改值, 不存在则段头后插入。返回是否实际修改文件行。</summary>
        private static bool EnsureKeyInSection(List<string> lines, string section, string key, string value)
        {
            int secStart = -1, secEnd = lines.Count;
            for (int i = 0; i < lines.Count; i++)
            {
                var t = lines[i].Trim();
                if (t.StartsWith("[") && t.EndsWith("]"))
                {
                    var name = t.Substring(1, t.Length - 2).Trim();
                    if (secStart < 0 && name.Equals(section, StringComparison.OrdinalIgnoreCase))
                        secStart = i;
                    else if (secStart >= 0) { secEnd = i; break; }
                }
            }
            if (secStart < 0) return false; // 段不存在(调用方负责追加整段)
            for (int i = secStart + 1; i < secEnd; i++)
            {
                var t = lines[i].Trim();
                if (string.IsNullOrEmpty(t) || t.StartsWith("#") || t.StartsWith(";")) continue;
                int eq = t.IndexOf('=');
                if (eq > 0 && t.Substring(0, eq).Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    var newLine = key + "=" + value;
                    if (lines[i].Trim() == newLine) return false; // 已正确
                    lines[i] = newLine;
                    return true;
                }
            }
            lines.Insert(secStart + 1, key + "=" + value); // 段内无此键 → 段头后插入
            return true;
        }

        private static Dictionary<string, Dictionary<string, string>> ParseConfigLines(IEnumerable<string> sourceLines)
        {
            var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            string currentSection = null;

            foreach (var rawLine in sourceLines)
            {
                var line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#") || line.StartsWith(";"))
                    continue;

                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    currentSection = line.Substring(1, line.Length - 2).Trim();
                    if (!sections.ContainsKey(currentSection))
                        sections[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }
                else if (currentSection != null)
                {
                    var eqIdx = line.IndexOf('=');
                    if (eqIdx > 0)
                    {
                        var key = line.Substring(0, eqIdx).Trim();
                        var value = line.Substring(eqIdx + 1).Trim();
                        sections[currentSection][key] = value;
                    }
                }
            }

            return sections;
        }

        private static void PluginLog(string msg)
        {
            try
            {
                var bobDir = BobCoachDataPaths.Root;
                Directory.CreateDirectory(bobDir);
                File.AppendAllText(Path.Combine(bobDir, "bob_coach.log"),
                    string.Format("[{0:O}] [LogConfig] {1}\n", DateTime.UtcNow, msg),
                    System.Text.Encoding.UTF8);
            }
            catch { }
        }
    }
}
