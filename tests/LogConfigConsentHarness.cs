using System;
using System.IO;
using System.Reflection;
using System.Threading;
using BobCoach.Engine;

internal static class LogConfigConsentHarness
{
    private static int Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "bobcoach-log-config-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "log.config");
        Environment.SetEnvironmentVariable("APPDATA", Path.Combine(root, "appdata"));
        Environment.SetEnvironmentVariable("LOCALAPPDATA", Path.Combine(root, "local-appdata"));

        var plan = LogConfigEnsurer.InspectAtPath(path);

        if (plan.Status != LogConfigStatus.Missing)
            return Fail("missing config was not reported as Missing");
        if (File.Exists(path) || Directory.Exists(root))
            return Fail("inspection created a file or directory");
        if (string.IsNullOrEmpty(plan.ProposedContent)
            || !plan.ProposedContent.Contains("[Power]")
            || !plan.ProposedContent.Contains("Verbose=True"))
            return Fail("missing config plan did not expose the proposed content");
        if (plan.Changes == null || plan.Changes.Count == 0)
            return Fail("missing config plan did not describe changes");

        var createStatus = LogConfigEnsurer.Apply(plan);
        if (createStatus != LogConfigStatus.Created)
            return Fail("explicit apply did not create missing config");
        if (!File.Exists(path) || File.ReadAllText(path) != plan.ProposedContent)
            return Fail("explicit apply did not write the reviewed content");

        var original = "[Power]\r\nVerbose=False\r\nCustomSetting=KeepMe\r\n\r\n[Unknown]\r\nKeep=1\r\n";
        File.WriteAllText(path, original);

        var patchPlan = LogConfigEnsurer.InspectAtPath(path);

        if (patchPlan.Status != LogConfigStatus.NeedsPatch)
            return Fail("incomplete config was not reported as NeedsPatch");
        if (File.ReadAllText(path) != original)
            return Fail("inspection modified an existing config");
        if (string.IsNullOrEmpty(patchPlan.ProposedContent)
            || !patchPlan.ProposedContent.Contains("Verbose=True")
            || !patchPlan.ProposedContent.Contains("CustomSetting=KeepMe")
            || !patchPlan.ProposedContent.Contains("[Unknown]")
            || !patchPlan.ProposedContent.Contains("Keep=1"))
            return Fail("patch preview did not preserve unknown content and correct required keys");
        if (patchPlan.Changes == null || patchPlan.Changes.Count == 0)
            return Fail("patch preview did not list changes");

        var patchStatus = LogConfigEnsurer.Apply(patchPlan);
        if (patchStatus != LogConfigStatus.Patched)
            return Fail("explicit apply did not patch an incomplete config");
        if (File.ReadAllText(path) != patchPlan.ProposedContent)
            return Fail("patched content differed from the reviewed preview");

        var completePlan = LogConfigEnsurer.InspectAtPath(path);
        if (completePlan.Status != LogConfigStatus.OK
            || completePlan.Changes == null || completePlan.Changes.Count != 0)
            return Fail("applied config did not re-inspect as complete");

        File.WriteAllText(path, original);
        var conflictPlan = LogConfigEnsurer.InspectAtPath(path);
        var externallyChanged = original + "ExternalChange=1\r\n";
        File.WriteAllText(path, externallyChanged);
        if (LogConfigEnsurer.Apply(conflictPlan) != LogConfigStatus.Conflict)
            return Fail("concurrent config change was not rejected");
        if (File.ReadAllText(path) != externallyChanged)
            return Fail("conflict handling overwrote external changes");

        var isolatedLocalAppData = Path.Combine(root, "local-appdata");
        var isolatedHearthstoneDir = Path.Combine(isolatedLocalAppData, "Blizzard", "Hearthstone");
        var isolatedConfigPath = Path.Combine(isolatedHearthstoneDir, "log.config");
        Directory.CreateDirectory(isolatedHearthstoneDir);
        File.WriteAllText(isolatedConfigPath, original);
        using (var watcher = new PowerLogWatcher())
        {
            watcher.StartWatchingAtConfigPath(isolatedConfigPath);
            if (watcher.ConfigStatus != LogConfigStatus.NeedsPatch)
                return Fail("watcher did not preserve NeedsPatch for an incomplete isolated config");
            if (watcher.IsWatching)
                return Fail("watcher started despite an incomplete isolated config");
            if (watcher.IsStarting)
                return Fail("watcher remained in the starting state after rejecting incomplete config");
        }

