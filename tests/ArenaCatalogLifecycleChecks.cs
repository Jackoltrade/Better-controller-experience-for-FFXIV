// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

// Exercises the actual compiled arena observer without constructing game hooks,
// reading native memory, or loading game assets. Null data deliberately faults
// requested catalog loads; completion is still observed and drained.
internal static class ArenaCatalogLifecycleChecks
{
    internal static void Run(Action<bool, string> check, Assembly plugin)
    {
        using (var h = new Harness(plugin))
        {
            h.Update(100, false);
            check(h.Task == null, "Inactive arena discovery scheduled metadata IO without an enabled boss/trace request");
            h.Update(101, false);
            check(h.Task == null && h.SoundCount == 0, "Moving through inactive zones loaded or retained an arena catalog");
            var beforeWarm = h.Generation;
            h.Update(101, false, prepare: true);
            check(h.Generation != beforeWarm, "Arming boss feedback did not start pre-combat catalog preparation");
            h.Drain(); h.Update(101, false, prepare: true);
            var generation = h.Generation;
            check(h.Task == null && h.SoundCount == 0, "A completed empty/faulted catalog was not safely consumed");
            for (var i = 0; i < 20; i++) h.Update(101, false, prepare: true);
            check(h.Task == null && h.Generation == generation, "Empty or failed catalogs retried every framework update");
            h.Update(102, false);
            check(h.Task == null && h.SoundCount == 0 && h.Generation != generation,
                "A territory transition did not invalidate prior catalog work without starting unused IO");
            var beforeActive = h.Generation;
            h.Update(102, true);
            check(h.Generation != beforeActive, "Active arena discovery failed to prepare its catalog without a separate prewarm");
            h.Drain(); h.Update(102, true);
            check(h.Task == null && h.SoundCount == 0, "Failed active preparation reached native traversal instead of remaining empty");
        }
        using (var h = new Harness(plugin))
        {
            h.Error = "Unsupported test layout";
            h.Update(100, true, prepare: true);
            check(h.Task == null && h.SoundCount == 0, "Unsupported arena layouts started metadata work");
        }
        using (var h = new Harness(plugin))
        {
            h.Update(100, false);
            using var pending = h.SetPendingCatalog();
            h.Update(101, false);
            check(ReferenceEquals(h.Task, pending.Task) && h.SoundCount == 0,
                "An unrequested zone queued another catalog behind old unfinished work");
            h.Update(101, false, prepare: true);
            check(h.Task != null && !ReferenceEquals(h.Task, pending.Task) && !h.Task.IsCompleted,
                "New catalog loading did not serialize behind the previous Lumina reader");
            pending.Complete(); h.Drain(); h.Update(101, false, prepare: true);
            check(h.SoundCount == 0 && h.Task == null, "A stale previous-zone catalog became the current result");
        }
    }

    private sealed class Harness : IDisposable
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly object arena, empty;
        private readonly Type type, catalog;
        private readonly MethodInfo update;
        internal Harness(Assembly assembly)
        {
            type = assembly.GetType("DualSenseLighting.Windows.ArenaSoundEffects", true)!;
            catalog = type.GetNestedType("Catalog", BindingFlags.NonPublic)!;
            empty = catalog.GetField("Empty", BindingFlags.Static | BindingFlags.Public)!.GetValue(null)!;
            arena = RuntimeHelpers.GetUninitializedObject(type);
            Set("catalog", empty); Set("territory", uint.MaxValue);
            Set("published", Array.CreateInstance(Field("published").FieldType.GetElementType()!, 0));
            Set("instances", Activator.CreateInstance(Field("instances").FieldType)!);
            Set("translationTables", new HashSet<nint>());
            update = type.GetMethod("Update")!;
        }
        private FieldInfo Field(string name) => type.GetField(name, Fields) ?? throw new MissingFieldException(type.FullName, name);
        private void Set(string name, object value) => Field(name).SetValue(arena, value);
        internal Task? Task => (Task?)Field("catalogTask").GetValue(arena);
        internal int Generation => (int)Field("loadGeneration").GetValue(arena)!;
        internal string Error { set => Set("startupError", value); }
        internal int SoundCount
        {
            get
            {
                var value = Field("catalog").GetValue(arena)!;
                var sounds = catalog.GetProperty("Sounds")!.GetValue(value)!;
                return (int)sounds.GetType().GetProperty("Count")!.GetValue(sounds)!;
            }
        }
        internal void Update(uint territory, bool active, bool prepare = false)
        {
            // The first red probe also supports the prior two-argument API;
            // its failure is unwanted IO, not a changed test signature.
            var args = update.GetParameters().Length == 2
                ? new object[] { territory, active } : new object[] { territory, active, prepare };
            update.Invoke(arena, args);
        }
        internal void Drain()
        {
            var task = Task;
            if (task == null) return;
            try { task.Wait(TimeSpan.FromSeconds(5)); }
            catch (AggregateException) { }
            if (!task.IsCompleted) throw new TimeoutException("Arena catalog fixture did not complete");
        }
        internal Pending SetPendingCatalog()
        {
            var completion = Activator.CreateInstance(typeof(TaskCompletionSource<>).MakeGenericType(catalog))!;
            var task = (Task)completion.GetType().GetProperty("Task")!.GetValue(completion)!;
            Set("catalogTask", task);
            return new Pending(task, () => completion.GetType().GetMethod("TrySetResult")!.Invoke(completion, new[] { empty }));
        }
        public void Dispose()
        {
            Drain();
            type.GetMethod("Dispose")!.Invoke(arena, null);
        }
    }
    private sealed record Pending(Task Task, Action Complete) : IDisposable
    { public void Dispose() => Complete(); }
}
