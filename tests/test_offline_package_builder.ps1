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

$packageName = "BobCoach-0.2.0-beta.1-win-x64"
$zipName = "$packageName.zip"
$previewPackageName = "BobCoach-0.2.0-beta.1-current-season-preview-20260719-win-x64"
$previewZipName = "$previewPackageName.zip"
$approvedPreviewPluginSize = 650240
$approvedPreviewPluginSha256 = "020C40CBC0927C230C74ED334995278D7D4669E16B4DEED38A92CD0F44804D37"
$previewCandidateDll = $env:BOBCOACH_PREVIEW_CANDIDATE_DLL
$previewIntegrationStatus = "skipped"
$expectedFiles = @(
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
$expectedEntries = @($expectedFiles | ForEach-Object { "$packageName/$_" } | Sort-Object)

function Assert-ExactStrings([string[]]$Expected, [string[]]$Actual, [string]$Label) {
    $delta = @(Compare-Object @($Expected | Sort-Object) @($Actual | Sort-Object))
    if ($delta.Count -ne 0) {
        throw "Assertion failed: $Label delta=$($delta.Count)"
    }
}

function Assert-ExtractedPackage([string]$PackageRoot, [switch]$SkipReflectionLoad) {
    $actualFiles = @(Get-ChildItem -LiteralPath $PackageRoot -Force | Select-Object -ExpandProperty Name)
    Assert-ExactStrings $expectedFiles $actualFiles "extracted file set"

    $manifest = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $PackageRoot "manifest.json") | ConvertFrom-Json
    Assert-Equal 1 ([int]$manifest.schemaVersion) "manifest schema"
    Assert-Equal "0.2.0-beta.1" ([string]$manifest.packageVersion) "manifest package version"
    Assert-Equal "0.2.0.0" ([string]$manifest.assemblyVersion) "manifest assembly version"
    Assert-Equal "0.2.0.0" ([string]$manifest.fileVersion) "manifest file version"
    Assert-Equal "0.2.0-beta.1" ([string]$manifest.informationalVersion) "manifest informational version"
    Assert-Equal ".NETFramework,Version=v4.7.2" ([string]$manifest.targetFramework) "manifest framework"
    Assert-Equal "win-x64" ([string]$manifest.runtimeIdentifier) "manifest RID"
    Assert-ExactStrings $expectedFiles @($manifest.files) "manifest files"

    $pluginPath = Join-Path $PackageRoot "BobCoach.dll"
    $pluginHash = (Get-FileHash -LiteralPath $pluginPath -Algorithm SHA256).Hash
    Assert-Equal $pluginHash ([string]$manifest.pluginSha256) "manifest plugin hash"
    Assert-Equal (Get-Item -LiteralPath $pluginPath).Length ([long]$manifest.pluginSize) "manifest plugin size"
    $name = [Reflection.AssemblyName]::GetAssemblyName($pluginPath)
    Assert-Equal "BobCoach" $name.Name "plugin assembly name"
    Assert-Equal "0.2.0.0" $name.Version.ToString() "plugin assembly version"
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
    Assert-Equal 10 $sumLines.Count "SHA256SUMS line count"
    $seen = @{}
    foreach ($line in $sumLines) {
        if ($line -notmatch '^([A-F0-9]{64})  ([^\\/:*?"<>|]+)$') { throw "Invalid SHA256SUMS line: $line" }
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

    if (![string]::IsNullOrWhiteSpace($previewCandidateDll)) {
      if (!(Test-Path -LiteralPath $previewCandidateDll -PathType Leaf)) {
          throw "Approved preview candidate missing: $previewCandidateDll"
      }
    Assert-Equal $approvedPreviewPluginSize (Get-Item -LiteralPath $previewCandidateDll).Length "approved preview candidate size"
    Assert-Equal $approvedPreviewPluginSha256 (Get-FileHash -LiteralPath $previewCandidateDll -Algorithm SHA256).Hash "approved preview candidate hash"

    $wrongCandidateDll = Join-Path $extractedPackageRoot "BobCoach.dll"
    Assert-False ((Get-FileHash -LiteralPath $wrongCandidateDll -Algorithm SHA256).Hash -eq $approvedPreviewPluginSha256) "wrong preview candidate fixture differs"
    $wrongCandidateBuild = Invoke-TestPowerShell $builder @(
        "-HdtDirectory", $hdtDirectory,
        "-OutputDirectory", $outputDirectory,
        "-CurrentSeasonPreview",
        "-CandidateDllPath", $wrongCandidateDll
    )
    Assert-True ($wrongCandidateBuild.ExitCode -ne 0) "unapproved preview candidate rejected"
    $previewZipPath = Join-Path $outputDirectory $previewZipName
    Assert-False (Test-Path -LiteralPath $previewZipPath) "unapproved preview candidate produces no ZIP"

    $previewConflictOutput = Join-Path $testRoot "PreviewConflictOutput"
    New-Item -ItemType Directory -Path $previewConflictOutput -Force | Out-Null
    $previewConflictZip = Join-Path $previewConflictOutput $previewZipName
    $previewConflictExternalHash = "$previewConflictZip.sha256"
    Write-Utf8NoBom $previewConflictExternalHash "sentinel-preview-external-hash"
    $previewConflictHashBefore = (Get-FileHash -LiteralPath $previewConflictExternalHash -Algorithm SHA256).Hash
    $previewConflictCases = @(
        [pscustomobject]@{ Label = "without Force"; Arguments = @() },
        [pscustomobject]@{ Label = "with Force"; Arguments = @("-Force") }
    )
    foreach ($previewConflictCase in $previewConflictCases) {
        $previewConflictArgs = @(
            "-HdtDirectory", $hdtDirectory,
            "-OutputDirectory", $previewConflictOutput,
            "-CurrentSeasonPreview",
            "-CandidateDllPath", $previewCandidateDll
        ) + $previewConflictCase.Arguments
        $previewConflictBuild = Invoke-TestPowerShell $builder $previewConflictArgs
        Assert-True ($previewConflictBuild.ExitCode -ne 0) "preview residual external hash rejected $($previewConflictCase.Label)"
        Assert-False (Test-Path -LiteralPath $previewConflictZip) "preview residual external hash produces no ZIP"
        Assert-Equal $previewConflictHashBefore (Get-FileHash -LiteralPath $previewConflictExternalHash -Algorithm SHA256).Hash "preview residual external hash retained"
    }

    $previewBuild = Invoke-TestPowerShell $builder @(
        "-HdtDirectory", $hdtDirectory,
        "-OutputDirectory", $outputDirectory,
        "-CurrentSeasonPreview",
        "-CandidateDllPath", $previewCandidateDll
    )
    Assert-Equal 0 $previewBuild.ExitCode "preview package build exit"
    Assert-True (Test-Path -LiteralPath $previewZipPath -PathType Leaf) "preview ZIP exists"
    Assert-False (Test-Path -LiteralPath "$previewZipPath.sha256") "preview external hash omitted"

    $previewArchive = [IO.Compression.ZipFile]::OpenRead($previewZipPath)
    try {
        $previewEntries = @($previewArchive.Entries | Where-Object { !$_.FullName.EndsWith("/") } | Select-Object -ExpandProperty FullName)
        $expectedPreviewEntries = @($expectedFiles | ForEach-Object { "$previewPackageName/$_" } | Sort-Object)
        Assert-ExactStrings $expectedPreviewEntries $previewEntries "preview ZIP entries"
    } finally {
        $previewArchive.Dispose()
    }

    $previewExtractRoot = Join-Path $testRoot "PreviewExtracted"
    [IO.Compression.ZipFile]::ExtractToDirectory($previewZipPath, $previewExtractRoot)
    $previewPackageRoot = Join-Path $previewExtractRoot $previewPackageName
    Assert-ExtractedPackage $previewPackageRoot -SkipReflectionLoad
    $previewPluginPath = Join-Path $previewPackageRoot "BobCoach.dll"
    Assert-Equal $approvedPreviewPluginSize (Get-Item -LiteralPath $previewPluginPath).Length "preview approved DLL size"
    Assert-Equal $approvedPreviewPluginSha256 (Get-FileHash -LiteralPath $previewPluginPath -Algorithm SHA256).Hash "preview approved DLL hash"
    $previewManifest = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $previewPackageRoot "manifest.json") | ConvertFrom-Json
    Assert-Equal $approvedPreviewPluginSize ([long]$previewManifest.pluginSize) "preview manifest approved DLL size"
    Assert-Equal $approvedPreviewPluginSha256 ([string]$previewManifest.pluginSha256) "preview manifest approved DLL hash"
    $previewReadme = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $previewPackageRoot "README_OFFLINE.md")
    $unconditionalPreviewIdentity = -join @(0x672C, 0x5305, 0x662F, 0x5F53, 0x524D, 0x8D5B, 0x5B63, 0x62A2, 0x5148, 0x9A8C, 0x8BC1, 0x7248 | ForEach-Object { [char]$_ })
    $directoryDependentIdentity = -join @(0x5F53, 0x89E3, 0x538B, 0x76EE, 0x5F55, 0x540D, 0x5305, 0x542B | ForEach-Object { [char]$_ })
    Assert-True $previewReadme.Contains("CURRENT SEASON PREVIEW") "preview README identity"
    Assert-True $previewReadme.Contains($unconditionalPreviewIdentity) "preview README unconditional identity"
    Assert-False $previewReadme.Contains($directoryDependentIdentity) "preview README identity does not depend on directory name"
    Assert-True $previewReadme.Contains("NOT FINAL BETA") "preview README non-final warning"
    Assert-True $previewReadme.Contains("NOT A FORMAL RELEASE") "preview README release warning"
    Assert-True $previewReadme.Contains($previewPackageName) "preview README package identity"
    Assert-True ($previewReadme.Contains("PREVIEW ZIP HAS NO EXTERNAL SHA-256")) "preview README external hash boundary"
      Assert-True $previewReadme.Contains("USER LOGIN REQUIRED") "preview README login boundary"
      Assert-True $previewReadme.Contains("ONLINE BATTLEGROUNDS MATCH") "preview README online match step"
      $previewIntegrationStatus = "verified"
    }

    $zipHashBeforeRefusal = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
    $refusal = Invoke-TestPowerShell $builder @("-HdtDirectory", $hdtDirectory, "-OutputDirectory", $outputDirectory)
    Assert-True ($refusal.ExitCode -ne 0) "existing output requires Force"
    Assert-Equal $zipHashBeforeRefusal (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash "refusal retains ZIP"

    $force = Invoke-TestPowerShell $builder @("-HdtDirectory", $hdtDirectory, "-OutputDirectory", $outputDirectory, "-Force")
    Assert-Equal 0 $force.ExitCode "Force package build exit"
    Assert-True (Test-Path -LiteralPath $sentinel -PathType Leaf) "Force retains output sentinel"
    $tempAfter = @(Get-ChildItem -LiteralPath $env:TEMP -Directory -Filter "bobcoach-offline-package-*" | Select-Object -ExpandProperty FullName)
    Assert-ExactStrings $tempBefore $tempAfter "package temp cleanup"

    Write-Host "PASS offline package release ZIP whitelist, hashes, Force, and cleanup contracts previewIntegration=$previewIntegrationStatus"
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
