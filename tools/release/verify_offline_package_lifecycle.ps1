[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ZipPath,
    [Parameter(Mandatory = $true)]
    [string]$Sha256Path,
    [Parameter(Mandatory = $true)]
    [string]$HdtDirectory,
    [Parameter(Mandatory = $true)]
    [string]$PreviousPluginPath,
    [Parameter(Mandatory = $true)]
    [string]$AppDataRoot,
    [Parameter(Mandatory = $true)]
    [string]$EvidenceDirectory,
    [string]$LogConfigPath,
    [switch]$Execute,
    [ValidateRange(5, 600)]
    [int]$CommandTimeoutSeconds = 120
)

$ErrorActionPreference = "Stop"
$script:PackageFiles = @(
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
$script:HdtExecutableNames = @("HearthstoneDeckTracker.exe", "Hearthstone Deck Tracker.exe")

function Resolve-FullPath([string]$Path, [string]$Label) {
    if ([string]::IsNullOrWhiteSpace($Path)) { throw "$Label is required" }
    try {
        $fullPath = [IO.Path]::GetFullPath($Path)
        $missingParts = New-Object 'Collections.Generic.List[string]'
        $existing = $fullPath
        while (!(Test-Path -LiteralPath $existing)) {
            $leaf = [IO.Path]::GetFileName($existing)
            if ([string]::IsNullOrWhiteSpace($leaf)) { break }
            $missingParts.Insert(0, $leaf)
            $parent = [IO.Path]::GetDirectoryName($existing)
            if ([string]::IsNullOrWhiteSpace($parent) -or $parent.Equals($existing, [StringComparison]::OrdinalIgnoreCase)) { break }
            $existing = $parent
        }
        if (Test-Path -LiteralPath $existing) {
            $fullPath = (Get-Item -LiteralPath $existing -Force).FullName
            foreach ($part in $missingParts) { $fullPath = Join-Path $fullPath $part }
        }
        $root = [IO.Path]::GetPathRoot($fullPath)
        if ($fullPath.Equals($root, [StringComparison]::OrdinalIgnoreCase)) { return $root }
        return $fullPath.TrimEnd('\')
    } catch {
        throw "$Label is not a valid path: $Path"
    }
}

function Test-PathEqual([string]$Left, [string]$Right) {
    return $Left.Equals($Right, [StringComparison]::OrdinalIgnoreCase)
}

function Test-PathWithin([string]$Parent, [string]$Child) {
    $prefix = $Parent.TrimEnd('\') + '\'
    return $Child.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)
}

function Assert-SupportedHost {
    if ($PSVersionTable.PSEdition -ne "Desktop" -or
        $PSVersionTable.PSVersion.Major -ne 5 -or
        $PSVersionTable.PSVersion.Minor -ne 1) {
        throw "Windows PowerShell 5.1 Desktop is required"
    }
    if (![Environment]::Is64BitProcess) { throw "64-bit Windows PowerShell is required" }
}

function Assert-SafeContainment([string]$Parent, [string]$Child, [string]$Label) {
    if (!(Test-PathWithin $Parent $Child)) { throw "$Label is outside required parent: $Child" }
}

function Assert-NoReparseInExistingChain([string]$Path, [string]$Label) {
    $current = Resolve-FullPath $Path $Label
    while (![string]::IsNullOrWhiteSpace($current)) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "$Label contains a reparse point: $current"
            }
        }
        $parent = [IO.Path]::GetDirectoryName($current)
        if ([string]::IsNullOrWhiteSpace($parent) -or (Test-PathEqual $parent $current)) { break }
        $current = $parent.TrimEnd('\')
    }
}

function Assert-NoReparseDescendants([string]$Path, [string]$Label) {
    Assert-NoReparseInExistingChain $Path $Label
    if (!(Test-Path -LiteralPath $Path -PathType Container)) { return }

    $pending = New-Object 'Collections.Generic.Stack[IO.DirectoryInfo]'
    $pending.Push((Get-Item -LiteralPath $Path -Force))
    while ($pending.Count -ne 0) {
        $directory = $pending.Pop()
        foreach ($entry in $directory.EnumerateFileSystemInfos()) {
            if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "$Label contains a reparse point: $($entry.FullName)"
            }
            if ($entry -is [IO.DirectoryInfo]) { $pending.Push($entry) }
        }
    }
}

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Get-StreamSha256([IO.Stream]$Stream) {
    $originalPosition = $Stream.Position
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try {
        $Stream.Position = 0
        return ([BitConverter]::ToString($algorithm.ComputeHash($Stream))).Replace("-", "")
    } finally {
        $Stream.Position = $originalPosition
        $algorithm.Dispose()
    }
}

function Read-ExternalSha256([string]$Path, [string]$ExpectedFileName) {
    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) { throw "External SHA-256 file missing: $Path" }
    $content = [IO.File]::ReadAllText($Path, [Text.Encoding]::UTF8)
    if ($content -notmatch '\A([A-Fa-f0-9]{64})  ([^\r\n]+)\r?\n?\z') {
        throw "External SHA-256 file must contain one canonical record"
    }
    if (!$Matches[2].Equals($ExpectedFileName, [StringComparison]::Ordinal)) {
        throw "External SHA-256 filename mismatch"
    }
    return $Matches[1].ToUpperInvariant()
}

function Assert-ExactSet([string[]]$Expected, [string[]]$Actual, [string]$Label) {
    if ($Expected.Count -ne $Actual.Count) {
        throw "$Label count mismatch expected=$($Expected.Count) actual=$($Actual.Count)"
    }
    $expectedSorted = @($Expected | Sort-Object)
    $actualSorted = @($Actual | Sort-Object)
    for ($index = 0; $index -lt $expectedSorted.Count; $index++) {
        if (!$expectedSorted[$index].Equals($actualSorted[$index], [StringComparison]::Ordinal)) {
            throw "$Label mismatch expected=$($expectedSorted[$index]) actual=$($actualSorted[$index])"
        }
    }
}

function Read-EntryText($Entry) {
    $stream = $Entry.Open()
    $reader = New-Object IO.StreamReader($stream, (New-Object Text.UTF8Encoding($false, $true)), $true)
    try { return $reader.ReadToEnd() } finally { $reader.Dispose(); $stream.Dispose() }
}

function Get-EntrySha256($Entry) {
    $stream = $Entry.Open()
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($algorithm.ComputeHash($stream))).Replace("-", "")
    } finally {
        $algorithm.Dispose()
        $stream.Dispose()
    }
}

