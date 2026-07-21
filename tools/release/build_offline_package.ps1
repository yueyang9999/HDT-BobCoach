[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$HdtDirectory,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$OutputDirectory,

    [switch]$CurrentSeasonPreview,

    [string]$CandidateDllPath,

    [string]$ApprovedCandidateDllPath,

    [long]$ApprovedCandidateSize,

    [string]$ApprovedCandidateSha256,

    [switch]$Force
)

$ErrorActionPreference = "Stop"
$releaseDirectory = $PSScriptRoot
$bobCoachRoot = Split-Path -Parent $releaseDirectory
$repoRoot = Split-Path -Parent $bobCoachRoot
$identityPath = Join-Path $repoRoot "release_identity.json"
$releaseBuilder = Join-Path $repoRoot "tools\build\build_release.ps1"
$tempRoot = Join-Path $env:TEMP ("bobcoach-offline-package-" + [Guid]::NewGuid().ToString("N"))
$candidateDirectory = Join-Path $tempRoot "candidate"
$stagingRoot = Join-Path $tempRoot "staging"
$archiveRoot = Join-Path $tempRoot "archive"
$ApprovedPreviewPluginSize = 650240
$ApprovedPreviewPluginSha256 = "020C40CBC0927C230C74ED334995278D7D4669E16B4DEED38A92CD0F44804D37"

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
$ZipEntryTimestamp = [DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)

function Write-Utf8NoBom([string]$Path, [string]$Content) {
    [IO.File]::WriteAllText($Path, $Content, (New-Object Text.UTF8Encoding($false)))
}

function Assert-ExactSet([string[]]$Expected, [string[]]$Actual, [string]$Label) {
    $delta = @(Compare-Object @($Expected | Sort-Object) @($Actual | Sort-Object))
    if ($delta.Count -ne 0) { throw "$Label mismatch: delta=$($delta.Count)" }
}

function Assert-NoPreviewExternalHash([string]$Path, [string]$Stage) {
    if (Test-Path -LiteralPath $Path) {
        throw "Preview external hash residue detected $Stage; remove it explicitly: $Path"
    }
}

