[CmdletBinding()]
param(
    [string]$HdtDirectory = $env:BOBCOACH_HDT_DIR,
    [string]$PluginDirectory = [IO.Path]::GetFullPath(
        (Join-Path $env:APPDATA "HearthstoneDeckTracker\Plugins")
    ),
    [string]$ArchiveRoot = "E:\HDT-BobCoach-evidence\dll-archive",
    [string]$ReleaseDllPath
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$releaseBuilder = Join-Path $repoRoot "tools\build\build_release.ps1"
$packageBuilder = Join-Path $PSScriptRoot "build_offline_package.ps1"
$identityPath = Join-Path $repoRoot "release_identity.json"
$tempRoot = Join-Path $env:TEMP ("bobcoach-local-release-deploy-" + [Guid]::NewGuid().ToString("N"))
$TrustedRepositoryRootFileName = "BobCoach.trusted-repository-root"

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

function Assert-NoReparseInChain([string]$Path, [string]$Label) {
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
    $resolved = if ([string]::IsNullOrWhiteSpace($RequestedPath)) {
        $defaultPlugins
    } else {
        [IO.Path]::GetFullPath($RequestedPath).TrimEnd('\')
    }
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
    Assert-NoReparseInChain $resolved "PluginDirectory"
    return $resolved
}

function Assert-HdtStopped {
    $processes = @(Get-Process -Name "HearthstoneDeckTracker", "Hearthstone Deck Tracker" -ErrorAction SilentlyContinue)
    if ($processes.Count -eq 0) { return }
    throw "Close Hearthstone Deck Tracker before changing Bob Coach"
}

function Assert-HdtBaseline([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or !(Test-Path -LiteralPath $Path -PathType Container)) {
        throw "HDT directory not found; set BOBCOACH_HDT_DIR"
    }
    $executable = Join-Path $Path "HearthstoneDeckTracker.exe"
    if (!(Test-Path -LiteralPath $executable -PathType Leaf)) {
        throw "HDT executable missing: $executable"
    }
    $version = [Reflection.AssemblyName]::GetAssemblyName($executable).Version.ToString()
    if ($version -ne "1.57.1.0") {
        throw "HDT baseline mismatch: expected 1.57.1.0, got $version"
    }
}

function Clear-ReadOnlyAttribute([string]$Path) {
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    if ($item.PSIsContainer -or (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) {
        throw "Expected a regular file before clearing ReadOnly: $Path"
    }
    if (($item.Attributes -band [IO.FileAttributes]::ReadOnly) -eq 0) { return }
    $updated = [IO.FileAttributes](
        ([int]$item.Attributes) -band (-bnot [int][IO.FileAttributes]::ReadOnly)
    )
    [IO.File]::SetAttributes($item.FullName, $updated)
}

function Assert-TrustedRepositoryRootSidecar([string]$PluginDirectory) {
    $sidecar = Join-Path $PluginDirectory $TrustedRepositoryRootFileName
    if (!(Test-Path -LiteralPath $sidecar -PathType Leaf)) {
        throw "Trusted repository-root sidecar missing: $sidecar"
    }
    $item = Get-Item -LiteralPath $sidecar -Force -ErrorAction Stop
    if ($item.PSIsContainer -or (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) {
        throw "Trusted repository-root sidecar must be a regular file: $sidecar"
    }
    $actual = [IO.File]::ReadAllText($sidecar).Trim()
    $expected = [IO.Path]::GetFullPath($repoRoot).TrimEnd('\')
    if (!$actual.Equals($expected, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Trusted repository-root sidecar mismatch: expected=$expected actual=$actual"
    }
}

function Write-TrustedRepositoryRootSidecar([string]$PluginDirectory) {
    $sidecar = Join-Path $PluginDirectory $TrustedRepositoryRootFileName
    if (Test-Path -LiteralPath $sidecar) {
        $existing = Get-Item -LiteralPath $sidecar -Force -ErrorAction Stop
        if ($existing.PSIsContainer -or (($existing.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) {
            throw "Trusted repository-root sidecar must be a regular file: $sidecar"
        }
        Clear-ReadOnlyAttribute $sidecar
    }
    $temporary = Join-Path $PluginDirectory (
        "BobCoach.trusted-repository-root.installing-" + [Guid]::NewGuid().ToString("N")
    )
    $replacementBackup = Join-Path $PluginDirectory (
        "BobCoach.trusted-repository-root.replace-backup-" + [Guid]::NewGuid().ToString("N")
    )
    try {
        [IO.File]::WriteAllText(
            $temporary,
            ($repoRoot + "`r`n"),
            (New-Object Text.UTF8Encoding($false))
        )
        if (Test-Path -LiteralPath $sidecar -PathType Leaf) {
            [IO.File]::Replace($temporary, $sidecar, $replacementBackup, $true)
        } else {
            [IO.File]::Move($temporary, $sidecar)
        }
        Assert-TrustedRepositoryRootSidecar $PluginDirectory
        Write-Host "PASS wrote trusted repository-root sidecar: $sidecar"
    } finally {
        if (Test-Path -LiteralPath $temporary) {
            Remove-Item -LiteralPath $temporary -Force
        }
        if (Test-Path -LiteralPath $replacementBackup -PathType Leaf) {
            Remove-Item -LiteralPath $replacementBackup -Force
        }
    }
}

function Assert-ArchiveRootExternal([string]$Path) {
    $fullPath = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    $repoFullPath = [IO.Path]::GetFullPath($repoRoot).TrimEnd('\')
    if ($fullPath.StartsWith($repoFullPath + '\', [StringComparison]::OrdinalIgnoreCase) -or
        $fullPath.Equals($repoFullPath, [StringComparison]::OrdinalIgnoreCase)) {
        throw "ArchiveRoot must be outside the repository: $fullPath"
    }
    Assert-NoReparseInChain $fullPath "ArchiveRoot"
}

function New-ArchiveDirectory([string]$Root) {
    $commit = $null
    $commitOutput = & git -C $repoRoot rev-parse --short=8 HEAD 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($commitOutput)) {
        throw "Cannot resolve repository HEAD for archive manifest"
    }
    $commit = $commitOutput.Trim()
    $stamp = [DateTime]::UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'", [Globalization.CultureInfo]::InvariantCulture)
    $baseName = "{0}-{1}" -f $stamp, $commit
    $candidate = Join-Path $Root $baseName
    $suffix = 2
    while (Test-Path -LiteralPath $candidate) {
        $candidate = Join-Path $Root ("{0}-{1}" -f $baseName, $suffix)
        $suffix++
    }
    New-Item -ItemType Directory -Path $candidate -Force | Out-Null
    return [pscustomobject]@{
        Path = $candidate
        Commit = $commit
        Stamp = $stamp
    }
}

function Get-PluginSnapshotEntries([string]$PluginDirectory) {
    if (!(Test-Path -LiteralPath $PluginDirectory -PathType Container)) {
        return @()
    }
    $entries = @(Get-ChildItem -LiteralPath $PluginDirectory -Filter "BobCoach.dll*" -File -Force)
    $result = @()
    foreach ($entry in $entries) {
        if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Bob Coach plugin entry must be a regular file: $($entry.FullName)"
        }
        if ($entry.Name -like "BobCoach.dll.installing-*" -or
            $entry.Name -like "BobCoach.dll.sha256.installing-*" -or
            $entry.Name -like "BobCoach.dll.failed-*") {
            throw "Refusing to archive transient plugin file: $($entry.FullName)"
        }
        $sourceKind = if ($entry.Name -eq "BobCoach.dll" -or $entry.Name -eq "BobCoach.dll.sha256") {
            "current"
        } else {
            "backup"
        }
        $result += [pscustomobject]@{
            Name = $entry.Name
            SourcePath = $entry.FullName
            SourceKind = $sourceKind
            Size = $entry.Length
            Sha256 = Get-Sha256 $entry.FullName
        }
    }
    return @($result | Sort-Object Name)
}

function Publish-Archive([string]$ArchiveDirectory, [string]$Commit, [string]$Stamp, $SnapshotEntries, [string]$SourceDllPath, [string]$PluginSha256) {
    $manifestEntries = @()
    foreach ($entry in $SnapshotEntries) {
        $destination = Join-Path $ArchiveDirectory $entry.Name
        Copy-Item -LiteralPath $entry.SourcePath -Destination $destination -Force
        $copiedSize = (Get-Item -LiteralPath $destination).Length
        $copiedHash = Get-Sha256 $destination
        if ($copiedSize -ne $entry.Size -or $copiedHash -ne $entry.Sha256) {
            throw "Archive copy mismatch: $($entry.Name)"
        }
        $manifestEntries += [ordered]@{
            relativePath = $entry.Name
            sourceKind = $entry.SourceKind
            size = $entry.Size
            sha256 = $entry.Sha256
        }
    }
    $identity = Get-Content -Raw -Encoding UTF8 -LiteralPath $identityPath | ConvertFrom-Json
    $manifest = [ordered]@{
        schemaVersion = 1
        capturedAtUtc = $Stamp
        commit = $Commit
        sourceDllPath = $SourceDllPath
        pluginVersion = [string]$identity.packageVersion
        pluginSha256 = $PluginSha256
        entries = $manifestEntries
    }
    $manifestJson = $manifest | ConvertTo-Json -Depth 6
    [IO.File]::WriteAllText(
        (Join-Path $ArchiveDirectory "manifest.json"),
        $manifestJson + "`n",
        (New-Object Text.UTF8Encoding($false))
    )
    $manifestText = [IO.File]::ReadAllText((Join-Path $ArchiveDirectory "manifest.json"), [Text.Encoding]::UTF8)
    $parsed = $manifestText | ConvertFrom-Json
    if ([int]$parsed.schemaVersion -ne 1 -or @($parsed.entries).Count -ne $manifestEntries.Count) {
        throw "Archive manifest validation failed"
    }
    Write-Host "PASS archived plugin snapshot entries=$(@($SnapshotEntries).Count) archive=$ArchiveDirectory"
}

try {
    $resolvedHdt = if ([string]::IsNullOrWhiteSpace($HdtDirectory)) {
        $null
    } else {
        [IO.Path]::GetFullPath($HdtDirectory).TrimEnd('\')
    }
    Assert-HdtBaseline $resolvedHdt
    $resolvedPlugin = Resolve-PluginDirectory $PluginDirectory
    Assert-HdtStopped
    $resolvedArchiveRoot = [IO.Path]::GetFullPath($ArchiveRoot).TrimEnd('\')
    Assert-ArchiveRootExternal $resolvedArchiveRoot

    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
    $candidateDll = $null
    $candidateHash = $null
    if (![string]::IsNullOrWhiteSpace($ReleaseDllPath)) {
        $candidateDll = [IO.Path]::GetFullPath($ReleaseDllPath)
        if (!(Test-Path -LiteralPath $candidateDll -PathType Leaf)) {
            throw "Release DLL missing: $candidateDll"
        }
    } else {
        $buildDirectory = Join-Path $tempRoot "build"
        New-Item -ItemType Directory -Path $buildDirectory -Force | Out-Null
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $releaseBuilder `
            -HdtDirectory $resolvedHdt -OutputDirectory $buildDirectory -Force
        if ($LASTEXITCODE -ne 0) { throw "Release build failed with exit code $LASTEXITCODE" }
        $candidateDll = Join-Path $buildDirectory "BobCoach.dll"
        if (!(Test-Path -LiteralPath $candidateDll -PathType Leaf)) {
            throw "Release build did not produce BobCoach.dll"
        }
    }
    $candidateHash = Get-Sha256 $candidateDll
    $candidateSize = (Get-Item -LiteralPath $candidateDll).Length

    $packageOutput = Join-Path $tempRoot "package"
    New-Item -ItemType Directory -Path $packageOutput -Force | Out-Null
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $packageBuilder `
        -HdtDirectory $resolvedHdt -OutputDirectory $packageOutput `
        -ApprovedCandidateDllPath $candidateDll `
        -ApprovedCandidateSize $candidateSize `
        -ApprovedCandidateSha256 $candidateHash `
        -Force
    if ($LASTEXITCODE -ne 0) {
        throw "Offline package build failed with exit code $LASTEXITCODE"
    }

    $identity = Get-Content -Raw -Encoding UTF8 -LiteralPath $identityPath | ConvertFrom-Json
    $packageName = "BobCoach-$($identity.packageVersion)-$($identity.runtimeIdentifier)"
    $zipPath = Join-Path $packageOutput "$packageName.zip"
    if (!(Test-Path -LiteralPath $zipPath -PathType Leaf)) {
        throw "Offline package ZIP missing: $zipPath"
    }

    $archiveInfo = New-ArchiveDirectory $resolvedArchiveRoot
    $snapshotEntries = Get-PluginSnapshotEntries $resolvedPlugin
    Publish-Archive $archiveInfo.Path $archiveInfo.Commit $archiveInfo.Stamp $snapshotEntries $candidateDll $candidateHash

    $extractRoot = Join-Path $tempRoot "extracted"
    New-Item -ItemType Directory -Path $extractRoot -Force | Out-Null
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($zipPath, $extractRoot)
    $installer = Join-Path (Join-Path $extractRoot $packageName) "INSTALL.ps1"
    if (!(Test-Path -LiteralPath $installer -PathType Leaf)) {
        throw "Offline package installer missing: $installer"
    }
    & $installer -PluginDirectory $resolvedPlugin -Confirm:$false
    $deployedDll = Join-Path $resolvedPlugin "BobCoach.dll"
    $deployedHash = Get-Sha256 $deployedDll
    if ($deployedHash -ne $candidateHash) {
        throw "Deployed DLL hash mismatch"
    }
    Write-TrustedRepositoryRootSidecar $resolvedPlugin
    $localPluginDirs = @(
        (Join-Path $env:LOCALAPPDATA "HearthstoneDeckTracker\app-1.54.1\Plugins"),
        (Join-Path $env:LOCALAPPDATA "HearthstoneDeckTracker\app-1.54.0\Plugins"),
        "D:\software\game\HDT-V2.7.0\HDT\Plugins"
    )
    $teamEditionRoot = Get-ChildItem -LiteralPath "D:\software\game" -Directory -Filter "HDT*2.5.9" |
        Select-Object -First 1
    if ($teamEditionRoot -ne $null) {
        $teamPlugin = Join-Path $teamEditionRoot.FullName "HDT\Plugins"
        if (Test-Path -LiteralPath $teamPlugin -PathType Container) {
            $localPluginDirs += $teamPlugin
        }
    }
    foreach ($localDir in $localPluginDirs) {
        if (!(Test-Path -LiteralPath $localDir -PathType Container)) { continue }
        $localDll = Join-Path $localDir "BobCoach.dll"
        Copy-Item -LiteralPath $candidateDll -Destination $localDll -Force
        $localHash = Get-Sha256 $localDll
        if ($localHash -ne $candidateHash) {
            throw "Local HDT plugin hash mismatch: $localDll"
        }
        [IO.File]::WriteAllText(
            (Join-Path $localDir "BobCoach.dll.sha256"),
            ($candidateHash + "  BobCoach.dll`r`n"),
            (New-Object Text.UTF8Encoding($false))
        )
        Write-Host "PASS synced local HDT plugin $localDll sha256=$localHash"
    }
    Write-Host "PASS deployed latest release DLL bytes=$candidateSize sha256=$deployedHash archive=$($archiveInfo.Path)"
} catch {
    Write-Host "FAIL local release deployment"
    Write-Host $_.Exception.Message
    throw
} finally {
    if (Test-Path -LiteralPath $tempRoot) {
        $resolvedTemp = [IO.Path]::GetFullPath($tempRoot).TrimEnd('\')
        $tempPrefix = [IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\bobcoach-local-release-deploy-'
        if (!$resolvedTemp.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Unsafe deployment temp cleanup path: $resolvedTemp"
        }
        Remove-Item -LiteralPath $resolvedTemp -Recurse -Force
    }
}
