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
if ($env:BOBCOACH_HDT_DIR) {
    $jsonCandidates += Join-Path $env:BOBCOACH_HDT_DIR "Newtonsoft.Json.dll"
}
if ($env:LOCALAPPDATA) {
    $jsonCandidates += Join-Path $env:LOCALAPPDATA "HearthstoneDeckTracker\app-1.54.0\Newtonsoft.Json.dll"
    $jsonCandidates += Join-Path $env:LOCALAPPDATA "HearthstoneDeckTracker\app-1.54.1\Newtonsoft.Json.dll"
}
if (Test-Path -LiteralPath "D:\software\game" -PathType Container) {
    $jsonCandidates += Get-ChildItem -Path "D:\software\game\HDT*\HDT\Newtonsoft.Json.dll" -File -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty FullName
}
$newtonsoft = $jsonCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (!$newtonsoft) { throw "Newtonsoft.Json.dll not found; set BOBCOACH_HDT_DIR" }

$output = Join-Path $env:TEMP ("bobcoach_algorithm_explanations_{0}.exe" -f [Guid]::NewGuid().ToString("N"))
$sources = @(@(
    (Join-Path $root "src\BobCoach\Core\Decision\Contracts.cs"),
    (Join-Path $root "src\BobCoach\Core\Decision\ActionFeatureSchema.cs"),
    (Join-Path $root "src\BobCoach\Core\Decision\RankingModel.cs"),
    (Join-Path $root "src\BobCoach\Core\Decision\LegacyDecisionPolicy.cs"),
    (Join-Path $root "src\BobCoach\Core\Decision\ExplanationBuilder.cs"),
    (Join-Path $root "src\BobCoach\Core\Decision\DecisionPolicyFactory.cs")
) | Where-Object { Test-Path -LiteralPath $_ })
$sources += (Join-Path $PSScriptRoot "AlgorithmExplanationBehavior.cs")
$fixture = Join-Path $PSScriptRoot "fixtures\algorithm\linear-v1.valid.envelope.json"

try {
    & $csc /nologo /langversion:7.3 /target:exe "/out:$output" "/reference:$newtonsoft" @sources
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $config = @"
<?xml version="1.0" encoding="utf-8" ?>
<configuration>
  <startup useLegacyV2RuntimeActivationPolicy="true">
    <supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.7.2" />
  </startup>
  <runtime>
    <assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1">
      <dependentAssembly>
        <assemblyIdentity name="Newtonsoft.Json" publicKeyToken="30ad4fe6b2a6aeed" culture="neutral" />
        <bindingRedirect oldVersion="0.0.0.0-99.0.0.0" newVersion="12.0.0.0" />
        <codeBase version="12.0.0.0" href="$([Uri]::new($newtonsoft).AbsoluteUri)" />
      </dependentAssembly>
    </assemblyBinding>
  </runtime>
</configuration>
"@
    [IO.File]::WriteAllText($output + ".config", $config, [Text.Encoding]::UTF8)
    & $output $fixture
    exit $LASTEXITCODE
}
finally {
    foreach ($path in @($output, $output + ".config")) {
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
    }
}
