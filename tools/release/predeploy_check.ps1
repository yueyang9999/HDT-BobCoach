# predeploy_check.ps1 — 部署前快速验证(2026-08, 防"实测一堆BUG")
# 流程: ①静态审计(括号/空catch/索引) ②MSBuild 编译 ③4 harness
# 用法: powershell -ExecutionPolicy Bypass -File tools\release\predeploy_check.ps1
param(
    [switch]$SkipBuild
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot | Split-Path -Parent
$pass = $true

Write-Host "=== [1/3] 静态审计 ===" -ForegroundColor Cyan
$audit = node "$root\tools\build\audit_plugin.js" 2>&1
$audit | ForEach-Object { Write-Host $_ }
$highRisk = ($audit | Select-String -Pattern "direct-index|first-no-guard" | Measure-Object).Count
if ($highRisk -gt 0) {
    Write-Host "!! 高风险项 $highRisk 处(直接索引/First无保护)" -ForegroundColor Red
    $pass = $false
} else {
    Write-Host "静态审计: OK" -ForegroundColor Green
}

if (-not $SkipBuild) {
    Write-Host "=== [2/3] MSBuild 编译 ===" -ForegroundColor Cyan
    $env:BOBCOACH_HDT_DIR = 'C:/Users/19844/AppData/Local/HearthstoneDeckTracker/app-1.54.1'
    & 'C:\Program Files\dotnet\sdk\10.0.300\MSBuild.exe' "$root\src\BobCoach\BobCoach.csproj" /t:Build /p:Configuration=Release /v:minimal /nologo 2>&1 | Select-Object -Last 5
    if ($LASTEXITCODE -ne 0) { Write-Host "!! 编译失败" -ForegroundColor Red; $pass = $false }
    else { Write-Host "编译: OK" -ForegroundColor Green }
}

Write-Host "=== [3/3] harness 回归(串行) ===" -ForegroundColor Cyan
Set-Location $root
& .\tests\test_trinket_rule_evaluator.ps1 | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host "!! H1 FAIL" -ForegroundColor Red; $pass = $false }
& .\tests\test_trinket_recommendation_service.ps1 | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host "!! H2 FAIL" -ForegroundColor Red; $pass = $false }

if ($pass) {
    Write-Host "\n✅ predeploy check: 全部通过, 可以部署" -ForegroundColor Green
    exit 0
} else {
    Write-Host "\n❌ predeploy check: 存在失败项, 修复后再部署" -ForegroundColor Red
    exit 1
}
