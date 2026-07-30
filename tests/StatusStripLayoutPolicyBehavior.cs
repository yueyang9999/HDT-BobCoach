using System;
using BobCoach.Engine;

internal static class StatusStripLayoutPolicyBehavior
{
    private static int _failures;

    public static int Main()
    {
        Equal(16, StatusStripLayoutPolicy.NormalizeFontSize(16), "default font size");
        Equal(12, StatusStripLayoutPolicy.NormalizeFontSize(12), "minimum font size");
        Equal(28, StatusStripLayoutPolicy.NormalizeFontSize(28), "maximum font size");
        Equal(16, StatusStripLayoutPolicy.NormalizeFontSize(11), "below-range font size falls back");
        Equal(16, StatusStripLayoutPolicy.NormalizeFontSize(29), "above-range font size falls back");
        Equal(16, StatusStripLayoutPolicy.NormalizeFontSize(double.NaN), "NaN font size falls back");
        Equal(16, StatusStripLayoutPolicy.NormalizeFontSize(double.PositiveInfinity), "positive infinity falls back");
        Equal(16, StatusStripLayoutPolicy.NormalizeFontSize(double.NegativeInfinity), "negative infinity falls back");
        Equal(17, StatusStripLayoutPolicy.NormalizeFontSize(16.6), "valid font size rounds to an integer");

        StatusStripMetrics metrics = StatusStripLayoutPolicy.CreateMetrics(26);
        Near(2.0, metrics.Scale, "font scale uses primary 13 DIP baseline");
        Near(26.0, metrics.PrimaryFontSize, "primary font size");
        Near(24.0, metrics.SecondaryFontSize, "secondary font preserves 12/13 ratio");
        Near(36.0, metrics.PrimaryLineHeight, "primary line height scales");
        Near(38.0, metrics.EmphasisLineHeight, "emphasis line height scales");
        Near(12.0, metrics.HorizontalPadding, "horizontal padding scales");
        Near(520.0, metrics.NarrowMaxWidth, "260 DIP maximum width scales");
        Near(560.0, metrics.WideMaxWidth, "280 DIP maximum width scales");

        StatusStripPosition inside = StatusStripLayoutPolicy.ClampPosition(20, 30, 800, 600, 240, 160);
        Near(20, inside.X, "inside x remains unchanged");
        Near(30, inside.Y, "inside y remains unchanged");

        StatusStripPosition negative = StatusStripLayoutPolicy.ClampPosition(-20, -30, 800, 600, 240, 160);
        Near(0, negative.X, "negative x clamps to zero");
        Near(0, negative.Y, "negative y clamps to zero");

        StatusStripPosition bottomRight = StatusStripLayoutPolicy.ClampPosition(700, 500, 800, 600, 240, 160);
        Near(560, bottomRight.X, "right edge clamps to canvas");
        Near(440, bottomRight.Y, "bottom edge clamps to canvas");

        StatusStripPosition oversized = StatusStripLayoutPolicy.ClampPosition(100, 100, 200, 100, 240, 160);
        Near(0, oversized.X, "oversized panel x is zero");
        Near(0, oversized.Y, "oversized panel y is zero");

        StatusStripPosition invalid = StatusStripLayoutPolicy.ClampPosition(
            double.NaN, double.PositiveInfinity, double.NaN, -1, double.PositiveInfinity, 20);
        Near(0, invalid.X, "non-finite x is safe");
        Near(0, invalid.Y, "non-finite y is safe");

        if (_failures == 0)
            Console.WriteLine("PASS status strip layout policy");
        return _failures == 0 ? 0 : 1;
    }

    private static void Equal<T>(T expected, T actual, string name)
    {
        if (Equals(expected, actual)) return;
        _failures++;
        Console.Error.WriteLine("FAIL " + name + ": expected=" + expected + " actual=" + actual);
    }

    private static void Near(double expected, double actual, string name)
    {
        if (Math.Abs(expected - actual) <= 0.001) return;
        _failures++;
        Console.Error.WriteLine("FAIL " + name + ": expected=" + expected + " actual=" + actual);
    }
}
