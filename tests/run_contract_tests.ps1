$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

function Invoke-Checked([string]$File, [string]$Kind) {
    $path = Join-Path $PSScriptRoot $File
    if ($Kind -eq "node") {
        & node $path
    } else {
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $path
    }
    if ($LASTEXITCODE -ne 0) { throw "$File failed with exit code $LASTEXITCODE" }
}

foreach ($file in @(
    "test_clean_checkout_contract.js",
    "test_public_documentation_contract.js",
    "test_ci_hdt_baseline_contract.js",
    "test_behavior_output_contract.js",
    "test_repository_validator.js",
    "test_release_build_contract.js",
    "test_release_identity.js",
    "test_release_package_contract.js"
)) {
    Invoke-Checked $file "node"
}
Invoke-Checked "test_release_build_script.ps1" "powershell"

Write-Host "PASS contract suite"
