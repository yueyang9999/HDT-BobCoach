[CmdletBinding()]
param(
    [string]$EvidenceRoot = "E:\HDT-BobCoach-evidence\season14\generated\ui-evidence",
    [string]$HearthstoneLogs = "D:\software\game\Hearthstone\Logs"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$expectedSha256 = "B0D9A1A272733D01ED9102EDDE32751155ECF39B3CAC2F4B55D4836AE15F32F3"

function Assert-PluginHash([string]$Path) {
    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Plugin DLL missing: $Path"
    }
    $hash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    if ($hash -ne $expectedSha256) {
        throw "Plugin DLL hash mismatch: $Path hash=$hash expected=$expectedSha256"
    }
    $checksumPath = $Path + ".sha256"
    if (!(Test-Path -LiteralPath $checksumPath -PathType Leaf)) {
        throw "Plugin checksum missing: $checksumPath"
    }
    $checksumText = (Get-Content -LiteralPath $checksumPath -Raw).Trim()
    if ($checksumText -ne "$expectedSha256  BobCoach.dll") {
        throw "Plugin checksum format mismatch: $checksumPath content=$checksumText"
    }
    Write-Host "PASS plugin DLL $Path sha256=$hash checksum=$checksumText"
}

function Find-LatestPowerLog([string]$LogsRoot) {
    if (!(Test-Path -LiteralPath $LogsRoot -PathType Container)) {
        throw "Hearthstone logs directory missing: $LogsRoot"
    }
    $directories = Get-ChildItem -LiteralPath $LogsRoot -Directory |
        Sort-Object LastWriteTime -Descending
    foreach ($directory in $directories) {
        foreach ($name in @("Power.log", "Power_old.log")) {
            $candidate = Join-Path $directory.FullName $name
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                $size = (Get-Item -LiteralPath $candidate).Length
                if ($size -gt 0) {
                    return [PSCustomObject]@{
                        Path = $candidate
                        Bytes = $size
                        Session = $directory.Name
                    }
                }
            }
        }
    }
    return $null
}

$pluginDlls = @(
    (Join-Path $env:APPDATA "HearthstoneDeckTracker\Plugins\BobCoach.dll"),
    "D:\software\game\HDT-V2.7.0\HDT\Plugins\BobCoach.dll",
    (Join-Path $env:LOCALAPPDATA "HearthstoneDeckTracker\app-1.54.1\Plugins\BobCoach.dll"),
    (Join-Path $env:LOCALAPPDATA "HearthstoneDeckTracker\app-1.54.0\Plugins\BobCoach.dll")
)
foreach ($pluginDll in $pluginDlls) {
    if (Test-Path -LiteralPath $pluginDll -PathType Leaf) {
        Assert-PluginHash $pluginDll
    }
}

New-Item -ItemType Directory -Path $EvidenceRoot -Force | Out-Null
$latest = Find-LatestPowerLog $HearthstoneLogs
if ($null -eq $latest) {
    Write-Host "NO_NONEMPTY_POWER_LOG"
} else {
    Write-Host "LATEST_POWER_LOG=$($latest.Path) bytes=$($latest.Bytes)"
    $tailFile = Join-Path $EvidenceRoot "power-latest-tail-20000.log"
    Get-Content -LiteralPath $latest.Path -Tail 20000 |
        Set-Content -LiteralPath $tailFile -Encoding UTF8
    Write-Host "REPLAY_TAIL=$tailFile"
    & node (Join-Path $repoRoot "tests\test_powerlog_choice_batch_behavior.js") $tailFile --replay-only
    if ($LASTEXITCODE -ne 0) { throw "Power.log replay failed" }
}

Write-Host "REAL_UI_EVIDENCE_DIR=$EvidenceRoot"
Write-Host "REQUIRED_EVIDENCE=screenshot-or-video plus latest Power.log and BobCoach plugin log"
Write-Host "PASS real UI evidence collector"
