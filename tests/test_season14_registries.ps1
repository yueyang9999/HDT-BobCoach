$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$evidenceRoot = "E:\HDT-BobCoach-evidence\season14"
$registryDir = Join-Path $evidenceRoot "generated\registries"
if (!(Test-Path (Join-Path $registryDir "dark_gift_registry.json"))) {
    throw "Season14 evidence registries not found: $registryDir"
}

$candidates = @()
if ($env:BOBCOACH_CSC) { $candidates += $env:BOBCOACH_CSC }
$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path $vswhere) {
    $found = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\Roslyn\csc.exe"
    if ($found) { $candidates += $found }
}
$csc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (!$csc) { throw "Roslyn csc.exe not found; install Visual Studio Build Tools or set BOBCOACH_CSC" }

$output = Join-Path $env:TEMP "bobcoach_season14_registries_harness.exe"
$previousRoot = $env:BOB_COACH_DATA_ROOT
$env:BOB_COACH_DATA_ROOT = $evidenceRoot

$sources = @(
    (Join-Path $root "src\BobCoach\Core\BobCoachDataPaths.cs"),
    (Join-Path $root "src\BobCoach\Core\Season14RegistryJsonParser.cs"),
    (Join-Path $root "src\BobCoach\Core\SeasonDataSelector.cs"),
    (Join-Path $root "src\BobCoach\Core\SeasonMechanicProfile.cs"),
    (Join-Path $root "src\BobCoach\Core\CompGuideCatalog.cs"),
    (Join-Path $root "src\BobCoach\Core\Season14CardRegistry.cs"),
    (Join-Path $root "src\BobCoach\Core\CardPurchaseFactSource.cs"),
    (Join-Path $root "src\BobCoach\Core\Season14MinionRegistry.cs"),
    (Join-Path $root "src\BobCoach\Core\Season14MinionPurchaseFactSource.cs"),
    (Join-Path $root "src\BobCoach\Core\Season14TavernSpellRegistry.cs"),
    (Join-Path $root "src\BobCoach\Core\Season14TavernSpellPurchaseFactSource.cs"),
    (Join-Path $root "src\BobCoach\Core\CombatContext.cs"),
    (Join-Path $root "src\BobCoach\Core\CombatSimulator.cs"),
    (Join-Path $root "src\BobCoach\Core\CombatEffects.cs"),
    (Join-Path $root "src\BobCoach\Core\CombatEventQueue.cs"),
    (Join-Path $root "src\BobCoach\Core\ActiveTrinketContext.cs"),
    (Join-Path $root "src\BobCoach\Core\GameState.cs"),
    (Join-Path $root "src\BobCoach\Core\EffectiveGameRules.cs"),
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
    (Join-Path $root "src\BobCoach\Core\HeroIdentityExpectation.cs"),
    (Join-Path $root "src\BobCoach\Core\SecondHeroPowerChoiceExpectation.cs"),
    (Join-Path $root "src\BobCoach\Core\SecondHeroPowerChoiceBatchObservation.cs"),
    (Join-Path $root "src\BobCoach\Core\SecondHeroPowerChoiceSelection.cs"),
    (Join-Path $root "src\BobCoach\Core\SecondHeroPowerEntityObservation.cs"),
    (Join-Path $root "src\BobCoach\Core\SimulatedTeammateGoldTransfer.cs"),
    (Join-Path $root "src\BobCoach\Core\ObservedTeammateGoldTransfer.cs"),
    (Join-Path $root "src\BobCoach\Core\HeroPowerState.cs"),
    (Join-Path $root "src\BobCoach\Core\FirstPurchaseExtraCopyRule.cs"),
    (Join-Path $root "src\BobCoach\Core\UpgradePrizeRule.cs"),
    (Join-Path $root "src\BobCoach\Core\PortalInBottleRule.cs"),
    (Join-Path $root "src\BobCoach\Core\SharedYoggWheelRule.cs"),
    (Join-Path $root "src\BobCoach\Core\SharedCardVoteRule.cs"),
    (Join-Path $root "src\BobCoach\Core\AllHeroesOverrideRule.cs"),
    (Join-Path $root "src\BobCoach\Core\SecondHeroPowerDiscoverRule.cs"),
    (Join-Path $root "src\BobCoach\Core\TeammateGoldTransferRule.cs"),
    (Join-Path $root "src\BobCoach\Core\EffectiveCardPoolRules.cs"),
    (Join-Path $root "src\BobCoach\Core\StartResourceExpectation.cs"),
    (Join-Path $root "src\BobCoach\Core\ScheduledGrant.cs"),
    (Join-Path $root "src\BobCoach\Core\SecondaryHeroPowerRule.cs"),
    (Join-Path $root "src\BobCoach\Core\TimewarpVisit.cs"),
    (Join-Path $root "src\BobCoach\Core\TimewarpOfferRule.cs"),
    (Join-Path $root "src\BobCoach\Core\TimewarpPoolMergeRule.cs"),
    (Join-Path $root "src\BobCoach\Core\BuddyPoolRule.cs"),
    (Join-Path $root "src\BobCoach\Core\ActivateRegistry.cs"),
    (Join-Path $root "src\BobCoach\Core\ActivateFactSource.cs"),
    (Join-Path $root "src\BobCoach\Core\LockboxRegistry.cs"),
    (Join-Path $root "src\BobCoach\Core\LockboxFactSource.cs"),
    (Join-Path $root "src\BobCoach\Core\FishbaitRegistry.cs"),
    (Join-Path $root "src\BobCoach\Core\FishbaitFactSource.cs"),
    (Join-Path $root "src\BobCoach\Core\DarkGiftRegistry.cs"),
    (Join-Path $root "src\BobCoach\Core\DarkGiftFactSource.cs"),
    (Join-Path $root "src\BobCoach\Core\PowerLogParser.cs"),
    (Join-Path $root "src\BobCoach\Core\AnomalyContext.cs"),
    (Join-Path $root "src\BobCoach\Core\AnomalyRegistry.cs"),
    (Join-Path $root "src\BobCoach\Core\AnomalyFactSource.cs"),
    (Join-Path $root "src\BobCoach\Core\TrinketChoiceBatchLifecycle.cs"),
    (Join-Path $root "src\BobCoach\Core\Season14RulesEvaluator.cs"),
    (Join-Path $root "src\BobCoach\Core\TrinketDefinitionRegistry.cs"),
    (Join-Path $root "src\BobCoach\Core\TrinketFactSource.cs"),
    (Join-Path $root "src\BobCoach\Core\TrinketDefinitionFactSource.cs"),
    (Join-Path $PSScriptRoot "Season14RegistriesHarness.cs")
)

try {
    & $csc /nologo /langversion:latest /target:exe "/out:$output" @sources
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $output
    exit $LASTEXITCODE
}
finally {
    $env:BOB_COACH_DATA_ROOT = $previousRoot
    if (Test-Path -LiteralPath $output) {
        Remove-Item -LiteralPath $output -Force -ErrorAction SilentlyContinue
    }
}
