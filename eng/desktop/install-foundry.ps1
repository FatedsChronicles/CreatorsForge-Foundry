[CmdletBinding()]
param(
    [string]$SourceDirectory = (Join-Path $PSScriptRoot 'app'),
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'Programs\Creators Forge Foundry'),
    [switch]$NoShortcut
)
$ErrorActionPreference = 'Stop'
$source = [IO.Path]::GetFullPath($SourceDirectory)
$target = [IO.Path]::GetFullPath($InstallDirectory)
if (-not (Test-Path -LiteralPath (Join-Path $source 'CreatorsForge.Foundry.exe') -PathType Leaf)) { throw 'The package does not contain CreatorsForge.Foundry.exe.' }
$productManifestPath = Join-Path $source 'foundry-product.json'
if (-not (Test-Path -LiteralPath $productManifestPath -PathType Leaf)) { throw 'The package does not contain foundry-product.json.' }
$productManifest = Get-Content -LiteralPath $productManifestPath -Raw | ConvertFrom-Json
$actualExecutableHash = (Get-FileHash -LiteralPath (Join-Path $source 'CreatorsForge.Foundry.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
if ($productManifest.schemaVersion -ne 1 -or -not $productManifest.version -or $productManifest.executableSha256 -ne $actualExecutableHash) { throw 'The product manifest is invalid or the executable has been modified.' }
if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) { throw 'LOCALAPPDATA is unavailable.' }
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs')) + [IO.Path]::DirectorySeparatorChar
if (-not $target.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw "Install directory must be inside $allowedRoot" }
if (Get-Process -Name 'CreatorsForge.Foundry' -ErrorAction SilentlyContinue) { throw 'Close Creators Forge Foundry before installing or updating.' }
$parent = Split-Path -Parent $target
[IO.Directory]::CreateDirectory($parent) | Out-Null
$staging = Join-Path $parent ('.foundry-install-' + [Guid]::NewGuid().ToString('N'))
$backup = $target + '.previous'
$hadExisting = Test-Path -LiteralPath $target
try {
    Copy-Item -LiteralPath $source -Destination $staging -Recurse
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'uninstall-foundry.ps1') -Destination (Join-Path $staging 'uninstall-foundry.ps1')
    if (Test-Path -LiteralPath $backup) { Remove-Item -LiteralPath $backup -Recurse -Force }
    if (Test-Path -LiteralPath $target) { Move-Item -LiteralPath $target -Destination $backup }
    Move-Item -LiteralPath $staging -Destination $target
    $receipt = [ordered]@{ schemaVersion = 1; installedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); installDirectory = $target; executable = 'CreatorsForge.Foundry.exe'; productVersion = $productManifest.version; productManifestSha256 = (Get-FileHash -LiteralPath $productManifestPath -Algorithm SHA256).Hash.ToLowerInvariant(); executableSha256 = $actualExecutableHash }
    $receipt | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $target 'install-receipt.json') -Encoding UTF8
    if (-not $NoShortcut) {
        $menu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Creators Forge'
        [IO.Directory]::CreateDirectory($menu) | Out-Null
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut((Join-Path $menu 'Creators Forge Foundry.lnk'))
        $shortcut.TargetPath = Join-Path $target 'CreatorsForge.Foundry.exe'
        $shortcut.WorkingDirectory = $target
        $shortcut.Save()
    }
    if (Test-Path -LiteralPath $backup) { Remove-Item -LiteralPath $backup -Recurse -Force }
    Write-Host "Creators Forge Foundry installed at $target"
}
catch {
    if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
    if (Test-Path -LiteralPath $backup) {
        if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
        Move-Item -LiteralPath $backup -Destination $target
    } elseif (-not $hadExisting -and (Test-Path -LiteralPath $target)) {
        Remove-Item -LiteralPath $target -Recurse -Force
    }
    throw
}
