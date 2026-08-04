[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Version = '1.0.0-rc.1',
    [DateTimeOffset]$PublishedAtUtc = [DateTimeOffset]'2026-07-29T00:00:00Z',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\..\artifacts\v1-release'),
    [switch]$AllowUnsignedStable
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression
if ($Version -notmatch '^1\.0\.0(?:-rc\.[0-9]+)?$') { throw 'Phase 16 accepts 1.0.0-rc.N or 1.0.0.' }
$channel = if ($Version -match '-rc\.') { 'release-candidate' } else { 'stable' }
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$output = [IO.Path]::GetFullPath($OutputDirectory)
$desktopA = Join-Path $output 'desktop-a'
$desktopB = Join-Path $output 'desktop-b'
$release = Join-Path $output ('CreatorsForge-Foundry-' + $Version)
$releaseArchive = $release + '-release.zip'
$utf8 = New-Object Text.UTF8Encoding($false)

if ($channel -eq 'stable' -and -not (Test-Path -LiteralPath (Join-Path $repository 'LICENSE.txt'))) { throw 'Stable v1 packaging requires an approved root LICENSE.txt.' }
$revision = 'uncommitted-source-inventory'
try {
    $gitErrorPreference = $ErrorActionPreference
    $ErrorActionPreference = 'SilentlyContinue'
    $candidateRevision = (& git -C $repository rev-parse HEAD 2>$null)
    if ($LASTEXITCODE -eq 0 -and $candidateRevision) { $revision = $candidateRevision }
} finally { $ErrorActionPreference = $gitErrorPreference }
if ($channel -eq 'stable' -and $revision -eq 'uncommitted-source-inventory') { throw 'Stable v1 packaging requires a committed source revision.' }

foreach ($path in @($release, $releaseArchive, $desktopA, $desktopB)) {
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
}
& (Join-Path $repository 'eng\desktop\package-desktop.ps1') -Configuration $Configuration -Version $Version -PublishedAtUtc $PublishedAtUtc -OutputDirectory $desktopA
if (-not $?) { throw 'First desktop packaging run failed.' }
& (Join-Path $repository 'eng\desktop\package-desktop.ps1') -Configuration $Configuration -Version $Version -PublishedAtUtc $PublishedAtUtc -OutputDirectory $desktopB
if (-not $?) { throw 'Second desktop packaging run failed.' }
$desktopName = 'CreatorsForge-Foundry-' + $Version + '-win-x64.zip'
$firstHash = (Get-FileHash -LiteralPath (Join-Path $desktopA $desktopName) -Algorithm SHA256).Hash.ToLowerInvariant()
$secondHash = (Get-FileHash -LiteralPath (Join-Path $desktopB $desktopName) -Algorithm SHA256).Hash.ToLowerInvariant()
if ($firstHash -ne $secondHash) { throw 'The two clean desktop packages are not byte-identical.' }

[IO.Directory]::CreateDirectory($release) | Out-Null
[IO.Directory]::CreateDirectory((Join-Path $release 'docs')) | Out-Null
[IO.Directory]::CreateDirectory((Join-Path $release 'samples')) | Out-Null
Copy-Item -LiteralPath (Join-Path $desktopA $desktopName) -Destination $release
Copy-Item -LiteralPath (Join-Path $desktopA 'foundry-update.json') -Destination $release
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'verify-v1-release.ps1') -Destination $release
Copy-Item -LiteralPath (Join-Path $repository 'CHANGELOG.md') -Destination $release
Copy-Item -LiteralPath (Join-Path $repository 'THIRD-PARTY-NOTICES.md') -Destination $release
if (Test-Path -LiteralPath (Join-Path $repository 'LICENSE.txt')) { Copy-Item -LiteralPath (Join-Path $repository 'LICENSE.txt') -Destination $release }
Copy-Item -LiteralPath (Join-Path $repository 'docs\release\v1-release.md') -Destination (Join-Path $release 'docs\RELEASE.md')
Copy-Item -LiteralPath (Join-Path $repository 'docs\final-acceptance\acceptance-checklist.md') -Destination (Join-Path $release 'docs\FINAL-ACCEPTANCE.md')
Copy-Item -LiteralPath (Join-Path $repository 'docs\compatibility\v1-matrix.md') -Destination (Join-Path $release 'docs\COMPATIBILITY.md')
Copy-Item -LiteralPath (Join-Path $repository 'docs\compatibility\v1-matrix.json') -Destination (Join-Path $release 'docs\compatibility-matrix.json')
Copy-Item -LiteralPath (Join-Path $repository 'docs\privacy-and-offline.md') -Destination (Join-Path $release 'docs\PRIVACY.md')
Copy-Item -LiteralPath (Join-Path $repository 'samples\PrivateAlphaSamples.foundryworkspace') -Destination (Join-Path $release 'samples\V1Samples.foundryworkspace')
foreach ($name in @('StreamerBotCreatorToolkit', 'ObsConfigurableFilter')) {
    $destination = Join-Path $release ('samples\' + $name)
    [IO.Directory]::CreateDirectory($destination) | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $repository ('samples\' + $name)) -Force | Where-Object { $_.Name -notin @('build', 'bin', 'obj') } | Copy-Item -Destination $destination -Recurse
}

$inventory = @()
$sourceRoots = @('src', 'eng', 'schemas', 'samples', 'docs', 'tests')
foreach ($sourceRoot in $sourceRoots) {
    Get-ChildItem -LiteralPath (Join-Path $repository $sourceRoot) -File -Recurse |
        Where-Object { $_.FullName -notmatch '[\\/](?:bin|obj|build|artifacts)[\\/]' } |
        Sort-Object FullName | ForEach-Object {
            $relative = $_.FullName.Substring($repository.Length + 1).Replace('\', '/')
            $inventory += [ordered]@{ path = $relative; size = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
        }
}
$inventoryPath = Join-Path $release 'source-inventory.json'
[IO.File]::WriteAllText($inventoryPath, (([ordered]@{ schemaVersion = 1; sourceRevision = $revision; files = $inventory } | ConvertTo-Json -Depth 8) + [Environment]::NewLine), $utf8)
$inventoryHash = (Get-FileHash -LiteralPath $inventoryPath -Algorithm SHA256).Hash.ToLowerInvariant()
$publishedExe = Join-Path $desktopA 'publish\CreatorsForge.Foundry.exe'
$signature = (Get-AuthenticodeSignature -LiteralPath $publishedExe).Status.ToString()
$blockers = @()
if (-not (Test-Path -LiteralPath (Join-Path $repository 'LICENSE.txt'))) { $blockers += 'Product licence has not been approved.' }
if ($revision -eq 'uncommitted-source-inventory') { $blockers += 'Source has no committed revision or release tag.' }
if ($signature -ne 'Valid') { $blockers += 'Desktop binaries are not Authenticode signed.' }
if ($channel -eq 'stable' -and $blockers.Count -gt 0 -and -not $AllowUnsignedStable) { throw ('Stable v1 release blockers: ' + ($blockers -join '; ')) }

$assets = @()
Get-ChildItem -LiteralPath $release -File -Recurse | Sort-Object FullName | ForEach-Object {
    $relative = $_.FullName.Substring($release.Length + 1).Replace('\', '/')
    if ($relative -ne 'v1-release-manifest.json' -and $relative -ne 'V1-RELEASE-SHA256.txt') { $assets += [ordered]@{ path = $relative; size = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() } }
}
$manifest = [ordered]@{ schemaVersion = 1; channel = $channel; version = $Version; createdAtUtc = $PublishedAtUtc.ToUniversalTime().ToString('O'); sourceRevision = $revision; sourceInventorySha256 = $inventoryHash; authenticodeStatus = $signature; releaseBlockers = $blockers; assets = $assets }
$manifestPath = Join-Path $release 'v1-release-manifest.json'
[IO.File]::WriteAllText($manifestPath, (($manifest | ConvertTo-Json -Depth 8) + [Environment]::NewLine), $utf8)
$manifestHash = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $release 'V1-RELEASE-SHA256.txt'), ($manifestHash + '  v1-release-manifest.json' + [Environment]::NewLine), [Text.Encoding]::ASCII)

$stream = [IO.File]::Open($releaseArchive, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
try {
    $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        $timestamp = [DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
        Get-ChildItem -LiteralPath $release -File -Recurse | Sort-Object FullName | ForEach-Object {
            $name = $_.FullName.Substring($release.Length + 1).Replace('\', '/')
            $entry = $zip.CreateEntry($name, [IO.Compression.CompressionLevel]::Optimal); $entry.LastWriteTime = $timestamp
            $input = [IO.File]::OpenRead($_.FullName); try { $target = $entry.Open(); try { $input.CopyTo($target) } finally { $target.Dispose() } } finally { $input.Dispose() }
        }
    } finally { $zip.Dispose() }
} finally { $stream.Dispose() }
Write-Host "V1 bundle: $releaseArchive"
Write-Host "Manifest SHA-256: $manifestHash"
Write-Host "Reproduced desktop SHA-256: $firstHash"
if ($blockers.Count -gt 0) { Write-Warning ('Release candidate blockers: ' + ($blockers -join '; ')) }
