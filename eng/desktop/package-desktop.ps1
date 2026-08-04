[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Version = '1.0.0',
    [DateTimeOffset]$PublishedAtUtc = [DateTimeOffset]::UtcNow,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\..\artifacts\desktop')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$output = [IO.Path]::GetFullPath($OutputDirectory)
$publish = Join-Path $output 'publish'
$package = Join-Path $output ('CreatorsForge-Foundry-' + $Version)
$archive = $package + '-win-x64.zip'
if (Test-Path -LiteralPath $publish) { Remove-Item -LiteralPath $publish -Recurse -Force }
if (Test-Path -LiteralPath $package) { Remove-Item -LiteralPath $package -Recurse -Force }
if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
$project = Join-Path $repository 'src\CreatorsForge.Foundry.App\CreatorsForge.Foundry.App.csproj'
dotnet restore $project -r win-x64 --configfile (Join-Path $repository 'NuGet.config')
if ($LASTEXITCODE -ne 0) { throw 'Desktop restore failed.' }
dotnet publish $project -c $Configuration -r win-x64 --self-contained true -p:Version=$Version -o $publish --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Desktop publish failed.' }
[IO.Directory]::CreateDirectory((Join-Path $package 'app')) | Out-Null
Copy-Item -Path (Join-Path $publish '*') -Destination (Join-Path $package 'app') -Recurse
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'install-foundry.ps1') -Destination $package
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'uninstall-foundry.ps1') -Destination $package
Copy-Item -LiteralPath (Join-Path $repository 'docs\privacy-and-offline.md') -Destination (Join-Path $package 'PRIVACY.md')
$executable = Join-Path $package 'app\CreatorsForge.Foundry.exe'
$productMetadata = [ordered]@{
    schemaVersion = 1
    version = $Version
    publishedAtUtc = $PublishedAtUtc.ToUniversalTime().ToString('O')
    executable = 'CreatorsForge.Foundry.exe'
    executableSha256 = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant()
}
$utf8 = New-Object Text.UTF8Encoding($false)
[IO.File]::WriteAllText((Join-Path $package 'app\foundry-product.json'), (($productMetadata | ConvertTo-Json) + [Environment]::NewLine), $utf8)

$archiveStream = [IO.File]::Open($archive, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
try {
    $zip = [IO.Compression.ZipArchive]::new($archiveStream, [IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        $fixedTimestamp = [DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
        Get-ChildItem -LiteralPath $package -File -Recurse | Sort-Object FullName | ForEach-Object {
            $entryName = $_.FullName.Substring($package.Length + 1).Replace('\', '/')
            $entry = $zip.CreateEntry($entryName, [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = $fixedTimestamp
            $input = [IO.File]::OpenRead($_.FullName)
            try { $destination = $entry.Open(); try { $input.CopyTo($destination) } finally { $destination.Dispose() } } finally { $input.Dispose() }
        }
    } finally { $zip.Dispose() }
} finally { $archiveStream.Dispose() }
$stream = [IO.File]::OpenRead($archive)
try {
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { $hash = -join ($algorithm.ComputeHash($stream) | ForEach-Object { $_.ToString('x2') }) } finally { $algorithm.Dispose() }
} finally { $stream.Dispose() }
$info = Get-Item -LiteralPath $archive
$manifest = [ordered]@{ schemaVersion = 1; version = $Version; packageUrl = $info.Name; sha256 = $hash; size = $info.Length; publishedAtUtc = $PublishedAtUtc.ToUniversalTime().ToString('O'); releaseNotesUrl = $null }
[IO.File]::WriteAllText((Join-Path $output 'foundry-update.json'), (($manifest | ConvertTo-Json) + [Environment]::NewLine), $utf8)
Write-Host "Desktop package: $archive"
Write-Host "Update manifest: $(Join-Path $output 'foundry-update.json')"
