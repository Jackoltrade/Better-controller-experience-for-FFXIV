param([string]$BuildScriptPath = (Join-Path $PSScriptRoot '..\tools\build.ps1'))
$ErrorActionPreference = 'Stop'
$script:checks = 0
function Check([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:checks += 1
}

# Load only the actual helper definition. Running build.ps1 here would publish plugin files.
$tokens = $null; $parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($BuildScriptPath, [ref]$tokens, [ref]$parseErrors)
Check ($parseErrors.Count -eq 0) 'Build script failed to parse'
$functions = @($ast.FindAll({ param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Invoke-Dotnet'
}, $true))
Check ($functions.Count -eq 1) 'Expected the actual Invoke-Dotnet helper'
. ([scriptblock]::Create($functions[0].Extent.Text))

$temporary = Join-Path ([IO.Path]::GetTempPath()) ('ds5 build process ' + [Guid]::NewGuid().ToString('N'))
$previousError = [Console]::Error
$capturedError = New-Object IO.StringWriter
$previousExitVariable = Get-Variable -Name LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue
$hadExitCode = $null -ne $previousExitVariable
$previousExitCode = if ($hadExitCode) { $previousExitVariable.Value } else { $null }
try {
    New-Item -ItemType Directory -Path $temporary | Out-Null
    $probe = Join-Path $temporary 'Argument Probe.exe'
    Add-Type -OutputAssembly $probe -OutputType ConsoleApplication -TypeDefinition @'
using System;
using System.Text;
public static class BuildProcessProbe {
    public static int Main(string[] args) {
        if (args.Length > 0 && args[0] == "--fail") {
            Console.Error.Write("expected failure"); return 37;
        }
        if (args.Length > 0 && args[0] == "--flood") {
            Console.Write(new string('o', 262144));
            Console.Error.Write(new string('e', 262144)); return 0;
        }
        if (args.Length > 0 && args[0] == "--sdks") {
            Console.Write("8.0.101 [fixture]\r\n9.0.100 [fixture]\r\n10.0.403 [fixture]\r\n"); return 0;
        }
        foreach (var arg in args)
            Console.WriteLine("arg:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(arg)));
        return 0;
    }
}
'@
    $dotnet = $probe
    [Console]::SetError($capturedError)
    $unicode = -join ([char[]]@(0x4E2D, 0x6587, 0x20, 0x65E5, 0x672C, 0x8A9E, 0x20, 0xE9))
    $cases = @(
        @{ Arguments = @('') },
        @{ Arguments = @('plain', 'with spaces', "`t", "line`nnext", "line`r`nnext") },
        @{ Arguments = @('C:\path with spaces\', '-p:DalamudLibPath=C:\path with spaces\') },
        @{ Arguments = @('"', '\"', '\\"', 'a"b', 'a\"b', 'trailing\', 'trailing\\') },
        @{ Arguments = @($unicode, '$(Write-Output injected)', '`literal`', '; exit 99', '& literal') }
    )
    $slashes = New-Object 'System.Collections.Generic.List[string]'
    for ($count = 0; $count -le 6; $count++) {
        $backslashes = '\' * $count
        $slashes.Add('a' + $backslashes + '"b')
        $slashes.Add('with space ' + $backslashes)
    }
    $cases += @{ Arguments = $slashes.ToArray() }
    $argumentCount = 0
    $global:LASTEXITCODE = 123
    foreach ($case in $cases) {
        $expected = @($case.Arguments)
        $output = @(Invoke-Dotnet -Arguments $expected)
        Check ($output.Count -eq $expected.Count) 'Native argument count changed'
        for ($index = 0; $index -lt $expected.Count; $index++) {
            Check ($output[$index].StartsWith('arg:')) 'Argument probe did not run'
            $actual = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($output[$index].Substring(4)))
            Check ($actual -ceq $expected[$index]) 'Native argument value changed'
            $argumentCount += 1
        }
    }
    Check ($global:LASTEXITCODE -eq 123) 'Process helper relied on or changed PowerShell LASTEXITCODE'
    $sdks = @(Invoke-Dotnet -Arguments @('--sdks'))
    Check ($sdks.Count -eq 3 -and @($sdks | Where-Object { $_ -match '^10\.' }).Count -eq 1) 'SDK output lost its line boundaries'
    $failed = $false
    try { $null = Invoke-Dotnet -Arguments @('--fail') } catch { $failed = $_.Exception.Message -match 'exit 37' }
    Check $failed 'Native exit 37 did not stop the build'
    Check ($capturedError.ToString().Contains('expected failure')) 'Native error output was lost'
    $errorLength = $capturedError.GetStringBuilder().Length
    $output = @(Invoke-Dotnet -Arguments @('--flood')) -join "`n"
    Check ($output.Length -eq 262144 -and $capturedError.GetStringBuilder().Length - $errorLength -eq 262144) 'Large stdout/stderr was truncated or did not finish'
    $dotnet = Join-Path $temporary 'does-not-exist.exe'
    $failed = $false
    try { $null = Invoke-Dotnet -Arguments @('--sdks') } catch { $failed = $true }
    Check $failed 'A failed process start did not stop the build'

    $parameters = @($ast.ParamBlock.Parameters | ForEach-Object { $_.Name.VariablePath.UserPath })
    Check (($parameters -join ',') -eq 'DalamudLibPath,Probe,DotnetPath,LockedRestore') 'SDK build parameters changed'
    $selector = @($ast.EndBlock.Statements | Where-Object { $_.Extent.Text -match '^\$dotnetCommand\s*=' -or $_.Extent.Text -match '^\$dotnet\s*=' })
    Check ($selector.Count -eq 2) 'Actual host selector was not found'
    foreach ($found in @($true, $false)) {
        & {
            param($Statements, $ProbePath, $HasGlobalHost)
            $fixtureHost = Join-Path $temporary 'global-runtime.exe'
            function Get-Command { param($Name, $ErrorAction) if ($HasGlobalHost) { [pscustomobject]@{ Source = $fixtureHost } } }
            $DotnetPath = $ProbePath
            foreach ($statement in $Statements) { . ([scriptblock]::Create($statement.Extent.Text)) }
            Check ($dotnet -ceq $ProbePath) 'Explicit SDK host lost to automatic discovery'
            $DotnetPath = ''
            foreach ($statement in $Statements) { . ([scriptblock]::Create($statement.Extent.Text)) }
            $expected = if ($HasGlobalHost) { $fixtureHost } else { Join-Path $env:ProgramFiles 'dotnet\dotnet.exe' }
            Check ($dotnet -ceq $expected) 'Automatic host selection changed'
        } $selector $probe $found
    }
    Write-Output "Build process checks passed: $script:checks assertions, $($cases.Count) cases, $argumentCount arguments."
} finally {
    [Console]::SetError($previousError)
    $capturedError.Dispose()
    # GitHub's PowerShell wrapper propagates LASTEXITCODE; do not leak the test sentinel.
    if ($hadExitCode) { $global:LASTEXITCODE = $previousExitCode }
    else { Remove-Variable -Name LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue }
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
}
