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
    $jsonCandidates += Join-Path $env:LOCALAPPDATA "HearthstoneDeckTracker\app-1.54.0\Newtonsoft.Json.dll"
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

$jsonUri = ([Uri]$jsonDll).AbsoluteUri
$config = @"
<?xml version="1.0" encoding="utf-8" ?>
<configuration>
  <runtime>
    <assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1">
      <dependentAssembly>
        <assemblyIdentity name="Newtonsoft.Json" publicKeyToken="30ad4fe6b2a6aeed" culture="neutral" />
        <bindingRedirect oldVersion="0.0.0.0-12.0.0.0" newVersion="12.0.0.0" />
        <codeBase version="12.0.0.0" href="$jsonUri" />
      </dependentAssembly>
    </assemblyBinding>
  </runtime>
</configuration>
"@
[IO.File]::WriteAllText($output + ".config", $config, [Text.Encoding]::UTF8)
& $output
exit $LASTEXITCODE
