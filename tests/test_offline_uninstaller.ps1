$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "offline_package_test_helpers.ps1")

$repoRoot = Split-Path -Parent $PSScriptRoot
$uninstaller = Join-Path $repoRoot "tools\release\UNINSTALL.ps1"
$testRoot = Join-Path $env:TEMP ("bobcoach-offline-uninstaller-test-" + [Guid]::NewGuid().ToString("N"))
$previousAppData = $env:APPDATA
Assert-SafeTestRoot $testRoot

try {
    if (!(Test-Path -LiteralPath $uninstaller -PathType Leaf)) {
        throw "Uninstaller source missing: $uninstaller"
    }

    New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
    $testAppData = Join-Path $testRoot "AppData"
    New-Item -ItemType Directory -Path $testAppData -Force | Out-Null
    $env:APPDATA = $testAppData

    $plugins = Join-Path (Join-Path $testAppData "HearthstoneDeckTracker") "Plugins"
    New-Item -ItemType Directory -Path $plugins -Force | Out-Null
    $targetDll = Join-Path $plugins "BobCoach.dll"
    New-TestManagedBobCoach -Path $targetDll | Out-Null
    Write-Utf8NoBom (Join-Path $plugins "BobCoach.dll.backup-20260101T000000000Z-1111111111111111") "backup one"
    Write-Utf8NoBom (Join-Path $plugins "BobCoach.dll.backup-20260102T000000000Z-2222222222222222") "backup two"

    $userData = Join-Path $testAppData "bob-coach"
    New-Item -ItemType Directory -Path $userData -Force | Out-Null
    $userDataFile = Join-Path $userData "player_profile.json"
    Write-Utf8NoBom $userDataFile "private test data"
    $logConfig = Join-Path $testRoot "log.config"
    Write-Utf8NoBom $logConfig "shared log config"

    $defaultResult = Invoke-TestPowerShell $uninstaller @("-Confirm:`$false")
    Assert-Equal 0 $defaultResult.ExitCode "default uninstall exit"
    Assert-False (Test-Path -LiteralPath $targetDll) "default removes DLL"
    Assert-Equal 2 @(Get-ChildItem -LiteralPath $plugins -Filter "BobCoach.dll.backup-*" -File).Count "default retains backups"
    Assert-True (Test-Path -LiteralPath $userDataFile) "default retains user data"
    Assert-True (Test-Path -LiteralPath $logConfig) "default retains log.config"

    $idempotent = Invoke-TestPowerShell $uninstaller @("-Confirm:`$false")
    Assert-Equal 0 $idempotent.ExitCode "idempotent uninstall exit"

    New-TestManagedBobCoach -Path $targetDll | Out-Null
    $whatIf = Invoke-TestPowerShell $uninstaller @("-PluginDirectory", $plugins, "-WhatIf", "-Confirm:`$false")
    Assert-Equal 0 $whatIf.ExitCode "uninstall WhatIf exit"
    Assert-True (Test-Path -LiteralPath $targetDll) "WhatIf retains DLL"
    Assert-True (Test-Path -LiteralPath $userDataFile) "WhatIf retains user data"

    $removeData = Invoke-TestPowerShell $uninstaller @(
        "-PluginDirectory", $plugins, "-RemoveUserData", "-Confirm:`$false"
    )
    Assert-Equal 0 $removeData.ExitCode "explicit data uninstall exit"
    Assert-False (Test-Path -LiteralPath $targetDll) "explicit data uninstall removes DLL"
    Assert-False (Test-Path -LiteralPath $userData) "explicit data uninstall removes exact user data"
    Assert-Equal 2 @(Get-ChildItem -LiteralPath $plugins -Filter "BobCoach.dll.backup-*" -File).Count "explicit data uninstall retains backups"
    Assert-True (Test-Path -LiteralPath $logConfig) "explicit data uninstall retains log.config"

    $junctionTarget = Join-Path (New-TestPortableHdt -Root $testRoot -Name "JunctionTargetHdt") "Plugins"
    New-Item -ItemType Directory -Path $junctionTarget -Force | Out-Null
    $junctionTargetDll = Join-Path $junctionTarget "BobCoach.dll"
    New-TestManagedBobCoach -Path $junctionTargetDll | Out-Null
    $junctionAppData = Join-Path $testRoot "JunctionAppData"
    $junctionHdtAppData = Join-Path $junctionAppData "HearthstoneDeckTracker"
    New-Item -ItemType Directory -Path $junctionHdtAppData -Force | Out-Null
    $junctionPlugins = Join-Path $junctionHdtAppData "Plugins"
    New-Item -ItemType Junction -Path $junctionPlugins -Target $junctionTarget | Out-Null
    $env:APPDATA = $junctionAppData
    $junctionResult = Invoke-TestPowerShell $uninstaller @(
        "-PluginDirectory", $junctionPlugins, "-Confirm:`$false"
    )
    Assert-True ($junctionResult.ExitCode -ne 0) "reparse Plugins path fails"
    Assert-True (Test-Path -LiteralPath $junctionTargetDll -PathType Leaf) "reparse target DLL retained"
    $env:APPDATA = $testAppData

    $runningHdt = New-TestPortableHdt -Root $testRoot -Name "RunningHdt"
    $runningDll = Join-Path $plugins "BobCoach.dll"
    New-TestManagedBobCoach -Path $runningDll | Out-Null
    $runningProcess = Start-TestHdtProcess $runningHdt
    try {
        $runningResult = Invoke-TestPowerShell $uninstaller @("-PluginDirectory", $plugins, "-Confirm:`$false")
        Assert-True ($runningResult.ExitCode -ne 0) "running HDT blocks uninstall"
        Assert-True (Test-Path -LiteralPath $runningDll) "running HDT retains DLL"
    } finally {
        if (!$runningProcess.HasExited) { Stop-Process -Id $runningProcess.Id -Force }
    }

    $outsideData = Join-Path $testRoot "OutsideData"
    New-Item -ItemType Directory -Path $outsideData -Force | Out-Null
    $outsideSentinel = Join-Path $outsideData "sentinel.txt"
    Write-Utf8NoBom $outsideSentinel "retain"
    $junctionCreated = $false
    try {
        New-Item -ItemType Junction -Path $userData -Target $outsideData -ErrorAction Stop | Out-Null
        $junctionCreated = $true
    } catch {
        Write-Host "SKIP reparse-point uninstall test: $($_.Exception.Message)"
    }
    if ($junctionCreated) {
        $reparseResult = Invoke-TestPowerShell $uninstaller @(
            "-PluginDirectory", $plugins, "-RemoveUserData", "-Confirm:`$false"
        )
        Assert-True ($reparseResult.ExitCode -ne 0) "reparse user data fails"
        Assert-True (Test-Path -LiteralPath $userData) "reparse user data link retained"
        Assert-True (Test-Path -LiteralPath $outsideSentinel) "reparse target retained"
    }

    Write-Host "PASS offline uninstaller default, explicit data, WhatIf, process, and path contracts"
} catch {
    Write-Host "FAIL offline uninstaller contracts"
    Write-Host $_.Exception.Message
    exit 1
} finally {
    $env:APPDATA = $previousAppData
    if (Test-Path -LiteralPath $testRoot) {
        Assert-SafeTestRoot $testRoot
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
