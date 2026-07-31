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
    "Core\Decision\Contracts.cs", "Core\Decision\ActionDescriptor.cs",
    "Core\Decision\ActionFeatureSchema.cs", "Core\Decision\RankingModel.cs",
    "Core\Decision\LinearPairwiseRanker.cs", "Core\Decision\UnifiedRankingPolicy.cs"
) | ForEach-Object { Join-Path $root (Join-Path "src\BobCoach" $_) } | Where-Object { Test-Path -LiteralPath $_ }
$sources += Join-Path $PSScriptRoot "AlgorithmLinearRankerBehavior.cs"

try {
    New-Item -ItemType Directory -Path $outputDirectory | Out-Null
    Copy-Item -LiteralPath $newtonsoft -Destination $outputDirectory
    & $csc /nologo /langversion:7.3 /target:exe "/out:$output" "/r:$newtonsoft" @sources
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $output
    exit $LASTEXITCODE
}
finally {
    if (Test-Path -LiteralPath $outputDirectory) { Remove-Item -LiteralPath $outputDirectory -Recurse -Force }
}
