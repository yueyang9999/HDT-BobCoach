$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "offline_package_test_helpers.ps1")

$repoRoot = Split-Path -Parent $PSScriptRoot
$uninstaller = Join-Path $repoRoot "tools\release\UNINSTALL.ps1"
$testRoot = Join-Path $env:TEMP ("bobcoach-hdt-plugin-directory-test-" + [Guid]::NewGuid().ToString("N"))
$previousAppData = $env:APPDATA
Assert-SafeTestRoot $testRoot

try {
    New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
    $package = New-TestOfflinePackage -Root $testRoot -Name "Package"

    $appData = Join-Path $testRoot "AppData"
    $hdtAppData = Join-Path $appData "HearthstoneDeckTracker"
    $appDataPlugins = Join-Path $hdtAppData "Plugins"
    New-Item -ItemType Directory -Path $hdtAppData -Force | Out-Null
    $env:APPDATA = $appData

    $hdtProgram = New-TestPortableHdt -Root $testRoot -Name "HdtProgram" `
        -ExecutableName "Hearthstone Deck Tracker.exe"
    $programPlugins = Join-Path $hdtProgram "Plugins"

    $wrongInstall = Invoke-TestPowerShell $package.Installer @(
        "-PluginDirectory", $programPlugins, "-Confirm:`$false"
    )
    Assert-True ($wrongInstall.ExitCode -ne 0) "HDT program Plugins install is rejected"
    Assert-False (Test-Path -LiteralPath $programPlugins) "rejected install writes nothing"

    New-Item -ItemType Directory -Path $programPlugins -Force | Out-Null
    $programDll = Join-Path $programPlugins "BobCoach.dll"
    Copy-Item -LiteralPath $package.Plugin -Destination $programDll
    $wrongUninstall = Invoke-TestPowerShell $uninstaller @(
        "-PluginDirectory", $programPlugins, "-Confirm:`$false"
    )
    Assert-True ($wrongUninstall.ExitCode -ne 0) "HDT program Plugins uninstall is rejected"
    Assert-True (Test-Path -LiteralPath $programDll -PathType Leaf) "rejected uninstall writes nothing"

    $explicitInstall = Invoke-TestPowerShell $package.Installer @(
        "-PluginDirectory", $appDataPlugins, "-Confirm:`$false"
    )
    Assert-Equal 0 $explicitInstall.ExitCode "explicit AppData install exit"
    Assert-FileHashEqual $package.Plugin (Join-Path $appDataPlugins "BobCoach.dll") `
        "explicit AppData install hash"

    Write-Host "PASS HDT AppData plugin source directory contract"
} catch {
    Write-Host "FAIL HDT AppData plugin source directory contract"
    Write-Host $_.Exception.Message
    exit 1
} finally {
    $env:APPDATA = $previousAppData
    if (Test-Path -LiteralPath $testRoot) {
        Assert-SafeTestRoot $testRoot
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
