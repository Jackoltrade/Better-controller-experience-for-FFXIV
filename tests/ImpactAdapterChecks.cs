// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;

// Calls the compiled plugin's adapter and trace serializer, so linked Core test
// types cannot accidentally replace the production assembly's identity checks.
internal static class ImpactAdapterChecks
{
    private const BindingFlags Hidden = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private static object New(Type type, params object?[] args) => Activator.CreateInstance(type, Hidden, null, args, null)!;
    private static FieldInfo Field(Type type, string name) => type.GetField(name, Hidden)!;
    private static object Record(Type type, params (string Name, object? Value)[] values)
    {
        var ctor = type.GetConstructors(Hidden).OrderByDescending(c => c.GetParameters().Length).First();
        return ctor.Invoke(ctor.GetParameters().Select(p =>
        {
            var named = values.FirstOrDefault(v => string.Equals(v.Name, p.Name, StringComparison.OrdinalIgnoreCase));
            if (named.Name != null) return named.Value;
            if (p.HasDefaultValue) return p.DefaultValue;
            return p.ParameterType == typeof(string) ? "" : p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null;
        }).ToArray());
    }

    internal static void Run(Action<bool, string> check, Assembly plugin)
    {
        Type T(string name) => plugin.GetType(name, true)!;
        var filterType = T("DualSenseLighting.Windows.EnemySkillFilter");
        var identityType = T("DualSenseLighting.Core.GameAudioLayout+PlayerIdentity");
        var tagType = T("DualSenseLighting.Core.EnemyActionTag");
        var kindType = T("DualSenseLighting.Core.EnemyActionKind");
        var phaseType = T("DualSenseLighting.Core.EnemyActionPhase");
        var bindingType = T("DualSenseLighting.Core.EnemySoundBinding");
        var impactType = T("DualSenseLighting.Core.HapticImpactPosition");
        var impactSourceType = T("DualSenseLighting.Core.HapticImpactSource");
        var definitionType = T("DualSenseLighting.Core.EnemyActionDefinition");
        var timelineType = T("DualSenseLighting.Core.EnemyTimelineDefinition");
        var definitions = Array.CreateInstance(definitionType, 1);
        definitions.SetValue(New(definitionType, 12u, 2u, 101u, 102u, 103u, 2u), 0);
        var catalog = New(T("DualSenseLighting.Core.EnemySkillCatalog"), definitions, Array.CreateInstance(timelineType, 0));
        var filter = RuntimeHelpers.GetUninitializedObject(filterType);
        Field(filterType, "catalog").SetValue(filter, catalog);
        var caster = New(identityType, (nint)0x1230, 42u, (nint)0x4560);
        // Equal record values still represent a different identity object.
        var equalCaster = New(identityType, (nint)0x1230, 42u, (nint)0x4560);
        object Tag(string kind = "Skill", uint action = 12, uint timeline = 101, string phase = "Execution", string binding = "Caster") =>
            New(tagType, Enum.Parse(kindType, kind), action, timeline, Enum.Parse(phaseType, phase), Enum.Parse(bindingType, binding), true);
        var impact = New(impactType, new Vector3(17, 3, -29), 12u, 987u, Enum.Parse(impactSourceType, "AreaTarget"), false);
        var scopeType = filterType.GetNestedType("ActionScope", BindingFlags.NonPublic)!;
        var scopeField = filterType.GetField("currentAction", BindingFlags.Static | BindingFlags.NonPublic)!;
        var original = scopeField.GetValue(null);
        var method = filterType.GetMethod("ImpactForSound")!;
        object? Query(object? actor, object tag, byte origin) => method.Invoke(filter, new[] { actor, tag, (object)origin });
        bool Attached(object? value) => value != null && (bool)impactType.GetProperty("CasterAttached")!.GetValue(value)!;
        try
        {
            scopeField.SetValue(null, Activator.CreateInstance(scopeType));
            check(Query(caster, Tag(), 3) == null, "An off-scope sound inherited stale or catalog-only impact evidence");
            scopeField.SetValue(null, New(scopeType, caster, Tag(), null, impact));
            var execution = Query(caster, Tag(action: 0), 3);
            check(execution != null && Attached(execution) &&
                (Vector3)impactType.GetProperty("Position")!.GetValue(execution)! == new Vector3(17, 3, -29),
                "An exact execution timeline did not retain copied impact coordinates and caster attachment");
            check(Query(equalCaster, Tag(), 3) == null && Query(null, Tag(), 3) == null,
                "An equal-valued or missing caster bypassed reference identity matching");
            check(Query(caster, Tag(action: 99), 3) == null,
                "A conflicting nonzero action ID borrowed impact from a matching execution timeline");
            check(Query(caster, Tag(action: 0, timeline: 103, phase: "Caster"), 3) == null,
                "A casting-only timeline inherited distant execution impact");
            check(Query(caster, Tag(action: 0, timeline: 999), 3) == null,
                "An unrelated timeline borrowed impact based on synchronous timing alone");
            foreach (var origin in new byte[] { 0, 2, 4 })
                check(Query(caster, Tag(), origin) == null, "An unsupported sound origin inherited action impact");
            check(Query(caster, Tag(kind: "AutoAttack"), 3) == null &&
                Query(caster, Tag(kind: "HitReaction"), 3) == null,
                "Auto-attack or hit-reaction audio inherited a skill impact");
            check(Query(caster, Tag(), 1) is { } effect && !Attached(effect),
                "An independently positioned effect was marked caster-attached");
            check(Query(caster, Tag(binding: "Target"), 3) is { } target && !Attached(target),
                "A target-bound execution sound was marked caster-attached");
            check(Query(caster, Tag(timeline: 103, phase: "Caster"), 3) == null,
                "Explicit action evidence overrode a non-execution casting location");
            scopeField.SetValue(null, New(scopeType, caster, Tag(kind: "AutoAttack"), null, impact));
            check(Query(caster, Tag(), 3) == null, "A non-skill action scope leaked impact evidence");
            scopeField.SetValue(null, New(scopeType, caster, Tag(), null,
                New(impactType, new Vector3(float.NaN, 0, 0), 12u, 987u, Enum.Parse(impactSourceType, "AreaTarget"), false)));
            check(Query(caster, Tag(), 3) == null, "Invalid copied impact coordinates escaped the actual adapter");
            scopeField.SetValue(null, New(scopeType, caster, Tag(), null, impact));
            Field(filterType, "disposed").SetValue(filter, 1);
            check(Query(caster, Tag(), 3) == null, "A disposed ownership adapter still supplied impact evidence");
        }
        finally { scopeField.SetValue(null, original); }
        CheckSelfTargetedTimeline(check, plugin);
        CheckTraceSerialization(check, plugin, impact);
    }

