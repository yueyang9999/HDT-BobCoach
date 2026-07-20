[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$RepositoryRoot
)

$ErrorActionPreference = "Stop"
$root = [System.IO.Path]::GetFullPath($RepositoryRoot)
$utf8 = New-Object System.Text.UTF8Encoding($false)
$files = Get-ChildItem -LiteralPath (Join-Path $root "tests") -Recurse -File |
    Where-Object { $_.Extension -in @(".cs", ".js", ".ps1") }

$replacements = [ordered]@{
    'bob-coach\hdt-plugin\BobCoach' = 'src\BobCoach'
    'bob-coach/hdt-plugin/BobCoach' = 'src/BobCoach'
    'bob-coach\release' = 'tools\release'
    'bob-coach/release' = 'tools/release'
    'hdt-plugin\BobCoach' = 'src\BobCoach'
    'hdt-plugin/BobCoach' = 'src/BobCoach'
    '"hdt-plugin", "BobCoach"' = '"src", "BobCoach"'
    "'hdt-plugin', 'BobCoach'" = "'src', 'BobCoach'"
    'path.join(root, "bob-coach", "package.json")' = 'path.join(root, "package.json")'
    'path.join(root, "bob-coach", "test",' = 'path.join(root, "tests",'
    'path.join(root, "scripts",' = 'path.join(root, "tools", "build",'
    "path.join(root, 'scripts'," = "path.join(root, 'tools', 'build',"
    'Join-Path $root "scripts\' = 'Join-Path $root "tools\build\'
}

$changed = 0
foreach ($file in $files) {
    $content = [System.IO.File]::ReadAllText($file.FullName)
    $updated = $content
    foreach ($entry in $replacements.GetEnumerator()) {
        $updated = $updated.Replace([string]$entry.Key, [string]$entry.Value)
    }
    if ($updated -ne $content) {
        [System.IO.File]::WriteAllText($file.FullName, $updated, $utf8)
        $changed++
    }
}

Write-Host "PASS layout paths rewritten files=$changed"
