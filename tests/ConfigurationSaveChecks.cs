// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Threading;
using System.Threading.Tasks;
using DualSenseLighting.Core;

internal static class ConfigurationSaveChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var calls = 0; var warnings = 0; var fail = true;
        var saves = new ConfigurationSaveScheduler(() => { calls++; if (fail) throw new InvalidOperationException("disk"); }, _ => warnings++);
        saves.MarkDirty(0); saves.TrySaveDue(.49);
        check(calls == 0 && saves.Dirty, "Autosave lost its debounce");
        saves.TrySaveDue(.5); saves.MarkDirty(.6); saves.TrySaveDue(1.1);
        check(calls == 1 && warnings == 1 && saves.Dirty, "Editing bypassed autosave failure backoff");
        saves.TrySaveDue(5.5); saves.TrySaveDue(5.6);
        check(calls == 2 && warnings == 1 && saves.Error == "disk", "Failed autosaves were not bounded and deduplicated");
        fail = false; saves.TrySaveDue(10.5);
        check(calls == 3 && !saves.Dirty && saves.Error == null, "Successful retry did not clear dirty/error state");
        saves.MarkDirty(11); fail = true; saves.TrySaveDue(11.5);
        check(warnings == 2, "A later save failure was not a new diagnostic episode");
        try { saves.SaveNow(); check(false, "Consent save swallowed storage failure"); }
        catch (InvalidOperationException) { check(saves.Dirty, "Explicit save failure cleared dirty state"); }
        fail = false; saves.TrySaveDue(12, force: true);
        check(!saves.Dirty, "Unload did not attempt a pending save before its deadline");
        CheckReentrantEdit(check);
        CheckConcurrentSaves(check);
    }

    private static void CheckReentrantEdit(Action<bool, string> check)
    {
        ConfigurationSaveScheduler? saves = null;
        var calls = 0;
        saves = new ConfigurationSaveScheduler(() => { if (++calls == 1) saves!.MarkDirty(1); }, _ => { });
        saves.MarkDirty(0); saves.TrySaveDue(.5);
        check(calls == 1 && saves.Dirty, "Saving revision N acknowledged a reentrant edit N+1");
        saves.TrySaveDue(1.49);
        check(calls == 1 && saves.Dirty, "Reentrant edit lost its debounce");
        saves.TrySaveDue(1.5);
        check(calls == 2 && !saves.Dirty, "Reentrant edit was never saved as its own revision");
        saves = new ConfigurationSaveScheduler(() => { saves!.MarkDirty(2); throw new InvalidOperationException("disk"); }, _ => { });
        saves.MarkDirty(0); saves.TrySaveDue(.5); saves.TrySaveDue(5.49);
        check(saves.Dirty && saves.Error == "disk", "Failed reentrant save lost pending edits or failure state");
    }

    private static void CheckConcurrentSaves(Action<bool, string> check)
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var secondRequested = new ManualResetEventSlim();
        var calls = 0; var active = 0; var maximum = 0;
        var saves = new ConfigurationSaveScheduler(() =>
        {
            var simultaneous = Interlocked.Increment(ref active);
            maximum = Math.Max(maximum, simultaneous);
            try
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    entered.Set();
                    if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Concurrent-save test release timed out");
                }
            }
            finally { Interlocked.Decrement(ref active); }
        }, _ => { });
        saves.MarkDirty(0);
        var first = Task.Run(saves.SaveNow);
        try
        {
            check(entered.Wait(TimeSpan.FromSeconds(5)), "Concurrent-save fixture did not enter its storage callback");
            // This must return immediately while the explicit UI save owns IO.
            saves.TrySaveDue(1);
            var second = Task.Run(() => { secondRequested.Set(); saves.SaveNow(); });
            check(secondRequested.Wait(TimeSpan.FromSeconds(5)), "Concurrent explicit save was not scheduled");
            release.Set();
            check(Task.WaitAll(new[] { first, second }, TimeSpan.FromSeconds(5)) && calls == 2 && maximum == 1 && !saves.Dirty,
                "Explicit saves overlapped or an update duplicated the active storage operation");
        }
        finally { release.Set(); first.GetAwaiter().GetResult(); }

        entered.Reset(); release.Reset(); secondRequested.Reset();
        saves = new ConfigurationSaveScheduler(() =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Concurrent-edit test release timed out");
        }, _ => { });
        saves.MarkDirty(0);
        first = Task.Run(saves.SaveNow);
        try
        {
            check(entered.Wait(TimeSpan.FromSeconds(5)), "Concurrent-edit fixture did not enter its storage callback");
            var edit = Task.Run(() => { secondRequested.Set(); saves.MarkDirty(1); });
            check(secondRequested.Wait(TimeSpan.FromSeconds(5)), "Concurrent edit was not scheduled");
            release.Set();
            check(Task.WaitAll(new[] { first, edit }, TimeSpan.FromSeconds(5)) && saves.Dirty,
                "An edit concurrent with an explicit save was acknowledged without its own save");
            saves.TrySaveDue(1.5);
            check(!saves.Dirty, "Concurrent edit was not saved after serialization completed");
        }
        finally { release.Set(); first.GetAwaiter().GetResult(); }
    }
}
