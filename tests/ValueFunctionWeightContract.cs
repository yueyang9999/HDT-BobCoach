using System;
using BobCoach.Engine;

namespace BobCoach.Engine
{
    public static class FeatureExtractor
    {
        public const int FeatureCount = 22;
        public const int F_HEALTH = 1;
    }
}

public static class ValueFunctionWeightContract
{
    private static readonly float[] Expected =
    {
        0.2148f, 0.1172f, 0.0488f, 0.0293f, 0.2100f, 0.0488f,
        0.1367f, 0.1465f, 0.0488f, 0.2441f, 0.0800f, 0.0977f,
        0.0488f, 0.0293f, 0.0977f, 0.1500f, 0.0293f, 0.0500f,
        0.0977f, 0.0391f, 0.0586f, 0.0600f,
    };

    private static void AssertWeights(float[] actual, string label)
    {
        if (actual == null || actual.Length != Expected.Length)
            throw new InvalidOperationException(label + " weight count");
        for (var i = 0; i < Expected.Length; i++)
        {
            if (Math.Abs(actual[i] - Expected[i]) > 0.000001f)
                throw new InvalidOperationException(string.Format(
                    "{0} weight mismatch at {1}: expected {2}, actual {3}",
                    label, i, Expected[i], actual[i]));
        }
    }

    private static void Run()
    {
        var first = new ValueFunction();
        var second = new ValueFunction();
        AssertWeights(first.Weights, "first");
        AssertWeights(second.Weights, "second");
        if (object.ReferenceEquals(first.Weights, second.Weights))
            throw new InvalidOperationException("ValueFunction instances share a mutable weight array");

        first.Weights[0] = -1f;
        AssertWeights(new ValueFunction().Weights, "after mutation");

        var custom = new float[Expected.Length];
        custom[0] = 0.5f;
        second.UpdateWeights(custom);
        if (Math.Abs(second.Weights[0] - 0.5f) > 0.000001f)
            throw new InvalidOperationException("UpdateWeights no longer updates the target instance");
    }

    public static int Main()
    {
        try
        {
            Run();
            Console.WriteLine("PASS ValueFunction exposes the exact 22 production weights");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL " + ex.Message);
            return 1;
        }
    }
}
