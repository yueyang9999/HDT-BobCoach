[CmdletBinding()]
param(
    [string]$DataRoot,
    [string]$Scenarios,
    [string]$SafetyProbes,
    [string]$CandidateModel,
    [string]$OutputDirectory,
    [string]$HdtDirectory = $env:BOBCOACH_HDT_DIR,
    [switch]$SelfTest
)

$ErrorActionPreference = "Stop"
$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent (Split-Path -Parent $PSScriptRoot))).TrimEnd('\')
$sourcePath = Join-Path $PSScriptRoot "PolicyBenchmarkRunner.cs"
$temporaryRoot = Join-Path $env:TEMP ("bobcoach-policy-benchmark-" + [Guid]::NewGuid().ToString("N"))

function Resolve-HdtDirectory([string]$ConfiguredPath) {
    if (![string]::IsNullOrWhiteSpace($ConfiguredPath)) {
        return [IO.Path]::GetFullPath($ConfiguredPath).TrimEnd('\')
    }
    $candidateDirectories = New-Object System.Collections.Generic.List[string]
    $gameRoot = "D:\software\game"
    if (Test-Path -LiteralPath $gameRoot -PathType Container) {
        [IO.Directory]::GetDirectories($gameRoot, "HDT*") | ForEach-Object {
            $candidateDirectories.Add((Join-Path $_ "HDT"))
        }
    }
    $repositoryDrive = Split-Path -Qualifier $repoRoot
    $evidenceReference = Join-Path $repositoryDrive "HDT-BobCoach-evidence\dependencies\HDT-1.54.0\build-reference\Hearthstone Deck Tracker"
    if (Test-Path -LiteralPath $evidenceReference -PathType Container) {
        $candidateDirectories.Add($evidenceReference)
    }
    return @($candidateDirectories | ForEach-Object {
        $candidate = $_
        $executable = Join-Path $candidate "HearthstoneDeckTracker.exe"
        if (Test-Path -LiteralPath $executable -PathType Leaf) {
            try {
                if ([Reflection.AssemblyName]::GetAssemblyName($executable).Version.ToString() -eq "1.54.0.7376") {
                    $candidate
                }
            } catch { }
        }
    } | Select-Object -First 1)
}

function Resolve-CSharpCompiler {
    if (![string]::IsNullOrWhiteSpace($env:BOBCOACH_CSC)) {
        $configured = [IO.Path]::GetFullPath($env:BOBCOACH_CSC)
        if (!(Test-Path -LiteralPath $configured -PathType Leaf)) {
            throw "BOBCOACH_CSC does not point to a file: $configured"
        }
        return $configured
    }
    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path -LiteralPath $vswhere -PathType Leaf) {
        $compiler = & $vswhere -latest -products * -find "MSBuild\**\Bin\Roslyn\csc.exe" |
            Select-Object -First 1
        if (![string]::IsNullOrWhiteSpace($compiler) -and (Test-Path -LiteralPath $compiler -PathType Leaf)) {
            return [IO.Path]::GetFullPath($compiler)
        }
    }
    throw "Roslyn csc.exe was not found; set BOBCOACH_CSC or install Visual Studio Build Tools"
}

function assertExternalDataRoot([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { throw "DataRoot is required in normal mode" }
    $resolved = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    $repoPrefix = $repoRoot + [IO.Path]::DirectorySeparatorChar
    if ($resolved.Equals($repoRoot, [StringComparison]::OrdinalIgnoreCase) -or
        $resolved.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "DataRoot must be outside the repository"
    }
    $systemRoot = [IO.Path]::GetPathRoot($env:SystemDrive + [IO.Path]::DirectorySeparatorChar)
    if ([IO.Path]::GetPathRoot($resolved).Equals($systemRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "DataRoot must not be on the system drive"
    }
    if (!(Test-Path -LiteralPath $resolved -PathType Container)) {
        throw "DataRoot does not exist: $resolved"
    }
    return $resolved
}

function Resolve-DataPath([string]$Root, [string]$RelativePath, [string]$Name, [switch]$AllowMissing) {
    if ([string]::IsNullOrWhiteSpace($RelativePath)) { throw "$Name is required in normal mode" }
    if ([IO.Path]::IsPathRooted($RelativePath)) { throw "$Name must be relative to DataRoot" }
    $rootPrefix = $Root.TrimEnd('\') + [IO.Path]::DirectorySeparatorChar
    $resolved = [IO.Path]::GetFullPath((Join-Path $Root $RelativePath))
    if (!$resolved.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Name escapes DataRoot"
    }
    if (!$AllowMissing -and !(Test-Path -LiteralPath $resolved -PathType Leaf)) {
        throw "$Name does not exist: $resolved"
    }
    return $resolved
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { throw "OutputDirectory is required" }
if (!(Test-Path -LiteralPath $sourcePath -PathType Leaf)) { throw "Benchmark source is missing: $sourcePath" }

$resolvedHdt = Resolve-HdtDirectory $HdtDirectory
if ([string]::IsNullOrWhiteSpace($resolvedHdt)) {
    throw "HDT 1.54.0.7376 not found; set BOBCOACH_HDT_DIR"
}
foreach ($name in @("HearthstoneDeckTracker.exe", "HearthDb.dll", "Newtonsoft.Json.dll")) {
    if (!(Test-Path -LiteralPath (Join-Path $resolvedHdt $name) -PathType Leaf)) {
        throw "Missing HDT dependency: $name"
    }
}

$resolvedOutput = $null
$resolvedScenarios = $null
$resolvedProbes = $null
$resolvedModel = $null
if ($SelfTest) {
    $resolvedOutput = [IO.Path]::GetFullPath($OutputDirectory)
} else {
    $resolvedDataRoot = assertExternalDataRoot $DataRoot
    $resolvedScenarios = Resolve-DataPath $resolvedDataRoot $Scenarios "Scenarios"
    $resolvedProbes = Resolve-DataPath $resolvedDataRoot $SafetyProbes "SafetyProbes"
    $resolvedModel = Resolve-DataPath $resolvedDataRoot $CandidateModel "CandidateModel"
    $resolvedOutput = Resolve-DataPath $resolvedDataRoot $OutputDirectory "OutputDirectory" -AllowMissing
}

$buildDirectory = Join-Path $temporaryRoot "build"
$runtimeDirectory = Join-Path $temporaryRoot "runtime"
$runnerPath = Join-Path $runtimeDirectory "PolicyBenchmarkRunner.exe"
$runnerExitCode = 1
try {
    New-Item -ItemType Directory -Path $buildDirectory, $runtimeDirectory -Force | Out-Null
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repoRoot "tools\build\build_release.ps1") `
        -HdtDirectory $resolvedHdt -OutputDirectory $buildDirectory -Force
    if ($LASTEXITCODE -ne 0) { throw "Release build failed with exit code $LASTEXITCODE" }

    $compiler = Resolve-CSharpCompiler
    $bobCoachAssembly = Join-Path $buildDirectory "BobCoach.dll"
    $references = @(
        $bobCoachAssembly,
        (Join-Path $resolvedHdt "Newtonsoft.Json.dll"),
        (Join-Path $resolvedHdt "HearthstoneDeckTracker.exe"),
        (Join-Path $resolvedHdt "HearthDb.dll"),
        "System.dll",
        "System.Core.dll"
    )
    $referenceArguments = @($references | ForEach-Object { "/reference:$($_)" })
    & $compiler /nologo /target:exe /platform:x64 /optimize+ /langversion:7.3 `
        "/out:$runnerPath" @referenceArguments $sourcePath
    if ($LASTEXITCODE -ne 0) { throw "Benchmark runner compilation failed with exit code $LASTEXITCODE" }

    Copy-Item -LiteralPath $bobCoachAssembly -Destination $runtimeDirectory
    foreach ($name in @("Newtonsoft.Json.dll", "HearthstoneDeckTracker.exe", "HearthDb.dll")) {
        Copy-Item -LiteralPath (Join-Path $resolvedHdt $name) -Destination $runtimeDirectory
    }

    $arguments = @("--output", $resolvedOutput)
    if ($SelfTest) {
        $arguments += "--self-test"
    } else {
        $arguments += @("--scenarios", $resolvedScenarios, "--probes", $resolvedProbes, "--model", $resolvedModel)
    }
    & $runnerPath @arguments
    $runnerExitCode = $LASTEXITCODE
} finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        $resolvedTemporary = [IO.Path]::GetFullPath($temporaryRoot)
        $temporaryPrefix = [IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + "\bobcoach-policy-benchmark-"
        if (!$resolvedTemporary.StartsWith($temporaryPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Unsafe benchmark cleanup path: $resolvedTemporary"
        }
        Remove-Item -LiteralPath $resolvedTemporary -Recurse -Force
    }
}
if ($runnerExitCode -ne 0) { exit $runnerExitCode }
