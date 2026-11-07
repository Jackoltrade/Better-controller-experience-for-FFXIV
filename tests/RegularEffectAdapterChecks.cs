// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// Exercises the compiled adapter with the pinned C012 layout, not a copy of its guards.
internal static unsafe class RegularEffectAdapterChecks
{
    private const BindingFlags Hidden = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private static object New(Type type, params object?[] args) => Activator.CreateInstance(type, Hidden, null, args, null)!;
    private static void Set(object target, string name, object? value) => target.GetType().GetField(name, Hidden)!.SetValue(target, value);
    private static object Record(Type type, params (string Name, object Value)[] values)
    {
        var ctor = type.GetConstructors(Hidden).OrderByDescending(c => c.GetParameters().Length).First();
        return ctor.Invoke(ctor.GetParameters().Select(p =>
        {
            var named = values.FirstOrDefault(v => string.Equals(v.Name, p.Name, StringComparison.OrdinalIgnoreCase));
            return named.Name != null ? named.Value : p.HasDefaultValue ? p.DefaultValue :
                p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null;
        }).ToArray());
    }

    internal static void Run(Action<bool, string> check, Assembly plugin)
    {
        Type T(string name) => plugin.GetType("DualSenseLighting." + name, true)!;
        var filterType = T("Windows.EnemySkillFilter");
        var observerType = T("Windows.EnemyEffectObserver");
        var ownershipType = T("Core.GameAudioLayout+PlayerSoundOwnership");
        var identityType = T("Core.GameAudioLayout+PlayerIdentity");
        var enemyIdentityType = T("Core.GameAudioLayout+EnemyIdentity");
        var actorType = T("Core.GameAudioLayout+EnemyActor");
        var tagType = T("Core.EnemyActionTag");
        var kindType = T("Core.EnemyActionKind");
        var phaseType = T("Core.EnemyActionPhase");
        var cache = New(T("Core.EnemyEffectOwnership"));
        var observer = RuntimeHelpers.GetUninitializedObject(observerType);
        var ownership = RuntimeHelpers.GetUninitializedObject(ownershipType);
        var filter = RuntimeHelpers.GetUninitializedObject(filterType);
        var caster = New(identityType, (nint)200, 2000u, (nint)20000);
        var equalCaster = New(identityType, (nint)200, 2000u, (nint)20000);
        var actor = Record(actorType, ("Actor", (nint)200), ("EntityId", 2000u), ("DrawObject", (nint)20000),
            ("Kind", Enum.Parse(T("Core.EnemySoundKind"), "Regular")));
        var roster = Array.CreateInstance(enemyIdentityType, 1);
        roster.SetValue(New(enemyIdentityType, actor, caster), 0);
        Set(ownership, "enemies", roster);
        Set(filter, "ownership", ownership); Set(filter, "effects", observer); Set(observer, "effects", cache);
        var module = (byte*)NativeMemory.AllocZeroed(4096);
        try
        {
            var clip = module + 1024; var data = module + 1200; var container = module + 1328;
            var handler = module + 1408; var effect = module + 1664;
            var table = (nint)module; var sceneTable = (nint)(module + 160); var listenerTable = (nint)(module + 192);
            *(nint*)clip = (nint)(module + 256); *(nint*)(module + 256 + 51 * 8) = (nint)(module + 768);
            new byte[] { 0x40, 0x53, 0x56, 0x57, 0x41, 0x54, 0x48, 0x83, 0xec, 0x78, 0x4c, 0x8b, 0x61,
                0x30, 0x33, 0xf6, 0x48, 0x8b, 0xb9, 0x98, 0, 0, 0, 0x48, 0x8b, 0xd9 }.CopyTo(new Span<byte>(module + 768, 26));
            *(uint*)(clip + 0x84) = 9; *(nint*)(clip + 0x48) = (nint)(data + 8);
            *(uint*)data = 0x32313043; *(uint*)(data + 4) = 0x48;
            *(nint*)(clip + 0x98) = (nint)container; *(nint*)(container + 0x28) = (nint)handler;
            *(nint*)handler = table; *(nint*)(module + 13 * 8) = (nint)(module + 128);
            *(ulong*)(module + 128) = 0xC300000080818B48UL; *(nint*)(handler + 0x80) = (nint)effect;
            *(nint*)effect = sceneTable; *(nint*)(effect + 0x90) = listenerTable;
            *(nint*)(effect + 0x2A0) = (nint)0x12340;
            Set(observer, "moduleStart", (nint)module); Set(observer, "moduleEnd", (nint)(module + 4096));
            Set(observer, "searchBase", (nint)module); Set(observer, "sceneTable", sceneTable); Set(observer, "listenerTable", listenerTable);
            object Tag(string kind, string phase) => Record(tagType, ("Kind", Enum.Parse(kindType, kind)),
                ("TimelineId", 3216u), ("Phase", Enum.Parse(phaseType, phase)));
            void Observe(object? who, object tag) => filterType.GetMethod("ObserveEffect")!.Invoke(filter, new[] { (object)(nint)clip, who, tag });
            object Resolve(object? who, nint? requestedTable = null) => filterType.GetMethod("EffectTag")!.Invoke(filter,
                new[] { (object)(nint)(effect + 0x90), (object)(requestedTable ?? listenerTable), who })!;
            bool Released(object tag) => (bool)tagType.GetProperty("IsReleasedEffect")!.GetValue(tag)!;
            var execution = Tag("Skill", "Execution");
            Observe(caster, execution);
            check(Released(Resolve(caster)), "A registered regular enemy lost delayed C012 skill ownership through the actual adapter");
            check(!Released(Resolve(equalCaster)) && !Released(Resolve(null)), "An unregistered/equal-valued caster borrowed regular effect ownership");
            check(!Released(Resolve(caster, sceneTable)), "A different native listener type borrowed regular effect ownership");
            *(nint*)(effect + 0x2A0) = (nint)0x12350;
            check(!Released(Resolve(caster)), "A reused native resource instance retained old regular skill ownership");
            *(nint*)(effect + 0x2A0) = (nint)0x12340;
            foreach (var rejected in new[] { Tag("AutoAttack", "Execution"), Tag("Skill", "Caster"), Activator.CreateInstance(tagType)! })
            {
                Observe(caster, execution); Observe(caster, rejected);
                check(!Released(Resolve(caster)), "A basic, casting or unknown regular effect retained a released-skill tag");
            }
            Observe(equalCaster, execution);
            check(!Released(Resolve(caster)), "An unregistered caster entered the regular effect cache");
            Observe(caster, execution); Set(filter, "disposed", 1);
            check(!Released(Resolve(caster)), "A disposed adapter supplied regular effect ownership");
        }
        finally { NativeMemory.Free(module); }
    }
}
