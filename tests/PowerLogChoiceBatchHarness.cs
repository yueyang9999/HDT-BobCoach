using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
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
            if (args.Length >= 2 && args[1] == "--watcher-session-switch")
                return RunWatcherSessionSwitchTest(args[0]);
            if (args[0] == "--watcher-late-start"
                || (args.Length >= 2 && args[1] == "--watcher-late-start"))
                return RunWatcherLateStartTest(null);
            if (args[0] == "--watcher-late-start-live-completion"
                || (args.Length >= 2 && args[1] == "--watcher-late-start-live-completion"))
                return RunWatcherLateStartLiveCompletionTest(null);
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

        var darkGiftParser = new PowerLogParser();
        PowerLogChoiceBatch darkGift = null;
        PowerLogChoiceCompletion darkGiftDone = null;
        int darkGiftActiveEvents = 0;
        int darkGiftDiscoverLeaks = 0;
        int darkGiftTrinketLeaks = 0;
        int darkGiftTimewarpLeaks = 0;
        int darkGiftChoiceCompletedLeaks = 0;
        bool darkGiftChoiceListActiveDuringOffer = true;
        darkGiftParser.DarkGiftChoiceActive += batch =>
        {
            darkGift = batch;
            darkGiftActiveEvents++;
            darkGiftChoiceListActiveDuringOffer = darkGiftParser.IsChoiceListActive();
        };
        darkGiftParser.DarkGiftCompleted += completion => darkGiftDone = completion;
        darkGiftParser.DiscoverOffered += batch => darkGiftDiscoverLeaks++;
        darkGiftParser.TrinketChoiceActive += batch => darkGiftTrinketLeaks++;
        darkGiftParser.TimewarpPurchaseOffered += batch => darkGiftTimewarpLeaks++;
        darkGiftParser.ChoiceCompleted += completion => darkGiftChoiceCompletedLeaks++;

        darkGiftParser.ParseLine("D 18:09:36.8559084 GameState.DebugPrintEntityChoices() - id=101 Player=Local TaskList=9100 ChoiceType=GENERAL CountMin=1 CountMax=1");
        darkGiftParser.ParseLine("D 18:09:36.8559084 GameState.DebugPrintEntityChoices() -   Source=[entityName=Dark Gift Button id=9100 zone=PLAY zonePos=0 cardId=BG36_Button_DarkGift player=1]");
        darkGiftParser.ParseLine("D 18:09:36.8559084 GameState.DebugPrintEntityChoices() -   Entities[0]=[entityName=Gift A id=9101 zone=SETASIDE zonePos=0 cardId=BG36_SyntheticDarkGift_001 player=1]");
        darkGiftParser.ParseLine("D 18:09:36.8559084 GameState.DebugPrintEntityChoices() -   Entities[1]=[entityName=Gift B id=9102 zone=SETASIDE zonePos=0 cardId=BG36_SyntheticDarkGift_002 player=1]");
        if (darkGift == null || darkGiftActiveEvents != 1)
            return Fail("dark gift active event was not emitted exactly once");
        if (darkGift.ChoiceId != 101 || darkGift.TaskList != 9100
            || darkGift.SourceCardId != "BG36_Button_DarkGift"
            || darkGift.SourcePlayerId != 1)
            return Fail("dark gift batch identity was not preserved");
        if (darkGift.Candidates == null || darkGift.Candidates.Count != 2
            || darkGift.Candidates[0].CardId != "BG36_SyntheticDarkGift_001"
            || darkGift.Candidates[1].CardId != "BG36_SyntheticDarkGift_002")
            return Fail("dark gift candidates were not preserved");
        if (darkGiftChoiceListActiveDuringOffer)
            return Fail("dark gift offer leaked into IsChoiceListActive()");
        if (darkGiftDiscoverLeaks != 0 || darkGiftTrinketLeaks != 0
            || darkGiftTimewarpLeaks != 0 || darkGiftChoiceCompletedLeaks != 0)
            return Fail("dark gift offer leaked into ordinary choice events");

        darkGiftParser.ParseLine("D 18:10:00.0000000 GameState.SendChoices() - id=101 ChoiceType=GENERAL");
        darkGiftParser.ParseLine("D 18:10:00.0000000 GameState.SendChoices() -   m_chosenEntities[0]=[entityName=Gift B id=9102 zone=SETASIDE zonePos=0 cardId=BG36_SyntheticDarkGift_002 player=1]");
        if (darkGiftDone == null || darkGiftDone.ChoiceId != 101
            || darkGiftDone.SelectedCardId != "BG36_SyntheticDarkGift_002"
            || darkGiftDone.SelectedEntityId != 9102)
            return Fail("dark gift completion was not emitted with identity");
        if (darkGiftChoiceCompletedLeaks != 0)
            return Fail("dark gift completion leaked into ordinary ChoiceCompleted");
        if (darkGiftParser.IsChoiceListActive())
            return Fail("dark gift completion left ordinary choice context active");

        var unknownDarkGiftParser = new PowerLogParser();
        int unknownDarkGiftEvents = 0;
        int unknownDiscoverLeaks = 0;
        int unknownTrinketLeaks = 0;
        int unknownTimewarpLeaks = 0;
        int unknownChoiceCompletedLeaks = 0;
        bool unknownChoiceListActiveAfterOffer = true;
        unknownDarkGiftParser.DarkGiftChoiceActive += batch => unknownDarkGiftEvents++;
        unknownDarkGiftParser.DarkGiftCompleted += completion => unknownDarkGiftEvents++;
        unknownDarkGiftParser.DiscoverOffered += batch => unknownDiscoverLeaks++;
        unknownDarkGiftParser.TrinketChoiceActive += batch => unknownTrinketLeaks++;
        unknownDarkGiftParser.TimewarpPurchaseOffered += batch => unknownTimewarpLeaks++;
        unknownDarkGiftParser.ChoiceCompleted += completion => unknownChoiceCompletedLeaks++;
        unknownDarkGiftParser.ParseLine("D 18:20:00.0000000 GameState.DebugPrintEntityChoices() - id=202 Player=Local TaskList=9200 ChoiceType=GENERAL CountMin=1 CountMax=1");
        unknownDarkGiftParser.ParseLine("D 18:20:00.0000000 GameState.DebugPrintEntityChoices() -   Source=[entityName=Unknown Button id=9200 zone=PLAY zonePos=0 cardId=BG36_Button_Unknown player=1]");
        unknownDarkGiftParser.ParseLine("D 18:20:00.0000000 GameState.DebugPrintEntityChoices() -   Entities[0]=[entityName=Unknown A id=9201 zone=SETASIDE zonePos=0 cardId=BG36_SyntheticUnknown_001 player=1]");
        unknownDarkGiftParser.ParseLine("D 18:20:00.0000000 GameState.DebugPrintEntityChoices() -   Entities[1]=[entityName=Unknown B id=9202 zone=SETASIDE zonePos=0 cardId=BG36_SyntheticUnknown_002 player=1]");
        unknownChoiceListActiveAfterOffer = unknownDarkGiftParser.IsChoiceListActive();
        unknownDarkGiftParser.ParseLine("D 18:20:05.0000000 GameState.SendChoices() - id=202 ChoiceType=GENERAL");
        unknownDarkGiftParser.ParseLine("D 18:20:05.0000000 GameState.SendChoices() -   m_chosenEntities[0]=[entityName=Unknown B id=9202 zone=SETASIDE zonePos=0 cardId=BG36_SyntheticUnknown_002 player=1]");
        if (unknownDarkGiftEvents != 0)
            return Fail("unknown dark gift source was treated as dark gift");
        if (unknownDiscoverLeaks != 0 || unknownTrinketLeaks != 0
            || unknownTimewarpLeaks != 0 || unknownChoiceCompletedLeaks != 0)
            return Fail("unknown dark gift source leaked into ordinary choice events");
        if (unknownChoiceListActiveAfterOffer)
            return Fail("unknown dark gift source left ordinary choice context active");

        var watcherExit = RunWatcherSessionSwitchTest(null);
        if (watcherExit != 0) return watcherExit;
        var lateStartExit = RunWatcherLateStartTest(null);
        if (lateStartExit != 0) return lateStartExit;
        var lateStartLiveCompletionExit = RunWatcherLateStartLiveCompletionTest(null);
        if (lateStartLiveCompletionExit != 0) return lateStartLiveCompletionExit;
        var pressableExit = RunDarkGiftPressableTest();
        if (pressableExit != 0) return pressableExit;
        var darkGiftTipGateExit = RunDarkGiftTipGateTest();
        if (darkGiftTipGateExit != 0) return darkGiftTipGateExit;
        var ordinaryDiscoverLeakExit = RunOrdinaryDiscoverLeakFilterTest();
        if (ordinaryDiscoverLeakExit != 0) return ordinaryDiscoverLeakExit;
        var ordinaryDiscoverLeakRegressionExit = RunOrdinaryDiscoverLeakRegressionTest();
        if (ordinaryDiscoverLeakRegressionExit != 0) return ordinaryDiscoverLeakRegressionExit;
        var displayNameExit = RunDisplayNameResolverTest();
        if (displayNameExit != 0) return displayNameExit;

        Console.WriteLine("PASS trinket/discover batch metadata and typed completion");
        return 0;
    }

    private static int RunOrdinaryDiscoverLeakFilterTest()
    {
        var parser = new PowerLogParser();
        var offers = new List<PowerLogChoiceBatch>();
        int darkGiftEvents = 0;
        parser.DiscoverOffered += batch => offers.Add(batch);
        parser.DarkGiftChoiceActive += batch => darkGiftEvents++;

        parser.ParseLine("D 18:30:00.0000000 GameState.DebugPrintEntityChoices() - id=9001 Player=Local TaskList=9101 ChoiceType=GENERAL CountMin=1 CountMax=1");
        parser.ParseLine("D 18:30:00.0000000 GameState.DebugPrintEntityChoices() -   Source=[entityName=Discover Source id=9001 zone=PLAY zonePos=0 cardId=BG28_521 player=8]");
        parser.ParseLine("D 18:30:00.0000000 GameState.DebugPrintEntityChoices() -   Entities[0]=[entityName=Upgrade Button id=9101 zone=PLAY zonePos=0 cardId=TB_BaconShopTechUp02_Button player=8]");
        parser.ParseLine("D 18:30:00.0000000 GameState.DebugPrintEntityChoices() -   Entities[1]=[entityName=Hand Minion id=9102 zone=HAND zonePos=0 cardId=BG26_523_G player=8]");
        parser.ParseLine("D 18:30:00.0000000 GameState.DebugPrintEntityChoices() -   Entities[2]=[entityName=Board Minion id=9103 zone=PLAY zonePos=0 cardId=BG26_523 player=8]");
        if (offers.Count != 0)
            return Fail("ordinary discover fired from PLAY/HAND/button candidates");

        offers.Clear();
        parser.ParseLine("D 18:31:00.0000000 GameState.DebugPrintEntityChoices() - id=9002 Player=Local TaskList=9102 ChoiceType=GENERAL CountMin=1 CountMax=1");
        parser.ParseLine("D 18:31:00.0000000 GameState.DebugPrintEntityChoices() -   Source=[entityName=Discover Source id=9002 zone=PLAY zonePos=0 cardId=BG28_521 player=8]");
        parser.ParseLine("D 18:31:00.0000000 GameState.DebugPrintEntityChoices() -   Entities[0]=[entityName=Board Minion id=9201 zone=PLAY zonePos=0 cardId=BG26_523 player=8]");
        parser.ParseLine("D 18:31:00.0000000 GameState.DebugPrintEntityChoices() -   Entities[1]=[entityName=Valid A id=9202 zone=SETASIDE zonePos=0 cardId=BG26_525 player=8]");
        parser.ParseLine("D 18:31:00.0000000 GameState.DebugPrintEntityChoices() -   Entities[2]=[entityName=Valid B id=9203 zone=SETASIDE zonePos=0 cardId=BG26_524 player=8]");
        if (offers.Count != 1)
            return Fail("mixed ordinary discover did not emit exactly one filtered batch");
        if (offers[0].Candidates.Count != 2
            || offers[0].Candidates.Any(c => c.EntityId == 9201)
            || !offers[0].Candidates.Any(c => c.EntityId == 9202)
            || !offers[0].Candidates.Any(c => c.EntityId == 9203))
            return Fail("mixed ordinary discover retained non-SETASIDE candidates");

        offers.Clear();
        parser.ParseLine("D 18:32:00.0000000 GameState.DebugPrintEntityChoices() - id=9003 Player=Local TaskList=9103 ChoiceType=GENERAL CountMin=1 CountMax=1");
        parser.ParseLine("D 18:32:00.0000000 GameState.DebugPrintEntityChoices() -   Source=[entityName=Discover Source id=9003 zone=PLAY zonePos=0 cardId=BG28_521 player=8]");
        parser.ParseLine("D 18:32:00.0000000 GameState.DebugPrintPower() - TAG_CHANGE Entity=[entityName=Board Minion id=9301 zone=PLAY zonePos=0 cardId=BG26_523 player=8] tag=ZONE value=PLAY");
        parser.ParseLine("D 18:32:00.0000000 GameState.DebugPrintPower() - TAG_CHANGE Entity=[entityName=Hand Minion id=9302 zone=HAND zonePos=0 cardId=BG26_524 player=8] tag=ZONE value=HAND");
        if (offers.Count != 0)
            return Fail("TAG_CHANGE PLAY/HAND added ordinary discover candidates");

        offers.Clear();
        parser.ParseLine("D 18:33:00.0000000 GameState.DebugPrintEntityChoices() - id=9004 Player=Local TaskList=9104 ChoiceType=GENERAL CountMin=1 CountMax=1");
        parser.ParseLine("D 18:33:00.0000000 GameState.DebugPrintEntityChoices() -   Source=[entityName=Discover Source id=9004 zone=PLAY zonePos=0 cardId=BG28_521 player=8]");
        parser.ParseLine("D 18:33:00.0000000 GameState.DebugPrintEntityChoices() -   Entities[0]=[entityName=Transitional A id=9401 zone=PLAY zonePos=0 cardId=BG26_525 player=8]");
        parser.ParseLine("D 18:33:00.0000000 GameState.DebugPrintEntityChoices() -   Entities[1]=[entityName=Valid A id=9402 zone=SETASIDE zonePos=0 cardId=BG26_524 player=8]");
        parser.ParseLine("D 18:33:00.0000000 GameState.DebugPrintEntityChoices() -   Entities[2]=[entityName=Valid B id=9403 zone=SETASIDE zonePos=0 cardId=BG26_528 player=8]");
        if (offers.Count != 1)
            return Fail("PLAY-zone candidate was not removed from ordinary discover buffer: offers="
                + offers.Count);
        if (offers[0].Candidates.Any(c => c.EntityId == 9401)
            || !offers[0].Candidates.Any(c => c.EntityId == 9402)
            || !offers[0].Candidates.Any(c => c.EntityId == 9403))
            return Fail("ordinary discover buffer kept a PLAY-zone candidate");

        var showParser = new PowerLogParser();
        offers.Clear();
        showParser.DiscoverOffered += batch => offers.Add(batch);
        showParser.ParseLine("D 18:34:00.0000000 GameState.DebugPrintPower() - TAG_CHANGE Entity=[entityName=Board Minion id=9501 zone=PLAY zonePos=0 cardId=BG26_525 player=8] tag=ZONE value=PLAY");
        showParser.ParseLine("D 18:34:00.0000000 GameState.DebugPrintPower() - TAG_CHANGE Entity=[entityName=Hand Minion id=9502 zone=HAND zonePos=0 cardId=BG26_524 player=8] tag=ZONE value=HAND");
        showParser.ParseLine("D 18:34:00.0000000 GameState.DebugPrintEntityChoices() - id=9005 Player=Local TaskList=9105 ChoiceType=GENERAL CountMin=1 CountMax=1");
        showParser.ParseLine("D 18:34:00.0000000 GameState.DebugPrintEntityChoices() -   Source=[entityName=Discover Source id=9005 zone=PLAY zonePos=0 cardId=BG28_521 player=8]");
        showParser.ParseLine("D 18:34:00.0000000 GameState.DebugPrintPower() - SHOW_ENTITY - Updating [entityName=Board Minion id=9501 zone=PLAY zonePos=0 cardId=BG26_525 player=8] CardID=BG26_525");
        showParser.ParseLine("D 18:34:00.0000000 GameState.DebugPrintPower() - SHOW_ENTITY - Updating [entityName=Hand Minion id=9502 zone=HAND zonePos=0 cardId=BG26_524 player=8] CardID=BG26_524");
        if (offers.Count != 0)
            return Fail("SHOW_ENTITY PLAY/HAND added ordinary discover candidates");

        var choiceParser = new PowerLogParser();
        offers.Clear();
        choiceParser.DiscoverOffered += batch => offers.Add(batch);
        choiceParser.ParseLine("D 18:35:00.0000000 GameState.DebugPrintPower() - TAG_CHANGE Entity=[entityName=Upgrade Button id=9601 zone=PLAY zonePos=0 cardId=TB_BaconShopTechUp02_Button player=8] tag=ZONE value=PLAY");
        choiceParser.ParseLine("D 18:35:00.0000000 GameState.DebugPrintPower() - TAG_CHANGE Entity=[entityName=Hand Minion id=9602 zone=HAND zonePos=0 cardId=BG26_523_G player=8] tag=ZONE value=HAND");
        choiceParser.ParseLine("D 18:35:00.0000000 GameState.DebugPrintPower() - TAG_CHANGE Entity=[entityName=Board Minion id=9603 zone=PLAY zonePos=0 cardId=BG26_523 player=8] tag=ZONE value=PLAY");
        choiceParser.ParseLine("D 18:35:00.0000000 GameState.DebugPrintPower() - ChoiceList=CHOICE id=9006 ChoiceType=GENERAL");
        choiceParser.ParseLine("D 18:35:00.0000000 GameState.DebugPrintPower() - ChoiceList=ENTITY id=9601");
        choiceParser.ParseLine("D 18:35:00.0000000 GameState.DebugPrintPower() - ChoiceList=ENTITY id=9602");
        choiceParser.ParseLine("D 18:35:00.0000000 GameState.DebugPrintPower() - ChoiceList=ENTITY id=9603");
        if (offers.Count != 0)
            return Fail("ChoiceList=ENTITY PLAY/HAND/button candidates leaked into ordinary discover");

        if (darkGiftEvents != 0)
            return Fail("ordinary discover leak filter disturbed dark gift source handling");

        Console.WriteLine("PASS ordinary discover rejects non-SETASIDE and button/midgame candidates");
        return 0;
    }

    private static int RunDarkGiftPressableTest()
    {
        var parser = new PowerLogParser();
        int pressableEvents = 0;
        int darkGiftChoiceActiveEvents = 0;
        int darkGiftCompletedEvents = 0;
        int discoverLeaks = 0;
        int trinketLeaks = 0;
        int timewarpLeaks = 0;
        int choiceCompletedLeaks = 0;
        PowerLogDarkGiftPressable lastPressable = null;
        bool choiceListActiveDuringPressable = true;
        parser.DarkGiftPressableChanged += pressable =>
        {
            pressableEvents++;
            lastPressable = pressable;
            choiceListActiveDuringPressable = parser.IsChoiceListActive();
        };
        parser.DarkGiftChoiceActive += batch => darkGiftChoiceActiveEvents++;
        parser.DarkGiftCompleted += completion => darkGiftCompletedEvents++;
        parser.DiscoverOffered += batch => discoverLeaks++;
        parser.TrinketChoiceActive += batch => trinketLeaks++;
        parser.TimewarpPurchaseOffered += batch => timewarpLeaks++;
        parser.ChoiceCompleted += completion => choiceCompletedLeaks++;

        parser.ParseLine("D 18:09:36.8559084 GameState.DebugPrintPower() - TAG_CHANGE Entity=[entityName=Dark Gift Button id=9100 zone=PLAY zonePos=0 cardId=BG36_Button_DarkGift player=7] tag=BACON_DARK_GIFT_PRESSABLE_VFX value=1");
        if (pressableEvents != 1)
            return Fail("dark gift pressable event was not emitted exactly once");
        if (lastPressable == null || !lastPressable.Pressable
            || lastPressable.CardId != "BG36_Button_DarkGift"
            || lastPressable.EntityId != 9100)
            return Fail("dark gift pressable payload was not preserved");
        if (choiceListActiveDuringPressable || parser.IsChoiceListActive())
            return Fail("dark gift pressable leaked into IsChoiceListActive()");
        if (darkGiftChoiceActiveEvents != 0 || darkGiftCompletedEvents != 0
            || discoverLeaks != 0 || trinketLeaks != 0
            || timewarpLeaks != 0 || choiceCompletedLeaks != 0)
            return Fail("dark gift pressable leaked into choice events");

        parser.ParseLine("D 18:09:36.8560000 GameState.DebugPrintPower() - TAG_CHANGE Entity=[entityName=Dark Gift Button id=9100 zone=PLAY zonePos=0 cardId=BG36_Button_DarkGift player=7] tag=BACON_DARK_GIFT_PRESSABLE_VFX value=1");
        if (pressableEvents != 1)
            return Fail("duplicate dark gift pressable=true emitted a second event");

        parser.ParseLine("D 18:09:37.0000000 GameState.DebugPrintPower() - TAG_CHANGE Entity=[entityName=Dark Gift Button id=9100 zone=PLAY zonePos=0 cardId=BG36_Button_DarkGift player=7] tag=BACON_DARK_GIFT_PRESSABLE_VFX value=0");
        if (pressableEvents != 2 || lastPressable == null || lastPressable.Pressable)
            return Fail("dark gift pressable=false was not emitted");

        parser.ParseLine("D 18:09:37.1000000 GameState.DebugPrintPower() - TAG_CHANGE Entity=[entityName=Dark Gift Button id=9100 zone=PLAY zonePos=0 cardId=BG36_Button_DarkGift player=7] tag=BACON_DARK_GIFT_PRESSABLE_VFX value=0");
        if (pressableEvents != 2)
            return Fail("duplicate dark gift pressable=false emitted a second event");

        parser.ParseLine("D 18:09:38.0000000 GameState.DebugPrintPower() - TAG_CHANGE Entity=[entityName=Unknown Button id=9200 zone=PLAY zonePos=0 cardId=BG36_Button_Unknown player=7] tag=BACON_DARK_GIFT_PRESSABLE_VFX value=1");
        parser.ParseLine("D 18:09:38.1000000 GameState.DebugPrintPower() - TAG_CHANGE Entity=[entityName=Mid Game Gift id=9300 zone=PLAY zonePos=0 cardId=BG36_MidGameEffect_010 player=7] tag=BACON_DARK_GIFT_PRESSABLE_VFX value=1");
        parser.ParseLine("D 18:09:38.2000000 GameState.DebugPrintPower() - TAG_CHANGE Entity=[entityName=Dark Gift Button id=9100 zone=PLAY zonePos=0 cardId=BG36_Button_DarkGift player=7] tag=OTHER_TAG value=1");
        if (pressableEvents != 2)
            return Fail("unknown or non-pressable dark gift source emitted a pressable event");

        parser.ParseLine("D 18:09:39.0000000 GameState.DebugPrintEntityChoices() - id=101 Player=Local TaskList=9100 ChoiceType=GENERAL CountMin=1 CountMax=1");
        parser.ParseLine("D 18:09:39.0000000 GameState.DebugPrintEntityChoices() -   Source=[entityName=Dark Gift Button id=9100 zone=PLAY zonePos=0 cardId=BG36_Button_DarkGift player=7]");
        parser.ParseLine("D 18:09:39.0000000 GameState.DebugPrintEntityChoices() -   Entities[0]=[entityName=Gift A id=9101 zone=SETASIDE zonePos=0 cardId=BG36_SyntheticDarkGift_001 player=7]");
        parser.ParseLine("D 18:09:39.0000000 GameState.DebugPrintEntityChoices() -   Entities[1]=[entityName=Gift B id=9102 zone=SETASIDE zonePos=0 cardId=BG36_SyntheticDarkGift_002 player=7]");
        if (darkGiftChoiceActiveEvents != 1 || parser.IsChoiceListActive())
            return Fail("dark gift choice did not stay independent from pressable state");
        if (pressableEvents != 2)
            return Fail("dark gift choice changed the pressable event stream");

        parser.ParseLine("D 18:09:40.0000000 GameState.SendChoices() - id=101 ChoiceType=GENERAL");
        parser.ParseLine("D 18:09:40.0000000 GameState.SendChoices() -   m_chosenEntities[0]=[entityName=Gift B id=9102 zone=SETASIDE zonePos=0 cardId=BG36_SyntheticDarkGift_002 player=7]");
        if (darkGiftCompletedEvents != 1 || parser.IsChoiceListActive())
            return Fail("dark gift completion did not stay independent from pressable state");
        if (pressableEvents != 2)
            return Fail("dark gift completion changed the pressable event stream");

        Console.WriteLine("PASS dark gift pressable stays independent from choice state");
        return 0;
    }

    private static int RunDarkGiftTipGateTest()
    {
        var t0 = new DateTime(2026, 8, 7, 10, 0, 0, DateTimeKind.Local);
        var gate = new DarkGiftTipGate(TimeSpan.FromSeconds(6));
        if (gate.ShouldShow(t0))
            return Fail("dark gift tip gate opened before pressable");

        gate.OnPressableChanged(true, t0, false);
        if (!gate.ShouldShow(t0))
            return Fail("dark gift tip gate did not open on pressable");

        gate.OnPressableChanged(true, t0.AddSeconds(3), false);
        if (!gate.ShouldShow(t0.AddSeconds(3)))
            return Fail("dark gift tip gate closed while still inside its window");

        gate.OnPressableChanged(true, t0.AddSeconds(5), false);
        if (!gate.ShouldShow(t0.AddSeconds(5)))
            return Fail("dark gift tip gate closed before the original window ended");
        if (gate.ShouldShow(t0.AddSeconds(6)))
            return Fail("repeated pressable=true extended the dark gift tip window");
        if (gate.ShouldShow(t0.AddSeconds(7)))
            return Fail("dark gift tip gate stayed open after its window");

        var falseGate = new DarkGiftTipGate(TimeSpan.FromSeconds(6));
        falseGate.OnPressableChanged(true, t0, false);
        falseGate.OnPressableChanged(false, t0.AddSeconds(1), false);
        if (falseGate.ShouldShow(t0.AddSeconds(1)))
            return Fail("dark gift tip gate stayed open after pressable=false");

        var backfillGate = new DarkGiftTipGate(TimeSpan.FromSeconds(6));
        backfillGate.OnPressableChanged(true, t0, true);
        if (backfillGate.ShouldShow(t0))
            return Fail("backfilled dark gift pressable opened the tip gate");

        var choiceGate = new DarkGiftTipGate(TimeSpan.FromSeconds(6));
        choiceGate.OnPressableChanged(true, t0, false);
        choiceGate.OnDarkGiftChoiceActive(t0.AddSeconds(1));
        if (choiceGate.ShouldShow(t0.AddSeconds(1)))
            return Fail("dark gift choice active did not close the tip gate");

        var completedGate = new DarkGiftTipGate(TimeSpan.FromSeconds(6));
        completedGate.OnPressableChanged(true, t0, false);
        completedGate.OnDarkGiftCompleted(t0.AddSeconds(1));
        if (completedGate.ShouldShow(t0.AddSeconds(1)))
            return Fail("dark gift completion did not close the tip gate");

        completedGate.OnPressableChanged(true, t0.AddSeconds(2), false);
        completedGate.Reset();
        if (completedGate.ShouldShow(t0.AddSeconds(2)))
            return Fail("dark gift tip gate did not reset");

        var suppressedGate = new DarkGiftTipGate(TimeSpan.FromSeconds(6));
        suppressedGate.OnPressableChanged(true, t0, false);
        suppressedGate.OnDarkGiftChoiceActive(t0.AddSeconds(1));
        suppressedGate.OnPressableChanged(true, t0.AddSeconds(2), true);
        if (suppressedGate.ShouldShow(t0.AddSeconds(2)))
            return Fail("backfilled dark gift pressable reopened the tip after choice active");
        suppressedGate.OnPressableChanged(true, t0.AddSeconds(3), false);
        if (!suppressedGate.ShouldShow(t0.AddSeconds(3)))
            return Fail("live dark gift pressable did not reopen the tip after choice active");

        Console.WriteLine("PASS dark gift tip gate only shows a bounded pressable window");
        return 0;
    }

    private static int RunOrdinaryDiscoverLeakRegressionTest()
    {
        var emptyZoneParser = new PowerLogParser();
        var emptyZoneOffers = new List<PowerLogChoiceBatch>();
        emptyZoneParser.DiscoverOffered += batch => emptyZoneOffers.Add(batch);
        emptyZoneParser.ParseLine("D 18:40:00.0000000 GameState.DebugPrintEntityChoices() - id=9701 Player=Local TaskList=9710 ChoiceType=GENERAL CountMin=1 CountMax=1");
        emptyZoneParser.ParseLine("D 18:40:00.0000000 GameState.DebugPrintEntityChoices() -   Source=[entityName=Discover Source id=9701 zone=PLAY zonePos=0 cardId=BG28_521 player=8]");
        emptyZoneParser.ParseLine("D 18:40:00.0000000 GameState.DebugPrintPower() - FULL_ENTITY - Creating ID=9702 CardID=BG26_525");
        emptyZoneParser.ParseLine("D 18:40:00.0000000 GameState.DebugPrintPower() - FULL_ENTITY - Creating ID=9703 CardID=BG26_524");
        if (emptyZoneOffers.Count != 0)
            return Fail("empty-zone FULL_ENTITY leaked into ordinary discover");

        var darkGiftIdParser = new PowerLogParser();
        var darkGiftIdOffers = new List<PowerLogChoiceBatch>();
        darkGiftIdParser.DiscoverOffered += batch => darkGiftIdOffers.Add(batch);
        darkGiftIdParser.ParseLine("D 18:41:00.0000000 GameState.DebugPrintEntityChoices() - id=9801 Player=Local TaskList=9810 ChoiceType=GENERAL CountMin=1 CountMax=1");
        darkGiftIdParser.ParseLine("D 18:41:00.0000000 GameState.DebugPrintEntityChoices() -   Source=[entityName=Discover Source id=9801 zone=PLAY zonePos=0 cardId=BG28_521 player=8]");
        darkGiftIdParser.ParseLine("D 18:41:00.0000000 GameState.DebugPrintEntityChoices() -   Entities[0]=[entityName=Dark Gift Fake id=9802 zone=SETASIDE zonePos=0 cardId=BG36_SyntheticDarkGift_001 player=8]");
        darkGiftIdParser.ParseLine("D 18:41:00.0000000 GameState.DebugPrintEntityChoices() -   Entities[1]=[entityName=Normal Card id=9803 zone=SETASIDE zonePos=0 cardId=BG26_524 player=8]");
        if (darkGiftIdOffers.Count != 0)
            return Fail("DarkGift CardId leaked into ordinary discover");

        var dedupeParser = new PowerLogParser();
        var dedupeOffers = new List<PowerLogChoiceBatch>();
        dedupeParser.DiscoverOffered += batch => dedupeOffers.Add(batch);
        dedupeParser.ParseLine("D 18:42:00.0000000 GameState.DebugPrintEntityChoices() - id=9901 Player=Local TaskList=9910 ChoiceType=GENERAL CountMin=1 CountMax=1");
        dedupeParser.ParseLine("D 18:42:00.0000000 GameState.DebugPrintEntityChoices() -   Source=[entityName=Discover Source id=9901 zone=PLAY zonePos=0 cardId=BG28_521 player=8]");
        dedupeParser.ParseLine("D 18:42:00.0000000 GameState.DebugPrintEntityChoices() -   Entities[0]=[entityName=First id=9902 zone=SETASIDE zonePos=0 cardId=BG26_525 player=8]");
        dedupeParser.ParseLine("D 18:42:00.0000000 GameState.DebugPrintEntityChoices() -   Entities[1]=[entityName=Duplicate id=9903 zone=SETASIDE zonePos=0 cardId=BG26_525 player=8]");
        dedupeParser.ParseLine("D 18:42:00.0000000 GameState.DebugPrintEntityChoices() -   Entities[2]=[entityName=Third id=9904 zone=SETASIDE zonePos=0 cardId=BG26_524 player=8]");
        if (dedupeOffers.Count != 1 || dedupeOffers[0].Candidates.Count != 2
            || dedupeOffers[0].Candidates.Any(c => c.CardId == "BG26_525" && c.EntityId == 9903)
            || !dedupeOffers[0].Candidates.Any(c => c.CardId == "BG26_525" && c.EntityId == 9902)
            || !dedupeOffers[0].Candidates.Any(c => c.CardId == "BG26_524"))
            return Fail("ordinary discover did not deduplicate by CardId");

        var sendParser = new PowerLogParser();
        var sendOffers = new List<PowerLogChoiceBatch>();
        sendParser.DiscoverOffered += batch => sendOffers.Add(batch);
        sendParser.ParseLine("D 18:43:00.0000000 GameState.DebugPrintEntityChoices() - id=9951 Player=Local TaskList=9950 ChoiceType=GENERAL CountMin=1 CountMax=1");
        sendParser.ParseLine("D 18:43:00.0000000 GameState.DebugPrintEntityChoices() -   Source=[entityName=Discover Source id=9951 zone=PLAY zonePos=0 cardId=BG28_521 player=8]");
        sendParser.ParseLine("D 18:43:00.0000000 GameState.DebugPrintEntityChoices() -   Entities[0]=[entityName=A id=9952 zone=SETASIDE zonePos=0 cardId=BG26_525 player=8]");
        sendParser.ParseLine("D 18:43:00.0000000 GameState.DebugPrintEntityChoices() -   Entities[1]=[entityName=B id=9953 zone=SETASIDE zonePos=0 cardId=BG26_524 player=8]");
        if (sendOffers.Count != 1)
            return Fail("ordinary discover send-context fixture did not emit a batch");
        if (sendParser.IsChoiceListActive())
            return Fail("ordinary discover became active without a fresh choice context");
        sendParser.ParseLine("D 18:43:05.0000000 GameState.SendChoices() - id=9951 ChoiceType=GENERAL");
        if (sendParser.IsChoiceListActive())
            return Fail("SendChoices left ordinary choice context active");

        var chosenParser = new PowerLogParser();
        chosenParser.ParseLine("D 18:44:00.0000000 GameState.DebugPrintPower() - ChoiceList=CHOICE id=9961 ChoiceType=GENERAL");
        chosenParser.ParseLine("D 18:44:00.0000000 GameState.DebugPrintPower() - ChoiceList=ENTITY id=9962");
        chosenParser.ParseLine("D 18:44:00.0000000 GameState.DebugPrintPower() - ChoiceList=CHOSEN id=9961");
        if (chosenParser.IsChoiceListActive())
            return Fail("ChoiceList=CHOSEN left ordinary choice context active");

        Console.WriteLine("PASS ordinary discover closes stale choice context and dedupes by CardId");
        return 0;
    }

    private static int RunDisplayNameResolverTest()
    {
        string resolved = DisplayNameResolver.Resolve("BG36_SyntheticUnknown_001", "", id => null);
        if (!string.Equals(resolved, "unknown", StringComparison.Ordinal))
            return Fail("unknown CardId displayed as raw ID");

        resolved = DisplayNameResolver.Resolve(
            "BG36_SyntheticUnknown_002", "BG36_SyntheticUnknown_002", id => "未知测试卡");
        if (!string.Equals(resolved, "未知测试卡", StringComparison.Ordinal))
            return Fail("known service name was not preferred over raw CardId");

        resolved = DisplayNameResolver.Resolve("BG26_525", "Board Minion", id => null);
        if (!string.Equals(resolved, "Board Minion", StringComparison.Ordinal))
            return Fail("known log name was not preserved");

        Console.WriteLine("PASS unknown CardIds display as unknown instead of raw IDs");
        return 0;
    }

    private static int RunWatcherLateStartTest(string explicitRoot)
    {
        var root = string.IsNullOrWhiteSpace(explicitRoot)
            ? Path.Combine(Path.GetTempPath(), "bobcoach-watcher-late-start-" + Guid.NewGuid().ToString("N"))
            : Path.GetFullPath(explicitRoot);
        try
        {
            var appData = Path.Combine(root, "appdata");
            var hearthstoneDir = Path.Combine(root, "hearthstone");
            var logsDir = Path.Combine(hearthstoneDir, "Logs");
            var sessionDir = Path.Combine(logsDir, "Hearthstone_late");
            Directory.CreateDirectory(sessionDir);

            var hdtConfigPath = Path.Combine(appData, "HearthstoneDeckTracker", "config.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(hdtConfigPath));
            File.WriteAllText(hdtConfigPath,
                "<Config><HearthstoneDirectory>" + hearthstoneDir
                + "</HearthstoneDirectory></Config>");

            var logConfigPath = Path.Combine(hearthstoneDir, "log.config");
            var plan = LogConfigEnsurer.InspectAtPath(logConfigPath);
            if (plan.Status != LogConfigStatus.Missing)
                return Fail("late-start watcher log.config was not reported missing");
            if (LogConfigEnsurer.Apply(plan) != LogConfigStatus.Created)
                return Fail("late-start watcher could not create isolated log.config");

            Environment.SetEnvironmentVariable("APPDATA", appData);
            Environment.SetEnvironmentVariable("LOCALAPPDATA", Path.Combine(root, "local-appdata"));
            Environment.SetEnvironmentVariable("BOB_COACH_DATA_ROOT", Path.Combine(root, "bob-coach-data"));

            var logPath = Path.Combine(sessionDir, "Power_old.log");
            File.WriteAllText(logPath,
                "D 18:09:00.0000000 GameState.DebugPrintGame() - BuildNumber=246003\r\n"
                + "D 18:09:01.0000000 GameState.DebugPrintPower() - TAG_CHANGE Entity=GameEntity tag=TURN value=6\r\n"
                + "D 18:09:01.5000000 GameState.DebugPrintPower() - TAG_CHANGE Entity=[entityName=Dark Gift Button id=10486 zone=PLAY zonePos=0 cardId=BG36_Button_DarkGift player=1] tag=BACON_DARK_GIFT_PRESSABLE_VFX value=1\r\n"
                + "D 18:09:01.6000000 GameState.DebugPrintPower() - TAG_CHANGE Entity=[entityName=Dark Gift Button id=10486 zone=PLAY zonePos=0 cardId=BG36_Button_DarkGift player=1] tag=BACON_DARK_GIFT_PRESSABLE_VFX value=0\r\n"
                + "D 18:09:01.7000000 GameState.DebugPrintPower() - TAG_CHANGE Entity=[entityName=Dark Gift Button id=10486 zone=PLAY zonePos=0 cardId=BG36_Button_DarkGift player=1] tag=BACON_DARK_GIFT_PRESSABLE_VFX value=1\r\n"
                + "D 18:09:02.0000000 GameState.DebugPrintEntityChoices() - id=10487 Player=Local TaskList=900 ChoiceType=GENERAL CountMin=1 CountMax=1\r\n"
                + "D 18:09:02.0000000 GameState.DebugPrintEntityChoices() -   Source=[entityName=Dark Gift id=10487 zone=PLAY zonePos=0 cardId=BG36_MidGameEffect_010 player=1]\r\n"
                + "D 18:09:02.0000000 GameState.DebugPrintEntityChoices() -   Entities[0]=[entityName=Gift A id=10488 zone=SETASIDE zonePos=0 cardId=BG31_809 player=1]\r\n"
                + "D 18:09:02.0000000 GameState.DebugPrintEntityChoices() -   Entities[1]=[entityName=Gift B id=10489 zone=SETASIDE zonePos=0 cardId=BG35_602 player=1]\r\n"
                + "D 18:09:02.0000000 GameState.DebugPrintEntityChoices() -   Entities[2]=[entityName=Gift C id=10490 zone=SETASIDE zonePos=0 cardId=BG32_111 player=1]\r\n"
                + "D 18:09:03.0000000 GameState.SendChoices() - id=10487 ChoiceType=GENERAL\r\n"
                + "D 18:09:03.0000000 GameState.SendChoices() -   m_chosenEntities[0]=[entityName=Gift A id=10488 zone=SETASIDE zonePos=0 cardId=BG31_809 player=1]\r\n");

            using (var watcher = new PowerLogWatcher())
            {
                int darkGiftActiveEvents = 0;
                int darkGiftCompletedEvents = 0;
                PowerLogChoiceBatch lastDarkGift = null;
                PowerLogChoiceCompletion lastDarkGiftCompletion = null;
                int darkGiftPressableEvents = 0;
                PowerLogDarkGiftPressable lastDarkGiftPressable = null;
                int discoverLeaks = 0;
                int trinketLeaks = 0;
                int timewarpLeaks = 0;
                int choiceCompletedLeaks = 0;
                watcher.DarkGiftChoiceActive += batch =>
                {
                    darkGiftActiveEvents++;
                    lastDarkGift = batch;
                };
                watcher.DarkGiftCompleted += completion =>
                {
                    darkGiftCompletedEvents++;
                    lastDarkGiftCompletion = completion;
                };
                watcher.DarkGiftPressableChanged += pressable =>
                {
                    darkGiftPressableEvents++;
                    lastDarkGiftPressable = pressable;
                };
                watcher.DiscoverOffered += batch =>
                {
                    if (batch != null && batch.Candidates != null && batch.Candidates.Count > 0)
                        discoverLeaks++;
                };
                watcher.TrinketChoiceActive += batch =>
                {
                    if (batch != null && batch.Candidates != null && batch.Candidates.Count > 0)
                        trinketLeaks++;
                };
                watcher.TimewarpPurchaseOffered += batch =>
                {
                    if (batch != null && batch.Candidates != null && batch.Candidates.Count > 0)
                        timewarpLeaks++;
                };
                watcher.ChoiceCompleted += completion => choiceCompletedLeaks++;

                watcher.StartWatchingAtConfigPath(logConfigPath);
                if (!WaitUntil(() => watcher.IsWatching, 3000))
                    return Fail("late-start watcher did not start against the prewritten log");

                if (darkGiftActiveEvents != 1)
                    return Fail("late-start watcher did not backfill one dark gift active event");
                if (darkGiftCompletedEvents != 1)
                    return Fail("late-start watcher did not backfill one dark gift completion event");
                if (darkGiftPressableEvents != 1 || lastDarkGiftPressable == null
                    || !lastDarkGiftPressable.Pressable
                    || !lastDarkGiftPressable.Backfilled
                    || lastDarkGiftPressable.CardId != "BG36_Button_DarkGift")
                    return Fail("late-start watcher did not backfill the latest dark gift pressable state");
                if (lastDarkGift == null || lastDarkGift.SourceCardId != "BG36_MidGameEffect_010"
                    || lastDarkGift.ChoiceId != 10487 || lastDarkGift.Candidates.Count < 2)
                    return Fail("late-start dark gift batch was malformed: source="
                        + (lastDarkGift?.SourceCardId ?? "null")
                        + " choice=" + (lastDarkGift?.ChoiceId ?? -1)
                        + " candidates=" + (lastDarkGift?.Candidates?.Count ?? -1));
                if (lastDarkGiftCompletion == null
                    || lastDarkGiftCompletion.ChoiceId != 10487
                    || lastDarkGiftCompletion.SelectedCardId != "BG31_809")
                    return Fail("late-start dark gift completion identity was not preserved");
                if (watcher.IsChoiceListActive)
                    return Fail("late-start dark gift leaked into ordinary choice-list state");
                if (discoverLeaks != 0 || trinketLeaks != 0
                    || timewarpLeaks != 0 || choiceCompletedLeaks != 0)
                    return Fail("late-start dark gift leaked into ordinary choice events");

                watcher.StopWatching();
            }

            Console.WriteLine("PASS watcher backfills recent dark gift choices after late start");
            return 0;
        }
        finally
        {
            try
            {
                if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
                    Directory.Delete(root, true);
            }
            catch { }
        }
    }

    private static int RunWatcherLateStartLiveCompletionTest(string explicitRoot)
    {
        var root = string.IsNullOrWhiteSpace(explicitRoot)
            ? Path.Combine(Path.GetTempPath(), "bobcoach-watcher-late-start-live-" + Guid.NewGuid().ToString("N"))
            : Path.GetFullPath(explicitRoot);
        try
        {
            var appData = Path.Combine(root, "appdata");
            var hearthstoneDir = Path.Combine(root, "hearthstone");
            var logsDir = Path.Combine(hearthstoneDir, "Logs");
            var sessionDir = Path.Combine(logsDir, "Hearthstone_late");
            Directory.CreateDirectory(sessionDir);

            var hdtConfigPath = Path.Combine(appData, "HearthstoneDeckTracker", "config.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(hdtConfigPath));
            File.WriteAllText(hdtConfigPath,
                "<Config><HearthstoneDirectory>" + hearthstoneDir
                + "</HearthstoneDirectory></Config>");

            var logConfigPath = Path.Combine(hearthstoneDir, "log.config");
            var plan = LogConfigEnsurer.InspectAtPath(logConfigPath);
            if (plan.Status != LogConfigStatus.Missing)
                return Fail("late-start-live log.config was not reported missing");
            if (LogConfigEnsurer.Apply(plan) != LogConfigStatus.Created)
                return Fail("late-start-live watcher could not create isolated log.config");

            Environment.SetEnvironmentVariable("APPDATA", appData);
            Environment.SetEnvironmentVariable("LOCALAPPDATA", Path.Combine(root, "local-appdata"));
            Environment.SetEnvironmentVariable("BOB_COACH_DATA_ROOT", Path.Combine(root, "bob-coach-data"));

            var logPath = Path.Combine(sessionDir, "Power_old.log");
            File.WriteAllText(logPath,
                "D 18:09:00.0000000 GameState.DebugPrintGame() - BuildNumber=246003\r\n"
                + "D 18:09:01.0000000 GameState.DebugPrintPower() - TAG_CHANGE Entity=GameEntity tag=TURN value=6\r\n"
                + "D 18:09:01.5000000 GameState.DebugPrintPower() - TAG_CHANGE Entity=[entityName=Dark Gift Button id=10586 zone=PLAY zonePos=0 cardId=BG36_Button_DarkGift player=1] tag=BACON_DARK_GIFT_PRESSABLE_VFX value=1\r\n"
                + "D 18:09:02.0000000 GameState.DebugPrintEntityChoices() - id=10587 Player=Local TaskList=901 ChoiceType=GENERAL CountMin=1 CountMax=1\r\n"
                + "D 18:09:02.0000000 GameState.DebugPrintEntityChoices() -   Source=[entityName=Dark Gift Button id=10587 zone=PLAY zonePos=0 cardId=BG36_Button_DarkGift player=1]\r\n"
                + "D 18:09:02.0000000 GameState.DebugPrintEntityChoices() -   Entities[0]=[entityName=Gift A id=10588 zone=SETASIDE zonePos=0 cardId=BG31_809 player=1]\r\n"
                + "D 18:09:02.0000000 GameState.DebugPrintEntityChoices() -   Entities[1]=[entityName=Gift B id=10589 zone=SETASIDE zonePos=0 cardId=BG35_602 player=1]\r\n");

            using (var watcher = new PowerLogWatcher())
            {
                int darkGiftActiveEvents = 0;
                int darkGiftCompletedEvents = 0;
                PowerLogChoiceBatch lastDarkGift = null;
                PowerLogChoiceCompletion lastDarkGiftCompletion = null;
                int discoverLeaks = 0;
                int trinketLeaks = 0;
                int timewarpLeaks = 0;
                int choiceCompletedLeaks = 0;
                watcher.DarkGiftChoiceActive += batch =>
                {
                    darkGiftActiveEvents++;
                    lastDarkGift = batch;
                };
                watcher.DarkGiftCompleted += completion =>
                {
                    darkGiftCompletedEvents++;
                    lastDarkGiftCompletion = completion;
                };
                watcher.DiscoverOffered += batch =>
                {
                    if (batch != null && batch.Candidates != null && batch.Candidates.Count > 0)
                        discoverLeaks++;
                };
                watcher.TrinketChoiceActive += batch =>
                {
                    if (batch != null && batch.Candidates != null && batch.Candidates.Count > 0)
                        trinketLeaks++;
                };
                watcher.TimewarpPurchaseOffered += batch =>
                {
                    if (batch != null && batch.Candidates != null && batch.Candidates.Count > 0)
                        timewarpLeaks++;
                };
                watcher.ChoiceCompleted += completion => choiceCompletedLeaks++;

                watcher.StartWatchingAtConfigPath(logConfigPath);
                if (!WaitUntil(() => watcher.IsWatching, 3000))
                    return Fail("late-start-live watcher did not start against the prewritten log");

                if (darkGiftActiveEvents != 1)
                    return Fail("late-start-live watcher did not backfill one dark gift active event");
                if (darkGiftCompletedEvents != 0)
                    return Fail("late-start-live watcher emitted completion before SendChoices was appended");
                if (lastDarkGift == null || lastDarkGift.SourceCardId != "BG36_Button_DarkGift"
                    || lastDarkGift.ChoiceId != 10587 || lastDarkGift.Candidates.Count < 2)
                    return Fail("late-start-live dark gift batch was malformed: source="
                        + (lastDarkGift?.SourceCardId ?? "null")
                        + " choice=" + (lastDarkGift?.ChoiceId ?? -1)
                        + " candidates=" + (lastDarkGift?.Candidates?.Count ?? -1));
                if (watcher.IsChoiceListActive)
                    return Fail("late-start-live dark gift leaked into ordinary choice-list state before completion");

                File.AppendAllText(logPath,
                    "D 18:09:03.0000000 GameState.SendChoices() - id=10587 ChoiceType=GENERAL\r\n"
                    + "D 18:09:03.0000000 GameState.SendChoices() -   m_chosenEntities[0]=[entityName=Gift A id=10588 zone=SETASIDE zonePos=0 cardId=BG31_809 player=1]\r\n");

                if (!WaitUntil(() => darkGiftCompletedEvents == 1, 5000))
                    return Fail("late-start-live completion was not emitted as DarkGiftCompleted");
                if (lastDarkGiftCompletion == null
                    || lastDarkGiftCompletion.ChoiceId != 10587
                    || lastDarkGiftCompletion.SelectedCardId != "BG31_809")
                    return Fail("late-start-live dark gift completion identity was not preserved");
                if (watcher.IsChoiceListActive)
                    return Fail("late-start-live dark gift leaked into ordinary choice-list state after completion");
                if (discoverLeaks != 0 || trinketLeaks != 0
                    || timewarpLeaks != 0 || choiceCompletedLeaks != 0)
                    return Fail("late-start-live dark gift leaked into ordinary choice events");

                watcher.StopWatching();
            }

            Console.WriteLine("PASS late-start dark gift active context completes without ordinary leaks");
            return 0;
        }
        finally
        {
            try
            {
                if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
                    Directory.Delete(root, true);
            }
            catch { }
        }
    }

    private static int RunWatcherSessionSwitchTest(string explicitRoot)
    {
        var root = string.IsNullOrWhiteSpace(explicitRoot)
            ? Path.Combine(Path.GetTempPath(), "bobcoach-watcher-session-" + Guid.NewGuid().ToString("N"))
            : Path.GetFullPath(explicitRoot);
        try
        {
            var appData = Path.Combine(root, "appdata");
            var hearthstoneDir = Path.Combine(root, "hearthstone");
            var logsDir = Path.Combine(hearthstoneDir, "Logs");
            var oldSessionDir = Path.Combine(logsDir, "Hearthstone_old");
            Directory.CreateDirectory(oldSessionDir);

            var hdtConfigPath = Path.Combine(appData, "HearthstoneDeckTracker", "config.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(hdtConfigPath));
            File.WriteAllText(hdtConfigPath,
                "<Config><HearthstoneDirectory>" + hearthstoneDir
                + "</HearthstoneDirectory></Config>");

            var logConfigPath = Path.Combine(hearthstoneDir, "log.config");
            var plan = LogConfigEnsurer.InspectAtPath(logConfigPath);
            if (plan.Status != LogConfigStatus.Missing)
                return Fail("watcher test log.config was not reported missing");
            if (LogConfigEnsurer.Apply(plan) != LogConfigStatus.Created)
                return Fail("watcher test could not create isolated log.config");

            Environment.SetEnvironmentVariable("APPDATA", appData);
            Environment.SetEnvironmentVariable("LOCALAPPDATA", Path.Combine(root, "local-appdata"));
            Environment.SetEnvironmentVariable("BOB_COACH_DATA_ROOT", Path.Combine(root, "bob-coach-data"));

            var oldLogPath = Path.Combine(oldSessionDir, "Power_old.log");
            File.WriteAllText(oldLogPath, "");

            using (var watcher = new PowerLogWatcher())
            {
                int darkGiftEvents = 0;
                PowerLogChoiceBatch lastDarkGift = null;
                int discoverLeaks = 0;
                int trinketLeaks = 0;
                int timewarpLeaks = 0;
                int choiceCompletedLeaks = 0;
                int buildEvents = 0;
                watcher.DarkGiftChoiceActive += batch =>
                {
                    darkGiftEvents++;
                    lastDarkGift = batch;
                };
                watcher.DiscoverOffered += batch =>
                {
                    if (batch != null && batch.Candidates != null && batch.Candidates.Count > 0)
                        discoverLeaks++;
                };
                watcher.TrinketChoiceActive += batch =>
                {
                    if (batch != null && batch.Candidates != null && batch.Candidates.Count > 0)
                        trinketLeaks++;
                };
                watcher.TimewarpPurchaseOffered += batch =>
                {
                    if (batch != null && batch.Candidates != null && batch.Candidates.Count > 0)
                        timewarpLeaks++;
                };
                watcher.ChoiceCompleted += completion => choiceCompletedLeaks++;
                watcher.BuildNumberChanged += build => buildEvents++;

                watcher.StartWatchingAtConfigPath(logConfigPath);
                if (!WaitUntil(() => watcher.IsWatching, 3000))
                    return Fail("watcher did not start against the isolated old session");

                File.AppendAllText(oldLogPath,
                    "D 18:09:00.0000000 GameState.DebugPrintPower() - TAG_CHANGE Entity=GameEntity tag=TURN value=10\r\n");
                File.AppendAllText(oldLogPath,
                    "D 18:09:01.0000000 GameState.DebugPrintGame() - BuildNumber=100\r\n");
                File.AppendAllText(oldLogPath,
                    "D 18:09:02.0000000 GameState.DebugPrintEntityChoices() - id=900 Player=Local TaskList=100 ChoiceType=GENERAL CountMin=1 CountMax=1\r\n"
                    + "D 18:09:02.0000000 GameState.DebugPrintEntityChoices() -   Source=[entityName=Dark Gift id=900 zone=PLAY zonePos=0 cardId=BG36_MidGameEffect_010 player=1]\r\n"
                    + "D 18:09:02.0000000 GameState.DebugPrintEntityChoices() -   Entities[0]=[entityName=Gift A id=9001 zone=SETASIDE zonePos=0 cardId=BG34_865_G player=1]\r\n"
                    + "D 18:09:02.0000000 GameState.DebugPrintEntityChoices() -   Entities[1]=[entityName=Gift B id=9002 zone=SETASIDE zonePos=0 cardId=BG32_111 player=1]\r\n"
                    + "D 18:09:02.0000000 GameState.DebugPrintEntityChoices() -   Entities[2]=[entityName=Gift C id=9003 zone=SETASIDE zonePos=0 cardId=BG32_820 player=1]\r\n");

                if (!WaitUntil(() => darkGiftEvents >= 1, 5000))
                    return Fail("old session dark gift was not captured by the watcher");
                if (lastDarkGift == null || lastDarkGift.SourceCardId != "BG36_MidGameEffect_010"
                    || lastDarkGift.Candidates.Count < 2)
                    return Fail("old session dark gift batch was malformed");
                if (watcher.Parser.CurrentTurn != 10)
                    return Fail("old session turn was not captured");
                if (watcher.Parser.CurrentBuildNumber != 100)
                    return Fail("old session build was not captured");
                if (watcher.IsChoiceListActive)
                    return Fail("dark gift offer leaked into ordinary choice-list state");
                if (discoverLeaks != 0 || trinketLeaks != 0
                    || timewarpLeaks != 0 || choiceCompletedLeaks != 0)
                    return Fail("old session dark gift leaked into ordinary choice events");

                var newSessionDir = Path.Combine(logsDir, "Hearthstone_new");
                Directory.CreateDirectory(newSessionDir);
                var newLogPath = Path.Combine(newSessionDir, "Power_old.log");
                File.WriteAllText(newLogPath,
                    "D 18:20:00.0000000 GameState.DebugPrintGame() - BuildNumber=200\r\n"
                    + "D 18:20:01.0000000 GameState.DebugPrintPower() - TAG_CHANGE Entity=GameEntity tag=TURN value=3\r\n"
                    + "D 18:20:02.0000000 GameState.DebugPrintEntityChoices() - id=901 Player=Local TaskList=101 ChoiceType=GENERAL CountMin=1 CountMax=1\r\n"
                    + "D 18:20:02.0000000 GameState.DebugPrintEntityChoices() -   Source=[entityName=Dark Gift Button id=901 zone=PLAY zonePos=0 cardId=BG36_Button_DarkGift player=1]\r\n"
                    + "D 18:20:02.0000000 GameState.DebugPrintEntityChoices() -   Entities[0]=[entityName=Gift X id=9101 zone=SETASIDE zonePos=0 cardId=BG34_865_G player=1]\r\n"
                    + "D 18:20:02.0000000 GameState.DebugPrintEntityChoices() -   Entities[1]=[entityName=Gift Y id=9102 zone=SETASIDE zonePos=0 cardId=BG32_111 player=1]\r\n");

                if (!WaitUntil(() => darkGiftEvents >= 2, 5000))
                    return Fail("watcher did not follow the new Hearthstone Power session");
                if (lastDarkGift == null || lastDarkGift.SourceCardId != "BG36_Button_DarkGift"
                    || lastDarkGift.ChoiceId != 901)
                    return Fail("new session dark gift batch identity was not preserved");
                if (watcher.Parser.CurrentBuildNumber != 200)
                    return Fail("new session build number was not applied");
                if (watcher.Parser.CurrentTurn != 3)
                    return Fail("new session did not reset turn state");
                if (watcher.IsChoiceListActive)
                    return Fail("new session dark gift leaked into ordinary choice-list state");
                if (discoverLeaks != 0 || trinketLeaks != 0
                    || timewarpLeaks != 0 || choiceCompletedLeaks != 0)
                    return Fail("new session dark gift leaked into ordinary choice events");

                watcher.StopWatching();
            }

            Console.WriteLine("PASS watcher follows latest Hearthstone Power session");
            return 0;
        }
        finally
        {
            try
            {
                if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
                    Directory.Delete(root, true);
            }
            catch { }
        }
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

namespace BobCoach.Engine
{
    // PowerLogWatcher.ExportReplay only needs type identity in this isolated harness.
    public sealed class TurnSnapshot { }

    internal static class FastJsonWriter
    {
        public static void Write(string path, object value) { }
    }
}
