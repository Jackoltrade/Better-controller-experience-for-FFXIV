// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Runtime.InteropServices;
using System.Text;
using DualSenseLighting.Core;
using DualSenseLighting.Windows;

internal static unsafe class EnemyTargetingCueChecks
{
    internal static void Run(Action<bool, string> check)
    {
        CheckNames(check);
        CheckReservations(check);
        CheckEncounterWarnings(check);
        CheckDeferredNames(check);
        CheckChangedReservations(check);
        CheckConcurrentRead(check);
        CheckNativeNames(check);
    }

    private static void CheckNames(Action<bool, string> check)
    {
        foreach (var path in new[]
        {
            "sound/vfx/monster2/SE_Vfx_Monster_ETC_LockOn_3sec_lock.scd",
            "sound/vfx/monster2/SE_Vfx_Monster_ETC_LockOn_5sec_lock.scd",
            "sound/vfx/monster3/SE_Vfx_Monster_ETC_LockOn_GT_3sec_lock.scd",
            "sound/vfx/monster3/SE_Vfx_Monster_ETC_LockOn_GT_5sec_lock.scd",
            "sound/vfx/monster4/SE_Vfx_Monster_ETC_LockOn_Share_5sec_lock.scd",
            "sound/vfx/monster6/SE_Vfx_Monster_ETC_LockOn_GT_4sec_lock.scd",
            "sound/vfx/monster6/SE_Vfx_Monster_ETC_LockOn_Share_3sec_lock.scd",
            "sound/vfx/monster7/SE_Vfx_Monster_ETC_LockOn_6sec_lock.scd",
            "sound/vfx/monster7/SE_Vfx_Monster_ETC_LockOn_8sec_lock.scd",
            "SOUND/VFX/MONSTER/SE_VFX_MONSTER_ETC_LOCKON_SHARE_5SEC_LOCK.SCD",
            "sound/vfx/monster123/SE_Vfx_Monster_ETC_LockOn_Future_10sec_lock.scd",
        }) check(StockTargetingCue.Matches(Encoding.ASCII.GetBytes(path)), "A stock targeting cue was missed: " + path);
        foreach (var path in new[]
        {
            "", "sound/vfx/monster4/SE_Vfx_Monster_ETC_LockOn_.scd",
            "sound/vfx/monster4/SE_Vfx_Monster_ETC_LockOn_Share_5sec_lock.scd.extra",
            "sound/vfx/monster4/SE_Vfx_Monster_ETC_LockOn_Share_5sec_lock.scd\0",
            "sound/vfx/monster4/SE_Vfx_Monster_ETC_LockOn_sub/name.scd",
            "sound/vfx/monster4/SE_Vfx_Monster_ETC_LockOn_../name.scd",
            "sound/vfx/monsterX/SE_Vfx_Monster_ETC_LockOn_Share_5sec_lock.scd",
            "other/sound/vfx/monster4/SE_Vfx_Monster_ETC_LockOn_Share_5sec_lock.scd",
            "sound/vfx/monster4/SE_Vfx_Monster_ETC_LockOn2_Share_5sec_lock.scd",
            "sound/vfx/monster4/SE_Vfx_Monster_ETC_LockOn_é.scd",
            "sound/vfx/monster6/SE_Vfx_Monster_Tsukuyomi_GunAtk_c.scd",
            "sound/vfx/monster6/SE_Vfx_Monster_Tsukuyomi_UltRaid_c.scd",
            "sound/vfx/monster8/SE_Vfx_Monster_m0785_lock06.scd",
            "sound/vfx/monster8/SE_Vfx_Monster_Necromancer_Explosion_c.scd",
        }) check(!StockTargetingCue.Matches(Encoding.UTF8.GetBytes(path)), "A non-stock targeting path was suppressed: " + path);
        for (var i = 0; i < 100; i++) StockTargetingCue.Matches("sound/vfx/monster4/SE_Vfx_Monster_ETC_LockOn_Share_5sec_lock.scd"u8);
        Action matchCue = () => StockTargetingCue.Matches("sound/vfx/monster4/SE_Vfx_Monster_ETC_LockOn_Share_5sec_lock.scd"u8);
        var allocated = AllocationChecks.MeasureAllocations(matchCue, warmup: 0, iterations: 10000);
        check(allocated == 0, $"Targeting cue filename matching allocated {allocated} bytes on the audio path");
    }

