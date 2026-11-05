# Shared native .NET process launch for Windows PowerShell 5 and PowerShell 7.
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
