[CmdletBinding()]
param(
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

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

Push-Location $PSScriptRoot
try {
    Invoke-DotNet @(
        "restore",
        ".\CreatorsForge.Foundry.sln",
        "--configfile",
        ".\NuGet.config"
    )
    Invoke-DotNet @(
        "build",
        ".\CreatorsForge.Foundry.sln",
        "--configuration",
        $Configuration,
        "--no-restore"
    )
    Invoke-DotNet @(
        "test",
        ".\CreatorsForge.Foundry.sln",
        "--configuration",
        $Configuration,
        "--no-build",
        "--no-restore"
    )

    $desktopExecutable = Join-Path `
        $PSScriptRoot `
        "src\CreatorsForge.Foundry.App\bin\$Configuration\net10.0-windows\CreatorsForge.Foundry.exe"
    $sampleProjects = @(
        (Join-Path $PSScriptRoot "samples\HelloFoundry\HelloFoundry.foundryproj"),
        (Join-Path $PSScriptRoot "samples\ObsPassthroughFilter\ObsPassthroughFilter.foundryproj"),
        (Join-Path $PSScriptRoot "samples\FoundrySamples.foundryworkspace"),
        (Join-Path $PSScriptRoot "samples\StreamerBotCreatorToolkit\StreamerBotCreatorToolkit.foundryproj"),
        (Join-Path $PSScriptRoot "samples\ObsConfigurableFilter\ObsConfigurableFilter.foundryproj"),
        (Join-Path $PSScriptRoot "samples\PrivateAlphaSamples.foundryworkspace")
    )
    foreach ($sampleProject in $sampleProjects) {
        $desktopProcess = Start-Process `
            -FilePath $desktopExecutable `
            -ArgumentList @("--smoke-test", "`"$sampleProject`"") `
            -WindowStyle Hidden `
            -Wait `
            -PassThru
        if ($desktopProcess.ExitCode -ne 0) {
            throw "Desktop smoke test failed for $sampleProject with exit code $($desktopProcess.ExitCode)."
        }
    }

    Write-Host "Managed, native, and multi-project workspace desktop smoke tests passed."
}
finally {
    Pop-Location
}