    private static unsafe void CheckSelfTargetedTimeline(Action<bool, string> check, Assembly plugin)
    {
        var type = plugin.GetType("DualSenseLighting.Windows.EnemySkillFilter", true)!;
        var filter = RuntimeHelpers.GetUninitializedObject(type);
        var method = type.GetMethod("SelfTargetedTimeline", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var module = (byte*)NativeMemory.AllocZeroed(2048);
        try
        {
            var start = (nint)module;
            var table = (nint*)module;
            var owner = module + 512;
            var count = module + 544;
            var target = module + 576;
            var timeline = module + 1024;
            new byte[] { 0x8B, 0x81, 0x8C, 0x01, 0, 0, 0xC3 }.CopyTo(new Span<byte>(owner, 7));
            new byte[] { 0x8B, 0x81, 0x90, 0x01, 0, 0, 0xC3 }.CopyTo(new Span<byte>(count, 7));
            new byte[] { 0x3B, 0x91, 0x90, 0x01, 0, 0, 0x72, 0x07, 0x8B, 0x81, 0x8C, 0x01, 0, 0, 0xC3,
                0x8B, 0xC2, 0x8B, 0x84, 0x81, 0x94, 0x01, 0, 0, 0xC3 }.CopyTo(new Span<byte>(target, 25));
            table[29] = (nint)owner; table[30] = (nint)count; table[31] = (nint)target;
            Field(type, "moduleStart").SetValue(filter, start);
            Field(type, "moduleEnd").SetValue(filter, start + 2048);
            Field(type, "searchBase").SetValue(filter, start);
            *(int*)(timeline + 0x18C) = 52;
            *(int*)(timeline + 0x190) = 1;
            *(int*)(timeline + 0x194) = 52;
            bool Read(int expected = 52, nint? requestedTable = null, nint? requestedTimeline = null) =>
                (bool)method.Invoke(filter, new object[] { requestedTimeline ?? (nint)timeline, requestedTable ?? start, expected })!;
            check(Read(), "The compiled self-target adapter rejected the validated single self target");
            foreach (var value in new[] { 0, 2 })
            {
                *(int*)(timeline + 0x190) = value;
                check(!Read(), "An empty or multi-target scheduler timeline qualified as confirmed self-targeted");
            }
            *(int*)(timeline + 0x190) = 1;
            *(int*)(timeline + 0x194) = 86;
            check(!Read(), "A different target qualified as self-targeted execution");
            *(int*)(timeline + 0x194) = 52;
            check(!Read(86) && !Read(-1), "The self-target adapter ignored the caller's owner identity");
            foreach (var offset in new[] { 512, 544, 576 })
            {
                var previous = module[offset]; module[offset] ^= 1;
                check(!Read(), "An unfamiliar native owner/count/target getter qualified the self-target adapter");
                module[offset] = previous;
            }
            table[31] = start + 2048 - 24;
            check(!Read(), "A truncated native target getter escaped the original-image bounds check");
            table[31] = start - 1;
            check(!Read(), "An out-of-module native target getter escaped validation");
            table[31] = (nint)target;
            check(!Read(requestedTable: start + 2048 - 255) && !Read(requestedTimeline: 0),
                "An out-of-bounds vtable or missing timeline qualified as self-targeted");
            check(Read(), "Rejected self-target metadata corrupted a later valid read");
            var direct = (Func<nint, nint, int, bool>)method.CreateDelegate(typeof(Func<nint, nint, int, bool>), filter);
            var timelineAddress = (nint)timeline;
            var valid = false;
            Action lookup = () => valid = direct(timelineAddress, start, 52);
            var bytes = AllocationChecks.MeasureAllocations(lookup, warmup: 2000, iterations: 10000);
            check(bytes == 0 && valid, $"The compiled timeline fallback lookup allocated {bytes} bytes or lost validity");
            var timer = Stopwatch.StartNew();
            for (var i = 0; i < 10000; i++) lookup();
            timer.Stop();
            Console.WriteLine($"Compiled self-target lookup: {timer.Elapsed.TotalMicroseconds / 10000:F3} us/call, {bytes} warmed bytes; isolated native-layout fixture, no FPS claim.");
            Field(type, "disposed").SetValue(filter, 1);
            check(!Read(), "A disposed adapter continued to report self-targeted execution");
        }
        finally { NativeMemory.Free(module); }
    }

    private static void CheckTraceSerialization(Action<bool, string> check, Assembly plugin, object impact)
    {
        var type = plugin.GetType("DualSenseLighting.Windows.SfxTrace", true)!;
        var trace = New(type, "impact-adapter-check", DateTimeOffset.UtcNow, 1);
        var actionType = plugin.GetType("DualSenseLighting.Windows.EnemySkillFilter+ActionResult", true)!;
        var action = Record(actionType, ("Seconds", 0.25), ("Caster", "1230"), ("CasterEntityId", 42u),
            ("ActionId", 12u), ("Sequence", 987u), ("AnimationTargetId", 42UL), ("Targets", new ulong[] { 42 }),
            ("AreaTarget", true), ("NativeTargetPosition", new Vector3(7, 8, 9)), ("Impact", impact));
        ((IDictionary)Field(type, "actionResults").GetValue(trace)!).Add(987u, action);
        var impactPointType = type.GetNestedType("ImpactPoint", BindingFlags.NonPublic)!;
        var point = impactPointType.GetMethod("Of", BindingFlags.Static | BindingFlags.Public)!.Invoke(null, new[] { impact });
        var soundType = type.GetNestedType("Sound", BindingFlags.NonPublic)!;
        var sound = Record(soundType, ("Impact", point), ("SelfTargetedExecution", true));
        var sounds = Array.CreateInstance(soundType, 1); sounds.SetValue(sound, 0);
        var frameType = type.GetNestedType("Frame", BindingFlags.NonPublic)!;
        var voicesType = frameType.GetConstructors(Hidden).OrderByDescending(c => c.GetParameters().Length).First().GetParameters().Single(p => p.Name == "Voices").ParameterType;
        var frame = Record(frameType, ("Seconds", 0.25), ("Sounds", sounds),
            ("Voices", Array.CreateInstance(voicesType.GetElementType()!, 0)));
        ((IList)Field(type, "frames").GetValue(trace)!).Add(frame);
        var root = Path.Combine(AppContext.BaseDirectory, "impact-adapter-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, "sfx-trace.json");
            type.GetMethod("Save")!.Invoke(trace, new object[] { file });
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            var savedAction = document.RootElement.GetProperty("ActionResults")[0];
            var savedSound = document.RootElement.GetProperty("Frames")[0].GetProperty("Sounds")[0];
            void Point(JsonElement value, float x, float y, float z, string message) =>
                check(value.GetProperty("X").GetSingle() == x && value.GetProperty("Y").GetSingle() == y &&
                    value.GetProperty("Z").GetSingle() == z && value.EnumerateObject().Count() == 3, message);
            check(savedSound.GetProperty("SelfTargetedExecution").GetBoolean(),
                "Saved sound metadata lost its copied self-targeted execution flag");
            Point(savedAction.GetProperty("NativeTargetPosition"), 7, 8, 9, "Saved native action coordinates were empty or changed");
            Point(savedAction.GetProperty("Impact").GetProperty("Position"), 17, 3, -29,
                "Saved action impact coordinates were empty or changed");
            Point(savedSound.GetProperty("Impact").GetProperty("Position"), 17, 3, -29,
                "Saved sound impact coordinates were empty or changed");
            check(savedAction.GetProperty("Impact").GetProperty("ActionId").GetUInt32() == 12 &&
                savedSound.GetProperty("Impact").GetProperty("Sequence").GetUInt32() == 987,
                "Serialized sound/action impact lost its exact action identity");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
