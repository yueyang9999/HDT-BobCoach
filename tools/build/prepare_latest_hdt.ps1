[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$OutputDirectory,

    [string]$GitHubEnvironmentFile
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
$apiUri = "https://api.github.com/repos/HearthSim/Hearthstone-Deck-Tracker/releases/latest"
$headers = @{
    Accept = "application/vnd.github+json"
    "User-Agent" = "HDT-BobCoach-CI"
    "X-GitHub-Api-Version" = "2022-11-28"
}

$release = Invoke-RestMethod -Uri $apiUri -Headers $headers
if ([bool]$release.draft -or [bool]$release.prerelease) {
    throw "GitHub latest release is not a stable published release"
}
if ([string]$release.tag_name -notmatch '^v(\d+\.\d+\.\d+)$') {
    throw "Unexpected HDT release tag: $($release.tag_name)"
}

$version = $Matches[1]
$assetName = "Hearthstone.Deck.Tracker-v$version.zip"
$assets = @($release.assets | Where-Object { [string]$_.name -eq $assetName })
if ($assets.Count -ne 1) {
    throw "Expected one official HDT ZIP asset named $assetName, found $($assets.Count)"
}
$asset = $assets[0]
$expectedUrl = "https://github.com/HearthSim/Hearthstone-Deck-Tracker/releases/download/v$version/$assetName"
if ([string]$asset.browser_download_url -ne $expectedUrl) {
    throw "Unexpected HDT asset URL: $($asset.browser_download_url)"
}
if ([long]$asset.size -le 0) {
    throw "Invalid HDT asset size: $($asset.size)"
}
if ([string]$asset.digest -notmatch '^sha256:([a-fA-F0-9]{64})$') {
    throw "Missing or invalid HDT asset digest"
}
$expectedHash = $Matches[1].ToLowerInvariant()

$outputRoot = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\')
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$archivePath = Join-Path $outputRoot $assetName
$extractRoot = Join-Path $outputRoot "hdt-$version"

Invoke-WebRequest -Uri $expectedUrl -Headers $headers -OutFile $archivePath
$archive = Get-Item -LiteralPath $archivePath
if ($archive.Length -ne [long]$asset.size) {
    throw "Unexpected HDT archive size: expected $($asset.size), got $($archive.Length)"
}
$actualHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualHash -ne $expectedHash) {
    throw "Unexpected HDT archive SHA-256: expected $expectedHash, got $actualHash"
}

Expand-Archive -LiteralPath $archivePath -DestinationPath $extractRoot -Force
$executables = @(Get-ChildItem -LiteralPath $extractRoot -Recurse -File -Filter 'Hearthstone Deck Tracker.exe')
if ($executables.Count -ne 1) {
    throw "Expected one official HDT executable, found $($executables.Count)"
}
$hdtDirectory = $executables[0].Directory.FullName
$buildReference = Join-Path $hdtDirectory 'HearthstoneDeckTracker.exe'
Copy-Item -LiteralPath $executables[0].FullName -Destination $buildReference
foreach ($fileName in @('HearthstoneDeckTracker.exe', 'HearthDb.dll', 'Newtonsoft.Json.dll')) {
    if (!(Test-Path -LiteralPath (Join-Path $hdtDirectory $fileName) -PathType Leaf)) {
        throw "Missing HDT build reference: $fileName"
    }
}

$assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($buildReference).Version.ToString()
if (![string]::IsNullOrWhiteSpace($GitHubEnvironmentFile)) {
    "BOBCOACH_HDT_DIR=$hdtDirectory" | Add-Content -LiteralPath $GitHubEnvironmentFile -Encoding utf8
    "BOBCOACH_LATEST_HDT_VERSION=$version" | Add-Content -LiteralPath $GitHubEnvironmentFile -Encoding utf8
    "BOBCOACH_LATEST_HDT_ASSEMBLY_VERSION=$assemblyVersion" |
        Add-Content -LiteralPath $GitHubEnvironmentFile -Encoding utf8
}

Write-Host "PASS latest HDT stable=v$version assembly=$assemblyVersion bytes=$($archive.Length) sha256=$actualHash"
[pscustomobject]@{
    Version = $version
    AssemblyVersion = $assemblyVersion
    HdtDirectory = $hdtDirectory
    ArchivePath = $archivePath
    ArchiveSize = $archive.Length
    ArchiveSha256 = $actualHash
}