function Read-StrictInternalSums([string]$Content) {
    $lines = @($Content -split '\r?\n' | Where-Object { $_ -ne "" })
    if ($lines.Count -ne ($script:PackageFiles.Count - 1)) {
        throw "SHA256SUMS.txt record count mismatch"
    }
    $result = @{}
    foreach ($line in $lines) {
        if ($line -notmatch '\A([A-Fa-f0-9]{64})  ([^/\\\r\n]+)\z') {
            throw "Invalid SHA256SUMS.txt record: $line"
        }
        $name = $Matches[2]
        if ($result.ContainsKey($name)) { throw "Duplicate SHA256SUMS.txt record: $name" }
        $result[$name] = $Matches[1].ToUpperInvariant()
    }
    Assert-ExactSet @($script:PackageFiles | Where-Object { $_ -ne "SHA256SUMS.txt" }) @($result.Keys) "SHA256SUMS.txt files"
    return $result
}

function Assert-ManifestContract($Manifest, [hashtable]$EntryHashes, [hashtable]$EntryLengths) {
    if ([int]$Manifest.schemaVersion -ne 1) { throw "Manifest schema version mismatch" }
    $expected = [ordered]@{
        packageVersion = "0.2.0-beta.1"
        assemblyVersion = "0.2.0.0"
        fileVersion = "0.2.0.0"
        informationalVersion = "0.2.0-beta.1"
        targetFramework = ".NETFramework,Version=v4.7.2"
        runtimeIdentifier = "win-x64"
        hdtBaselineVersion = "1.53.5.0"
        pluginFile = "BobCoach.dll"
    }
    foreach ($property in $expected.Keys) {
        if ([string]$Manifest.$property -ne $expected[$property]) { throw "Manifest $property mismatch" }
    }
    Assert-ExactSet $script:PackageFiles @($Manifest.files) "Manifest files"
    if ([long]$Manifest.pluginSize -ne [long]$EntryLengths["BobCoach.dll"]) { throw "Manifest pluginSize mismatch" }
    if ([string]$Manifest.pluginSha256 -ne $EntryHashes["BobCoach.dll"]) { throw "Manifest plugin SHA-256 mismatch" }
}

function Read-ZipContract([string]$Path) {
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $seen = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
        $safeEntries = @()
        foreach ($entry in $archive.Entries) {
            $name = $entry.FullName
            if ([string]::IsNullOrWhiteSpace($name) -or $name.EndsWith("/", [StringComparison]::Ordinal) -or
                $name.Contains("\") -or $name.StartsWith("/", [StringComparison]::Ordinal) -or
                $name.Contains(":") -or $name -match '(^|/)\.\.(/|$)' -or $name -match '(^|/)\.(/|$)') {
                throw "Unsafe ZIP entry: $name"
            }
            $rawAttributes = [BitConverter]::ToUInt32([BitConverter]::GetBytes([int]$entry.ExternalAttributes), 0)
            $dosAttributes = $rawAttributes -band 0xFFFF
            $unixMode = ($rawAttributes -shr 16) -band 0xFFFF
            $unixType = $unixMode -band 0xF000
            if (($dosAttributes -band [uint32][IO.FileAttributes]::ReparsePoint) -ne 0 -or
                ($dosAttributes -band [uint32][IO.FileAttributes]::Directory) -ne 0 -or
                $unixType -eq 0xA000 -or $unixType -eq 0x4000) {
                throw "ZIP entry attributes are unsafe: $name"
            }
            if (!$seen.Add($name)) { throw "Duplicate ZIP entry: $name" }
            $parts = @($name.Split('/'))
            if ($parts.Count -ne 2 -or [string]::IsNullOrWhiteSpace($parts[0]) -or [string]::IsNullOrWhiteSpace($parts[1])) {
                throw "Unsafe ZIP entry layout: $name"
            }
            $safeEntries += [pscustomobject]@{ Entry = $entry; Root = $parts[0]; Leaf = $parts[1]; FullName = $name }
        }
        if ($safeEntries.Count -ne $script:PackageFiles.Count) {
            throw "ZIP file count mismatch expected=$($script:PackageFiles.Count) actual=$($safeEntries.Count)"
        }
        $roots = @($safeEntries | Select-Object -ExpandProperty Root -Unique)
        if ($roots.Count -ne 1) { throw "ZIP must contain one common package directory" }
        Assert-ExactSet $script:PackageFiles @($safeEntries | Select-Object -ExpandProperty Leaf) "ZIP files"

        $hashes = @{}
        $lengths = @{}
        foreach ($item in $safeEntries) {
            $hashes[$item.Leaf] = Get-EntrySha256 $item.Entry
            $lengths[$item.Leaf] = [long]$item.Entry.Length
        }
        $sumEntry = ($safeEntries | Where-Object { $_.Leaf -eq "SHA256SUMS.txt" }).Entry
        $manifestEntry = ($safeEntries | Where-Object { $_.Leaf -eq "manifest.json" }).Entry
        $internalSums = Read-StrictInternalSums (Read-EntryText $sumEntry)
        foreach ($fileName in $internalSums.Keys) {
            if ($internalSums[$fileName] -ne $hashes[$fileName]) {
                throw "Internal SHA-256 mismatch: $fileName"
            }
        }
        try { $manifest = Read-EntryText $manifestEntry | ConvertFrom-Json } catch { throw "Manifest JSON is invalid" }
        Assert-ManifestContract $manifest $hashes $lengths
        return [pscustomobject]@{
            PackageDirectory = $roots[0]
            EntryNames = @($safeEntries | Select-Object -ExpandProperty FullName)
            EntryHashes = $hashes
            Manifest = $manifest
        }
    } finally {
        $archive.Dispose()
    }
}

function Assert-NoTargetArtifacts([string]$PluginDirectory) {
    $artifacts = @()
    foreach ($pattern in @("BobCoach.dll", "BobCoach.dll.backup-*", "BobCoach.dll.installing-*")) {
        $artifacts += @(Get-ChildItem -LiteralPath $PluginDirectory -Filter $pattern -Force -ErrorAction Stop)
    }
    if ($artifacts.Count -gt 0) { throw "Plugin target is not empty: $($artifacts[0].FullName)" }
}

