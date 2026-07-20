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
    $oldTuanziLatest = "HDT" + [string][char]0x56E2 + [string][char]0x5B50 `
        + [string][char]0x7248 + [string][char]0x6700 + [string][char]0x65B0
    $ignored = @(Join-Path $gameDir $oldTuanziLatest)
    $hdtDir = @([System.IO.Directory]::GetDirectories($gameDir, "HDT*") | ForEach-Object {
        if ($ignored -contains ([System.IO.Path]::GetFullPath($_).TrimEnd('\'))) { return }
        $candidate = Join-Path $_ "HDT"
        if (!(Test-Path (Join-Path $candidate "HearthDb.dll"))) { $candidate = $_ }
        $exe = Join-Path $candidate "HearthstoneDeckTracker.exe"
        if ((Test-Path (Join-Path $candidate "HearthDb.dll")) -and (Test-Path $exe)) {
            try {
                if ([Reflection.AssemblyName]::GetAssemblyName($exe).Version.ToString() -eq "1.53.5.7354") {
                    $candidate
                }
            } catch { }
        }
    } | Select-Object -First 1)
}
if (!$hdtDir -or !(Test-Path (Join-Path $hdtDir "HearthDb.dll"))) {
    throw "HearthDb.dll not found; set BOBCOACH_HDT_DIR"
}

$output = Join-Path $env:TEMP "bobcoach_hero_strategy_facade.exe"
$sources = @(
    (Join-Path $root "src\BobCoach\Core\HeroPowerEngine.cs"),
    (Join-Path $root "src\BobCoach\Core\HeroPowerFactSource.cs"),
    (Join-Path $root "src\BobCoach\Core\HeroStrategyRuleEvaluator.cs"),
    (Join-Path $root "src\BobCoach\Core\HearthDbHeroPowerFactSource.cs"),
    (Join-Path $root "src\BobCoach\Core\CachedHeroStrategySource.cs"),
    (Join-Path $PSScriptRoot "HeroStrategyFacadeBehavior.cs")
)
$netstandard = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\netstandard.dll"
try {
    & $csc /nologo /langversion:latest /target:exe "/out:$output" `
        /r:System.Web.Extensions.dll "/r:$(Join-Path $hdtDir 'HearthDb.dll')" `
        "/r:$netstandard" @sources
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $output
    exit $LASTEXITCODE
}
finally {
    if (Test-Path -LiteralPath $output) {
        Remove-Item -LiteralPath $output -Force -ErrorAction SilentlyContinue
    }
}
