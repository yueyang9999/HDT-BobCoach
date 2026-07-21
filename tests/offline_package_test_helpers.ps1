$ErrorActionPreference = "Stop"

$script:OfflinePackageFiles = @(
    "BobCoach.dll",
    "README_OFFLINE.md",
    "INSTALL.ps1",
    "UNINSTALL.ps1",
    "LICENSE",
    "NOTICE",
    "DATA_SOURCES.md",
    "PRIVACY.md",
    "SUPPORT.md",
    "manifest.json",
    "SHA256SUMS.txt"
)

function Write-Utf8NoBom([string]$Path, [string]$Content) {
    [IO.File]::WriteAllText($Path, $Content, (New-Object Text.UTF8Encoding($false)))
}

function Assert-True([bool]$Condition, [string]$Label) {
    if (!$Condition) { throw "Assertion failed: $Label" }
}

function Assert-False([bool]$Condition, [string]$Label) {
    if ($Condition) { throw "Assertion failed: $Label" }
}

function Assert-Equal($Expected, $Actual, [string]$Label) {
    if ($Expected -ne $Actual) {
        throw "Assertion failed: $Label expected=$Expected actual=$Actual"
    }
}

function Assert-FileHashEqual([string]$ExpectedPath, [string]$ActualPath, [string]$Label) {
    $expected = (Get-FileHash -LiteralPath $ExpectedPath -Algorithm SHA256).Hash
    $actual = (Get-FileHash -LiteralPath $ActualPath -Algorithm SHA256).Hash
    Assert-Equal $expected $actual $Label
}

function Assert-SafeTestRoot([string]$Path) {
    $fullPath = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    $tempRoot = [IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\'
    if (!$fullPath.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unsafe test root outside TEMP: $fullPath"
    }
}

function New-TestPortableHdt(
    [string]$Root,
    [string]$Name = "PortableHdt",
    [string]$ExecutableName = "HearthstoneDeckTracker.exe"
) {
    Assert-SafeTestRoot $Root
    if ($ExecutableName -notin @("HearthstoneDeckTracker.exe", "Hearthstone Deck Tracker.exe")) {
        throw "Unsupported test HDT executable name: $ExecutableName"
    }
    $hdtRoot = Join-Path $Root $Name
    New-Item -ItemType Directory -Path $hdtRoot -Force | Out-Null
    Write-Utf8NoBom (Join-Path $hdtRoot $ExecutableName) "test fixture"
    return $hdtRoot
}

function New-TestManagedBobCoach([string]$Path, [string]$Version = "0.1.0.0") {
    $provider = New-Object Microsoft.CSharp.CSharpCodeProvider
    $parameters = New-Object System.CodeDom.Compiler.CompilerParameters
    $parameters.GenerateExecutable = $false
    $parameters.GenerateInMemory = $false
    $parameters.OutputAssembly = $Path
    $marker = "LegacyMarker" + [Guid]::NewGuid().ToString("N")
    $source = @"
using System.Reflection;
[assembly: AssemblyVersion("$Version")]
namespace BobCoach { public sealed class $marker { } }
"@
    $result = $provider.CompileAssemblyFromSource($parameters, $source)
    if ($result.Errors.Count -gt 0) {
        $messages = @($result.Errors | ForEach-Object { $_.ToString() }) -join "`n"
        throw "Failed to compile test BobCoach assembly:`n$messages"
    }
    return $Path
}

function Start-TestHdtProcess(
    [string]$HdtRoot,
    [string]$ExecutableName = "HearthstoneDeckTracker.exe"
) {
    Assert-SafeTestRoot $HdtRoot
    if ($ExecutableName -notin @("HearthstoneDeckTracker.exe", "Hearthstone Deck Tracker.exe")) {
        throw "Unsupported test HDT executable name: $ExecutableName"
    }
    $executable = Join-Path $HdtRoot $ExecutableName
    $expectedProcessName = [IO.Path]::GetFileNameWithoutExtension($ExecutableName)
    Copy-Item -LiteralPath (Get-Command powershell.exe -ErrorAction Stop).Source -Destination $executable -Force
    $process = Start-Process -FilePath $executable -ArgumentList @(
        "-NoProfile", "-Command", "Start-Sleep -Seconds 30"
    ) -PassThru -WindowStyle Hidden
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        $live = Get-Process -Id $process.Id -ErrorAction SilentlyContinue
        if ($null -ne $live -and $live.ProcessName -eq $expectedProcessName) { return $process }
        Start-Sleep -Milliseconds 100
    }
    Stop-TestHdtProcess $process
    throw "Test HDT process did not start"
}

