$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$runner = Join-Path $root "tools\model\run_policy_benchmark.ps1"
$pathContracts = Join-Path $root "tools\model\path_contracts.psm1"
$source = Join-Path $root "tools\model\PolicyBenchmarkRunner.cs"
$factorySource = Join-Path $root "src\BobCoach\Core\Decision\DecisionPolicyFactory.cs"
if (!(Test-Path -LiteralPath $runner -PathType Leaf)) { throw "benchmark runner wrapper is missing" }
if (!(Test-Path -LiteralPath $pathContracts -PathType Leaf)) { throw "benchmark path contracts are missing" }
if (!(Test-Path -LiteralPath $source -PathType Leaf)) { throw "benchmark runner source is missing" }

Import-Module $pathContracts -Force
try {
    Assert-ExternalOutputPath -Path (Join-Path $root "forbidden-self-test-output") `
        -RepositoryRoot $root | Out-Null
    throw "repository self-test output was accepted"
} catch {
    if ($_.Exception.Message -notmatch "outside the repository") { throw }
}
$pathContractRoot = Join-Path (Split-Path -Qualifier $root) `
    ("HDT-BobCoach-path-contract-" + [Guid]::NewGuid().ToString("N"))
$pathContractData = Join-Path $pathContractRoot "data"
$rootJunction = Join-Path $pathContractRoot "root-junction"
$childJunction = Join-Path $pathContractData "child-junction"
try {
    New-Item -ItemType Directory -Path $pathContractData -Force | Out-Null
    New-Item -ItemType Junction -Path $rootJunction -Target $root | Out-Null
    try {
        Assert-ExternalDataRoot -Path $rootJunction -RepositoryRoot $root | Out-Null
        throw "DataRoot junction was accepted"
    } catch {
        if ($_.Exception.Message -notmatch "reparse point|outside the repository") { throw }
    }
    try {
        Assert-ExternalOutputPath -Path (Join-Path $rootJunction "self-test-output") `
            -RepositoryRoot $root | Out-Null
        throw "self-test output parent junction was accepted"
    } catch {
        if ($_.Exception.Message -notmatch "reparse point|outside the repository") { throw }
    }

    New-Item -ItemType Junction -Path $childJunction -Target $root | Out-Null
    $resolvedDataRoot = Assert-ExternalDataRoot -Path $pathContractData -RepositoryRoot $root
    foreach ($case in @(
        @{ Relative = "child-junction\AGENTS.md"; Name = "InputEvidence"; AllowMissing = $false; Type = "Leaf" },
        @{ Relative = "child-junction\new-output"; Name = "OutputEvidence"; AllowMissing = $true; Type = "Container" }
    )) {
        try {
            Resolve-DataPath -Root $resolvedDataRoot -RelativePath $case.Relative `
                -Name $case.Name -AllowMissing:$case.AllowMissing -ExpectedType $case.Type | Out-Null
            throw "$($case.Name) reparse path was accepted"
        } catch {
            if ($_.Exception.Message -notmatch "reparse point|escapes DataRoot") { throw }
        }
    }
} finally {
    foreach ($junction in @($childJunction, $rootJunction)) {
        if (Test-Path -LiteralPath $junction) { [IO.Directory]::Delete($junction) }
    }
    if (Test-Path -LiteralPath $pathContractRoot) {
        Remove-Item -LiteralPath $pathContractRoot -Recurse -Force
    }
}

