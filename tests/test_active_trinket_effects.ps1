$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$candidates = @()
if ($env:BOBCOACH_CSC) { $candidates += $env:BOBCOACH_CSC }
$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path $vswhere) {
    $found = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\Roslyn\csc.exe"
    if ($found) { $candidates += $found }
}
$csc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (!$csc) { throw "Roslyn csc.exe not found; install Visual Studio Build Tools or set BOBCOACH_CSC" }

$hdtDir = $env:BOBCOACH_HDT_DIR
if (!$hdtDir) {
    $gameDir = "D:\software\game"
    $hdtRoots = @([System.IO.Directory]::GetDirectories($gameDir, "HDT*") | Where-Object {
        (Test-Path (Join-Path $_ "HDT\HearthDb.dll")) -or (Test-Path (Join-Path $_ "HearthDb.dll"))
    })
    if ($hdtRoots.Count -gt 0) {
        $nested = Join-Path $hdtRoots[0] "HDT"
        $hdtDir = if (Test-Path (Join-Path $nested "HearthDb.dll")) { $nested } else { $hdtRoots[0] }
    }
}
if (!$hdtDir -or !(Test-Path (Join-Path $hdtDir "HearthDb.dll"))) {
    throw "HearthDb.dll not found; set BOBCOACH_HDT_DIR"
}
$netstandard = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\netstandard.dll"

$output = Join-Path $env:TEMP "bobcoach_active_trinket_effects.exe"
$sources = @(
    (Join-Path $root "src\BobCoach\Core\GameState.cs"),
    (Join-Path $root "src\BobCoach\Core\ActiveTrinketContext.cs"),
    (Join-Path $root "src\BobCoach\Core\TrinketEffectRegistry.cs"),
    (Join-Path $root "src\BobCoach\Core\TrinketEffectResolver.cs"),
    (Join-Path $root "src\BobCoach\Core\EffectiveGameRules.cs"),
    (Join-Path $root "src\BobCoach\Core\EffectiveCardPoolRules.cs"),
    (Join-Path $root "src\BobCoach\Core\FirstPurchaseExtraCopyRule.cs"),
    (Join-Path $root "src\BobCoach\Core\UpgradePrizeRule.cs"),
    (Join-Path $root "src\BobCoach\Core\PortalInBottleRule.cs"),
    (Join-Path $root "src\BobCoach\Core\SharedYoggWheelRule.cs"),
    (Join-Path $root "src\BobCoach\Core\SharedCardVoteRule.cs"),
    (Join-Path $root "src\BobCoach\Core\BuddyPoolRule.cs"),
    (Join-Path $root "src\BobCoach\Core\AllHeroesOverrideRule.cs"),
    (Join-Path $root "src\BobCoach\Core\SecondHeroPowerDiscoverRule.cs"),
    (Join-Path $root "src\BobCoach\Core\TeammateGoldTransferRule.cs"),
    (Join-Path $root "src\BobCoach\Core\StartResourceExpectation.cs"),
    (Join-Path $root "src\BobCoach\Core\ScheduledGrant.cs"),
    (Join-Path $root "src\BobCoach\Core\SecondaryHeroPowerRule.cs"),
    (Join-Path $root "src\BobCoach\Core\TimewarpVisit.cs"),
    (Join-Path $root "src\BobCoach\Core\TimewarpOfferRule.cs"),
    (Join-Path $root "src\BobCoach\Core\TimewarpPoolMergeRule.cs"),
    (Join-Path $root "src\BobCoach\Core\HeroPowerState.cs"),
    (Join-Path $root "src\BobCoach\Core\HeroIdentityExpectation.cs"),
    (Join-Path $root "src\BobCoach\Core\SecondHeroPowerChoiceExpectation.cs"),
    (Join-Path $root "src\BobCoach\Core\SecondHeroPowerChoiceBatchObservation.cs"),
    (Join-Path $root "src\BobCoach\Core\SecondHeroPowerChoiceSelection.cs"),
    (Join-Path $root "src\BobCoach\Core\SecondHeroPowerEntityObservation.cs"),
    (Join-Path $root "src\BobCoach\Core\ObservedTeammateGoldTransfer.cs"),
    (Join-Path $root "src\BobCoach\Core\SimulatedTeammateGoldTransfer.cs"),
    (Join-Path $root "src\BobCoach\Core\PurchaseRewardExpectation.cs"),
    (Join-Path $root "src\BobCoach\Core\TavernUpgradeOccurrence.cs"),
    (Join-Path $root "src\BobCoach\Core\PrizeDiscoverExpectation.cs"),
    (Join-Path $root "src\BobCoach\Core\TurnStartCardGrantExpectation.cs"),
    (Join-Path $root "src\BobCoach\Core\SharedTurnEventExpectation.cs"),
    (Join-Path $root "src\BobCoach\Core\SharedTurnEventOutcome.cs"),
    (Join-Path $root "src\BobCoach\Core\SharedCardVoteOccurrence.cs"),
    (Join-Path $root "src\BobCoach\Core\SharedCardVoteSelection.cs"),
    (Join-Path $root "src\BobCoach\Core\SharedCardGrantExpectation.cs"),
    (Join-Path $root "src\BobCoach\Core\SharedCardGrantObservation.cs"),
    (Join-Path $root "src\BobCoach\Core\GameRuleEvaluator.cs"),
    (Join-Path $root "src\BobCoach\Core\ActionEnumerator.cs"),
    (Join-Path $root "src\BobCoach\Core\TripleRuleEvaluator.cs"),
    (Join-Path $root "src\BobCoach\Core\TeammateGoldTransferEvaluator.cs"),
    (Join-Path $root "src\BobCoach\Core\UpgradePrizeEvaluator.cs"),
    (Join-Path $root "src\BobCoach\Core\CardPoolFactSource.cs"),
    (Join-Path $root "src\BobCoach\Core\LocalCardPoolMembershipSource.cs"),
    (Join-Path $root "src\BobCoach\Core\LocalCardPoolMembershipSnapshot.cs"),
    (Join-Path $root "src\BobCoach\Core\CardPoolSampler.cs"),
    (Join-Path $root "src\BobCoach\Core\BuddyCardPoolEvaluator.cs"),
    (Join-Path $root "src\BobCoach\Core\TimewarpCardPoolEvaluator.cs"),
    (Join-Path $root "src\BobCoach\Core\Simulator.cs"),
    (Join-Path $root "src\BobCoach\Core\CombatEventQueue.cs"),
    (Join-Path $root "src\BobCoach\Core\CombatContext.cs"),
    (Join-Path $root "src\BobCoach\Core\CombatEffects.cs"),
    (Join-Path $root "src\BobCoach\Core\CombatSimulator.cs"),
    (Join-Path $PSScriptRoot "ActiveTrinketEffectsHarness.cs")
)

& $csc /nologo /langversion:latest /target:exe "/out:$output" `
    "/r:$(Join-Path $hdtDir 'HearthDb.dll')" "/r:$netstandard" @sources
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $output
exit $LASTEXITCODE
