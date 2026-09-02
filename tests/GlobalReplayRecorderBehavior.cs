using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BobCoach.Engine;

internal static class GlobalReplayRecorderBehavior
{
    private static int Main()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "bobcoach-global-replay-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            try
            {
                using (var recorder = new GlobalReplayRecorder(root))
                {
                    recorder.Start("TB_BaconShop_HERO_35", "尤格-萨隆", "1", "248348", "S14");
                    recorder.RecordState(new GlobalReplayState
                    {
                        Turn = 1,
                        Gold = 3,
                        LiveGold = 3,
                        MaxGold = 3,
                        Tier = 1,
                        Phase = "shop",
                        Shop = new List<GlobalCardRef>
                        {
                            new GlobalCardRef { EntityId = 11, CardId = "BG23_000", Attack = 1, Health = 4, Tier = 1, Position = 0, Cost = 3 },
                        },
                    });
                    recorder.RecordState(new GlobalReplayState
                    {
                        Turn = 1,
                        Gold = 3,
                        LiveGold = 3,
                        MaxGold = 3,
                        Tier = 1,
                        Phase = "shop",
                        Shop = new List<GlobalCardRef>
                        {
                            new GlobalCardRef { EntityId = 11, CardId = "BG23_000", Attack = 1, Health = 4, Tier = 1, Position = 0, Cost = 3 },
                        },
                    });
                    recorder.RecordAction("buy", "BG23_000", 0, 3, 1, "state");
                    recorder.RecordAction("buy", "BG23_000", 0, 3, 1, "state");
                    recorder.RecordPowerAction(
                        "buy", "BG23_000", 42, "BG23_000", 6, 1, "shop");
                    recorder.RecordCombatEvent(
                        "attack", 11, "BG23_000", 6, 22, "BG23_001", 14,
                        0, 0, 2, "combat");
                    recorder.RecordChoice(
                        "discover", 7, 12, "BG33_101",
                        new[] { "BG23_000", "BGS_004" }, 3);
                    recorder.RecordRecommendation(
                        "{\"type\":\"BuyMinion\",\"target\":0}",
                        "[{\"type\":\"BuyMinion\",\"target\":0,\"score\":0.7}]",
                        "[{\"type\":\"BuyMinion\",\"target\":0,\"score\":0.7}]",
                        "1.0,0.0",
                        "{\"archetype\":\"BEAST\",\"tier\":\"T0\"}",
                        1);
                    recorder.RecordGameEnd(1, 10, 2, 15.25);
                }

                var files = Directory.GetFiles(root, "global_*.jsonl");
                if (files.Length != 1)
                    throw new InvalidOperationException("expected exactly one global replay file");
                var lines = File.ReadAllLines(files[0]);
                Assert(lines.Length == 8, "expected 8 compact event lines, got " + lines.Length);
                Assert(lines[0].Contains("\"e\":\"meta\""), "meta event missing");
                Assert(lines[1].Contains("\"e\":\"state\""), "state event missing");
                Assert(lines[2].Contains("\"e\":\"action\""), "action event missing");
                Assert(lines[3].Contains("\"e\":\"powerAction\""), "powerAction event missing");
                Assert(lines[4].Contains("\"e\":\"combat\""), "combat event missing");
                Assert(lines[5].Contains("\"e\":\"choice\""), "choice event missing");
                Assert(lines[6].Contains("\"e\":\"recommend\""), "recommend event missing");
                Assert(lines[6].Contains("\"mainlinePlan\":{\"archetype\":\"BEAST\",\"tier\":\"T0\"}"),
                    "recommend event must include mainlinePlan");
                Assert(lines[7].Contains("\"e\":\"end\""), "end event missing");
                Assert(lines.Count(line => line.Contains("\"e\":\"state\"")) == 1, "duplicate state was written");
                Assert(lines.Count(line => line.Contains("\"e\":\"action\"")) == 1, "duplicate action was written");
                Assert(lines.Count(line => line.Contains("\"e\":\"powerAction\"")) == 1,
                    "duplicate powerAction was written");
                Assert(lines.Count(line => line.Contains("\"e\":\"combat\"")) == 1,
                    "duplicate combat was written");
                Console.WriteLine("PASS global replay recorder compact streaming contract");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FAIL " + ex.GetType().Name + ": " + ex.Message);
                return 1;
            }
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private static void Assert(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
    }
}