    private static GameAudioLayout.PlayerSoundOwnership Setup(GameAudioLayout.SoundData* pool, Func<nint, bool?> reader)
    {
        var ownership = new GameAudioLayout.PlayerSoundOwnership(pool, reader);
        ownership.SetPlayer(100, 1000, 10000);
        ownership.SetEnemies(new[] { new GameAudioLayout.EnemyActor(200, 2000, 20000, 20, EnemySoundKind.Boss) }, 1);
        pool->IsActive = 1; pool->SoundId = 123; pool->ResourceHandle = 999; pool->SoundNumber = 0;
        return ownership;
    }

    private static GameAudioLayout.SoundData* Allocate() => (GameAudioLayout.SoundData*)NativeMemory.AllocZeroed(
        (nuint)(GameAudioLayout.PlayerSoundOwnership.Capacity * sizeof(GameAudioLayout.SoundData)));

    private static EnemyActionTag Released => new(EnemyActionKind.Skill, TimelineId: 3209,
        Phase: EnemyActionPhase.ReleasedEffect, HasAttackType: true);

    private static void CheckReservations(Action<bool, string> check)
    {
        var pool = Allocate();
        try
        {
            var reads = 0; var marker = true;
            var ownership = Setup(pool, _ => { reads++; return marker; });
            var boss = ownership.ResolveCaster(200, 0);
            ownership.Acquired(pool, boss, action: new(EnemyActionKind.Skill, TimelineId: 6394, Phase: EnemyActionPhase.BossMechanic));
            check(ownership.EnemyKindOf(123) == EnemySoundKind.None && !ownership.TryGetEnemyPosition(123, out _) && reads == 1,
                "A targeting arrow was admitted through boss-helper promotion or reread each block");
            marker = false;
            ownership.Acquired(pool, boss, action: Released);
            check(ownership.EnemyKindOf(123) == EnemySoundKind.Boss && ownership.EnemyKindOf(123) == EnemySoundKind.Boss && reads == 2,
                "An actual impact was suppressed, or identical slot reuse retained the prior cue result");
            pool->SoundNumber = 1;
            check(ownership.EnemyKindOf(123) == EnemySoundKind.Boss && reads == 3, "Sound number changes retained stale cue results");
            pool->ResourceHandle = 1001;
            check(ownership.EnemyKindOf(123) == EnemySoundKind.Boss && reads == 4, "Resource pointer changes retained stale cue results");
            pool->SoundId = 124;
            check(ownership.EnemyKindOf(124) == EnemySoundKind.Boss && reads == 5, "Reservation ID changes retained stale cue results");
            marker = true;
            ownership.Acquired(pool, boss, action: Released);
            check(ownership.EnemyKindOf(124) == EnemySoundKind.None && reads == 6, "Reused resource pointers retained an earlier non-cue decision");
            ownership.Acquired(pool, boss, action: new(EnemyActionKind.AutoAttack, 7));
            check(ownership.EnemyKindOf(124) == EnemySoundKind.None && reads == 6, "Cue filtering examined an already-rejected basic attack");
            ownership.Acquired(pool, boss, action: new(EnemyActionKind.Skill, TimelineId: 3211, Phase: EnemyActionPhase.Caster));
            check(ownership.EnemyKindOf(124) == EnemySoundKind.None && reads == 6, "Cue filtering admitted or read a rejected boss casting layer");
            ownership.Acquired(pool, ownership.Player, action: Released);
            check(ownership.Contains(124) && ownership.EnemyKindOf(124) == EnemySoundKind.None && reads == 6,
                "Enemy targeting-cue suppression interfered with the player's existing SFX channel");
            marker = false;
            ownership.Acquired(pool, boss, action: Released);
            ownership.EnemyKindOf(124);
            for (var i = 0; i < 100; i++) ownership.EnemyKindOf(124);
            Action lookupKind = () => ownership.EnemyKindOf(124);
            var allocated = AllocationChecks.MeasureAllocations(lookupKind, warmup: 0, iterations: 10000);
            check(allocated == 0 && reads == 7,
                $"Cached enemy cue filtering allocated {allocated} bytes or performed repeated resource reads");
            marker = true; pool->SoundId = -20;
            ownership.Acquired(pool, boss, action: Released); ownership.LinkPlayback(-20, 500);
            check(ownership.EnemyKindOf(500) == EnemySoundKind.None && reads == 8,
                "A positional playback alias bypassed targeting cue suppression");
            marker = false;
            ownership.Acquired(pool, boss, action: Released); ownership.LinkPlayback(-20, 500);
            check(ownership.EnemyKindOf(500) == EnemySoundKind.Boss && reads == 9,
                "Targeting cue filtering lost an actual impact's positional playback alias");
        }
        finally { NativeMemory.Free(pool); }
    }

