$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "offline_package_test_helpers.ps1")

$repoRoot = Split-Path -Parent $PSScriptRoot
$deployScript = Join-Path $repoRoot "tools\release\deploy_local_release.ps1"
$testRoot = Join-Path $env:TEMP ("bobcoach-local-deploy-archive-test-" + [Guid]::NewGuid().ToString("N"))
$previousAppData = $env:APPDATA
$assertions = 0
Assert-SafeTestRoot $testRoot

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
                if ([Reflection.AssemblyName]::GetAssemblyName($executable).Version.ToString() -eq "1.55.0.0") {
                    $candidate
                }
            } catch { }
        }
    } | Select-Object -First 1)
}

function Assert-TestTrue([bool]$Condition, [string]$Label) {
    $script:assertions++
    Assert-True $Condition $Label
}

function Assert-TestEqual($Expected, $Actual, [string]$Label) {
    $script:assertions++
    Assert-Equal $Expected $Actual $Label
}

function Assert-TestFalse([bool]$Condition, [string]$Label) {
    $script:assertions++
    Assert-False $Condition $Label
}

function Invoke-Deploy([string[]]$Arguments) {
    $previousErrorAction = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $deployScript @Arguments 2>&1
        $exitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previousErrorAction
    }
    return [pscustomobject]@{
        ExitCode = $exitCode
        Output = @($output)
    }
}

function New-TestPluginDirectory([string]$Root) {
    $appData = Join-Path $Root "AppData"
    $hdtAppData = Join-Path $appData "HearthstoneDeckTracker"
    $plugins = Join-Path $hdtAppData "Plugins"
    New-Item -ItemType Directory -Path $plugins -Force | Out-Null
    $env:APPDATA = $appData
    return [IO.Path]::GetFullPath($plugins)
}

function Write-OldPluginSnapshot([string]$PluginDirectory, [string]$OldDllPath) {
    $dll = Join-Path $PluginDirectory "BobCoach.dll"
    $sidecar = Join-Path $PluginDirectory "BobCoach.dll.sha256"
    Copy-Item -LiteralPath $OldDllPath -Destination $dll -Force
    Write-PluginChecksumFile $dll $sidecar

    $backupDll = Join-Path $PluginDirectory "BobCoach.dll.backup-20260101T000000001Z-abcdef0123456789"
    $backupSidecar = Join-Path $PluginDirectory "BobCoach.dll.sha256.backup-20260101T000000002Z-abcdef0123456789"
    Copy-Item -LiteralPath $OldDllPath -Destination $backupDll -Force
    Write-PluginChecksumFile $backupDll $backupSidecar
    return @($dll, $sidecar, $backupDll, $backupSidecar)
}

function Assert-ArchiveManifestValid([string]$ArchiveDirectory) {
    $manifestPath = Join-Path $ArchiveDirectory "manifest.json"
    Assert-TestTrue (Test-Path -LiteralPath $manifestPath -PathType Leaf) "archive manifest exists"
    $manifest = Get-Content -Raw -Encoding UTF8 -LiteralPath $manifestPath | ConvertFrom-Json
    Assert-TestEqual 1 ([int]$manifest.schemaVersion) "archive manifest schemaVersion"
    Assert-TestTrue ([string]::IsNullOrWhiteSpace([string]$manifest.commit) -eq $false) "archive manifest commit"
    Assert-TestTrue ([string]::IsNullOrWhiteSpace([string]$manifest.capturedAtUtc) -eq $false) "archive manifest capturedAtUtc"
    Assert-TestTrue (@($manifest.entries).Count -gt 0) "archive manifest entries"

    foreach ($entry in @($manifest.entries)) {
        $relativePath = [string]$entry.relativePath
        Assert-TestTrue (![string]::IsNullOrWhiteSpace($relativePath)) "archive entry relativePath"
        $entryPath = Join-Path $ArchiveDirectory ($relativePath.Replace('/', [IO.Path]::DirectorySeparatorChar))
        Assert-TestTrue (Test-Path -LiteralPath $entryPath -PathType Leaf) "archive entry file $relativePath"
        $actualSize = (Get-Item -LiteralPath $entryPath).Length
        $actualHash = (Get-FileHash -LiteralPath $entryPath -Algorithm SHA256).Hash
        Assert-TestEqual ([long]$entry.size) $actualSize "archive entry size $relativePath"
        Assert-TestEqual ([string]$entry.sha256) $actualHash "archive entry hash $relativePath"
    }
    return $manifest
}

