[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Version = '1.0.0',
    [DateTimeOffset]$PublishedAtUtc = [DateTimeOffset]::UtcNow,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\..\artifacts\desktop'),
    [string]$InnoCompilerPath,
    [string]$SignToolCommand
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$output = [IO.Path]::GetFullPath($OutputDirectory)
$publish = Join-Path $output 'publish'
$package = Join-Path $output ('CreatorsForge-Foundry-' + $Version)
$archive = $package + '-win-x64.zip'
$setup = Join-Path $output ('CreatorsForge-Foundry-' + $Version + '-Setup.exe')
$updater = Join-Path $output ('CreatorsForge-Foundry-' + $Version + '-Update.exe')
if (Test-Path -LiteralPath $publish) { Remove-Item -LiteralPath $publish -Recurse -Force }
if (Test-Path -LiteralPath $package) { Remove-Item -LiteralPath $package -Recurse -Force }
if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
if (Test-Path -LiteralPath $setup) { Remove-Item -LiteralPath $setup -Force }
if (Test-Path -LiteralPath $updater) { Remove-Item -LiteralPath $updater -Force }
$project = Join-Path $repository 'src\CreatorsForge.Foundry.App\CreatorsForge.Foundry.App.csproj'
dotnet restore $project -r win-x64 --configfile (Join-Path $repository 'NuGet.config')
if ($LASTEXITCODE -ne 0) { throw 'Desktop restore failed.' }
dotnet publish $project -c $Configuration -r win-x64 --self-contained true -p:Version=$Version -o $publish --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Desktop publish failed.' }
$executable = Join-Path $publish 'CreatorsForge.Foundry.exe'
$productMetadata = [ordered]@{
    schemaVersion = 1
    version = $Version
    publishedAtUtc = $PublishedAtUtc.ToUniversalTime().ToString('O')
    executable = 'CreatorsForge.Foundry.exe'
    executableSha256 = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant()
}
$utf8 = New-Object Text.UTF8Encoding($false)
[IO.File]::WriteAllText((Join-Path $publish 'foundry-product.json'), (($productMetadata | ConvertTo-Json) + [Environment]::NewLine), $utf8)

[IO.Directory]::CreateDirectory((Join-Path $package 'app')) | Out-Null
Copy-Item -Path (Join-Path $publish '*') -Destination (Join-Path $package 'app') -Recurse
Copy-Item -LiteralPath (Join-Path $repository 'docs\privacy-and-offline.md') -Destination (Join-Path $package 'PRIVACY.md')

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

if ([string]::IsNullOrWhiteSpace($InnoCompilerPath)) {
    $compilerCandidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    )
    $InnoCompilerPath = $compilerCandidates |
        Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) } |
        Select-Object -First 1
}
if (-not $InnoCompilerPath -or -not (Test-Path -LiteralPath $InnoCompilerPath -PathType Leaf)) {
    throw 'Inno Setup 6 was not found. Install it with: winget install --id JRSoftware.InnoSetup'
}
$setupScript = Join-Path $PSScriptRoot 'FoundrySetup.iss'
$versionCore = $Version.Split('-', 2)[0].Split('+', 2)[0]
$parsedVersion = [Version]$versionCore
$numericVersion = '{0}.{1}.{2}.0' -f $parsedVersion.Major, $parsedVersion.Minor, $parsedVersion.Build
$setupBaseName = [IO.Path]::GetFileNameWithoutExtension($setup)
$compilerArguments = @(
    '/Qp',
    "/DAppVersion=$Version",
    "/DNumericAppVersion=$numericVersion",
    "/DSourceDir=$publish",
    "/DRepositoryRoot=$repository",
    "/O$output",
    "/F$setupBaseName"
)
if (-not [string]::IsNullOrWhiteSpace($SignToolCommand)) {
    $compilerArguments += '/DSignInstaller=1'
    $compilerArguments += "/Sfoundry=$SignToolCommand"
}
$compilerArguments += $setupScript
& $InnoCompilerPath @compilerArguments
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $setup -PathType Leaf)) { throw 'Native Foundry setup compilation failed.' }
Copy-Item -LiteralPath $setup -Destination $updater

$stream = [IO.File]::OpenRead($updater)
try {
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { $hash = -join ($algorithm.ComputeHash($stream) | ForEach-Object { $_.ToString('x2') }) } finally { $algorithm.Dispose() }
} finally { $stream.Dispose() }
$info = Get-Item -LiteralPath $updater
$manifest = [ordered]@{ schemaVersion = 1; version = $Version; packageUrl = $info.Name; sha256 = $hash; size = $info.Length; publishedAtUtc = $PublishedAtUtc.ToUniversalTime().ToString('O'); releaseNotesUrl = 'https://github.com/FatedsChronicles/CreatorsForge-Foundry/releases/latest' }
[IO.File]::WriteAllText((Join-Path $output 'foundry-update.json'), (($manifest | ConvertTo-Json) + [Environment]::NewLine), $utf8)
Write-Host "Native installer: $setup"
Write-Host "Native updater: $updater"
Write-Host "Desktop package: $archive"
Write-Host "Update manifest: $(Join-Path $output 'foundry-update.json')"
