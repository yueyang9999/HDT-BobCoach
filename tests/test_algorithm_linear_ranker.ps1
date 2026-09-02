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
if (!$hdtDir) { throw "BOBCOACH_HDT_DIR is required" }
$newtonsoft = Join-Path $hdtDir "Newtonsoft.Json.dll"
if (!(Test-Path -LiteralPath $newtonsoft)) { throw "Newtonsoft.Json.dll not found; set BOBCOACH_HDT_DIR" }

$outputDirectory = Join-Path $env:TEMP ("bobcoach_algorithm_linear_ranker_{0}" -f [Guid]::NewGuid().ToString("N"))
$output = Join-Path $outputDirectory "AlgorithmLinearRankerBehavior.exe"
$sources = @(
    "Core\ActionEnumerator.cs", "Core\HandCombatCardPolicy.cs", "Core\BobCoachDataPaths.cs", "Core\Season14RegistryJsonParser.cs",
    "Core\HeroPowerRuleCatalog.cs",
    "Core\Season14CardRegistry.cs",
    "Core\SeasonMechanicProfile.cs", "Core\ActivateRegistry.cs", "Core\ActivateFactSource.cs",
    "Core\LockboxRegistry.cs", "Core\LockboxFactSource.cs", "Core\FishbaitRegistry.cs",
    "Core\FishbaitFactSource.cs", "Core\DarkGiftRegistry.cs", "Core\DarkGiftFactSource.cs",
    "Core\Season14RulesEvaluator.cs", "Core\SeasonDataSelector.cs", "Core\Simulator.cs",
    "Core\Decision\Contracts.cs", "Core\Decision\ActionDescriptor.cs",
    "Core\Decision\ActionFeatureSchema.cs", "Core\Decision\RankingModel.cs",
    "Core\Decision\LinearPairwiseRanker.cs", "Core\Decision\DecisionConfidenceEvaluator.cs",
    "Core\Decision\ExpectedShopEstimator.cs", "Core\Decision\BeamPlanner.cs",
    "Core\Decision\ExplanationBuilder.cs", "Core\Decision\CandidateGenerator.cs",
    "Core\Decision\HardConstraintFilter.cs", "Core\Decision\ActionFeatureExtractor.cs",
    "Core\Decision\LegacyDecisionPolicy.cs", "Core\Decision\DecisionPolicyFactory.cs",
    "Core\Decision\UnifiedRankingPolicy.cs"
) | ForEach-Object { Join-Path $root (Join-Path "src\BobCoach" $_) } | Where-Object { Test-Path -LiteralPath $_ }
$sources += Join-Path $PSScriptRoot "AlgorithmLinearRankerBehavior.cs"
$fixture = Join-Path $PSScriptRoot "fixtures\algorithm\linear-v1.valid.envelope.json"

try {
    New-Item -ItemType Directory -Path $outputDirectory | Out-Null
    Copy-Item -LiteralPath $newtonsoft -Destination $outputDirectory
    Copy-Item -LiteralPath $fixture -Destination $outputDirectory
    & $csc /nologo /langversion:7.3 /target:exe "/out:$output" "/r:$newtonsoft" @sources
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $output
    exit $LASTEXITCODE
}
finally {
    if (Test-Path -LiteralPath $outputDirectory) { Remove-Item -LiteralPath $outputDirectory -Recurse -Force }
}
