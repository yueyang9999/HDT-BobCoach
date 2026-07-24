[CmdletBinding()]
param(
    [switch]$FreshInstallOnly,
    [switch]$ChecksumFailureOnly,
    [switch]$RollbackChecksumFailureOnly
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "offline_package_test_helpers.ps1")

$testRoot = Join-Path $env:TEMP ("bobcoach-offline-installer-test-" + [Guid]::NewGuid().ToString("N"))
$previousAppData = $env:APPDATA
Assert-SafeTestRoot $testRoot

function New-TestHdtPluginDirectory([string]$Name) {
    $appData = Join-Path (Join-Path $testRoot $Name) "AppData"
    $hdtAppData = Join-Path $appData "HearthstoneDeckTracker"
    New-Item -ItemType Directory -Path $hdtAppData -Force | Out-Null
    $env:APPDATA = $appData
    return Join-Path $hdtAppData "Plugins"
}

function Invoke-TestInstallerWithoutHostHdt(
    [string]$Installer,
    [string]$PluginDirectory,
    [switch]$Rollback
) {
    $wrapper = Join-Path $testRoot "invoke-installer-without-host-hdt.ps1"
    Write-Utf8NoBom $wrapper @'
param([string]$Installer, [string]$PluginDirectory, [switch]$Rollback)
$ErrorActionPreference = "Stop"
function Get-Process {
    param([string[]]$Name, [object]$ErrorAction)
    return @()
}
if ($Rollback) {
    & $Installer -PluginDirectory $PluginDirectory -Rollback -Confirm:$false
} else {
    & $Installer -PluginDirectory $PluginDirectory -Confirm:$false
}
'@
    $arguments = @("-Installer", $Installer, "-PluginDirectory", $PluginDirectory)
    if ($Rollback) { $arguments += "-Rollback" }
    return Invoke-TestPowerShell $wrapper $arguments
}