    private static void CheckEncounterWarnings(Action<bool, string> check)
    {
        var pool = Allocate();
        try
        {
            var reads = 0;
            var path = "sound/vfx/monster4/SE_Vfx_Monster_ETC_LockOn_Share_5sec_lock.scd";
            var ownership = Setup(pool, _ => { reads++; return StockTargetingCue.Matches(Encoding.ASCII.GetBytes(path)); });
            ownership.SetEnemies(new[] { new GameAudioLayout.EnemyActor(200, 2000, 20000, 20,
                EnemySoundKind.Boss, EnemyActorRole.BossEncounter) }, 1);
            var boss = ownership.ResolveCaster(200, 0);
            ownership.Acquired(pool, boss);
            check(ownership.EnemyKindOf(123) == EnemySoundKind.None && ownership.EnemyKindOf(123) == EnemySoundKind.None && reads == 1,
                "An untagged encounter warning bypassed the stock cue veto or repeated its resource read");
            ownership.Acquired(pool, boss, action: Released);
            check(ownership.EnemyKindOf(123) == EnemySoundKind.None && reads == 2,
                "A released encounter warning bypassed the stock cue veto");
            path = "sound/vfx/monster6/SE_Vfx_Monster_Tsukuyomi_GunAtk_c.scd";
            ownership.Acquired(pool, boss, action: Released);
            check(ownership.EnemyKindOf(123) == EnemySoundKind.Boss && reads == 3,
                "Filtering the warning suppressed the recorded line-stack impact");
            ownership.Acquired(pool, boss);
            check(ownership.EnemyKindOf(123) == EnemySoundKind.Boss && reads == 4,
                "Filtering warnings removed the broad admission of untagged real encounter effects");
            path = "sound/vfx/monster4/SE_Vfx_Monster_ETC_LockOn_Share_5sec_lock.scd";
            pool->SoundId = -20;
            ownership.Acquired(pool, boss); ownership.LinkPlayback(-20, 500);
            check(ownership.EnemyKindOf(500) == EnemySoundKind.None && reads == 5,
                "An encounter warning playback alias bypassed the stock cue veto");
            ownership.Acquired(pool, ownership.Player, action: Released); ownership.LinkPlayback(-20, 500);
            check(ownership.Contains(500) && reads == 5,
                "The encounter warning veto interfered with the owned player channel");
        }
        finally { NativeMemory.Free(pool); }
    }

