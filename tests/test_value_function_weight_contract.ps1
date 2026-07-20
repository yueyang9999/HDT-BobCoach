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

$output = Join-Path $env:TEMP ("bobcoach_value_function_weight_contract_{0}.exe" -f [Guid]::NewGuid().ToString("N"))
$sources = @(
    (Join-Path $root "src\BobCoach\Core\BobCoachDataPaths.cs"),
    (Join-Path $root "src\BobCoach\Core\ValueFunction.cs"),
    (Join-Path $PSScriptRoot "ValueFunctionWeightContract.cs")
)

try {
    & $csc /nologo /langversion:latest /target:exe "/out:$output" @sources
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $output
    exit $LASTEXITCODE
}
finally {
    if (Test-Path -LiteralPath $output) {
        Remove-Item -LiteralPath $output -Force
    }
}