function Assert-HdtStopped([string]$TargetHdtDirectory) {
    $names = @($script:HdtExecutableNames | ForEach-Object { [IO.Path]::GetFileNameWithoutExtension($_) })
    $processes = @(Get-Process -Name $names -ErrorAction SilentlyContinue)
    foreach ($process in $processes) {
        try { $processPath = $process.MainModule.FileName } catch { throw "HDT process path is unreadable: pid=$($process.Id)" }
        if ([string]::IsNullOrWhiteSpace($processPath)) { throw "HDT process path is unreadable: pid=$($process.Id)" }
        $fullProcessPath = Resolve-FullPath $processPath "HDT process path"
        throw "HDT process is running: pid=$($process.Id) path=$fullProcessPath target=$TargetHdtDirectory"
    }
}

function Assert-NonOverlappingEvidence(
    [string]$Evidence,
    [string]$Hdt,
    [string]$AppData,
    [string]$Zip,
    [string]$Previous
) {
    foreach ($protected in @($Hdt, $AppData, $Zip, $Previous)) {
        if ((Test-PathEqual $Evidence $protected) -or (Test-PathWithin $protected $Evidence) -or (Test-PathWithin $Evidence $protected)) {
            throw "EvidenceDirectory overlaps protected path: $protected"
        }
    }
}

function Invoke-ReadOnlyPreflight {
    Assert-SupportedHost
    $zip = Resolve-FullPath $ZipPath "ZipPath"
    $sha = Resolve-FullPath $Sha256Path "Sha256Path"
    $hdt = Resolve-FullPath $HdtDirectory "HdtDirectory"
    $previous = Resolve-FullPath $PreviousPluginPath "PreviousPluginPath"
    $appData = Resolve-FullPath $AppDataRoot "AppDataRoot"
    $evidence = Resolve-FullPath $EvidenceDirectory "EvidenceDirectory"
    $logConfig = if ([string]::IsNullOrWhiteSpace($LogConfigPath)) { $null } else { Resolve-FullPath $LogConfigPath "LogConfigPath" }

    foreach ($pathItem in @(
        [pscustomobject]@{ Path = $zip; Label = "ZipPath" },
        [pscustomobject]@{ Path = $sha; Label = "Sha256Path" },
        [pscustomobject]@{ Path = $hdt; Label = "HdtDirectory" },
        [pscustomobject]@{ Path = $previous; Label = "PreviousPluginPath" },
        [pscustomobject]@{ Path = $appData; Label = "AppDataRoot" },
        [pscustomobject]@{ Path = $evidence; Label = "EvidenceDirectory" }
    )) { Assert-NoReparseInExistingChain $pathItem.Path $pathItem.Label }
    if ($null -ne $logConfig) { Assert-NoReparseInExistingChain $logConfig "LogConfigPath" }

    if (!(Test-Path -LiteralPath $zip -PathType Leaf)) { throw "ZIP missing: $zip" }
    if (!(Test-Path -LiteralPath $sha -PathType Leaf)) { throw "External SHA-256 file missing: $sha" }
    if (!(Test-Path -LiteralPath $hdt -PathType Container)) { throw "HdtDirectory missing: $hdt" }
    if (!(Test-Path -LiteralPath $previous -PathType Leaf)) { throw "PreviousPluginPath missing: $previous" }
    if (Test-Path -LiteralPath $evidence) { throw "EvidenceDirectory already exists: $evidence" }
    if ($null -ne $logConfig -and !(Test-Path -LiteralPath $logConfig -PathType Leaf)) { throw "LogConfigPath missing: $logConfig" }

    $realAppData = Resolve-FullPath $env:APPDATA "real APPDATA"
    if ((Test-PathEqual $appData $realAppData) -or (Test-PathWithin $realAppData $appData)) {
        throw "AppDataRoot must not equal or be within real APPDATA"
    }
    if (Test-Path -LiteralPath $appData) {
        if (!(Test-Path -LiteralPath $appData -PathType Container)) { throw "AppDataRoot must be a directory" }
        if (@(Get-ChildItem -LiteralPath $appData -Force).Count -ne 0) { throw "AppDataRoot must be empty" }
    }

    Assert-NonOverlappingEvidence $evidence $hdt $appData $zip $previous
    $candidateExePaths = @($script:HdtExecutableNames | ForEach-Object { Join-Path $hdt $_ })
    foreach ($candidateExePath in @($candidateExePaths | Where-Object { Test-Path -LiteralPath $_ })) {
        Assert-NoReparseInExistingChain $candidateExePath "HdtExecutable"
    }
    $exePaths = @($candidateExePaths | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })
    if ($exePaths.Count -ne 1) { throw "HdtDirectory must contain exactly one supported HDT executable" }
    Assert-NoReparseInExistingChain $exePaths[0] "HdtExecutable"
    $pluginDirectory = Join-Path $hdt "Plugins"
    if (!(Test-Path -LiteralPath $pluginDirectory -PathType Container)) { throw "HDT Plugins directory missing: $pluginDirectory" }
    Assert-NoReparseInExistingChain $pluginDirectory "PluginDirectory"
    Assert-NoTargetArtifacts $pluginDirectory
    Assert-HdtStopped $hdt

    $externalHash = Read-ExternalSha256 $sha ([IO.Path]::GetFileName($zip))
    $zipHash = Get-Sha256 $zip
    if ($externalHash -ne $zipHash) { throw "External SHA-256 mismatch for ZIP" }
    $zipContract = Read-ZipContract $zip
    $candidateHash = [string]$zipContract.EntryHashes["BobCoach.dll"]
    $previousHash = Get-Sha256 $previous
    if ($previousHash -eq $candidateHash) { throw "Previous plugin must differ from package candidate" }
    try { $previousAssembly = [Reflection.AssemblyName]::GetAssemblyName($previous) } catch { throw "Previous plugin is not a managed assembly" }
    if ($previousAssembly.Name -ne "BobCoach") { throw "Previous plugin assembly name mismatch" }

    return [pscustomobject]@{
        Paths = [ordered]@{
            Zip = $zip
            Sha256 = $sha
            Hdt = $hdt
            PluginDirectory = $pluginDirectory
            PreviousPlugin = $previous
            AppData = $appData
            Evidence = $evidence
            LogConfig = $logConfig
        }
        Zip = [ordered]@{
            Bytes = (Get-Item -LiteralPath $zip).Length
            Sha256 = $zipHash
            ExternalSha256 = $externalHash
            PackageDirectory = $zipContract.PackageDirectory
            Entries = $zipContract.EntryNames
            EntryHashes = $zipContract.EntryHashes
        }
        Manifest = $zipContract.Manifest
        CandidateSha256 = $candidateHash
        PreviousPlugin = [ordered]@{
            Bytes = (Get-Item -LiteralPath $previous).Length
            Sha256 = $previousHash
            AssemblyName = $previousAssembly.Name
            AssemblyVersion = $previousAssembly.Version.ToString()
        }
        HdtExecutable = $exePaths[0]
    }
}

