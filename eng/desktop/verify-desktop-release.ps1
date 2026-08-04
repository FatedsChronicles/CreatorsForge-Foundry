[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Version,
    [Parameter(Mandatory)]
    [string]$ReleaseDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($Version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$') {
    throw "Version '$Version' is not valid semantic version text."
}

$release = [IO.Path]::GetFullPath($ReleaseDirectory)
$setup = Join-Path $release "CreatorsForge-Foundry-$Version-Setup.exe"
$updater = Join-Path $release "CreatorsForge-Foundry-$Version-Update.exe"
$manifestPath = Join-Path $release 'foundry-update.json'

foreach ($path in @($setup, $updater, $manifestPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required release asset is missing: $path"
    }
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$expectedUpdaterName = [IO.Path]::GetFileName($updater)
if ($manifest.schemaVersion -ne 1 -or
    $manifest.version -ne $Version -or
    $manifest.packageUrl -ne $expectedUpdaterName -or
    $manifest.size -ne (Get-Item -LiteralPath $updater).Length) {
    throw 'The update manifest identity, package name, or package size is invalid.'
}

$setupHash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
$updaterHash = (Get-FileHash -LiteralPath $updater -Algorithm SHA256).Hash.ToLowerInvariant()
if ($setupHash -ne $updaterHash) {
    throw 'The setup and updater must contain the same verified installer payload.'
}
if ($manifest.sha256 -ne $updaterHash) {
    throw 'The update manifest SHA-256 does not match the updater executable.'
}
if ($manifest.releaseNotesUrl -ne 'https://github.com/FatedsChronicles/CreatorsForge-Foundry/releases/latest') {
    throw 'The update manifest release-notes location is not the official GitHub Releases page.'
}

$product = (Get-Item -LiteralPath $setup).VersionInfo
$numericVersion = [Version]($Version.Split('-', 2)[0].Split('+', 2)[0])
if ($product.ProductName.Trim() -ne 'Creators Forge Foundry' -or
    $product.ProductVersion -notlike "$($numericVersion.Major).$($numericVersion.Minor).$($numericVersion.Build).*" ) {
    throw 'The setup executable Windows product metadata does not match the requested release.'
}

[pscustomobject]@{
    Version = $Version
    Setup = $setup
    Updater = $updater
    Manifest = $manifestPath
    Size = $manifest.size
    Sha256 = $updaterHash
} | Format-List

Write-Host 'Foundry desktop release assets verified.'