    private static void CheckDeferredNames(Action<bool, string> check)
    {
        var pool = Allocate();
        try
        {
            var reads = 0; bool? marker = null; var fail = false;
            var ownership = Setup(pool, _ => { reads++; if (fail) throw new InvalidOperationException(); return marker; });
            pool->ResourceHandle = 0;
            ownership.Acquired(pool, ownership.ResolveCaster(200, 0), action: Released);
            check(ownership.EnemyKindOf(123) == EnemySoundKind.Boss && reads == 0, "A deferred resource changed existing skill admission");
            pool->ResourceHandle = 999;
            check(ownership.EnemyKindOf(123) == EnemySoundKind.Boss && ownership.EnemyKindOf(123) == EnemySoundKind.Boss && reads == 2,
                "Unreadable resource names were cached or suppressed known impacts");
            marker = true;
            check(ownership.EnemyKindOf(123) == EnemySoundKind.None && ownership.EnemyKindOf(123) == EnemySoundKind.None && reads == 3,
                "A resource that finished loading was not retried and classified");
            fail = true; ownership.Acquired(pool, ownership.ResolveCaster(200, 0), action: Released);
            check(ownership.EnemyKindOf(123) == EnemySoundKind.Boss && reads == 4,
                "An optional name-reader failure disrupted existing skill attribution");
            fail = false;
            check(ownership.EnemyKindOf(123) == EnemySoundKind.None && reads == 5,
                "A name-reader failure was permanently cached");
        }
        finally { NativeMemory.Free(pool); }
    }

    private static void CheckChangedReservations(Action<bool, string> check)
    {
        var pool = Allocate();
        try
        {
            var reads = 0; var mutation = 1;
            GameAudioLayout.PlayerSoundOwnership? ownership = null;
            ownership = Setup(pool, _ =>
            {
                reads++;
                if (mutation == 1) pool->SoundId++;
                else if (mutation == 2) pool->ResourceHandle++;
                else if (mutation == 3) pool->SoundNumber++;
                else if (mutation == 4) ownership!.Acquired(pool, ownership.ResolveCaster(200, 0), action: Released);
                return false;
            });
            var boss = ownership.ResolveCaster(200, 0);
            for (var change = 1; change <= 4; change++)
            {
                mutation = change;
                ownership.Acquired(pool, boss, action: Released);
                var id = pool->SoundId;
                check(ownership.EnemyKindOf(id) == EnemySoundKind.None,
                    "A native reservation changed during its name read but retained enemy attribution");
                mutation = 0;
                check(ownership.EnemyKindOf(pool->SoundId) == EnemySoundKind.Boss && reads == change * 2,
                    "An unstable resource read polluted the next reservation's cache");
            }
        }
        finally { NativeMemory.Free(pool); }
    }

    private static void CheckConcurrentRead(Action<bool, string> check)
    {
        var pool = Allocate();
        try
        {
            var reads = 0; var nested = EnemySoundKind.Boss;
            GameAudioLayout.PlayerSoundOwnership? ownership = null;
            ownership = Setup(pool, _ => { reads++; nested = ownership!.EnemyKindOf(123); return true; });
            ownership.Acquired(pool, ownership.ResolveCaster(200, 0), action: Released);
            check(ownership.EnemyKindOf(123) == EnemySoundKind.None && nested == EnemySoundKind.None && reads == 1,
                "Concurrent marker classification leaked its first added haptic block or blocked recursively");
            check(ownership.EnemyKindOf(123) == EnemySoundKind.None && reads == 1,
                "Concurrent marker classification failed to release or cache its result");
        }
        finally { NativeMemory.Free(pool); }
    }

