[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Assembly,
    [Parameter(Mandatory = $true)][string]$HdtDirectory
)
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$src = Join-Path $repoRoot "tests\ActivateTargetBehavior.cs"
if (!(Test-Path -LiteralPath $Assembly)) { throw "Assembly missing: $Assembly" }
$tmp = Join-Path $env:TEMP ("bobcoach-behavior-test-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tmp | Out-Null
$exe = Join-Path $tmp "PolicyBenchmarkRunner.exe"
$vswhere = "C:/Program Files (x86)/Microsoft Visual Studio/Installer/vswhere.exe"
$csc = & $vswhere -latest -products * -find "MSBuild/**/Bin/Roslyn/csc.exe" | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($csc)) { throw "csc not found" }
foreach ($n in @("Newtonsoft.Json.dll", "HearthDb.dll", "HearthstoneDeckTracker.exe")) { $s = Join-Path $HdtDirectory $n; if (Test-Path $s) { Copy-Item $s $tmp } }
& $csc /nologo /target:exe /platform:x64 /optimize+ /langversion:7.3 "/out:$exe" "/reference:$Assembly" "/reference:System.dll" "/reference:System.Core.dll" $src
if ($LASTEXITCODE -ne 0) { throw "test compile failed" }
Push-Location $tmp
& ".\PolicyBenchmarkRunner.exe"
Pop-Location
exit $LASTEXITCODE
