[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$SourceProjectDirectory,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$DestinationProjectDirectory
)

$ErrorActionPreference = "Stop"
$source = [System.IO.Path]::GetFullPath($SourceProjectDirectory)
$destination = [System.IO.Path]::GetFullPath($DestinationProjectDirectory)
$projectPath = Join-Path $source "BobCoach.csproj"

if (!(Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "Source project not found: $projectPath"
}

$project = [xml](Get-Content -Raw -LiteralPath $projectPath)
$namespace = New-Object System.Xml.XmlNamespaceManager($project.NameTable)
$namespace.AddNamespace("msb", "http://schemas.microsoft.com/developer/msbuild/2003")
$items = @($project.SelectNodes("//msb:Compile | //msb:Resource", $namespace))

New-Item -ItemType Directory -Path $destination -Force | Out-Null
Copy-Item -LiteralPath $projectPath -Destination (Join-Path $destination "BobCoach.csproj") -Force

foreach ($item in $items) {
    $relativePath = [string]$item.Include
    $sourcePath = Join-Path $source $relativePath
    $destinationPath = Join-Path $destination $relativePath
    if (!(Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        throw "Declared project item is missing: $sourcePath"
    }
    New-Item -ItemType Directory -Path (Split-Path -Parent $destinationPath) -Force | Out-Null
    Copy-Item -LiteralPath $sourcePath -Destination $destinationPath -Force
}

$missing = @($items | Where-Object {
    !(Test-Path -LiteralPath (Join-Path $destination ([string]$_.Include)) -PathType Leaf)
})
if ($missing.Count -ne 0) {
    throw "Destination verification failed for $($missing.Count) project item(s)"
}

$compileCount = @($project.SelectNodes("//msb:Compile", $namespace)).Count
$resourceCount = @($project.SelectNodes("//msb:Resource", $namespace)).Count
Write-Host "PASS project allowlist copied compile=$compileCount resource=$resourceCount destination=$destination"
