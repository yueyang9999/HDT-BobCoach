using System;
using BobCoach.Engine;

internal static class LayoutConfigMigrationBehavior
{
    private static int _failures;

    private static void Check(string name, bool condition, string detail)
    {
        if (condition)
        {
            Console.WriteLine("PASS " + name);
            return;
        }

        _failures++;
        Console.Error.WriteLine("FAIL " + name + ": " + detail);
    }

    public static int Main()
    {
        var legacyDefault = new LayoutConfig
        {
            Version = 9,
            CalibrationWidth = 2559,
            ShopOffsetX = -86.0,
            ShopOffsetY = -449.8666666666667,
            BoardOffsetX = -5.33125,
            PanelScale = 0.85,
        };

        var migrated = LayoutConfig.NormalizeLoadedConfig(legacyDefault);
        Check("v9 layout config upgrades to current version",
            migrated.Version == 10,
            "actual=" + migrated.Version);
        Check("v9 legacy default shop offset migrates to zero",
            Math.Abs(migrated.ShopOffsetX) < 0.001,
            "actual=" + migrated.ShopOffsetX);
        Check("v9 migration preserves unrelated calibration",
            Math.Abs(migrated.ShopOffsetY - legacyDefault.ShopOffsetY) < 0.001
                && Math.Abs(migrated.BoardOffsetX - legacyDefault.BoardOffsetX) < 0.001
                && Math.Abs(migrated.PanelScale - legacyDefault.PanelScale) < 0.001,
            "unrelated calibration changed");

        var customOffset = new LayoutConfig
        {
            Version = 9,
            CalibrationWidth = 2559,
            ShopOffsetX = -40.0,
        };

        var preserved = LayoutConfig.NormalizeLoadedConfig(customOffset);
        Check("v9 custom shop offset is preserved",
            Math.Abs(preserved.ShopOffsetX + 40.0) < 0.001,
            "actual=" + preserved.ShopOffsetX);

        return _failures == 0 ? 0 : 1;
    }
}
