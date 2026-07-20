[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = "High")]
param(
    [string]$PluginDirectory,
    [switch]$Rollback,
    [string]$BackupPath
)

$ErrorActionPreference = "Stop"

$PackageFiles = @(
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
$HashedFiles = @($PackageFiles | Where-Object { $_ -ne "SHA256SUMS.txt" } | Sort-Object)

function Get-Sha256([string]$Path) {
    $previousWhatIf = $WhatIfPreference
    try {
        $WhatIfPreference = $false
        return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    } finally {
        $WhatIfPreference = $previousWhatIf
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
        if ($line -notmatch '^([A-F0-9]{64})  ([^\\/:*?"<>|]+)$') {
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
    $actualEntries = @(Get-ChildItem -LiteralPath $PSScriptRoot -Force | Select-Object -ExpandProperty Name)
    Assert-ExactSet $PackageFiles $actualEntries "Package files"

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
        "hdtBaselineVersion", "pluginFile", "pluginSize", "pluginSha256", "files"
    )
    Assert-ExactSet $expectedManifestFields @($manifest.PSObject.Properties.Name) "Manifest fields"
    if ([int]$manifest.schemaVersion -ne 1) { throw "Unsupported manifest schemaVersion: $($manifest.schemaVersion)" }
    if ([string]$manifest.packageVersion -ne "0.2.0-beta.1") { throw "Package version mismatch" }
    if ([string]$manifest.assemblyVersion -ne "0.2.0.0") { throw "Assembly version contract mismatch" }
    if ([string]$manifest.fileVersion -ne "0.2.0.0") { throw "File version contract mismatch" }
    if ([string]$manifest.informationalVersion -ne "0.2.0-beta.1") { throw "Informational version contract mismatch" }
    if ([string]$manifest.targetFramework -ne ".NETFramework,Version=v4.7.2") { throw "Target framework contract mismatch" }
    if ([string]$manifest.runtimeIdentifier -ne "win-x64") { throw "Runtime identifier contract mismatch" }
    if ([string]$manifest.hdtBaselineVersion -ne "1.53.5.0") { throw "HDT baseline contract mismatch" }
    if ([string]$manifest.pluginFile -ne "BobCoach.dll") { throw "Plugin file contract mismatch" }
    if (@($manifest.files).Count -ne $PackageFiles.Count -or (@($manifest.files) -join "`n") -ne ($PackageFiles -join "`n")) {
        throw "Manifest file order mismatch"
    }

    $pluginPath = Join-Path $PSScriptRoot "BobCoach.dll"
    $pluginItem = Get-Item -LiteralPath $pluginPath
    $pluginHash = Get-Sha256 $pluginPath
    if ([long]$manifest.pluginSize -ne $pluginItem.Length) { throw "Plugin size mismatch" }
    if ([string]$manifest.pluginSha256 -ne $pluginHash) { throw "Plugin hash mismatch" }

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
        PluginHash = $pluginHash
        PackageVersion = [string]$manifest.packageVersion
    }
}

function Resolve-PortableHdtExecutable([string]$Parent) {
    $candidates = @(
        (Join-Path $Parent "Hearthstone Deck Tracker.exe"),
        (Join-Path $Parent "HearthstoneDeckTracker.exe")
    )
    $matches = @($candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })
    if ($matches.Count -eq 0) {
        throw "Portable HDT executable not found; expected one of: $($candidates -join ', ')"
    }
    if ($matches.Count -ne 1) {
        throw "Multiple portable HDT executables found: $($matches -join ', ')"
    }
    return $matches[0]
}

