using System;
using BobCoach.Engine;

internal static class LayoutResolutionMatrixBehavior
{
    private static int _failures;

    private static readonly int[,] Resolutions =
    {
        { 1024, 768 },
        { 1280, 720 },
        { 1366, 768 },
        { 1600, 900 },
        { 1920, 1080 },
        { 2560, 1080 },
        { 2560, 1440 },
        { 2560, 1600 },
        { 3840, 2160 },
    };

    private static void Check(string name, bool condition, string detail)
    {
        if (condition) return;
        _failures++;
        Console.Error.WriteLine("FAIL " + name + ": " + detail);
    }

    private static bool IsInside(LayoutRect rect, double width, double height)
    {
        const double epsilon = 0.01;
        return rect.Left >= -epsilon
            && rect.Top >= -epsilon
            && rect.Left + rect.Width <= width + epsilon
            && rect.Top + rect.Height <= height + epsilon;
    }

    public static int Main()
    {
        var calc = new GameLayoutCalculator(new LayoutConfig());
        int layoutsChecked = 0;

        for (int resolutionIndex = 0; resolutionIndex < Resolutions.GetLength(0); resolutionIndex++)
        {
            int width = Resolutions[resolutionIndex, 0];
            int height = Resolutions[resolutionIndex, 1];
            string resolution = width + "x" + height;
            calc.RefreshWithSize(width, height);

            Check(resolution + " shop area", IsInside(calc.GetShopArea(), width, height), "outside canvas");
            Check(resolution + " board area", IsInside(calc.GetBoardArea(), width, height), "outside canvas");
            Check(resolution + " tavern button", IsInside(calc.GetTavernButtonArea(), width, height), "outside canvas");
            Check(resolution + " refresh button", IsInside(calc.GetRefreshButtonArea(), width, height), "outside canvas");
            Check(resolution + " freeze button", IsInside(calc.GetFreezeButtonArea(), width, height), "outside canvas");
            Check(resolution + " hero power", IsInside(calc.GetHeroPowerArea(), width, height), "outside canvas");
            Check(resolution + " hand area", IsInside(calc.GetHandArea(), width, height), "outside canvas");

            LayoutPoint status = calc.GetStatusStripPosition();
            Check(resolution + " status strip", status.X >= 0 && status.X < width && status.Y >= 0 && status.Y < height,
                "position=" + status.X + "," + status.Y);

            double panelLeft = calc.Config.ScaleX(calc.Config.PanelOffsetX, width);
            double panelTop = calc.Config.ScaleY(calc.Config.PanelOffsetY, height);
            double panelWidth = calc.ScaleX(360) * calc.Config.PanelScale;
            Check(resolution + " recommendation panel", panelLeft >= 0 && panelTop >= 0 && panelLeft + panelWidth <= width,
                "bounds=" + panelLeft + ".." + (panelLeft + panelWidth));

            for (int tier = 1; tier <= 6; tier++)
            {
                calc.SetTier(tier);
                for (int count = 1; count <= 7; count++)
                {
                    LayoutRect[] shop = calc.GetShopCardRects(count);
                    LayoutRect[] board = calc.GetBoardCardRects(count);
                    string prefix = resolution + " T" + tier + " N" + count;
                    Check(prefix + " shop count", shop.Length == count, "actual=" + shop.Length);
                    Check(prefix + " board count", board.Length == count, "actual=" + board.Length);
                    if (shop.Length != count || board.Length != count) continue;

                    double groupCenter = (shop[0].Left + shop[shop.Length - 1].Left + shop[shop.Length - 1].Width) / 2.0;
                    Check(prefix + " shop center", Math.Abs(groupCenter - width / 2.0) <= 0.01,
                        "actual=" + groupCenter + " expected=" + width / 2.0);

                    for (int i = 0; i < count; i++)
                    {
                        Check(prefix + " shop card " + i, IsInside(shop[i], width, height), "outside canvas");
                        Check(prefix + " board card " + i, IsInside(board[i], width, height), "outside canvas");

                        double labelLeft = shop[i].Left + shop[i].Width * 0.02 + calc.ShopLabelOffsetX;
                        double labelRight = labelLeft + shop[i].Width * 0.96;
                        double labelBottom = shop[i].Top + shop[i].Height + 4 + 18 + 2 + 5;
                        Check(prefix + " purchase label " + i,
                            labelLeft >= 0 && labelRight <= width && labelBottom <= height,
                            "bounds=" + labelLeft + ".." + labelRight + ", bottom=" + labelBottom);

                        double labelCenter = (labelLeft + labelRight) / 2.0;
                        double cardCenter = shop[i].Left + shop[i].Width / 2.0;
                        Check(prefix + " label center " + i, Math.Abs(labelCenter - cardCenter) <= 0.01,
                            "label=" + labelCenter + " card=" + cardCenter);

                        if (i > 0)
                        {
                            double previousRight = shop[i - 1].Left + shop[i - 1].Width;
                            Check(prefix + " shop spacing " + i, shop[i].Left >= previousRight,
                                "overlap=" + (previousRight - shop[i].Left));
                        }
                    }
                    layoutsChecked++;
                }
            }
        }

        calc.SetTier(1);
        calc.RefreshWithSize(1280, 720);
        double smallCardWidth = calc.GetShopCardRect(0, 3).Width;
        calc.RefreshWithSize(3840, 2160);
        double largeCardWidth = calc.GetShopCardRect(0, 3).Width;
        Check("resolution refresh invalidates layout cache", Math.Abs(largeCardWidth - smallCardWidth * 3) <= 0.01,
            "small=" + smallCardWidth + " large=" + largeCardWidth);

        var calibrated = new LayoutConfig { CalibrationWidth = 1920, CalibrationHeight = 1080, ShopOffsetX = 120 };
        var calibratedCalc = new GameLayoutCalculator(calibrated);
        calibratedCalc.RefreshWithSize(1280, 720);
        LayoutRect calibratedArea = calibratedCalc.GetShopArea();
        Check("calibration offset scales with resolution",
            Math.Abs((calibratedArea.Left + calibratedArea.Width / 2.0) - (640 + 80)) <= 0.01,
            "center=" + (calibratedArea.Left + calibratedArea.Width / 2.0));

        if (_failures == 0)
            Console.WriteLine("PASS layout resolution matrix (" + layoutsChecked + " shop layouts)");
        return _failures == 0 ? 0 : 1;
    }
}
