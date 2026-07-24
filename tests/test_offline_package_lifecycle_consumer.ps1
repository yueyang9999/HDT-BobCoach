[CmdletBinding()]
param(
    [ValidateSet("All", "Success", "Preflight", "DryRun", "ChildFailure", "CanonicalPath", "OutputFlood", "TerminalEvidenceFailure", "ResultPathTakeover", "EnvironmentIsolation", "NestedReparse", "FinalStateCaptureFailure", "StepEvidenceFailure", "DoubleFailure")]
    [string[]]$Scenario = @("All")
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "offline_package_test_helpers.ps1")

$repoRoot = Split-Path -Parent $PSScriptRoot
$consumerPath = Join-Path $repoRoot "tools\release\verify_offline_package_lifecycle.ps1"
$candidatePath = if ([string]::IsNullOrWhiteSpace($env:BOBCOACH_TEST_DLL)) {
    Join-Path $repoRoot "src\BobCoach\BobCoach.dll"
} else {
    [IO.Path]::GetFullPath($env:BOBCOACH_TEST_DLL)
}
$script:PackageFiles = @(
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

function Get-TestHash([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

function Assert-Contains([string]$Needle, [string]$Haystack, [string]$Label) {
    if ($Haystack.IndexOf($Needle, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
        throw "Assertion failed: $Label missing=$Needle"
    }
}

function Assert-ExactNames([string[]]$Expected, [string[]]$Actual, [string]$Label) {
    Assert-Equal $Expected.Count $Actual.Count "$Label count"
    for ($index = 0; $index -lt $Expected.Count; $index++) {
        Assert-Equal $Expected[$index] $Actual[$index] "$Label item $index"
    }
}

function Assert-FileStateEqual($Expected, $Actual, [string]$Label) {
    Assert-Equal $Expected.Exists $Actual.Exists "$Label exists"
    if ($Expected.Exists) {
        Assert-Equal $Expected.Bytes $Actual.Bytes "$Label bytes"
        Assert-Equal $Expected.Sha256 $Actual.Sha256 "$Label SHA-256"
        Assert-Equal $Expected.Attributes $Actual.Attributes "$Label attributes"
        Assert-Equal $Expected.LastWriteUtc $Actual.LastWriteUtc "$Label last write"
    }
}

function Assert-UserDataState($State, [bool]$ExpectedExists, [string]$ExpectedHash, [string]$Label) {
    Assert-Equal $ExpectedExists $State.Exists "$Label exists"
    if ($ExpectedExists) {
        Assert-Equal 1 @($State.Files).Count "$Label file count"
        Assert-Equal "lifecycle-sentinel.txt" $State.Files[0].RelativePath "$Label sentinel path"
        Assert-Equal $ExpectedHash $State.Files[0].Sha256 "$Label sentinel hash"
    } else {
        Assert-Equal 0 @($State.Files).Count "$Label file count"
    }
}

function Get-StateEvidence([string]$EvidenceDirectory, [int]$Number, [string]$Name) {
    $path = Join-Path $EvidenceDirectory ("states\{0:D2}-{1}.json" -f $Number, $Name)
    Assert-True (Test-Path -LiteralPath $path -PathType Leaf) "state evidence $Number $Name exists"
    return Get-Content -Raw -Encoding UTF8 -LiteralPath $path | ConvertFrom-Json
}

function Assert-TargetState($State, [string]$ExpectedHash, [string]$Label) {
    if ([string]::IsNullOrWhiteSpace($ExpectedHash)) {
        Assert-False $State.Exists "$Label target absent"
    } else {
        Assert-True $State.Exists "$Label target exists"
        Assert-Equal $ExpectedHash $State.Sha256 "$Label target hash"
    }
}

function Assert-BackupHashes($State, [string[]]$ExpectedHashes, [string]$Label) {
    $actual = @($State | ForEach-Object { $_.Sha256 } | Sort-Object)
    $expected = @($ExpectedHashes | Sort-Object)
    Assert-Equal $expected.Count $actual.Count "$Label backup count"
    for ($index = 0; $index -lt $expected.Count; $index++) {
        Assert-Equal $expected[$index] $actual[$index] "$Label backup hash $index"
    }
}

function Get-TargetSnapshot([string]$PluginDirectory) {
    if (!(Test-Path -LiteralPath $PluginDirectory -PathType Container)) { return @() }
    $root = Get-Item -LiteralPath $PluginDirectory -Force
    $rootPath = $root.FullName.TrimEnd('\')
    return @(
        @($root) + @(Get-ChildItem -LiteralPath $PluginDirectory -Recurse -Force) |
            Sort-Object FullName |
            ForEach-Object {
                $relativePath = if ($_.FullName.TrimEnd('\').Equals($rootPath, [StringComparison]::OrdinalIgnoreCase)) {
                    "."
                } else {
                    $_.FullName.Substring($rootPath.Length + 1)
                }
                if ($_.PSIsContainer) {
                    "{0}|Directory|{1}" -f $relativePath, [int]$_.Attributes
                } else {
                    "{0}|File|{1}|{2}|{3}" -f $relativePath, [int]$_.Attributes, $_.Length, (Get-TestHash $_.FullName)
                }
            }
    )
}

function Assert-Snapshot([string[]]$Expected, [string]$PluginDirectory, [string]$Label) {
    $actual = @(Get-TargetSnapshot $PluginDirectory)
    Assert-Equal $Expected.Count $actual.Count "$Label count"
    for ($index = 0; $index -lt $Expected.Count; $index++) {
        Assert-Equal $Expected[$index] $actual[$index] "$Label item $index"
    }
}

function Write-TestManifestAndSums([string]$PackageRoot) {
    $pluginPath = Join-Path $PackageRoot "BobCoach.dll"
    $pluginHash = Get-TestHash $pluginPath
    $pluginSize = (Get-Item -LiteralPath $pluginPath).Length
    $manifest = [ordered]@{
        schemaVersion = 2
        packageVersion = "1.0.0"
        assemblyVersion = "1.0.0.0"
        fileVersion = "1.0.0.0"
        informationalVersion = "1.0.0"
        targetFramework = ".NETFramework,Version=v4.7.2"
        runtimeIdentifier = "win-x64"
        hdtBaselineVersion = "1.53.5.7354"
        pluginFile = "BobCoach.dll"
        pluginChecksumFile = "BobCoach.dll.sha256"
        pluginSize = $pluginSize
        pluginSha256 = $pluginHash
        files = $script:PackageFiles
    }
    Write-Utf8NoBom (Join-Path $PackageRoot "manifest.json") (($manifest | ConvertTo-Json -Depth 4) + "`n")

    Write-TestInternalSums $PackageRoot
}

function Write-TestInternalSums([string]$PackageRoot) {
    $lines = foreach ($fileName in ($script:PackageFiles | Where-Object { $_ -ne "SHA256SUMS.txt" } | Sort-Object)) {
        "{0}  {1}" -f (Get-TestHash (Join-Path $PackageRoot $fileName)), $fileName
    }
    Write-Utf8NoBom (Join-Path $PackageRoot "SHA256SUMS.txt") (($lines -join "`n") + "`n")
}

function Publish-TestZip(
    [string]$PackageRoot,
    [string]$ZipPath,
    [string[]]$AdditionalEntries = @(),
    [switch]$DuplicateReadme
) {
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    if (Test-Path -LiteralPath $ZipPath) { [IO.File]::Delete($ZipPath) }
    $packageName = Split-Path -Leaf $PackageRoot
    $archive = [IO.Compression.ZipFile]::Open($ZipPath, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($fileName in $script:PackageFiles) {
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $archive,
                (Join-Path $PackageRoot $fileName),
                "$packageName/$fileName",
                [IO.Compression.CompressionLevel]::Optimal
            ) | Out-Null
        }
        if ($DuplicateReadme) {
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $archive,
                (Join-Path $PackageRoot "README_OFFLINE.md"),
                "$packageName/README_OFFLINE.md",
                [IO.Compression.CompressionLevel]::Optimal
            ) | Out-Null
        }
        foreach ($entryName in $AdditionalEntries) {
            $entry = $archive.CreateEntry($entryName)
            $writer = New-Object IO.StreamWriter($entry.Open(), (New-Object Text.UTF8Encoding($false)))
            try { $writer.Write("unexpected") } finally { $writer.Dispose() }
        }
    } finally {
        $archive.Dispose()
    }
}

function Set-ZipEntryExternalAttributes([string]$ZipPath, [string]$LeafName, [uint32]$Attributes) {
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::Open($ZipPath, [IO.Compression.ZipArchiveMode]::Update)
    try {
        $entry = @($archive.Entries | Where-Object { $_.FullName.EndsWith("/$LeafName", [StringComparison]::Ordinal) })
        Assert-Equal 1 $entry.Count "ZIP attribute entry count"
        $entry[0].ExternalAttributes = [BitConverter]::ToInt32([BitConverter]::GetBytes($Attributes), 0)
    } finally {
        $archive.Dispose()
    }
}

function Write-ExternalHash([string]$ZipPath, [string]$Sha256Path) {
    Write-Utf8NoBom $Sha256Path ("{0}  {1}`n" -f (Get-TestHash $ZipPath), (Split-Path -Leaf $ZipPath))
}

function New-PackageFixture([string]$Root, [string]$Name) {
    Assert-SafeTestRoot $Root
    if (!(Test-Path -LiteralPath $candidatePath -PathType Leaf)) {
        throw "Formal BobCoach.dll missing: $candidatePath"
    }

    $fixtureRoot = Join-Path $Root $Name
    $packageRoot = Join-Path $fixtureRoot "BobCoach-1.0.0-win-x64"
    $hdtDirectory = Join-Path $fixtureRoot "SyntheticHdt"
    $appDataRoot = Join-Path $fixtureRoot "IsolatedAppData"
    $pluginDirectory = Join-Path (Join-Path $appDataRoot "HearthstoneDeckTracker") "Plugins"
    $evidenceDirectory = Join-Path $fixtureRoot "Evidence"
    $logConfigPath = Join-Path $fixtureRoot "log.config"
    $previousPluginPath = Join-Path $fixtureRoot "previous\BobCoach.dll"
    $zipPath = Join-Path $fixtureRoot "BobCoach-1.0.0-win-x64.zip"
    $sha256Path = "$zipPath.sha256"

    New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $hdtDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path (Split-Path -Parent $previousPluginPath) -Force | Out-Null
    Write-Utf8NoBom (Join-Path $hdtDirectory "HearthstoneDeckTracker.exe") "synthetic HDT fixture"
    Write-Utf8NoBom $logConfigPath "isolated log.config sentinel`n"
    (Get-Item -LiteralPath $logConfigPath).IsReadOnly = $true
    Copy-Item -LiteralPath $candidatePath -Destination (Join-Path $packageRoot "BobCoach.dll")
    Write-PluginChecksumFile (Join-Path $packageRoot "BobCoach.dll") (Join-Path $packageRoot "BobCoach.dll.sha256")
    Copy-Item -LiteralPath (Join-Path $repoRoot "tools\release\INSTALL.ps1") -Destination $packageRoot
    Copy-Item -LiteralPath (Join-Path $repoRoot "tools\release\UNINSTALL.ps1") -Destination $packageRoot
    Copy-Item -LiteralPath (Join-Path $repoRoot "docs\user\INSTALL.html") -Destination (Join-Path $packageRoot "安装教程.html")
    foreach ($imageName in @(
        "install-01-exit-hdt.png",
        "install-02-open-plugins-folder.png",
        "install-03-copy-bobcoach-dll.png",
        "install-04-enable-bobcoach.png"
    )) {
        $destination = Join-Path $packageRoot "images\install\$imageName"
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $repoRoot "docs\user\images\install\$imageName") -Destination $destination
    }
    foreach ($fileName in @("README_OFFLINE.md", "LICENSE", "NOTICE", "DATA_SOURCES.md", "PRIVACY.md", "SUPPORT.md")) {
        $source = if ($fileName -eq "README_OFFLINE.md") {
            Join-Path $repoRoot "tools\release\README_OFFLINE.md"
        } else {
            Join-Path $repoRoot $fileName
        }
        Copy-Item -LiteralPath $source -Destination (Join-Path $packageRoot $fileName)
    }
    Copy-Item -LiteralPath $script:previousFixturePath -Destination $previousPluginPath
    Write-TestManifestAndSums $packageRoot
    Publish-TestZip $packageRoot $zipPath
    Write-ExternalHash $zipPath $sha256Path

    return [pscustomobject]@{
        Root = $fixtureRoot
        PackageRoot = $packageRoot
        ZipPath = $zipPath
        Sha256Path = $sha256Path
        HdtDirectory = $hdtDirectory
        PluginDirectory = $pluginDirectory
        PreviousPluginPath = $previousPluginPath
        AppDataRoot = $appDataRoot
        EvidenceDirectory = $evidenceDirectory
        LogConfigPath = $logConfigPath
        CandidateHash = Get-TestHash (Join-Path $packageRoot "BobCoach.dll")
        PreviousHash = Get-TestHash $previousPluginPath
        LogConfigHash = Get-TestHash $logConfigPath
    }
}

function Invoke-Consumer($Fixture, [switch]$Execute, [string]$AppDataRoot, [string]$EvidenceDirectory, [int]$CommandTimeoutSeconds = 120) {
    if (!(Test-Path -LiteralPath $consumerPath -PathType Leaf)) {
        throw "consumer script missing: $consumerPath"
    }
    if ([string]::IsNullOrWhiteSpace($AppDataRoot)) { $AppDataRoot = $Fixture.AppDataRoot }
    if ([string]::IsNullOrWhiteSpace($EvidenceDirectory)) { $EvidenceDirectory = $Fixture.EvidenceDirectory }
    $arguments = @(
        "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
        "-File", $consumerPath,
        "-ZipPath", $Fixture.ZipPath,
        "-Sha256Path", $Fixture.Sha256Path,
        "-HdtDirectory", $Fixture.HdtDirectory,
        "-PreviousPluginPath", $Fixture.PreviousPluginPath,
        "-AppDataRoot", $AppDataRoot,
        "-EvidenceDirectory", $EvidenceDirectory,
        "-LogConfigPath", $Fixture.LogConfigPath,
        "-CommandTimeoutSeconds", [string]$CommandTimeoutSeconds
    )
    if ($Execute) { $arguments += "-Execute" }

    $stdout = Join-Path $Fixture.Root ("consumer-" + [Guid]::NewGuid().ToString("N") + ".stdout.txt")
    $stderr = Join-Path $Fixture.Root ("consumer-" + [Guid]::NewGuid().ToString("N") + ".stderr.txt")
    $process = Start-Process -FilePath "powershell.exe" -ArgumentList $arguments -Wait -PassThru -NoNewWindow `
        -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    return [pscustomobject]@{
        ExitCode = $process.ExitCode
        Stdout = if (Test-Path -LiteralPath $stdout) { Get-Content -Raw -LiteralPath $stdout } else { "" }
        Stderr = if (Test-Path -LiteralPath $stderr) { Get-Content -Raw -LiteralPath $stderr } else { "" }
    }
}

function Invoke-RefusalScenario([string]$Name, [scriptblock]$Mutate, [string]$ExpectedMessage) {
    $fixture = New-PackageFixture $script:testRoot ("refusal-" + $Name)
    $appData = $fixture.AppDataRoot
    $evidence = $fixture.EvidenceDirectory
    $process = $null
    & $Mutate $fixture ([ref]$appData) ([ref]$evidence) ([ref]$process)
    $before = @(Get-TargetSnapshot $fixture.PluginDirectory)
    try {
        $result = Invoke-Consumer $fixture -Execute -AppDataRoot $appData -EvidenceDirectory $evidence
        Assert-True ($result.ExitCode -ne 0) "$Name must fail"
        Assert-Contains $ExpectedMessage ($result.Stdout + $result.Stderr) "$Name diagnostic"
        if ($Name -eq "ExistingEvidence") {
            Assert-True (Test-Path -LiteralPath $evidence -PathType Container) "$Name preserves existing evidence"
            Assert-Equal 1 @(Get-ChildItem -LiteralPath $evidence -Force).Count "$Name evidence unchanged"
        } else {
            Assert-False (Test-Path -LiteralPath $evidence) "$Name must not create evidence"
        }
        Assert-Snapshot $before $fixture.PluginDirectory "$Name target changed"
    } finally {
        if ($null -ne $process -and !$process.HasExited) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            $process.WaitForExit()
        }
    }
}

function Test-PreflightRefusals {
    Invoke-RefusalScenario "ShaMismatch" {
        param($f, $app, $evidence, $process)
        Write-Utf8NoBom $f.Sha256Path (("0" * 64) + "  " + (Split-Path -Leaf $f.ZipPath) + "`n")
    } "external SHA-256 mismatch"

    Invoke-RefusalScenario "ExtraEntry" {
        param($f, $app, $evidence, $process)
        Publish-TestZip $f.PackageRoot $f.ZipPath -AdditionalEntries @((Split-Path -Leaf $f.PackageRoot) + "/EXTRA.txt")
        Write-ExternalHash $f.ZipPath $f.Sha256Path
    } "ZIP file count"

    Invoke-RefusalScenario "TraversalEntry" {
        param($f, $app, $evidence, $process)
        Publish-TestZip $f.PackageRoot $f.ZipPath -AdditionalEntries @((Split-Path -Leaf $f.PackageRoot) + "/../escape.txt")
        Write-ExternalHash $f.ZipPath $f.Sha256Path
    } "unsafe ZIP entry"

    Invoke-RefusalScenario "DuplicateEntry" {
        param($f, $app, $evidence, $process)
        Publish-TestZip $f.PackageRoot $f.ZipPath -DuplicateReadme
        Write-ExternalHash $f.ZipPath $f.Sha256Path
    } "duplicate ZIP entry"

    Invoke-RefusalScenario "InternalHashMismatch" {
        param($f, $app, $evidence, $process)
        Add-Content -LiteralPath (Join-Path $f.PackageRoot "README_OFFLINE.md") -Value "tampered"
        Publish-TestZip $f.PackageRoot $f.ZipPath
        Write-ExternalHash $f.ZipPath $f.Sha256Path
    } "internal SHA-256 mismatch"

    Invoke-RefusalScenario "ManifestPluginSizeMismatch" {
        param($f, $app, $evidence, $process)
        $manifestPath = Join-Path $f.PackageRoot "manifest.json"
        $manifest = Get-Content -Raw -Encoding UTF8 -LiteralPath $manifestPath | ConvertFrom-Json
        $manifest.pluginSize = [long]$manifest.pluginSize + 1
        Write-Utf8NoBom $manifestPath (($manifest | ConvertTo-Json -Depth 4) + "`n")
        Write-TestInternalSums $f.PackageRoot
        Publish-TestZip $f.PackageRoot $f.ZipPath
        Write-ExternalHash $f.ZipPath $f.Sha256Path
    } "Manifest pluginSize mismatch"

    Invoke-RefusalScenario "UnixSymlinkEntry" {
        param($f, $app, $evidence, $process)
        Set-ZipEntryExternalAttributes $f.ZipPath "README_OFFLINE.md" ([Convert]::ToUInt32("A1FF0000", 16))
        Write-ExternalHash $f.ZipPath $f.Sha256Path
    } "ZIP entry attributes"

    Invoke-RefusalScenario "DosReparseEntry" {
        param($f, $app, $evidence, $process)
        Set-ZipEntryExternalAttributes $f.ZipPath "README_OFFLINE.md" ([uint32]0x00000400)
        Write-ExternalHash $f.ZipPath $f.Sha256Path
    } "ZIP entry attributes"

    Invoke-RefusalScenario "ExistingEvidence" {
        param($f, $app, $evidence, $process)
        New-Item -ItemType Directory -Path $evidence.Value | Out-Null
        Write-Utf8NoBom (Join-Path $evidence.Value "sentinel.txt") "preserve"
    } "EvidenceDirectory already exists"

    Invoke-RefusalScenario "NonemptyTarget" {
        param($f, $app, $evidence, $process)
        New-Item -ItemType Directory -Path $f.PluginDirectory -Force | Out-Null
        Write-Utf8NoBom (Join-Path $f.PluginDirectory "BobCoach.dll") "occupied"
    } "plugin target is not empty"

    Invoke-RefusalScenario "NonemptyTargetDirectory" {
        param($f, $app, $evidence, $process)
        New-Item -ItemType Directory -Path $f.PluginDirectory -Force | Out-Null
        New-Item -ItemType Directory -Path (Join-Path $f.PluginDirectory "BobCoach.dll") | Out-Null
    } "plugin target is not empty"

    Invoke-RefusalScenario "ReparsePath" {
        param($f, $app, $evidence, $process)
        $real = Join-Path $f.Root "real-appdata"
        New-Item -ItemType Directory -Path $real | Out-Null
        New-Item -ItemType Junction -Path $app.Value -Target $real | Out-Null
    } "reparse point"

    Invoke-RefusalScenario "HdtExecutableReparse" {
        param($f, $app, $evidence, $process)
        $executable = Join-Path $f.HdtDirectory "HearthstoneDeckTracker.exe"
        Move-Item -LiteralPath $executable -Destination (Join-Path $f.Root "parked-hdt-executable.bin")
        $target = Join-Path $f.Root "real-hdt-executable-target"
        New-Item -ItemType Directory -Path $target | Out-Null
        New-Item -ItemType Junction -Path $executable -Target $target | Out-Null
    } "reparse point"

    Invoke-RefusalScenario "RealAppData" {
        param($f, $app, $evidence, $process)
        $app.Value = $env:APPDATA
    } "real APPDATA"

    Invoke-RefusalScenario "RealAppDataChild" {
        param($f, $app, $evidence, $process)
        $app.Value = Join-Path $env:APPDATA ("bobcoach-lifecycle-refusal-" + [Guid]::NewGuid().ToString("N"))
    } "real APPDATA"

    Invoke-RefusalScenario "RunningHdt" {
        param($f, $app, $evidence, $process)
        $executable = Join-Path $f.HdtDirectory "HearthstoneDeckTracker.exe"
        Copy-Item -LiteralPath (Get-Command powershell.exe -ErrorAction Stop).Source -Destination $executable -Force
        $process.Value = Start-Process -FilePath $executable -ArgumentList @(
            "-NoProfile", "-Command", "Start-Sleep -Seconds 120"
        ) -PassThru -WindowStyle Hidden
        Start-Sleep -Milliseconds 100
    } "HDT process is running"

    Invoke-RefusalScenario "RunningHdtOutsideTarget" {
        param($f, $app, $evidence, $process)
        $outsideDirectory = Join-Path $f.Root "outside-target"
        New-Item -ItemType Directory -Path $outsideDirectory | Out-Null
        $executable = Join-Path $outsideDirectory "HearthstoneDeckTracker.exe"
        Copy-Item -LiteralPath (Get-Command powershell.exe -ErrorAction Stop).Source -Destination $executable
        $process.Value = Start-Process -FilePath $executable -ArgumentList @(
            "-NoProfile", "-Command", "Start-Sleep -Seconds 120"
        ) -PassThru -WindowStyle Hidden
        Start-Sleep -Milliseconds 100
    } "HDT process is running"

    Write-Host "PASS preflight scenarios"
}

function Test-DryRun {
    $fixture = New-PackageFixture $script:testRoot "dry-run"
    $before = @(Get-TargetSnapshot $fixture.PluginDirectory)
    Assert-False (Test-Path -LiteralPath $fixture.AppDataRoot) "dry-run APPDATA starts absent"
    Assert-False (Test-Path -LiteralPath $fixture.EvidenceDirectory) "dry-run evidence starts absent"
    $result = Invoke-Consumer $fixture
    Assert-Equal 0 $result.ExitCode "dry-run exit"
    Assert-Contains "PASS read-only preflight" ($result.Stdout + $result.Stderr) "dry-run output"
    Assert-False (Test-Path -LiteralPath $fixture.AppDataRoot) "dry-run APPDATA remains absent"
    Assert-False (Test-Path -LiteralPath $fixture.EvidenceDirectory) "dry-run evidence remains absent"
    Assert-Snapshot $before $fixture.PluginDirectory "dry-run target changed"
    Write-Host "PASS dry-run zero-write"
}

function Test-LifecycleSuccess {
    $fixture = New-PackageFixture $script:testRoot "success"
    $result = Invoke-Consumer $fixture -Execute
    Assert-Equal 0 $result.ExitCode "lifecycle exit: $($result.Stderr)"
    Assert-Contains "PASS offline package lifecycle" ($result.Stdout + $result.Stderr) "lifecycle output"

    $resultPath = Join-Path $fixture.EvidenceDirectory "lifecycle-result.json"
    Assert-True (Test-Path -LiteralPath $resultPath -PathType Leaf) "lifecycle result exists"
    $lifecycle = Get-Content -Raw -Encoding UTF8 -LiteralPath $resultPath | ConvertFrom-Json
    Assert-Equal "Passed" $lifecycle.status "lifecycle status"
    $expectedSteps = @(
        "fresh-install",
        "default-uninstall",
        "seed-previous",
        "upgrade",
        "latest-rollback",
        "specified-rollback",
        "default-uninstall-after-rollbacks",
        "fresh-install-again",
        "remove-user-data"
    )
    Assert-ExactNames $expectedSteps @($lifecycle.steps | ForEach-Object { $_.name }) "lifecycle step names"
    Assert-Equal 9 @($lifecycle.steps).Count "lifecycle step count"
    Assert-True (@($lifecycle.steps | Where-Object { $_.status -ne "Passed" }).Count -eq 0) "all steps passed"

    $stateFiles = @(Get-ChildItem -LiteralPath (Join-Path $fixture.EvidenceDirectory "states") -Filter "*.json" -File | Sort-Object Name)
    Assert-Equal 9 $stateFiles.Count "state evidence count"
    for ($index = 0; $index -lt $expectedSteps.Count; $index++) {
        Assert-Equal ("{0:D2}-{1}.json" -f ($index + 1), $expectedSteps[$index]) $stateFiles[$index].Name "state filename order $index"
    }
    $commandStdout = @(Get-ChildItem -LiteralPath (Join-Path $fixture.EvidenceDirectory "commands") -Filter "*.stdout.txt" -File)
    $commandStderr = @(Get-ChildItem -LiteralPath (Join-Path $fixture.EvidenceDirectory "commands") -Filter "*.stderr.txt" -File)
    Assert-Equal 9 $commandStdout.Count "command stdout count"
    Assert-Equal 9 $commandStderr.Count "command stderr count"

    $states = for ($index = 0; $index -lt $expectedSteps.Count; $index++) {
        Get-StateEvidence $fixture.EvidenceDirectory ($index + 1) $expectedSteps[$index]
    }
    $sentinelHash = $states[0].before.UserData.Files[0].Sha256
    Assert-True (![string]::IsNullOrWhiteSpace($sentinelHash)) "initial user-data sentinel hash"
    $expectedTargetHashes = @(
        $fixture.CandidateHash,
        $null,
        $fixture.PreviousHash,
        $fixture.CandidateHash,
        $fixture.PreviousHash,
        $fixture.CandidateHash,
        $null,
        $fixture.CandidateHash,
        $null
    )
    $expectedBackupHashes = @(
        @(),
        @(),
        @(),
        @($fixture.PreviousHash),
        @($fixture.PreviousHash, $fixture.CandidateHash),
        @($fixture.PreviousHash, $fixture.PreviousHash, $fixture.CandidateHash),
        @($fixture.PreviousHash, $fixture.PreviousHash, $fixture.CandidateHash),
        @($fixture.PreviousHash, $fixture.PreviousHash, $fixture.CandidateHash),
        @($fixture.PreviousHash, $fixture.PreviousHash, $fixture.CandidateHash)
    )
    $initialLogConfig = $states[0].before.LogConfig
    Assert-True $initialLogConfig.Exists "initial log.config exists"
    Assert-Equal $fixture.LogConfigHash $initialLogConfig.Sha256 "initial log.config hash"
    for ($index = 0; $index -lt $states.Count; $index++) {
        $state = $states[$index]
        Assert-Equal ($index + 1) $state.stepNumber "state step number $index"
        Assert-Equal $expectedSteps[$index] $state.stepName "state step name $index"
        Assert-TargetState $state.after.Target $expectedTargetHashes[$index] "step $($index + 1)"
        Assert-BackupHashes $state.after.Backups $expectedBackupHashes[$index] "step $($index + 1)"
        Assert-Equal 0 @($state.before.TemporaryDlls).Count "step $($index + 1) temporary before"
        Assert-Equal 0 @($state.after.TemporaryDlls).Count "step $($index + 1) temporary after"
        Assert-UserDataState $state.before.UserData $true $sentinelHash "step $($index + 1) user data before"
        Assert-UserDataState $state.after.UserData ($index -lt 8) $sentinelHash "step $($index + 1) user data after"
        Assert-FileStateEqual $initialLogConfig $state.before.LogConfig "step $($index + 1) log.config before"
        Assert-FileStateEqual $initialLogConfig $state.after.LogConfig "step $($index + 1) log.config after"
    }

    $plugin = Join-Path $fixture.PluginDirectory "BobCoach.dll"
    Assert-False (Test-Path -LiteralPath $plugin) "final plugin removed"
    Assert-False (Test-Path -LiteralPath (Join-Path $fixture.AppDataRoot "bob-coach")) "final user data removed"
    Assert-Equal 0 @(Get-ChildItem -LiteralPath $fixture.PluginDirectory -Filter "BobCoach.dll.installing-*" -File).Count "temporary DLL count"
    $backups = @(Get-ChildItem -LiteralPath $fixture.PluginDirectory -Filter "BobCoach.dll.backup-*" -File)
    Assert-Equal 3 $backups.Count "final backup count"
    Assert-Equal 2 @($backups | Where-Object { (Get-TestHash $_.FullName) -eq $fixture.PreviousHash }).Count "previous backup count"
    Assert-Equal 1 @($backups | Where-Object { (Get-TestHash $_.FullName) -eq $fixture.CandidateHash }).Count "candidate backup count"

    $sumPath = Join-Path $fixture.EvidenceDirectory "EVIDENCE_SHA256SUMS.txt"
    Assert-True (Test-Path -LiteralPath $sumPath -PathType Leaf) "evidence checksum exists"
    $sumLines = @(Get-Content -LiteralPath $sumPath -Encoding UTF8 | Where-Object { $_ -ne "" })
    Assert-True ($sumLines.Count -gt 10) "evidence checksum coverage"
    $sumPaths = @()
    foreach ($line in $sumLines) {
        if ($line -notmatch '^([A-Fa-f0-9]{64})  (.+)$') { throw "Invalid evidence checksum line: $line" }
        $sumPaths += $Matches[2]
        $path = Join-Path $fixture.EvidenceDirectory $Matches[2]
        Assert-Equal $Matches[1].ToUpperInvariant() (Get-TestHash $path) "evidence hash $($Matches[2])"
    }
    [string[]]$ordinalPaths = @($sumPaths)
    [Array]::Sort($ordinalPaths, [StringComparer]::Ordinal)
    for ($index = 0; $index -lt $ordinalPaths.Count; $index++) {
        Assert-Equal $ordinalPaths[$index] $sumPaths[$index] "evidence checksum ordinal order $index"
    }
    Write-Host "PASS lifecycle success"
}

function Test-CanonicalPath {
    $tokens = $null
    $errors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile($consumerPath, [ref]$tokens, [ref]$errors)
    Assert-Equal 0 @($errors).Count "consumer parse errors"
    $functionAst = @($ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq "Resolve-FullPath" }, $true))
    Assert-Equal 1 $functionAst.Count "Resolve-FullPath definition count"
    . ([ScriptBlock]::Create($functionAst[0].Extent.Text))
    if (Test-Path -LiteralPath "C:\PROGRA~1" -PathType Container) {
        $expected = (Get-Item -LiteralPath "C:\Program Files" -Force).FullName.TrimEnd('\')
        Assert-Equal $expected (Resolve-FullPath "C:\PROGRA~1" "short path") "8.3 path canonicalization"
        Assert-Equal (Join-Path $expected "BobCoach\new") (Resolve-FullPath "C:\PROGRA~1\BobCoach\new" "short child") "8.3 missing child canonicalization"
    }
    Write-Host "PASS canonical path contract"
}

function Test-OutputFlood {
    $fixture = New-PackageFixture $script:testRoot "output-flood"
    $installerPath = Join-Path $fixture.PackageRoot "INSTALL.ps1"
    $installerBody = Get-Content -Raw -Encoding UTF8 -LiteralPath $installerPath
    $flood = "[Console]::Out.Write(('O' * 262144)); [Console]::Error.Write(('E' * 262144))`r`n"
    $marker = '$ErrorActionPreference = "Stop"'
    Assert-True ($installerBody.Contains($marker)) "output flood injection marker"
    Write-Utf8Bom $installerPath ($installerBody.Replace($marker, ($flood + $marker)))
    Write-TestManifestAndSums $fixture.PackageRoot
    Publish-TestZip $fixture.PackageRoot $fixture.ZipPath
    Write-ExternalHash $fixture.ZipPath $fixture.Sha256Path
    $result = Invoke-Consumer $fixture -Execute
    Assert-Equal 0 $result.ExitCode "output flood lifecycle exit: $($result.Stderr)"
    Write-Host "PASS child output flood"
}

function Test-TerminalEvidenceFailure {
    $fixture = New-PackageFixture $script:testRoot "terminal-evidence-failure"
    $uninstallerPath = Join-Path $fixture.PackageRoot "UNINSTALL.ps1"
    $uninstallerBody = Get-Content -Raw -Encoding UTF8 -LiteralPath $uninstallerPath
    $injection = @'
if ($RemoveUserData) {
    $evidenceRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    New-Item -ItemType Directory -Path (Join-Path $evidenceRoot "EVIDENCE_SHA256SUMS.txt") | Out-Null
}
'@
    Write-Utf8NoBom $uninstallerPath ($uninstallerBody + "`r`n" + $injection)
    Write-TestManifestAndSums $fixture.PackageRoot
    Publish-TestZip $fixture.PackageRoot $fixture.ZipPath
    Write-ExternalHash $fixture.ZipPath $fixture.Sha256Path
    $result = Invoke-Consumer $fixture -Execute
    Assert-True ($result.ExitCode -ne 0) "terminal evidence failure must fail"
    $resultPath = Join-Path $fixture.EvidenceDirectory "lifecycle-result.json"
    Assert-True (Test-Path -LiteralPath $resultPath -PathType Leaf) "terminal evidence failure lifecycle result exists"
    $lifecycle = Get-Content -Raw -Encoding UTF8 -LiteralPath $resultPath | ConvertFrom-Json
    Assert-Equal "Failed" $lifecycle.status "terminal evidence failure status"
    Write-Host "PASS terminal evidence failure semantics"
}

function Test-ResultPathTakeover {
    $fixture = New-PackageFixture $script:testRoot "result-path-takeover"
    $installerPath = Join-Path $fixture.PackageRoot "INSTALL.ps1"
    $installerBody = Get-Content -Raw -Encoding UTF8 -LiteralPath $installerPath
    $injection = @'
$evidenceRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$resultPath = Join-Path $evidenceRoot "lifecycle-result.json"
$renamedPath = Join-Path $evidenceRoot "lifecycle-result.renamed.json"
$replacementPath = Join-Path $evidenceRoot "lifecycle-result.replacement.json"
$backupPath = Join-Path $evidenceRoot "lifecycle-result.backup.json"
$unexpectedSuccess = @()
try { [IO.File]::Delete($resultPath); $unexpectedSuccess += "delete" } catch { }
try { [IO.File]::Move($resultPath, $renamedPath); $unexpectedSuccess += "move" } catch { }
try {
    [IO.File]::WriteAllText($replacementPath, '{"status":"Passed"}')
    [IO.File]::Replace($replacementPath, $resultPath, $backupPath, $true)
    $unexpectedSuccess += "replace"
} catch { }
if ($unexpectedSuccess.Count -ne 0) {
    throw "Result path takeover operation unexpectedly succeeded: $($unexpectedSuccess -join ',')"
}
[IO.File]::WriteAllText(
    $resultPath,
    '{"status":"Passed"}',
    (New-Object Text.UTF8Encoding($false))
)
'@
    Write-Utf8Bom $installerPath ($installerBody + "`r`n" + $injection)
    Write-TestManifestAndSums $fixture.PackageRoot
    Publish-TestZip $fixture.PackageRoot $fixture.ZipPath
    Write-ExternalHash $fixture.ZipPath $fixture.Sha256Path

    $result = Invoke-Consumer $fixture -Execute
    Assert-True ($result.ExitCode -ne 0) "result path takeover must fail"
    $resultPath = Join-Path $fixture.EvidenceDirectory "lifecycle-result.json"
    Assert-True (Test-Path -LiteralPath $resultPath -PathType Leaf) "result path takeover lifecycle result exists"
    $lifecycle = Get-Content -Raw -Encoding UTF8 -LiteralPath $resultPath | ConvertFrom-Json
    Assert-Equal "Failed" $lifecycle.status "result path takeover status"
    Assert-Equal "fresh-install" $lifecycle.failedStep "result path takeover step"
    Assert-Contains "Child PowerShell failed for fresh-install" $lifecycle.errorMessage "result path takeover root error"
    Write-Host "PASS result path takeover protection"
}

function Test-EnvironmentIsolation {
    $fixture = New-PackageFixture $script:testRoot "environment-isolation"
    $installerPath = Join-Path $fixture.PackageRoot "INSTALL.ps1"
    $installerBody = Get-Content -Raw -Encoding UTF8 -LiteralPath $installerPath
    $injection = @'
if ($env:BOBCOACH_TEST_SECRET -eq "must-not-leak") {
    throw "Parent environment leaked into child"
}
$isolatedRoot = [IO.Path]::GetFullPath($env:APPDATA).TrimEnd('\')
foreach ($name in @("LOCALAPPDATA", "USERPROFILE", "TEMP", "TMP")) {
    $value = [Environment]::GetEnvironmentVariable($name)
    if ([string]::IsNullOrWhiteSpace($value)) { throw "Isolated environment variable missing: $name" }
    $full = [IO.Path]::GetFullPath($value).TrimEnd('\')
    if (!$full.StartsWith($isolatedRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Environment variable is outside isolated APPDATA: $name=$full"
    }
}
'@
    Write-Utf8Bom $installerPath ($installerBody + "`r`n" + $injection)
    Write-TestManifestAndSums $fixture.PackageRoot
    Publish-TestZip $fixture.PackageRoot $fixture.ZipPath
    Write-ExternalHash $fixture.ZipPath $fixture.Sha256Path

    $previousSecret = [Environment]::GetEnvironmentVariable("BOBCOACH_TEST_SECRET", "Process")
    try {
        [Environment]::SetEnvironmentVariable("BOBCOACH_TEST_SECRET", "must-not-leak", "Process")
        $result = Invoke-Consumer $fixture -Execute
    } finally {
        [Environment]::SetEnvironmentVariable("BOBCOACH_TEST_SECRET", $previousSecret, "Process")
    }
    Assert-Equal 0 $result.ExitCode "environment isolation lifecycle exit: $($result.Stderr)"
    Write-Host "PASS child environment isolation"
}

function Test-NestedReparse {
    $fixture = New-PackageFixture $script:testRoot "nested-reparse"
    $installerPath = Join-Path $fixture.PackageRoot "INSTALL.ps1"
    $installerBody = Get-Content -Raw -Encoding UTF8 -LiteralPath $installerPath
    $injection = @'
$userData = Join-Path $env:APPDATA "bob-coach"
$target = Join-Path $env:APPDATA "junction-target"
$link = Join-Path $userData "nested-junction"
New-Item -ItemType Directory -Path $target -Force | Out-Null
if (!(Test-Path -LiteralPath $link)) {
    New-Item -ItemType Junction -Path $link -Target $target | Out-Null
}
'@
    Write-Utf8Bom $installerPath ($installerBody + "`r`n" + $injection)
    Write-TestManifestAndSums $fixture.PackageRoot
    Publish-TestZip $fixture.PackageRoot $fixture.ZipPath
    Write-ExternalHash $fixture.ZipPath $fixture.Sha256Path

    $result = Invoke-Consumer $fixture -Execute
    Assert-True ($result.ExitCode -ne 0) "nested reparse must fail"
    $resultPath = Join-Path $fixture.EvidenceDirectory "lifecycle-result.json"
    Assert-True (Test-Path -LiteralPath $resultPath -PathType Leaf) "nested reparse lifecycle result exists"
    $lifecycle = Get-Content -Raw -Encoding UTF8 -LiteralPath $resultPath | ConvertFrom-Json
    Assert-Equal "Failed" $lifecycle.status "nested reparse status"
    Assert-Equal "fresh-install" $lifecycle.failedStep "nested reparse step"
    Assert-Contains "reparse point" $lifecycle.errorMessage "nested reparse root error"
    Write-Host "PASS nested reparse refusal"
}

function Test-FinalStateCaptureFailure {
    $fixture = New-PackageFixture $script:testRoot "final-state-capture-failure"
    $canonicalPluginDirectory = Join-Path (Get-Item -LiteralPath $fixture.Root -Force).FullName "IsolatedAppData\HearthstoneDeckTracker\Plugins"
    $uninstallerPath = Join-Path $fixture.PackageRoot "UNINSTALL.ps1"
    $uninstallerBody = Get-Content -Raw -Encoding UTF8 -LiteralPath $uninstallerPath
    $injection = @'
$parkedPluginDirectory = $PluginDirectory + ".parked"
Move-Item -LiteralPath $PluginDirectory -Destination $parkedPluginDirectory
throw "synthetic lifecycle root failure before final state capture"
'@
    Write-Utf8NoBom $uninstallerPath ($uninstallerBody + "`r`n" + $injection)
    Write-TestManifestAndSums $fixture.PackageRoot
    Publish-TestZip $fixture.PackageRoot $fixture.ZipPath
    Write-ExternalHash $fixture.ZipPath $fixture.Sha256Path

    $result = Invoke-Consumer $fixture -Execute
    Assert-True ($result.ExitCode -ne 0) "final state capture failure must fail"
    $resultPath = Join-Path $fixture.EvidenceDirectory "lifecycle-result.json"
    Assert-True (Test-Path -LiteralPath $resultPath -PathType Leaf) "final state capture failure lifecycle result exists"
    $lifecycle = Get-Content -Raw -Encoding UTF8 -LiteralPath $resultPath | ConvertFrom-Json
    Assert-Equal "Failed" $lifecycle.status "final state capture failure status"
    Assert-Equal "default-uninstall" $lifecycle.failedStep "final state capture preserves lifecycle step"
    Assert-Contains "Child PowerShell failed for default-uninstall" $lifecycle.errorMessage "final state capture preserves lifecycle root error"
    Assert-True ($null -ne $lifecycle.finalStateEvidenceFailure) "final state capture records evidence failure"
    Assert-Equal "System.Management.Automation.ItemNotFoundException" $lifecycle.finalStateEvidenceFailure.errorType "final state capture diagnostic type"
    Assert-Contains $canonicalPluginDirectory $lifecycle.finalStateEvidenceFailure.errorMessage "final state capture diagnostic path"
    Assert-True ($null -eq $lifecycle.finalState) "final state capture leaves unavailable state null"
    Write-Host "PASS final state capture failure preservation"
}

function Test-StepEvidenceFailureAttribution {
    $fixture = New-PackageFixture $script:testRoot "step-evidence-failure"
    $installerPath = Join-Path $fixture.PackageRoot "INSTALL.ps1"
    $installerBody = Get-Content -Raw -Encoding UTF8 -LiteralPath $installerPath
    $injection = @'
$evidenceRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
New-Item -ItemType Directory -Path (Join-Path $evidenceRoot "states\01-fresh-install.json") | Out-Null
'@
    Write-Utf8Bom $installerPath ($installerBody + "`r`n" + $injection)
    Write-TestManifestAndSums $fixture.PackageRoot
    Publish-TestZip $fixture.PackageRoot $fixture.ZipPath
    Write-ExternalHash $fixture.ZipPath $fixture.Sha256Path

    $result = Invoke-Consumer $fixture -Execute
    Assert-True ($result.ExitCode -ne 0) "step evidence failure must fail"
    $resultPath = Join-Path $fixture.EvidenceDirectory "lifecycle-result.json"
    Assert-True (Test-Path -LiteralPath $resultPath -PathType Leaf) "step evidence failure lifecycle result exists"
    $lifecycle = Get-Content -Raw -Encoding UTF8 -LiteralPath $resultPath | ConvertFrom-Json
    Assert-Equal "Failed" $lifecycle.status "step evidence failure status"
    Assert-Equal "fresh-install" $lifecycle.failedStep "step evidence failure step"
    Assert-Equal 1 @($lifecycle.steps).Count "step evidence failure step count"
    Assert-Equal "fresh-install" $lifecycle.steps[0].name "step evidence failure recorded name"
    Assert-Equal "Failed" $lifecycle.steps[0].status "step evidence failure recorded status"
    Assert-Contains "Evidence file already exists" $lifecycle.errorMessage "step evidence failure root error"
    Write-Host "PASS step evidence failure attribution"
}

function Test-LifecycleAndEvidenceDoubleFailure {
    $fixture = New-PackageFixture $script:testRoot "double-failure"
    $uninstallerPath = Join-Path $fixture.PackageRoot "UNINSTALL.ps1"
    $uninstallerBody = Get-Content -Raw -Encoding UTF8 -LiteralPath $uninstallerPath
    $injection = @'
$evidenceRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
New-Item -ItemType Directory -Path (Join-Path $evidenceRoot "EVIDENCE_SHA256SUMS.txt") | Out-Null
throw "synthetic lifecycle root failure"
'@
    Write-Utf8NoBom $uninstallerPath ($uninstallerBody + "`r`n" + $injection)
    Write-TestManifestAndSums $fixture.PackageRoot
    Publish-TestZip $fixture.PackageRoot $fixture.ZipPath
    Write-ExternalHash $fixture.ZipPath $fixture.Sha256Path

    $result = Invoke-Consumer $fixture -Execute
    Assert-True ($result.ExitCode -ne 0) "double failure must fail"
    $resultPath = Join-Path $fixture.EvidenceDirectory "lifecycle-result.json"
    Assert-True (Test-Path -LiteralPath $resultPath -PathType Leaf) "double failure lifecycle result exists"
    $lifecycle = Get-Content -Raw -Encoding UTF8 -LiteralPath $resultPath | ConvertFrom-Json
    Assert-Equal "Failed" $lifecycle.status "double failure status"
    Assert-Equal "default-uninstall" $lifecycle.failedStep "double failure lifecycle step"
    Assert-Contains "Child PowerShell failed for default-uninstall" $lifecycle.errorMessage "double failure preserves lifecycle root error"
    Assert-True ($null -ne $lifecycle.evidenceFinalizationFailure) "double failure records evidence finalization failure"
    Assert-Contains "Evidence checksum file already exists" $lifecycle.evidenceFinalizationFailure.errorMessage "double failure finalization diagnostic"
    Write-Host "PASS lifecycle and evidence double failure"
}

function Test-ChildFailurePreservation {
    $fixture = New-PackageFixture $script:testRoot "child-failure"
    $uninstallPath = Join-Path $fixture.PackageRoot "UNINSTALL.ps1"
    $uninstallBody = Get-Content -Raw -Encoding UTF8 -LiteralPath $uninstallPath
    Set-Content -LiteralPath $uninstallPath -Encoding UTF8 -Value ("throw 'synthetic child failure'`r`n" + $uninstallBody)
    Write-TestManifestAndSums $fixture.PackageRoot
    Publish-TestZip $fixture.PackageRoot $fixture.ZipPath
    Write-ExternalHash $fixture.ZipPath $fixture.Sha256Path
    $result = Invoke-Consumer $fixture -Execute
    Assert-True ($result.ExitCode -ne 0) "child failure must fail"
    Assert-True (Test-Path -LiteralPath $fixture.EvidenceDirectory -PathType Container) "child failure preserves evidence"
    $lifecycle = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $fixture.EvidenceDirectory "lifecycle-result.json") | ConvertFrom-Json
    Assert-Equal "Failed" $lifecycle.status "child failure status"
    Assert-Equal "default-uninstall" $lifecycle.failedStep "child failure step"
    Assert-Equal 2 @($lifecycle.steps).Count "child failure stops later steps"
    Assert-True (Test-Path -LiteralPath (Join-Path $fixture.PluginDirectory "BobCoach.dll")) "child failure leaves installed candidate"
    Write-Host "PASS child failure preservation"
}

$script:testRoot = Join-Path $env:TEMP ("bobcoach-lifecycle-consumer-test-" + [Guid]::NewGuid().ToString("N"))
Assert-SafeTestRoot $script:testRoot
New-Item -ItemType Directory -Path $script:testRoot | Out-Null
$script:previousFixturePath = Join-Path $script:testRoot "shared-previous\BobCoach.dll"
New-Item -ItemType Directory -Path (Split-Path -Parent $script:previousFixturePath) | Out-Null
New-TestManagedBobCoach -Path $script:previousFixturePath -Version "0.1.0.0" | Out-Null

$selected = @($Scenario)
if ($selected -contains "All") { $selected = @("Preflight", "DryRun", "Success", "ChildFailure", "CanonicalPath", "OutputFlood", "TerminalEvidenceFailure", "ResultPathTakeover", "EnvironmentIsolation", "NestedReparse", "FinalStateCaptureFailure", "StepEvidenceFailure", "DoubleFailure") }
foreach ($name in $selected) {
    switch ($name) {
        "Preflight" { Test-PreflightRefusals }
        "DryRun" { Test-DryRun }
        "Success" { Test-LifecycleSuccess }
        "ChildFailure" { Test-ChildFailurePreservation }
        "CanonicalPath" { Test-CanonicalPath }
        "OutputFlood" { Test-OutputFlood }
        "TerminalEvidenceFailure" { Test-TerminalEvidenceFailure }
        "ResultPathTakeover" { Test-ResultPathTakeover }
        "EnvironmentIsolation" { Test-EnvironmentIsolation }
        "NestedReparse" { Test-NestedReparse }
        "FinalStateCaptureFailure" { Test-FinalStateCaptureFailure }
        "StepEvidenceFailure" { Test-StepEvidenceFailureAttribution }
        "DoubleFailure" { Test-LifecycleAndEvidenceDoubleFailure }
    }
}

Write-Host "PASS offline lifecycle consumer contract root=$script:testRoot"
