[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = "High")]
param(
    [string]$PluginDirectory,
    [switch]$Rollback,
    [string]$BackupPath
)

$ErrorActionPreference = "Stop"

$PackageFiles = @(
    "BobCoach.dll",
    "BobCoach.dll.sha256",
    "安装教程.html",
    "images/install/install-01-exit-hdt.png",
    "images/install/install-02-open-plugins-folder.png",
    "images/install/install-03-copy-bobcoach-dll.png",
    "images/install/install-04-enable-bobcoach.png",
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
$HashedFiles = @($PackageFiles | Where-Object { $_ -ne "SHA256SUMS.txt" } | Sort-Object)

function Get-Sha256([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    try {
        $sha256 = [Security.Cryptography.SHA256]::Create()
        try {
            return [BitConverter]::ToString($sha256.ComputeHash($stream)).Replace("-", "")
        } finally {
            $sha256.Dispose()
        }
    } finally {
        $stream.Dispose()
    }
}

function Assert-ExactSet([string[]]$Expected, [string[]]$Actual, [string]$Label) {
    $expectedSorted = @($Expected | Sort-Object)
    $actualSorted = @($Actual | Sort-Object)
    $delta = @(Compare-Object -ReferenceObject $expectedSorted -DifferenceObject $actualSorted)
    if ($delta.Count -ne 0) {
        $details = @($delta | ForEach-Object { "{0}:{1}" -f $_.SideIndicator, $_.InputObject }) -join ", "
        throw "$Label mismatch: $details"
    }
}

function Get-AssemblyAttributeValue($Assembly, [string]$AttributeTypeName) {
    $attribute = $Assembly.GetCustomAttributesData() |
        Where-Object { $_.AttributeType.FullName -eq $AttributeTypeName } |
        Select-Object -First 1
    if ($null -eq $attribute -or $attribute.ConstructorArguments.Count -ne 1) {
        throw "Missing assembly attribute: $AttributeTypeName"
    }
    return [string]$attribute.ConstructorArguments[0].Value
}

function Get-PluginAssemblyFacts([string]$Path) {
    $assemblyName = [Reflection.AssemblyName]::GetAssemblyName($Path)
    $bytes = [IO.File]::ReadAllBytes($Path)
    $assembly = [Reflection.Assembly]::ReflectionOnlyLoad($bytes)
    $peKind = [Reflection.PortableExecutableKinds]::ILOnly
    $machine = [Reflection.ImageFileMachine]::I386
    $assembly.ManifestModule.GetPEKind([ref]$peKind, [ref]$machine)
    return [pscustomobject]@{
        Name = $assemblyName.Name
        AssemblyVersion = $assemblyName.Version.ToString()
        FileVersion = Get-AssemblyAttributeValue $assembly "System.Reflection.AssemblyFileVersionAttribute"
        InformationalVersion = Get-AssemblyAttributeValue $assembly "System.Reflection.AssemblyInformationalVersionAttribute"
        TargetFramework = Get-AssemblyAttributeValue $assembly "System.Runtime.Versioning.TargetFrameworkAttribute"
        Machine = $machine
        PEKind = $peKind
    }
}

function Read-StrictSha256Sums([string]$Path) {
    $records = @{}
    $lines = @(Get-Content -LiteralPath $Path -Encoding UTF8)
    foreach ($line in $lines) {
        if ($line -notmatch '^([A-F0-9]{64})  ([^\\:*?"<>|\r\n]+)$') {
            throw "Invalid SHA256SUMS line: $line"
        }
        $hash = $Matches[1]
        $fileName = $Matches[2]
        if ($fileName -eq "." -or $fileName -eq ".." -or $records.ContainsKey($fileName)) {
            throw "Invalid or duplicate SHA256SUMS file: $fileName"
        }
        $records[$fileName] = $hash
    }
    return $records
}

function Assert-PackageIntegrity {
    $packageRoot = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')
    $prefixLength = $packageRoot.Length + 1
    $expectedDirectorySet = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    foreach ($packageFile in $PackageFiles) {
        $directory = [IO.Path]::GetDirectoryName($packageFile.Replace('/', '\'))
        while (![string]::IsNullOrWhiteSpace($directory)) {
            [void]$expectedDirectorySet.Add($directory.Replace('\', '/'))
            $directory = [IO.Path]::GetDirectoryName($directory)
        }
    }
    $expectedDirectories = @($expectedDirectorySet)
    $actualEntries = @(Get-ChildItem -LiteralPath $PSScriptRoot -Recurse -Force)
    foreach ($entry in $actualEntries) {
        if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Package contains a reparse point: $($entry.FullName)"
        }
    }
    $actualFiles = @(
        $actualEntries | Where-Object { !$_.PSIsContainer } |
            ForEach-Object { $_.FullName.Substring($prefixLength).Replace('\', '/') }
    )
    $actualDirectories = @(
        $actualEntries | Where-Object { $_.PSIsContainer } |
            ForEach-Object { $_.FullName.Substring($prefixLength).Replace('\', '/') }
    )
    Assert-ExactSet $PackageFiles $actualFiles "Package files"
    Assert-ExactSet $expectedDirectories $actualDirectories "Package directories"

    $sumPath = Join-Path $PSScriptRoot "SHA256SUMS.txt"
    $records = Read-StrictSha256Sums $sumPath
    Assert-ExactSet $HashedFiles @($records.Keys) "SHA256SUMS records"
    foreach ($fileName in $HashedFiles) {
        $filePath = Join-Path $PSScriptRoot $fileName
        $actualHash = Get-Sha256 $filePath
        if ($actualHash -ne $records[$fileName]) {
            throw "Package hash mismatch: $fileName"
        }
    }

    $manifestPath = Join-Path $PSScriptRoot "manifest.json"
    $manifest = Get-Content -Raw -Encoding UTF8 -LiteralPath $manifestPath | ConvertFrom-Json
    $expectedManifestFields = @(
        "schemaVersion", "packageVersion", "assemblyVersion", "fileVersion",
        "informationalVersion", "targetFramework", "runtimeIdentifier",
        "hdtBaselineVersion", "pluginFile", "pluginChecksumFile", "pluginSize", "pluginSha256", "files"
    )
    Assert-ExactSet $expectedManifestFields @($manifest.PSObject.Properties.Name) "Manifest fields"
    if ([int]$manifest.schemaVersion -ne 2) { throw "Unsupported manifest schemaVersion: $($manifest.schemaVersion)" }
    if ([string]$manifest.packageVersion -ne "1.0.0") { throw "Package version mismatch" }
    if ([string]$manifest.assemblyVersion -ne "1.0.0.0") { throw "Assembly version contract mismatch" }
    if ([string]$manifest.fileVersion -ne "1.0.0.0") { throw "File version contract mismatch" }
    if ([string]$manifest.informationalVersion -ne "1.0.0") { throw "Informational version contract mismatch" }
    if ([string]$manifest.targetFramework -ne ".NETFramework,Version=v4.7.2") { throw "Target framework contract mismatch" }
    if ([string]$manifest.runtimeIdentifier -ne "win-x64") { throw "Runtime identifier contract mismatch" }
    if ([string]$manifest.hdtBaselineVersion -ne "1.53.5.7354") { throw "HDT baseline contract mismatch" }
    if ([string]$manifest.pluginFile -ne "BobCoach.dll") { throw "Plugin file contract mismatch" }
    if ([string]$manifest.pluginChecksumFile -ne "BobCoach.dll.sha256") { throw "Plugin checksum file contract mismatch" }
    if (@($manifest.files).Count -ne $PackageFiles.Count -or (@($manifest.files) -join "`n") -ne ($PackageFiles -join "`n")) {
        throw "Manifest file order mismatch"
    }

    $pluginPath = Join-Path $PSScriptRoot "BobCoach.dll"
    $pluginItem = Get-Item -LiteralPath $pluginPath
    $pluginHash = Get-Sha256 $pluginPath
    if ([long]$manifest.pluginSize -ne $pluginItem.Length) { throw "Plugin size mismatch" }
    if ([string]$manifest.pluginSha256 -ne $pluginHash) { throw "Plugin hash mismatch" }

    $pluginChecksumPath = Join-Path $PSScriptRoot "BobCoach.dll.sha256"
    $pluginChecksumLines = @(Get-Content -LiteralPath $pluginChecksumPath -Encoding UTF8)
    if ($pluginChecksumLines.Count -ne 1 -or $pluginChecksumLines[0] -notmatch '^([A-F0-9]{64})  BobCoach\.dll$') {
        throw "Invalid BobCoach.dll.sha256 format"
    }
    if ($Matches[1] -ne $pluginHash) { throw "Plugin checksum sidecar mismatch" }

    $facts = Get-PluginAssemblyFacts $pluginPath
    if ($facts.Name -ne "BobCoach") { throw "Unexpected plugin assembly name: $($facts.Name)" }
    if ($facts.AssemblyVersion -ne [string]$manifest.assemblyVersion) { throw "Plugin AssemblyVersion mismatch" }
    if ($facts.FileVersion -ne [string]$manifest.fileVersion) { throw "Plugin AssemblyFileVersion mismatch" }
    if ($facts.InformationalVersion -ne [string]$manifest.informationalVersion) { throw "Plugin informational version mismatch" }
    if ($facts.TargetFramework -ne [string]$manifest.targetFramework) { throw "Plugin target framework mismatch" }
    if ($facts.Machine -ne [Reflection.ImageFileMachine]::AMD64 -or
        ($facts.PEKind -band [Reflection.PortableExecutableKinds]::PE32Plus) -eq 0 -or
        ($facts.PEKind -band [Reflection.PortableExecutableKinds]::ILOnly) -eq 0) {
        throw "Plugin architecture mismatch: machine=$($facts.Machine) peKind=$($facts.PEKind)"
    }

    return [pscustomobject]@{
        PluginPath = $pluginPath
        PluginChecksumPath = $pluginChecksumPath
        PluginHash = $pluginHash
        PackageVersion = [string]$manifest.packageVersion
    }
}

function Assert-NoReparseInExistingChain([string]$Path, [string]$Label) {
    $current = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    while (![string]::IsNullOrWhiteSpace($current)) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force -ErrorAction Stop
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "$Label contains a reparse point: $current"
            }
        }
        $parent = [IO.Path]::GetDirectoryName($current)
        if ([string]::IsNullOrWhiteSpace($parent) -or
            $parent.Equals($current, [StringComparison]::OrdinalIgnoreCase)) {
            break
        }
        $current = $parent.TrimEnd('\')
    }
}

function Resolve-PluginDirectory([string]$RequestedPath) {
    if ([string]::IsNullOrWhiteSpace($env:APPDATA)) { throw "APPDATA is not available" }
    $defaultParent = [IO.Path]::GetFullPath((Join-Path $env:APPDATA "HearthstoneDeckTracker")).TrimEnd('\')
    $defaultPlugins = [IO.Path]::GetFullPath((Join-Path $defaultParent "Plugins")).TrimEnd('\')
    $resolved = if ([string]::IsNullOrWhiteSpace($RequestedPath)) { $defaultPlugins } else { [IO.Path]::GetFullPath($RequestedPath).TrimEnd('\') }
    if ([IO.Path]::GetFileName($resolved) -ne "Plugins") {
        throw "PluginDirectory must end with Plugins: $resolved"
    }
    if (!$resolved.Equals($defaultPlugins, [StringComparison]::OrdinalIgnoreCase)) {
        throw "PluginDirectory must be the HDT AppData plugin directory: $defaultPlugins"
    }

    $parent = Split-Path -Parent $resolved
    if (!(Test-Path -LiteralPath $parent -PathType Container)) {
        throw "PluginDirectory parent does not exist: $parent"
    }
    Assert-NoReparseInExistingChain $resolved "PluginDirectory"
    return [pscustomobject]@{
        Path = $resolved
        Parent = $parent
    }
}

function Assert-HdtStopped($ResolvedPluginDirectory) {
    $processes = @(Get-Process -Name "HearthstoneDeckTracker", "Hearthstone Deck Tracker" -ErrorAction SilentlyContinue)
    if ($processes.Count -eq 0) { return }
    throw "Close Hearthstone Deck Tracker before changing Bob Coach"
}

function Clear-ReadOnlyAttribute([string]$Path) {
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Expected a regular file before clearing ReadOnly: $Path"
    }
    if (($item.Attributes -band [IO.FileAttributes]::ReadOnly) -eq 0) { return }
    $updated = [IO.FileAttributes](
        ([int]$item.Attributes) -band (-bnot [int][IO.FileAttributes]::ReadOnly)
    )
    [IO.File]::SetAttributes($item.FullName, $updated)
}

function Write-PluginChecksum([string]$Path, [string]$Hash) {
    [IO.File]::WriteAllText(
        $Path,
        ("{0}  BobCoach.dll`n" -f $Hash),
        (New-Object Text.UTF8Encoding($false))
    )
}

function Publish-PluginChecksum([string]$TemporaryChecksum, [string]$TargetChecksum) {
    if (Test-Path -LiteralPath $TargetChecksum -PathType Leaf) {
        Clear-ReadOnlyAttribute $TargetChecksum
        $replacementBackup = "$TargetChecksum.replace-backup"
        try {
            [IO.File]::Replace($TemporaryChecksum, $TargetChecksum, $replacementBackup, $true)
        } finally {
            if (Test-Path -LiteralPath $replacementBackup -PathType Leaf) {
                Remove-Item -LiteralPath $replacementBackup -Force
            }
        }
    } else {
        [IO.File]::Move($TemporaryChecksum, $TargetChecksum)
    }
}

function Replace-BobCoachDll(
    [string]$TemporaryDll,
    [string]$TargetDll,
    [string]$BackupDll
) {
    $targetItem = Get-Item -LiteralPath $TargetDll -Force -ErrorAction Stop
    if ($targetItem.PSIsContainer -or ($targetItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "BobCoach target must be a regular file: $TargetDll"
    }
    $originalAttributes = $targetItem.Attributes
    $replaceSucceeded = $false
    try {
        Clear-ReadOnlyAttribute $TargetDll
        [IO.File]::Replace($TemporaryDll, $TargetDll, $BackupDll, $true)
        $replaceSucceeded = $true
    } finally {
        if (!$replaceSucceeded -and (Test-Path -LiteralPath $TargetDll -PathType Leaf)) {
            [IO.File]::SetAttributes($TargetDll, $originalAttributes)
        }
    }
}

function New-BackupPath([string]$TargetDll) {
    $stamp = [DateTime]::UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'", [Globalization.CultureInfo]::InvariantCulture)
    $hash = Get-Sha256 $TargetDll
    $fileName = "BobCoach.dll.backup-{0}-{1}" -f $stamp, $hash.Substring(0, 16)
    $path = Join-Path (Split-Path -Parent $TargetDll) $fileName
    if (Test-Path -LiteralPath $path) { throw "Backup collision: $path" }
    return $path
}

function Resolve-RollbackBackup([string]$ResolvedPluginDirectory, [string]$RequestedBackupPath) {
    if (!(Test-Path -LiteralPath $ResolvedPluginDirectory -PathType Container)) {
        throw "No Bob Coach backup found in missing plugin directory: $ResolvedPluginDirectory"
    }
    if ([string]::IsNullOrWhiteSpace($RequestedBackupPath)) {
        $candidate = Get-ChildItem -LiteralPath $ResolvedPluginDirectory -Filter "BobCoach.dll.backup-*" -File |
            Sort-Object Name -Descending |
            Select-Object -First 1
        if ($null -eq $candidate) { throw "No Bob Coach backup found in: $ResolvedPluginDirectory" }
    } else {
        $fullBackupPath = [IO.Path]::GetFullPath($RequestedBackupPath)
        $backupParent = [IO.Path]::GetFullPath((Split-Path -Parent $fullBackupPath)).TrimEnd('\')
        if (!$backupParent.Equals($ResolvedPluginDirectory, [StringComparison]::OrdinalIgnoreCase)) {
            throw "BackupPath must be inside the target Plugins directory"
        }
        $backupName = [IO.Path]::GetFileName($fullBackupPath)
        if ($backupName -notmatch '^BobCoach\.dll\.backup-') {
            throw "BackupPath has an invalid file name: $backupName"
        }
        if (!(Test-Path -LiteralPath $fullBackupPath -PathType Leaf)) {
            throw "BackupPath not found: $fullBackupPath"
        }
        $candidate = Get-Item -LiteralPath $fullBackupPath -Force
    }
    if (($candidate.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "BackupPath must be a regular file"
    }
    try {
        $assemblyName = [Reflection.AssemblyName]::GetAssemblyName($candidate.FullName)
    } catch {
        throw "Backup is not a managed BobCoach assembly: $($candidate.FullName)"
    }
    if ($assemblyName.Name -ne "BobCoach") {
        throw "Backup assembly name mismatch: $($assemblyName.Name)"
    }
    return $candidate.FullName
}

function Invoke-Rollback([string]$ResolvedPluginDirectory, [string]$RequestedBackupPath) {
    $sourceBackup = Resolve-RollbackBackup $ResolvedPluginDirectory $RequestedBackupPath
    $targetDll = Join-Path $ResolvedPluginDirectory "BobCoach.dll"
    if (!$PSCmdlet.ShouldProcess($targetDll, "Restore Bob Coach backup $sourceBackup")) { return }

    $sourceHash = Get-Sha256 $sourceBackup
    $tempDll = Join-Path $ResolvedPluginDirectory ("BobCoach.dll.installing-" + [Guid]::NewGuid().ToString("N"))
    $targetChecksum = Join-Path $ResolvedPluginDirectory "BobCoach.dll.sha256"
    $tempChecksum = Join-Path $ResolvedPluginDirectory ("BobCoach.dll.sha256.installing-" + [Guid]::NewGuid().ToString("N"))
    $currentBackup = $null
    $dllPublished = $false
    try {
        Copy-Item -LiteralPath $sourceBackup -Destination $tempDll -ErrorAction Stop
        Clear-ReadOnlyAttribute $tempDll
        $tempHash = Get-Sha256 $tempDll
        if ($tempHash -ne $sourceHash) { throw "Temporary rollback hash mismatch" }
        Write-PluginChecksum $tempChecksum $sourceHash
        if (Test-Path -LiteralPath $targetDll -PathType Leaf) {
            $currentBackup = New-BackupPath $targetDll
            Replace-BobCoachDll $tempDll $targetDll $currentBackup
        } else {
            [IO.File]::Move($tempDll, $targetDll)
        }
        $dllPublished = $true
        Publish-PluginChecksum $tempChecksum $targetChecksum
        Write-Host "PASS restored Bob Coach backup to $targetDll"
    } catch {
        $rollbackError = $_
        if ($dllPublished) {
            if (![string]::IsNullOrWhiteSpace($currentBackup) -and
                (Test-Path -LiteralPath $currentBackup -PathType Leaf)) {
                $failedCandidate = Join-Path $ResolvedPluginDirectory ("BobCoach.dll.failed-" + [Guid]::NewGuid().ToString("N"))
                try {
                    Replace-BobCoachDll $currentBackup $targetDll $failedCandidate
                } finally {
                    if (Test-Path -LiteralPath $failedCandidate -PathType Leaf) {
                        Remove-Item -LiteralPath $failedCandidate -Force
                    }
                }
            } elseif (Test-Path -LiteralPath $targetDll -PathType Leaf) {
                Remove-Item -LiteralPath $targetDll -Force
            }
        }
        throw $rollbackError
    } finally {
        if (Test-Path -LiteralPath $tempDll) {
            Remove-Item -LiteralPath $tempDll -Force
        }
        if (Test-Path -LiteralPath $tempChecksum) {
            Remove-Item -LiteralPath $tempChecksum -Force
        }
    }
}

if (!$Rollback -and ![string]::IsNullOrWhiteSpace($BackupPath)) {
    throw "BackupPath requires -Rollback"
}

if ($Rollback) {
    $rollbackDirectory = Resolve-PluginDirectory $PluginDirectory
    Assert-HdtStopped $rollbackDirectory
    Invoke-Rollback $rollbackDirectory.Path $BackupPath
    return
}

$package = Assert-PackageIntegrity
$resolvedPluginDirectory = Resolve-PluginDirectory $PluginDirectory
Assert-HdtStopped $resolvedPluginDirectory
$targetDll = Join-Path $resolvedPluginDirectory.Path "BobCoach.dll"
$targetChecksum = Join-Path $resolvedPluginDirectory.Path "BobCoach.dll.sha256"

if (!$PSCmdlet.ShouldProcess($targetDll, "Install Bob Coach $($package.PackageVersion)")) {
    return
}

if (!(Test-Path -LiteralPath $resolvedPluginDirectory.Path -PathType Container)) {
    New-Item -ItemType Directory -Path $resolvedPluginDirectory.Path | Out-Null
}

$tempDll = Join-Path $resolvedPluginDirectory.Path ("BobCoach.dll.installing-" + [Guid]::NewGuid().ToString("N"))
$tempChecksum = Join-Path $resolvedPluginDirectory.Path ("BobCoach.dll.sha256.installing-" + [Guid]::NewGuid().ToString("N"))
$backupPathForUpgrade = $null
$dllPublished = $false
try {
    Copy-Item -LiteralPath $package.PluginPath -Destination $tempDll -ErrorAction Stop
    Clear-ReadOnlyAttribute $tempDll
    $tempHash = Get-Sha256 $tempDll
    if ($tempHash -ne $package.PluginHash) { throw "Temporary plugin hash mismatch" }
    Copy-Item -LiteralPath $package.PluginChecksumPath -Destination $tempChecksum -ErrorAction Stop
    Clear-ReadOnlyAttribute $tempChecksum
    if (Test-Path -LiteralPath $targetDll -PathType Leaf) {
        $backupPathForUpgrade = New-BackupPath $targetDll
        Replace-BobCoachDll $tempDll $targetDll $backupPathForUpgrade
    } else {
        [IO.File]::Move($tempDll, $targetDll)
    }
    $dllPublished = $true
    Publish-PluginChecksum $tempChecksum $targetChecksum
    if (![string]::IsNullOrWhiteSpace($backupPathForUpgrade)) {
        Write-Host "PASS upgraded Bob Coach $($package.PackageVersion) to $targetDll backup=$backupPathForUpgrade"
    } else {
        Write-Host "PASS installed Bob Coach $($package.PackageVersion) to $targetDll"
    }
} catch {
    $installError = $_
    if ($dllPublished) {
        if (![string]::IsNullOrWhiteSpace($backupPathForUpgrade) -and
            (Test-Path -LiteralPath $backupPathForUpgrade -PathType Leaf)) {
            $failedCandidate = Join-Path $resolvedPluginDirectory.Path ("BobCoach.dll.failed-" + [Guid]::NewGuid().ToString("N"))
            try {
                Replace-BobCoachDll $backupPathForUpgrade $targetDll $failedCandidate
            } finally {
                if (Test-Path -LiteralPath $failedCandidate -PathType Leaf) {
                    Remove-Item -LiteralPath $failedCandidate -Force
                }
            }
        } elseif (Test-Path -LiteralPath $targetDll -PathType Leaf) {
            Remove-Item -LiteralPath $targetDll -Force
        }
    }
    throw $installError
} finally {
    if (Test-Path -LiteralPath $tempDll) {
        Remove-Item -LiteralPath $tempDll -Force
    }
    if (Test-Path -LiteralPath $tempChecksum) {
        Remove-Item -LiteralPath $tempChecksum -Force
    }
}
