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

$output = Join-Path $env:TEMP ("bobcoach_layout_resolution_matrix_{0}.exe" -f [Guid]::NewGuid().ToString("N"))
$sources = @(
    (Join-Path $root "src\BobCoach\Core\BobCoachDataPaths.cs"),
    (Join-Path $root "src\BobCoach\Core\LayoutConfig.cs"),
    (Join-Path $root "src\BobCoach\Core\SafeNativeMethods.cs"),
    (Join-Path $root "src\BobCoach\Core\GameLayoutCalculator.cs"),
    (Join-Path $PSScriptRoot "LayoutResolutionMatrixBehavior.cs")
)
$webExtensions = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\System.Web.Extensions.dll"

try {
    & $csc /nologo /langversion:latest /target:exe "/out:$output" "/r:$webExtensions" @sources
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $output
    exit $LASTEXITCODE
}
finally {
    if (Test-Path -LiteralPath $output) {
        Remove-Item -LiteralPath $output -Force
    }
}
