param(
    [string]$ChecksPath = (Join-Path $PSScriptRoot '..\artifacts\checks\LightingChecks.dll'),
    [string]$DotnetPath = 'dotnet'
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\tools\Invoke-Dotnet.ps1')
$dotnet = $DotnetPath
foreach ($mode in @('sync', 'async', 'pass')) {
    $lines = New-Object 'Collections.Generic.List[string]'
    $failed = $false
    try { Invoke-Dotnet -Arguments @($ChecksPath, '--runner-self-test', $mode) | ForEach-Object { $lines.Add($_) } }
    catch {
        if ($_.Exception.Message -notmatch 'exit 1\)') {
            throw "$($_.Exception.Message)`nCaptured child output:`n$($lines -join "`n")"
        }
        $failed = $true
    }
    $text = $lines -join "`n"
    if (-not $text.Contains('LATER_GROUP_EXECUTED')) { throw "$mode failure aborted an independent group.`nCaptured child output:`n$text" }
    if ($failed -ne ($mode -ne 'pass')) { throw "$mode returned an incorrect process exit status.`nCaptured child output:`n$text" }
    if ($mode -ne 'pass' -and (-not $text.Contains("$mode sentinel") -or -not $text.Contains("injected $mode failure"))) {
        throw "$mode lost its original failure diagnostic.`nCaptured child output:`n$text"
    }
}
'Passed runner process checks: synchronous/async failures preserve later groups and exit nonzero; all-pass exits zero.'
$lines = New-Object 'Collections.Generic.List[string]'
$failed = $false
try { Invoke-Dotnet -Arguments @($ChecksPath, '--plugin') | ForEach-Object { $lines.Add($_) } }
catch {
    if ($_.Exception.Message -notmatch 'exit 1\)') {
        throw "$($_.Exception.Message)`nCaptured child output:`n$($lines -join "`n")"
    }
    $failed = $true
}
$text = $lines -join "`n"
if (-not $failed -or -not $text.Contains('Provide both --plugin') -or -not $text.Contains('PASS CheckSfxSession') -or
    -not $text.Contains('0 skipped; 1 failed;')) {
    throw "Malformed plugin arguments did not produce a clear failure while completing independent source checks.`nCaptured child output:`n$text"
}
'Passed malformed-argument process check: metadata group fails clearly, later groups complete, exit status is nonzero.'
