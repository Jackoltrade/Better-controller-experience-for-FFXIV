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
if ([bool]$PluginPath -ne [bool]$DalamudLibPath) { throw 'Provide both PluginPath and DalamudLibPath.' }
$ChecksPath = (Resolve-Path -LiteralPath $ChecksPath).Path
if ($PluginPath) { $PluginPath = (Resolve-Path -LiteralPath $PluginPath).Path }
$version = '18.12.0'
$collector = Join-Path $ToolPath ".store\dotnet-coverage\$version\dotnet-coverage\$version\tools\net8.0\any\dotnet-coverage.dll"
if (-not (Test-Path -LiteralPath $collector)) {
    $arguments = @('tool', 'install', 'dotnet-coverage', '--version', $version, '--tool-path', $ToolPath)
    if ($ToolSource) { $arguments += @('--source', $ToolSource) }
    Invoke-Dotnet -Arguments $arguments
}
if (-not (Test-Path -LiteralPath $collector)) { throw 'Pinned coverage collector was not installed.' }
# Static instrumentation may rewrite DLL/PDB files. Never offer the release inputs
# to the collector; retain each owned shadow directory for diagnosis, including failures.
$runPath = Join-Path $OutputPath ('run-' + [guid]::NewGuid().ToString('N'))
$checksDirectory = Join-Path $runPath 'checks'
New-Item -ItemType Directory -Force -Path $checksDirectory | Out-Null
$runPath = (Resolve-Path -LiteralPath $runPath).Path
$checksDirectory = Join-Path $runPath 'checks'
$inputs = @()
function Copy-CoverageInput([string]$Source, [string]$Destination) {
    $hash = (Get-FileHash -LiteralPath $Source -Algorithm SHA256).Hash
    Copy-Item -LiteralPath $Source -Destination $Destination
    $script:inputs += [pscustomobject]@{ OriginalPath = $Source; ShadowPath = $Destination; BeforeSha256 = $hash; AfterSha256 = $null; Unchanged = $null; Error = $null }
}
$failure = $null
$cleanupFailures = New-Object 'System.Collections.Generic.List[Exception]'
Start-Transcript -Path (Join-Path $runPath 'run.log') | Out-Null
try {
    # Runtime dependencies are top-level only: never copy recording archives or subdirectories.
    Get-ChildItem -LiteralPath (Split-Path -Parent $ChecksPath) -File |
        Where-Object { $_.Extension -in @('.dll', '.pdb') -or $_.Name -match '\.(deps|runtimeconfig(\.dev)?)\.json$' } |
        ForEach-Object { Copy-CoverageInput $_.FullName (Join-Path $checksDirectory $_.Name) }
    $shadowChecks = Join-Path $checksDirectory (Split-Path -Leaf $ChecksPath)
    $directories = @($checksDirectory)
    if ($PluginPath) {
        $pluginDirectory = Join-Path $runPath 'plugin'
        New-Item -ItemType Directory -Path $pluginDirectory | Out-Null
        $shadowPlugin = Join-Path $pluginDirectory (Split-Path -Leaf $PluginPath)
        Copy-CoverageInput $PluginPath $shadowPlugin
        $pdb = [IO.Path]::ChangeExtension($PluginPath, '.pdb')
        if (Test-Path -LiteralPath $pdb) { Copy-CoverageInput $pdb (Join-Path $pluginDirectory (Split-Path -Leaf $pdb)) }
        $directories += $pluginDirectory
    }
    [xml]$settings = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'coverage.runsettings') -Raw
    $modulePaths = $settings.Configuration.CodeCoverage.ModulePaths
    $includeDirectories = $settings.CreateElement('IncludeDirectories')
    foreach ($directory in $directories) {
        $element = $settings.CreateElement('Directory')
        $element.InnerText = $directory
        [void]$includeDirectories.AppendChild($element)
    }
    [void]$modulePaths.AppendChild($includeDirectories)
    $settingsPath = Join-Path $runPath 'coverage.runsettings'
    $settings.Save($settingsPath)
    $reportedVersion = (Invoke-Dotnet -Arguments @($collector, '--version')) -join "`n"
    if ($reportedVersion -notmatch '^18\.12\.0(\+|$)') { throw "Unexpected collector version: $reportedVersion" }
    Write-Output $reportedVersion
    Invoke-Dotnet -Arguments @($collector, 'collect', '--help') | Set-Content (Join-Path $runPath 'collector-help.txt')
    $report = Join-Path $runPath 'coverage.cobertura.xml'
    $command = @($collector, 'collect', '--settings', $settingsPath,
        '--output', $report, '--output-format', 'cobertura', $DotnetPath, $shadowChecks)
    if ($PluginPath) { $command += @('--plugin', $shadowPlugin, '--dalamud', $DalamudLibPath) }
    $command | ConvertTo-Json | Set-Content (Join-Path $runPath 'command.json')
    # Run separately from performance evidence; instrumentation perturbs timing.
    Invoke-Dotnet -Arguments $command
    if (-not (Test-Path -LiteralPath $report)) { throw 'STRUCTURAL_COVERAGE_UNAVAILABLE: collector produced no report.' }
    # Reuse the authored-source/populated-plugin guard rather than accepting an empty XML.
    $dotnet = 'python'
    $summaryArguments = @((Join-Path $PSScriptRoot 'summarize-coverage.py'), $report, '--output', (Join-Path $runPath 'summary.json'))
    if ($PluginPath) { $summaryArguments += '--require-plugin' }
    Invoke-Dotnet -Arguments $summaryArguments
    Write-Output "Coverage report and validated summary: $runPath"
}
catch { $failure = $_; $_ | Out-String | Set-Content (Join-Path $runPath 'failure.txt') }
finally {
    try {
        foreach ($inputFile in $inputs) {
            try {
                $inputFile.AfterSha256 = (Get-FileHash -LiteralPath $inputFile.OriginalPath -Algorithm SHA256 -ErrorAction Stop).Hash
                $inputFile.Unchanged = $inputFile.BeforeSha256 -eq $inputFile.AfterSha256
            }
            catch {
                $inputFile.AfterSha256 = $null
                $inputFile.Unchanged = $false
                $inputFile.Error = $_.Exception.Message
            }
        }
        try { ConvertTo-Json -InputObject @($inputs) -Depth 4 | Set-Content (Join-Path $runPath 'input-fingerprints.json') }
        catch { $cleanupFailures.Add($_.Exception) }
    }
    finally {
        try { Stop-Transcript | Out-Null }
        catch { $cleanupFailures.Add($_.Exception) }
    }
}
$changed = @($inputs | Where-Object { -not $_.Unchanged })
if ($changed.Count -gt 0) {
    $cleanupFailures.Add([InvalidOperationException]::new("Coverage original-input integrity verification failed; inspect $runPath : " +
        (($changed | ForEach-Object { $_.OriginalPath + $(if ($_.Error) { ': ' + $_.Error }) }) -join '; ')))
}
if ($cleanupFailures.Count -gt 0) {
    if ($failure) { $cleanupFailures.Insert(0, $failure.Exception) }
    throw [AggregateException]::new('Coverage collection or original-input integrity verification failed.', $cleanupFailures.ToArray())
}
if ($failure) { throw $failure }
