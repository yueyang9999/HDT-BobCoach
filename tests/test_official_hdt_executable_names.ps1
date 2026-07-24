[CmdletBinding()]
param(
    [switch]$Preserve,
    [ValidateSet("All", "WhatIf", "ReadOnlyFresh", "ReadOnlyUpgrade", "ReadOnlyRollback", "ReplaceFailure")]
    [string]$Scenario = "All"
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "offline_package_test_helpers.ps1")

$testRoot = Join-Path $env:TEMP ("bobcoach-official-hdt-name-test-" + [Guid]::NewGuid().ToString("N"))
$previousAppData = $env:APPDATA
Assert-SafeTestRoot $testRoot

function New-TestHdtPluginDirectory([string]$Name) {
    $appData = Join-Path (Join-Path $testRoot $Name) "AppData"
    $hdtAppData = Join-Path $appData "HearthstoneDeckTracker"
    New-Item -ItemType Directory -Path $hdtAppData -Force | Out-Null
    $env:APPDATA = $appData
    return Join-Path $hdtAppData "Plugins"
}

function Set-TestReadOnly([string]$Path) {
    $item = Get-Item -LiteralPath $Path -Force
    [IO.File]::SetAttributes($item.FullName, $item.Attributes -bor [IO.FileAttributes]::ReadOnly)
}

function Get-TestFileSnapshot([string]$Path) {
    $item = Get-Item -LiteralPath $Path -Force
    return [pscustomobject]@{
        Length = $item.Length
        Hash = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash
        Attributes = [int]$item.Attributes
    }
}

function Assert-TestFileSnapshot($Expected, [string]$Path, [string]$Label) {
    $actual = Get-TestFileSnapshot $Path
    Assert-Equal $Expected.Length $actual.Length "$Label length"
    Assert-Equal $Expected.Hash $actual.Hash "$Label hash"
    Assert-Equal $Expected.Attributes $actual.Attributes "$Label attributes"
}

function Assert-NoInstallingDll([string]$Plugins, [string]$Label) {
    if (!(Test-Path -LiteralPath $Plugins -PathType Container)) { return }
    Assert-Equal 0 @(Get-ChildItem -LiteralPath $Plugins -Filter "BobCoach.dll.installing-*" -File).Count $Label
}

function Complete-SelectedScenario([string]$Name) {
    if ($Scenario -eq $Name) {
        Write-Host "PASS selected installer scenario: $Name"
        return $true
    }
    return $false
}

