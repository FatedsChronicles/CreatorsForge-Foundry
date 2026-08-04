[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Version = '0.15.0-alpha.1',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\..\artifacts\private-alpha')
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression.FileSystem

if ($Version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+-alpha\.[0-9]+$') {
    throw 'Private alpha versions must use the form 0.15.0-alpha.1.'
}

$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$output = [IO.Path]::GetFullPath($OutputDirectory)
$desktopOutput = Join-Path $output 'desktop'
$release = Join-Path $output ('CreatorsForge-Foundry-' + $Version + '-private-alpha')
$releaseArchive = $release + '.zip'

function Copy-SampleSource {
    param([string]$Name)
    $source = Join-Path $repository ('samples\' + $Name)
    $destination = Join-Path $release ('samples\' + $Name)
    [IO.Directory]::CreateDirectory($destination) | Out-Null
    Get-ChildItem -LiteralPath $source -Force |
        Where-Object { $_.Name -notin @('build', 'bin', 'obj') } |
        Copy-Item -Destination $destination -Recurse
}

if (Test-Path -LiteralPath $release) { Remove-Item -LiteralPath $release -Recurse -Force }
if (Test-Path -LiteralPath $releaseArchive) { Remove-Item -LiteralPath $releaseArchive -Force }
& (Join-Path $repository 'eng\desktop\package-desktop.ps1') -Configuration $Configuration -Version $Version -OutputDirectory $desktopOutput
if ($LASTEXITCODE -ne 0) { throw 'Desktop packaging failed.' }

[IO.Directory]::CreateDirectory($release) | Out-Null
[IO.Directory]::CreateDirectory((Join-Path $release 'docs')) | Out-Null
[IO.Directory]::CreateDirectory((Join-Path $release 'samples')) | Out-Null

$desktopArchive = Get-Item -LiteralPath (Join-Path $desktopOutput ('CreatorsForge-Foundry-' + $Version + '-win-x64.zip'))
Copy-Item -LiteralPath $desktopArchive.FullName -Destination $release
Copy-Item -LiteralPath (Join-Path $desktopOutput 'foundry-update.json') -Destination $release
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'verify-private-alpha.ps1') -Destination $release
Copy-Item -LiteralPath (Join-Path $repository 'docs\private-alpha\tester-onboarding.md') -Destination (Join-Path $release 'docs\TESTER-ONBOARDING.md')
Copy-Item -LiteralPath (Join-Path $repository 'docs\private-alpha\update-strategy.md') -Destination (Join-Path $release 'docs\UPDATE-STRATEGY.md')
Copy-Item -LiteralPath (Join-Path $repository 'docs\private-alpha\issue-report-template.md') -Destination (Join-Path $release 'docs\ISSUE-REPORT.md')
Copy-Item -LiteralPath (Join-Path $repository 'docs\private-alpha\crash-recovery.md') -Destination (Join-Path $release 'docs\CRASH-RECOVERY.md')
Copy-Item -LiteralPath (Join-Path $repository 'docs\private-alpha\acceptance-checklist.md') -Destination (Join-Path $release 'docs\ACCEPTANCE-CHECKLIST.md')
Copy-Item -LiteralPath (Join-Path $repository 'docs\privacy-and-offline.md') -Destination (Join-Path $release 'docs\PRIVACY.md')
Copy-Item -LiteralPath (Join-Path $repository 'docs\compatibility\private-alpha-matrix.md') -Destination (Join-Path $release 'docs\COMPATIBILITY.md')
Copy-Item -LiteralPath (Join-Path $repository 'docs\compatibility\private-alpha-matrix.json') -Destination (Join-Path $release 'docs\compatibility-matrix.json')
Copy-Item -LiteralPath (Join-Path $repository 'samples\PrivateAlphaSamples.foundryworkspace') -Destination (Join-Path $release 'samples')
Copy-SampleSource 'StreamerBotCreatorToolkit'
Copy-SampleSource 'ObsConfigurableFilter'

$assets = @()
Get-ChildItem -LiteralPath $release -File -Recurse | Sort-Object FullName | ForEach-Object {
    $relative = $_.FullName.Substring($release.Length + 1).Replace('\', '/')
    if ($relative -ne 'private-alpha-manifest.json' -and $relative -ne 'PRIVATE-ALPHA-SHA256.txt') {
        $assets += [ordered]@{ path = $relative; size = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    }
}
$manifest = [ordered]@{
    schemaVersion = 1
    channel = 'private-alpha'
    version = $Version
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    trustModel = 'invitation-only channel plus out-of-band manifest SHA-256'
    assets = $assets
}
$manifestPath = Join-Path $release 'private-alpha-manifest.json'
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
$manifestHash = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath (Join-Path $release 'PRIVATE-ALPHA-SHA256.txt') -Value ($manifestHash + '  private-alpha-manifest.json') -Encoding ASCII

[IO.Compression.ZipFile]::CreateFromDirectory($release, $releaseArchive, [IO.Compression.CompressionLevel]::Optimal, $false)
Write-Host "Private alpha bundle: $releaseArchive"
Write-Host "Share this manifest SHA-256 separately with invited testers: $manifestHash"
