# deploy_plugin_check.ps1 - BobCoach plugin deployment fixed flow (2026-08-20)
# =============================================================================
# Purpose: eliminate repeated deployment incidents (sha256 file format errors
# causing plugin integrity check FAILED -> plugin never loads).
# EVERY deployment MUST use this script. It validates with the EXACT same
# regex as plugin PluginIntegrityVerifier; any FAIL aborts with exit 1.
#
# Usage:
#   powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\release\deploy_plugin_check.ps1 `
#     -DllPath "C:\...\Temp\bobcoach-rXXX-build\BobCoach.dll"
#
# Optional:
#   -PluginsDirs "D:\...\Plugins;D:\...\Plugins;C:\...\Plugins"  (semicolon list)
#   -SkipHdtRunningCheck
#
# Flow:
#   1. verify dll exists + compute SHA256
#   2. build sha256 content: "{64UPPERHASH}  BobCoach.dll`r`n" (SINGLE CRLF, ASCII, no BOM)
#      regex identical to PluginIntegrityVerifier.ChecksumPattern:
#      ^([A-F0-9]{64})  BobCoach\.dll(?:\r?\n)?$
#   3. pre-validate the in-memory content against the regex BEFORE writing
#      (bad format never touches disk)
#   4. per-dir deploy: copy dll + write sha256, then READ BACK and re-validate
#      format + hash for every directory
#   5. any dir FAIL -> overall FAIL (exit 1), rollback that dir's sha256
# =============================================================================
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$DllPath,
    [string]$PluginsDirs = "",   # empty = auto-discover under D:\software\game + AppData
    [switch]$SkipHdtRunningCheck
)

$ErrorActionPreference = "Stop"

# Regex identical to plugin PluginIntegrityVerifier.ChecksumPattern
$ChecksumPattern = '^([A-F0-9]{64})  BobCoach\.dll(?:\r?\n)?$'

function Test-ChecksumFormat([string]$Text) {
    return [regex]::IsMatch($Text, $ChecksumPattern)
}

function Get-Sha256Hex([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

# -- 1. dll validation ------------------------------------------------
if (-not (Test-Path -LiteralPath $DllPath)) {
    Write-Error "dll not found: $DllPath"
    exit 1
}
$dllFull = [IO.Path]::GetFullPath($DllPath)
$expectedHash = Get-Sha256Hex $dllFull
Write-Host "dll: $dllFull"
Write-Host "SHA256: $expectedHash"

# -- 2. build content and pre-validate (never write bad format) -------
# NOTE: SINGLE CRLF. Double CRLF fails the plugin regex -> integrity FAILED.
$shaContent = "$expectedHash  BobCoach.dll`r`n"
if (-not (Test-ChecksumFormat $shaContent)) {
    Write-Error "generated sha256 content failed format regex - deploy aborted"
    exit 1
}
Write-Host "sha256 format: single CRLF ASCII no BOM (regex pre-check passed)"

# -- 3. HDT running guard (dll is locked while HDT is running) --------
if (-not $SkipHdtRunningCheck) {
    $hdtProc = @(Get-Process -Name "HearthstoneDeckTracker" -ErrorAction SilentlyContinue)
    if ($hdtProc.Count -gt 0) {
        Write-Error "HDT is running (PID $($hdtProc.Id -join ',')). Close HDT first - Plugins\BobCoach.dll is locked."
        exit 1
    }
}

# -- 4. per-dir deploy + read-back validation -------------------------
if ([string]::IsNullOrWhiteSpace($PluginsDirs)) {
    # auto-discover: D:\software\game\HDT*\HDT\Plugins + AppData HDT Plugins
    $found = @()
    $gameRoot = 'D:\software\game'
    if (Test-Path $gameRoot) {
        foreach ($sub in @(Get-ChildItem $gameRoot -Directory -Filter 'HDT*' -ErrorAction SilentlyContinue)) {
            $cand = Join-Path $sub.FullName 'HDT\Plugins'
            if (Test-Path $cand) { $found += $cand }
        }
    }
    $found += (Join-Path $env:APPDATA 'HearthstoneDeckTracker\Plugins')
    $PluginsDirs = ($found | Select-Object -Unique) -join ';'
    Write-Host "auto-discovered dirs: $PluginsDirs"
}
$dirs = @($PluginsDirs -split ';' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$okCount = 0
$failCount = 0
foreach ($dir in $dirs) {
    if (-not (Test-Path -LiteralPath $dir)) {
        Write-Warning "dir missing, skipped: $dir"
        $failCount++
        continue
    }
    $targetDll = Join-Path $dir "BobCoach.dll"
    $targetSha = Join-Path $dir "BobCoach.dll.sha256"
    $oldSha = $null
    if (Test-Path -LiteralPath $targetSha) { $oldSha = [IO.File]::ReadAllText($targetSha) }
    try {
        Copy-Item -LiteralPath $dllFull -Destination $targetDll -Force
        [IO.File]::WriteAllText($targetSha, $shaContent, (New-Object System.Text.ASCIIEncoding))
        $readBack = [IO.File]::ReadAllText($targetSha)
        $formatOk = Test-ChecksumFormat $readBack
        $hashOk = ((Get-Sha256Hex $targetDll) -eq $expectedHash)
        if ($formatOk -and $hashOk) {
            Write-Host "OK   $dir  [format:$formatOk hash:$hashOk]"
            $okCount++
        } else {
            Write-Error "FAIL $dir  [format:$formatOk hash:$hashOk]"
            if ($null -ne $oldSha) { [IO.File]::WriteAllText($targetSha, $oldSha, (New-Object System.Text.ASCIIEncoding)) }
            $failCount++
        }
    } catch {
        Write-Error "ERR  $dir : $($_.Exception.Message)"
        $failCount++
    }
}

Write-Host "=========================================="
Write-Host "deploy result: $okCount OK / $failCount FAIL"
Write-Host "SHA256: $expectedHash"
if ($failCount -gt 0) {
    Write-Error "deploy FAILED for some dirs - fix and redeploy"
    exit 1
}
Write-Host "deploy verified. Restart HDT - plugin integrity check will match this hash."