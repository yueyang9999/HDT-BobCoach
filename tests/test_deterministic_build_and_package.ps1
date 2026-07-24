$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$buildScript = Join-Path $repoRoot "tools\build\build_release.ps1"
$packageScript = Join-Path $repoRoot "tools\release\build_offline_package.ps1"
$hdtDirectory = $env:BOBCOACH_HDT_DIR
$testRoot = Join-Path $env:TEMP ("bobcoach-deterministic-build-package-" + [Guid]::NewGuid().ToString("N"))
$packageName = "BobCoach-1.0.0-win-x64"
$zipName = "$packageName.zip"
$packageFiles = @(
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
$zipEntryTimestamp = [DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
$failures = New-Object System.Collections.Generic.List[string]

function Invoke-CheckedScript([string]$Path, [string[]]$Arguments, [string]$Label) {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $Path @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Label failed with exit code $LASTEXITCODE"
    }
}

function Add-FailureIfDifferent([string]$Expected, [string]$Actual, [string]$Label) {
    if ($Expected -ne $Actual) {
        $failures.Add("$Label expected=$Expected actual=$Actual")
    }
}

try {
    if ([string]::IsNullOrWhiteSpace($hdtDirectory) -or !(Test-Path -LiteralPath $hdtDirectory -PathType Container)) {
        throw "HDT test baseline missing; set BOBCOACH_HDT_DIR: $hdtDirectory"
    }
    foreach ($path in @($buildScript, $packageScript)) {
        if (!(Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Required script missing: $path"
        }
    }

    New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
    $buildOutputOne = Join-Path $testRoot "build-one"
    $buildOutputTwo = Join-Path $testRoot "build-two"
    Invoke-CheckedScript $buildScript @("-HdtDirectory", $hdtDirectory, "-OutputDirectory", $buildOutputOne, "-Force") "first release build"
    Invoke-CheckedScript $buildScript @("-HdtDirectory", $hdtDirectory, "-OutputDirectory", $buildOutputTwo, "-Force") "second release build"

    $dllOne = Join-Path $buildOutputOne "BobCoach.dll"
    $dllTwo = Join-Path $buildOutputTwo "BobCoach.dll"
    $dllHashOne = (Get-FileHash -LiteralPath $dllOne -Algorithm SHA256).Hash
    $dllHashTwo = (Get-FileHash -LiteralPath $dllTwo -Algorithm SHA256).Hash
    Add-FailureIfDifferent $dllHashOne $dllHashTwo "independent release DLL SHA-256"

    $approvedCandidateSize = (Get-Item -LiteralPath $dllOne).Length
    $approvedCandidateHash = $dllHashOne
    $packageOutputOne = Join-Path $testRoot "package-one"
    $packageOutputTwo = Join-Path $testRoot "package-two"
    (Get-Item -LiteralPath $dllOne).LastWriteTime = [DateTime]::new(2001, 2, 3, 4, 5, 6)
    Invoke-CheckedScript $packageScript @(
        "-HdtDirectory", $hdtDirectory,
        "-OutputDirectory", $packageOutputOne,
        "-ApprovedCandidateDllPath", $dllOne,
        "-ApprovedCandidateSize", $approvedCandidateSize,
        "-ApprovedCandidateSha256", $approvedCandidateHash
    ) "first approved-candidate package build"

    Start-Sleep -Seconds 3
    (Get-Item -LiteralPath $dllOne).LastWriteTime = [DateTime]::new(2003, 4, 5, 6, 7, 8)
    Invoke-CheckedScript $packageScript @(
        "-HdtDirectory", $hdtDirectory,
        "-OutputDirectory", $packageOutputTwo,
        "-ApprovedCandidateDllPath", $dllOne,
        "-ApprovedCandidateSize", $approvedCandidateSize,
        "-ApprovedCandidateSha256", $approvedCandidateHash
    ) "second approved-candidate package build"

    $zipOne = Join-Path $packageOutputOne $zipName
    $zipTwo = Join-Path $packageOutputTwo $zipName
    $zipHashOne = (Get-FileHash -LiteralPath $zipOne -Algorithm SHA256).Hash
    $zipHashTwo = (Get-FileHash -LiteralPath $zipTwo -Algorithm SHA256).Hash
    Add-FailureIfDifferent $zipHashOne $zipHashTwo "approved-candidate offline ZIP SHA-256"

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    foreach ($zipPath in @($zipOne, $zipTwo)) {
        $archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
        try {
            $entries = @($archive.Entries | Where-Object { !$_.FullName.EndsWith("/") })
            $entryNames = @($entries | Select-Object -ExpandProperty FullName)
            $expectedEntryNames = @($packageFiles | ForEach-Object { "$packageName/$_" })
            if (@(Compare-Object $expectedEntryNames $entryNames -SyncWindow 0).Count -ne 0) {
                $failures.Add("ZIP entry order mismatch: $zipPath")
            }
            foreach ($entry in $entries) {
                if ($entry.LastWriteTime.DateTime -ne $zipEntryTimestamp.DateTime) {
                    $failures.Add("ZIP entry timestamp is not fixed: $($entry.FullName)=$($entry.LastWriteTime)")
                }
            }
        } finally {
            $archive.Dispose()
        }
    }

    if ($failures.Count -gt 0) {
        throw ($failures -join [Environment]::NewLine)
    }
    Write-Host "PASS deterministic release DLL and approved-candidate offline ZIP hashes"
} catch {
    Write-Host "FAIL deterministic release build and offline package"
    Write-Host $_.Exception.Message
    exit 1
} finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
