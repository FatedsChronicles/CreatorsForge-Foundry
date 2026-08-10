[CmdletBinding()]
param(
    [string]$ProductVersion = '1.0.0',
    [string]$ObsRoot,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\..\artifacts\final-acceptance'),
    [switch]$CleanMachineAttested,
    [switch]$SkipSolutionBuild
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$output = [IO.Path]::GetFullPath($OutputDirectory)
$work = Join-Path $output 'workspace'
$logs = Join-Path $output 'evidence'
$reportPath = Join-Path $output 'final-acceptance-report.json'
$utf8 = New-Object Text.UTF8Encoding($false)
$checks = @()

if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
[IO.Directory]::CreateDirectory($work) | Out-Null
[IO.Directory]::CreateDirectory($logs) | Out-Null

function Add-Check {
    param([string]$Id, [string]$Outcome, [string]$Details, [string]$Evidence)
    $script:checks += [ordered]@{ id = $Id; outcome = $Outcome; details = $Details; evidence = $Evidence }
}

function Invoke-Logged {
    param([string]$Id, [scriptblock]$Action)
    $logName = ($Id -replace '[^a-zA-Z0-9.-]', '-') + '.log'
    $logPath = Join-Path $logs $logName
    try {
        & $Action *> $logPath
        if ($LASTEXITCODE -ne 0) { throw "Command exited with code $LASTEXITCODE." }
        Add-Check $Id 'passed' 'Completed successfully.' ('evidence/' + $logName)
        return $true
    } catch {
        Add-Content -LiteralPath $logPath -Value $_.Exception.Message
        Add-Check $Id 'failed' $_.Exception.Message ('evidence/' + $logName)
        return $false
    }
}

function Copy-SourceSample {
    param([string]$Name)
    $destination = Join-Path $work $Name
    [IO.Directory]::CreateDirectory($destination) | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $repository ('samples\' + $Name)) -Force |
        Where-Object { $_.Name -notin @('build', 'bin', 'obj') } |
        Copy-Item -Destination $destination -Recurse
}

$originalEpoch = $env:SOURCE_DATE_EPOCH
try {
    $env:SOURCE_DATE_EPOCH = '1785283200'
    if (-not $SkipSolutionBuild) {
        Invoke-Logged 'solution.build-test-smoke' { & (Join-Path $repository 'build.ps1') } | Out-Null
    }

    Copy-SourceSample 'StreamerBotCreatorToolkit'
    Copy-SourceSample 'ObsConfigurableFilter'
    $cli = Join-Path $repository 'src\CreatorsForge.Foundry.Cli\bin\Release\net10.0\CreatorsForge.Foundry.Cli.dll'
    if (-not (Test-Path -LiteralPath $cli)) { throw 'The Release CLI is missing. Run without -SkipSolutionBuild.' }
    $streamer = Join-Path $work 'StreamerBotCreatorToolkit\StreamerBotCreatorToolkit.foundryproj'
    $obs = Join-Path $work 'ObsConfigurableFilter\ObsConfigurableFilter.foundryproj'

    Invoke-Logged 'streamerbot.build-package' { & dotnet $cli build $streamer } | Out-Null
    Invoke-Logged 'streamerbot.tests' { & dotnet $cli test $streamer } | Out-Null
    Invoke-Logged 'streamerbot.matrix' { & dotnet $cli test-matrix $streamer } | Out-Null
    Invoke-Logged 'streamerbot.publish-validate' { & dotnet $cli publish validate $streamer } | Out-Null
    if (Invoke-Logged 'streamerbot.publish-first' { & dotnet $cli publish $streamer }) {
        $archive = Get-ChildItem (Join-Path (Split-Path $streamer) 'build\release') -Filter '*-foundry.zip' | Select-Object -First 1
        $first = (Get-FileHash -LiteralPath $archive.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        if (Invoke-Logged 'streamerbot.publish-second' { & dotnet $cli publish $streamer }) {
            $second = (Get-FileHash -LiteralPath $archive.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            Add-Check 'streamerbot.release-reproducible' $(if ($first -eq $second) { 'passed' } else { 'failed' }) "First $first; second $second." $null
        }
    }

    Invoke-Logged 'obsstudio.build-package' { & dotnet $cli build $obs } | Out-Null
    if ($ObsRoot) {
        $resolvedObs = [IO.Path]::GetFullPath($ObsRoot)
        if (-not (Test-Path -LiteralPath (Join-Path $resolvedObs 'bin\64bit\obs64.exe'))) { throw 'ObsRoot does not contain bin\64bit\obs64.exe.' }
        Invoke-Logged 'obsstudio.abi-lifecycle' { & dotnet $cli test $obs --obs $resolvedObs } | Out-Null
        Invoke-Logged 'obsstudio.matrix' { & dotnet $cli test-matrix $obs --obs $resolvedObs } | Out-Null
        Invoke-Logged 'obsstudio.publish-validate' { & dotnet $cli publish validate $obs } | Out-Null
        if (Invoke-Logged 'obsstudio.publish-first' { & dotnet $cli publish $obs }) {
            $archive = Get-ChildItem (Join-Path (Split-Path $obs) 'build\release') -Filter '*-foundry.zip' | Select-Object -First 1
            $first = (Get-FileHash -LiteralPath $archive.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            if (Invoke-Logged 'obsstudio.publish-second' { & dotnet $cli publish $obs }) {
                $second = (Get-FileHash -LiteralPath $archive.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
                Add-Check 'obsstudio.release-reproducible' $(if ($first -eq $second) { 'passed' } else { 'failed' }) "First $first; second $second." $null
            }
        }
    } else {
        foreach ($id in @('obsstudio.abi-lifecycle', 'obsstudio.matrix', 'obsstudio.publish-validate', 'obsstudio.release-reproducible')) { Add-Check $id 'pending' 'Provide -ObsRoot for the automated OBS runtime gate.' $null }
    }
} finally {
    $env:SOURCE_DATE_EPOCH = $originalEpoch
}

$manualIds = @(
    'product.install-first-run', 'product.update-alpha3-to-v1', 'product.recovery', 'product.uninstall-preserve-data',
    'streamerbot.1.0.4.create-edit-deploy-runtime', 'streamerbot.1.0.4.update-repair-rollback-uninstall',
    'streamerbot.1.0.5-alpha.34.create-edit-deploy-runtime', 'streamerbot.1.0.5-alpha.34.update-repair-rollback-uninstall',
    'streamerbot.1.0.5-beta.1.create-edit-deploy-runtime', 'streamerbot.1.0.5-beta.1.update-repair-rollback-uninstall',
    'streamerbot.1.0.5-beta.6.create-edit-deploy-runtime', 'streamerbot.1.0.5-beta.6.update-repair-rollback-uninstall',
    'streamerbot.1.0.7.create-edit-deploy-runtime', 'streamerbot.1.0.7.update-repair-rollback-uninstall',
    'obsstudio.32.1.2.create-edit-deploy-runtime', 'obsstudio.32.1.2.restart-shutdown-repair-rollback-uninstall',
    'obsstudio.32.2.1.create-edit-deploy-runtime', 'obsstudio.32.2.1.restart-shutdown-repair-rollback-uninstall',
    'release.licence-approved', 'release.source-committed-tagged', 'release.publisher-trust-approved'
)
$manual = @($manualIds | ForEach-Object { [ordered]@{ id = $_; outcome = 'pending'; details = 'Record this check on the clean-machine checklist.'; evidence = $null } })
$automatedFailed = @($checks | Where-Object { $_.outcome -eq 'failed' }).Count -gt 0
$report = [ordered]@{
    schemaVersion = 1
    productVersion = $ProductVersion
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    machine = [ordered]@{ operatingSystem = [Environment]::OSVersion.VersionString; architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString(); cleanMachineAttested = [bool]$CleanMachineAttested }
    outcome = if ($automatedFailed) { 'failed' } else { 'pending-manual' }
    automatedChecks = $checks
    manualChecks = $manual
}
[IO.File]::WriteAllText($reportPath, (($report | ConvertTo-Json -Depth 8) + [Environment]::NewLine), $utf8)
Write-Host "Final acceptance report: $reportPath"
Write-Host "Outcome: $($report.outcome)"
if ($automatedFailed) { exit 1 }