$sourceText = [System.IO.File]::ReadAllText($source)
foreach ($required in @(
    "DecisionPolicyFactory.CreateForEffectEvaluation", "CandidateGenerator", "DecisionEventRecord.Create",
    "bundle.Legacy.Decide", "bundle.Unified.Decide", "DecideWithDiagnostics", "LoadEvaluationContext",
    "BestCompletePath", "legacy-v1.1.0", "CandidateEvidenceSurface",
    "ValidateDeclaredCandidates", "self-test must reject a mismatched candidate declaration",
    "CandidateBenchmarkAdapter", "ValidateProbeModelSource")) {
    if (!$sourceText.Contains($required)) { throw "benchmark path missing required evaluation surface: $required" }
}
if ($sourceText.Contains('.Where(property => !property.Name.StartsWith("_"')) {
    throw "underscore-prefixed protocol fields must remain checksum-bound"
}
$factorySourceText = [System.IO.File]::ReadAllText($factorySource)
foreach ($required in @("internal static DecisionPolicyBundle CreateForEffectEvaluation",
    "RankingModelLoader.Load", "UnifiedRankingPolicy.CreateForEffectEvaluation")) {
    if (!$factorySourceText.Contains($required)) { throw "evaluation factory contract is missing: $required" }
}
if ($sourceText.Contains("DecisionEventRecorder")) { throw "benchmark runner must not instantiate gameplay recording" }
if ($sourceText.Contains("new RankingModelRuntimeContext")) { throw "benchmark runner must not forge an accepted runtime context" }
foreach ($forbidden in @("DecisionRuntimeMode.LegacyOnly", "DecisionRuntimeMode.Unified")) {
    if ($sourceText.Contains($forbidden)) {
        throw "benchmark runner must not use $forbidden as candidate evidence"
    }
}
if ($sourceText.Contains("DecisionOrchestrator") -or $sourceText.Contains("ShadowComparison")) {
    throw "benchmark runner must call Unified diagnostics directly without Shadow orchestration"
}
$runnerText = [System.IO.File]::ReadAllText($runner)
foreach ($required in @("EvaluationProtocol", "--evaluation-protocol")) {
    if (!$runnerText.Contains($required)) { throw "benchmark wrapper does not pass the frozen protocol: $required" }
}
$buildText = [System.IO.File]::ReadAllText((Join-Path $root "docs\maintainer\BUILD.md"))
foreach ($required in @("protocols\season-2026-next-effect-v1.json",
    "-EvaluationProtocol", "--evaluation-protocol")) {
    if (!$buildText.Contains($required)) { throw "BUILD protocol example is incomplete: $required" }
}

