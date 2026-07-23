[CmdletBinding()]
param(
    [string]$RepositoryRoot
)

$ErrorActionPreference = "Stop"
$MaximumFileBytes = 5MB
$forbiddenDirectories = @(
    ".debate", ".mcp", "sessions", "local-data", "vm", "data", "_authority",
    "simulation", "electron", "artifacts", "eval", "bin", "obj", "TestResults", "coverage"
)
$forbiddenBinaryExtensions = @(
    ".dll", ".exe", ".zip", ".iso", ".vhd", ".vhdx", ".vmdk", ".ova", ".qcow", ".qcow2",
    ".png", ".jpg", ".jpeg", ".gif", ".bmp"
)
$allowedInstallationImagePaths = @(
    "docs/user/images/install/install-01-exit-hdt.png",
    "docs/user/images/install/install-02-open-plugins-folder.png",
    "docs/user/images/install/install-03-copy-bobcoach-dll.png",
    "docs/user/images/install/install-04-enable-bobcoach.png"
)
$binaryExtensions = $forbiddenBinaryExtensions + @(
    ".7z", ".rar", ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".pdf", ".mp3", ".mp4", ".wav", ".woff", ".woff2", ".ttf", ".pdb"
)
$sensitiveFilePattern = '(?i)(^|/)(\.env(?:\..*)?|[^/]*\.(pfx|p12|snk|pem|key)|[^/]*(credential|secret|private[-_]?key)[^/]*)$'
$replayOrLogPattern = '(?i)(\.replay|\.log|\.dmp|power\.log|crash[-_]?dump)($|/)'
$secretPatterns = @(
    '(?i)\b(?:ghp|gho|ghu|ghs|ghr)_[A-Za-z0-9]{20,}\b',
    '(?i)\bgithub_pat_[A-Za-z0-9_]{20,}\b',
    '(?i)\bsk-(?:live|proj|test)-[A-Za-z0-9]{16,}\b',
    '(?i)\b(?:api[_-]?key|access[_-]?token|client[_-]?secret)\s*[:=]\s*["''][^"''\r\n]{12,}'
)
$personalPathPatterns = @(
    '(?i)\b[A-Z]:\\Users\\',
    '(?i)\b[A-Z]:\\Documents and Settings\\'
)

function Get-RepositoryPaths([string]$Root) {
    $tracked = @(& git -c core.quotepath=false -C $Root ls-files)
    if ($LASTEXITCODE -ne 0) { throw "Unable to enumerate Git tracked files in: $Root" }

    $untracked = @(& git -c core.quotepath=false -C $Root ls-files --others --exclude-standard)
    if ($LASTEXITCODE -ne 0) { throw "Unable to enumerate Git untracked files in: $Root" }

    return @($tracked + $untracked | Where-Object { ![string]::IsNullOrWhiteSpace([string]$_) } | Sort-Object -Unique)
}

function Test-BinaryContent([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    try {
        $buffer = New-Object byte[] 8192
        $read = $stream.Read($buffer, 0, $buffer.Length)
        if ($read -eq 0) { return $false }
        $hasUtf16Bom = $read -ge 2 -and (
            ($buffer[0] -eq 0xFF -and $buffer[1] -eq 0xFE) -or
            ($buffer[0] -eq 0xFE -and $buffer[1] -eq 0xFF)
        )
        $hasUtf32Bom = $read -ge 4 -and (
            ($buffer[0] -eq 0xFF -and $buffer[1] -eq 0xFE -and $buffer[2] -eq 0x00 -and $buffer[3] -eq 0x00) -or
            ($buffer[0] -eq 0x00 -and $buffer[1] -eq 0x00 -and $buffer[2] -eq 0xFE -and $buffer[3] -eq 0xFF)
        )
        if ($hasUtf16Bom -or $hasUtf32Bom) { return $false }
        $controlBytes = 0
        for ($index = 0; $index -lt $read; $index++) {
            $value = $buffer[$index]
            if ($value -eq 0) { return $true }
            if ($value -lt 9 -or ($value -gt 13 -and $value -lt 32)) { $controlBytes++ }
        }
        return $controlBytes -gt ($read / 4)
    } finally {
        $stream.Dispose()
    }
}

function Add-Failure([System.Collections.Generic.List[string]]$Failures, [string]$Category, [string]$RelativePath, [string]$Detail) {
    $Failures.Add(("[{0}] {1} {2}" -f $Category, $RelativePath, $Detail))
}

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
}
$repoRoot = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\')
if (!(Test-Path -LiteralPath $repoRoot -PathType Container)) { throw "Repository root not found: $repoRoot" }
if (!(Test-Path -LiteralPath (Join-Path $repoRoot ".git"))) { throw "Repository root is not a Git worktree: $repoRoot" }

