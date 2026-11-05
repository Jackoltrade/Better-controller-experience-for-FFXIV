// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Runtime.InteropServices;
using DualSenseLighting.Core;

internal static unsafe class EnemyEncounterChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var catalog = new EnemyEncounterCatalog(new[] {
            new EnemyHuntDefinition(2919, 3175, 1), new EnemyHuntDefinition(13361, 17707, 2),
            new EnemyHuntDefinition(2953, 3209, 3), new EnemyHuntDefinition(50, 500, 0),
            new EnemyHuntDefinition(51, 501, 4), new EnemyHuntDefinition(0, 502, 2), new EnemyHuntDefinition(52, 0, 2)
        }, new[] {
            new EnemyFateDefinition(123, 60502, 60722), new EnemyFateDefinition(1599, 60802, 63915),
            new EnemyFateDefinition(902, 60501, 60722), new EnemyFateDefinition(2000, 0, 63915),
            new EnemyFateDefinition(120, 60501, 60721), new EnemyFateDefinition(168, 60503, 60723),
            new EnemyFateDefinition(136, 60504, 60724), new EnemyFateDefinition(0, 60502, 60722)
        }, new[] {
            new EnemyNpcRankDefinition(17422, 1), new EnemyNpcRankDefinition(12029, 2),
            new EnemyNpcRankDefinition(13294, 6), new EnemyNpcRankDefinition(20000, 0),
            new EnemyNpcRankDefinition(20001, 3), new EnemyNpcRankDefinition(0, 1)
        });
        foreach (var pair in new[] { (2919u, 3175u), (13361u, 17707u), (2953u, 3209u) })
        {
            check(catalog.IsBoss(0, pair.Item1, pair.Item2, 0), "A registered B/A/S elite Hunt mark was left regular");
            check(!catalog.IsBoss(0, pair.Item1, pair.Item2 + 1, 0) && !catalog.IsBoss(0, pair.Item1 + 1, pair.Item2, 0),
                "A different NPC entry or a shared name inherited Hunt boss classification");
        }
        check(!catalog.IsBoss(0, 50, 500, 0) && !catalog.IsBoss(0, 51, 501, 0) &&
            !catalog.IsBoss(0, 0, 502, 0) && !catalog.IsBoss(0, 52, 0, 0),
            "Invalid or unrecognized Hunt metadata became a boss");
        foreach (var fate in new ushort[] { 123, 1599, 902, 2000 })
            foreach (var npc in new uint[] { 17422, 12029, 13294 })
                check(catalog.IsBoss(0, 13006, npc, fate), "A ranked NPC in a boss-marked FATE was missed");
        foreach (var fate in new ushort[] { 0, 120, 168, 136, 65535 })
            check(!catalog.IsBoss(0, 13006, 17422, fate), "An elite near a FATE or an ordinary event was promoted");
        foreach (var npc in new uint[] { 0, 20000, 20001, 999999 })
            check(!catalog.IsBoss(0, 13006, npc, 123), "FATE trash, an unknown rank or a missing base entry became a boss");
        check(!catalog.IsBoss(0, 0, 17422, 123), "An unnamed FATE object became a boss anchor");
        check(catalog.IsBoss(737, 9511, 12029, 0) && !catalog.IsBoss(738, 9511, 12029, 0),
            "Open-world classification changed duty-specific boss matching");
        CheckHelperIdentity(check);
        EnemyMechanicRoutingChecks.Run(check);
        for (var i = 0; i < 2000; i++) catalog.IsBoss(0, 13006, 17422, 123);
        Action lookup = () => { catalog.IsBoss(0, 13006, 17422, 123); catalog.IsBoss(0, 13361, 17707, 0); };
        var allocated = AllocationChecks.MeasureAllocations(lookup, warmup: 0, iterations: 10000);
        check(allocated == 0, $"Boss metadata lookup allocated {allocated} bytes on framework updates");
    }

    private static void CheckHelperIdentity(Action<bool, string> check)
    {
        var boss = new GameAudioLayout.EnemyActor(200, 2000, 20000, 20, EnemySoundKind.Boss,
            NameId: 13006, BaseId: 17422);
        var helper = new GameAudioLayout.EnemyActor(500, 5000, 50000, 50, EnemySoundKind.Boss,
            Role: EnemyActorRole.BossMechanic, NameId: 13006, BaseId: 9020);
        var selected = new GameAudioLayout.EnemyActor[2];
        check(GameAudioLayout.SelectEnemyActors(new[] { helper, boss }, selected) == 2 &&
            GameAudioLayout.SelectEnemyActors(new[] { helper }, selected) == 0,
            "An open-world boss helper was missed or registered without its live boss");
        check(GameAudioLayout.SelectEnemyActors(new[] { helper, boss with { NameId = 13007 } }, selected) == 1 &&
            GameAudioLayout.SelectEnemyActors(new[] { helper, boss with { Kind = EnemySoundKind.Regular } }, selected) == 1,
            "A helper inherited an unrelated NPC or ordinary enemy as its boss anchor");
        var pool = (GameAudioLayout.SoundData*)NativeMemory.AllocZeroed((nuint)(GameAudioLayout.PlayerSoundOwnership.Capacity * sizeof(GameAudioLayout.SoundData)));
        try
        {
            var ownership = new GameAudioLayout.PlayerSoundOwnership(pool);
            ownership.SetPlayer(100, 1000, 10000);
            var count = GameAudioLayout.SelectEnemyActors(new[] { helper, boss }, selected);
            ownership.SetEnemies(selected.AsSpan(0, count), 10);
            var identity = ownership.ResolveCaster(helper.Actor, 0)!;
            check(identity != null && ReferenceEquals(identity, ownership.ResolveTimelineCaster(helper.ObjectIndex, -1)),
                "A native boss helper's actor and timeline index resolved to different identities");
            var tag = new EnemyActionTag(EnemyActionKind.Skill, 1000, Phase: EnemyActionPhase.BossMechanic);
            pool->IsActive = 1; pool->SoundId = 123;
            ownership.Acquired(pool, identity, 1, action: tag);
            check(ownership.EnemyKindOf(123) == EnemySoundKind.Boss, "A confirmed boss helper did not retain its exact sound ownership");
            count = GameAudioLayout.SelectEnemyActors(new[] { helper }, selected);
            ownership.SetEnemies(selected.AsSpan(0, count), 10);
            check(ownership.ResolveCaster(helper.Actor, 0) == null && ownership.EnemyKindOf(123) == EnemySoundKind.None,
                "Removing the live boss retained a helper or its queued audio ownership");
            count = GameAudioLayout.SelectEnemyActors(new[] { helper, boss }, selected);
            ownership.SetEnemies(selected.AsSpan(0, count), 10);
            check(!ReferenceEquals(identity, ownership.ResolveCaster(helper.Actor, 0)) && ownership.EnemyKindOf(123) == EnemySoundKind.None,
                "Restoring an identical helper resurrected its previous native sound identity");
        }
        finally { NativeMemory.Free(pool); }
    }
}
