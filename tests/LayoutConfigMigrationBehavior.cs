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
            migrated.Version == 11,
            "actual=" + migrated.Version);
        Check("v9 legacy default shop offset migrates to zero",
            Math.Abs(migrated.ShopOffsetX) < 0.001,
            "actual=" + migrated.ShopOffsetX);
        Check("v9 migration preserves unrelated calibration",
            Math.Abs(migrated.ShopOffsetY - legacyDefault.ShopOffsetY) < 0.001
                && Math.Abs(migrated.BoardOffsetX - legacyDefault.BoardOffsetX) < 0.001
                && Math.Abs(migrated.PanelScale - legacyDefault.PanelScale) < 0.001,
            "unrelated calibration changed");
        Check("v9 migration adds status defaults",
            Math.Abs(migrated.StatusOffsetX - 8) < 0.001
                && Math.Abs(migrated.StatusOffsetY - 105) < 0.001
                && migrated.StatusFontSize == 16,
            "status=" + migrated.StatusOffsetX + "," + migrated.StatusOffsetY + "," + migrated.StatusFontSize);

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

        var version10 = new LayoutConfig
        {
            Version = 10,
            CalibrationWidth = 2560,
            CalibrationHeight = 1440,
            ShopOffsetX = 37,
            ShopOffsetY = -19,
            BoardOffsetX = 22,
            BoardOffsetY = 11,
            PanelOffsetX = 55,
            PanelOffsetY = 260,
            PanelScale = 1.25,
        };
        var migratedV10 = LayoutConfig.NormalizeLoadedConfig(version10);
        Check("v10 layout config upgrades to v11", migratedV10.Version == 11,
            "actual=" + migratedV10.Version);
        Check("v10 migration preserves existing calibration",
            migratedV10.CalibrationWidth == 2560
                && migratedV10.CalibrationHeight == 1440
                && migratedV10.ShopOffsetX == 37
                && migratedV10.ShopOffsetY == -19
                && migratedV10.BoardOffsetX == 22
                && migratedV10.BoardOffsetY == 11
                && migratedV10.PanelOffsetX == 55
                && migratedV10.PanelOffsetY == 260
                && migratedV10.PanelScale == 1.25,
            "existing fields changed");
        Check("v10 migration adds status defaults",
            migratedV10.StatusOffsetX == 8
                && migratedV10.StatusOffsetY == 105
                && migratedV10.StatusFontSize == 16,
            "status defaults missing");

        var invalidStatus = new LayoutConfig
        {
            Version = 11,
            CalibrationWidth = 2560,
            CalibrationHeight = 1440,
            ShopOffsetX = 43,
            BoardOffsetY = 17,
            PanelScale = 1.1,
            StatusOffsetX = double.NaN,
            StatusOffsetY = double.PositiveInfinity,
            StatusFontSize = 29,
        };
        var normalizedInvalid = LayoutConfig.NormalizeLoadedConfig(invalidStatus);
        Check("invalid status values fall back independently",
            normalizedInvalid.StatusOffsetX == 8
                && normalizedInvalid.StatusOffsetY == 105
                && normalizedInvalid.StatusFontSize == 16,
            "status=" + normalizedInvalid.StatusOffsetX + "," + normalizedInvalid.StatusOffsetY + "," + normalizedInvalid.StatusFontSize);
        Check("invalid status values do not reset other calibration",
            normalizedInvalid.CalibrationWidth == 2560
                && normalizedInvalid.CalibrationHeight == 1440
                && normalizedInvalid.ShopOffsetX == 43
                && normalizedInvalid.BoardOffsetY == 17
                && normalizedInvalid.PanelScale == 1.1,
            "unrelated values changed");

        var malformedStatus = LayoutConfig.DeserializeConfig(
            "{\"Version\":11,\"CalibrationWidth\":2560,\"ShopOffsetX\":43," +
            "\"BoardOffsetY\":17,\"PanelScale\":1.1,\"StatusOffsetX\":\"left\"," +
            "\"StatusOffsetY\":null,\"StatusFontSize\":\"large\"}");
        Check("unparseable status fields fall back independently",
            malformedStatus.StatusOffsetX == 8
                && malformedStatus.StatusOffsetY == 105
                && malformedStatus.StatusFontSize == 16,
            "status=" + malformedStatus.StatusOffsetX + "," + malformedStatus.StatusOffsetY + "," + malformedStatus.StatusFontSize);
        Check("unparseable status fields preserve other calibration",
            malformedStatus.CalibrationWidth == 2560
                && malformedStatus.ShopOffsetX == 43
                && malformedStatus.BoardOffsetY == 17
                && malformedStatus.PanelScale == 1.1,
            "unrelated values changed");

        var rebased = new LayoutConfig
        {
            CalibrationWidth = 1920,
            CalibrationHeight = 1080,
            StatusOffsetX = 80,
            StatusOffsetY = 120,
            StatusFontSize = 24,
        };
        rebased.SetCalibrationSize(3840, 2160);
        Check("status position rebases with calibration size",
            rebased.StatusOffsetX == 160 && rebased.StatusOffsetY == 240,
            "position=" + rebased.StatusOffsetX + "," + rebased.StatusOffsetY);
        Check("status font size does not rebase", rebased.StatusFontSize == 24,
            "actual=" + rebased.StatusFontSize);

        return _failures == 0 ? 0 : 1;
    }
}