function Write-Utf8NoBom([string]$Path, [string]$Content) {
    [IO.File]::WriteAllText($Path, $Content, (New-Object Text.UTF8Encoding($false)))
}

function Write-JsonEvidence([string]$Path, $Value) {
    if (Test-Path -LiteralPath $Path) { throw "Evidence file already exists: $Path" }
    Write-Utf8NoBom $Path (($Value | ConvertTo-Json -Depth 12) + "`n")
}

function Write-ProtectedJsonEvidence([IO.FileStream]$Stream, $Value) {
    $content = ($Value | ConvertTo-Json -Depth 12) + "`n"
    $bytes = (New-Object Text.UTF8Encoding($false)).GetBytes($content)
    $Stream.Position = 0
    $Stream.SetLength(0)
    $Stream.Write($bytes, 0, $bytes.Length)
    $Stream.Flush($true)
}

function Get-FileState([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return $null }
    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) {
        return [ordered]@{ Path = $Path; Exists = $false }
    }
    $item = Get-Item -LiteralPath $Path -Force
    return [ordered]@{
        Path = $Path
        Exists = $true
        Bytes = $item.Length
        Sha256 = Get-Sha256 $Path
        Attributes = [int]$item.Attributes
        LastWriteUtc = $item.LastWriteTimeUtc.ToString("o")
    }
}

