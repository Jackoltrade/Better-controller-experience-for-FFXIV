// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Collections.Generic;

namespace DualSenseLighting.Core;

// Only used during teardown: attempt every resource, then retain every failure.
internal static class ResourceCleanup
{
    internal static void Run(params Action[] actions)
    {
        List<Exception>? errors = null;
        foreach (var action in actions)
        {
            try { action(); }
            catch (Exception error) { (errors ??= []).Add(error); }
        }
        if (errors != null) throw new AggregateException("Resource cleanup failed.", errors);
    }
}
