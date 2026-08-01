using System;
using System.IO;
using System.Linq;
using System.Reflection;

internal static class PluginRankingEntrypointBehavior
{
    [STAThread]
    private static int Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += ResolveDependency;
        try
        {
            RunBehavior();
            Console.WriteLine("PASS plugin ranking entrypoint behavior");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static Assembly ResolveDependency(object sender, ResolveEventArgs args)
    {
        var assemblyName = new AssemblyName(args.Name).Name;
        var hdtDirectory = Environment.GetEnvironmentVariable("BOBCOACH_HDT_DIR");
        if (string.IsNullOrWhiteSpace(hdtDirectory)) return null;

        var dllPath = Path.Combine(hdtDirectory, assemblyName + ".dll");
        if (File.Exists(dllPath)) return Assembly.LoadFrom(dllPath);
        var executablePath = Path.Combine(hdtDirectory, assemblyName + ".exe");
        return File.Exists(executablePath) ? Assembly.LoadFrom(executablePath) : null;
    }

    private static void RunBehavior()
    {
        var plugin = new BobCoach.BobCoachPlugin();
        var throwingLegacy = new ThrowingPolicy("legacy-v1.1.0");
        var throwingUnified = new ThrowingPolicy("unified-linear-v1");
        var candidates = new BobCoach.Engine.Decision.CandidateGenerator(
            new BobCoach.Engine.ActionEnumerator());
        var bundleConstructor = typeof(BobCoach.Engine.Decision.DecisionPolicyBundle)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single();
        var bundle = (BobCoach.Engine.Decision.DecisionPolicyBundle)bundleConstructor.Invoke(
            new object[] { throwingLegacy, throwingUnified, candidates, "unknown", "unknown" });

        var failingPath = Path.Combine(Path.GetTempPath(),
            "bobcoach_plugin_ranking_recorder_failure");
        Directory.CreateDirectory(failingPath);
        var recorder = new BobCoach.Engine.Decision.DecisionEventRecorder(failingPath);
        var lifecycle = new BobCoach.Engine.Decision.DecisionLifecycleTracker(
            "plugin-entrypoint-test", recorder);
        var orchestrator = new BobCoach.Engine.Decision.DecisionOrchestrator(
            throwingLegacy, throwingUnified, recorder,
            BobCoach.Engine.Decision.DecisionRuntimeMode.Shadow);

        SetField(plugin, "_decisionPolicies", bundle);
        SetField(plugin, "_decisionEventRecorder", recorder);
        SetField(plugin, "_decisionLifecycle", lifecycle);
        SetField(plugin, "_decisionOrchestrator", orchestrator);

        var method = typeof(BobCoach.BobCoachPlugin).GetMethod(
            "EvaluateRankingDecision", BindingFlags.Instance | BindingFlags.NonPublic);
        if (method == null)
            throw new InvalidOperationException(
                "BobCoachPlugin ranking entrypoint cannot be verified as executable behavior");

        BobCoach.Engine.Decision.DecisionResult result;
        try
        {
            result = (BobCoach.Engine.Decision.DecisionResult)method.Invoke(plugin,
                new object[] { new BobCoach.Engine.GameState { GameActive = true, Turn = 6 } });
        }
        catch (TargetInvocationException ex)
        {
            throw new InvalidOperationException("plugin ranking entrypoint leaked an exception",
                ex.InnerException ?? ex);
        }

        AssertEqual(BobCoach.Engine.Decision.FallbackReason.NoSafeAction,
            result.FallbackReason, "fallback reason");
        AssertTrue(result.SelectedAction == null, "fallback must not expose an action");
        AssertEqual(1, throwingLegacy.DecideCount, "legacy failure must be contained once");
        AssertTrue(GetField(plugin, "_decisionEventRecorder") == null,
            "failing recorder must be reset");
        AssertTrue(GetField(plugin, "_decisionLifecycle") == null,
            "failing lifecycle must be reset");
    }

    private static void SetField(object target, string name, object value)
    {
        var field = target.GetType().GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null) throw new InvalidOperationException("Missing field " + name);
        field.SetValue(target, value);
    }

    private static object GetField(object target, string name)
    {
        var field = target.GetType().GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null) throw new InvalidOperationException("Missing field " + name);
        return field.GetValue(target);
    }

    private static void AssertEqual<T>(T expected, T actual, string label)
    {
        if (!Equals(expected, actual))
            throw new InvalidOperationException(label + " expected=" + expected
                + " actual=" + actual);
    }

    private static void AssertTrue(bool value, string label)
    {
        if (!value) throw new InvalidOperationException(label);
    }

    private sealed class ThrowingPolicy : BobCoach.Engine.Decision.IDecisionPolicy
    {
        public string PolicyId { get; private set; }
        public int DecideCount { get; private set; }

        public ThrowingPolicy(string policyId)
        {
            PolicyId = policyId;
        }

        public BobCoach.Engine.Decision.DecisionResult Decide(BobCoach.Engine.GameState state)
        {
            DecideCount++;
            throw new InvalidOperationException("injected policy failure");
        }
    }
}
