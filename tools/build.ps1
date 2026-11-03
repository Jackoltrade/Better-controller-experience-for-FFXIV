[CmdletBinding()]
param(
    [string]$DalamudLibPath,
    [string]$CompilerPath,
    [switch]$PrepareOffline,
    [switch]$Probe
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$pluginOutput = Join-Path $projectRoot 'artifacts\build\plugin'
$publishOutput = Join-Path $projectRoot 'artifacts\plugin'
$checksOutput = Join-Path $projectRoot 'artifacts\checks'
$toolOutput = Join-Path $projectRoot 'artifacts\tools'
foreach ($directory in @($pluginOutput, $checksOutput, $toolOutput)) {
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
$dotnet = if ($dotnetCommand) { $dotnetCommand.Source } else { Join-Path $env:ProgramFiles 'dotnet\dotnet.exe' }
if (-not (Test-Path $dotnet)) { throw '.NET 10 runtime or SDK is required.' }
$sdks = @()
if (-not $PrepareOffline) {
    $global:LASTEXITCODE = 0
    $sdks = @(& $dotnet --list-sdks)
    if ($LASTEXITCODE -ne 0) { throw 'Could not inspect .NET SDKs.' }
}
if ($sdks | Where-Object { $_ -match '^10\.' }) {
    & $dotnet build (Join-Path $projectRoot 'src\DualSenseLighting\PS5DualSenseControllerForFFXIV.csproj') -c Release "-p:DalamudLibPath=$DalamudLibPath" -o $pluginOutput
    if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed.' }
    & $dotnet build (Join-Path $projectRoot 'tests\LightingChecks.csproj') -c Release -o $checksOutput
    if ($LASTEXITCODE -ne 0) { throw 'Checks build failed.' }
} else {
    # Offline fallback for this machine: use Roslyn already installed by another plugin.
    # No compiler or controller dependencies are bundled with the resulting plugin.
    if (-not $CompilerPath) {
        foreach ($launcher in @('XIVLauncherCN', 'XIVLauncher')) {
            $installed = Join-Path $env:APPDATA "$launcher\installedPlugins"
            if (-not (Test-Path $installed)) { continue }
            $compiler = Get-ChildItem $installed -Filter Microsoft.CodeAnalysis.CSharp.dll -Recurse |
                Sort-Object LastWriteTime -Descending | Select-Object -First 1
            if ($compiler) { $CompilerPath = $compiler.DirectoryName; break }
        }
    }
    if (-not $CompilerPath) { throw 'Install .NET 10 SDK, or pass -CompilerPath with Microsoft.CodeAnalysis.CSharp.dll and Microsoft.CodeAnalysis.dll.' }
    $bootstrap = Join-Path $toolOutput 'OfflineBuild.exe'
    if (Test-Path $bootstrap) { Remove-Item -LiteralPath $bootstrap }
    Add-Type -Path (Join-Path $PSScriptRoot 'OfflineBuild.cs') -OutputAssembly $bootstrap -OutputType ConsoleApplication
    $runtimeConfig = '{"runtimeOptions":{"tfm":"net10.0","framework":{"name":"Microsoft.NETCore.App","version":"10.0.0"}}}'
    [IO.File]::WriteAllText((Join-Path $toolOutput 'OfflineBuild.runtimeconfig.json'), $runtimeConfig)
    [IO.File]::WriteAllText((Join-Path $checksOutput 'LightingChecks.runtimeconfig.json'), $runtimeConfig)
    [IO.File]::WriteAllText((Join-Path $toolOutput 'VerifyAssembly.runtimeconfig.json'), $runtimeConfig)
    $pluginSources = @(Get-ChildItem (Join-Path $projectRoot 'src\DualSenseLighting') -Filter *.cs -Recurse |
        Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' } | ForEach-Object FullName)
    $checkSources = @(Get-ChildItem (Join-Path $projectRoot 'src\DualSenseLighting\Core') -Filter *.cs | ForEach-Object FullName)
    $checkSources += @(Get-ChildItem (Join-Path $projectRoot 'src\DualSenseLighting\Ui') -Filter UiText*.cs | ForEach-Object FullName)
    $checkSources += Join-Path $projectRoot 'src\DualSenseLighting\Windows\Native.cs'
    $checkSources += Join-Path $projectRoot 'src\DualSenseLighting\Windows\HidDevices.cs'
    $checkSources += Join-Path $projectRoot 'src\DualSenseLighting\Windows\NativeHapticOutput.cs'
    $checkSources += @(Get-ChildItem (Join-Path $projectRoot 'tests') -Filter *.cs | ForEach-Object FullName)
    if ($PrepareOffline) {
        $plan = [ordered]@{
            Dotnet = $dotnet; Bootstrap = $bootstrap; Dalamud = $DalamudLibPath; Compiler = $CompilerPath
            Plugin = (Join-Path $pluginOutput 'DualSenseLighting.dll'); Checks = (Join-Path $checksOutput 'LightingChecks.dll')
            Verifier = (Join-Path $toolOutput 'VerifyAssembly.dll')
            VerifierSources = @((Join-Path $projectRoot 'tools\VerifyAssembly\Program.cs'))
            PluginSources = $pluginSources; CheckSources = $checkSources
        }
        [IO.File]::WriteAllText((Join-Path $projectRoot 'artifacts\build-plan.json'), ($plan | ConvertTo-Json -Depth 4))
        Write-Output 'Offline build prepared: artifacts\build-plan.json'
        return
    }
    & $dotnet $bootstrap (Join-Path $pluginOutput 'DualSenseLighting.dll') library $DalamudLibPath $CompilerPath @pluginSources
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path (Join-Path $pluginOutput 'DualSenseLighting.dll'))) { throw 'Plugin build failed.' }
    & $dotnet $bootstrap (Join-Path $checksOutput 'LightingChecks.dll') console $DalamudLibPath $CompilerPath @checkSources
    if ($LASTEXITCODE -ne 0) { throw 'Checks build failed.' }
    & $dotnet $bootstrap (Join-Path $toolOutput 'VerifyAssembly.dll') console $DalamudLibPath $CompilerPath (Join-Path $projectRoot 'tools\VerifyAssembly\Program.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Metadata verifier build failed.' }
}

& $dotnet (Join-Path $checksOutput 'LightingChecks.dll')
if ($LASTEXITCODE -ne 0) { throw 'Lighting checks failed.' }
if ($sdks | Where-Object { $_ -match '^10\.' }) {
    & $dotnet run --project (Join-Path $projectRoot 'tools\VerifyAssembly\VerifyAssembly.csproj') -c Release -- (Join-Path $pluginOutput 'DualSenseLighting.dll') (Join-Path $projectRoot 'src\DualSenseLighting\DualSenseLighting.json') $DalamudLibPath
} else {
    & $dotnet (Join-Path $toolOutput 'VerifyAssembly.dll') (Join-Path $pluginOutput 'DualSenseLighting.dll') (Join-Path $projectRoot 'src\DualSenseLighting\DualSenseLighting.json') $DalamudLibPath
}
if ($LASTEXITCODE -ne 0) { throw 'Plugin metadata verification failed.' }
if ($Probe) {
    & $dotnet (Join-Path $checksOutput 'LightingChecks.dll') --probe
    if ($LASTEXITCODE -ne 0) { throw 'Read-only HID probe failed.' }
}
Copy-Item (Join-Path $projectRoot 'src\DualSenseLighting\DualSenseLighting.json') $pluginOutput -Force
foreach ($file in @('LICENSE.txt', 'LICENSE-HidLibrary.txt', 'LICENSE-FFXIVClientStructs.txt', 'THIRD-PARTY-NOTICES.txt')) {
    Copy-Item (Join-Path $projectRoot $file) $pluginOutput -Force
}
New-Item -ItemType Directory -Force -Path $publishOutput | Out-Null
Copy-Item (Join-Path $pluginOutput '*') $publishOutput -Force
Write-Output "Plugin ready: $(Join-Path $publishOutput 'DualSenseLighting.dll')"
