$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path $env:TEMP ("bobcoach-package-suite-" + [Guid]::NewGuid().ToString("N"))
$buildRoot = Join-Path $testRoot "build"
$oldTestDll = $env:BOBCOACH_TEST_DLL

function Resolve-HdtDirectory {
    if (![string]::IsNullOrWhiteSpace($env:BOBCOACH_HDT_DIR)) {
        return [IO.Path]::GetFullPath($env:BOBCOACH_HDT_DIR)
    }
    $gameRoot = "D:\software\game"
    if (!(Test-Path -LiteralPath $gameRoot -PathType Container)) { return $null }
    return @([IO.Directory]::GetDirectories($gameRoot, "HDT*") | ForEach-Object {
        $candidate = Join-Path $_ "HDT"
        $executable = Join-Path $candidate "HearthstoneDeckTracker.exe"
        if (Test-Path -LiteralPath $executable -PathType Leaf) {
            try {
                if ([Reflection.AssemblyName]::GetAssemblyName($executable).Version.ToString() -eq "1.53.5.0") {
                    $candidate
                }
            } catch { }
        }
    } | Select-Object -First 1)
}

function Invoke-Checked([string]$File) {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot $File)
    if ($LASTEXITCODE -ne 0) { throw "$File failed with exit code $LASTEXITCODE" }
}

try {
    New-Item -ItemType Directory -Path $buildRoot -Force | Out-Null
    $hdtDirectory = Resolve-HdtDirectory
    if ([string]::IsNullOrWhiteSpace($hdtDirectory)) {
        throw "HDT 1.53.5.0 not found; set BOBCOACH_HDT_DIR"
    }
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repoRoot "tools\build\build_release.ps1") `
        -HdtDirectory $hdtDirectory -OutputDirectory $buildRoot -Force
    if ($LASTEXITCODE -ne 0) { throw "release build failed with exit code $LASTEXITCODE" }
    $env:BOBCOACH_TEST_DLL = Join-Path $buildRoot "BobCoach.dll"

    foreach ($file in @(
        "test_deterministic_build_and_package.ps1",
        "test_official_hdt_executable_names.ps1",
        "test_offline_installer.ps1",
        "test_offline_uninstaller.ps1",
        "test_offline_package_builder.ps1",
        "test_offline_package_lifecycle_consumer.ps1"
    )) {
        Invoke-Checked $file
    }
    Write-Host "PASS package suite"
} finally {
    $env:BOBCOACH_TEST_DLL = $oldTestDll
    if (Test-Path -LiteralPath $testRoot) {
        $resolved = [IO.Path]::GetFullPath($testRoot)
        $tempPrefix = [IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\bobcoach-package-suite-'
        if (!$resolved.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Unsafe package suite cleanup path: $resolved"
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
