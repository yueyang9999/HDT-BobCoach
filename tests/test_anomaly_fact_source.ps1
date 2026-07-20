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
                if ([Reflection.AssemblyName]::GetAssemblyName($exe).Version.ToString() -eq "1.53.5.0") {
                    $candidate
                }
            } catch { }
        }
    } | Select-Object -First 1)
}
if (!$hdtDir -or !(Test-Path (Join-Path $hdtDir "HearthDb.dll"))) {
    throw "HearthDb.dll not found; set BOBCOACH_HDT_DIR"
}

$source = Join-Path $root "src\BobCoach\Core\HearthDbAnomalyFactSource.cs"
if (!(Test-Path -LiteralPath $source)) {
    Write-Error "RED target production interface missing: HearthDbAnomalyFactSource.cs"
    exit 1
}

$output = Join-Path $env:TEMP "bobcoach_anomaly_fact_source_behavior.exe"
$netstandard = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\netstandard.dll"
try {
    & $csc /nologo /langversion:latest /target:exe "/out:$output" `
        "/r:$(Join-Path $hdtDir 'HearthDb.dll')" "/r:$netstandard" `
        (Join-Path $root "src\BobCoach\Core\AnomalyRegistry.cs") `
        (Join-Path $root "src\BobCoach\Core\AnomalyFactSource.cs") `
        $source `
        (Join-Path $PSScriptRoot "AnomalyFactSourceHearthDbBehavior.cs")
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $output $hdtDir
    exit $LASTEXITCODE
}
finally {
    if (Test-Path -LiteralPath $output) {
        Remove-Item -LiteralPath $output -Force -ErrorAction SilentlyContinue
    }
}