function Resolve-ReadmeBlock([string]$Content, [string]$Name, [bool]$Include) {
    $openingMarker = "{{$Name}}"
    $closingMarker = "{{/$Name}}"
    $pattern = "(?s)" + [regex]::Escape($openingMarker) + "\r?\n?(.*?)\r?\n?" + [regex]::Escape($closingMarker)
    $block = New-Object Text.RegularExpressions.Regex($pattern)
    if ($block.Matches($Content).Count -ne 1) {
        throw "README template block must appear exactly once: $Name"
    }
    if ($Include) {
        return $block.Replace($Content, '$1', 1)
    }
    return $block.Replace($Content, "", 1)
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

function Get-PluginFacts([string]$Path) {
    $assemblyName = [Reflection.AssemblyName]::GetAssemblyName($Path)
    $assembly = [Reflection.Assembly]::ReflectionOnlyLoad([IO.File]::ReadAllBytes($Path))
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

function Publish-FileAtomically([string]$Source, [string]$Destination) {
    $destinationDirectory = Split-Path -Parent $Destination
    $temporaryDestination = Join-Path $destinationDirectory ((Split-Path -Leaf $Destination) + ".new-" + [Guid]::NewGuid().ToString("N"))
    $replacementBackup = Join-Path $destinationDirectory ((Split-Path -Leaf $Destination) + ".replace-backup-" + [Guid]::NewGuid().ToString("N"))
    $replaceSucceeded = $false
    try {
        Copy-Item -LiteralPath $Source -Destination $temporaryDestination -ErrorAction Stop
        $sourceHash = (Get-FileHash -LiteralPath $Source -Algorithm SHA256).Hash
        $temporaryHash = (Get-FileHash -LiteralPath $temporaryDestination -Algorithm SHA256).Hash
        if ($sourceHash -ne $temporaryHash) { throw "Output temporary hash mismatch: $Destination" }
        if (Test-Path -LiteralPath $Destination -PathType Leaf) {
            try {
                [IO.File]::Replace($temporaryDestination, $Destination, $replacementBackup, $true)
                $replaceSucceeded = $true
            } catch {
                if (!(Test-Path -LiteralPath $Destination) -and (Test-Path -LiteralPath $replacementBackup -PathType Leaf)) {
                    [IO.File]::Move($replacementBackup, $Destination)
                }
                throw
            }
        } else {
            [IO.File]::Move($temporaryDestination, $Destination)
        }
    } finally {
        if (Test-Path -LiteralPath $temporaryDestination) {
            Remove-Item -LiteralPath $temporaryDestination -Force
        }
        if (Test-Path -LiteralPath $replacementBackup -PathType Leaf) {
            if ($replaceSucceeded -or (Test-Path -LiteralPath $Destination -PathType Leaf)) {
                Remove-Item -LiteralPath $replacementBackup -Force
            }
        }
    }
}

$approvedCandidateParameterNames = @(
    "ApprovedCandidateDllPath",
    "ApprovedCandidateSize",
    "ApprovedCandidateSha256"
)
$approvedCandidateParameterCount = @($approvedCandidateParameterNames | Where-Object {
    $PSBoundParameters.ContainsKey($_)
}).Count
if ($approvedCandidateParameterCount -ne 0 -and $approvedCandidateParameterCount -ne $approvedCandidateParameterNames.Count) {
    throw "Approved candidate requires path, size, and SHA-256 together"
}
$hasApprovedCandidate = $approvedCandidateParameterCount -eq $approvedCandidateParameterNames.Count

if (!(Test-Path -LiteralPath $identityPath -PathType Leaf)) { throw "Release identity missing: $identityPath" }
$identity = Get-Content -Raw -Encoding UTF8 -LiteralPath $identityPath | ConvertFrom-Json
if ($CurrentSeasonPreview -and $identity.packageVersion -ne "0.2.0-beta.1") {
    throw "CurrentSeasonPreview is retained only for historical 0.2.0-beta.1 artifacts"
}
if ($CurrentSeasonPreview) {
    if ($hasApprovedCandidate) {
        throw "Approved candidate parameters are not valid with CurrentSeasonPreview"
    }
    if ([string]::IsNullOrWhiteSpace($CandidateDllPath)) {
        throw "CurrentSeasonPreview requires CandidateDllPath"
    }
    $resolvedCandidateDll = [IO.Path]::GetFullPath($CandidateDllPath)
    if (!(Test-Path -LiteralPath $resolvedCandidateDll -PathType Leaf)) {
        throw "Preview candidate DLL missing: $resolvedCandidateDll"
    }
} else {
    if (![string]::IsNullOrWhiteSpace($CandidateDllPath)) {
        throw "CandidateDllPath is only valid with CurrentSeasonPreview"
    }
    if ($hasApprovedCandidate) {
        if ([string]::IsNullOrWhiteSpace($ApprovedCandidateDllPath)) {
            throw "Approved candidate DLL path is empty"
        }
        if ($ApprovedCandidateSize -le 0) {
            throw "Approved candidate size must be positive"
        }
        if ($ApprovedCandidateSha256 -notmatch '^[A-Fa-f0-9]{64}$') {
            throw "Approved candidate SHA-256 is invalid"
        }
        $resolvedApprovedCandidateDll = [IO.Path]::GetFullPath($ApprovedCandidateDllPath)
        if (!(Test-Path -LiteralPath $resolvedApprovedCandidateDll -PathType Leaf)) {
            throw "Approved candidate DLL missing: $resolvedApprovedCandidateDll"
        }
        $ApprovedCandidateSha256 = $ApprovedCandidateSha256.ToUpperInvariant()
        $approvedSourceSize = (Get-Item -LiteralPath $resolvedApprovedCandidateDll).Length
        $approvedSourceHash = (Get-FileHash -LiteralPath $resolvedApprovedCandidateDll -Algorithm SHA256).Hash
        if ($approvedSourceSize -ne $ApprovedCandidateSize -or $approvedSourceHash -ne $ApprovedCandidateSha256) {
            throw "Approved candidate source mismatch: bytes=$approvedSourceSize sha256=$approvedSourceHash"
        }
    } elseif (!(Test-Path -LiteralPath $releaseBuilder -PathType Leaf)) {
        throw "Release builder missing: $releaseBuilder"
    }
}
if ($identity.packageVersion -ne "0.2.0-beta.2" -or $identity.assemblyVersion -ne "0.2.0.0" -or
    $identity.targetFramework -ne "net472" -or $identity.runtimeIdentifier -ne "win-x64" -or
    $identity.hdtBaselineVersion -ne "1.53.5.7354") {
    throw "Unsupported release identity"
}

$packageName = if ($CurrentSeasonPreview) {
    "BobCoach-$($identity.packageVersion)-current-season-preview-20260719-$($identity.runtimeIdentifier)"
} else {
    "BobCoach-$($identity.packageVersion)-$($identity.runtimeIdentifier)"
}
$zipName = "$packageName.zip"
$outputFullPath = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\')
if (!(Test-Path -LiteralPath $outputFullPath)) {
    New-Item -ItemType Directory -Path $outputFullPath -Force | Out-Null
}
$zipPath = Join-Path $outputFullPath $zipName
$externalHashPath = "$zipPath.sha256"
if ($CurrentSeasonPreview) {
    Assert-NoPreviewExternalHash $externalHashPath "initial"
}
$outputPaths = @($zipPath)
if (!$CurrentSeasonPreview) { $outputPaths += $externalHashPath }
foreach ($outputPath in $outputPaths) {
    if ((Test-Path -LiteralPath $outputPath) -and !$Force) {
        throw "Output already exists; pass -Force to replace: $outputPath"
    }
}

try {
    New-Item -ItemType Directory -Path $candidateDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $archiveRoot -Force | Out-Null

    if ($CurrentSeasonPreview) {
        $candidateDll = $resolvedCandidateDll
    } elseif ($hasApprovedCandidate) {
        $candidateDll = $resolvedApprovedCandidateDll
    } else {
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $releaseBuilder `
            -HdtDirectory $HdtDirectory -OutputDirectory $candidateDirectory -Force
        if ($LASTEXITCODE -ne 0) { throw "Release build failed with exit code $LASTEXITCODE" }
        $candidateEntries = @(Get-ChildItem -LiteralPath $candidateDirectory -Force | Select-Object -ExpandProperty Name)
        Assert-ExactSet @("BobCoach.dll", "build_log.txt") $candidateEntries "Release candidate files"
        $candidateDll = Join-Path $candidateDirectory "BobCoach.dll"
    }
    $packageDirectory = Join-Path $stagingRoot $packageName
    New-Item -ItemType Directory -Path $packageDirectory | Out-Null
    $readmeTemplatePath = Join-Path $releaseDirectory "README_OFFLINE.md"
    if (!(Test-Path -LiteralPath $readmeTemplatePath -PathType Leaf)) { throw "Package source missing: $readmeTemplatePath" }
    $copyMap = @(
        [pscustomobject]@{ Source = $candidateDll; Name = "BobCoach.dll" },
        [pscustomobject]@{ Source = (Join-Path $releaseDirectory "INSTALL.ps1"); Name = "INSTALL.ps1" },
        [pscustomobject]@{ Source = (Join-Path $releaseDirectory "UNINSTALL.ps1"); Name = "UNINSTALL.ps1" },
        [pscustomobject]@{ Source = (Join-Path $repoRoot "LICENSE"); Name = "LICENSE" },
        [pscustomobject]@{ Source = (Join-Path $repoRoot "NOTICE"); Name = "NOTICE" },
        [pscustomobject]@{ Source = (Join-Path $repoRoot "DATA_SOURCES.md"); Name = "DATA_SOURCES.md" },
        [pscustomobject]@{ Source = (Join-Path $repoRoot "PRIVACY.md"); Name = "PRIVACY.md" },
        [pscustomobject]@{ Source = (Join-Path $repoRoot "SUPPORT.md"); Name = "SUPPORT.md" }
    )
    foreach ($entry in $copyMap) {
        if (!(Test-Path -LiteralPath $entry.Source -PathType Leaf)) { throw "Package source missing: $($entry.Source)" }
        Copy-Item -LiteralPath $entry.Source -Destination (Join-Path $packageDirectory $entry.Name)
    }

    $readme = Get-Content -Raw -Encoding UTF8 -LiteralPath $readmeTemplatePath
    $readme = Resolve-ReadmeBlock $readme "PREVIEW_NOTICE" ([bool]$CurrentSeasonPreview)
    $readme = Resolve-ReadmeBlock $readme "ZIP_HASH_GUIDANCE" ([bool]$CurrentSeasonPreview)
    $readme = Resolve-ReadmeBlock $readme "PREVIEW_LIMIT" ([bool]$CurrentSeasonPreview)
    $readme = Resolve-ReadmeBlock $readme "RELEASE_ZIP_HASH_GUIDANCE" (!$CurrentSeasonPreview)
    if ($readme -match '\{\{/?[A-Z0-9_]+\}\}') { throw "Unresolved README template marker" }
    Write-Utf8NoBom (Join-Path $packageDirectory "README_OFFLINE.md") $readme

    $stagedPluginPath = Join-Path $packageDirectory "BobCoach.dll"
    $pluginHash = (Get-FileHash -LiteralPath $stagedPluginPath -Algorithm SHA256).Hash
    $pluginSize = (Get-Item -LiteralPath $stagedPluginPath).Length
    if ($CurrentSeasonPreview -and
        ($pluginSize -ne $ApprovedPreviewPluginSize -or $pluginHash -ne $ApprovedPreviewPluginSha256)) {
        throw "Preview candidate is not the approved artifact: bytes=$pluginSize sha256=$pluginHash"
    }
    if ($hasApprovedCandidate -and
        ($pluginSize -ne $ApprovedCandidateSize -or $pluginHash -ne $ApprovedCandidateSha256)) {
        throw "Packaged approved candidate mismatch: bytes=$pluginSize sha256=$pluginHash"
    }

    $facts = Get-PluginFacts $stagedPluginPath
    if ($facts.Name -ne "BobCoach" -or $facts.AssemblyVersion -ne [string]$identity.assemblyVersion -or
        $facts.FileVersion -ne [string]$identity.assemblyVersion -or
        $facts.InformationalVersion -ne [string]$identity.packageVersion -or
        $facts.TargetFramework -ne ".NETFramework,Version=v4.7.2" -or
        $facts.Machine -ne [Reflection.ImageFileMachine]::AMD64 -or
        ($facts.PEKind -band [Reflection.PortableExecutableKinds]::PE32Plus) -eq 0 -or
        ($facts.PEKind -band [Reflection.PortableExecutableKinds]::ILOnly) -eq 0) {
        throw "Release candidate assembly contract mismatch"
    }

    $manifest = [ordered]@{
        schemaVersion = 1
        packageVersion = [string]$identity.packageVersion
        assemblyVersion = [string]$identity.assemblyVersion
        fileVersion = [string]$identity.assemblyVersion
        informationalVersion = [string]$identity.packageVersion
        targetFramework = ".NETFramework,Version=v4.7.2"
        runtimeIdentifier = [string]$identity.runtimeIdentifier
        hdtBaselineVersion = [string]$identity.hdtBaselineVersion
        pluginFile = "BobCoach.dll"
        pluginSize = $pluginSize
        pluginSha256 = $pluginHash
        files = $PackageFiles
    }
    $manifestPath = Join-Path $packageDirectory "manifest.json"
    Write-Utf8NoBom $manifestPath (($manifest | ConvertTo-Json -Depth 4) + "`n")
    Get-Content -Raw -Encoding UTF8 -LiteralPath $manifestPath | ConvertFrom-Json | Out-Null

    $hashLines = foreach ($fileName in ($PackageFiles | Where-Object { $_ -ne "SHA256SUMS.txt" } | Sort-Object)) {
        $hash = (Get-FileHash -LiteralPath (Join-Path $packageDirectory $fileName) -Algorithm SHA256).Hash
        "{0}  {1}" -f $hash, $fileName
    }
    Write-Utf8NoBom (Join-Path $packageDirectory "SHA256SUMS.txt") (($hashLines -join "`n") + "`n")

    $actualPackageFiles = @(Get-ChildItem -LiteralPath $packageDirectory -Force | Select-Object -ExpandProperty Name)
    Assert-ExactSet $PackageFiles $actualPackageFiles "Package files"
    foreach ($scriptName in @("INSTALL.ps1", "UNINSTALL.ps1")) {
        $tokens = $null
        $errors = $null
        [Management.Automation.Language.Parser]::ParseFile(
            (Join-Path $packageDirectory $scriptName), [ref]$tokens, [ref]$errors
        ) | Out-Null
        if ($errors.Count -gt 0) { throw "$scriptName PowerShell parse errors=$($errors.Count)" }
    }

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $temporaryZip = Join-Path $archiveRoot $zipName
    $createArchive = [IO.Compression.ZipFile]::Open($temporaryZip, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($fileName in $PackageFiles) {
            $zipEntry = $createArchive.CreateEntry(
                "$packageName/$fileName",
                [IO.Compression.CompressionLevel]::Optimal
            )
            $zipEntry.LastWriteTime = $ZipEntryTimestamp
            $sourceStream = [IO.File]::OpenRead((Join-Path $packageDirectory $fileName))
            $entryStream = $zipEntry.Open()
            try {
                $sourceStream.CopyTo($entryStream)
            } finally {
                $entryStream.Dispose()
                $sourceStream.Dispose()
            }
        }
    } finally {
        $createArchive.Dispose()
    }
    $archive = [IO.Compression.ZipFile]::OpenRead($temporaryZip)
    try {
        $actualEntries = @($archive.Entries | Where-Object { !$_.FullName.EndsWith("/") } | Select-Object -ExpandProperty FullName)
        $expectedEntries = @($PackageFiles | ForEach-Object { "$packageName/$_" })
        Assert-ExactSet $expectedEntries $actualEntries "ZIP entries"
        foreach ($entryName in $actualEntries) {
            if ($entryName.Contains("..") -or $entryName.StartsWith("/") -or $entryName.Contains("\")) {
                throw "Unsafe ZIP entry: $entryName"
            }
        }
    } finally {
        $archive.Dispose()
    }

    $zipHash = (Get-FileHash -LiteralPath $temporaryZip -Algorithm SHA256).Hash
    if ($CurrentSeasonPreview) {
        Assert-NoPreviewExternalHash $externalHashPath "before publication"
    }
    Publish-FileAtomically $temporaryZip $zipPath
    if (!$CurrentSeasonPreview) {
        $temporaryHash = Join-Path $archiveRoot "$zipName.sha256"
        Write-Utf8NoBom $temporaryHash ("{0}  {1}`n" -f $zipHash, $zipName)
        Publish-FileAtomically $temporaryHash $externalHashPath
    }
    if ($CurrentSeasonPreview) {
        Assert-NoPreviewExternalHash $externalHashPath "after publication"
    }

    $publishedHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
    if ($publishedHash -ne $zipHash) { throw "Published ZIP hash mismatch" }
    if ($CurrentSeasonPreview) {
        Write-Host "PASS preview offline package zip=$zipPath bytes=$((Get-Item -LiteralPath $zipPath).Length)"
    } else {
        Write-Host "PASS offline package zip=$zipPath bytes=$((Get-Item -LiteralPath $zipPath).Length) sha256=$publishedHash"
    }
} finally {
    if (Test-Path -LiteralPath $tempRoot) {
        $resolvedTemp = [IO.Path]::GetFullPath($tempRoot).TrimEnd('\')
        $tempPrefix = [IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\bobcoach-offline-package-'
        if (!$resolvedTemp.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Unsafe package temp cleanup path: $resolvedTemp"
        }
        Remove-Item -LiteralPath $resolvedTemp -Recurse -Force -ErrorAction Stop
    }
}