$first = Join-Path $env:TEMP ("bobcoach_policy_evidence_a_" + [Guid]::NewGuid().ToString("N"))
$second = Join-Path $env:TEMP ("bobcoach_policy_evidence_b_" + [Guid]::NewGuid().ToString("N"))
try {
    & $runner -SelfTest -OutputDirectory $first
    if ($LASTEXITCODE -ne 0) { throw "first benchmark self-test failed" }
    & $runner -SelfTest -OutputDirectory $second
    if ($LASTEXITCODE -ne 0) { throw "second benchmark self-test failed" }

    foreach ($name in @("baseline-predictions.jsonl", "candidate-predictions.jsonl", "safety-probes.json")) {
        $a = (Get-FileHash (Join-Path $first $name) -Algorithm SHA256).Hash
        $b = (Get-FileHash (Join-Path $second $name) -Algorithm SHA256).Hash
        if ($a -ne $b) { throw "$name is not deterministic" }
    }

    $baseline = Get-Content (Join-Path $first "baseline-predictions.jsonl") | ConvertFrom-Json
    $candidate = Get-Content (Join-Path $first "candidate-predictions.jsonl") | ConvertFrom-Json
    if ($baseline.evidenceSchemaVersion -ne 1 -or $candidate.evidenceSchemaVersion -ne 1) { throw "prediction schema mismatch" }
    if ($baseline.policyId -ne "legacy-v1.1.0") { throw "baseline policy identity mismatch" }
    if ($baseline.scenarioId -ne $candidate.scenarioId -or
        $baseline.stateFingerprint -ne $candidate.stateFingerprint -or
        $baseline.candidateFingerprint -ne $candidate.candidateFingerprint) {
        throw "baseline and candidate evidence are not paired"
    }
    if (@($candidate.topActionKeys).Count -gt 3) { throw "candidate Top-N exceeds three actions" }
    if ($candidate.plannerStatus -eq "NotRequested" -or
        $candidate.selectedActionKey -eq $candidate.topActionKeys[0] -or
        @($candidate.bestCompletePath).Count -eq 0 -or
        $candidate.bestCompletePath[0] -ne $candidate.selectedActionKey) {
        throw "candidate evidence did not preserve the actual planner action and path"
    }

    $latency = Get-Content -Raw (Join-Path $first "latency.json") | ConvertFrom-Json
    if ($latency.latencyEvidenceSchemaVersion -ne 1 -or $latency.measurementsPerPolicyPerScenario -ne 10) {
        throw "latency schema or sample count mismatch"
    }
    if ($latency.baselinePolicyId -ne "legacy-v1.1.0" -or
        $latency.candidatePolicyId -ne "unified-linear-v1" -or
        $latency.candidateModelId -ne "synthetic-model" -or
        $latency.candidateModelPayloadSha256 -ne ("b" * 64)) {
        throw "latency policy or model identity mismatch"
    }
    foreach ($sample in @($latency.scenarios[0].baselineSamplesMilliseconds) + @($latency.scenarios[0].candidateSamplesMilliseconds)) {
        if ($sample -lt 0 -or [double]::IsNaN($sample) -or [double]::IsInfinity($sample)) { throw "invalid latency sample" }
    }

    $safety = Get-Content -Raw (Join-Path $first "safety-probes.json") | ConvertFrom-Json
    if ($safety.safetyEvidenceSchemaVersion -ne 1 -or @($safety.probes).Count -ne 7) { throw "safety evidence is incomplete" }
    $expectedProbeIds = @("critical-state-missing", "low-confidence", "model-unavailable", "no-safe-action", "out-of-distribution", "planner-timeout", "unsupported-mechanic")
    $actualProbeIds = @($safety.probes | ForEach-Object { $_.probeId } | Sort-Object)
    if (($actualProbeIds -join "|") -ne ($expectedProbeIds -join "|")) { throw "safety probe ID set mismatch" }
    if (@($safety.probes | Where-Object { !$_.passed }).Count -ne 0) { throw "self-test safety probe failed" }
    $timeout = @($safety.probes | Where-Object { $_.probeId -eq "planner-timeout" })[0]
    if ($timeout.actualFallbackReason -ne "None" -or $timeout.actualPlannerStatus -ne "TimedOut" -or
        [string]::IsNullOrWhiteSpace($timeout.actualSelectedActionKey) -or
        @($timeout.actualBestCompletePath).Count -eq 0 -or
        $timeout.actualBestCompletePath[0] -ne $timeout.actualSelectedActionKey) {
        throw "planner timeout is not a safe completed path"
    }

    $contracts = Get-Content -Raw (Join-Path $first "self-test-contracts.json") | ConvertFrom-Json
    foreach ($name in @("scenarioStateIdentityMismatchRejected", "unknownSafetyEnumRejected",
        "incompleteSafetyProbeSetRejected", "candidateOutputDistinctFromLegacy",
        "explicitFallbackUsesLegacySurface",
        "candidateLatencyIncludesLegacyFallback", "missingCandidateDecisionRejected",
        "protocolUnderscoreFieldsChecksumBound",
        "plannerActionDistinctFromRankedTopOne",
        "modelUnavailableCandidateSourceRejected", "candidateProbeNoneSourceRejected",
        "orchestratedSafetyOutcomesObserved")) {
        if ($contracts.$name -ne $true) { throw "self-test did not dynamically prove $name" }
    }

    if (Test-Path -LiteralPath (Join-Path $root "src\BobCoach\Resources\DecisionModels")) {
        throw "Task 12 must not create embedded model resources"
    }
    Write-Host "PASS benchmark evidence is paired, deterministic, recorder-free, and versioned"
}
finally {
    foreach ($path in @($first, $second)) {
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
    }
}
