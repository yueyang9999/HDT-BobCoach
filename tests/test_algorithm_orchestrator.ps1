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

$jsonCandidates = @()
if ($env:BOBCOACH_HDT_DIR) { $jsonCandidates += Join-Path $env:BOBCOACH_HDT_DIR "Newtonsoft.Json.dll" }
if ($env:LOCALAPPDATA) {
    $jsonCandidates += Join-Path $env:LOCALAPPDATA "HearthstoneDeckTracker\app-1.55.0\Newtonsoft.Json.dll"
    $jsonCandidates += Join-Path $env:LOCALAPPDATA "HearthstoneDeckTracker\app-1.54.1\Newtonsoft.Json.dll"
}
$jsonDll = $jsonCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (!$jsonDll) { throw "Newtonsoft.Json.dll not found; set BOBCOACH_HDT_DIR" }

$output = Join-Path $env:TEMP ("bobcoach_algorithm_orchestrator_{0}.exe" -f [Guid]::NewGuid().ToString("N"))
$sources = @(
    (Join-Path $root "src\BobCoach\Core\BobCoachDataPaths.cs"),
    (Join-Path $root "src\BobCoach\Core\Decision\Contracts.cs"),
    (Join-Path $root "src\BobCoach\Core\Decision\DecisionEventRecord.cs"),
    (Join-Path $root "src\BobCoach\Core\Decision\DecisionEventRecorder.cs"),
    (Join-Path $root "src\BobCoach\Core\Decision\LegacyDecisionPolicy.cs"),
    (Join-Path $root "src\BobCoach\Core\Decision\DecisionOrchestrator.cs"),
    (Join-Path $PSScriptRoot "AlgorithmOrchestratorBehavior.cs")
) | Where-Object { Test-Path -LiteralPath $_ }

& $csc /nologo /langversion:7.3 /target:exe "/out:$output" "/reference:$jsonDll" @sources
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# 将实际 Newtonsoft.Json.dll 复制到 exe 同目录(HDT 1.54=v12 / HDT-V2.7.0=v13),
# 让 .NET Framework 默认探测命中; v13 为 netstandard2.0 构建需 netstandard.dll facade
$exeDir = Split-Path $output
Copy-Item -LiteralPath $jsonDll -Destination (Join-Path $exeDir "Newtonsoft.Json.dll") -Force
$netstandardFacade = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\netstandard.dll"
if (Test-Path -LiteralPath $netstandardFacade) {
    Copy-Item -LiteralPath $netstandardFacade -Destination (Join-Path $exeDir "netstandard.dll") -Force
}
& $output
exit $LASTEXITCODE