try {
    New-Item -ItemType Directory -Path $testRoot -Force | Out-Null

    $package = New-TestOfflinePackage -Root $testRoot -Name "FreshPackage"
    $plugins = New-TestHdtPluginDirectory "Fresh"

    if ($ChecksumFailureOnly) {
        New-Item -ItemType Directory -Path $plugins -Force | Out-Null
        $targetDll = Join-Path $plugins "BobCoach.dll"
        New-TestManagedBobCoach -Path $targetDll -Version "0.1.0.0" | Out-Null
        $legacyHash = (Get-FileHash -LiteralPath $targetDll -Algorithm SHA256).Hash
        New-Item -ItemType Directory -Path (Join-Path $plugins "BobCoach.dll.sha256") | Out-Null
        $result = Invoke-TestInstallerWithoutHostHdt $package.Installer $plugins
        Assert-True ($result.ExitCode -ne 0) "checksum publish failure exits nonzero"
        Assert-Equal $legacyHash (Get-FileHash -LiteralPath $targetDll -Algorithm SHA256).Hash "checksum publish failure preserves previous DLL"
        Write-Host "PASS checksum publish failure preserves the installed DLL"
        return
    }

    if ($RollbackChecksumFailureOnly) {
        New-Item -ItemType Directory -Path $plugins -Force | Out-Null
        $targetDll = Join-Path $plugins "BobCoach.dll"
        New-TestManagedBobCoach -Path $targetDll -Version "1.0.0.0" | Out-Null
        $currentHash = (Get-FileHash -LiteralPath $targetDll -Algorithm SHA256).Hash
        $rollbackDll = Join-Path $plugins "BobCoach.dll.backup-20260724T000000000Z-0000000000000000"
        $rollbackSource = Join-Path $testRoot "rollback-source\BobCoach.dll"
        New-Item -ItemType Directory -Path (Split-Path -Parent $rollbackSource) -Force | Out-Null
        New-TestManagedBobCoach -Path $rollbackSource -Version "0.1.0.0" | Out-Null
        Copy-Item -LiteralPath $rollbackSource -Destination $rollbackDll
        New-Item -ItemType Directory -Path (Join-Path $plugins "BobCoach.dll.sha256") | Out-Null
        $result = Invoke-TestInstallerWithoutHostHdt $package.Installer $plugins -Rollback
        Assert-True ($result.ExitCode -ne 0) "rollback checksum publish failure exits nonzero"
        $failureOutput = $result.Output -join "`n"
        Assert-True ($failureOutput -match 'Cannot create a file when that file already exists') "rollback reaches checksum publish"
        $rollbackBackups = @(Get-ChildItem -LiteralPath $plugins -Filter "BobCoach.dll.backup-*" -File)
        Assert-Equal 1 $rollbackBackups.Count "rollback compensation preserves original backup only"
        Assert-Equal $currentHash (Get-FileHash -LiteralPath $targetDll -Algorithm SHA256).Hash "rollback checksum publish failure preserves current DLL"
        Write-Host "PASS rollback checksum publish failure preserves the installed DLL"
        return
    }

    $previousErrorAction = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $whatIfOutput = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $package.Installer `
            -PluginDirectory $plugins -WhatIf 2>&1
        $whatIfExitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previousErrorAction
    }
    Assert-Equal 0 $whatIfExitCode "WhatIf -File exit; output=$(@($whatIfOutput) -join ' | ')"
    Assert-False (Test-Path -LiteralPath $plugins) "WhatIf creates no plugin directory"

    $fresh = Invoke-TestPowerShell $package.Installer @("-PluginDirectory", $plugins, "-Confirm:`$false")
    Assert-Equal 0 $fresh.ExitCode "fresh install exit"
    Assert-FileHashEqual $package.Plugin (Join-Path $plugins "BobCoach.dll") "fresh install DLL hash"
    Assert-PluginChecksum (Join-Path $plugins "BobCoach.dll") (Join-Path $plugins "BobCoach.dll.sha256") "fresh install"
    Assert-Equal 0 @(Get-ChildItem -LiteralPath $plugins -Filter "BobCoach.dll.backup-*" -File).Count "fresh backup count"

    $defaultPlugins = New-TestHdtPluginDirectory "DefaultSuccess"
    $defaultFresh = Invoke-TestPowerShell $package.Installer @("-Confirm:`$false")
    Assert-Equal 0 $defaultFresh.ExitCode "default fresh install exit"
    Assert-FileHashEqual $package.Plugin (Join-Path $defaultPlugins "BobCoach.dll") "default fresh install DLL hash"
    Assert-PluginChecksum (Join-Path $defaultPlugins "BobCoach.dll") (Join-Path $defaultPlugins "BobCoach.dll.sha256") "default fresh install"

    $missingSidecar = New-TestOfflinePackage -Root $testRoot -Name "MissingSidecarPackage"
    Remove-Item -LiteralPath $missingSidecar.PluginChecksum -Force
    $missingSidecarPlugins = New-TestHdtPluginDirectory "MissingSidecar"
    $missingSidecarResult = Invoke-TestPowerShell $missingSidecar.Installer @("-PluginDirectory", $missingSidecarPlugins, "-Confirm:`$false")
    Assert-True ($missingSidecarResult.ExitCode -ne 0) "missing sidecar package fails"
    Assert-False (Test-Path -LiteralPath $missingSidecarPlugins) "missing sidecar package writes nothing"

    $mismatchedSidecar = New-TestOfflinePackage -Root $testRoot -Name "MismatchedSidecarPackage"
    Write-Utf8NoBom $mismatchedSidecar.PluginChecksum (("0" * 64) + "  BobCoach.dll`n")
    Write-TestOfflinePackageSums $mismatchedSidecar.Root
    $mismatchedSidecarPlugins = New-TestHdtPluginDirectory "MismatchedSidecar"
    $mismatchedSidecarResult = Invoke-TestPowerShell $mismatchedSidecar.Installer @("-PluginDirectory", $mismatchedSidecarPlugins, "-Confirm:`$false")
    Assert-True ($mismatchedSidecarResult.ExitCode -ne 0) "mismatched sidecar package fails"
    Assert-False (Test-Path -LiteralPath $mismatchedSidecarPlugins) "mismatched sidecar package writes nothing"

    $tampered = New-TestOfflinePackage -Root $testRoot -Name "TamperedPackage"
    Add-Content -LiteralPath (Join-Path $tampered.Root "README_OFFLINE.md") -Value "tampered"
    $tamperPlugins = New-TestHdtPluginDirectory "Tamper"
    $tamperResult = Invoke-TestPowerShell $tampered.Installer @("-PluginDirectory", $tamperPlugins, "-Confirm:`$false")
    Assert-True ($tamperResult.ExitCode -ne 0) "tampered package fails"
    Assert-False (Test-Path -LiteralPath $tamperPlugins) "tampered package writes nothing"

    $invalidTarget = Join-Path $testRoot "NotAPluginsDirectory"
    $invalidResult = Invoke-TestPowerShell $package.Installer @("-PluginDirectory", $invalidTarget, "-Confirm:`$false")
    Assert-True ($invalidResult.ExitCode -ne 0) "non-Plugins path fails"
    Assert-False (Test-Path -LiteralPath $invalidTarget) "non-Plugins path writes nothing"

    $programHdt = New-TestPortableHdt -Root $testRoot -Name "ProgramHdt"
    $programPlugins = Join-Path $programHdt "Plugins"
    $programResult = Invoke-TestPowerShell $package.Installer @("-PluginDirectory", $programPlugins, "-Confirm:`$false")
    Assert-True ($programResult.ExitCode -ne 0) "HDT program Plugins path fails"
    Assert-False (Test-Path -LiteralPath $programPlugins) "HDT program Plugins path writes nothing"

    $junctionTarget = Join-Path (New-TestPortableHdt -Root $testRoot -Name "JunctionTargetHdt") "Plugins"
    New-Item -ItemType Directory -Path $junctionTarget -Force | Out-Null
    $junctionPlugins = New-TestHdtPluginDirectory "JunctionPlugins"
    New-Item -ItemType Junction -Path $junctionPlugins -Target $junctionTarget | Out-Null
    $junctionResult = Invoke-TestPowerShell $package.Installer @(
        "-PluginDirectory", $junctionPlugins, "-Confirm:`$false"
    )
    Assert-True ($junctionResult.ExitCode -ne 0) "reparse Plugins path fails"
    Assert-False (Test-Path -LiteralPath (Join-Path $junctionTarget "BobCoach.dll")) "reparse target writes nothing"

    if ($FreshInstallOnly) {
        Write-Host "PASS offline installer fresh install, integrity, path, and WhatIf contracts"
        return
    }

    $upgradePlugins = New-TestHdtPluginDirectory "Upgrade"
    $upgradeAppData = $env:APPDATA
    New-Item -ItemType Directory -Path $upgradePlugins -Force | Out-Null
    $targetDll = Join-Path $upgradePlugins "BobCoach.dll"
    New-TestManagedBobCoach -Path $targetDll -Version "0.1.0.0" | Out-Null
    Write-PluginChecksumFile $targetDll (Join-Path $upgradePlugins "BobCoach.dll.sha256")
    $legacyHash = (Get-FileHash -LiteralPath $targetDll -Algorithm SHA256).Hash
    $legacyBytes = [IO.File]::ReadAllBytes($targetDll)

    $upgrade = Invoke-TestPowerShell $package.Installer @("-PluginDirectory", $upgradePlugins, "-Confirm:`$false")
    Assert-Equal 0 $upgrade.ExitCode "upgrade exit"
    Assert-FileHashEqual $package.Plugin $targetDll "upgrade target hash"
    Assert-PluginChecksum $targetDll (Join-Path $upgradePlugins "BobCoach.dll.sha256") "upgrade"
    $upgradeBackups = @(Get-ChildItem -LiteralPath $upgradePlugins -Filter "BobCoach.dll.backup-*" -File)
    Assert-Equal 1 $upgradeBackups.Count "upgrade backup count"
    $expectedBackupPattern = '^BobCoach\.dll\.backup-\d{8}T\d{9}Z-' + [regex]::Escape($legacyHash.Substring(0, 16)) + '$'
    Assert-True ($upgradeBackups[0].Name -match $expectedBackupPattern) "upgrade backup name"
    Assert-True ([Linq.Enumerable]::SequenceEqual([byte[]]$legacyBytes, [byte[]][IO.File]::ReadAllBytes($upgradeBackups[0].FullName))) "upgrade backup bytes"

    $checksumFailurePlugins = New-TestHdtPluginDirectory "ChecksumPublishFailure"
    New-Item -ItemType Directory -Path $checksumFailurePlugins -Force | Out-Null
    $checksumFailureDll = Join-Path $checksumFailurePlugins "BobCoach.dll"
    New-TestManagedBobCoach -Path $checksumFailureDll -Version "0.1.0.0" | Out-Null
    $checksumFailureLegacyHash = (Get-FileHash -LiteralPath $checksumFailureDll -Algorithm SHA256).Hash
    New-Item -ItemType Directory -Path (Join-Path $checksumFailurePlugins "BobCoach.dll.sha256") | Out-Null
    $checksumFailureResult = Invoke-TestPowerShell $package.Installer @(
        "-PluginDirectory", $checksumFailurePlugins, "-Confirm:`$false"
    )
    Assert-True ($checksumFailureResult.ExitCode -ne 0) "checksum publish failure exits nonzero"
    Assert-Equal $checksumFailureLegacyHash (Get-FileHash -LiteralPath $checksumFailureDll -Algorithm SHA256).Hash "checksum publish failure preserves previous DLL"

    $env:APPDATA = $upgradeAppData
    $rollback = Invoke-TestPowerShell $package.Installer @("-PluginDirectory", $upgradePlugins, "-Rollback", "-Confirm:`$false")
    Assert-Equal 0 $rollback.ExitCode "latest rollback exit; output=$(@($rollback.Output) -join ' | ')"
    Assert-Equal $legacyHash (Get-FileHash -LiteralPath $targetDll -Algorithm SHA256).Hash "latest rollback target hash"
    Assert-PluginChecksum $targetDll (Join-Path $upgradePlugins "BobCoach.dll.sha256") "latest rollback"
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
    Assert-PluginChecksum $targetDll (Join-Path $upgradePlugins "BobCoach.dll.sha256") "specified rollback"

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

    $emptyPlugins = New-TestHdtPluginDirectory "EmptyRollback"
    $emptyRollback = Invoke-TestPowerShell $package.Installer @("-PluginDirectory", $emptyPlugins, "-Rollback", "-Confirm:`$false")
    Assert-True ($emptyRollback.ExitCode -ne 0) "rollback without backup fails"
    Assert-False (Test-Path -LiteralPath $emptyPlugins) "empty rollback writes nothing"

    $runningHdt = New-TestPortableHdt -Root $testRoot -Name "RunningHdt"
    $runningPlugins = New-TestHdtPluginDirectory "Running"
    $runningProcess = Start-TestHdtProcess $runningHdt
    try {
        $runningResult = Invoke-TestPowerShell $package.Installer @("-PluginDirectory", $runningPlugins, "-Confirm:`$false")
        Assert-True ($runningResult.ExitCode -ne 0) "running HDT blocks AppData install"
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
        Stop-TestHdtProcess $runningProcess
    }

    Write-Host "PASS offline installer install, integrity, upgrade, rollback, and HDT process contracts"
} catch {
    Write-Host "FAIL offline installer fresh install contracts"
    Write-Host $_.Exception.Message
    exit 1
} finally {
    $env:APPDATA = $previousAppData
    if (Test-Path -LiteralPath $testRoot) {
        Assert-SafeTestRoot $testRoot
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
