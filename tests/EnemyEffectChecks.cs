// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using DualSenseLighting.Core;

internal static unsafe class EnemyEffectChecks
{
    internal static void Run(Action<bool, string> check)
    {
        // Captured 兽魂的苦痛: an execution timeline with C012 effects and no
        // C063 sound clip. Its later AVFX must keep that exact skill association.
        var catalog = new EnemySkillCatalog(new[] {
            new EnemyActionDefinition(21000, 4, 3216, 5996, 130),
            new EnemyActionDefinition(7, 1, 66, 1875, 0) }, new[] {
            new EnemyTimelineDefinition(3216, "mon_sp/[SKL_ID]/mon_sp016"),
            new EnemyTimelineDefinition(5996, "hit/pain"), new EnemyTimelineDefinition(130, "cast/monster"),
            new EnemyTimelineDefinition(66, "normal/attack"), new EnemyTimelineDefinition(1875, "normal_hit/normal_hit") });
        var caster = new GameAudioLayout.PlayerIdentity(200, 2000, 20000);
        caster.ObserveAction(21000); caster.ObserveAction(7);
        var execution = catalog.TimelineKey("mon_sp/m0109/mon_sp016"u8, caster);
        var effect = EnemySkillCatalog.TimelineEffect(execution);
        var boss = new GameAudioLayout.EnemyActor(200, 2000, 20000, 20, EnemySoundKind.Boss);
        check(execution.IsSkill && execution.Phase == EnemyActionPhase.Execution &&
            effect.IsReleasedEffect && effect.TimelineId == 3216 &&
            catalog.ClassifySound(boss, caster, 1, default, effect).IsReleasedEffect,
            "The captured C012-only boss skill lost its delayed released sound");
        check(!EnemySkillCatalog.TimelineSound(execution, EnemySoundBinding.Caster).IsReleasedEffect &&
            !catalog.ClassifySound(boss, caster, 2, default, effect).IsReleasedEffect &&
            !catalog.ClassifySound(boss, caster, 1, catalog.Action(7, 1), effect).IsReleasedEffect,
            "AVFX attribution admitted caster/body sounds or a confirmed basic attack");
        foreach (var rejected in new[] { default(EnemyActionTag), catalog.Action(7, 1),
            catalog.TimelineKey("normal_hit/normal_hit"u8), catalog.TimelineKey("cast/monster"u8),
            new EnemyActionTag(EnemyActionKind.Skill, TimelineId: 3216, Phase: EnemyActionPhase.Unknown) })
            check(!EnemySkillCatalog.TimelineEffect(rejected).IsReleasedEffect,
                "An unconfirmed, shared, casting or basic-attack effect was promoted");

        var cache = new EnemyEffectOwnership();
        cache.Observe(100, 1000, caster, execution);
        check(cache.Resolve(100, 1000, caster) == effect, "Delayed effect lookup lost its timeline tag");
        check(!cache.Resolve(100, 2000, caster).IsSkill && !cache.Resolve(200, 1000, caster).IsSkill &&
            !cache.Resolve(100, 1000, new GameAudioLayout.PlayerIdentity(200, 2000, 20000)).IsSkill &&
            !cache.Resolve(100, 1000, null).IsSkill,
            "A reused resource, different effect or replaced actor inherited a previous skill");
        cache.Forget(101);
        check(cache.Resolve(100, 1000, caster) == effect, "Unrelated effect creation cleared an active skill");
        cache.Forget(100);
        check(!cache.Resolve(100, 1000, caster).IsSkill, "Native object reconstruction retained stale skill evidence");
        foreach (var rejected in new[] { default(EnemyActionTag), catalog.Action(7, 1), catalog.TimelineKey("cast/monster"u8) })
        {
            cache.Observe(100, 1000, caster, execution); cache.Observe(100, 1000, caster, rejected);
            check(!cache.Resolve(100, 1000, caster).IsSkill, "An unknown/basic/cast reuse retained an earlier skill");
        }
        cache.Observe(100, 1000, caster, execution); cache.Observe(100, 0, caster, execution);
        check(!cache.Resolve(100, 1000, caster).IsSkill, "An unloaded effect resource retained a prior association");
        cache.Observe(100, 1000, caster, execution); cache.Observe(100, 1000, null, execution);
        check(!cache.Resolve(100, 1000, caster).IsSkill, "A missing caster retained an earlier skill association");
        cache.Observe(100, 1000, caster, execution);
        const int collision = 100 + 4096 * 16;
        cache.Observe(collision, 2000, caster, execution);
        check(!cache.Resolve(100, 1000, caster).IsSkill && cache.Resolve(collision, 2000, caster) == effect,
            "A bounded-cache collision returned another effect's skill");
        cache.Forget(100);
        check(cache.Resolve(collision, 2000, caster) == effect, "An old colliding address cleared a newer effect");
        cache.Observe(0, 1000, caster, execution);
        check(!cache.Resolve(0, 1000, caster).IsSkill, "A null effect inherited skill ownership");
        Action observeAndResolve = () =>
        {
            cache.Observe(collision, 2000, caster, execution);
            cache.Resolve(collision, 2000, caster);
        };
        var allocated = AllocationChecks.MeasureAllocations(observeAndResolve, warmup: 2000, iterations: 10000);
        check(allocated == 0, $"Repeated effect observation/lookup allocated {allocated} bytes in native callbacks " +
            $"(warmup=2000, iterations=10000, thread={Environment.CurrentManagedThreadId}, runtime={Environment.Version})");
        CheckNativeLink(check);
    }

