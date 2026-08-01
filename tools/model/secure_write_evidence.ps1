[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$DataRoot,
    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"
Add-Type -Path (Join-Path $PSScriptRoot "SecureEvidenceIo.cs")
[Console]::InputEncoding = New-Object Text.UTF8Encoding($false)
$content = [Console]::In.ReadToEnd()
[BobCoach.ModelTools.SecureEvidenceIo]::WriteAllText($DataRoot, $OutputPath, $content)
