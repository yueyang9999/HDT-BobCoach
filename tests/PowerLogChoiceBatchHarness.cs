using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BobCoach.Engine;

public static class PowerLogChoiceBatchHarness
{
    public static int Main(string[] args)
    {
        if (args != null && args.Length >= 1)
        {
            if (args.Length >= 2 && args[1] == "--kelthuzad-20260712")
                return ReplayKelThuzad20260712(args[0]);
            if (args.Length >= 2 && args[1] == "--cube-20260712")
                return ReplayCube20260712(args[0]);
            if (args.Length >= 2 && args[1] == "--greater-timewarp-20260712")
                return ReplayGreaterTimewarp20260712(args[0]);
            if (args.Length >= 2 && args[1] == "--kelthuzad-20260713")
                return ReplayKelThuzad20260713(args[0]);
            if (args.Length >= 2 && args[1] == "--timewarp-purchase-20260713")
                return ReplayTimewarpPurchase20260713(args[0]);
            if (args.Length >= 2 && args[1] == "--upgrade-prize-20260713")
                return ReplayUpgradePrize20260713(args[0]);
            return ReplayCapturedLog(args[0]);
        }

        var parser = new PowerLogParser();
        PowerLogChoiceBatch last = null;
        PowerLogChoiceBatch lastDiscover = null;
        PowerLogChoiceBatch lastTimewarpPurchase = null;
        PowerLogChoiceCompletion completed = null;
        PLEvent goldTransfer = null;
        int detectedBuild = 0;
        parser.TrinketChoiceActive += batch => last = batch;
        parser.DiscoverOffered += batch => lastDiscover = batch;
        parser.TimewarpPurchaseOffered += batch => lastTimewarpPurchase = batch;
        parser.ChoiceCompleted += completion => completed = completion;
        parser.TeammateGoldTransferObserved += evt => goldTransfer = evt;
        parser.BuildNumberDetected += build => detectedBuild = build;

        parser.ParseLine("D 18:09:36.8559084 GameState.DebugPrintGame() - BuildNumber=246003");
        if (detectedBuild != 246003) return Fail("Power.log BuildNumber was not emitted");
        parser.ParseLine("D 18:09:36.8559084 GameState.DebugPrintGame() - BuildNumber=246003");
        if (parser.CurrentBuildNumber != 246003) return Fail("Power.log current build was not retained");

        var scanParser = new PowerLogParser();
        int scannedBuild = 0;
        scanParser.BuildNumberDetected += build => scannedBuild = build;
        var sharedLogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "powerlog-buildnumber-shared.log");
        File.WriteAllText(sharedLogPath,
            "D 20:42:50.0000000 GameState.DebugPrintGame() - BuildNumber=246003\r\n");
        using (var writer = new FileStream(
            sharedLogPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
        {
            if (!PowerLogInitialBuildScanner.TryScan(sharedLogPath, scanParser))
                return Fail("initial BuildNumber scan failed while Power.log was open for writing");
        }
        if (scannedBuild != 246003)
            return Fail("shared Power.log BuildNumber was not emitted");

        parser.ParseLine("D 11:04:26.8790451 GameState.DebugPrintEntityChoices() - id=12 Player=Local TaskList=3600 ChoiceType=GENERAL CountMin=1 CountMax=1");
        parser.ParseLine("D 11:04:26.8790451 GameState.DebugPrintEntityChoices() -   Source=[entityName=Scheduled Trinket id=498 zone=PLAY zonePos=0 cardId=BG30_Trinket_1st player=2]");
        parser.ParseLine("D 11:04:26.8790451 GameState.DebugPrintEntityChoices() -   Entities[0]=[entityName=Option A id=10174 zone=SETASIDE zonePos=0 cardId=BG32_MagicItem_858 player=2]");
        parser.ParseLine("D 11:04:26.8790451 GameState.DebugPrintEntityChoices() -   Entities[1]=[entityName=Option B id=10171 zone=SETASIDE zonePos=0 cardId=BG32_MagicItem_428 player=2]");
        parser.ParseLine("D 11:04:26.8790451 GameState.DebugPrintEntityChoices() -   Entities[2]=[entityName=Option C id=10172 zone=SETASIDE zonePos=0 cardId=BG32_MagicItem_894 player=2]");
        parser.ParseLine("D 11:04:26.8790451 GameState.DebugPrintEntityChoices() -   Entities[3]=[entityName=Option D id=10173 zone=SETASIDE zonePos=0 cardId=BG35_MagicItem_834 player=2]");

        if (last == null) return Fail("no trinket choice batch emitted");
        if (last.ChoiceId != 12) return Fail("choiceId was not preserved");
        if (last.TaskList != 3600) return Fail("taskList was not preserved");
        if (last.SourceCardId != "BG30_Trinket_1st") return Fail("sourceCardId was not preserved");
        if (last.SourcePlayerId != 2) return Fail("sourcePlayerId was not preserved");
        if (last.Candidates == null || last.Candidates.Count != 4) return Fail("final four-candidate batch missing");

        var lifecycle = new TrinketChoiceBatchLifecycle();
        var binding = lifecycle.Observe(last, 8);
        if (binding.TargetTurn != 9) return Fail("scheduled greater trinket was not bound to turn 9");
        if (!lifecycle.TryGetForTurn(8, out var activeAtEight))
            return Fail("explicit scheduled trinket batch was hidden until the cached turn advanced");
        if (activeAtEight.Batch.ChoiceId != 12) return Fail("wrong choice batch was active immediately");
        if (!lifecycle.TryGetForTurn(9, out var activeAtNine)) return Fail("turn-9 batch was discarded on transition");
        if (activeAtNine.Batch.ChoiceId != 12) return Fail("wrong choice batch survived transition");

        parser.ParseLine("D 11:05:37.3860758 GameState.SendChoices() - id=12 ChoiceType=GENERAL");
        parser.ParseLine("D 11:05:37.3860758 GameState.SendChoices() -   m_chosenEntities[0]=[entityName=Option C id=10172 zone=SETASIDE zonePos=0 cardId=BG32_MagicItem_894 player=2]");

        if (completed == null) return Fail("no typed completion emitted");
        if (completed.ChoiceId != 12) return Fail("completion choiceId mismatch");
        if (completed.SelectedCardId != "BG32_MagicItem_894") return Fail("selectedCardId was not preserved");
        if (completed.SelectedEntityId != 10172) return Fail("selectedEntityId was not preserved");
        var finishedBinding = lifecycle.Complete(completed);
        if (finishedBinding == null || finishedBinding.Batch.ChoiceId != 12)
            return Fail("matching completion did not close its batch");
        if (lifecycle.TryGetForTurn(9, out _)) return Fail("completed batch remained active");

        parser.ParseLine("D 11:09:04.5203600 GameState.DebugPrintEntityChoices() - id=29 Player=Local TaskList=5865 ChoiceType=GENERAL CountMin=1 CountMax=1");
        parser.ParseLine("D 11:09:04.5203600 GameState.DebugPrintEntityChoices() -   Source=[entityName=Newborn Sapling id=14958 zone=PLAY zonePos=0 cardId=BG33_101 player=2]");
        parser.ParseLine("D 11:09:04.5203600 GameState.DebugPrintEntityChoices() -   Entities[0]=[entityName=Discover A id=15000 zone=SETASIDE zonePos=0 cardId=BG28_300 player=2]");
        parser.ParseLine("D 11:09:04.5203600 GameState.DebugPrintEntityChoices() -   Entities[1]=[entityName=Discover B id=15001 zone=SETASIDE zonePos=0 cardId=BG26_146 player=2]");
        parser.ParseLine("D 11:09:04.5203600 GameState.DebugPrintEntityChoices() -   Entities[2]=[entityName=Discover C id=15002 zone=SETASIDE zonePos=0 cardId=BG33_140 player=2]");
        if (lastDiscover == null || lastDiscover.ChoiceId != 29)
            return Fail("discover choice identity missing");
        if (lastDiscover.SourceCardId != "BG33_101" || lastDiscover.Candidates.Count != 3)
            return Fail("discover source or candidates missing");

        parser.ParseLine("D 11:09:05.5203600 GameState.DebugPrintEntityChoices() - id=30 Player=Local TaskList=5866 ChoiceType=GENERAL CountMin=1 CountMax=1");
        parser.ParseLine("D 11:09:05.5203600 GameState.DebugPrintEntityChoices() -   Source=[entityName=Timewarp id=14959 zone=PLAY zonePos=0 cardId=BG34_HERO_004p player=2]");
        parser.ParseLine("D 11:09:05.5203600 GameState.DebugPrintEntityChoices() -   Entities[0]=[entityName=Timewarp A id=15010 zone=SETASIDE zonePos=0 cardId=BG34_Treasure_601 player=2]");
        parser.ParseLine("D 11:09:05.5203600 GameState.DebugPrintEntityChoices() -   Entities[1]=[entityName=Timewarp B id=15011 zone=SETASIDE zonePos=0 cardId=BG34_Treasure_602 player=2]");
        parser.ParseLine("D 11:09:05.5203600 GameState.DebugPrintEntityChoices() -   Entities[2]=[entityName=Timewarp C id=15012 zone=SETASIDE zonePos=0 cardId=BG34_Treasure_603 player=2]");
        parser.ParseLine("D 11:09:05.5203600 GameState.DebugPrintEntityChoices() -   Entities[3]=[entityName=Timewarp D id=15013 zone=SETASIDE zonePos=0 cardId=BG34_Treasure_604 player=2]");
        parser.ParseLine("D 11:09:05.5203600 GameState.DebugPrintEntityChoices() -   Entities[4]=[entityName=Timewarp E id=15014 zone=SETASIDE zonePos=0 cardId=BG34_Treasure_605 player=2]");
        if (lastTimewarpPurchase == null || lastTimewarpPurchase.ChoiceId != 30
            || lastTimewarpPurchase.SourceCardId != "BG34_HERO_004p"
            || lastTimewarpPurchase.Candidates.Count != 5)
            return Fail("five-candidate timewarp purchase batch was truncated or rejected");
        if (!CombatChoiceRenderPolicy.CanRenderDiscoverDuringCombat(lastDiscover, true))
            return Fail("authoritative active discover batch was blocked during lagging combat flag");
        if (CombatChoiceRenderPolicy.CanRenderDiscoverDuringCombat(lastDiscover, false))
            return Fail("inactive discover batch was allowed during combat");
        if (CombatChoiceRenderPolicy.CanRenderDiscoverDuringCombat(
            new PowerLogChoiceBatch { ChoiceId = -1, Candidates = lastDiscover.Candidates }, true))
            return Fail("discover batch without valid choiceId was allowed during combat");
        if (CombatChoiceRenderPolicy.CanRenderDiscoverDuringCombat(
            new PowerLogChoiceBatch
            {
                ChoiceId = 31,
                Candidates = new System.Collections.Generic.List<DiscoverCandidate>
                {
                    new DiscoverCandidate { CardId = "BG28_300", EntityId = 15000 },
                },
            }, true))
            return Fail("single-candidate residue was allowed during combat");

        var timewarpParser = new PowerLogParser();
        PowerLogChoiceBatch timewarpPurchase = null;
        int timewarpPurchaseEvents = 0;
        int timewarpDiscoverEvents = 0;
        timewarpParser.TimewarpPurchaseOffered += batch =>
        {
            timewarpPurchase = batch;
            timewarpPurchaseEvents++;
        };
        timewarpParser.DiscoverOffered += batch => timewarpDiscoverEvents++;
        timewarpParser.ParseLine("D 11:37:40.0008280 GameState.DebugPrintPower() - TAG_CHANGE Entity=LocalPlayer tag=BACON_ALT_TAVERN_COIN value=2");
        timewarpParser.ParseLine("D 11:37:40.0008280 GameState.DebugPrintPower() - FULL_ENTITY - Creating ID=5567 CardID=BG34_Treasure_932");
        timewarpParser.ParseLine("D 11:37:40.0008280 GameState.DebugPrintPower() - TAG_CHANGE Entity=5567 tag=BACON_OVERRIDE_BG_COST value=2");
        timewarpParser.ParseLine("D 11:37:40.0008280 GameState.DebugPrintPower() - FULL_ENTITY - Creating ID=5568 CardID=BG34_Giant_034");
        timewarpParser.ParseLine("D 11:37:40.0008280 GameState.DebugPrintPower() - TAG_CHANGE Entity=5568 tag=BACON_OVERRIDE_BG_COST value=1");
        timewarpParser.ParseLine("D 11:37:40.0063595 GameState.DebugPrintEntityChoices() - id=10 Player=LocalPlayer TaskList=2010 ChoiceType=GENERAL CountMin=1 CountMax=1");
        timewarpParser.ParseLine("D 11:37:40.0063595 GameState.DebugPrintEntityChoices() -   Source=[entityName=Parallel Timeline id=312 zone=PLAY zonePos=0 cardId=BG34_HERO_000p player=1]");
        timewarpParser.ParseLine("D 11:37:40.0063595 GameState.DebugPrintEntityChoices() -   Entities[0]=[entityName=Timewarp Oil id=5567 zone=SETASIDE zonePos=0 cardId=BG34_Treasure_932 player=1]");
        timewarpParser.ParseLine("D 11:37:40.0063595 GameState.DebugPrintEntityChoices() -   Entities[1]=[entityName=Timewarp Ghoul id=5568 zone=SETASIDE zonePos=0 cardId=BG34_Giant_034 player=1]");
        if (timewarpPurchase == null || timewarpPurchase.ChoiceId != 10)
            return Fail("timewarp purchase batch was not emitted");
        if (timewarpPurchase.TimeCoinCount != 2)
            return Fail("timewarp purchase did not preserve dynamic alternate currency");
        if (timewarpPurchase.Candidates.Count != 2
            || timewarpPurchase.Candidates[0].PurchaseCost != 2
            || timewarpPurchase.Candidates[1].PurchaseCost != 1)
            return Fail("timewarp purchase candidate costs were not preserved");
        if (timewarpDiscoverEvents != 0)
            return Fail("timewarp purchase leaked into ordinary discover events");
        int eventsBeforeCoinUpdate = timewarpPurchaseEvents;
        timewarpParser.ParseLine("D 11:37:41.0008280 GameState.DebugPrintPower() - TAG_CHANGE Entity=LocalPlayer tag=BACON_ALT_TAVERN_COIN value=3");
        if (timewarpPurchaseEvents != eventsBeforeCoinUpdate + 1
            || timewarpPurchase == null || timewarpPurchase.ChoiceId != 10
            || timewarpPurchase.TimeCoinCount != 3)
            return Fail("active timewarp purchase did not refresh when alternate currency changed to three");

        var purchaseScores = new Dictionary<int, double>
        {
            { 0, 100.0 },
            { 1, 50.0 },
            { 2, 40.0 },
        };
        var pricedBatch = new PowerLogChoiceBatch
        {
            TimeCoinCount = 2,
            Candidates = new List<DiscoverCandidate>
            {
                new DiscoverCandidate { CardId = "cost3", PurchaseCost = 3 },
                new DiscoverCandidate { CardId = "cost2", PurchaseCost = 2 },
                new DiscoverCandidate { CardId = "cost1", PurchaseCost = 1 },
            },
        };
        if (TimewarpPurchaseAdvisor.SelectBestAffordableIndex(pricedBatch, purchaseScores) != 1)
            return Fail("two time coins did not select the best affordable candidate");
        pricedBatch.TimeCoinCount = 3;
        if (TimewarpPurchaseAdvisor.SelectBestAffordableIndex(pricedBatch, purchaseScores) != 0)
            return Fail("three time coins did not unlock the higher-cost best candidate");

        parser.ParseLine("D 11:10:00.0000000 GameState.DebugPrintPower() - TAG_CHANGE Entity=GameEntity tag=TURN value=3");
        parser.ParseLine("D 11:10:00.1000000 GameState.DebugPrintPower() - FULL_ENTITY Creating ID=901 CardID=BGDUO_Anomaly_007t");
        parser.ParseLine("D 11:10:01.1000000 GameState.DebugPrintPower() - BLOCK_START BlockType=POWER Entity=[entityName=PooledResources id=901 zone=PLAY zonePos=0 cardId=BGDUO_Anomaly_007t player=2] EffectCardId= System.PowerTaskList.DebugPrintPower");
        if (goldTransfer == null || goldTransfer.CardId != "BGDUO_Anomaly_007t"
            || goldTransfer.EntityId != 901 || goldTransfer.Turn != 3
            || goldTransfer.Type != PLEventType.HeroPowerUsed)
            return Fail("exact teammate gold transfer POWER block was not emitted: "
                + (goldTransfer == null ? "null" : string.Format(
                    "card={0} entity={1} turn={2} type={3}", goldTransfer.CardId,
                    goldTransfer.EntityId, goldTransfer.Turn, goldTransfer.Type)));
        goldTransfer = null;
        parser.ParseLine("D 11:10:02.1000000 GameState.DebugPrintPower() - BLOCK_START BlockType=POWER Entity=[entityName=PooledResources id=902 zone=PLAY zonePos=0 cardId=BGDUO_Anomaly_007t player=2] EffectCardId= System.PowerTaskList.DebugPrintPower");
        if (goldTransfer == null || goldTransfer.EntityId != 902
            || goldTransfer.CardId != "BGDUO_Anomaly_007t")
            return Fail("self-contained teammate transfer POWER block depended on FULL_ENTITY cache");

        var lesser = new PowerLogChoiceBatch { ChoiceId = 5, SourceCardId = "BG30_Trinket_1st" };
        var lesserBinding = lifecycle.Observe(lesser, 5);
        if (lesserBinding.TargetTurn != 6 || lesserBinding.Context != TrinketChoiceContext.ScheduledLesser)
            return Fail("scheduled lesser trinket was not bound to turn 6");

        var cubeExtra = new PowerLogChoiceBatch
        {
            ChoiceId = 31,
            SourceCardId = "BG30_MagicItem_703t",
            Candidates = new System.Collections.Generic.List<DiscoverCandidate>
            {
                new DiscoverCandidate { CardId = "BG30_MagicItem_841", EntityId = 16001 },
                new DiscoverCandidate { CardId = "BG32_MagicItem_416", EntityId = 16002 },
            },
        };
        var cubeBinding = lifecycle.Observe(cubeExtra, 4);
        if (cubeBinding.Context != TrinketChoiceContext.AnomalyExtra || cubeBinding.TargetTurn != 4)
            return Fail("cube extra trinket was not classified at its observed turn");
        if (!lifecycle.TryGetForTurn(5, out var cubeAtNextTurn)
            || cubeAtNextTurn.Batch.ChoiceId != 31)
            return Fail("explicit cube trinket choice was discarded across combat-to-recruit turn transition");
        if (lifecycle.Complete(new PowerLogChoiceCompletion { ChoiceId = 999 }) != null)
            return Fail("mismatched completion closed the cube trinket batch");
        if (lifecycle.Complete(new PowerLogChoiceCompletion { ChoiceId = 31 }) == null)
            return Fail("matching completion did not close the cross-turn cube trinket batch");
        if (lifecycle.TryGetForTurn(5, out _))
            return Fail("completed cross-turn cube trinket batch remained active");

        var anomaly = new PowerLogChoiceBatch
        {
            ChoiceId = 13,
            SourceCardId = "BG30_MagicItem_703t",
            Candidates = new System.Collections.Generic.List<DiscoverCandidate>
            {
                new DiscoverCandidate { CardId = "BG30_MagicItem_994", EntityId = 10558 },
                new DiscoverCandidate { CardId = "BG32_MagicItem_416", EntityId = 10556 },
            },
        };
        var anomalyBinding = lifecycle.Observe(anomaly, 9);
        if (anomalyBinding.Context != TrinketChoiceContext.AnomalyExtra)
            return Fail("anomaly trinket choice was not classified separately");
        if (anomalyBinding.EligibleForCalibration)
            return Fail("anomaly trinket choice was calibration-eligible");
        if (lifecycle.Complete(completed) != null || !lifecycle.TryGetForTurn(9, out _))
            return Fail("non-matching completion cleared the active anomaly batch");
        var anomalyCompletion = new PowerLogChoiceCompletion { ChoiceId = 13, SelectedCardId = "BG32_MagicItem_416" };
        var shadow = new TrinketShadowCaptureSession();
        shadow.Stage(anomalyBinding, 5, true, 18,
            new System.Collections.Generic.List<TrinketShadowOffer>
            {
                new TrinketShadowOffer { CardId = "BG30_MagicItem_994", Name = "Option 1", Score = 2.0 },
                new TrinketShadowOffer { CardId = "BG32_MagicItem_416", Name = "Option 2", Score = 6.0 },
            });
        var shadowRecord = shadow.Complete(anomalyBinding, anomalyCompletion);
        if (shadowRecord == null || shadowRecord.SchemaVersion != 2)
            return Fail("schema-v2 shadow record was not completed");
        if (shadowRecord.ChoiceId != 13 || shadowRecord.SelectedCardId != "BG32_MagicItem_416")
            return Fail("shadow completion identity was not preserved");
        if (shadowRecord.EligibleForCalibration || shadowRecord.SelectionContext != "anomaly_extra")
            return Fail("anomaly shadow record was not excluded from calibration");
        var incompleteShadow = new TrinketShadowCaptureSession();
        incompleteShadow.Stage(anomalyBinding, 5, true, 18,
            new System.Collections.Generic.List<TrinketShadowOffer>());
        if (incompleteShadow.Complete(anomalyBinding,
            new PowerLogChoiceCompletion { ChoiceId = 13 }) != null)
            return Fail("completion without selectedCardId produced a shadow record");
        if (lifecycle.Complete(anomalyCompletion) == null)
            return Fail("matching anomaly completion did not close its batch");

        Console.WriteLine("PASS trinket/discover batch metadata and typed completion");
        return 0;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine("FAIL " + message);
        return 1;
    }

    private static int ReplayCapturedLog(string path)
    {
        if (!File.Exists(path)) return Fail("replay log not found");
        var parser = new PowerLogParser();
        var trinketBatches = new List<PowerLogChoiceBatch>();
        var completions = new List<PowerLogChoiceCompletion>();
        parser.TrinketChoiceActive += batch =>
        {
            if (batch != null && batch.Candidates != null && batch.Candidates.Count > 0)
                trinketBatches.Add(batch);
        };
        parser.ChoiceCompleted += completion =>
        {
            if (completion != null) completions.Add(completion);
        };
        foreach (var line in File.ReadLines(path)) parser.ParseLine(line);

        var scheduled = trinketBatches.LastOrDefault(b => b.ChoiceId == 12 && b.Candidates.Count == 4);
        if (scheduled == null || scheduled.SourceCardId != "BG30_Trinket_1st")
            return Fail("captured choice 12 scheduled batch missing");
        var scheduledDone = completions.LastOrDefault(c => c.ChoiceId == 12);
        if (scheduledDone == null || scheduledDone.SelectedCardId != "BG32_MagicItem_894")
            return Fail("captured choice 12 completion missing");

        var anomaly = trinketBatches.LastOrDefault(b => b.ChoiceId == 13 && b.Candidates.Count == 2);
        if (anomaly == null || anomaly.SourceCardId != "BG30_MagicItem_703t")
            return Fail("captured choice 13 anomaly batch missing");
        var anomalyDone = completions.LastOrDefault(c => c.ChoiceId == 13);
        if (anomalyDone == null || anomalyDone.SelectedCardId != "BG32_MagicItem_416")
            return Fail("captured choice 13 completion missing");

        Console.WriteLine("PASS captured log choices 12/13 remain distinct");
        return 0;
    }

    private static int ReplayKelThuzad20260712(string path)
    {
        if (!File.Exists(path)) return Fail("replay log not found");
        var parser = new PowerLogParser();
        var discovers = new List<PowerLogChoiceBatch>();
        var timewarpPurchases = new List<PowerLogChoiceBatch>();
        var trinkets = new List<PowerLogChoiceBatch>();
        var completions = new List<PowerLogChoiceCompletion>();
        parser.DiscoverOffered += batch =>
        {
            if (batch != null && batch.Candidates != null && batch.Candidates.Count > 0)
                discovers.Add(batch);
        };
        parser.TimewarpPurchaseOffered += batch =>
        {
            if (batch != null && batch.Candidates != null && batch.Candidates.Count > 0)
                timewarpPurchases.Add(batch);
        };
        parser.TrinketChoiceActive += batch =>
        {
            if (batch != null && batch.Candidates != null && batch.Candidates.Count > 0)
                trinkets.Add(batch);
        };
        parser.ChoiceCompleted += completion =>
        {
            if (completion != null) completions.Add(completion);
        };
        foreach (var line in File.ReadLines(path)) parser.ParseLine(line);

        var timewarp = timewarpPurchases.LastOrDefault(b => b.ChoiceId == 2 && b.Candidates.Count == 5);
        if (timewarp == null || timewarp.SourceCardId != "BG34_HERO_004p")
            return Fail("20260712 timewarp five-option purchase batch missing");
        foreach (var choiceId in new[] { 4, 16 })
        {
            if (!trinkets.Any(b => b.ChoiceId == choiceId && b.Candidates.Count == 4))
                return Fail("20260712 scheduled trinket batch missing: choice=" + choiceId);
            if (!completions.Any(c => c.ChoiceId == choiceId))
                return Fail("20260712 scheduled trinket completion missing: choice=" + choiceId);
        }
        foreach (var choiceId in new[] { 8, 30, 36 })
        {
            var batch = discovers.LastOrDefault(b => b.ChoiceId == choiceId && b.Candidates.Count == 3);
            if (batch == null || batch.SourceCardId != "BG34_Treasure_606pe")
                return Fail("20260712 combat-end treasure discover missing: choice=" + choiceId);
            if (!completions.Any(c => c.ChoiceId == choiceId))
                return Fail("20260712 combat-end treasure completion missing: choice=" + choiceId);
        }

        Console.WriteLine("PASS 20260712 timewarp, scheduled trinkets, and combat-end treasure discovers");
        return 0;
    }

    private static int ReplayKelThuzad20260713(string path)
    {
        if (!File.Exists(path)) return Fail("replay log not found");
        var parser = new PowerLogParser();
        var timewarpPurchases = new List<PowerLogChoiceBatch>();
        var trinkets = new List<PowerLogChoiceBatch>();
        var completions = new List<PowerLogChoiceCompletion>();
        parser.TimewarpPurchaseOffered += batch =>
        {
            if (batch != null && batch.Candidates != null && batch.Candidates.Count > 0)
                timewarpPurchases.Add(batch);
        };
        parser.TrinketChoiceActive += batch =>
        {
            if (batch != null && batch.Candidates != null && batch.Candidates.Count > 0)
                trinkets.Add(batch);
        };
        parser.ChoiceCompleted += completion =>
        {
            if (completion != null) completions.Add(completion);
        };
        foreach (var line in File.ReadLines(path)) parser.ParseLine(line);

        var timewarp = timewarpPurchases.LastOrDefault(batch =>
            batch.ChoiceId == 3 && batch.Candidates.Count == 5);
        if (timewarp == null || timewarp.SourceCardId != "BG34_HERO_004p")
            return Fail("20260713 lesser timewarp five-option batch missing");
        if (timewarp.TimeCoinCount <= 0
            || timewarp.Candidates.Any(candidate => candidate.PurchaseCost <= 0))
            return Fail("20260713 lesser timewarp currency or purchase costs missing");
        var timewarpDone = completions.LastOrDefault(completion => completion.ChoiceId == 3);
        if (timewarpDone == null || timewarpDone.SelectedCardId != "BG34_Giant_038")
            return Fail("20260713 lesser timewarp completion missing");

        var greaterTrinket = trinkets.LastOrDefault(batch =>
            batch.ChoiceId == 18 && batch.Candidates.Count == 4);
        if (greaterTrinket == null || greaterTrinket.SourceCardId != "BG30_Trinket_2nd")
            return Fail("20260713 greater trinket batch missing");
        var trinketDone = completions.LastOrDefault(completion => completion.ChoiceId == 18);
        if (trinketDone == null || trinketDone.SelectedCardId != "BG32_MagicItem_807")
            return Fail("20260713 greater trinket completion missing");

        Console.WriteLine("PASS 20260713 lesser timewarp and greater trinket choices");
        return 0;
    }

    private static int ReplayTimewarpPurchase20260713(string path)
    {
        if (!File.Exists(path)) return Fail("replay log not found");
        var parser = new PowerLogParser();
        var timewarpPurchases = new List<PowerLogChoiceBatch>();
        var discoverBatches = new List<PowerLogChoiceBatch>();
        var completions = new List<PowerLogChoiceCompletion>();
        parser.TimewarpPurchaseOffered += batch =>
        {
            if (batch != null && batch.Candidates != null && batch.Candidates.Count > 0)
                timewarpPurchases.Add(batch);
        };
        parser.DiscoverOffered += batch =>
        {
            if (batch != null) discoverBatches.Add(batch);
        };
        parser.ChoiceCompleted += completion =>
        {
            if (completion != null) completions.Add(completion);
        };
        foreach (var line in File.ReadLines(path)) parser.ParseLine(line);

        var purchase = timewarpPurchases.LastOrDefault(batch =>
            batch.ChoiceId == 10 && batch.Candidates.Count == 5);
        if (purchase == null || purchase.SourceCardId != "BG34_HERO_000p")
            return Fail("20260713 greater timewarp purchase batch missing");
        if (purchase.TimeCoinCount != 2)
            return Fail("20260713 greater timewarp dynamic coin count mismatch");
        var costs = purchase.Candidates.Select(candidate => candidate.PurchaseCost).ToArray();
        if (!costs.SequenceEqual(new[] { 2, 2, 1, 1, 1 }))
            return Fail("20260713 greater timewarp candidate costs mismatch: "
                + string.Join(",", costs));
        if (discoverBatches.Any(batch => batch.ChoiceId == 10
            && batch.SourceCardId == "BG34_HERO_000p"))
            return Fail("20260713 greater timewarp leaked into discover event");
        var completed = completions.LastOrDefault(completion => completion.ChoiceId == 10);
        if (completed == null || completed.SelectedCardId != "BG34_Treasure_932")
            return Fail("20260713 greater timewarp purchase completion missing");

        Console.WriteLine("PASS 20260713 greater timewarp purchase currency and costs");
        return 0;
    }

    private static int ReplayUpgradePrize20260713(string path)
    {
        if (!File.Exists(path)) return Fail("replay log not found");
        var parser = new PowerLogParser();
        var discovers = new List<PowerLogChoiceBatch>();
        var trinkets = new List<PowerLogChoiceBatch>();
        var timewarpPurchases = new List<PowerLogChoiceBatch>();
        var completions = new List<PowerLogChoiceCompletion>();
        parser.DiscoverOffered += batch =>
        {
            if (batch != null && batch.Candidates != null && batch.Candidates.Count > 0)
                discovers.Add(batch);
        };
        parser.TrinketChoiceActive += batch =>
        {
            if (batch != null && batch.Candidates != null && batch.Candidates.Count > 0)
                trinkets.Add(batch);
        };
        parser.TimewarpPurchaseOffered += batch =>
        {
            if (batch != null && batch.Candidates != null && batch.Candidates.Count > 0)
                timewarpPurchases.Add(batch);
        };
        parser.ChoiceCompleted += completion =>
        {
            if (completion != null) completions.Add(completion);
        };
        foreach (var line in File.ReadLines(path)) parser.ParseLine(line);

        var prizeSelections = new Dictionary<int, string>
        {
            { 3, "BGS_Treasures_014" },
            { 5, "BGS_Treasures_030" },
            { 8, "BGS_Treasures_012" },
            { 12, "BGS_Treasures_037" },
            { 21, "BGS_Treasures_039" },
        };
        foreach (var pair in prizeSelections)
        {
            if (!discovers.Any(batch => batch.ChoiceId == pair.Key
                && batch.SourceCardId == "BG27_Anomaly_716"
                && batch.Candidates.Count == 3))
                return Fail("20260713 upgrade-prize batch missing: choice=" + pair.Key);
            if (!completions.Any(completion => completion.ChoiceId == pair.Key
                && completion.SelectedCardId == pair.Value))
                return Fail("20260713 upgrade-prize completion missing: choice=" + pair.Key);
        }
        if (!trinkets.Any(batch => batch.ChoiceId == 6 && batch.Candidates.Count == 4)
            || !trinkets.Any(batch => batch.ChoiceId == 15 && batch.Candidates.Count == 4))
            return Fail("20260713 scheduled trinket batches missing");
        if (timewarpPurchases.Count != 0)
            return Fail("20260713 upgrade-prize game unexpectedly emitted timewarp purchases");
        int completedDiscoverChoices = discovers
            .Where(batch => batch.Candidates.Count == 3)
            .Select(batch => batch.ChoiceId).Distinct().Count();
        if (completedDiscoverChoices != 18)
            return Fail("20260713 discover batch count mismatch: " + completedDiscoverChoices);

        Console.WriteLine("PASS 20260713 upgrade-prize choices, scheduled trinkets, and 18 discovers");
        return 0;
    }

    private static int ReplayCube20260712(string path)
    {
        if (!File.Exists(path)) return Fail("replay log not found");
        var parser = new PowerLogParser();
        var trinkets = new List<PowerLogChoiceBatch>();
        var completions = new List<PowerLogChoiceCompletion>();
        parser.TrinketChoiceActive += batch =>
        {
            if (batch != null && batch.Candidates != null && batch.Candidates.Count > 0)
                trinkets.Add(batch);
        };
        parser.ChoiceCompleted += completion =>
        {
            if (completion != null) completions.Add(completion);
        };
        foreach (var line in File.ReadLines(path)) parser.ParseLine(line);

        var expectedCubeChoices = new[] { 2, 4, 5, 11, 14, 17, 21 };
        foreach (var choiceId in expectedCubeChoices)
        {
            var batch = trinkets.LastOrDefault(b => b.ChoiceId == choiceId && b.Candidates.Count == 2);
            if (batch == null || batch.SourceCardId != "BG30_MagicItem_703t")
                return Fail("20260712 cube extra trinket batch missing: choice=" + choiceId);
            if (!completions.Any(c => c.ChoiceId == choiceId))
                return Fail("20260712 cube extra trinket completion missing: choice=" + choiceId);
        }
        if (!trinkets.Any(b => b.ChoiceId == 3 && b.SourceCardId == "BG30_Trinket_1st"
                && b.Candidates.Count == 4)
            || !trinkets.Any(b => b.ChoiceId == 13 && b.SourceCardId == "BG30_Trinket_2nd"
                && b.Candidates.Count == 4))
            return Fail("20260712 scheduled lesser/greater trinket batches missing");

        Console.WriteLine("PASS 20260712 cube extras and scheduled trinket batches");
        return 0;
    }

    private static int ReplayGreaterTimewarp20260712(string path)
    {
        if (!File.Exists(path)) return Fail("replay log not found");
        var parser = new PowerLogParser();
        var timewarpPurchases = new List<PowerLogChoiceBatch>();
        var completions = new List<PowerLogChoiceCompletion>();
        parser.TimewarpPurchaseOffered += batch =>
        {
            if (batch != null && batch.Candidates != null && batch.Candidates.Count > 0)
                timewarpPurchases.Add(batch);
        };
        parser.ChoiceCompleted += completion =>
        {
            if (completion != null) completions.Add(completion);
        };
        foreach (var line in File.ReadLines(path)) parser.ParseLine(line);

        var timewarp = timewarpPurchases.LastOrDefault(b => b.ChoiceId == 4 && b.Candidates.Count == 5);
        if (timewarp == null || timewarp.SourceCardId != "BG34_HERO_000p")
            return Fail("20260712 turn-8 greater timewarp five-option batch missing");
        var completed = completions.LastOrDefault(c => c.ChoiceId == 4);
        if (completed == null || completed.SelectedCardId != "BG34_Treasure_955")
            return Fail("20260712 turn-8 greater timewarp completion missing");

        Console.WriteLine("PASS 20260712 turn-8 greater timewarp five-option batch and completion");
        return 0;
    }
}
