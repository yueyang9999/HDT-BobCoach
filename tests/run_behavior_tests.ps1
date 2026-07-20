$ErrorActionPreference = "Stop"

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
    "test_offline_runtime_contract.js",
    "test_panel_statemachine.js",
    "test_ui_target_statemachine.js",
    "test_powerlog_choice_batch_behavior.js",
    "test_gold_tracker.js",
    "test_log_config_consent.js"
)) {
    Invoke-Checked $file "node"
}

foreach ($file in @(
    "test_value_function_weight_contract.ps1",
    "test_comp_strategy_exit_behavior.ps1",
    "test_card_effect_rule_evaluator.ps1",
    "test_card_effect_source.ps1",
    "test_effect_value_table.ps1",
    "test_prize_spell_rule_evaluator.ps1",
    "test_prize_spell_source.ps1",
    "test_prize_spell_decision.ps1",
    "test_hero_strategy_rule_evaluator.ps1",
    "test_hero_strategy_source.ps1",
    "test_hero_strategy_facade.ps1",
    "test_anomaly_rule_evaluator_core.ps1",
    "test_anomaly_rule_evaluator_scheduled.ps1",
    "test_anomaly_fact_source.ps1",
    "test_trinket_rule_evaluator.ps1",
    "test_trinket_recommendation_service.ps1",
    "test_trinket_reason_formatter.ps1",
    "test_card_semantic_rule_evaluator.ps1",
    "test_card_semantic_source.ps1",
    "test_semantic_synergy_evaluator.ps1"
)) {
    Invoke-Checked $file "powershell"
}

Write-Host "PASS behavior suite"