try {
    New-Item -ItemType Directory -Path $testRoot -Force | Out-Null

    if ($Scenario -in @("All", "WhatIf")) {
        $whatIfPackage = New-TestOfflinePackage -Root $testRoot -Name "WhatIfPackage"
        Set-TestReadOnly $whatIfPackage.Plugin
        $whatIfPlugins = New-TestHdtPluginDirectory "WhatIf"
        $packageSnapshots = @{}
        foreach ($file in Get-ChildItem -LiteralPath $whatIfPackage.Root -File) {
            $packageSnapshots[$file.Name] = Get-TestFileSnapshot $file.FullName
        }

        $tokens = $null
        $parseErrors = $null
        $installerAst = [Management.Automation.Language.Parser]::ParseFile(
            $whatIfPackage.Installer,
            [ref]$tokens,
            [ref]$parseErrors
        )
        Assert-Equal 0 @($parseErrors).Count "installer AST errors"
        $shaFunctions = @($installerAst.FindAll({
            param($node)
            $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
                $node.Name -eq "Get-Sha256"
        }, $true))
        Assert-Equal 1 $shaFunctions.Count "installer Get-Sha256 function count"
        $shaText = $shaFunctions[0].Extent.Text
        Assert-True ($shaText -match '\[IO\.File\]::OpenRead\(\$Path\)') `
            "Get-Sha256 opens a regular file stream"
        Assert-True ($shaText -match '\[Security\.Cryptography\.SHA256\]::Create\(\)') `
            "Get-Sha256 creates a SHA256 implementation"
        Assert-True ($shaText -match '\.ComputeHash\(\$stream\)') `
            "Get-Sha256 hashes the file stream"
        Assert-True ($shaText -match 'finally[\s\S]*\$sha256\.Dispose\(\)') `
            "Get-Sha256 disposes SHA256 in finally"
        Assert-True ($shaText -match 'finally[\s\S]*\$stream\.Dispose\(\)') `
            "Get-Sha256 disposes the file stream in finally"

        $whatIfResult = Invoke-TestPowerShell $whatIfPackage.Installer @(
            "-PluginDirectory", $whatIfPlugins, "-WhatIf", "-Confirm:`$false"
        )
        Assert-Equal 0 $whatIfResult.ExitCode "install WhatIf exit"
        Assert-False (Test-Path -LiteralPath $whatIfPlugins) "install WhatIf creates no Plugins directory"
        foreach ($fileName in $packageSnapshots.Keys) {
            Assert-TestFileSnapshot $packageSnapshots[$fileName] `
                (Join-Path $whatIfPackage.Root $fileName) "install WhatIf package $fileName"
        }
        Assert-NoInstallingDll $whatIfPlugins "install WhatIf temp count"

        $tamperedPackage = New-TestOfflinePackage -Root $testRoot -Name "TamperedWhatIfPackage"
        [IO.File]::AppendAllText(
            (Join-Path $tamperedPackage.Root "README_OFFLINE.md"),
            "tampered",
            (New-Object Text.UTF8Encoding($false))
        )
        $tamperedPlugins = New-TestHdtPluginDirectory "TamperedWhatIf"
        $tamperedResult = Invoke-TestPowerShell $tamperedPackage.Installer @(
            "-PluginDirectory", $tamperedPlugins, "-WhatIf", "-Confirm:`$false"
        )
        Assert-True ($tamperedResult.ExitCode -ne 0) "tampered package fails under install WhatIf"
        Assert-True ((@($tamperedResult.Output) -join "`n") -match
            'Package hash mismatch: README_OFFLINE\.md') "tampered package reports hash mismatch"
        Assert-False (Test-Path -LiteralPath $tamperedPlugins) `
            "tampered install WhatIf creates no Plugins directory"
        Assert-NoInstallingDll $tamperedPlugins "tampered install WhatIf temp count"
        if (Complete-SelectedScenario "WhatIf") { return }
    }

    if ($Scenario -in @("All", "ReadOnlyFresh")) {
        $readOnlyPackage = New-TestOfflinePackage -Root $testRoot -Name "ReadOnlyFreshPackage"
        Set-TestReadOnly $readOnlyPackage.Plugin
        $sourceBefore = Get-TestFileSnapshot $readOnlyPackage.Plugin
        $readOnlyPlugins = New-TestHdtPluginDirectory "ReadOnlyFresh"

        $readOnlyFresh = Invoke-TestPowerShell $readOnlyPackage.Installer @(
            "-PluginDirectory", $readOnlyPlugins, "-Confirm:`$false"
        )
        Assert-Equal 0 $readOnlyFresh.ExitCode "read-only source fresh install exit"
        $readOnlyTarget = Join-Path $readOnlyPlugins "BobCoach.dll"
        Assert-FileHashEqual $readOnlyPackage.Plugin $readOnlyTarget "read-only source fresh target hash"
        Assert-False ((Get-Item -LiteralPath $readOnlyTarget -Force).IsReadOnly) "fresh target is writable"
        Assert-TestFileSnapshot $sourceBefore $readOnlyPackage.Plugin "read-only package source preserved"
        Assert-Equal 0 @(Get-ChildItem -LiteralPath $readOnlyPlugins -Filter "BobCoach.dll.backup-*" -File).Count `
            "read-only fresh backup count"
        Assert-NoInstallingDll $readOnlyPlugins "read-only fresh temp count"
        if (Complete-SelectedScenario "ReadOnlyFresh") { return }
    }

    if ($Scenario -in @("All", "ReadOnlyUpgrade")) {
        $upgradePackage = New-TestOfflinePackage -Root $testRoot -Name "ReadOnlyUpgradePackage"
        $upgradePlugins = New-TestHdtPluginDirectory "ReadOnlyUpgrade"
        New-Item -ItemType Directory -Path $upgradePlugins | Out-Null
        $upgradeTarget = Join-Path $upgradePlugins "BobCoach.dll"
        New-TestManagedBobCoach -Path $upgradeTarget -Version "0.1.0.0" | Out-Null
        $legacyBytes = [IO.File]::ReadAllBytes($upgradeTarget)
        Set-TestReadOnly $upgradeTarget

        $upgradeResult = Invoke-TestPowerShell $upgradePackage.Installer @(
            "-PluginDirectory", $upgradePlugins, "-Confirm:`$false"
        )
        if ($upgradeResult.ExitCode -ne 0) {
            $upgradeResult.Output | ForEach-Object { Write-Host $_.ToString() }
        }
        Assert-Equal 0 $upgradeResult.ExitCode "read-only legacy target upgrade exit"
        Assert-FileHashEqual $upgradePackage.Plugin $upgradeTarget "read-only upgrade target hash"
        Assert-False ((Get-Item -LiteralPath $upgradeTarget -Force).IsReadOnly) "upgraded target is writable"
        $upgradeBackups = @(Get-ChildItem -LiteralPath $upgradePlugins -Filter "BobCoach.dll.backup-*" -File)
        Assert-Equal 1 $upgradeBackups.Count "read-only upgrade backup count"
        Assert-True ([Linq.Enumerable]::SequenceEqual(
            [byte[]]$legacyBytes,
            [byte[]][IO.File]::ReadAllBytes($upgradeBackups[0].FullName)
        )) "read-only upgrade backup bytes"
        Assert-NoInstallingDll $upgradePlugins "read-only upgrade temp count"
        if (Complete-SelectedScenario "ReadOnlyUpgrade") { return }
    }

    if ($Scenario -in @("All", "ReadOnlyRollback")) {
        $rollbackPackage = New-TestOfflinePackage -Root $testRoot -Name "ReadOnlyRollbackPackage"
        $rollbackPlugins = New-TestHdtPluginDirectory "ReadOnlyRollback"
        New-Item -ItemType Directory -Path $rollbackPlugins | Out-Null
        $rollbackTarget = Join-Path $rollbackPlugins "BobCoach.dll"
        Copy-Item -LiteralPath $rollbackPackage.Plugin -Destination $rollbackTarget
        $rollbackTargetBefore = Get-TestFileSnapshot $rollbackTarget

        $seedRoot = Join-Path $testRoot "ReadOnlyRollbackSeed"
        New-Item -ItemType Directory -Path $seedRoot | Out-Null
        $seed = Join-Path $seedRoot "BobCoach.dll"
        New-TestManagedBobCoach -Path $seed -Version "0.1.0.0" | Out-Null
        $seedHash = (Get-FileHash -LiteralPath $seed -Algorithm SHA256).Hash
        $sourceBackup = Join-Path ($rollbackPlugins + "\.") `
            ("BobCoach.dll.backup-20000101T000000000Z-" + $seedHash.Substring(0, 16))
        Move-Item -LiteralPath $seed -Destination $sourceBackup
        Set-TestReadOnly $sourceBackup
        $sourceBackupBefore = Get-TestFileSnapshot $sourceBackup

        $rollbackResult = Invoke-TestPowerShell $rollbackPackage.Installer @(
            "-PluginDirectory", $rollbackPlugins, "-Rollback", "-BackupPath", $sourceBackup, "-Confirm:`$false"
        )
        if ($rollbackResult.ExitCode -ne 0) {
            $rollbackResult.Output | ForEach-Object { Write-Host $_.ToString() }
        }
        Assert-Equal 0 $rollbackResult.ExitCode "read-only source backup rollback exit"
        Assert-Equal $seedHash (Get-FileHash -LiteralPath $rollbackTarget -Algorithm SHA256).Hash `
            "read-only rollback target hash"
        Assert-False ((Get-Item -LiteralPath $rollbackTarget -Force).IsReadOnly) "rollback target is writable"
        Assert-TestFileSnapshot $sourceBackupBefore $sourceBackup "read-only rollback source preserved"
        $rollbackBackups = @(Get-ChildItem -LiteralPath $rollbackPlugins -Filter "BobCoach.dll.backup-*" -File)
        Assert-Equal 2 $rollbackBackups.Count "read-only rollback backup count"
        $sourceBackupName = [IO.Path]::GetFileName($sourceBackup)
        $currentTargetBackups = @($rollbackBackups | Where-Object { $_.Name -ne $sourceBackupName })
        Assert-Equal 1 $currentTargetBackups.Count "read-only rollback current target backup count"
        $currentTargetBackup = Get-TestFileSnapshot $currentTargetBackups[0].FullName
        Assert-Equal $rollbackTargetBefore.Length $currentTargetBackup.Length `
            "read-only rollback current target backup length"
        Assert-Equal $rollbackTargetBefore.Hash $currentTargetBackup.Hash `
            "read-only rollback current target backup hash"
        Assert-NoInstallingDll $rollbackPlugins "read-only rollback temp count"
        if (Complete-SelectedScenario "ReadOnlyRollback") { return }
    }

    if ($Scenario -in @("All", "ReplaceFailure")) {
        $failurePackage = New-TestOfflinePackage -Root $testRoot -Name "ReplaceFailurePackage"
        $failurePlugins = New-TestHdtPluginDirectory "ReplaceFailure"
        New-Item -ItemType Directory -Path $failurePlugins | Out-Null
        $failureTarget = Join-Path $failurePlugins "BobCoach.dll"
        Copy-Item -LiteralPath $failurePackage.Plugin -Destination $failureTarget
        Set-TestReadOnly $failureTarget
        $failureTargetBefore = Get-TestFileSnapshot $failureTarget
        $failureTemp = Join-Path $failurePlugins `
            ("BobCoach.dll.installing-" + [Guid]::NewGuid().ToString("N"))
        Copy-Item -LiteralPath $failurePackage.Plugin -Destination $failureTemp
        $blockedBackup = Join-Path $failurePlugins "BobCoach.dll.backup-blocked"
        New-Item -ItemType Directory -Path $blockedBackup | Out-Null

        $tokens = $null
        $parseErrors = $null
        $installerAst = [Management.Automation.Language.Parser]::ParseFile(
            $failurePackage.Installer,
            [ref]$tokens,
            [ref]$parseErrors
        )
        Assert-Equal 0 @($parseErrors).Count "replace failure installer AST errors"
        $replaceFunctions = @($installerAst.FindAll({
            param($node)
            $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
                $node.Name -in @("Clear-ReadOnlyAttribute", "Replace-BobCoachDll")
        }, $true))
        Assert-Equal 2 $replaceFunctions.Count "replace failure helper count"
        foreach ($functionAst in $replaceFunctions) {
            Invoke-Expression $functionAst.Extent.Text
        }
        $productionReplaceCalls = @($installerAst.FindAll({
            param($node)
            $node -is [Management.Automation.Language.CommandAst] -and
                $node.GetCommandName() -eq "Replace-BobCoachDll"
        }, $true))
        Assert-Equal 4 $productionReplaceCalls.Count "production replace call count"
        $publishReplaceCalls = @($productionReplaceCalls | Where-Object {
            $_.Extent.Text -match 'Replace-BobCoachDll\s+\$tempDll\s+\$targetDll'
        })
        $compensationReplaceCalls = @($productionReplaceCalls | Where-Object {
            $_.Extent.Text -match 'Replace-BobCoachDll\s+\$(currentBackup|backupPathForUpgrade)\s+\$targetDll\s+\$failedCandidate'
        })
        Assert-Equal 2 $publishReplaceCalls.Count "production publish replace call count"
        Assert-Equal 2 $compensationReplaceCalls.Count "production compensation replace call count"
        foreach ($replaceCall in $publishReplaceCalls) {
            $tryAncestor = $replaceCall.Parent
            while ($null -ne $tryAncestor -and
                $tryAncestor -isnot [Management.Automation.Language.TryStatementAst]) {
                $tryAncestor = $tryAncestor.Parent
            }
            Assert-True ($null -ne $tryAncestor) "production publish replace call has try ancestor"
            Assert-True ($null -ne $tryAncestor.Finally) "production publish replace call has finally"
            Assert-True ($tryAncestor.Finally.Extent.Text -match
                'Remove-Item\s+-LiteralPath\s+\$tempDll\s+-Force') `
                "production publish replace call finally cleans temp DLL"
            Assert-True ($tryAncestor.Finally.Extent.Text -match
                'Remove-Item\s+-LiteralPath\s+\$tempChecksum\s+-Force') `
                "production publish replace call finally cleans temp checksum"
        }
        foreach ($replaceCall in $compensationReplaceCalls) {
            $tryAncestor = $replaceCall.Parent
            while ($null -ne $tryAncestor -and
                $tryAncestor -isnot [Management.Automation.Language.TryStatementAst]) {
                $tryAncestor = $tryAncestor.Parent
            }
            Assert-True ($null -ne $tryAncestor) "production compensation replace call has try ancestor"
            Assert-True ($null -ne $tryAncestor.Finally) "production compensation replace call has finally"
            Assert-True ($tryAncestor.Finally.Extent.Text -match
                'Remove-Item\s+-LiteralPath\s+\$failedCandidate\s+-Force') `
                "production compensation replace call finally cleans failed candidate"
        }

        $replaceError = $null
        try {
            Replace-BobCoachDll $failureTemp $failureTarget $blockedBackup
        } catch {
            $replaceError = $_
            Write-Host "EXPECTED replace failure: $($_.Exception.Message)"
        } finally {
            if (Test-Path -LiteralPath $failureTemp -PathType Leaf) {
                Assert-SafeTestRoot $testRoot
                Remove-Item -LiteralPath $failureTemp -Force
            }
        }
        Assert-True ($null -ne $replaceError) "replace failure was triggered"
        Assert-TestFileSnapshot $failureTargetBefore $failureTarget "replace failure target restored"
        Assert-True (Test-Path -LiteralPath $blockedBackup -PathType Container) `
            "replace failure blocked backup directory preserved"
        Assert-NoInstallingDll $failurePlugins "replace failure temp count"
        if (Complete-SelectedScenario "ReplaceFailure") { return }
    }

    $package = New-TestOfflinePackage -Root $testRoot -Name "Package"
    $plugins = New-TestHdtPluginDirectory "Main"

    $install = Invoke-TestPowerShell $package.Installer @("-PluginDirectory", $plugins, "-Confirm:`$false")
    Assert-Equal 0 $install.ExitCode "AppData plugin directory install exit"
    Assert-FileHashEqual $package.Plugin (Join-Path $plugins "BobCoach.dll") "AppData plugin directory install hash"

    $programHdt = New-TestPortableHdt -Root $testRoot -Name "ProgramHdt" `
        -ExecutableName "Hearthstone Deck Tracker.exe"
    $programPlugins = Join-Path $programHdt "Plugins"
    $wrongProgramInstall = Invoke-TestPowerShell $package.Installer @(
        "-PluginDirectory", $programPlugins, "-Confirm:`$false"
    )
    Assert-True ($wrongProgramInstall.ExitCode -ne 0) "HDT program Plugins install fails"
    Assert-False (Test-Path -LiteralPath $programPlugins) "HDT program Plugins install writes nothing"

    $uninstaller = Join-Path (Split-Path -Parent $PSScriptRoot) "tools\release\UNINSTALL.ps1"
    New-Item -ItemType Directory -Path $programPlugins -Force | Out-Null
    Copy-Item -LiteralPath $package.Plugin -Destination (Join-Path $programPlugins "BobCoach.dll")
    $wrongProgramUninstall = Invoke-TestPowerShell $uninstaller @(
        "-PluginDirectory", $programPlugins, "-WhatIf", "-Confirm:`$false"
    )
    Assert-True ($wrongProgramUninstall.ExitCode -ne 0) "HDT program Plugins uninstall fails"
    Assert-True (Test-Path -LiteralPath (Join-Path $programPlugins "BobCoach.dll") -PathType Leaf) `
        "HDT program Plugins uninstall writes nothing"

    $plugins = New-TestHdtPluginDirectory "Main"
    $uninstallWhatIf = Invoke-TestPowerShell $uninstaller @(
        "-PluginDirectory", $plugins, "-WhatIf", "-Confirm:`$false"
    )
    Assert-Equal 0 $uninstallWhatIf.ExitCode "AppData uninstall WhatIf exit"
    Assert-True (Test-Path -LiteralPath (Join-Path $plugins "BobCoach.dll") -PathType Leaf) "uninstall WhatIf preserves DLL"

    $runningHdt = New-TestPortableHdt -Root $testRoot -Name "RunningOfficialHdt" -ExecutableName "Hearthstone Deck Tracker.exe"
    $runningPlugins = New-TestHdtPluginDirectory "Running"
    $runningProcess = Start-TestHdtProcess -HdtRoot $runningHdt -ExecutableName "Hearthstone Deck Tracker.exe"
    try {
        $runningInstall = Invoke-TestPowerShell $package.Installer @(
            "-PluginDirectory", $runningPlugins, "-Confirm:`$false"
        )
        Assert-True ($runningInstall.ExitCode -ne 0) "running official HDT blocks install"
        Assert-False (Test-Path -LiteralPath $runningPlugins) "running official HDT writes nothing"

        New-Item -ItemType Directory -Path $runningPlugins -Force | Out-Null
        Copy-Item -LiteralPath $package.Plugin -Destination (Join-Path $runningPlugins "BobCoach.dll")
        $runningUninstall = Invoke-TestPowerShell $uninstaller @(
            "-PluginDirectory", $runningPlugins, "-WhatIf", "-Confirm:`$false"
        )
        Assert-True ($runningUninstall.ExitCode -ne 0) "running official HDT blocks uninstall"
        Assert-True (Test-Path -LiteralPath (Join-Path $runningPlugins "BobCoach.dll") -PathType Leaf) "running official HDT uninstall writes nothing"
    } finally {
        Stop-TestHdtProcess $runningProcess
    }

    Write-Host "PASS installer WhatIf, readonly lifecycle, failure recovery, AppData path, and process contracts"
} catch {
    Write-Host "FAIL official HDT executable name install contract"
    Write-Host $_.Exception.Message
    exit 1
} finally {
    $env:APPDATA = $previousAppData
    if ($Preserve) {
        Write-Host "PRESERVED test root: $testRoot"
    } elseif (Test-Path -LiteralPath $testRoot) {
        Assert-SafeTestRoot $testRoot
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
