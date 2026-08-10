[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Container })]
    [string] $StablePath,

    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Container })]
    [string] $AlphaPath,

    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Container })]
    [string] $BetaPath,

    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Container })]
    [string] $Beta6Path,

    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Container })]
    [string] $Stable107Path,

    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Invoke-DotNet {
    param(
        [Parameter(Mandatory)]
        [string[]] $Arguments
    )

    & dotnet @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Assert-StreamerBotHost {
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $ExpectedProductVersion
    )

    foreach ($requiredFile in @(
        "Streamer.bot.exe",
        "Streamer.bot.exe.config",
        "Streamer.bot.Plugin.Interface.dll",
        "Streamer.bot.Common.dll",
        "Twitch.Common.dll"
    )) {
        $candidate = Join-Path $Path $requiredFile
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            throw "Required Streamer.bot file was not found: $candidate"
        }
    }

    $actualVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo(
        (Join-Path $Path "Streamer.bot.exe")
    ).ProductVersion
    if (-not [string]::Equals(
        $actualVersion,
        $ExpectedProductVersion,
        [StringComparison]::OrdinalIgnoreCase
    )) {
        throw "Expected Streamer.bot $ExpectedProductVersion at '$Path', but found '$actualVersion'."
    }
}

$repositoryRoot = Split-Path $PSScriptRoot -Parent
$bridgeProject = Join-Path $repositoryRoot (
    "experiments\StreamerBotCompatibility\" +
    "CreatorsForge.Foundry.StreamerBot.CPHInlineBridge\" +
    "CreatorsForge.Foundry.StreamerBot.CPHInlineBridge.csproj"
)
$bridgeSource = Join-Path (Split-Path $bridgeProject -Parent) "CPHInline.cs"
$nugetConfig = Join-Path $repositoryRoot "NuGet.config"
$outputRoot = Join-Path $repositoryRoot "artifacts\streamerbot-compatibility"
$bridgeBuildOutput = Join-Path (Split-Path $bridgeProject -Parent) (
    "bin\$Configuration\net481"
)

$profiles = @(
    [pscustomobject]@{ Name = "stable-1.0.4"; Path = $StablePath; ExpectedVersion = "1.0.4" },
    [pscustomobject]@{ Name = "alpha-1.0.5-alpha.34"; Path = $AlphaPath; ExpectedVersion = "1.0.5-alpha.34" },
    [pscustomobject]@{ Name = "beta-1.0.5-beta.1"; Path = $BetaPath; ExpectedVersion = "1.0.5-beta.1" }
)
if (-not [string]::IsNullOrWhiteSpace($Beta6Path)) {
    $profiles += [pscustomobject]@{
        Name = "beta-1.0.5-beta.6"
        Path = $Beta6Path
        ExpectedVersion = "1.0.5-beta.6"
    }
}
if (-not [string]::IsNullOrWhiteSpace($Stable107Path)) {
    $profiles += [pscustomobject]@{
        Name = "stable-1.0.7"
        Path = $Stable107Path
        ExpectedVersion = "1.0.7"
    }
}

Push-Location $repositoryRoot
try {
    foreach ($profile in $profiles) {
        Assert-StreamerBotHost $profile.Path $profile.ExpectedVersion
    }

    Invoke-DotNet @(
        "restore",
        $bridgeProject,
        "--configfile",
        $nugetConfig,
        "-p:StreamerBotPath=$($profiles[0].Path)"
    )

    $report = foreach ($profile in $profiles) {
        Invoke-DotNet @(
            "build",
            $bridgeProject,
            "--configuration",
            $Configuration,
            "--no-restore",
            "-p:StreamerBotPath=$($profile.Path)"
        )

        $profileOutput = Join-Path $outputRoot $profile.Name
        New-Item -ItemType Directory -Force -Path $profileOutput | Out-Null

        foreach ($artifactName in @(
            "CreatorsForge.Foundry.StreamerBot.CompatibilityProbe.dll",
            "CreatorsForge.Foundry.StreamerBot.DependencyProbe.dll",
            "CreatorsForge.Foundry.StreamerBot.CPHInlineBridge.dll"
        )) {
            Copy-Item `
                -LiteralPath (Join-Path $bridgeBuildOutput $artifactName) `
                -Destination (Join-Path $profileOutput $artifactName) `
                -Force
        }

        Copy-Item `
            -LiteralPath $bridgeSource `
            -Destination (Join-Path $profileOutput "CPHInline.cs") `
            -Force

        $executablePath = Join-Path $profile.Path "Streamer.bot.exe"
        $pluginInterfacePath = Join-Path $profile.Path "Streamer.bot.Plugin.Interface.dll"
        $roslynPath = Join-Path $profile.Path "Microsoft.CodeAnalysis.CSharp.dll"
        $configText = Get-Content -Raw -LiteralPath ($executablePath + ".config")
        $configuredFramework = [regex]::Match(
            $configText,
            'sku="([^"]+)"'
        ).Groups[1].Value
        $executableVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo(
            $executablePath
        ).ProductVersion
        $roslynVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo(
            $roslynPath
        ).FileVersion

        [pscustomobject]@{
            profile = $profile.Name
            productVersion = $executableVersion
            configuredFramework = $configuredFramework
            roslynFileVersion = $roslynVersion
            pluginInterfaceSha256 = (
                Get-FileHash -Algorithm SHA256 -LiteralPath $pluginInterfacePath
            ).Hash
            compatibilityProbeSha256 = (
                Get-FileHash `
                    -Algorithm SHA256 `
                    -LiteralPath (
                        Join-Path $profileOutput (
                            "CreatorsForge.Foundry.StreamerBot." +
                            "CompatibilityProbe.dll"
                        )
                    )
            ).Hash
            dependencyProbeSha256 = (
                Get-FileHash `
                    -Algorithm SHA256 `
                    -LiteralPath (
                        Join-Path $profileOutput (
                            "CreatorsForge.Foundry.StreamerBot." +
                            "DependencyProbe.dll"
                        )
                    )
            ).Hash
            outputDirectory = $profileOutput
        }
    }

    New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
    $reportPath = Join-Path $outputRoot "compatibility-report.json"
    $report |
        ConvertTo-Json -Depth 4 |
        Set-Content -LiteralPath $reportPath -Encoding UTF8

    Write-Output "Streamer.bot compatibility bridge compiled for $($profiles.Count) exact-version profiles."
    Write-Output "Report: $reportPath"
}
finally {
    Pop-Location
}
