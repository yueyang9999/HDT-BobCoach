$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$candidates = @()
if ($env:BOBCOACH_CSC) { $candidates += $env:BOBCOACH_CSC }
$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path -LiteralPath $vswhere) {
    $found = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\Roslyn\csc.exe"
    if ($found) { $candidates += $found }
}
$csc = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
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

$outputDirectory = Join-Path $env:TEMP ("bobcoach_algorithm_features_{0}" -f [Guid]::NewGuid().ToString("N"))
$output = Join-Path $outputDirectory "AlgorithmFeatureBehavior.exe"
$netstandard = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\netstandard.dll"
$sources = @(
    "Core\GameState.cs", "Core\AnomalyFactSource.cs", "Core\AnomalyRegistry.cs", "Core\AnomalyContext.cs",
    "Core\ActiveTrinketContext.cs", "Core\TrinketEffectRegistry.cs",
    "Core\TrinketEffectResolver.cs", "Core\EffectiveGameRules.cs", "Core\EffectiveCardPoolRules.cs",
    "Core\FirstPurchaseExtraCopyRule.cs", "Core\UpgradePrizeRule.cs", "Core\PortalInBottleRule.cs",
    "Core\SharedYoggWheelRule.cs", "Core\SharedCardVoteRule.cs", "Core\BuddyPoolRule.cs",
    "Core\AllHeroesOverrideRule.cs", "Core\SecondHeroPowerDiscoverRule.cs", "Core\TeammateGoldTransferRule.cs",
    "Core\StartResourceExpectation.cs", "Core\ScheduledGrant.cs", "Core\SecondaryHeroPowerRule.cs",
    "Core\TimewarpVisit.cs", "Core\TimewarpOfferRule.cs", "Core\TimewarpPoolMergeRule.cs",
    "Core\HeroPowerState.cs", "Core\HeroIdentityExpectation.cs", "Core\SecondHeroPowerChoiceExpectation.cs",
    "Core\SecondHeroPowerChoiceBatchObservation.cs", "Core\SecondHeroPowerChoiceSelection.cs",
    "Core\SecondHeroPowerEntityObservation.cs", "Core\ObservedTeammateGoldTransfer.cs",
    "Core\SimulatedTeammateGoldTransfer.cs", "Core\PurchaseRewardExpectation.cs", "Core\TavernUpgradeOccurrence.cs",
    "Core\PrizeDiscoverExpectation.cs", "Core\TurnStartCardGrantExpectation.cs", "Core\SharedTurnEventExpectation.cs",
    "Core\SharedTurnEventOutcome.cs", "Core\SharedCardVoteOccurrence.cs", "Core\SharedCardVoteSelection.cs",
    "Core\SharedCardGrantExpectation.cs", "Core\SharedCardGrantObservation.cs", "Core\GameRuleEvaluator.cs",
    "Core\ActionEnumerator.cs", "Core\TripleRuleEvaluator.cs", "Core\TeammateGoldTransferEvaluator.cs",
    "Core\UpgradePrizeEvaluator.cs", "Core\CardPoolFactSource.cs", "Core\LocalCardPoolMembershipSource.cs",
    "Core\LocalCardPoolMembershipSnapshot.cs", "Core\CardPoolSampler.cs", "Core\BuddyCardPoolEvaluator.cs",
    "Core\TimewarpCardPoolEvaluator.cs", "Core\Simulator.cs", "Core\Decision\HardConstraintFilter.cs", "Core\CombatEventQueue.cs",
    "Core\CombatContext.cs", "Core\CombatEffects.cs", "Core\CombatSimulator.cs", "Core\Decision\ActionDescriptor.cs",
    "Core\Decision\ActionFeatureSchema.cs", "Core\Decision\ExpectedShopEstimator.cs",
    "Core\Decision\ActionFeatureExtractor.cs"
) | ForEach-Object { Join-Path $root (Join-Path "src\BobCoach" $_) } | Where-Object { Test-Path -LiteralPath $_ }
$sources += Join-Path $PSScriptRoot "AlgorithmFeatureBehavior.cs"

try {
    New-Item -ItemType Directory -Path $outputDirectory | Out-Null
    Copy-Item -LiteralPath (Join-Path $hdtDir 'HearthDb.dll') -Destination $outputDirectory
    & $csc /nologo /langversion:7.3 /target:exe "/out:$output" "/r:$(Join-Path $hdtDir 'HearthDb.dll')" "/r:$netstandard" @sources
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $output
    exit $LASTEXITCODE
}
finally {
    if (Test-Path -LiteralPath $outputDirectory) { Remove-Item -LiteralPath $outputDirectory -Recurse -Force }
}
