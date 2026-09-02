param(
    [Parameter(Mandatory = $true)][string]$Path,
    [int]$Offset = 1,
    [int]$Limit = 2000
)
$ErrorActionPreference = "Stop"
if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { Write-Error "file not found: $Path"; exit 1 }
$fi = Get-Item -LiteralPath $Path
$raw = [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
$normalized = $raw.Replace("`r`n", "`n").Replace("`r", "`n")
$lines = $normalized -split "`n", -1
$total = $lines.Count
if ($total -gt 0 -and $lines[$total - 1] -eq "") { $total = $total - 1 }
if ($total -le 1 -and $fi.Length -gt 200000) {
    $charLen = $normalized.Length
    $start = [Math]::Max(0, $Offset - 1)
    $start = [Math]::Min($start, $charLen)
    $end = [Math]::Min($start + $Limit, $charLen)
    $slice = $normalized.Substring($start, $end - $start)
    $count = 0
    foreach ($chunk in ($slice -split "`n")) {
        $count++
        "{0}: {1}" -f ($start + $count), $chunk
        if ($count -ge 200) { break }
    }
    Write-Host ""
    Write-Host "-- total_chars=$charLen shown_chars=$($end - $start) (single-line huge file, char-sliced) --"
} else {
    if ($Offset -lt 1) { $Offset = 1 }
    $end = [Math]::Min($Offset + $Limit - 1, $total)
    for ($i = $Offset; $i -le $end; $i++) { "{0}: {1}" -f $i, $lines[$i - 1] }
    Write-Host ""
    Write-Host "-- total=$total shown=$($end - $Offset + 1) --"
}