$failures = New-Object 'System.Collections.Generic.List[string]'
$paths = Get-RepositoryPaths $repoRoot
foreach ($gitPath in $paths) {
    $relativePath = ([string]$gitPath).Replace('\\', '/').TrimStart('/')
    if ($relativePath.StartsWith(".git/", [StringComparison]::OrdinalIgnoreCase)) { continue }
    $fullPath = [IO.Path]::GetFullPath((Join-Path $repoRoot $relativePath))
    if (!$fullPath.StartsWith($repoRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
        Add-Failure $failures "unsafe-path" $relativePath "path resolves outside repository root"
        continue
    }
    if (!(Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        Add-Failure $failures "missing-file" $relativePath "Git path does not resolve to a file"
        continue
    }

    $segments = $relativePath.Split('/')
    $forbiddenDirectory = @($segments | Where-Object { $forbiddenDirectories -contains $_ }).Count -gt 0
    if ($forbiddenDirectory) {
        Add-Failure $failures "forbidden-directory" $relativePath "directory is not allowed in the public repository"
        continue
    }
    if ($relativePath -match $sensitiveFilePattern) {
        Add-Failure $failures "sensitive-file" $relativePath "sensitive filename or key material is not allowed"
        continue
    }
    if ($relativePath -match $replayOrLogPattern) {
        Add-Failure $failures "replay-or-log-data" $relativePath "replay, log, or crash data is not allowed"
        continue
    }

    $extension = [IO.Path]::GetExtension($relativePath).ToLowerInvariant()
    $isAllowedInstallationImage = $extension -eq ".png" -and $allowedInstallationImagePaths -ccontains $relativePath
    if (($forbiddenBinaryExtensions -contains $extension) -and !$isAllowedInstallationImage) {
        Add-Failure $failures "forbidden-binary-or-image" $relativePath "extension $extension is not allowed"
        continue
    }

    $fileInfo = Get-Item -LiteralPath $fullPath
    if ($fileInfo.Length -gt $MaximumFileBytes) {
        Add-Failure $failures "large-file" $relativePath ("bytes={0}; maximum={1} bytes (5 MiB)" -f $fileInfo.Length, $MaximumFileBytes)
        continue
    }
    if ($binaryExtensions -contains $extension -or (Test-BinaryContent $fullPath)) { continue }

    $content = [IO.File]::ReadAllText($fullPath, [Text.Encoding]::UTF8)
    foreach ($pattern in $secretPatterns) {
        if ($content -match $pattern) {
            Add-Failure $failures "secret-or-token" $relativePath "credential-like content detected"
            break
        }
    }
    foreach ($pattern in $personalPathPatterns) {
        if ($content -match $pattern) {
            Add-Failure $failures "personal-absolute-path" $relativePath "user profile or migration-source path detected"
            break
        }
    }
}

if ($failures.Count -gt 0) {
    Write-Host ("FAIL repository validation: {0} issue(s) across {1} Git tracked or untracked non-ignored file(s); large-file limit is {2} bytes (5 MiB)." -f $failures.Count, $paths.Count, $MaximumFileBytes)
    $failures | Sort-Object | ForEach-Object { Write-Host $_ }
    exit 1
}

Write-Host ("PASS repository validation: {0} Git tracked or untracked non-ignored file(s) scanned; large-file limit is {1} bytes (5 MiB)." -f $paths.Count, $MaximumFileBytes)