function Get-DirectoryState([string]$Path) {
    if (!(Test-Path -LiteralPath $Path -PathType Container)) {
        return [ordered]@{ Path = $Path; Exists = $false; Files = @() }
    }
    Assert-NoReparseDescendants $Path "DirectoryState"
    $prefixLength = $Path.TrimEnd('\').Length + 1
    $files = @(
        Get-ChildItem -LiteralPath $Path -Recurse -File -Force |
            Sort-Object FullName |
            ForEach-Object {
                [ordered]@{
                    RelativePath = $_.FullName.Substring($prefixLength).Replace('\', '/')
                    Bytes = $_.Length
                    Sha256 = Get-Sha256 $_.FullName
                    Attributes = [int]$_.Attributes
                }
            }
    )
    return [ordered]@{ Path = $Path; Exists = $true; Files = $files }
}

function Get-LifecycleState([string]$PluginDirectory, [string]$AppDataRoot, [string]$LogConfigPath) {
    $backups = @(
        Get-ChildItem -LiteralPath $PluginDirectory -Filter "BobCoach.dll.backup-*" -File -Force -ErrorAction Stop |
            Sort-Object Name |
            ForEach-Object { Get-FileState $_.FullName }
    )
    $temporary = @(
        Get-ChildItem -LiteralPath $PluginDirectory -Filter "BobCoach.dll.installing-*" -File -Force -ErrorAction Stop |
            Sort-Object Name |
            ForEach-Object { Get-FileState $_.FullName }
    )
    return [ordered]@{
        CapturedUtc = [DateTime]::UtcNow.ToString("o")
        Target = Get-FileState (Join-Path $PluginDirectory "BobCoach.dll")
        Backups = $backups
        TemporaryDlls = $temporary
        UserData = Get-DirectoryState (Join-Path $AppDataRoot "bob-coach")
        LogConfig = if ([string]::IsNullOrWhiteSpace($LogConfigPath)) { $null } else { Get-FileState $LogConfigPath }
    }
}

function Get-AssemblyAttributeValue($Assembly, [string]$AttributeTypeName) {
    $attribute = @(
        [Reflection.CustomAttributeData]::GetCustomAttributes($Assembly) |
            Where-Object { $_.AttributeType.FullName -eq $AttributeTypeName }
    ) | Select-Object -First 1
    if ($null -eq $attribute -or $attribute.ConstructorArguments.Count -ne 1) { return $null }
    return [string]$attribute.ConstructorArguments[0].Value
}

function Get-CandidateFacts([string]$Path) {
    try {
        $assemblyName = [Reflection.AssemblyName]::GetAssemblyName($Path)
        $assembly = [Reflection.Assembly]::ReflectionOnlyLoadFrom($Path)
        $peKind = [Reflection.PortableExecutableKinds]::NotAPortableExecutableImage
        $machine = [Reflection.ImageFileMachine]::I386
        $assembly.ManifestModule.GetPEKind([ref]$peKind, [ref]$machine)
    } catch {
        throw "Extracted candidate is not a valid managed assembly: $($_.Exception.Message)"
    }
    return [ordered]@{
        Name = $assemblyName.Name
        AssemblyVersion = $assemblyName.Version.ToString()
        FileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($Path).FileVersion
        InformationalVersion = Get-AssemblyAttributeValue $assembly "System.Reflection.AssemblyInformationalVersionAttribute"
        TargetFramework = Get-AssemblyAttributeValue $assembly "System.Runtime.Versioning.TargetFrameworkAttribute"
        Machine = $machine.ToString()
        PEKind = $peKind.ToString()
        Bytes = (Get-Item -LiteralPath $Path).Length
        Sha256 = Get-Sha256 $Path
    }
}

function Assert-CandidateContract($Facts, $Manifest) {
    if ($Facts.Name -ne "BobCoach" -or
        $Facts.AssemblyVersion -ne [string]$Manifest.assemblyVersion -or
        $Facts.FileVersion -ne [string]$Manifest.fileVersion -or
        $Facts.InformationalVersion -ne [string]$Manifest.informationalVersion -or
        $Facts.TargetFramework -ne [string]$Manifest.targetFramework -or
        $Facts.Machine -ne "AMD64" -or
        $Facts.PEKind.IndexOf("PE32Plus", [StringComparison]::Ordinal) -lt 0 -or
        $Facts.PEKind.IndexOf("ILOnly", [StringComparison]::Ordinal) -lt 0 -or
        $Facts.Bytes -ne [long]$Manifest.pluginSize -or
        $Facts.Sha256 -ne [string]$Manifest.pluginSha256) {
        throw "Extracted candidate assembly contract mismatch"
    }
}

function Expand-ValidatedPackage($Preflight) {
    $extractRoot = Join-Path $Preflight.Paths.Evidence "extracted"
    [IO.Directory]::CreateDirectory($extractRoot) | Out-Null
    Assert-NoReparseInExistingChain $extractRoot "ExtractRoot"
    [IO.Compression.ZipFile]::ExtractToDirectory($Preflight.Paths.Zip, $extractRoot)
    $packageRoot = Join-Path $extractRoot $Preflight.Zip.PackageDirectory
    if (!(Test-Path -LiteralPath $packageRoot -PathType Container)) { throw "Extracted package directory missing" }
    Assert-NoReparseInExistingChain $packageRoot "ExtractedPackage"
    $files = @(Get-ChildItem -LiteralPath $packageRoot -File -Force | Select-Object -ExpandProperty Name)
    Assert-ExactSet $script:PackageFiles $files "Extracted package files"
    if (@(Get-ChildItem -LiteralPath $packageRoot -Directory -Force).Count -ne 0) {
        throw "Extracted package contains unexpected directories"
    }
    foreach ($fileName in $script:PackageFiles) {
        $filePath = Join-Path $packageRoot $fileName
        Assert-NoReparseInExistingChain $filePath "ExtractedFile"
        $actualHash = Get-Sha256 $filePath
        $expectedHash = [string]$Preflight.Zip.EntryHashes.$fileName
        if ($actualHash -ne $expectedHash) { throw "Extracted file SHA-256 mismatch: $fileName" }
    }
    $facts = Get-CandidateFacts (Join-Path $packageRoot "BobCoach.dll")
    Assert-CandidateContract $facts $Preflight.Manifest
    return [pscustomobject]@{ Root = $packageRoot; CandidateFacts = $facts }
}

function ConvertTo-PowerShellLiteral([string]$Value) {
    return "'{0}'" -f $Value.Replace("'", "''")
}

function Invoke-IsolatedPowerShell(
    [string]$ScriptPath,
    [string[]]$Arguments,
    [string]$StepName,
    [int]$StepNumber
) {
    $script:CommandIndex++
    $commandNumber = $script:CommandIndex
    $baseName = "{0:D2}-{1}" -f $commandNumber, $StepName
    $stdoutPath = Join-Path $script:CommandsDirectory "$baseName.stdout.txt"
    $stderrPath = Join-Path $script:CommandsDirectory "$baseName.stderr.txt"
    $argumentText = foreach ($argument in $Arguments) {
        if ($argument.StartsWith("-", [StringComparison]::Ordinal)) { $argument } else { ConvertTo-PowerShellLiteral $argument }
    }
    $command = "& {0} {1}" -f (ConvertTo-PowerShellLiteral $ScriptPath), ($argumentText -join " ")
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
    $startInfo = New-Object Diagnostics.ProcessStartInfo
    $startInfo.FileName = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"
    $startInfo.Arguments = "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand $encoded"
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $systemRoot = [IO.Path]::GetFullPath($env:SystemRoot).TrimEnd('\')
    $system32 = Join-Path $systemRoot "System32"
    $powerShellDirectory = Join-Path $system32 "WindowsPowerShell\v1.0"
    $environmentRoot = Join-Path $preflight.Paths.AppData "lifecycle-environment"
    $isolatedEnvironment = [ordered]@{
        APPDATA = $preflight.Paths.AppData
        LOCALAPPDATA = Join-Path $environmentRoot "local-appdata"
        USERPROFILE = Join-Path $environmentRoot "profile"
        TEMP = Join-Path $environmentRoot "temp"
        TMP = Join-Path $environmentRoot "temp"
    }
    foreach ($directory in @($isolatedEnvironment.LOCALAPPDATA, $isolatedEnvironment.USERPROFILE, $isolatedEnvironment.TEMP)) {
        [IO.Directory]::CreateDirectory($directory) | Out-Null
        Assert-NoReparseDescendants $directory "ChildEnvironmentDirectory"
    }
    $startInfo.EnvironmentVariables.Clear()
    foreach ($entry in $isolatedEnvironment.GetEnumerator()) {
        $startInfo.EnvironmentVariables[$entry.Key] = $entry.Value
    }
    $startInfo.EnvironmentVariables["SystemRoot"] = $systemRoot
    $startInfo.EnvironmentVariables["WINDIR"] = $systemRoot
    $startInfo.EnvironmentVariables["ComSpec"] = Join-Path $system32 "cmd.exe"
    $startInfo.EnvironmentVariables["PATH"] = "$system32;$powerShellDirectory"
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $startInfo
    if (!$process.Start()) { throw "Failed to start child PowerShell for $StepName" }
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $timedOut = !$process.WaitForExit($CommandTimeoutSeconds * 1000)
    if ($timedOut) {
        try { $process.Kill() } catch { }
        $process.WaitForExit()
    }
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    Write-Utf8NoBom $stdoutPath $stdout
    Write-Utf8NoBom $stderrPath $stderr
    $result = [pscustomobject]@{
        CommandNumber = $commandNumber
        StepNumber = $StepNumber
        StepName = $StepName
        Script = [IO.Path]::GetFileName($ScriptPath)
        Arguments = $Arguments
        ExitCode = if ($timedOut) { $null } else { $process.ExitCode }
        TimedOut = $timedOut
        Stdout = $stdoutPath
        Stderr = $stderrPath
    }
    $script:CommandResults += $result
    if ($timedOut) { throw "Child PowerShell timed out for $StepName" }
    if ($process.ExitCode -ne 0) { throw "Child PowerShell failed for $StepName exit=$($process.ExitCode)" }
    return $result
}

function Assert-TargetHash([string]$ExpectedHash, [string]$Label) {
    $target = Join-Path $preflight.Paths.PluginDirectory "BobCoach.dll"
    if (!(Test-Path -LiteralPath $target -PathType Leaf)) { throw "$Label target DLL missing" }
    $actual = Get-Sha256 $target
    if ($actual -ne $ExpectedHash) { throw "$Label target hash mismatch expected=$ExpectedHash actual=$actual" }
}

function Assert-TargetAbsent([string]$Label) {
    if (Test-Path -LiteralPath (Join-Path $preflight.Paths.PluginDirectory "BobCoach.dll")) {
        throw "$Label target DLL must be absent"
    }
}

function Assert-NoTemporaryDll {
    $temporary = @(Get-ChildItem -LiteralPath $preflight.Paths.PluginDirectory -Filter "BobCoach.dll.installing-*" -File -Force)
    if ($temporary.Count -ne 0) { throw "Temporary plugin DLL remains: $($temporary[0].FullName)" }
}

function Assert-FileStateEqual($Expected, $Actual, [string]$Label) {
    if ($null -eq $Expected -and $null -eq $Actual) { return }
    if ($null -eq $Expected -or $null -eq $Actual -or $Expected.Exists -ne $Actual.Exists) {
        throw "$Label existence changed"
    }
    if ($Expected.Exists -and ($Expected.Bytes -ne $Actual.Bytes -or
        $Expected.Sha256 -ne $Actual.Sha256 -or
        $Expected.Attributes -ne $Actual.Attributes -or
        $Expected.LastWriteUtc -ne $Actual.LastWriteUtc)) {
        throw "$Label changed"
    }
}

function Assert-DirectoryStateEqual($Expected, $Actual, [string]$Label) {
    if ($Expected.Exists -ne $Actual.Exists) { throw "$Label existence changed" }
    $expectedFiles = @($Expected.Files)
    $actualFiles = @($Actual.Files)
    if ($expectedFiles.Count -ne $actualFiles.Count) { throw "$Label file count changed" }
    for ($index = 0; $index -lt $expectedFiles.Count; $index++) {
        $left = $expectedFiles[$index]
        $right = $actualFiles[$index]
        if ($left.RelativePath -ne $right.RelativePath -or $left.Bytes -ne $right.Bytes -or
            $left.Sha256 -ne $right.Sha256 -or $left.Attributes -ne $right.Attributes) {
            throw "$Label changed: $($left.RelativePath)"
        }
    }
}

function Assert-LifecyclePathsSafe {
    foreach ($item in @(
        [pscustomobject]@{ Path = $preflight.Paths.Evidence; Label = "EvidenceDirectory" },
        [pscustomobject]@{ Path = $script:CommandsDirectory; Label = "CommandsDirectory" },
        [pscustomobject]@{ Path = $script:StatesDirectory; Label = "StatesDirectory" },
        [pscustomobject]@{ Path = $preflight.Paths.PluginDirectory; Label = "PluginDirectory" },
        [pscustomobject]@{ Path = $preflight.Paths.AppData; Label = "AppDataRoot" },
        [pscustomobject]@{ Path = (Join-Path $preflight.Paths.AppData "bob-coach"); Label = "UserData" }
    )) { Assert-NoReparseDescendants $item.Path $item.Label }
    foreach ($artifact in @(Get-ChildItem -LiteralPath $preflight.Paths.PluginDirectory -Filter "BobCoach.dll*" -Force)) {
        Assert-NoReparseInExistingChain $artifact.FullName "PluginArtifact"
    }
    if ($null -ne $preflight.Paths.LogConfig) {
        Assert-NoReparseInExistingChain $preflight.Paths.LogConfig "LogConfigPath"
    }
}

function Get-BackupFiles {
    return @(Get-ChildItem -LiteralPath $preflight.Paths.PluginDirectory -Filter "BobCoach.dll.backup-*" -File -Force | Sort-Object Name)
}

function Assert-BackupState([int]$Count, [int]$PreviousCount, [int]$CandidateCount, [string]$Label) {
    $backups = @(Get-BackupFiles)
    if ($backups.Count -ne $Count) { throw "$Label backup count mismatch expected=$Count actual=$($backups.Count)" }
    $previous = @($backups | Where-Object { (Get-Sha256 $_.FullName) -eq $preflight.PreviousPlugin.Sha256 })
    $candidate = @($backups | Where-Object { (Get-Sha256 $_.FullName) -eq $preflight.CandidateSha256 })
    if ($previous.Count -ne $PreviousCount -or $candidate.Count -ne $CandidateCount) {
        throw "$Label backup identities mismatch previous=$($previous.Count) candidate=$($candidate.Count)"
    }
}

function Invoke-LifecycleStep(
    [int]$Number,
    [string]$Name,
    [ValidateSet("Install", "Upgrade", "Rollback", "Uninstall", "Reinstall", "Setup")]
    [string]$Phase,
    [scriptblock]$Action,
    [switch]$AllowUserDataRemoval
) {
    $script:CurrentStepName = $Name
    $step = [ordered]@{ number = $Number; name = $Name; phase = $Phase; status = "Running"; commands = @() }
    $commandsBefore = $script:CommandResults.Count
    $before = $null
    $after = $null
    $stepFailure = $null
    try {
        Assert-LifecyclePathsSafe
        $before = Get-LifecycleState $preflight.Paths.PluginDirectory $preflight.Paths.AppData $preflight.Paths.LogConfig
        Assert-DirectoryStateEqual $script:InitialUserData $before.UserData "$Name user data before"
        Assert-FileStateEqual $script:InitialLogConfig $before.LogConfig "$Name log.config before"
        & $Action
        Assert-LifecyclePathsSafe
        $after = Get-LifecycleState $preflight.Paths.PluginDirectory $preflight.Paths.AppData $preflight.Paths.LogConfig
        Assert-FileStateEqual $script:InitialLogConfig $after.LogConfig "$Name log.config after"
        if ($AllowUserDataRemoval) {
            if ($after.UserData.Exists) { throw "$Name user data remains" }
        } else {
            Assert-DirectoryStateEqual $script:InitialUserData $after.UserData "$Name user data after"
        }
        $step.status = "Passed"
    } catch {
        $stepFailure = $_
        $step.status = "Failed"
        $step.errorType = $_.Exception.GetType().FullName
        $step.errorMessage = $_.Exception.Message
    }

    try {
        if ($null -eq $after) {
            $after = Get-LifecycleState $preflight.Paths.PluginDirectory $preflight.Paths.AppData $preflight.Paths.LogConfig
        }
        $step.commands = @($script:CommandResults | Select-Object -Skip $commandsBefore)
        $statePath = Join-Path $script:StatesDirectory ("{0:D2}-{1}.json" -f $Number, $Name)
        Write-JsonEvidence $statePath ([ordered]@{ stepNumber = $Number; stepName = $Name; phase = $Phase; before = $before; after = $after })
    } catch {
        $stateEvidenceFailure = $_
        if ($null -eq $stepFailure) {
            $stepFailure = $stateEvidenceFailure
            $step.status = "Failed"
            $step.errorType = $stateEvidenceFailure.Exception.GetType().FullName
            $step.errorMessage = $stateEvidenceFailure.Exception.Message
        } else {
            $step.stateEvidenceFailure = [ordered]@{
                errorType = $stateEvidenceFailure.Exception.GetType().FullName
                errorMessage = $stateEvidenceFailure.Exception.Message
            }
        }
    }
    $script:LifecycleSteps += [pscustomobject]$step
    if ($null -ne $stepFailure) { throw $stepFailure }
}

function Write-EvidenceChecksums([string]$EvidenceRoot, [string]$ProtectedPath, [IO.FileStream]$ProtectedStream) {
    $manifestPath = Join-Path $EvidenceRoot "EVIDENCE_SHA256SUMS.txt"
    if (Test-Path -LiteralPath $manifestPath) { throw "Evidence checksum file already exists" }
    $prefixLength = $EvidenceRoot.TrimEnd('\').Length + 1
    [string[]]$relativePaths = @(
        Get-ChildItem -LiteralPath $EvidenceRoot -Recurse -File -Force |
            Where-Object { $_.FullName -ne $manifestPath } |
            ForEach-Object { $_.FullName.Substring($prefixLength).Replace('\', '/') }
    )
    [Array]::Sort($relativePaths, [StringComparer]::Ordinal)
    $records = @($relativePaths | ForEach-Object {
        $fullName = Join-Path $EvidenceRoot $_.Replace('/', '\')
        $hash = if (Test-PathEqual $fullName $ProtectedPath) {
            Get-StreamSha256 $ProtectedStream
        } else {
            Get-Sha256 $fullName
        }
        "{0}  {1}" -f $hash, $_
    })
    Write-Utf8NoBom $manifestPath (($records -join "`n") + "`n")
}

function Invoke-LifecycleExecution($Preflight) {
    [IO.Directory]::CreateDirectory($Preflight.Paths.Evidence) | Out-Null
    Assert-NoReparseInExistingChain $Preflight.Paths.Evidence "EvidenceDirectory"
    $script:CommandsDirectory = Join-Path $Preflight.Paths.Evidence "commands"
    $script:StatesDirectory = Join-Path $Preflight.Paths.Evidence "states"
    [IO.Directory]::CreateDirectory($script:CommandsDirectory) | Out-Null
    [IO.Directory]::CreateDirectory($script:StatesDirectory) | Out-Null
    Assert-NoReparseInExistingChain $script:CommandsDirectory "CommandsDirectory"
    Assert-NoReparseInExistingChain $script:StatesDirectory "StatesDirectory"
    $script:CommandIndex = 0
    $script:CommandResults = @()
    $script:LifecycleSteps = @()
    $script:CurrentStepName = $null
    $script:PreviousBackup = $null
    $script:CandidateBackup = $null
    $startedUtc = [DateTime]::UtcNow.ToString("o")
    $failedStep = $null
    $failure = $null
    $resultPath = Join-Path $Preflight.Paths.Evidence "lifecycle-result.json"
    $resultStream = New-Object IO.FileStream(
        $resultPath,
        [IO.FileMode]::CreateNew,
        [IO.FileAccess]::ReadWrite,
        [IO.FileShare]::Read
    )

    try {
        Write-ProtectedJsonEvidence $resultStream ([ordered]@{
            status = "Running"
            failedStep = $null
            startedUtc = $startedUtc
            finishedUtc = $null
        })

        try {
            $package = Expand-ValidatedPackage $Preflight
            $userData = Join-Path $Preflight.Paths.AppData "bob-coach"
            [IO.Directory]::CreateDirectory($userData) | Out-Null
            Assert-NoReparseDescendants $userData "UserData"
            Write-Utf8NoBom (Join-Path $userData "lifecycle-sentinel.txt") "isolated lifecycle sentinel`n"
            Assert-LifecyclePathsSafe
            $script:InitialUserData = Get-DirectoryState $userData
            $script:InitialLogConfig = if ($null -eq $Preflight.Paths.LogConfig) { $null } else { Get-FileState $Preflight.Paths.LogConfig }
            $preflightEvidence = [ordered]@{
                capturedUtc = [DateTime]::UtcNow.ToString("o")
                paths = $Preflight.Paths
                zip = $Preflight.Zip
                manifest = $Preflight.Manifest
                candidate = $package.CandidateFacts
                previousPlugin = $Preflight.PreviousPlugin
                hdtExecutable = $Preflight.HdtExecutable
                host = [ordered]@{
                    PowerShellVersion = $PSVersionTable.PSVersion.ToString()
                    WindowsVersion = [Environment]::OSVersion.VersionString
                    Is64BitProcess = [Environment]::Is64BitProcess
                }
            }
            Write-JsonEvidence (Join-Path $Preflight.Paths.Evidence "preflight.json") $preflightEvidence
            $installer = Join-Path $package.Root "INSTALL.ps1"
            $uninstaller = Join-Path $package.Root "UNINSTALL.ps1"

            Invoke-LifecycleStep 1 "fresh-install" "Install" {
                Invoke-IsolatedPowerShell $installer @("-PluginDirectory", $Preflight.Paths.PluginDirectory, "-Confirm:`$false") "fresh-install" 1 | Out-Null
                Assert-TargetHash $Preflight.CandidateSha256 "fresh install"
                Assert-BackupState 0 0 0 "fresh install"
                Assert-NoTemporaryDll
            }
            Invoke-LifecycleStep 2 "default-uninstall" "Uninstall" {
                Invoke-IsolatedPowerShell $uninstaller @("-PluginDirectory", $Preflight.Paths.PluginDirectory, "-Confirm:`$false") "default-uninstall" 2 | Out-Null
                Assert-TargetAbsent "default uninstall"
                Invoke-IsolatedPowerShell $uninstaller @("-PluginDirectory", $Preflight.Paths.PluginDirectory, "-Confirm:`$false") "default-uninstall-idempotent" 2 | Out-Null
                Assert-TargetAbsent "idempotent default uninstall"
                Assert-BackupState 0 0 0 "default uninstall"
                Assert-NoTemporaryDll
            }
            Invoke-LifecycleStep 3 "seed-previous" "Setup" {
                Copy-Item -LiteralPath $Preflight.Paths.PreviousPlugin -Destination (Join-Path $Preflight.Paths.PluginDirectory "BobCoach.dll")
                Assert-TargetHash $Preflight.PreviousPlugin.Sha256 "seed previous"
                Assert-BackupState 0 0 0 "seed previous"
                Assert-NoTemporaryDll
            }
            Invoke-LifecycleStep 4 "upgrade" "Upgrade" {
                $beforeNames = @(Get-BackupFiles | Select-Object -ExpandProperty Name)
                Invoke-IsolatedPowerShell $installer @("-PluginDirectory", $Preflight.Paths.PluginDirectory, "-Confirm:`$false") "upgrade" 4 | Out-Null
                Assert-TargetHash $Preflight.CandidateSha256 "upgrade"
                Assert-BackupState 1 1 0 "upgrade"
                $newBackups = @(Get-BackupFiles | Where-Object { $_.Name -notin $beforeNames })
                if ($newBackups.Count -ne 1 -or (Get-Sha256 $newBackups[0].FullName) -ne $Preflight.PreviousPlugin.Sha256) {
                    throw "Upgrade backup identity mismatch"
                }
                $script:PreviousBackup = $newBackups[0].FullName
                Assert-NoTemporaryDll
            }
            Invoke-LifecycleStep 5 "latest-rollback" "Rollback" {
                $beforeNames = @(Get-BackupFiles | Select-Object -ExpandProperty Name)
                Invoke-IsolatedPowerShell $installer @("-Rollback", "-PluginDirectory", $Preflight.Paths.PluginDirectory, "-Confirm:`$false") "latest-rollback" 5 | Out-Null
                Assert-TargetHash $Preflight.PreviousPlugin.Sha256 "latest rollback"
                Assert-BackupState 2 1 1 "latest rollback"
                if (!(Test-Path -LiteralPath $script:PreviousBackup -PathType Leaf)) { throw "Latest rollback removed source backup" }
                $newBackups = @(Get-BackupFiles | Where-Object { $_.Name -notin $beforeNames })
                if ($newBackups.Count -ne 1 -or (Get-Sha256 $newBackups[0].FullName) -ne $Preflight.CandidateSha256) {
                    throw "Latest rollback backup identity mismatch"
                }
                $script:CandidateBackup = $newBackups[0].FullName
                Assert-NoTemporaryDll
            }
            Invoke-LifecycleStep 6 "specified-rollback" "Rollback" {
                Invoke-IsolatedPowerShell $installer @("-Rollback", "-BackupPath", $script:CandidateBackup, "-PluginDirectory", $Preflight.Paths.PluginDirectory, "-Confirm:`$false") "specified-rollback" 6 | Out-Null
                Assert-TargetHash $Preflight.CandidateSha256 "specified rollback"
                Assert-BackupState 3 2 1 "specified rollback"
                if (!(Test-Path -LiteralPath $script:CandidateBackup -PathType Leaf)) { throw "Specified rollback removed source backup" }
                Assert-NoTemporaryDll
            }
            Invoke-LifecycleStep 7 "default-uninstall-after-rollbacks" "Uninstall" {
                Invoke-IsolatedPowerShell $uninstaller @("-PluginDirectory", $Preflight.Paths.PluginDirectory, "-Confirm:`$false") "default-uninstall-after-rollbacks" 7 | Out-Null
                Assert-TargetAbsent "default uninstall after rollbacks"
                Assert-BackupState 3 2 1 "default uninstall after rollbacks"
                Assert-NoTemporaryDll
            }
            Invoke-LifecycleStep 8 "fresh-install-again" "Reinstall" {
                Invoke-IsolatedPowerShell $installer @("-PluginDirectory", $Preflight.Paths.PluginDirectory, "-Confirm:`$false") "fresh-install-again" 8 | Out-Null
                Assert-TargetHash $Preflight.CandidateSha256 "fresh install again"
                Assert-BackupState 3 2 1 "fresh install again"
                Assert-NoTemporaryDll
            }

            Invoke-LifecycleStep 9 "remove-user-data" "Uninstall" {
                Invoke-IsolatedPowerShell $uninstaller @("-PluginDirectory", $Preflight.Paths.PluginDirectory, "-RemoveUserData", "-Confirm:`$false") "remove-user-data" 9 | Out-Null
                Assert-TargetAbsent "remove user data"
                if (Test-Path -LiteralPath $userData) { throw "Isolated user data remains after removal" }
                Assert-BackupState 3 2 1 "remove user data"
                Assert-NoTemporaryDll
            } -AllowUserDataRemoval
        } catch {
            $failure = $_
            $failedStep = $script:CurrentStepName
        }

        $finalState = $null
        $finalStateEvidenceFailure = $null
        try {
            Assert-LifecyclePathsSafe
            $finalState = Get-LifecycleState $Preflight.Paths.PluginDirectory $Preflight.Paths.AppData $Preflight.Paths.LogConfig
        } catch {
            $finalStateFailure = $_
            $finalStateEvidenceFailure = [ordered]@{
                errorType = $finalStateFailure.Exception.GetType().FullName
                errorMessage = $finalStateFailure.Exception.Message
            }
            if ($null -eq $failure) {
                $failure = $finalStateFailure
                $failedStep = "final-state-capture"
            }
        }

        $status = if ($null -eq $failure) { "Passed" } else { "Failed" }
        $result = [ordered]@{
            status = $status
            failedStep = $failedStep
            startedUtc = $startedUtc
            finishedUtc = [DateTime]::UtcNow.ToString("o")
            steps = $script:LifecycleSteps
            commands = $script:CommandResults
            finalState = $finalState
            finalStateEvidenceFailure = $finalStateEvidenceFailure
            errorType = if ($null -eq $failure) { $null } else { $failure.Exception.GetType().FullName }
            errorMessage = if ($null -eq $failure) { $null } else { $failure.Exception.Message }
            evidenceFinalizationFailure = $null
        }
        Write-ProtectedJsonEvidence $resultStream $result
        try {
            Write-EvidenceChecksums $Preflight.Paths.Evidence $resultPath $resultStream
        } catch {
            $finalizationFailure = $_
            $result.status = "Failed"
            $result.finishedUtc = [DateTime]::UtcNow.ToString("o")
            $result.evidenceFinalizationFailure = [ordered]@{
                errorType = $finalizationFailure.Exception.GetType().FullName
                errorMessage = $finalizationFailure.Exception.Message
            }
            if ($null -eq $failure) {
                $result.failedStep = "evidence-finalization"
                $result.errorType = $finalizationFailure.Exception.GetType().FullName
                $result.errorMessage = $finalizationFailure.Exception.Message
            }
            Write-ProtectedJsonEvidence $resultStream $result
            throw $finalizationFailure
        }
        if ($null -ne $failure) { throw $failure }
        Write-Host "PASS offline package lifecycle evidence=$($Preflight.Paths.Evidence)"
    } finally {
        $resultStream.Dispose()
    }
}

$preflight = Invoke-ReadOnlyPreflight
if (!$Execute) {
    Write-Host "PASS read-only preflight zip=$($preflight.Paths.Zip) sha256=$($preflight.Zip.Sha256)"
    exit 0
}

Invoke-LifecycleExecution $preflight
