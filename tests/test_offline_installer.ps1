[CmdletBinding()]
param([switch]$FreshInstallOnly)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "offline_package_test_helpers.ps1")

$testRoot = Join-Path $env:TEMP ("bobcoach-offline-installer-test-" + [Guid]::NewGuid().ToString("N"))
Assert-SafeTestRoot $testRoot

try {
    New-Item -ItemType Directory -Path $testRoot -Force | Out-Null

    $package = New-TestOfflinePackage -Root $testRoot -Name "FreshPackage"
    $hdt = New-TestPortableHdt -Root $testRoot -Name "FreshHdt"
    $plugins = Join-Path $hdt "Plugins"

    $whatIf = Invoke-TestPowerShell $package.Installer @("-PluginDirectory", $plugins, "-WhatIf", "-Confirm:`$false")
    Assert-Equal 0 $whatIf.ExitCode "WhatIf exit"
    Assert-False (Test-Path -LiteralPath $plugins) "WhatIf creates no plugin directory"

    $fresh = Invoke-TestPowerShell $package.Installer @("-PluginDirectory", $plugins, "-Confirm:`$false")
    Assert-Equal 0 $fresh.ExitCode "fresh install exit"
    Assert-FileHashEqual $package.Plugin (Join-Path $plugins "BobCoach.dll") "fresh install DLL hash"
    Assert-Equal 0 @(Get-ChildItem -LiteralPath $plugins -Filter "BobCoach.dll.backup-*" -File).Count "fresh backup count"

    $tampered = New-TestOfflinePackage -Root $testRoot -Name "TamperedPackage"
    Add-Content -LiteralPath (Join-Path $tampered.Root "README_OFFLINE.md") -Value "tampered"
    $tamperHdt = New-TestPortableHdt -Root $testRoot -Name "TamperHdt"
    $tamperPlugins = Join-Path $tamperHdt "Plugins"
    $tamperResult = Invoke-TestPowerShell $tampered.Installer @("-PluginDirectory", $tamperPlugins, "-Confirm:`$false")
    Assert-True ($tamperResult.ExitCode -ne 0) "tampered package fails"
    Assert-False (Test-Path -LiteralPath $tamperPlugins) "tampered package writes nothing"

    $invalidTarget = Join-Path $testRoot "NotAPluginsDirectory"
    $invalidResult = Invoke-TestPowerShell $package.Installer @("-PluginDirectory", $invalidTarget, "-Confirm:`$false")
    Assert-True ($invalidResult.ExitCode -ne 0) "non-Plugins path fails"
    Assert-False (Test-Path -LiteralPath $invalidTarget) "non-Plugins path writes nothing"

    $missingHdtParent = Join-Path (Join-Path $testRoot "MissingHdt") "Plugins"
    New-Item -ItemType Directory -Path (Split-Path -Parent $missingHdtParent) -Force | Out-Null
    $missingHdtResult = Invoke-TestPowerShell $package.Installer @("-PluginDirectory", $missingHdtParent, "-Confirm:`$false")
    Assert-True ($missingHdtResult.ExitCode -ne 0) "portable path without HDT exe fails"
    Assert-False (Test-Path -LiteralPath $missingHdtParent) "missing-HDT path writes nothing"

    if ($FreshInstallOnly) {
        Write-Host "PASS offline installer fresh install, integrity, path, and WhatIf contracts"
        return
    }

    $upgradeHdt = New-TestPortableHdt -Root $testRoot -Name "UpgradeHdt"
    $upgradePlugins = Join-Path $upgradeHdt "Plugins"
    New-Item -ItemType Directory -Path $upgradePlugins -Force | Out-Null
    $targetDll = Join-Path $upgradePlugins "BobCoach.dll"
    New-TestManagedBobCoach -Path $targetDll -Version "0.1.0.0" | Out-Null
    $legacyHash = (Get-FileHash -LiteralPath $targetDll -Algorithm SHA256).Hash
    $legacyBytes = [IO.File]::ReadAllBytes($targetDll)

    $upgrade = Invoke-TestPowerShell $package.Installer @("-PluginDirectory", $upgradePlugins, "-Confirm:`$false")
    Assert-Equal 0 $upgrade.ExitCode "upgrade exit"
    Assert-FileHashEqual $package.Plugin $targetDll "upgrade target hash"
    $upgradeBackups = @(Get-ChildItem -LiteralPath $upgradePlugins -Filter "BobCoach.dll.backup-*" -File)
    Assert-Equal 1 $upgradeBackups.Count "upgrade backup count"
    $expectedBackupPattern = '^BobCoach\.dll\.backup-\d{8}T\d{9}Z-' + [regex]::Escape($legacyHash.Substring(0, 16)) + '$'
    Assert-True ($upgradeBackups[0].Name -match $expectedBackupPattern) "upgrade backup name"
    Assert-True ([Linq.Enumerable]::SequenceEqual([byte[]]$legacyBytes, [byte[]][IO.File]::ReadAllBytes($upgradeBackups[0].FullName))) "upgrade backup bytes"

    $rollback = Invoke-TestPowerShell $package.Installer @("-PluginDirectory", $upgradePlugins, "-Rollback", "-Confirm:`$false")
    Assert-Equal 0 $rollback.ExitCode "latest rollback exit"
    Assert-Equal $legacyHash (Get-FileHash -LiteralPath $targetDll -Algorithm SHA256).Hash "latest rollback target hash"
    $afterRollbackBackups = @(Get-ChildItem -LiteralPath $upgradePlugins -Filter "BobCoach.dll.backup-*" -File)
    Assert-Equal 2 $afterRollbackBackups.Count "rollback preserves current version"

    $candidateHash = (Get-FileHash -LiteralPath $package.Plugin -Algorithm SHA256).Hash
    $candidateBackup = @($afterRollbackBackups | Where-Object {
        (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash -eq $candidateHash
    })
    Assert-Equal 1 $candidateBackup.Count "candidate rollback backup"
    $specifiedRollback = Invoke-TestPowerShell $package.Installer @(
        "-PluginDirectory", $upgradePlugins, "-Rollback", "-BackupPath", $candidateBackup[0].FullName, "-Confirm:`$false"
    )
    Assert-Equal 0 $specifiedRollback.ExitCode "specified rollback exit"
    Assert-Equal $candidateHash (Get-FileHash -LiteralPath $targetDll -Algorithm SHA256).Hash "specified rollback target hash"

    $targetBeforeFailure = (Get-FileHash -LiteralPath $targetDll -Algorithm SHA256).Hash
    $corruptBackup = Join-Path $upgradePlugins "BobCoach.dll.backup-20990101T000000000Z-0000000000000000"
    Write-Utf8NoBom $corruptBackup "not a managed assembly"
    $corruptResult = Invoke-TestPowerShell $package.Installer @(
        "-PluginDirectory", $upgradePlugins, "-Rollback", "-BackupPath", $corruptBackup, "-Confirm:`$false"
    )
    Assert-True ($corruptResult.ExitCode -ne 0) "corrupt backup fails"
    Assert-Equal $targetBeforeFailure (Get-FileHash -LiteralPath $targetDll -Algorithm SHA256).Hash "corrupt backup preserves target"

    $outsideBackup = Join-Path $testRoot "BobCoach.dll.backup-20990101T000000001Z-1111111111111111"
    New-TestManagedBobCoach -Path $outsideBackup -Version "0.0.1.0" | Out-Null
    $outsideResult = Invoke-TestPowerShell $package.Installer @(
        "-PluginDirectory", $upgradePlugins, "-Rollback", "-BackupPath", $outsideBackup, "-Confirm:`$false"
    )
    Assert-True ($outsideResult.ExitCode -ne 0) "outside backup fails"
    Assert-Equal $targetBeforeFailure (Get-FileHash -LiteralPath $targetDll -Algorithm SHA256).Hash "outside backup preserves target"

    $withoutRollback = Invoke-TestPowerShell $package.Installer @(
        "-PluginDirectory", $upgradePlugins, "-BackupPath", $candidateBackup[0].FullName, "-Confirm:`$false"
    )
    Assert-True ($withoutRollback.ExitCode -ne 0) "BackupPath without Rollback fails"

    $emptyHdt = New-TestPortableHdt -Root $testRoot -Name "EmptyRollbackHdt"
    $emptyPlugins = Join-Path $emptyHdt "Plugins"
    $emptyRollback = Invoke-TestPowerShell $package.Installer @("-PluginDirectory", $emptyPlugins, "-Rollback", "-Confirm:`$false")
    Assert-True ($emptyRollback.ExitCode -ne 0) "rollback without backup fails"
    Assert-False (Test-Path -LiteralPath $emptyPlugins) "empty rollback writes nothing"

    $runningHdt = New-TestPortableHdt -Root $testRoot -Name "RunningHdt"
    $runningPlugins = Join-Path $runningHdt "Plugins"
    $runningProcess = Start-TestHdtProcess $runningHdt
    try {
        $runningResult = Invoke-TestPowerShell $package.Installer @("-PluginDirectory", $runningPlugins, "-Confirm:`$false")
        Assert-True ($runningResult.ExitCode -ne 0) "running portable HDT blocks install"
        Assert-False (Test-Path -LiteralPath $runningPlugins) "running HDT writes nothing"

        $previousAppData = $env:APPDATA
        $testAppData = Join-Path $testRoot "DefaultAppData"
        $defaultHdtParent = Join-Path $testAppData "HearthstoneDeckTracker"
        New-Item -ItemType Directory -Path $defaultHdtParent -Force | Out-Null
        $env:APPDATA = $testAppData
        try {
            $defaultRunningResult = Invoke-TestPowerShell $package.Installer @("-Confirm:`$false")
            Assert-True ($defaultRunningResult.ExitCode -ne 0) "any running HDT blocks default install"
            Assert-False (Test-Path -LiteralPath (Join-Path $defaultHdtParent "Plugins")) "default running HDT writes nothing"
        } finally {
            $env:APPDATA = $previousAppData
        }
    } finally {
        if (!$runningProcess.HasExited) { Stop-Process -Id $runningProcess.Id -Force }
    }

    Write-Host "PASS offline installer install, integrity, upgrade, rollback, and HDT process contracts"
} catch {
    Write-Host "FAIL offline installer fresh install contracts"
    Write-Host $_.Exception.Message
    exit 1
} finally {
    if (Test-Path -LiteralPath $testRoot) {
        Assert-SafeTestRoot $testRoot
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
