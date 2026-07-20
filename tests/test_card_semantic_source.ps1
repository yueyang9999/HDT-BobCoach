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

$output = Join-Path $env:TEMP "bobcoach_card_semantic_source.exe"
$sources = @(
    (Join-Path $root "src\BobCoach\Core\CardSemanticFactSource.cs"),
    (Join-Path $root "src\BobCoach\Core\CardSemanticRuleEvaluator.cs"),
    (Join-Path $root "src\BobCoach\Core\CachedCardSemanticSource.cs"),
    (Join-Path $PSScriptRoot "CardSemanticSourceBehavior.cs")
)
& $csc /nologo /langversion:7.3 /target:exe "/out:$output" @sources
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $output
exit $LASTEXITCODE
