// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using DualSenseLighting.Core;

internal static unsafe class EnemyClipChecks
{
    internal static void Run(Action<bool, string> check, EnemySkillCatalog catalog)
    {
        var boss = new GameAudioLayout.EnemyActor(200, 2000, 20000, 20, EnemySoundKind.Boss);
        var actor = new GameAudioLayout.PlayerIdentity(200, 2000, 20000);
        var execution = catalog.TimelineKey("mon_sp/m0176/mon_sp001"u8);
        var cast = catalog.TimelineKey("cast/monster"u8);
        foreach (var binding in new[] { EnemySoundBinding.Default, EnemySoundBinding.Target })
        {
            var track = EnemySkillCatalog.TimelineSound(execution, binding);
            check(track.IsReleasedEffect && track.Binding == binding &&
                catalog.ClassifySound(boss, actor, 3, default, track).IsReleasedEffect,
                "Default/target sound tracks embedded in skill execution timelines were rejected");
            check(!EnemySkillCatalog.TimelineSound(cast, binding).IsReleasedEffect &&
                !EnemySkillCatalog.TimelineSound(catalog.Action(870, 1), binding).IsReleasedEffect &&
                !EnemySkillCatalog.TimelineSound(default, binding).IsReleasedEffect &&
                !EnemySkillCatalog.TimelineSound(catalog.TimelineKey("normal_hit/normal_hit"u8), binding).IsReleasedEffect,
                "Clip binding promoted casting, auto-attacks, unknown or ambiguous shared timelines");
            check(!catalog.ClassifySound(boss, actor, 2, default, track).IsReleasedEffect &&
                !catalog.ClassifySound(boss, actor, 3, catalog.Action(870, 1), track).IsReleasedEffect,
                "Embedded skill tracks admitted character sounds or a confirmed basic attack");
        }
        foreach (var binding in new[] { EnemySoundBinding.Caster, EnemySoundBinding.Unknown })
            check(!EnemySkillCatalog.TimelineSound(execution, binding).IsReleasedEffect,
                "Caster-only or unvalidated sound clips were admitted as released effects");
        CheckCapturedSkills(check);
        CheckNativeBinding(check);
        for (var i = 0; i < 2000; i++) EnemySkillCatalog.TimelineSound(execution, EnemySoundBinding.Target);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++) EnemySkillCatalog.TimelineSound(execution, EnemySoundBinding.Target);
        check(GC.GetAllocatedBytesForCurrentThread() == before, "Per-track phase classification allocated on sound callbacks");
    }

    private static void CheckCapturedSkills(Action<bool, string> check)
    {
        // Real metadata from 幻光盗贼 and the previous 拉格 test; no skill-ID allowlist in production.
        var catalog = new EnemySkillCatalog(new[] { new EnemyActionDefinition(20427, 3, 3981, 1875, 130),
            new EnemyActionDefinition(20438, 4, 7684, 1875, 130), new EnemyActionDefinition(7, 1, uint.MaxValue, 1875, 0),
            new EnemyActionDefinition(900, 3, 3201, 1875, 130) }, new[] {
            new EnemyTimelineDefinition(3981, "ability/dgr_ninja/abl021"),
            new EnemyTimelineDefinition(7684, "human_sp/c0101/human_sp094"),
            new EnemyTimelineDefinition(3201, "mon_sp/d1047/mon_sp001"),
            new EnemyTimelineDefinition(1875, "normal_hit/normal_hit"), new EnemyTimelineDefinition(130, "cast") });
        var target = EnemySkillCatalog.TimelineSound(catalog.TimelineKey("ability/dgr_ninja/abl021"u8), EnemySoundBinding.Target);
        var caster = EnemySkillCatalog.TimelineSound(catalog.TimelineKey("ability/dgr_ninja/abl021"u8), EnemySoundBinding.Caster);
        var throwing = EnemySkillCatalog.TimelineSound(catalog.TimelineKey("human_sp/c0101/human_sp094"u8), EnemySoundBinding.Default);
        var laggBody = EnemySkillCatalog.TimelineSound(catalog.TimelineKey("mon_sp/d1047/mon_sp001"u8), EnemySoundBinding.Caster);
        check(target.IsReleasedEffect && throwing.IsReleasedEffect && !caster.IsReleasedEffect && !laggBody.IsReleasedEffect,
            "Captured Ninja/throw effects were missed or the previously excluded 拉格 caster sound returned");
    }

    private static void CheckNativeBinding(Action<bool, string> check)
    {
        var clip = stackalloc byte[0xD0]; new Span<byte>(clip, 0xD0).Clear();
        var actor = stackalloc byte[0x60]; new Span<byte>(actor, 0x60).Clear();
        var track = stackalloc byte[0x18]; new Span<byte>(track, 0x18).Clear();
        var data = stackalloc byte[0x20]; new Span<byte>(data, 0x20).Clear();
        const int table = 1234;
        *(nint*)actor = table; *(uint*)(clip + 0x84) = 0x34;
        *(uint*)data = 0x33363043; *(int*)(data + 4) = 0x20;
        *(nint*)(clip + 0x48) = (nint)(data + 8); *(nint*)(clip + 0x28) = (nint)actor;
        foreach (var (value, expected) in new[] { (0u, EnemySoundBinding.Default), (1u, EnemySoundBinding.Caster),
            (2u, EnemySoundBinding.Target), (3u, EnemySoundBinding.Unknown), (4u, EnemySoundBinding.Unknown),
            (uint.MaxValue, EnemySoundBinding.Unknown) })
        {
            *(uint*)(actor + 0x50) = value;
            check(GameAudioLayout.ClipSoundBinding((nint)clip, table) == expected,
                "Native scheduler sound binding did not follow its validated actor selector");
        }
        *(uint*)(actor + 0x50) = 2;
        *(nint*)(clip + 0x28) = 0; *(nint*)(clip + 8) = (nint)track; *(nint*)(track + 8) = (nint)actor;
        check(GameAudioLayout.ClipSoundBinding((nint)clip, table) == EnemySoundBinding.Target,
            "A first-use sound clip required a populated actor cache");
        *(nint*)actor = 5678;
        check(GameAudioLayout.ClipSoundBinding((nint)clip, table) == EnemySoundBinding.Unknown,
            "An unfamiliar scheduler actor layout was read as a known binding");
        *(nint*)actor = table; *(uint*)data = 0x33353043; *(uint*)(clip + 0x84) = 0x29;
        check(GameAudioLayout.ClipSoundBinding((nint)clip, table) == EnemySoundBinding.Unknown,
            "C053 voice tracks were treated as C063 attack sounds");
        *(uint*)(clip + 0x84) = 0x34;
        check(GameAudioLayout.ClipSoundBinding((nint)clip, table) == EnemySoundBinding.Unknown,
            "A clip with an incompatible data header was accepted");
        *(uint*)data = 0x33363043; *(int*)(data + 4) = 0x1c;
        check(GameAudioLayout.ClipSoundBinding((nint)clip, table) == EnemySoundBinding.Unknown,
            "An unexpected C063 entry size bypassed sound-track validation");
        *(int*)(data + 4) = 0x20; *(nint*)(clip + 0x48) = 0;
        check(GameAudioLayout.ClipSoundBinding((nint)clip, table) == EnemySoundBinding.Unknown &&
            GameAudioLayout.ClipSoundBinding(0, table) == EnemySoundBinding.Unknown &&
            GameAudioLayout.ClipSoundBinding((nint)clip, 0) == EnemySoundBinding.Unknown,
            "Missing clips, data or binding support invented a target track");
        *(nint*)(clip + 0x48) = (nint)(data + 8);
        for (var i = 0; i < 2000; i++) GameAudioLayout.ClipSoundBinding((nint)clip, table);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++) GameAudioLayout.ClipSoundBinding((nint)clip, table);
        check(GC.GetAllocatedBytesForCurrentThread() == before, "Native clip binding lookup allocated in sound callbacks");
        var body = new byte[0x50];
        new byte[] { 0x40, 0x53, 0x48, 0x83, 0xec, 0x20, 0x48, 0x8b, 0xd9, 0x48, 0x8d, 0x05 }.CopyTo(body, 0);
        new byte[] { 0x44, 0x89, 0x43, 0x50, 0x44, 0x89, 0x4b, 0x54 }.CopyTo(body, 0x3b);
        new byte[] { 0xc7, 0x43, 0x58, 0x00, 0x00, 0xff, 0x01 }.CopyTo(body, 0x43);
        check(GameAudioLayout.ActorBindingLayout(body) && !GameAudioLayout.ActorBindingLayout(body.AsSpan(0, 0x48)),
            "Scheduler actor validation accepted truncated code or rejected its pinned layout");
        body[0x3e] = 0x58;
        check(!GameAudioLayout.ActorBindingLayout(body), "A changed scheduler actor binding offset was ignored");
    }
}
