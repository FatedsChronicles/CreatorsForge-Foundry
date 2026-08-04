[CmdletBinding()]
param(
    [string]$ReleaseDirectory = $PSScriptRoot,
    [string]$ExpectedManifestSha256
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = [IO.Path]::GetFullPath($ReleaseDirectory)
$manifestPath = Join-Path $root 'private-alpha-manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw 'private-alpha-manifest.json is missing.' }

$actualManifestHash = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($ExpectedManifestSha256 -and $actualManifestHash -ne $ExpectedManifestSha256.ToLowerInvariant()) {
    throw 'The manifest SHA-256 does not match the value supplied through the trusted invitation.'
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1 -or $manifest.channel -ne 'private-alpha' -or $manifest.version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+-alpha\.[0-9]+$') {
    throw 'The private alpha manifest identity is invalid.'
}
foreach ($asset in $manifest.assets) {
    if ([IO.Path]::IsPathRooted($asset.path) -or $asset.path -match '(^|/)\.\.(/|$)') { throw "Unsafe asset path: $($asset.path)" }
    $path = [IO.Path]::GetFullPath((Join-Path $root ($asset.path -replace '/', '\')))
    if (-not $path.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "Asset leaves release directory: $($asset.path)" }
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing asset: $($asset.path)" }
    $file = Get-Item -LiteralPath $path
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($file.Length -ne [long]$asset.size -or $hash -ne $asset.sha256) { throw "Modified asset: $($asset.path)" }
}

$desktopAsset = @($manifest.assets | Where-Object { $_.path -match '^CreatorsForge-Foundry-.+-win-x64\.zip$' })
if ($desktopAsset.Count -ne 1) { throw 'The manifest must contain exactly one desktop archive.' }
$updatePath = Join-Path $root 'foundry-update.json'
$update = Get-Content -LiteralPath $updatePath -Raw | ConvertFrom-Json
if ($update.version -ne $manifest.version -or $update.packageUrl -ne [IO.Path]::GetFileName($desktopAsset[0].path) -or
    [long]$update.size -ne [long]$desktopAsset[0].size -or $update.sha256 -ne $desktopAsset[0].sha256) {
    throw 'The update manifest does not match the verified desktop archive.'
}
Write-Host "Verified Creators Forge Foundry $($manifest.version) private alpha bundle."
Write-Host "Manifest SHA-256: $actualManifestHash"

