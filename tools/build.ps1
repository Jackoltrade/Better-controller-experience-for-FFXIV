[CmdletBinding()]
param(
    [string]$DalamudLibPath,
    [switch]$Probe,
    [string]$DotnetPath,
    [switch]$LockedRestore
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$pluginOutput = Join-Path $projectRoot 'artifacts\build\plugin'
$publishOutput = Join-Path $projectRoot 'artifacts\plugin'
$checksOutput = Join-Path $projectRoot 'artifacts\checks'
foreach ($directory in @($pluginOutput, $checksOutput)) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}

if (-not $DalamudLibPath) { $DalamudLibPath = $env:DALAMUD_HOME }
if (-not $DalamudLibPath) {
    foreach ($launcher in @('XIVLauncherCN', 'XIVLauncher')) {
        $hooks = Join-Path $env:APPDATA "$launcher\addon\Hooks"
        if (-not (Test-Path $hooks)) { continue }
        $candidate = Get-ChildItem $hooks -Directory | Where-Object {
            $_.Name -match '^15\.\d+\.\d+\.\d+$' -and (Test-Path (Join-Path $_.FullName 'Dalamud.dll'))
        } | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
        if ($candidate) { $DalamudLibPath = $candidate.FullName; break }
    }
}
if (-not $DalamudLibPath -or -not (Test-Path (Join-Path $DalamudLibPath 'Dalamud.dll'))) {
    throw 'Set DALAMUD_HOME or pass -DalamudLibPath with an API 15 Dalamud directory.'
}

$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
$dotnet = if ($DotnetPath) { $DotnetPath } elseif ($dotnetCommand) { $dotnetCommand.Source } else { Join-Path $env:ProgramFiles 'dotnet\dotnet.exe' }
if (-not (Test-Path -LiteralPath $dotnet)) { throw '.NET 10 SDK is required.' }
Write-Output "Using .NET host: $dotnet"

function Invoke-Dotnet {
    param([Parameter(Mandatory)][AllowEmptyString()][string[]]$Arguments)
    # Use the process's own exit status. A native launch can leave PowerShell's
    # LASTEXITCODE unchanged; accepting that stale value can publish an old DLL.
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $dotnet
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    if ($start.PSObject.Properties['ArgumentList']) {
        foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    } else {
        # Windows PowerShell 5 runs on .NET Framework, without ArgumentList.
        $start.Arguments = ($Arguments | ForEach-Object {
            $escaped = [regex]::Replace($_, '(\\*)"', '$1$1\"')
            '"' + [regex]::Replace($escaped, '(\\+)$', '$1$1') + '"'
        }) -join ' '
    }
    $process = [Diagnostics.Process]::Start($start)
    try {
        # Drain both pipes concurrently, including when a compiler writes a lot.
        $standardOutput = $process.StandardOutput.ReadToEndAsync()
        $standardError = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $nativeOutput = $standardOutput.GetAwaiter().GetResult()
        $nativeError = $standardError.GetAwaiter().GetResult()
        if ($nativeOutput) { $nativeOutput.TrimEnd("`r", "`n") -split "`r?`n" }
        if ($nativeError) { [Console]::Error.Write($nativeError) }
        if ($process.ExitCode -ne 0) { throw ".NET command failed (exit $($process.ExitCode)): $($Arguments[0])" }
    } finally { $process.Dispose() }
}

$sdks = @(Invoke-Dotnet -Arguments @('--list-sdks'))
if (-not ($sdks | Where-Object { $_ -match '^10\.' })) {
    throw '.NET 10 SDK is required. Install it or pass -DotnetPath pointing to its dotnet.exe.'
}
$project = Join-Path $projectRoot 'src\DualSenseLighting\BetterControllerExperienceForFFXIV.csproj'
$restoreArguments = @('restore', $project, "-p:DalamudLibPath=$DalamudLibPath")
if ($LockedRestore) { $restoreArguments += '--locked-mode' }
Invoke-Dotnet -Arguments $restoreArguments
Invoke-Dotnet -Arguments @('build', $project, '-c', 'Release', '--no-restore', "-p:DalamudLibPath=$DalamudLibPath", '-o', $pluginOutput)
Invoke-Dotnet -Arguments @('build', (Join-Path $projectRoot 'tests\LightingChecks.csproj'), '-c', 'Release', '-o', $checksOutput)

Invoke-Dotnet -Arguments @((Join-Path $checksOutput 'LightingChecks.dll'), '--plugin', (Join-Path $pluginOutput 'DualSenseLighting.dll'), '--dalamud', $DalamudLibPath)
Invoke-Dotnet -Arguments @('run', '--project', (Join-Path $projectRoot 'tools\VerifyAssembly\VerifyAssembly.csproj'), '-c', 'Release', '--', (Join-Path $pluginOutput 'DualSenseLighting.dll'), (Join-Path $projectRoot 'src\DualSenseLighting\DualSenseLighting.json'), $DalamudLibPath)
if ($Probe) {
    Invoke-Dotnet -Arguments @((Join-Path $checksOutput 'LightingChecks.dll'), '--probe')
}
# Stage the exact standard Packager result only after all checks pass. Never
# overwrite its generated manifest with the source/localized metadata.
$standard = Join-Path $pluginOutput 'DualSenseLighting'
if (-not (Test-Path (Join-Path $standard 'latest.zip'))) { throw 'DalamudPackager did not produce a Release package.' }
New-Item -ItemType Directory -Force -Path $publishOutput | Out-Null
foreach ($file in @('DualSenseLighting.dll', 'DualSenseLighting.json', 'build-verification.json', 'LICENSE.txt', 'LICENSE-HidLibrary.txt', 'LICENSE-FFXIVClientStructs.txt', 'THIRD-PARTY-NOTICES.txt')) {
    Copy-Item (Join-Path $pluginOutput $file) $publishOutput -Force
}
Copy-Item $standard $publishOutput -Recurse -Force
$imageOutput = Join-Path $publishOutput 'images'
New-Item -ItemType Directory -Force -Path $imageOutput | Out-Null
Copy-Item (Join-Path $pluginOutput 'images\icon.png') (Join-Path $imageOutput 'icon.png') -Force
Write-Output "Plugin ready: $(Join-Path $publishOutput 'DualSenseLighting.dll')"
