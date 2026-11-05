// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.IO;
using System.Threading.Tasks;

internal static class CheckRunnerChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        using var output = new StringWriter();
        var runner = new CheckRunner(output, () => 7);
        var later = false;
        await runner.Run("sync fixture", (Action)(() => throw new InvalidOperationException("sync sentinel")));
        await runner.Run("async fixture", (Func<Task>)(async () => { await Task.Yield(); throw new ArgumentException("async sentinel"); }));
        await runner.Run("independent fixture", () => later = true);
        check(later && runner.Groups == 3 && runner.Failures == 2 && runner.Complete() == 1,
            "Independent fixtures did not complete or a failure returned success");
        check(output.ToString().Contains("sync fixture") && output.ToString().Contains("sync sentinel") &&
            output.ToString().Contains("async fixture") && output.ToString().Contains("async sentinel"),
            "Fixture failure lost its name or exception");
        using var passedOutput = new StringWriter();
        var passed = new CheckRunner(passedOutput, () => 0);
        await passed.Run("passing fixture", () => { });
        passed.Skip("optional metadata fixture", "No plugin path supplied");
        check(passed.Complete() == 0 && passed.Skipped == 1 && passed.Groups == 2 && passedOutput.ToString().Contains("SKIP optional metadata fixture"),
            "Skipped verification was reported as an executed pass or passing fixtures returned failure");
    }
}
