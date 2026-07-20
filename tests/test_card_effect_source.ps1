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

$coreSources = @(
    (Join-Path $root "src\BobCoach\Core\CardEffectFactSource.cs"),
    (Join-Path $root "src\BobCoach\Core\CardEffectRuleEvaluator.cs"),
    (Join-Path $root "src\BobCoach\Core\CachedCardEffectSource.cs")
)
$cacheOutput = Join-Path $env:TEMP "bobcoach_card_effect_source_behavior.exe"
& $csc /nologo /langversion:latest /target:exe "/out:$cacheOutput" `
    @coreSources (Join-Path $PSScriptRoot "CardEffectFactSourceBehavior.cs")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $cacheOutput
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

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

$localOutput = Join-Path $env:TEMP "bobcoach_card_effect_source_hearthdb.exe"
$netstandard = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\netstandard.dll"
& $csc /nologo /langversion:latest /target:exe "/out:$localOutput" `
    "/r:$(Join-Path $hdtDir 'HearthDb.dll')" "/r:$netstandard" `
    @coreSources `
    (Join-Path $root "src\BobCoach\Core\HearthDbCardEffectFactSource.cs") `
    (Join-Path $PSScriptRoot "CardEffectFactSourceHearthDbBehavior.cs")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $localOutput $hdtDir
exit $LASTEXITCODE
