[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$HdtDirectory,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$OutputDirectory,

    [switch]$Force
)

$ErrorActionPreference = "Stop"
$scriptDir = $PSScriptRoot
$repoRoot = Split-Path -Parent (Split-Path -Parent $scriptDir)
$projectPath = Join-Path $repoRoot "src\BobCoach\BobCoach.csproj"
$identityPath = Join-Path $repoRoot "release_identity.json"
$targetPack = "C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2\RedistList\FrameworkList.xml"
$intermediateRoot = Join-Path $env:TEMP ("bobcoach-release-intermediate-" + [Guid]::NewGuid().ToString("N"))

function Get-ManagedPeInfo([string]$Path) {
    $assemblyName = [System.Reflection.AssemblyName]::GetAssemblyName($Path)
    $assembly = [System.Reflection.Assembly]::ReflectionOnlyLoadFrom($Path)
    $peKind = [System.Reflection.PortableExecutableKinds]::ILOnly
    $machine = [System.Reflection.ImageFileMachine]::I386
    $assembly.ManifestModule.GetPEKind([ref]$peKind, [ref]$machine)
    return [pscustomobject]@{
        AssemblyName = $assemblyName
        Assembly = $assembly
        PEKind = $peKind
        Machine = $machine
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

if (!(Test-Path -LiteralPath $identityPath -PathType Leaf)) {
    throw "Missing release identity: $identityPath"
}
if (!(Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "Missing tracked project: $projectPath"
}
if (!(Test-Path -LiteralPath $targetPack -PathType Leaf)) {
    throw "Missing .NET Framework 4.7.2 developer pack: $targetPack"
}

$identity = Get-Content -Raw -Encoding UTF8 -LiteralPath $identityPath | ConvertFrom-Json
$requiredIdentityFields = @("packageVersion", "assemblyVersion", "targetFramework", "runtimeIdentifier", "hdtBaselineVersion")
foreach ($field in $requiredIdentityFields) {
    if ([string]::IsNullOrWhiteSpace([string]$identity.$field)) {
        throw "Missing release identity field: $field"
    }
}
if ([string]$identity.packageVersion -notmatch '^\d+\.\d+\.\d+$') {
    throw "Invalid packageVersion: $($identity.packageVersion)"
}
if ([string]$identity.assemblyVersion -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    throw "Invalid assemblyVersion: $($identity.assemblyVersion)"
}
if ($identity.targetFramework -ne "net472") { throw "Unsupported targetFramework: $($identity.targetFramework)" }
if ($identity.runtimeIdentifier -ne "win-x64") { throw "Unsupported runtimeIdentifier: $($identity.runtimeIdentifier)" }

$hdtFullPath = [System.IO.Path]::GetFullPath($HdtDirectory).TrimEnd('\')
if (!(Test-Path -LiteralPath $hdtFullPath -PathType Container)) {
    throw "HDT directory not found: $hdtFullPath"
}
$requiredHdtFiles = @("HearthstoneDeckTracker.exe", "HearthDb.dll", "Newtonsoft.Json.dll")
foreach ($fileName in $requiredHdtFiles) {
    $requiredPath = Join-Path $hdtFullPath $fileName
    if (!(Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Missing HDT build reference: $requiredPath"
    }
}

$hdtExecutable = Join-Path $hdtFullPath "HearthstoneDeckTracker.exe"
$hdtInfo = Get-ManagedPeInfo $hdtExecutable
if ($hdtInfo.AssemblyName.Version.ToString() -ne [string]$identity.hdtBaselineVersion) {
    throw "HDT baseline mismatch: expected $($identity.hdtBaselineVersion), got $($hdtInfo.AssemblyName.Version)"
}
if ($hdtInfo.Machine -ne [System.Reflection.ImageFileMachine]::AMD64 -or
    ($hdtInfo.PEKind -band [System.Reflection.PortableExecutableKinds]::PE32Plus) -eq 0) {
    throw "HDT architecture mismatch: machine=$($hdtInfo.Machine) peKind=$($hdtInfo.PEKind)"
}

$outputFullPath = [System.IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\')
if (!(Test-Path -LiteralPath $outputFullPath)) {
    New-Item -ItemType Directory -Path $outputFullPath -Force | Out-Null
}
$outputDll = Join-Path $outputFullPath "BobCoach.dll"
$logPath = Join-Path $outputFullPath "build_log.txt"
foreach ($outputFile in @($outputDll, $logPath)) {
    if (Test-Path -LiteralPath $outputFile) {
        if (!$Force) { throw "Output already exists; pass -Force to replace: $outputFile" }
        Remove-Item -LiteralPath $outputFile -Force
    }
}

function Write-BuildLog([string]$Message) {
    $Message | Out-File -LiteralPath $logPath -Append -Encoding UTF8
}

"=== BobCoach release build $($identity.packageVersion) $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') ===" |
    Out-File -LiteralPath $logPath -Encoding UTF8
Write-BuildLog "Project: $projectPath"
Write-BuildLog "HDT: $hdtFullPath"
Write-BuildLog "Output: $outputFullPath"
Write-BuildLog "Contract: assembly=$($identity.assemblyVersion) framework=$($identity.targetFramework) rid=$($identity.runtimeIdentifier)"

$msbuildCandidates = New-Object System.Collections.Generic.List[object]
if (![string]::IsNullOrWhiteSpace($env:BOBCOACH_MSBUILD)) {
    $configuredMsBuild = [IO.Path]::GetFullPath($env:BOBCOACH_MSBUILD)
    if (!(Test-Path -LiteralPath $configuredMsBuild -PathType Leaf)) {
        throw "BOBCOACH_MSBUILD does not point to a file: $configuredMsBuild"
    }
    $msbuildCandidates.Add([pscustomobject]@{ Exe = $configuredMsBuild; Prefix = @(); Label = "BOBCOACH_MSBUILD" })
}
$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path -LiteralPath $vswhere -PathType Leaf) {
    $vsMsBuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" |
        Select-Object -First 1
    if (![string]::IsNullOrWhiteSpace($vsMsBuild) -and (Test-Path -LiteralPath $vsMsBuild -PathType Leaf)) {
        $msbuildCandidates.Add([pscustomobject]@{ Exe = $vsMsBuild; Prefix = @(); Label = "Visual Studio MSBuild" })
    }
}
$pathMsBuild = Get-Command "MSBuild.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -ne $pathMsBuild -and @($msbuildCandidates | Where-Object { $_.Exe -eq $pathMsBuild.Source }).Count -eq 0) {
    $msbuildCandidates.Add([pscustomobject]@{ Exe = $pathMsBuild.Source; Prefix = @(); Label = "PATH MSBuild" })
}
$frameworkMsBuild = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe"
if (Test-Path -LiteralPath $frameworkMsBuild) {
    $msbuildCandidates.Add([pscustomobject]@{ Exe = $frameworkMsBuild; Prefix = @(); Label = ".NET Framework64 MSBuild" })
}
$dotnet = "C:\Program Files\dotnet\dotnet.exe"
if (Test-Path -LiteralPath $dotnet) {
    $msbuildCandidates.Add([pscustomobject]@{ Exe = $dotnet; Prefix = @("msbuild"); Label = "dotnet msbuild" })
}
if ($msbuildCandidates.Count -eq 0) { throw "No supported MSBuild found" }

New-Item -ItemType Directory -Path $intermediateRoot -Force | Out-Null
$intermediateOutput = Join-Path $intermediateRoot "obj"
$outputProperty = $outputFullPath + [System.IO.Path]::DirectorySeparatorChar
$intermediateProperty = $intermediateOutput + [System.IO.Path]::DirectorySeparatorChar
$candidate = $msbuildCandidates[0]
$arguments = @($candidate.Prefix) + @(
    $projectPath,
    "/nologo",
    "/v:minimal",
    "/t:Rebuild",
    "/p:Configuration=Release",
    "/p:Platform=x64",
    "/p:HdtDirectory=$hdtFullPath",
    "/p:OutputPath=$outputProperty",
    "/p:BaseIntermediateOutputPath=$intermediateProperty",
    "/p:IntermediateOutputPath=$intermediateProperty"
)

try {
    Write-BuildLog "MSBuild: $($candidate.Label)"
    $buildOutput = & $candidate.Exe @arguments 2>&1
    $buildExitCode = $LASTEXITCODE
    $buildOutput | Out-File -LiteralPath $logPath -Append -Encoding UTF8
    Write-BuildLog "MSBuild exit: $buildExitCode"
    if ($buildExitCode -ne 0) { throw "MSBuild failed with exit code $buildExitCode" }

    $logText = [System.IO.File]::ReadAllText($logPath, [System.Text.Encoding]::UTF8)
    if ($logText -match 'MSB3270|processor architecture.*does not match') {
        throw "Architecture mismatch found in build log"
    }
    if (!(Test-Path -LiteralPath $outputDll -PathType Leaf)) { throw "Build output missing: $outputDll" }

    $outputInfo = Get-ManagedPeInfo $outputDll
    if ($outputInfo.AssemblyName.Version.ToString() -ne [string]$identity.assemblyVersion) {
        throw "Assembly version mismatch: expected $($identity.assemblyVersion), got $($outputInfo.AssemblyName.Version)"
    }
    if ($outputInfo.Machine -ne [System.Reflection.ImageFileMachine]::AMD64 -or
        ($outputInfo.PEKind -band [System.Reflection.PortableExecutableKinds]::PE32Plus) -eq 0 -or
        ($outputInfo.PEKind -band [System.Reflection.PortableExecutableKinds]::ILOnly) -eq 0) {
        throw "Output architecture mismatch: machine=$($outputInfo.Machine) peKind=$($outputInfo.PEKind)"
    }

    $fileVersion = Get-AssemblyAttributeValue $outputInfo.Assembly "System.Reflection.AssemblyFileVersionAttribute"
    $informationalVersion = Get-AssemblyAttributeValue $outputInfo.Assembly "System.Reflection.AssemblyInformationalVersionAttribute"
    $targetFramework = Get-AssemblyAttributeValue $outputInfo.Assembly "System.Runtime.Versioning.TargetFrameworkAttribute"
    if ($fileVersion -ne [string]$identity.assemblyVersion) {
        throw "File version mismatch: expected $($identity.assemblyVersion), got $fileVersion"
    }
    if ($informationalVersion -ne [string]$identity.packageVersion) {
        throw "Informational version mismatch: expected $($identity.packageVersion), got $informationalVersion"
    }
    if ($targetFramework -ne ".NETFramework,Version=v4.7.2") {
        throw "Target framework mismatch: $targetFramework"
    }

    $unexpectedFiles = @(Get-ChildItem -LiteralPath $outputFullPath -File | Where-Object {
        $_.Name -notin @("BobCoach.dll", "build_log.txt")
    })
    if ($unexpectedFiles.Count -gt 0) {
        $unexpectedNames = $unexpectedFiles.Name -join ", "
        throw "Unexpected release build outputs: $unexpectedNames"
    }

    $hash = (Get-FileHash -LiteralPath $outputDll -Algorithm SHA256).Hash
    $size = (Get-Item -LiteralPath $outputDll).Length
    Write-BuildLog "PASS BobCoach.dll bytes=$size sha256=$hash version=$($outputInfo.AssemblyName.Version) fileVersion=$fileVersion informational=$informationalVersion machine=$($outputInfo.Machine) peKind=$($outputInfo.PEKind)"
    Write-Host "PASS release build BobCoach.dll bytes=$size sha256=$hash"
} catch {
    Write-BuildLog "FAIL $($_.Exception.Message)"
    throw
} finally {
    if (Test-Path -LiteralPath $intermediateRoot) {
        Remove-Item -LiteralPath $intermediateRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
