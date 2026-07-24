$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "offline_package_test_helpers.ps1")

Add-Type -AssemblyName System.IO.Compression.FileSystem

$repoRoot = Split-Path -Parent $PSScriptRoot
$builder = Join-Path $repoRoot "tools\release\build_offline_package.ps1"
$hdtDirectory = $env:BOBCOACH_HDT_DIR
if ([string]::IsNullOrWhiteSpace($hdtDirectory)) {
    $gameRoot = "D:\software\game"
    $hdtDirectory = @([IO.Directory]::GetDirectories($gameRoot, "HDT*") | ForEach-Object {
        $candidate = Join-Path $_ "HDT"
        $executable = Join-Path $candidate "HearthstoneDeckTracker.exe"
        if (Test-Path -LiteralPath $executable -PathType Leaf) {
            try {
                if ([Reflection.AssemblyName]::GetAssemblyName($executable).Version.ToString() -eq "1.53.5.7354") {
                    $candidate
                }
            } catch { }
        }
    } | Select-Object -First 1)
}
$testRoot = Join-Path $env:TEMP ("bobcoach-offline-builder-test-" + [Guid]::NewGuid().ToString("N"))
Assert-SafeTestRoot $testRoot

$packageName = "BobCoach-1.0.0-win-x64"
$zipName = "$packageName.zip"
$expectedFiles = @(
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
$expectedEntries = @($expectedFiles | ForEach-Object { "$packageName/$_" } | Sort-Object)

function Assert-ExactStrings([string[]]$Expected, [string[]]$Actual, [string]$Label) {
    $delta = @(Compare-Object @($Expected | Sort-Object) @($Actual | Sort-Object))
    if ($delta.Count -ne 0) {
        throw "Assertion failed: $Label delta=$($delta.Count)"
    }
}

function Assert-ExtractedPackage([string]$PackageRoot, [switch]$SkipReflectionLoad) {
    $actualFiles = @(
        Get-ChildItem -LiteralPath $PackageRoot -Recurse -File -Force -Name |
            ForEach-Object { $_.Replace('\', '/') }
    )
    Assert-ExactStrings $expectedFiles $actualFiles "extracted file set"

    $manifest = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $PackageRoot "manifest.json") | ConvertFrom-Json
    Assert-Equal 2 ([int]$manifest.schemaVersion) "manifest schema"
    Assert-Equal "1.0.0" ([string]$manifest.packageVersion) "manifest package version"
    Assert-Equal "1.0.0.0" ([string]$manifest.assemblyVersion) "manifest assembly version"
    Assert-Equal "1.0.0.0" ([string]$manifest.fileVersion) "manifest file version"
    Assert-Equal "1.0.0" ([string]$manifest.informationalVersion) "manifest informational version"
    Assert-Equal ".NETFramework,Version=v4.7.2" ([string]$manifest.targetFramework) "manifest framework"
    Assert-Equal "win-x64" ([string]$manifest.runtimeIdentifier) "manifest RID"
    Assert-ExactStrings $expectedFiles @($manifest.files) "manifest files"
    Assert-Equal "BobCoach.dll.sha256" ([string]$manifest.pluginChecksumFile) "manifest plugin checksum file"

    $pluginPath = Join-Path $PackageRoot "BobCoach.dll"
    $pluginHash = (Get-FileHash -LiteralPath $pluginPath -Algorithm SHA256).Hash
    Assert-Equal $pluginHash ([string]$manifest.pluginSha256) "manifest plugin hash"
    Assert-PluginChecksum $pluginPath (Join-Path $PackageRoot "BobCoach.dll.sha256") "built package"
    Assert-Equal (Get-Item -LiteralPath $pluginPath).Length ([long]$manifest.pluginSize) "manifest plugin size"
    $name = [Reflection.AssemblyName]::GetAssemblyName($pluginPath)
    Assert-Equal "BobCoach" $name.Name "plugin assembly name"
    Assert-Equal "1.0.0.0" $name.Version.ToString() "plugin assembly version"
    if (!$SkipReflectionLoad) {
        $bytes = [IO.File]::ReadAllBytes($pluginPath)
        $assembly = [Reflection.Assembly]::ReflectionOnlyLoad($bytes)
        $peKind = [Reflection.PortableExecutableKinds]::ILOnly
        $machine = [Reflection.ImageFileMachine]::I386
        $assembly.ManifestModule.GetPEKind([ref]$peKind, [ref]$machine)
        Assert-Equal ([Reflection.ImageFileMachine]::AMD64) $machine "plugin machine"
        Assert-True (($peKind -band [Reflection.PortableExecutableKinds]::PE32Plus) -ne 0) "plugin PE32Plus"
        Assert-True (($peKind -band [Reflection.PortableExecutableKinds]::ILOnly) -ne 0) "plugin ILOnly"
    }

    $sumLines = @(Get-Content -LiteralPath (Join-Path $PackageRoot "SHA256SUMS.txt") -Encoding UTF8)
    Assert-Equal 16 $sumLines.Count "SHA256SUMS line count"
    $seen = @{}
    foreach ($line in $sumLines) {
        if ($line -notmatch '^([A-F0-9]{64})  ([^\\:*?"<>|\r\n]+)$') { throw "Invalid SHA256SUMS line: $line" }
        $hash = $Matches[1]
        $fileName = $Matches[2]
        Assert-False $seen.ContainsKey($fileName) "duplicate SHA256SUMS file"
        $seen[$fileName] = $true
        Assert-Equal $hash (Get-FileHash -LiteralPath (Join-Path $PackageRoot $fileName) -Algorithm SHA256).Hash "package hash $fileName"
    }
    Assert-ExactStrings @($expectedFiles | Where-Object { $_ -ne "SHA256SUMS.txt" }) @($seen.Keys) "SHA256SUMS files"
}

try {
    if (!(Test-Path -LiteralPath $builder -PathType Leaf)) {
        throw "Offline package builder missing: $builder"
    }
    $builderSource = Get-Content -Raw -Encoding UTF8 -LiteralPath $builder
    Assert-False $builderSource.Contains("020C40CBC0927C230C74ED334995278D7D4669E16B4DEED38A92CD0F44804D37") "retired beta.1 preview hash omitted from active builder"
    Assert-False $builderSource.Contains("ApprovedPreviewPluginSize") "retired beta.1 preview size omitted from active builder"
    if ([string]::IsNullOrWhiteSpace([string]$hdtDirectory) -or !(Test-Path -LiteralPath $hdtDirectory -PathType Container)) {
        throw "HDT test baseline missing; set BOBCOACH_HDT_DIR: $hdtDirectory"
    }
    New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
    $outputDirectory = Join-Path $testRoot "Output"
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
    $sentinel = Join-Path $outputDirectory "retain.txt"
    Write-Utf8NoBom $sentinel "retain"

    $tempBefore = @(Get-ChildItem -LiteralPath $env:TEMP -Directory -Filter "bobcoach-offline-package-*" | Select-Object -ExpandProperty FullName)
    $build = Invoke-TestPowerShell $builder @("-HdtDirectory", $hdtDirectory, "-OutputDirectory", $outputDirectory)
    Assert-Equal 0 $build.ExitCode "offline package build exit"
    $zipPath = Join-Path $outputDirectory $zipName
    $externalHashPath = "$zipPath.sha256"
    Assert-True (Test-Path -LiteralPath $zipPath -PathType Leaf) "ZIP exists"
    Assert-True (Test-Path -LiteralPath $externalHashPath -PathType Leaf) "external hash exists"
    Assert-True (Test-Path -LiteralPath $sentinel -PathType Leaf) "output sentinel retained"

    $archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $entries = @($archive.Entries | Where-Object { !$_.FullName.EndsWith("/") } | Select-Object -ExpandProperty FullName)
        Assert-ExactStrings $expectedEntries $entries "ZIP entries"
    } finally {
        $archive.Dispose()
    }

    $extractRoot = Join-Path $testRoot "Extracted"
    [IO.Compression.ZipFile]::ExtractToDirectory($zipPath, $extractRoot)
    $extractedPackageRoot = Join-Path $extractRoot $packageName
    Assert-ExtractedPackage $extractedPackageRoot
    $releaseReadme = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $extractedPackageRoot "README_OFFLINE.md")
    Assert-True ($releaseReadme.Contains(
        "LOCAL RELEASE CANDIDATE / NOT A PUBLIC GITHUB RELEASE"
    )) "release README identifies the local non-public candidate"
    foreach ($previewOnlyText in @(
        "CURRENT SEASON PREVIEW",
        "NOT FINAL BETA",
        "NOT A FORMAL RELEASE",
        "current-season-preview-20260719",
        "PREVIEW ZIP HAS NO EXTERNAL SHA-256"
    )) {
        Assert-False ($releaseReadme.Contains($previewOnlyText)) "release README omits preview-only text: $previewOnlyText"
    }

    $externalLine = (Get-Content -Raw -Encoding UTF8 -LiteralPath $externalHashPath).Trim()
    if ($externalLine -notmatch ('^([A-F0-9]{64})  ' + [regex]::Escape($zipName) + '$')) {
        throw "Invalid external SHA-256 line: $externalLine"
    }
    Assert-Equal $Matches[1] (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash "external ZIP hash"

    $approvedCandidateDll = Join-Path $extractedPackageRoot "BobCoach.dll"
    $approvedCandidateSize = (Get-Item -LiteralPath $approvedCandidateDll).Length
    $approvedCandidateSha256 = (Get-FileHash -LiteralPath $approvedCandidateDll -Algorithm SHA256).Hash
    $approvedOutput = Join-Path $testRoot "ApprovedOutput"
    New-Item -ItemType Directory -Path $approvedOutput -Force | Out-Null

    $partialApprovedBuild = Invoke-TestPowerShell $builder @(
        "-HdtDirectory", $hdtDirectory,
        "-OutputDirectory", $approvedOutput,
        "-ApprovedCandidateDllPath", $approvedCandidateDll
    )
    Assert-True ($partialApprovedBuild.ExitCode -ne 0) "partial approved candidate contract rejected"

    $mixedPreviewApprovedBuild = Invoke-TestPowerShell $builder @(
        "-HdtDirectory", $hdtDirectory,
        "-OutputDirectory", $approvedOutput,
        "-CurrentSeasonPreview",
        "-ApprovedCandidateDllPath", $approvedCandidateDll,
        "-ApprovedCandidateSize", $approvedCandidateSize,
        "-ApprovedCandidateSha256", $approvedCandidateSha256
    )
    Assert-True ($mixedPreviewApprovedBuild.ExitCode -ne 0) "preview and approved candidate modes rejected"

    $retiredPreviewBuild = Invoke-TestPowerShell $builder @(
        "-HdtDirectory", $hdtDirectory,
        "-OutputDirectory", $outputDirectory,
        "-CurrentSeasonPreview",
        "-CandidateDllPath", $approvedCandidateDll
    )
    Assert-True ($retiredPreviewBuild.ExitCode -ne 0) "beta.2 preview build rejected"
    Assert-True ((@($retiredPreviewBuild.Output) -join "`n").Contains(
        "CurrentSeasonPreview is retained only for historical 0.2.0-beta.1 artifacts"
    )) "beta.2 preview rejection explains historical boundary"

    $wrongApprovedHash = "0" * 64
    $wrongApprovedBuild = Invoke-TestPowerShell $builder @(
        "-HdtDirectory", $hdtDirectory,
        "-OutputDirectory", $approvedOutput,
        "-ApprovedCandidateDllPath", $approvedCandidateDll,
        "-ApprovedCandidateSize", $approvedCandidateSize,
        "-ApprovedCandidateSha256", $wrongApprovedHash
    )
    Assert-True ($wrongApprovedBuild.ExitCode -ne 0) "approved candidate hash mismatch rejected"
    Assert-False (Test-Path -LiteralPath (Join-Path $approvedOutput $zipName)) "rejected approved candidate produces no ZIP"

    $approvedBuild = Invoke-TestPowerShell $builder @(
        "-HdtDirectory", $hdtDirectory,
        "-OutputDirectory", $approvedOutput,
        "-ApprovedCandidateDllPath", $approvedCandidateDll,
        "-ApprovedCandidateSize", $approvedCandidateSize,
        "-ApprovedCandidateSha256", $approvedCandidateSha256
    )
    Assert-Equal 0 $approvedBuild.ExitCode "approved candidate package build exit"
    $approvedZipPath = Join-Path $approvedOutput $zipName
    Assert-True (Test-Path -LiteralPath $approvedZipPath -PathType Leaf) "approved candidate ZIP exists"
    Assert-True (Test-Path -LiteralPath "$approvedZipPath.sha256" -PathType Leaf) "approved candidate external hash exists"
    $approvedExtractRoot = Join-Path $testRoot "ApprovedExtracted"
    [IO.Compression.ZipFile]::ExtractToDirectory($approvedZipPath, $approvedExtractRoot)
    $approvedPackagedDll = Join-Path (Join-Path $approvedExtractRoot $packageName) "BobCoach.dll"
    Assert-Equal $approvedCandidateSize (Get-Item -LiteralPath $approvedPackagedDll).Length "approved candidate packaged size"
    Assert-Equal $approvedCandidateSha256 (Get-FileHash -LiteralPath $approvedPackagedDll -Algorithm SHA256).Hash "approved candidate packaged hash"

    $zipHashBeforeRefusal = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
    $refusal = Invoke-TestPowerShell $builder @("-HdtDirectory", $hdtDirectory, "-OutputDirectory", $outputDirectory)
    Assert-True ($refusal.ExitCode -ne 0) "existing output requires Force"
    Assert-Equal $zipHashBeforeRefusal (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash "refusal retains ZIP"

    $force = Invoke-TestPowerShell $builder @("-HdtDirectory", $hdtDirectory, "-OutputDirectory", $outputDirectory, "-Force")
    Assert-Equal 0 $force.ExitCode "Force package build exit"
    Assert-True (Test-Path -LiteralPath $sentinel -PathType Leaf) "Force retains output sentinel"
    $tempAfter = @(Get-ChildItem -LiteralPath $env:TEMP -Directory -Filter "bobcoach-offline-package-*" | Select-Object -ExpandProperty FullName)
    Assert-ExactStrings $tempBefore $tempAfter "package temp cleanup"

    Write-Host "PASS offline package release ZIP whitelist, hashes, retired preview boundary, Force, and cleanup contracts"
} catch {
    Write-Host "FAIL offline package builder contracts"
    Write-Host $_.Exception.Message
    exit 1
} finally {
    if (Test-Path -LiteralPath $testRoot) {
        Assert-SafeTestRoot $testRoot
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