        var controlledStart = typeof(PowerLogWatcher).GetMethod(
            "StartWatchingWithConfigInspector",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (controlledStart == null)
            return Fail("watcher has no controllable config inspection lifecycle entry point");

        using (var inspectionEntered = new ManualResetEvent(false))
        using (var releaseInspection = new ManualResetEvent(false))
        using (var watcher = new PowerLogWatcher())
        {
            Exception startError = null;
            var startThread = new Thread(() =>
            {
                try
                {
                    controlledStart.Invoke(watcher, new object[]
                    {
                        new Func<LogConfigPlan>(() =>
                        {
                            inspectionEntered.Set();
                            releaseInspection.WaitOne();
                            return new LogConfigPlan { Status = LogConfigStatus.OK };
                        })
                    });
                }
                catch (TargetInvocationException ex)
                {
                    startError = ex.InnerException ?? ex;
                }
                catch (Exception ex)
                {
                    startError = ex;
                }
            });

            startThread.Start();
            if (!inspectionEntered.WaitOne(2000))
                return Fail("controlled config inspection did not start");

            watcher.StopWatching();
            releaseInspection.Set();
            if (!startThread.Join(3000))
                return Fail("start call did not return after config inspection was released");
            if (startError != null)
                return Fail("controlled start failed: " + startError.Message);
            if (watcher.IsWatching || watcher.IsStarting)
                return Fail("watcher restarted after StopWatching returned during config inspection");
        }

        using (var watcher = new PowerLogWatcher())
        {
            try
            {
                controlledStart.Invoke(watcher, new object[]
                {
                    new Func<LogConfigPlan>(() =>
                    {
                        throw new InvalidOperationException("inspection failed");
                    })
                });
                return Fail("throwing config inspection unexpectedly returned");
            }
            catch (TargetInvocationException ex)
            {
                if (!(ex.InnerException is InvalidOperationException))
                    return Fail("throwing config inspection changed exception identity");
            }

            if (watcher.IsWatching || watcher.IsStarting)
                return Fail("throwing config inspection left watcher lifecycle occupied");
        }

        using (var firstInspectionEntered = new ManualResetEvent(false))
        using (var releaseFirstInspection = new ManualResetEvent(false))
        using (var secondInspectionEntered = new ManualResetEvent(false))
        using (var releaseSecondInspection = new ManualResetEvent(false))
        using (var watcher = new PowerLogWatcher())
        {
            Exception firstError = null;
            Exception secondError = null;
            var firstStart = new Thread(() =>
            {
                try
                {
                    controlledStart.Invoke(watcher, new object[]
                    {
                        new Func<LogConfigPlan>(() =>
                        {
                            firstInspectionEntered.Set();
                            releaseFirstInspection.WaitOne();
                            return new LogConfigPlan { Status = LogConfigStatus.Error };
                        })
                    });
                }
                catch (TargetInvocationException ex) { firstError = ex.InnerException ?? ex; }
                catch (Exception ex) { firstError = ex; }
            });
            var secondStart = new Thread(() =>
            {
                try
                {
                    controlledStart.Invoke(watcher, new object[]
                    {
                        new Func<LogConfigPlan>(() =>
                        {
                            secondInspectionEntered.Set();
                            releaseSecondInspection.WaitOne();
                            return new LogConfigPlan { Status = LogConfigStatus.NeedsPatch };
                        })
                    });
                }
                catch (TargetInvocationException ex) { secondError = ex.InnerException ?? ex; }
                catch (Exception ex) { secondError = ex; }
            });

            firstStart.Start();
            if (!firstInspectionEntered.WaitOne(2000))
                return Fail("first generation config inspection did not start");
            watcher.StopWatching();
            secondStart.Start();
            if (!secondInspectionEntered.WaitOne(2000))
                return Fail("second generation config inspection did not start");

            releaseFirstInspection.Set();
            if (!firstStart.Join(3000))
                return Fail("superseded config inspection did not return");
            if (firstError != null)
                return Fail("superseded config inspection failed: " + firstError.Message);
            if (!watcher.IsStarting)
                return Fail("superseded config inspection cancelled the newer lifecycle generation");

            releaseSecondInspection.Set();
            if (!secondStart.Join(3000))
                return Fail("newer config inspection did not return");
            if (secondError != null)
                return Fail("newer config inspection failed: " + secondError.Message);
            if (watcher.ConfigStatus != LogConfigStatus.NeedsPatch)
                return Fail("superseded inspection overwrote the newer config status");
            if (watcher.IsWatching || watcher.IsStarting)
                return Fail("newer rejected config left watcher lifecycle occupied");
        }

        var isolatedLogsDir = Path.Combine(isolatedHearthstoneDir, "Logs");
        var isolatedPowerLogPath = Path.Combine(isolatedLogsDir, "Power.log");
        Directory.CreateDirectory(isolatedLogsDir);
        File.WriteAllText(isolatedConfigPath, patchPlan.ProposedContent);
        File.WriteAllText(isolatedPowerLogPath, "");

        var watchThreadField = typeof(PowerLogWatcher).GetField(
            "_watchThread", BindingFlags.Instance | BindingFlags.NonPublic);
        if (watchThreadField == null)
            return Fail("watcher lifecycle thread field was not found");

        using (var releaseBlockingThread = new ManualResetEvent(false))
        using (var watcher = new PowerLogWatcher())
        {
            watcher.StartWatchingAtConfigPath(isolatedConfigPath);
            if (!WaitUntil(() => watcher.IsWatching, 2000))
                return Fail("isolated watcher did not enter the running state");

            var firstWatchThread = watchThreadField.GetValue(watcher) as Thread;
            if (firstWatchThread == null || !firstWatchThread.IsAlive)
                return Fail("isolated watcher did not create its first watch thread");

            var blockingThread = new Thread(() => releaseBlockingThread.WaitOne())
            {
                IsBackground = true,
                Name = "PowerLogStopJoinGate"
            };
            blockingThread.Start();
            watchThreadField.SetValue(watcher, blockingThread);

            var stopThread = new Thread(watcher.StopWatching) { IsBackground = true };
            stopThread.Start();
            if (!WaitUntil(() => !watcher.IsWatching, 2000))
                return Fail("concurrent stop did not leave the running state");

            Thread.Sleep(100);
            watcher.StartWatchingAtConfigPath(isolatedConfigPath);
            if (!WaitUntil(() => watcher.IsWatching, 2000))
                return Fail("watcher did not restart while the prior stop was waiting");
            if (!stopThread.Join(3000))
                return Fail("prior stop did not return after its bounded join");

            releaseBlockingThread.Set();
            if (!blockingThread.Join(1000))
                return Fail("blocking lifecycle thread did not exit");
            if (!firstWatchThread.Join(1000))
                return Fail("superseded watch generation remained alive after restart");

            var currentWatchThread = watchThreadField.GetValue(watcher) as Thread;
            if (currentWatchThread == null || !currentWatchThread.IsAlive)
                return Fail("prior stop cleared the newer watch thread reference");

            watcher.StopWatching();
        }

        Directory.Delete(root, true);
        Console.WriteLine("PASS log.config consent and disabled watcher lifecycle");
        return 0;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine("FAIL " + message);
        return 1;
    }

    private static bool WaitUntil(Func<bool> predicate, int timeoutMilliseconds)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (DateTime.UtcNow < deadline)
        {
            if (predicate()) return true;
            Thread.Sleep(10);
        }
        return predicate();
    }
}

namespace BobCoach.Engine
{
    // PowerLogWatcher.ExportReplay only needs the type identity in this isolated harness.
    public sealed class TurnSnapshot { }

    internal static class FastJsonWriter
    {
        public static void Write(string path, object value) { }
    }
}