    private static void CheckNativeNames(Action<bool, string> check)
    {
        if (!OperatingSystem.IsWindows()) return;
        // Only fake resource handles in this test process are read. No installed
        // game, audio engine, controller, or resource loader is involved.
        var resource = (byte*)NativeMemory.AllocZeroed(0x100);
        var filename = (byte*)NativeMemory.AllocZeroed(1024);
        try
        {
            var marker = "sound/vfx/monster4/SE_Vfx_Monster_ETC_LockOn_Share_5sec_lock.scd"u8;
            marker.CopyTo(new Span<byte>(filename, 1024));
            *(nint*)(resource + 0x48) = (nint)filename;
            *(ulong*)(resource + 0x58) = (ulong)marker.Length;
            *(ulong*)(resource + 0x60) = 1023;
            check(EnemySoundCueFilter.Read((nint)resource) == true,
                "A valid heap-backed native filename did not identify the stock targeting cue");
            filename[0] = (byte)'b';
            check(EnemySoundCueFilter.Read((nint)resource) == false,
                "A readable non-marker filename was treated as a targeting cue");
            filename[0] = (byte)'s';
            check(EnemySoundCueFilter.Read(0) == null && EnemySoundCueFilter.Read(1) == null &&
                EnemySoundCueFilter.Read(nint.MaxValue) == null,
                "A null, unreadable, or overflowing resource handle was dereferenced or classified");

            *(ulong*)(resource + 0x58) = 0;
            check(EnemySoundCueFilter.Read((nint)resource) == null,
                "An empty/deferred native filename was cached as a definite non-marker");
            *(ulong*)(resource + 0x58) = 1025;
            check(EnemySoundCueFilter.Read((nint)resource) == null,
                "An oversized native filename exceeded the bounded path read");
            *(ulong*)(resource + 0x58) = ulong.MaxValue;
            check(EnemySoundCueFilter.Read((nint)resource) == null,
                "An overflowing native string length reached a pointer read or integer cast");
            *(ulong*)(resource + 0x58) = (ulong)marker.Length;
            *(ulong*)(resource + 0x60) = 1;
            check(EnemySoundCueFilter.Read((nint)resource) == null,
                "A native string length larger than capacity was accepted");
            *(ulong*)(resource + 0x60) = 1023;
            *(nint*)(resource + 0x48) = 0;
            check(EnemySoundCueFilter.Read((nint)resource) == null,
                "A missing heap string pointer was treated as a readable filename");
            *(nint*)(resource + 0x48) = 1;
            check(EnemySoundCueFilter.Read((nint)resource) == null,
                "An unreadable heap string pointer was dereferenced or classified");
            *(nint*)(resource + 0x48) = nint.MaxValue - marker.Length + 1;
            check(EnemySoundCueFilter.Read((nint)resource) == null,
                "A native heap filename spanning the address limit reached ReadProcessMemory");

            new Span<byte>(resource + 0x48, 32).Clear();
            "other.scd"u8.CopyTo(new Span<byte>(resource + 0x48, 16));
            *(ulong*)(resource + 0x58) = 9;
            *(ulong*)(resource + 0x60) = 15;
            check(EnemySoundCueFilter.Read((nint)resource) == false,
                "A valid inline native filename was mistaken for a heap pointer or targeting cue");
            *(ulong*)(resource + 0x58) = 16;
            check(EnemySoundCueFilter.Read((nint)resource) == null,
                "An invalid inline native string exceeded its inline capacity");
            *(ulong*)(resource + 0x58) = 0;
            check(EnemySoundCueFilter.Read((nint)resource) == null,
                "An empty inline native filename lost its deferred/unknown state");

            *(nint*)(resource + 0x48) = (nint)filename;
            *(ulong*)(resource + 0x58) = 1024;
            *(ulong*)(resource + 0x60) = 1024;
            check(EnemySoundCueFilter.Read((nint)resource) == false,
                "A readable path at the maximum supported length was rejected as unreadable");
            *(ulong*)(resource + 0x58) = (ulong)marker.Length;
            for (var i = 0; i < 100; i++) EnemySoundCueFilter.Read((nint)resource);
            var resourceAddress = (nint)resource;
            Action readCue = () => EnemySoundCueFilter.Read(resourceAddress);
            var allocated = AllocationChecks.MeasureAllocations(readCue, warmup: 0, iterations: 1000);
            check(allocated == 0,
                $"The bounded native filename reader allocated {allocated} bytes on the audio callback path");
        }
        finally { NativeMemory.Free(resource); NativeMemory.Free(filename); }
    }
}
