[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Add-Type -Path (Join-Path $PSScriptRoot "SecureEvidenceIo.cs")
[Console]::InputEncoding = New-Object Text.UTF8Encoding($false)
[Console]::OutputEncoding = New-Object Text.UTF8Encoding($false)

$manifest = [Console]::In.ReadToEnd() | ConvertFrom-Json
if ($null -eq $manifest -or [string]::IsNullOrWhiteSpace([string]$manifest.dataRoot) -or
    $null -eq $manifest.files) {
    throw "secure input manifest is invalid"
}

$inputs = New-Object 'Collections.Generic.Dictionary[string,string]'
foreach ($property in $manifest.files.PSObject.Properties) {
    if ([string]::IsNullOrWhiteSpace($property.Name) -or
        [string]::IsNullOrWhiteSpace([string]$property.Value)) {
        throw "secure input manifest contains an invalid file"
    }
    $inputs.Add($property.Name, [string]$property.Value)
}

$snapshot = [BobCoach.ModelTools.SecureInputSnapshot]::Capture(
    [string]$manifest.dataRoot, $inputs)
try {
    $captured = [ordered]@{}
    foreach ($name in $inputs.Keys) {
        $captured[$name] = [Convert]::ToBase64String($snapshot.GetBytes($name))
    }
    $captured | ConvertTo-Json -Compress
} finally {
    $snapshot.Dispose()
}
