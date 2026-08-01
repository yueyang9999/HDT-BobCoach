$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$runner = Join-Path $root "tools\model\run_policy_benchmark.ps1"
$pathContracts = Join-Path $root "tools\model\path_contracts.psm1"
$source = Join-Path $root "tools\model\PolicyBenchmarkRunner.cs"
$secureIoSource = Join-Path $root "tools\model\SecureEvidenceIo.cs"
$secureWriter = Join-Path $root "tools\model\secure_write_evidence.ps1"
$factorySource = Join-Path $root "src\BobCoach\Core\Decision\DecisionPolicyFactory.cs"
if (!(Test-Path -LiteralPath $runner -PathType Leaf)) { throw "benchmark runner wrapper is missing" }
if (!(Test-Path -LiteralPath $pathContracts -PathType Leaf)) { throw "benchmark path contracts are missing" }
if (!(Test-Path -LiteralPath $source -PathType Leaf)) { throw "benchmark runner source is missing" }
if (!(Test-Path -LiteralPath $secureIoSource -PathType Leaf)) { throw "secure evidence I/O source is missing" }
if (!(Test-Path -LiteralPath $secureWriter -PathType Leaf)) { throw "secure evidence writer is missing" }

function Invoke-SecureEvidenceWriter(
    [string]$Content,
    [string]$DataRoot,
    [string]$OutputPath
) {
    $startInfo = New-Object Diagnostics.ProcessStartInfo
    $startInfo.FileName = "powershell.exe"
    $startInfo.Arguments = '-NoProfile -ExecutionPolicy Bypass -File "{0}" -DataRoot "{1}" -OutputPath "{2}"' -f `
        $secureWriter.Replace('"', '\"'), $DataRoot.Replace('"', '\"'), $OutputPath.Replace('"', '\"')
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true

    $process = New-Object Diagnostics.Process
    $process.StartInfo = $startInfo
    if (!$process.Start()) { throw "secure evidence writer process did not start" }
    $inputBytes = (New-Object Text.UTF8Encoding($false)).GetBytes($Content)
    $process.StandardInput.BaseStream.Write($inputBytes, 0, $inputBytes.Length)
    $process.StandardInput.BaseStream.Flush()
    $process.StandardInput.Close()
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    [PSCustomObject]@{
        ExitCode = $process.ExitCode
        Stdout = $stdout
        Stderr = $stderr
    }
}

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

$secureContractRoot = Join-Path $env:TEMP `
    ("bobcoach_secure_evidence_" + [Guid]::NewGuid().ToString("N"))
$secureAttackRoot = Join-Path $env:TEMP `
    ("bobcoach_secure_evidence_attack_" + [Guid]::NewGuid().ToString("N"))
$secureInputRoot = Join-Path $env:TEMP `
    ("bobcoach_secure_input_" + [Guid]::NewGuid().ToString("N"))
$secureInputAttackRoot = Join-Path $env:TEMP `
    ("bobcoach_secure_input_attack_" + [Guid]::NewGuid().ToString("N"))
$secureInputDirectory = Join-Path $secureInputRoot "nested"
$secureInputFile = Join-Path $secureInputDirectory "scenarios.jsonl"
$renamedInputDirectory = Join-Path $secureInputRoot "locked-input"
$secureParent = Join-Path $secureContractRoot "nested"
$secureOutput = Join-Path $secureParent "output"
$renamedOutput = Join-Path $secureParent "locked-output"
$nonSystemDriveRoot = [IO.Path]::GetPathRoot($root)
$driveRootOutputName = "bobcoach_secure_drive_root_" + [Guid]::NewGuid().ToString("N")
$driveRootOutput = Join-Path $nonSystemDriveRoot $driveRootOutputName
$misdirectedDriveRootOutput = Join-Path $root $driveRootOutputName
$replacementJunctionCreated = $false
try {
    New-Item -ItemType Directory -Path $secureContractRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $secureAttackRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $secureInputDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path $secureInputAttackRoot -Force | Out-Null
    [IO.File]::WriteAllText($secureInputFile, "trusted-input", (New-Object Text.UTF8Encoding($false)))
    Add-Type -Path $secureIoSource
    $secureInputs = New-Object 'Collections.Generic.Dictionary[string,string]'
    $secureInputs.Add("scenarios", $secureInputFile)
    $inputSnapshot = [BobCoach.ModelTools.SecureInputSnapshot]::Capture(
        $secureInputRoot, $secureInputs)
    try {
        $inputDirectoryReplacementRejected = $false
        try {
            Rename-Item -LiteralPath $secureInputDirectory `
                -NewName (Split-Path -Leaf $renamedInputDirectory) -ErrorAction Stop
            New-Item -ItemType Junction -Path $secureInputDirectory `
                -Target $secureInputAttackRoot | Out-Null
        } catch {
            $inputDirectoryReplacementRejected = $true
        }
        if (!$inputDirectoryReplacementRejected) {
            throw "secure input snapshot allowed a concurrent directory rename and junction replacement"
        }
        if ((New-Object Text.UTF8Encoding($false)).GetString($inputSnapshot.GetBytes("scenarios")) `
                -ne "trusted-input") {
            throw "secure input snapshot did not preserve the bytes read from retained handles"
        }
    } finally {
        $inputSnapshot.Dispose()
    }
    $scope = [BobCoach.ModelTools.SecureEvidenceDirectory]::Acquire(
        $secureContractRoot, $secureOutput)
    try {
        if (!(Test-Path -LiteralPath $secureOutput -PathType Container)) {
            throw "secure evidence acquisition did not create the output hierarchy"
        }
        $directoryReplacementRejected = $false
        try {
            Rename-Item -LiteralPath $secureOutput -NewName (Split-Path -Leaf $renamedOutput) `
                -ErrorAction Stop
            New-Item -ItemType Junction -Path $secureOutput -Target $secureAttackRoot | Out-Null
            $replacementJunctionCreated = $true
        } catch {
            $directoryReplacementRejected = $true
        }
        if (!$directoryReplacementRejected) {
            throw "secure evidence directory allowed a concurrent rename"
        }
        $scope.WriteAllText("locked.json", "trusted")
        $trustedResult = if ($directoryReplacementRejected) {
            Join-Path $secureOutput "locked.json"
        } else {
            Join-Path $renamedOutput "locked.json"
        }
        if (!(Test-Path -LiteralPath $trustedResult -PathType Leaf) -or
            (Test-Path -LiteralPath (Join-Path $secureAttackRoot "locked.json") -PathType Leaf)) {
            throw "secure evidence write followed a concurrently replaced junction"
        }
    } finally {
        $scope.Dispose()
    }

    if ($replacementJunctionCreated -and (Test-Path -LiteralPath $secureOutput)) {
        [IO.Directory]::Delete($secureOutput)
        $replacementJunctionCreated = $false
    }
    $existingDirectory = if (Test-Path -LiteralPath $renamedOutput -PathType Container) {
        $renamedOutput
    } else {
        $secureOutput
    }
    $existingOutput = Join-Path $existingDirectory "existing.json"
    $originalContent = "original-" + [char]0x4E2D
    $firstWrite = Invoke-SecureEvidenceWriter -Content $originalContent `
        -DataRoot $secureContractRoot -OutputPath $existingOutput
    if ($firstWrite.ExitCode -ne 0) {
        throw "secure evidence writer failed its first publish: $($firstWrite.Stderr)"
    }
    if (!(Test-Path -LiteralPath $existingOutput -PathType Leaf) -or
        [IO.File]::ReadAllText($existingOutput).TrimEnd() -ne $originalContent) {
        $actualContent = if (Test-Path -LiteralPath $existingOutput -PathType Leaf) {
            [IO.File]::ReadAllText($existingOutput)
        } else {
            $null
        }
        $actualCodes = if ($null -eq $actualContent) {
            "missing"
        } else {
            (@($actualContent.ToCharArray() | ForEach-Object { [int]$_ }) -join ",")
        }
        throw "secure evidence writer did not publish its first output: path=$existingOutput; actualCodes=$actualCodes"
    }
    $secondWrite = Invoke-SecureEvidenceWriter -Content "replacement" `
        -DataRoot $secureContractRoot -OutputPath $existingOutput
    if ($secondWrite.ExitCode -eq 0 -or
        [IO.File]::ReadAllText($existingOutput).TrimEnd() -ne $originalContent) {
        throw "secure evidence writer overwrote an existing evidence file"
    }
    if (@(Get-ChildItem -LiteralPath $existingDirectory -Filter ".bobcoach-staging-*" -Force).Count -ne 0) {
        throw "secure evidence writer left a staging file after rejected overwrite"
    }

    if (![IO.Path]::GetPathRoot($env:SystemDrive + [IO.Path]::DirectorySeparatorChar).Equals(
            $nonSystemDriveRoot, [StringComparison]::OrdinalIgnoreCase)) {
        $driveRootScope = [BobCoach.ModelTools.SecureEvidenceDirectory]::Acquire(
            $nonSystemDriveRoot, $driveRootOutput)
        try {
            $driveRootScope.WriteAllText("root.json", "declared-root")
        } finally {
            $driveRootScope.Dispose()
        }
        if (!(Test-Path -LiteralPath (Join-Path $driveRootOutput "root.json") -PathType Leaf) -or
            (Test-Path -LiteralPath $misdirectedDriveRootOutput)) {
            throw "secure evidence drive-root DataRoot did not preserve its declared absolute path"
        }
    }
} finally {
    if ($replacementJunctionCreated -and (Test-Path -LiteralPath $secureOutput)) {
        [IO.Directory]::Delete($secureOutput)
    }
    if (Test-Path -LiteralPath $secureContractRoot) {
        Remove-Item -LiteralPath $secureContractRoot -Recurse -Force
    }
    if (Test-Path -LiteralPath $secureAttackRoot) {
        Remove-Item -LiteralPath $secureAttackRoot -Recurse -Force
    }
    if (Test-Path -LiteralPath $secureInputRoot) {
        Remove-Item -LiteralPath $secureInputRoot -Recurse -Force
    }
    if (Test-Path -LiteralPath $secureInputAttackRoot) {
        Remove-Item -LiteralPath $secureInputAttackRoot -Recurse -Force
    }
    if (Test-Path -LiteralPath $driveRootOutput) {
        Remove-Item -LiteralPath $driveRootOutput -Recurse -Force
    }
    if (Test-Path -LiteralPath $misdirectedDriveRootOutput) {
        Remove-Item -LiteralPath $misdirectedDriveRootOutput -Recurse -Force
    }
}

$sourceText = [System.IO.File]::ReadAllText($source)
foreach ($required in @(
    "DecisionPolicyFactory.CreateForEffectEvaluation", "CandidateGenerator", "DecisionEventRecord.Create",
    "bundle.Legacy.Decide", "bundle.Unified.Decide", "DecideWithDiagnostics", "LoadEvaluationContext",
    "BestCompletePath", "legacy-v1.1.0", "CandidateEvidenceSurface",
    "ValidateDeclaredCandidates", "self-test must reject a mismatched candidate declaration",
    "CandidateBenchmarkAdapter", "ValidateProbeModelSource", "SecureEvidenceDirectory",
    "SecureInputSnapshot.Capture", "LoadScenarios(scenarioBytes)",
    "LoadEvaluationContext(protocolBytes, scenarioBytes)", "new MemoryStream(modelBytes, false)",
    "RunSafetyProbes(probeBytes")) {
    if (!$sourceText.Contains($required)) { throw "benchmark path missing required evaluation surface: $required" }
}
if (!$sourceText.Contains('{ "candidateOutputDistinctFromLegacy", candidateOutputDistinctFromLegacy }') -or
    $sourceText.Contains('{ "candidateOutputDistinctFromLegacy", true }')) {
    throw "candidate output distinctness contract must use the computed selected-action comparison"
}
foreach ($required in @('ParseStrictJsonLine(line, "scenario")',
    'ParseStrictJsonLine(line, "safety probe")')) {
    if (!$sourceText.Contains($required)) { throw "benchmark path missing strict JSONL parsing: $required" }
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
foreach ($required in @("EvaluationProtocol", "--evaluation-protocol", '"--data-root", $resolvedDataRoot')) {
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
        $candidate.selectedActionKey -ne $candidate.topActionKeys[0] -or
        @($candidate.bestCompletePath).Count -eq 0 -or
        $candidate.bestCompletePath[0] -eq $candidate.selectedActionKey) {
        throw "candidate evidence did not preserve production SelectedAction and planner diagnostics separately"
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
    if ($safety.safetyEvidenceSchemaVersion -ne 2 -or @($safety.probes).Count -ne 7) { throw "safety evidence is incomplete" }
    $expectedProbeIds = @("critical-state-missing", "low-confidence", "model-unavailable", "no-safe-action", "out-of-distribution", "planner-timeout", "unsupported-mechanic")
    $actualProbeIds = @($safety.probes | ForEach-Object { $_.probeId } | Sort-Object)
    if (($actualProbeIds -join "|") -ne ($expectedProbeIds -join "|")) { throw "safety probe ID set mismatch" }
    if (@($safety.probes | Where-Object { !$_.passed }).Count -ne 0) { throw "self-test safety probe failed" }
    foreach ($probe in @($safety.probes | Where-Object { $_.actualFallbackReason -ne "None" })) {
        if ([string]::IsNullOrWhiteSpace($probe.legacySelectedActionKey) -or
            $probe.actualSelectedActionKey -ne $probe.legacySelectedActionKey -or
            (@($probe.actualTopActionKeys) -join "|") -ne (@($probe.legacyTopActionKeys) -join "|")) {
            throw "fallback safety probe did not use the legacy action surface: $($probe.probeId)"
        }
    }
    $timeout = @($safety.probes | Where-Object { $_.probeId -eq "planner-timeout" })[0]
    if ($timeout.actualFallbackReason -ne "None" -or $timeout.actualPlannerStatus -ne "TimedOut" -or
        [string]::IsNullOrWhiteSpace($timeout.actualSelectedActionKey) -or
        @($timeout.actualTopActionKeys).Count -eq 0 -or
        $timeout.actualTopActionKeys[0] -ne $timeout.actualSelectedActionKey -or
        @($timeout.actualBestCompletePath).Count -eq 0 -or
        $timeout.actualBestCompletePath[0] -eq $timeout.actualSelectedActionKey) {
        throw "planner timeout is not a safe completed path"
    }

    $contracts = Get-Content -Raw (Join-Path $first "self-test-contracts.json") | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace($contracts.baselineSelectedActionKey) -or
        [string]::IsNullOrWhiteSpace($contracts.candidateSelectedActionKey) -or
        $contracts.baselineSelectedActionKey -eq $contracts.candidateSelectedActionKey) {
        throw "self-test did not dynamically prove distinct baseline and candidate selected actions"
    }
    foreach ($name in @("scenarioStateIdentityMismatchRejected", "unknownSafetyEnumRejected",
        "incompleteSafetyProbeSetRejected", "candidateOutputDistinctFromLegacy",
        "explicitFallbackUsesLegacySurface",
        "candidateLatencyIncludesLegacyFallback", "missingCandidateDecisionRejected",
        "protocolUnderscoreFieldsChecksumBound",
        "protocolNumericCanonicalizationCrossLanguage",
        "protocolUnsafeIntegerRejected",
        "productionSelectedActionDistinctFromPlannerDiagnostic",
        "modelUnavailableCandidateSourceRejected", "candidateProbeNoneSourceRejected",
        "scenarioDuplicatePropertyRejected", "safetyProbeDuplicatePropertyRejected",
        "orchestratedSafetyOutcomesObserved", "safetyProbeFallbackUsesLegacySurface")) {
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
