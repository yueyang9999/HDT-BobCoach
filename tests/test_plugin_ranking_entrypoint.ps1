$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" |
    Select-Object -First 1
$csc = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\Roslyn\csc.exe" |
    Select-Object -First 1
if (!$msbuild) { throw "MSBuild.exe not found" }
if (!$csc) { throw "Roslyn csc.exe not found" }

$hdtCandidates = @()
if ($env:BOBCOACH_HDT_DIR) { $hdtCandidates += $env:BOBCOACH_HDT_DIR }
if ($env:LOCALAPPDATA) {
    $hdtCandidates += Join-Path $env:LOCALAPPDATA "HearthstoneDeckTracker\app-1.55.0"
    $hdtCandidates += Join-Path $env:LOCALAPPDATA "HearthstoneDeckTracker\app-1.54.1"
}
$hdtDirectory = $hdtCandidates | Where-Object {
    Test-Path -LiteralPath (Join-Path $_ "HearthstoneDeckTracker.exe")
} | Select-Object -First 1
if (!$hdtDirectory) { throw "HDT directory not found; set BOBCOACH_HDT_DIR" }

$project = Join-Path $root "src\BobCoach\BobCoach.csproj"
& $msbuild $project /nologo /verbosity:minimal /target:Build /property:Configuration=Release `
    /property:Platform=x64 "/property:HdtDirectory=$hdtDirectory"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$pluginDirectory = Join-Path $root "src\BobCoach\bin\x64\Release"
$pluginDll = Join-Path $pluginDirectory "BobCoach.dll"
$output = Join-Path $pluginDirectory "PluginRankingEntrypointBehavior.exe"
$references = @(
    $pluginDll,
    (Join-Path $hdtDirectory "HearthstoneDeckTracker.exe"),
    (Join-Path $hdtDirectory "HearthDb.dll"),
    (Join-Path $hdtDirectory "Newtonsoft.Json.dll")
)

& $csc /nologo /langversion:7.3 /platform:x64 /target:exe "/out:$output" `
    ($references | ForEach-Object { "/reference:$_" }) `
    (Join-Path $PSScriptRoot "PluginRankingEntrypointBehavior.cs")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$previousHdtDirectory = $env:BOBCOACH_HDT_DIR
try {
    $env:BOBCOACH_HDT_DIR = $hdtDirectory
    & $output
    exit $LASTEXITCODE
}
finally {
    $env:BOBCOACH_HDT_DIR = $previousHdtDirectory
}
