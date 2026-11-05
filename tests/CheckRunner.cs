// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.IO;
using System.Threading.Tasks;

/// <summary>Fail fast within a fixture; continue independent fixtures sequentially.</summary>
internal sealed class CheckRunner(TextWriter output, Func<int> assertionCount)
{
    internal int Groups { get; private set; }
    internal int Failures { get; private set; }
    internal int Skipped { get; private set; }

    internal void Skip(string name, string reason)
    {
        Groups++; Skipped++;
        output.WriteLine($"SKIP {name}: {reason}");
    }

    internal Task Run(string name, Action action) => Run(name, () => { action(); return Task.CompletedTask; });

    internal async Task Run(string name, Func<Task> action)
    {
        Groups++;
        var before = assertionCount();
        try
        {
            await action();
            output.WriteLine($"PASS {name} ({assertionCount() - before:N0} assertions)");
        }
        catch (Exception error)
        {
            Failures++;
            output.WriteLine($"FAIL {name} ({assertionCount() - before:N0} assertions before failure): {error}");
        }
    }

    internal int Complete()
    {
        output.WriteLine($"{Groups - Skipped - Failures}/{Groups - Skipped} executed check groups passed; {Skipped} skipped; {Failures} failed; {assertionCount():N0} assertions passed. Repeated assertions are not independent test cases.");
        return Failures == 0 ? 0 : 1;
    }
}