try {
    New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
    $hdtDirectory = Resolve-HdtDirectory
    if ([string]::IsNullOrWhiteSpace($hdtDirectory)) {
        throw "HDT 1.55.0.0 not found; set BOBCOACH_HDT_DIR"
    }
    $candidateDll = $env:BOBCOACH_TEST_DLL
    if ([string]::IsNullOrWhiteSpace($candidateDll) -or !(Test-Path -LiteralPath $candidateDll -PathType Leaf)) {
        throw "BOBCOACH_TEST_DLL must point to a built BobCoach.dll"
    }
    $candidateDll = [IO.Path]::GetFullPath($candidateDll)
    $archiveRoot = Join-Path $testRoot "Archive"
    $appDataRoot = Join-Path $testRoot "AppDataRoot"

    $plugins = New-TestPluginDirectory $appDataRoot
    $missingDll = Join-Path $testRoot "missing\BobCoach.dll"
    $invalidCandidate = Invoke-Deploy @(
        "-HdtDirectory", $hdtDirectory,
        "-PluginDirectory", $plugins,
        "-ArchiveRoot", $archiveRoot,
        "-ReleaseDllPath", $missingDll
    )
    Assert-TestTrue ($invalidCandidate.ExitCode -ne 0) "missing candidate DLL is rejected"
    Assert-TestFalse (Test-Path -LiteralPath $archiveRoot) "missing candidate writes no archive"

    $programPlugins = Join-Path $testRoot "HdtProgram\Plugins"
    New-Item -ItemType Directory -Path $programPlugins -Force | Out-Null
    $wrongDirectory = Invoke-Deploy @(
        "-HdtDirectory", $hdtDirectory,
        "-PluginDirectory", $programPlugins,
        "-ArchiveRoot", $archiveRoot,
        "-ReleaseDllPath", $candidateDll
    )
    Assert-TestTrue ($wrongDirectory.ExitCode -ne 0) "non-AppData Plugins directory is rejected"
    Assert-TestFalse (Test-Path -LiteralPath $archiveRoot) "wrong directory writes no archive"

    $plugins = New-TestPluginDirectory $appDataRoot
    $hdtProcessRoot = Join-Path $testRoot "RunningHdt"
    $runningHdt = New-TestPortableHdt -Root $hdtProcessRoot -Name "PortableHdt" `
        -ExecutableName "HearthstoneDeckTracker.exe"
    $hdtProcess = Start-TestHdtProcess $runningHdt
    try {
        $runningDeploy = Invoke-Deploy @(
            "-HdtDirectory", $hdtDirectory,
            "-PluginDirectory", $plugins,
            "-ArchiveRoot", $archiveRoot,
            "-ReleaseDllPath", $candidateDll
        )
        Assert-TestTrue ($runningDeploy.ExitCode -ne 0) "running HDT deploy is rejected"
        Assert-TestFalse (Test-Path -LiteralPath $archiveRoot) "running HDT writes no archive"
    } finally {
        Stop-TestHdtProcess $hdtProcess
    }

    $oldDllPath = Join-Path $testRoot "old\BobCoach.dll"
    New-Item -ItemType Directory -Path (Split-Path -Parent $oldDllPath) -Force | Out-Null
    New-TestManagedBobCoach $oldDllPath "0.1.0.0" | Out-Null
    $plugins = New-TestPluginDirectory $appDataRoot
    $snapshotFiles = Write-OldPluginSnapshot $plugins $oldDllPath
    $oldCurrentDll = Join-Path $plugins "BobCoach.dll"
    $oldCurrentHash = (Get-FileHash -LiteralPath $oldCurrentDll -Algorithm SHA256).Hash
    $oldBackupDll = Join-Path $plugins "BobCoach.dll.backup-20260101T000000001Z-abcdef0123456789"

    $successfulDeploy = Invoke-Deploy @(
        "-HdtDirectory", $hdtDirectory,
        "-PluginDirectory", $plugins,
        "-ArchiveRoot", $archiveRoot,
        "-ReleaseDllPath", $candidateDll
    )
    if ($successfulDeploy.ExitCode -ne 0) {
        Write-Host "first deploy output:"
        $successfulDeploy.Output | ForEach-Object { Write-Host $_ }
    }
    Assert-TestEqual 0 $successfulDeploy.ExitCode "successful deploy exit"
    Assert-FileHashEqual $candidateDll (Join-Path $plugins "BobCoach.dll") "deployed DLL hash"
    Assert-PluginChecksum (Join-Path $plugins "BobCoach.dll") (Join-Path $plugins "BobCoach.dll.sha256") "deployed sidecar"

    foreach ($snapshotPath in $snapshotFiles) {
        Assert-TestTrue (Test-Path -LiteralPath $snapshotPath -PathType Leaf) "historical plugin file retained $snapshotPath"
    }

    $archiveDirectories = @(Get-ChildItem -LiteralPath $archiveRoot -Directory -Force | Sort-Object Name)
    Assert-TestEqual 1 @($archiveDirectories).Count "first deploy archive count"
    $firstArchive = $archiveDirectories[0].FullName
    $firstManifest = Assert-ArchiveManifestValid $firstArchive
    $archiveEntries = @($firstManifest.entries)
    Assert-TestTrue (@($archiveEntries | Where-Object { $_.relativePath -eq "BobCoach.dll" -and $_.sourceKind -eq "current" }).Count -eq 1) "archive contains current DLL"
    Assert-TestTrue (@($archiveEntries | Where-Object { $_.relativePath -like "BobCoach.dll.backup-*" -and $_.sourceKind -eq "backup" }).Count -ge 1) "archive contains historical backup"
    Assert-TestTrue (@($archiveEntries | Where-Object { $_.sha256 -eq $oldCurrentHash }).Count -ge 1) "archive preserves old DLL hash"
    Assert-TestTrue (Test-Path -LiteralPath $oldBackupDll -PathType Leaf) "old plugin backup still present after deploy"

    $secondDeploy = Invoke-Deploy @(
        "-HdtDirectory", $hdtDirectory,
        "-PluginDirectory", $plugins,
        "-ArchiveRoot", $archiveRoot,
        "-ReleaseDllPath", $candidateDll
    )
    if ($secondDeploy.ExitCode -ne 0) {
        Write-Host "second deploy output:"
        $secondDeploy.Output | ForEach-Object { Write-Host $_ }
    }
    Assert-TestEqual 0 $secondDeploy.ExitCode "second deploy exit"
    Assert-TestEqual 2 @(Get-ChildItem -LiteralPath $archiveRoot -Directory -Force).Count "second archive does not overwrite"
    $firstManifestAfterSecond = Assert-ArchiveManifestValid $firstArchive
    Assert-TestEqual ([string]$firstManifest.capturedAtUtc) ([string]$firstManifestAfterSecond.capturedAtUtc) "first archive manifest unchanged"
    Assert-PluginChecksum (Join-Path $plugins "BobCoach.dll") (Join-Path $plugins "BobCoach.dll.sha256") "second deployed sidecar"

    Write-Host "PASS local deploy and DLL archive contract assertions=$assertions"
} catch {
    Write-Host "FAIL local deploy and DLL archive contract"
    Write-Host $_.Exception.Message
    exit 1
} finally {
    $env:APPDATA = $previousAppData
    if (Test-Path -LiteralPath $testRoot) {
        Assert-SafeTestRoot $testRoot
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
