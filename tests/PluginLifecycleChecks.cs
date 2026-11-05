// SPDX-License-Identifier: GPL-3.0-only
using System;
using DualSenseLighting.Core;

internal static class PluginLifecycleChecks
{
    internal static void Run(Action<bool, string> check)
    {
        for (var failAt = 0; failAt < 4; failAt++)
        {
            var called = 0; var failed = failAt;
            Action Step(int index) => () => { check(called++ == index, "Cleanup reordered resource release"); if (index == failed) throw new InvalidOperationException(index.ToString()); };
            try { ResourceCleanup.Run(Step(0), Step(1), Step(2), Step(3)); check(false, "Cleanup discarded failure"); }
            catch (AggregateException error)
            { check(called == 4 && error.InnerExceptions.Count == 1 && error.InnerException!.Message == failAt.ToString(), "Cleanup skipped resources or lost original failure"); }
        }
        try { ResourceCleanup.Run(() => throw new Exception("first"), () => throw new Exception("second")); }
        catch (AggregateException error) { check(error.InnerExceptions.Count == 2, "Cleanup discarded an additional failure"); }
    }
}
