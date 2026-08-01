Set-StrictMode -Version Latest

function ConvertTo-NormalizedPath([string]$Path) {
    $resolved = [IO.Path]::GetFullPath($Path)
    $root = [IO.Path]::GetPathRoot($resolved)
    if ($resolved.Equals($root, [StringComparison]::OrdinalIgnoreCase)) {
        return $root
    }
    return $resolved.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
}

function Test-PathContained([string]$Parent, [string]$Candidate) {
    $resolvedParent = ConvertTo-NormalizedPath $Parent
    $resolvedCandidate = ConvertTo-NormalizedPath $Candidate
    $separator = [IO.Path]::DirectorySeparatorChar
    $prefix = if ($resolvedParent.EndsWith($separator.ToString(), [StringComparison]::Ordinal) -or
            $resolvedParent.EndsWith(
                [IO.Path]::AltDirectorySeparatorChar.ToString(),
                [StringComparison]::Ordinal)) {
        $resolvedParent
    } else {
        $resolvedParent + $separator
    }
    return $resolvedCandidate.Equals($resolvedParent, [StringComparison]::OrdinalIgnoreCase) -or
        $resolvedCandidate.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)
}

function Assert-NoReparsePointPath([string]$Path, [string]$Name) {
    $resolved = [IO.Path]::GetFullPath($Path)
    $pathRoot = [IO.Path]::GetPathRoot($resolved)
    $current = ConvertTo-NormalizedPath $pathRoot
    $relative = $resolved.Substring($pathRoot.Length)
    foreach ($segment in @($relative.Split([IO.Path]::DirectorySeparatorChar) |
            Where-Object { ![string]::IsNullOrWhiteSpace($_) })) {
        $current = Join-Path $current $segment
        try {
            $item = Get-Item -LiteralPath $current -Force -ErrorAction Stop
        } catch [System.Management.Automation.ItemNotFoundException] {
            break
        }
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "$Name contains a reparse point: $current"
        }
    }
}

function Assert-ExternalDataRoot([string]$Path, [string]$RepositoryRoot) {
    if ([string]::IsNullOrWhiteSpace($Path)) { throw "DataRoot is required in normal mode" }
    $resolved = ConvertTo-NormalizedPath $Path
    $repoRoot = ConvertTo-NormalizedPath $RepositoryRoot
    if (Test-PathContained $repoRoot $resolved -or Test-PathContained $resolved $repoRoot) {
        throw "DataRoot must be outside the repository"
    }
    if (Test-PathContained $resolved $repoRoot) {
        throw "DataRoot cannot contain the repository"
    }
    $systemRoot = [IO.Path]::GetPathRoot($env:SystemDrive + [IO.Path]::DirectorySeparatorChar)
    if ([IO.Path]::GetPathRoot($resolved).Equals($systemRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "DataRoot must not be on the system drive"
    }
    if (!(Test-Path -LiteralPath $resolved -PathType Container)) {
        throw "DataRoot does not exist: $resolved"
    }
    Assert-NoReparsePointPath $resolved "DataRoot"
    return $resolved
}

function Assert-ExternalOutputPath([string]$Path, [string]$RepositoryRoot) {
    if ([string]::IsNullOrWhiteSpace($Path)) { throw "OutputDirectory is required" }
    $resolved = ConvertTo-NormalizedPath $Path
    $repoRoot = ConvertTo-NormalizedPath $RepositoryRoot
    if (Test-PathContained $repoRoot $resolved) {
        throw "OutputDirectory must be outside the repository"
    }
    Assert-NoReparsePointPath $repoRoot "RepositoryRoot"
    Assert-NoReparsePointPath $resolved "OutputDirectory"

    $existing = $resolved
    while (!(Test-Path -LiteralPath $existing)) {
        $parent = Split-Path -Parent $existing
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $existing) {
            throw "OutputDirectory has no existing parent: $resolved"
        }
        $existing = $parent
    }
    $physicalRepo = ConvertTo-NormalizedPath `
        (Get-Item -LiteralPath $repoRoot -Force -ErrorAction Stop).FullName
    $physicalParent = ConvertTo-NormalizedPath `
        (Get-Item -LiteralPath $existing -Force -ErrorAction Stop).FullName
    if ((Test-PathContained $physicalRepo $physicalParent) -or
        (Test-PathContained $physicalParent $physicalRepo)) {
        throw "OutputDirectory must be outside the repository"
    }
    return $resolved
}

function Resolve-DataPath(
    [string]$Root,
    [string]$RelativePath,
    [string]$Name,
    [switch]$AllowMissing,
    [ValidateSet("Leaf", "Container", "Any")]
    [string]$ExpectedType = "Leaf") {
    if ([string]::IsNullOrWhiteSpace($RelativePath)) { throw "$Name is required in normal mode" }
    if ([IO.Path]::IsPathRooted($RelativePath)) { throw "$Name must be relative to DataRoot" }
    $resolvedRoot = ConvertTo-NormalizedPath $Root
    $resolved = [IO.Path]::GetFullPath((Join-Path $resolvedRoot $RelativePath))
    if (!(Test-PathContained $resolvedRoot $resolved) -or
        $resolved.Equals($resolvedRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Name escapes DataRoot"
    }
    Assert-NoReparsePointPath $resolvedRoot "DataRoot"
    Assert-NoReparsePointPath $resolved $Name

    $existing = $resolved
    while (!(Test-Path -LiteralPath $existing)) {
        $parent = Split-Path -Parent $existing
        if ([string]::IsNullOrWhiteSpace($parent) -or !(Test-PathContained $resolvedRoot $parent)) {
            throw "$Name escapes DataRoot"
        }
        $existing = $parent
    }
    if (!$AllowMissing -and !$existing.Equals($resolved, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Name does not exist: $resolved"
    }
    if (Test-Path -LiteralPath $resolved) {
        if ($ExpectedType -eq "Leaf" -and !(Test-Path -LiteralPath $resolved -PathType Leaf)) {
            throw "$Name must be a file: $resolved"
        }
        if ($ExpectedType -eq "Container" -and
            !(Test-Path -LiteralPath $resolved -PathType Container)) {
            throw "$Name must be a directory: $resolved"
        }
    }
    return $resolved
}

Export-ModuleMember -Function Assert-ExternalDataRoot, Assert-ExternalOutputPath, Resolve-DataPath