function Stop-TestHdtProcess([Diagnostics.Process]$Process) {
    if ($null -eq $Process) { return }
    if (!$Process.HasExited) {
        Stop-Process -Id $Process.Id -Force -ErrorAction Stop
    }
    if (!$Process.WaitForExit(5000)) {
        throw "Test HDT process did not stop within 5 seconds: $($Process.Id)"
    }
}

function New-TestOfflinePackage([string]$Root, [string]$Name = "Package") {
    Assert-SafeTestRoot $Root
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $pluginSource = if ([string]::IsNullOrWhiteSpace($env:BOBCOACH_TEST_DLL)) {
        Join-Path $repoRoot "src\BobCoach\BobCoach.dll"
    } else {
        [IO.Path]::GetFullPath($env:BOBCOACH_TEST_DLL)
    }
    $installerSource = Join-Path $repoRoot "tools\release\INSTALL.ps1"
    if (!(Test-Path -LiteralPath $pluginSource -PathType Leaf)) {
        throw "Formal BobCoach.dll missing; set BOBCOACH_TEST_DLL or run npm run build:hdt first: $pluginSource"
    }
    if (!(Test-Path -LiteralPath $installerSource -PathType Leaf)) {
        throw "Installer source missing: $installerSource"
    }

    $packageRoot = Join-Path $Root $Name
    New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
    Copy-Item -LiteralPath $pluginSource -Destination (Join-Path $packageRoot "BobCoach.dll")
    Copy-Item -LiteralPath $installerSource -Destination (Join-Path $packageRoot "INSTALL.ps1")

    foreach ($nameToWrite in @(
        "README_OFFLINE.md",
        "UNINSTALL.ps1",
        "LICENSE",
        "NOTICE",
        "DATA_SOURCES.md",
        "PRIVACY.md",
        "SUPPORT.md"
    )) {
        Write-Utf8NoBom (Join-Path $packageRoot $nameToWrite) ("offline test fixture: {0}`n" -f $nameToWrite)
    }

    $pluginHash = (Get-FileHash -LiteralPath (Join-Path $packageRoot "BobCoach.dll") -Algorithm SHA256).Hash
    $pluginSize = (Get-Item -LiteralPath (Join-Path $packageRoot "BobCoach.dll")).Length
    $manifest = [ordered]@{
        schemaVersion = 1
        packageVersion = "0.2.0-beta.2"
        assemblyVersion = "0.2.0.0"
        fileVersion = "0.2.0.0"
        informationalVersion = "0.2.0-beta.2"
        targetFramework = ".NETFramework,Version=v4.7.2"
        runtimeIdentifier = "win-x64"
        hdtBaselineVersion = "1.53.5.7354"
        pluginFile = "BobCoach.dll"
        pluginSize = $pluginSize
        pluginSha256 = $pluginHash
        files = $script:OfflinePackageFiles
    }
    Write-Utf8NoBom (Join-Path $packageRoot "manifest.json") (($manifest | ConvertTo-Json -Depth 4) + "`n")

    $hashLines = foreach ($fileName in ($script:OfflinePackageFiles | Where-Object { $_ -ne "SHA256SUMS.txt" } | Sort-Object)) {
        $hash = (Get-FileHash -LiteralPath (Join-Path $packageRoot $fileName) -Algorithm SHA256).Hash
        "{0}  {1}" -f $hash, $fileName
    }
    Write-Utf8NoBom (Join-Path $packageRoot "SHA256SUMS.txt") (($hashLines -join "`n") + "`n")

    return [pscustomobject]@{
        Root = $packageRoot
        Installer = Join-Path $packageRoot "INSTALL.ps1"
        Plugin = Join-Path $packageRoot "BobCoach.dll"
    }
}

function Invoke-TestPowerShell([string]$ScriptPath, [string[]]$Arguments) {
    $escapedScript = $ScriptPath.Replace("'", "''")
    $commandArguments = foreach ($argument in $Arguments) {
        if ($argument.StartsWith("-", [StringComparison]::Ordinal)) {
            $argument
        } else {
            "'{0}'" -f $argument.Replace("'", "''")
        }
    }
    $command = "& '{0}' {1}" -f $escapedScript, ($commandArguments -join " ")
    $encodedCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
    $previousErrorAction = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -EncodedCommand $encodedCommand 2>&1
        $exitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previousErrorAction
    }
    return [pscustomobject]@{
        ExitCode = $exitCode
        Output = @($output)
    }
}
