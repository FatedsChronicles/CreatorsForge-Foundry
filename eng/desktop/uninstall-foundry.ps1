[CmdletBinding()]
param(
    [string]$InstallDirectory = $PSScriptRoot,
    [switch]$RemoveUserData
)
$ErrorActionPreference = 'Stop'
$target = [IO.Path]::GetFullPath($InstallDirectory)
if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) { throw 'LOCALAPPDATA is unavailable.' }
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs')) + [IO.Path]::DirectorySeparatorChar
if (-not $target.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw "Uninstall directory must be inside $allowedRoot" }
if (-not (Test-Path -LiteralPath (Join-Path $target 'install-receipt.json') -PathType Leaf)) { throw 'The Foundry ownership receipt is missing; no files were removed.' }
if (Get-Process -Name 'CreatorsForge.Foundry' -ErrorAction SilentlyContinue) { throw 'Close Creators Forge Foundry before uninstalling.' }
$shortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Creators Forge\Creators Forge Foundry.lnk'
if (Test-Path -LiteralPath $shortcut) { Remove-Item -LiteralPath $shortcut -Force }
$currentDirectory = [IO.Path]::GetFullPath((Get-Location).Path)
$directorySeparators = [char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
$normalizedTarget = $target.TrimEnd($directorySeparators)
$targetPrefix = $normalizedTarget + [IO.Path]::DirectorySeparatorChar
if ($currentDirectory.Equals(
        $normalizedTarget,
        [StringComparison]::OrdinalIgnoreCase) -or
    $currentDirectory.StartsWith($targetPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    Set-Location -LiteralPath ([IO.Path]::GetTempPath())
}
try {
    Remove-Item -LiteralPath $target -Recurse -Force
}
catch {
    throw "Foundry could not be removed because an installed file is still in use. Close Foundry and any File Explorer or terminal window open inside '$target', then try again. $($_.Exception.Message)"
}
if ($RemoveUserData) {
    $state = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Creators Forge\Foundry'))
    if (Test-Path -LiteralPath $state) { Remove-Item -LiteralPath $state -Recurse -Force }
    Write-Host 'Foundry and its local settings/recovery data were removed.'
} else {
    Write-Host 'Foundry was removed. Local settings and recovery data were preserved.'
}
