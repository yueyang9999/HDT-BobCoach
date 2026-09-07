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
    "test_ui_lifecycle.js",
    "test_status_strip_ui_contract.js",
    "test_shop_entity_snapshot.js",
    "test_shop_layout_slot_policy.js",
    "test_powerlog_choice_batch_behavior.js",
    "test_gold_tracker.js",
    "test_log_config_consent.js",
    "test_season14_guide_sync.js",
    "test_season14_guide_fetch.js",
    "test_season14_guide_refresh.js",
    "test_season14_guide_weights.js",
    "test_replay_decision_evaluator.js",
    "test_replay_snapshot_analyzer.js",
    "test_firestone_review_extract.js",
    "test_firestone_xml_to_snapshot.js",
    "test_global_replay_recorder.js",
    "test_global_replay_analyzer.js",
    "test_replay_sequence_compare.js",
    "test_structural_upgrade_contract.js",
    "test_t33_live_feedback_contract.js",
    "test_t34_live_feedback_contract.js"
)) {
    Invoke-Checked $file "node"
}

foreach ($file in @(
    "test_status_strip_layout_policy.ps1",
    "test_shop_entity_snapshot.ps1",
    "test_shop_layout_slot_policy.ps1",
    "test_layout_config_migration.ps1",
    "test_layout_resolution_matrix.ps1",
    "test_value_function_weight_contract.ps1",
    "test_algorithm_features.ps1",
    "test_algorithm_model_contract.ps1",
    "test_algorithm_policy_contract.ps1",
    "test_algorithm_candidates.ps1",
    "test_algorithm_decision_events.ps1",
    "test_algorithm_observed_actions.ps1",
    "test_algorithm_orchestrator.ps1",
    "test_plugin_ranking_entrypoint.ps1",
    "test_algorithm_linear_ranker.ps1",
    "test_algorithm_confidence.ps1",
    "test_algorithm_beam_planner.ps1",
    "test_algorithm_explanations.ps1",
    "test_comp_strategy_exit_behavior.ps1",
    "test_algorithm_guide_weight.ps1",
    "test_card_effect_rule_evaluator.ps1",
    "test_card_effect_source.ps1",
    "test_effect_value_table.ps1",
    "test_prize_spell_rule_evaluator.ps1",
    "test_prize_spell_source.ps1",
    "test_prize_spell_decision.ps1",
    "test_hero_strategy_rule_evaluator.ps1",
    "test_early_turn_recruit_gate.ps1",
    "test_hero_strategy_source.ps1",
    "test_hero_strategy_facade.ps1",
    "test_battlegrounds_hero_identity.ps1",
    "test_hero_power_rule_catalog.ps1",
    "test_hero_playbook_registry.ps1",
    "test_hero_playbook_decision_service.ps1",
    "test_opponent_tech_advisor.ps1",
    "test_strategic_plan_contract.ps1",
    "test_anomaly_rule_evaluator_core.ps1",
    "test_anomaly_rule_evaluator_scheduled.ps1",
    "test_anomaly_fact_source.ps1",
    "test_trinket_rule_evaluator.ps1",
    "test_trinket_recommendation_service.ps1",
    "test_trinket_stats_verifier.ps1",
    "test_plugin_integrity_verifier.ps1",
    "test_trinket_reason_formatter.ps1",
    "test_card_semantic_rule_evaluator.ps1",
    "test_card_semantic_source.ps1",
    "test_semantic_synergy_evaluator.ps1",
    "test_active_trinket_effects.ps1",
    "test_season14_registries.ps1",
    "test_phase1_live_decision.ps1",
    "test_phase2_decision_services.ps1",
    "test_phase3_recommendation_plan.ps1",
    "test_t35_live_feedback.ps1",
    "test_combat_phase_signal.ps1",
    "test_minion_text_util.ps1"
)) {
    Invoke-Checked $file "powershell"
}

Write-Host "PASS behavior suite"