function Resolve-PluginDirectory([string]$RequestedPath) {
    if ([string]::IsNullOrWhiteSpace($env:APPDATA)) { throw "APPDATA is not available" }
    $defaultParent = [IO.Path]::GetFullPath((Join-Path $env:APPDATA "HearthstoneDeckTracker")).TrimEnd('\')
    $defaultPlugins = [IO.Path]::GetFullPath((Join-Path $defaultParent "Plugins")).TrimEnd('\')
    $isDefault = [string]::IsNullOrWhiteSpace($RequestedPath)
    $resolved = if ($isDefault) { $defaultPlugins } else { [IO.Path]::GetFullPath($RequestedPath).TrimEnd('\') }
    if ([IO.Path]::GetFileName($resolved) -ne "Plugins") {
        throw "PluginDirectory must end with Plugins: $resolved"
    }

    $parent = Split-Path -Parent $resolved
    if (!(Test-Path -LiteralPath $parent -PathType Container)) {
        throw "PluginDirectory parent does not exist: $parent"
    }
    if (!$resolved.Equals($defaultPlugins, [StringComparison]::OrdinalIgnoreCase)) {
        Resolve-PortableHdtExecutable $parent | Out-Null
    }
    return [pscustomobject]@{
        Path = $resolved
        Parent = $parent
        IsDefault = $resolved.Equals($defaultPlugins, [StringComparison]::OrdinalIgnoreCase)
    }
}

function Assert-HdtStopped($ResolvedPluginDirectory) {
    $processes = @(Get-Process -Name "HearthstoneDeckTracker", "Hearthstone Deck Tracker" -ErrorAction SilentlyContinue)
    if ($processes.Count -eq 0) { return }
    if ($ResolvedPluginDirectory.IsDefault) {
        throw "Close Hearthstone Deck Tracker before changing Bob Coach"
    }
    foreach ($process in $processes) {
        $processPath = $null
        try { $processPath = $process.Path } catch { $processPath = $null }
        if ([string]::IsNullOrWhiteSpace($processPath)) { continue }
        $processParent = [IO.Path]::GetFullPath((Split-Path -Parent $processPath)).TrimEnd('\')
        if ($processParent.Equals($ResolvedPluginDirectory.Parent, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Close the portable Hearthstone Deck Tracker before changing Bob Coach"
        }
    }
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
    try {
        Copy-Item -LiteralPath $sourceBackup -Destination $tempDll -ErrorAction Stop
        Clear-ReadOnlyAttribute $tempDll
        $tempHash = Get-Sha256 $tempDll
        if ($tempHash -ne $sourceHash) { throw "Temporary rollback hash mismatch" }
        if (Test-Path -LiteralPath $targetDll -PathType Leaf) {
            $currentBackup = New-BackupPath $targetDll
            Replace-BobCoachDll $tempDll $targetDll $currentBackup
        } else {
            [IO.File]::Move($tempDll, $targetDll)
        }
        Write-Host "PASS restored Bob Coach backup to $targetDll"
    } finally {
        if (Test-Path -LiteralPath $tempDll) {
            Remove-Item -LiteralPath $tempDll -Force
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

if (!$PSCmdlet.ShouldProcess($targetDll, "Install Bob Coach $($package.PackageVersion)")) {
    return
}

if (!(Test-Path -LiteralPath $resolvedPluginDirectory.Path -PathType Container)) {
    New-Item -ItemType Directory -Path $resolvedPluginDirectory.Path | Out-Null
}

$tempDll = Join-Path $resolvedPluginDirectory.Path ("BobCoach.dll.installing-" + [Guid]::NewGuid().ToString("N"))
try {
    Copy-Item -LiteralPath $package.PluginPath -Destination $tempDll -ErrorAction Stop
    Clear-ReadOnlyAttribute $tempDll
    $tempHash = Get-Sha256 $tempDll
    if ($tempHash -ne $package.PluginHash) { throw "Temporary plugin hash mismatch" }
    if (Test-Path -LiteralPath $targetDll -PathType Leaf) {
        $backupPathForUpgrade = New-BackupPath $targetDll
        Replace-BobCoachDll $tempDll $targetDll $backupPathForUpgrade
        Write-Host "PASS upgraded Bob Coach $($package.PackageVersion) to $targetDll backup=$backupPathForUpgrade"
    } else {
        [IO.File]::Move($tempDll, $targetDll)
        Write-Host "PASS installed Bob Coach $($package.PackageVersion) to $targetDll"
    }
} finally {
    if (Test-Path -LiteralPath $tempDll) {
        Remove-Item -LiteralPath $tempDll -Force
    }
}
