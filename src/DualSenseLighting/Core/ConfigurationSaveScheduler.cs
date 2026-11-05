// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Threading;

namespace DualSenseLighting.Core;

internal sealed class ConfigurationSaveScheduler(Action save, Action<Exception> report)
{
    private readonly object sync = new();
    private double due = double.PositiveInfinity, editDue, retryAfter;
    private long revision;
    private bool saving;
    private string? error;
    public bool Dirty { get { lock (sync) return !double.IsPositiveInfinity(due); } }
    public string? Error => Volatile.Read(ref error);

    public void MarkDirty(double now)
    {
        lock (sync)
        {
            revision++;
            editDue = now + 0.5;
            due = Math.Max(editDue, retryAfter);
        }
    }

    // Explicit consent saves serialize with other saves and propagate failure.
    public void SaveNow()
    {
        lock (sync)
        {
            if (saving) throw new InvalidOperationException("A configuration save is already in progress.");
            SaveCurrentRevision();
        }
    }

    private void SaveCurrentRevision()
    {
        var savedRevision = revision;
        saving = true;
        try
        {
            save();
            // A reentrant edit during serialization still needs another save.
            due = revision == savedRevision ? double.PositiveInfinity : editDue;
            retryAfter = 0;
            Volatile.Write(ref error, null);
        }
        finally { saving = false; }
    }

    public void TrySaveDue(double now, bool force = false)
    {
        // UI can be finishing an explicit save. Never wait for it on an update.
        if (force) Monitor.Enter(sync);
        else if (!Monitor.TryEnter(sync)) return;
        Exception? warning = null;
        try
        {
            if (saving || double.IsPositiveInfinity(due) || !force && now < due) return;
            try { SaveCurrentRevision(); }
            catch (Exception failure)
            {
                retryAfter = now + 5;
                due = Math.Max(editDue, retryAfter);
                if (error == null) warning = failure;
                Volatile.Write(ref error, failure.Message);
            }
        }
        finally { Monitor.Exit(sync); }
        if (warning != null)
            try { report(warning); } catch { /* Diagnostics cannot break the framework callback. */ }
    }
}
