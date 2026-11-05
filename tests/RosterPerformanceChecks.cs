// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Diagnostics;
using DualSenseLighting.Core;

/// <summary>Replays production roster stages and immutable publication, excluding Dalamud/native reads.</summary>
internal static unsafe class RosterPerformanceChecks
{
    internal sealed record Result(string Case, int Candidates, int Iterations, double[] MicrosecondsPerTick, long[] AllocatedBytes, long Checksum);

    internal static Result[] Run()
    {
        var results = new Result[9];
        var index = 0;
        foreach (var count in new[] { 0, 1, 32, 128, 1024 }) results[index++] = Measure("stable", count, 1000);
        results[index++] = Measure("identity churn", 128, 100);
        results[index++] = Measure("territory reset", 128, 100);
        results[index++] = Measure("ownership chain", 128, 100);
        results[index] = Measure("saturated history", 128, 100);
        return results;
    }

    private static Result Measure(string scenario, int count, int iterations)
    {
        var times = new double[10];
        var allocations = new long[10];
        long checksum = 0;
        for (var batch = 0; batch < times.Length; batch++)
        {
            var source = new GameAudioLayout.EnemyActor[count];
            var candidates = new GameAudioLayout.EnemyActor[count];
            var selected = new GameAudioLayout.EnemyActor[count];
            var seeds = new uint[] { 1, 2, 3, 4, 5, 6, 7, 8 };
            for (var i = 0; i < count; i++) source[i] = new((nint)(i + 100), (uint)(i + 100), (nint)(i + 200), i,
                EnemySoundKind.Regular, OwnerId: scenario.StartsWith("ownership", StringComparison.Ordinal) ? (uint)(i == 0 ? 1 : i + 99) : 0,
                BaseId: (uint)(i + 300));
            var state = new BossEncounterState(scenario == "saturated history" ? 32 : BossEncounterState.MaximumCapacity);
            var ownership = new GameAudioLayout.PlayerSoundOwnership(null);
            void Tick(int tick)
            {
                source.AsSpan().CopyTo(candidates);
                if (scenario == "identity churn")
                    for (var i = 0; i < count; i++) candidates[i] = candidates[i] with { EntityId = (uint)(100 + i + tick * count) };
                var scene = scenario == "territory reset" ? (uint)(100 + tick % 2) : 100u;
                var length = state.ExcludePlayerOwned(candidates, seeds);
                state.Update(scene, 1, 1, false, false);
                var chosen = state.SelectRoster(candidates.AsSpan(0, length), selected);
                ownership.SetEnemies(selected.AsSpan(0, chosen), scene);
                if (chosen > 0 && ownership.ResolveCaster(selected[0].Actor, selected[0].DrawObject) == null)
                    throw new Exception("Roster benchmark did not publish selected identities");
                if (scenario.StartsWith("ownership", StringComparison.Ordinal) && chosen != 0)
                    throw new Exception("Player ownership chain escaped benchmark exclusion");
                if (scenario == "saturated history" && (!state.Saturated || state.Count != 32))
                    throw new Exception("Roster benchmark did not saturate bounded history");
                checksum += chosen + state.Count;
            }
            for (var i = 0; i < 200; i++) Tick(i);
            var before = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            for (var i = 0; i < iterations; i++) Tick(i + 200);
            var elapsed = Stopwatch.GetTimestamp() - start;
            allocations[batch] = GC.GetAllocatedBytesForCurrentThread() - before;
            times[batch] = elapsed * 1_000_000.0 / Stopwatch.Frequency / iterations;
            if (scenario == "stable" && allocations[batch] != 0)
                throw new Exception($"Stable roster ({count} candidates, batch {batch}) allocated {allocations[batch]} bytes");
        }
        return new(scenario, count, iterations, times, allocations, checksum);
    }
}
