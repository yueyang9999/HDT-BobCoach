param(
    [string]$RegistryPath,
    [string]$ManifestPath
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrEmpty($RegistryPath)) {
    $RegistryPath = Join-Path $root "data\card_classification_registry.json"
}
if ([string]::IsNullOrEmpty($ManifestPath)) {
    $ManifestPath = Join-Path $root "data\card_classification_registry.manifest.json"
}
$candidates = @()
if ($env:BOBCOACH_CSC) { $candidates += $env:BOBCOACH_CSC }
$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path $vswhere) {
    $found = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\Roslyn\csc.exe"
    if ($found) { $candidates += $found }
}
$csc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (!$csc) { throw "Roslyn csc.exe not found; install Visual Studio Build Tools or set BOBCOACH_CSC" }

$output = Join-Path $env:TEMP "bobcoach_card_classification_registry_generator.exe"
$sources = @(
    (Join-Path $root "hdt-plugin\BobCoach\Core\CardSemantics.cs"),
    (Join-Path $root "hdt-plugin\BobCoach\Core\BobCoachDataPaths.cs"),
    (Join-Path $root "hdt-plugin\BobCoach\Core\CardClassifier.cs"),
    (Join-Path $PSScriptRoot "CardClassificationRegistryGenerator.cs")
)
& $csc /nologo /langversion:latest /define:BOBCOACH_LEGACY_CLASSIFICATION_GENERATOR `
    /target:exe "/out:$output" /r:System.Web.Extensions.dll @sources
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $output `
    (Join-Path $root "data\cards.json") `
    (Join-Path $root "hdt-plugin\BobCoach\Resources\semantic_index.json") `
    $RegistryPath `
    $ManifestPath
exit $LASTEXITCODE
