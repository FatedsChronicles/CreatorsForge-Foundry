[CmdletBinding()]
param(
    [string]$ReleaseDirectory = $PSScriptRoot,
    [string]$ExpectedManifestSha256
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = [IO.Path]::GetFullPath($ReleaseDirectory)
$manifestPath = Join-Path $root 'v1-release-manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw 'v1-release-manifest.json is missing.' }
$actualManifestHash = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($ExpectedManifestSha256 -and $actualManifestHash -ne $ExpectedManifestSha256.ToLowerInvariant()) { throw 'The v1 manifest does not match the separately supplied SHA-256.' }
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1 -or $manifest.channel -notin @('release-candidate', 'stable') -or $manifest.version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(?:-rc\.[0-9]+)?$') { throw 'The v1 release identity is invalid.' }
foreach ($asset in $manifest.assets) {
    if ([IO.Path]::IsPathRooted($asset.path) -or $asset.path -match '(^|/)\.\.(/|$)') { throw "Unsafe asset path: $($asset.path)" }
    $path = [IO.Path]::GetFullPath((Join-Path $root ($asset.path -replace '/', '\')))
    if (-not $path.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "Asset leaves release directory: $($asset.path)" }
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing asset: $($asset.path)" }
    $file = Get-Item -LiteralPath $path
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($file.Length -ne [long]$asset.size -or $hash -ne $asset.sha256) { throw "Modified asset: $($asset.path)" }
}
$desktop = @($manifest.assets | Where-Object { $_.path -match '^CreatorsForge-Foundry-.+-win-x64\.zip$' })
if ($desktop.Count -ne 1) { throw 'Exactly one desktop archive is required.' }
$setup = @($manifest.assets | Where-Object { $_.path -match '^CreatorsForge-Foundry-.+-Setup\.exe$' })
if ($setup.Count -ne 1) { throw 'Exactly one native setup executable is required.' }
$updater = @($manifest.assets | Where-Object { $_.path -match '^CreatorsForge-Foundry-.+-Update\.exe$' })
if ($updater.Count -ne 1) { throw 'Exactly one native updater executable is required.' }
if ($setup[0].size -ne $updater[0].size -or $setup[0].sha256 -ne $updater[0].sha256) { throw 'The setup and updater do not contain the same verified payload.' }
$update = Get-Content -LiteralPath (Join-Path $root 'foundry-update.json') -Raw | ConvertFrom-Json
if ($update.version -ne $manifest.version -or $update.packageUrl -ne [IO.Path]::GetFileName($updater[0].path) -or [long]$update.size -ne [long]$updater[0].size -or $update.sha256 -ne $updater[0].sha256) { throw 'The update manifest does not match the native updater.' }
$inventoryHash = (Get-FileHash -LiteralPath (Join-Path $root 'source-inventory.json') -Algorithm SHA256).Hash.ToLowerInvariant()
if ($inventoryHash -ne $manifest.sourceInventorySha256) { throw 'The source inventory does not match the v1 manifest.' }
Write-Host "Verified Creators Forge Foundry $($manifest.version) $($manifest.channel) bundle."
if ($manifest.releaseBlockers.Count -gt 0) { Write-Warning ('Release blockers: ' + ($manifest.releaseBlockers -join '; ')) }
Write-Host "Manifest SHA-256: $actualManifestHash"

