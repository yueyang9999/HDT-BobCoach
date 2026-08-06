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
if (!$hdtDir) { $hdtDir = "D:\software\game\HDT-V2.7.0\HDT" }
$newtonsoft = Join-Path $hdtDir "Newtonsoft.Json.dll"
if (!(Test-Path -LiteralPath $newtonsoft)) { throw "Newtonsoft.Json.dll not found; set BOBCOACH_HDT_DIR" }

$outputDirectory = Join-Path $env:TEMP ("bobcoach_algorithm_model_contract_{0}" -f [Guid]::NewGuid().ToString("N"))
$output = Join-Path $outputDirectory "AlgorithmModelContractBehavior.exe"
$sources = @(
    (Join-Path $root "src\BobCoach\Core\Decision\ActionFeatureSchema.cs"),
    (Join-Path $root "src\BobCoach\Core\Decision\RankingModel.cs"),
    (Join-Path $PSScriptRoot "AlgorithmModelContractBehavior.cs")
) | Where-Object { Test-Path -LiteralPath $_ }

try {
    New-Item -ItemType Directory -Path $outputDirectory | Out-Null
    Copy-Item -LiteralPath $newtonsoft -Destination $outputDirectory
    & $csc /nologo /langversion:7.3 /target:exe "/out:$output" "/r:$newtonsoft" @sources
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $output $root
    exit $LASTEXITCODE
}
finally {
    if (Test-Path -LiteralPath $outputDirectory) { Remove-Item -LiteralPath $outputDirectory -Recurse -Force }
}
