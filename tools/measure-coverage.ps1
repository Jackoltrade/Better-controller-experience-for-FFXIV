param(
    [string]$ChecksPath = (Join-Path $PSScriptRoot '..\artifacts\checks\LightingChecks.dll'),
    [string]$PluginPath,
    [string]$DalamudLibPath,
    [string]$DotnetPath = 'dotnet',
    [string]$ToolPath = (Join-Path $PSScriptRoot '..\artifacts\tools\coverage'),
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\artifacts\coverage'),
    [string]$ToolSource
)
$ErrorActionPreference = 'Stop'
$env:DOTNET_COVERAGE_TELEMETRY_OPTOUT = '1'
. (Join-Path $PSScriptRoot 'Invoke-Dotnet.ps1')
$dotnet = $DotnetPath
$version = '18.12.0'
$collector = Join-Path $ToolPath ".store\dotnet-coverage\$version\dotnet-coverage\$version\tools\net8.0\any\dotnet-coverage.dll"
if (-not (Test-Path $collector)) {
    $arguments = @('tool', 'install', 'dotnet-coverage', '--version', $version, '--tool-path', $ToolPath)
    if ($ToolSource) { $arguments += @('--source', $ToolSource) }
    Invoke-Dotnet -Arguments $arguments
}
if (-not (Test-Path $collector)) { throw 'Pinned coverage collector was not installed.' }
New-Item -ItemType Directory -Force -Path $OutputPath | Out-Null
$reportedVersion = (Invoke-Dotnet -Arguments @($collector, '--version')) -join "`n"
if ($reportedVersion -notmatch '^18\.12\.0(\+|$)') { throw "Unexpected collector version: $reportedVersion" }
Write-Output $reportedVersion
Invoke-Dotnet -Arguments @($collector, 'collect', '--help') | Set-Content (Join-Path $OutputPath 'collector-help.txt')
$command = @($collector, 'collect', '--settings', (Join-Path $PSScriptRoot 'coverage.runsettings'),
    '--output', (Join-Path $OutputPath 'coverage.cobertura.xml'), '--output-format', 'cobertura', $DotnetPath, $ChecksPath)
if ($PluginPath -or $DalamudLibPath) {
    if (-not $PluginPath -or -not $DalamudLibPath) { throw 'Provide both PluginPath and DalamudLibPath.' }
    $command += @('--plugin', $PluginPath, '--dalamud', $DalamudLibPath)
}
# Run separately from performance evidence; instrumentation perturbs timing.
Invoke-Dotnet -Arguments $command
if (-not (Test-Path (Join-Path $OutputPath 'coverage.cobertura.xml'))) { throw 'STRUCTURAL_COVERAGE_UNAVAILABLE: collector produced no report.' }
Write-Output 'Coverage XML collected. Summarize with python tools/summarize-coverage.py <report> --output <summary.json>.'
