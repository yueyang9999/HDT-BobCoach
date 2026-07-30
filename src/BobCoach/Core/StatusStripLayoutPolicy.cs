using System;

namespace BobCoach.Engine
{
    internal sealed class StatusStripMetrics
    {
        public double Scale { get; private set; }
        public double PrimaryFontSize { get; private set; }
        public double SecondaryFontSize { get; private set; }
        public double PrimaryLineHeight { get; private set; }
        public double EmphasisLineHeight { get; private set; }
        public double HorizontalPadding { get; private set; }
        public double NarrowMaxWidth { get; private set; }
        public double WideMaxWidth { get; private set; }

        internal StatusStripMetrics(double scale, double primaryFontSize)
        {
            Scale = scale;
            PrimaryFontSize = primaryFontSize;
            SecondaryFontSize = 12.0 * scale;
            PrimaryLineHeight = 18.0 * scale;
            EmphasisLineHeight = 19.0 * scale;
            HorizontalPadding = 6.0 * scale;
            NarrowMaxWidth = 260.0 * scale;
            WideMaxWidth = 280.0 * scale;
        }
    }

    internal struct StatusStripPosition
    {
        public double X { get; private set; }
        public double Y { get; private set; }

        public StatusStripPosition(double x, double y)
        {
            X = x;
            Y = y;
        }
    }

    internal static class StatusStripLayoutPolicy
    {
        public const int MinimumFontSize = 12;
        public const int MaximumFontSize = 28;
        public const int DefaultFontSize = 16;
        private const double PrimaryFontBaseline = 13.0;

        public static int NormalizeFontSize(double value)
        {
            if (!IsFinite(value) || value < MinimumFontSize || value > MaximumFontSize)
                return DefaultFontSize;
            return (int)Math.Round(value, MidpointRounding.AwayFromZero);
        }

        public static StatusStripMetrics CreateMetrics(double fontSize)
        {
            int normalized = NormalizeFontSize(fontSize);
            double scale = normalized / PrimaryFontBaseline;
            return new StatusStripMetrics(scale, normalized);
        }

        public static StatusStripPosition ClampPosition(
            double x, double y, double canvasWidth, double canvasHeight,
            double panelWidth, double panelHeight)
        {
            x = IsFinite(x) ? x : 0;
            y = IsFinite(y) ? y : 0;
            canvasWidth = IsFinite(canvasWidth) && canvasWidth > 0 ? canvasWidth : 0;
            canvasHeight = IsFinite(canvasHeight) && canvasHeight > 0 ? canvasHeight : 0;
            panelWidth = IsFinite(panelWidth) && panelWidth > 0 ? panelWidth : 0;
            panelHeight = IsFinite(panelHeight) && panelHeight > 0 ? panelHeight : 0;

            double maxX = Math.Max(0, canvasWidth - panelWidth);
            double maxY = Math.Max(0, canvasHeight - panelHeight);
            return new StatusStripPosition(
                Math.Max(0, Math.Min(maxX, x)),
                Math.Max(0, Math.Min(maxY, y)));
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
