[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = "High")]
param(
    [string]$PluginDirectory,
    [switch]$RemoveUserData
)

$ErrorActionPreference = "Stop"

function Resolve-PortableHdtExecutable([string]$Parent) {
    $candidates = @(
        (Join-Path $Parent "Hearthstone Deck Tracker.exe"),
        (Join-Path $Parent "HearthstoneDeckTracker.exe")
    )
    $matches = @($candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })
    if ($matches.Count -eq 0) {
        throw "Portable HDT executable not found; expected one of: $($candidates -join ', ')"
    }
    if ($matches.Count -ne 1) {
        throw "Multiple portable HDT executables found: $($matches -join ', ')"
    }
    return $matches[0]
}

function Resolve-PluginDirectory([string]$RequestedPath) {
    if ([string]::IsNullOrWhiteSpace($env:APPDATA)) { throw "APPDATA is not available" }
    $defaultParent = [IO.Path]::GetFullPath((Join-Path $env:APPDATA "HearthstoneDeckTracker")).TrimEnd('\')
    $defaultPlugins = [IO.Path]::GetFullPath((Join-Path $defaultParent "Plugins")).TrimEnd('\')
    $resolved = if ([string]::IsNullOrWhiteSpace($RequestedPath)) {
        $defaultPlugins
    } else {
        [IO.Path]::GetFullPath($RequestedPath).TrimEnd('\')
    }
    if ([IO.Path]::GetFileName($resolved) -ne "Plugins") {
        throw "PluginDirectory must end with Plugins: $resolved"
    }
    $parent = Split-Path -Parent $resolved
    if (!(Test-Path -LiteralPath $parent -PathType Container)) {
        throw "PluginDirectory parent does not exist: $parent"
    }
    $isDefault = $resolved.Equals($defaultPlugins, [StringComparison]::OrdinalIgnoreCase)
    if (!$isDefault) {
        Resolve-PortableHdtExecutable $parent | Out-Null
    }
    return [pscustomobject]@{
        Path = $resolved
        Parent = $parent
        IsDefault = $isDefault
    }
}

function Assert-HdtStopped($ResolvedPluginDirectory) {
    $processes = @(Get-Process -Name "HearthstoneDeckTracker", "Hearthstone Deck Tracker" -ErrorAction SilentlyContinue)
    if ($processes.Count -eq 0) { return }
    if ($ResolvedPluginDirectory.IsDefault) {
        throw "Close Hearthstone Deck Tracker before uninstalling Bob Coach"
    }
    foreach ($process in $processes) {
        $processPath = $null
        try { $processPath = $process.Path } catch { $processPath = $null }
        if ([string]::IsNullOrWhiteSpace($processPath)) { continue }
        $processParent = [IO.Path]::GetFullPath((Split-Path -Parent $processPath)).TrimEnd('\')
        if ($processParent.Equals($ResolvedPluginDirectory.Parent, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Close the portable Hearthstone Deck Tracker before uninstalling Bob Coach"
        }
    }
}

function Resolve-UserDataPath {
    if ([string]::IsNullOrWhiteSpace($env:APPDATA)) { throw "APPDATA is not available" }
    $appDataRoot = [IO.Path]::GetFullPath($env:APPDATA).TrimEnd('\')
    $userData = [IO.Path]::GetFullPath((Join-Path $appDataRoot "bob-coach")).TrimEnd('\')
    $parent = [IO.Path]::GetFullPath((Split-Path -Parent $userData)).TrimEnd('\')
    if (!$parent.Equals($appDataRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unsafe Bob Coach user data path: $userData"
    }
    if (Test-Path -LiteralPath $userData) {
        $item = Get-Item -LiteralPath $userData -Force
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing reparse-point Bob Coach user data path: $userData"
        }
        if (!$item.PSIsContainer) { throw "Bob Coach user data path is not a directory: $userData" }
    }
    return $userData
}

$resolvedPluginDirectory = Resolve-PluginDirectory $PluginDirectory
Assert-HdtStopped $resolvedPluginDirectory
$userDataPath = if ($RemoveUserData) { Resolve-UserDataPath } else { $null }
$targetDll = Join-Path $resolvedPluginDirectory.Path "BobCoach.dll"

if (Test-Path -LiteralPath $targetDll -PathType Leaf) {
    if ($PSCmdlet.ShouldProcess($targetDll, "Uninstall Bob Coach plugin DLL")) {
        Remove-Item -LiteralPath $targetDll -Force
        Write-Host "PASS removed Bob Coach plugin DLL: $targetDll"
    }
} else {
    Write-Host "PASS Bob Coach plugin DLL is already absent: $targetDll"
}

if ($RemoveUserData) {
    if (Test-Path -LiteralPath $userDataPath -PathType Container) {
        if ($PSCmdlet.ShouldProcess($userDataPath, "Remove Bob Coach user data")) {
            Remove-Item -LiteralPath $userDataPath -Recurse -Force
            Write-Host "PASS removed Bob Coach user data: $userDataPath"
        }
    } else {
        Write-Host "PASS Bob Coach user data is already absent: $userDataPath"
    }
}
