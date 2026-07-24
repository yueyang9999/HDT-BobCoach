$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$scriptPath = Join-Path $repoRoot "tools\build\build_release.ps1"
$errors = New-Object System.Collections.Generic.List[string]

if (!(Test-Path -LiteralPath $scriptPath -PathType Leaf)) {
    Write-Host "FAIL release build script contract"
    Write-Host "- missing tools/build/build_release.ps1"
    exit 1
}

$source = [IO.File]::ReadAllText($scriptPath, [Text.Encoding]::UTF8)
$tokens = $null
$parseErrors = $null
[Management.Automation.Language.Parser]::ParseFile($scriptPath, [ref]$tokens, [ref]$parseErrors) | Out-Null
if ($parseErrors.Count -gt 0) { $errors.Add("PowerShell AST parse errors=$($parseErrors.Count)") }

$required = [ordered]@{
    'param\s*\(' = 'param block'
    '\[string\]\s*\$HdtDirectory' = 'HdtDirectory parameter'
    '\[string\]\s*\$OutputDirectory' = 'OutputDirectory parameter'
    '\[switch\]\s*\$Force' = 'Force parameter'
    'release_identity\.json' = 'release identity input'
    "packageVersion -notmatch '\^\\d\+\\\.\\d\+\\\.\\d\+\$'" = 'stable semantic package version gate'
    'hdtBaselineVersion' = 'HDT baseline validation'
    'HearthstoneDeckTracker\.exe' = 'HDT executable validation'
    'HearthDb\.dll' = 'HearthDb validation'
    'Newtonsoft\.Json\.dll' = 'Newtonsoft.Json validation'
    'GetPEKind' = 'PE inspection'
    'PE32Plus' = 'PE32Plus output gate'
    'AMD64' = 'AMD64 output gate'
    '/p:Configuration=Release' = 'Release build property'
    '/p:Platform=x64' = 'x64 build property'
    'BobCoach\.csproj' = 'project input'
    'BobCoach\.dll' = 'DLL output'
    'build_log\.txt' = 'build log output'
}

if ($source -match 'packageVersion[^\r\n]+beta') {
    $errors.Add('release build still requires a beta package version')
}
foreach ($entry in $required.GetEnumerator()) {
    if ($source -notmatch $entry.Key) { $errors.Add("missing $($entry.Value)") }
}

$forbidden = [ordered]@{
    'HearthstoneDeckTracker\\Plugins' = 'HDT deployment path'
    'AppData' = 'AppData deployment path'
    'D:\\software\\Microsoft Visual Studio' = 'machine-specific Visual Studio path'
    '<Compile Include=' = 'embedded project compile list'
}
foreach ($entry in $forbidden.GetEnumerator()) {
    if ($source -match $entry.Key) { $errors.Add("forbidden $($entry.Value)") }
}

if ($errors.Count -gt 0) {
    Write-Host "FAIL release build script contract"
    foreach ($message in $errors) { Write-Host "- $message" }
    exit 1
}

Write-Host "PASS release build script is deterministic, x64, versioned, and deployment-free"
