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

$output = Join-Path $env:TEMP "bobcoach_effect_value_table_behavior.exe"
$sources = @(
    (Join-Path $root "src\BobCoach\Core\CardEffectFactSource.cs"),
    (Join-Path $root "src\BobCoach\Core\CardEffectRuleEvaluator.cs"),
    (Join-Path $root "src\BobCoach\Core\CachedCardEffectSource.cs"),
    (Join-Path $root "src\BobCoach\Core\LocalCardPoolMembershipSource.cs"),
    (Join-Path $root "src\BobCoach\Core\HearthDbCardPoolMembershipSource.cs"),
    (Join-Path $root "src\BobCoach\Core\HearthDbCardEffectFactSource.cs"),
    (Join-Path $root "src\BobCoach\Core\CardEffectBaselineProvider.cs"),
    (Join-Path $root "src\BobCoach\Core\EffectValueTable.cs"),
    (Join-Path $PSScriptRoot "EffectValueTableStubs.cs"),
    (Join-Path $PSScriptRoot "EffectValueTableBehavior.cs")
)
$netstandard = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\netstandard.dll"
& $csc /nologo /langversion:latest /target:exe "/out:$output" `
    "/r:$(Join-Path $hdtDir 'HearthDb.dll')" "/r:$netstandard" @sources
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $output
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$decision = Get-Content -Raw -Encoding UTF8 (Join-Path $root "src\BobCoach\Core\DecisionEngine.cs")
if ([regex]::Matches($decision, 'score\s*\+=\s*EffectValueBonus\(').Count -ne 2) {
    throw "DecisionEngine must retain exactly two EffectValueBonus consumers"
}
if ((-not [regex]::IsMatch($decision, 'EFFECT_BONUS_LAMBDA\s*=\s*ReadEffectLambda\(\)')) -or
    (-not [regex]::IsMatch($decision, 'return\s+0\.20\s*;')) -or
    (-not [regex]::IsMatch($decision, 'EFFECT_BONUS_CAP\s*=\s*0\.6'))) {
    throw "DecisionEngine effect bonus constants changed"
}
Write-Output "PASS DecisionEngine retains two bounded EffectValue consumers"