    private static void CheckNativeLink(Action<bool, string> check)
    {
        var clip = stackalloc byte[0xA0]; new Span<byte>(clip, 0xA0).Clear();
        var data = stackalloc byte[0x48]; new Span<byte>(data, 0x48).Clear();
        var container = stackalloc byte[0x30]; new Span<byte>(container, 0x30).Clear();
        var handler = stackalloc byte[0x88]; new Span<byte>(handler, 0x88).Clear();
        var effect = stackalloc byte[0x390]; new Span<byte>(effect, 0x390).Clear();
        var module = stackalloc byte[1024]; new Span<byte>(module, 1024).Clear();
        var table = (nint)module; var getter = (nint)(module + 128);
        var sceneTable = (nint)(module + 160); var listenerTable = (nint)(module + 192);
        *(nint*)clip = (nint)(module + 256); *(nint*)(module + 256 + 51 * 8) = (nint)(module + 768);
        new byte[] { 0x40, 0x53, 0x56, 0x57, 0x41, 0x54, 0x48, 0x83, 0xec, 0x78, 0x4c, 0x8b, 0x61,
            0x30, 0x33, 0xf6, 0x48, 0x8b, 0xb9, 0x98, 0, 0, 0, 0x48, 0x8b, 0xd9 }.CopyTo(new Span<byte>(module + 768, 26));
        *(uint*)(clip + 0x84) = 9; *(nint*)(clip + 0x48) = (nint)(data + 8);
        *(uint*)data = 0x32313043; *(uint*)(data + 4) = 0x48;
        *(nint*)(clip + 0x98) = (nint)container; *(nint*)(container + 0x28) = (nint)handler;
        *(nint*)handler = table; *(nint*)(module + 13 * 8) = getter;
        *(ulong*)getter = 0xC300000080818B48UL; *(nint*)(handler + 0x80) = (nint)effect;
        *(nint*)effect = sceneTable; *(nint*)(effect + 0x90) = listenerTable;
        nint Link() => GameAudioLayout.ClipEffectListener((nint)clip, (nint)module, (nint)(module + 1024), sceneTable, listenerTable);
        check(Link() == (nint)(effect + 0x90), "The native C012 scene effect did not resolve its Apricot listener");
        module[768 + 19] = 0xA0;
        check(Link() == 0, "A changed C012 container offset was read as the pinned layout");
        module[768 + 19] = 0x98;
        *(nint*)(effect + 0x90) = sceneTable;
        check(Link() == 0, "A different scene listener interface was accepted");
        *(nint*)(effect + 0x90) = listenerTable; *(nint*)effect = listenerTable;
        check(Link() == 0, "A different scene object type was accepted");
        *(nint*)effect = sceneTable; *(byte*)getter = 0x90;
        check(Link() == 0, "An unfamiliar scene getter was invoked or its offset was assumed");
        *(ulong*)getter = 0xC300000080818B48UL; *(nint*)(module + 13 * 8) = (nint)(module + 1023);
        check(Link() == 0, "An out-of-range scene getter was read");
        *(nint*)(module + 13 * 8) = getter; *(nint*)handler = (nint)(module + 1023);
        check(Link() == 0, "An out-of-range scene handler table was read");
        *(nint*)handler = table; *(uint*)(data + 4) = 0x44;
        check(Link() == 0, "A changed C012 entry size was accepted");
        *(uint*)(data + 4) = 0x48; *(uint*)data = 0x33363043;
        check(Link() == 0, "A C063 sound entry inherited C012 scene ownership");
        *(uint*)data = 0x32313043; *(uint*)(clip + 0x84) = 0x34;
        check(Link() == 0, "A sound clip was read as a visual effect clip");
        *(uint*)(clip + 0x84) = 9; *(nint*)(clip + 0x98) = 0;
        check(Link() == 0 && !GameAudioLayout.IsEffectClip(0), "A missing native effect container was dereferenced");
    }
}
