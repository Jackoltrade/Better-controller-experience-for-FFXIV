// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using DualSenseLighting.Core;
using DualSenseLighting.Windows;

internal static unsafe class AudioCapturePerformanceChecks
{
    internal static void Run(Action<bool, string> check)
    {
        CheckAdmission(check);
        CheckPopulatedPool(check);
        CheckSharedLevels(check);
        CompareRmsPasses(check, 2048);
        CompareRmsPasses(check, 4096);
    }

    private static void CheckAdmission(Action<bool, string> check)
    {
        var screen = new HapticSettings(ScreenEffects: true, Spatial: true);
        var player = new HapticSettings(Enabled: true, Strength: .5f);
        check(!SfxCapturePolicy.NeedsVoice(false, screen, false, false, false) &&
            !SfxCapturePolicy.NeedsOwnership(true, 0, false, screen, false, false),
            "Screen-only feedback unnecessarily requested source-voice ownership");
        check(!SfxCapturePolicy.NeedsVoice(false, null, false, false, false) &&
            !SfxCapturePolicy.NeedsVoice(false, player with { Strength = 0 }, false, false, false),
            "Disabled/zero-strength source feedback admitted voice work");
        check(SfxCapturePolicy.NeedsVoice(false, player, false, false, false) &&
            SfxCapturePolicy.NeedsOwnership(true, 0, false, player, false, false),
            "Player SFX feedback lost its ownership lookup");
        check(SfxCapturePolicy.NeedsOwnership(true, 0, false, screen, true, false) &&
            SfxCapturePolicy.NeedsOwnership(true, 0, false, screen, false, true),
            "Enemy-only or hit-reaction-only feedback lost combat attribution");
        check(SfxCapturePolicy.NeedsVoice(true, null, false, false, false) &&
            SfxCapturePolicy.NeedsOwnership(true, 0, true, null, false, false) &&
            !SfxCapturePolicy.NeedsOwnership(true, 0, false, null, false, false),
            "Player-only lighting lost attribution or all-combat lighting gained unnecessary ownership work");
        check(SfxCapturePolicy.NeedsVoice(false, null, false, false, true) &&
            !SfxCapturePolicy.NeedsOwnership(false, 0, false, null, false, false),
            "Native footstep processing was skipped or required unrelated actor attribution");
        check(SfxCapturePolicy.NeedsOwnership(false, 0, false, screen, true, false, arenaEffects: true) &&
            !SfxCapturePolicy.NeedsOwnership(false, 0, false, screen, true, false),
            "Arena routing lost player-owned exclusion or unrelated noncombat buses admitted actor scans");
        foreach (uint flags in new uint[] { 2, 4, 8, 14 })
            check(!SfxCapturePolicy.NeedsOwnership(true, flags, false, player, true, true),
                "Already-native/vibration-only flags admitted redundant haptic ownership work");
        check(SfxCapturePolicy.NeedsOwnership(true, 2, true, null, false, false) &&
            !SfxCapturePolicy.NeedsOwnership(false, 2, true, player, true, true),
            "Native haptic flags accidentally suppressed valid player lighting or admitted a noncombat voice");
    }

    private static NativeHapticOutput RumbleOutput()
    {
        var output = new NativeHapticOutput(null, 0, 0, 0);
        output.Configure(new HapticSettings(Enabled: true, Spatial: false, DistanceFalloff: false),
            true, true, useRumble: true);
        return output;
    }

    private static void CheckSharedLevels(Action<bool, string> check)
    {
        var span = new SfxLoudness(); var level = new SfxLoudness();
        span.SetActive(true); level.SetActive(true);
        foreach (var length in new[] { 1, 2048, 4096 })
        {
            var pcm = new float[length];
            for (var i = 0; i < pcm.Length; i++) pcm[i] = i % 2 == 0 ? .125f : -.375f;
            span.Observe(pcm, span.Generation);
            level.ObserveLevel(SfxLoudness.MeasureRms(pcm), level.Generation);
            check(span.TakePeak() == level.TakePeak(), "Shared validated level changed the lighting handoff");
        }
        foreach (var invalid in new[] { float.NaN, float.PositiveInfinity, -.1f, 1.1f, -0f })
            level.ObserveLevel(invalid, level.Generation);
        level.ObserveLevel(.25f, level.Generation);
        check(level.TakePeak() == .25f, "Invalid shared level (including negative zero) blocked or amplified valid lighting");
        var stale = level.Generation;
        level.SetActive(false); level.SetActive(true);
        level.ObserveLevel(.8f, stale);
        check(level.TakePeak() == 0, "A stale haptic level republished lighting after reset");
        level.SetActive(false); level.ObserveLevel(.8f, level.Generation);
        check(level.TakePeak() == 0, "Inactive lighting accepted a shared haptic level");

        var output = RumbleOutput();
        var frame = output.Prepare(null, true, 2, 0);
        var pcmBlock = stackalloc float[2048];
        for (var i = 0; i < 2048; i++) pcmBlock[i] = i % 2 == 0 ? .125f : -.375f;
        var measured = output.Mix(frame, pcmBlock, 2048, 0);
        check(measured.HasValue && Math.Abs(measured.Value - MathF.Sqrt(.078125f)) < .000001f &&
            pcmBlock[0] == .125f && pcmBlock[2047] == -.375f,
            "Haptic Mix did not return its actual validated RMS or changed the input PCM");
        _ = output.RumbleSignal.TakePeak();
        pcmBlock[2047] = float.NaN;
        check(output.Mix(frame, pcmBlock, 2048, 0) == 0 && output.RumbleSignal.TakePeak() == 0,
            "Shared RMS skipped validation of an invalid sample at the end of the block");
        pcmBlock[2047] = 17;
        check(output.Mix(frame, pcmBlock, 2048, 0) == 0,
            "Shared RMS admitted out-of-range PCM instead of rejecting the whole block");
        pcmBlock[2047] = -.375f;
        check(output.Mix(default, pcmBlock, 2048, 0) == null && output.Mix(frame, null, 2048, 0) == null,
            "A skipped haptic path claimed a measured level");
        output.Configure(new HapticSettings(), false, false);
        check(output.Mix(frame, pcmBlock, 2048, 0) == null, "A stale haptic frame claimed fresh PCM measurement");
        span.SetActive(true); span.Observe(new ReadOnlySpan<float>(pcmBlock, 2048), span.Generation);
        check(Math.Abs(span.TakePeak() - MathF.Sqrt(.078125f)) < .000001f,
            "The lighting-only fallback failed when haptic work was skipped");
    }

    private static void CompareRmsPasses(Action<bool, string> check, int count)
    {
        var pcm = (float*)NativeMemory.Alloc((nuint)(count * sizeof(float)));
        try
        {
            for (var i = 0; i < count; i++) pcm[i] = i % 2 == 0 ? .125f : -.375f;
            var address = (nint)pcm;
            var oldOutput = RumbleOutput(); var newOutput = RumbleOutput();
            var oldFrame = oldOutput.Prepare(null, true, 2, 0); var newFrame = newOutput.Prepare(null, true, 2, 0);
            var oldLight = new SfxLoudness(); var newLight = new SfxLoudness();
            oldLight.SetActive(true); newLight.SetActive(true);
            var oldGeneration = oldLight.Generation; var newGeneration = newLight.Generation;
            Action oldPath = () =>
            {
                _ = oldOutput.Mix(oldFrame, (float*)address, count, 0);
                oldLight.Observe(new ReadOnlySpan<float>((void*)address, count), oldGeneration);
            };
            Action newPath = () =>
            {
                var rms = newOutput.Mix(newFrame, (float*)address, count, 0);
                if (rms is { } value) newLight.ObserveLevel(value, newGeneration);
                else newLight.Observe(new ReadOnlySpan<float>((void*)address, count), newGeneration);
            };
            for (var i = 0; i < 1000; i++) { oldPath(); newPath(); }
            var oldBytes = AllocationChecks.MeasureAllocations(oldPath, warmup: 0, iterations: 1000);
            var newBytes = AllocationChecks.MeasureAllocations(newPath, warmup: 0, iterations: 1000);
            check(oldBytes == 0 && newBytes == 0 && oldLight.TakePeak() == newLight.TakePeak() &&
                oldOutput.RumbleSignal.TakePeak() == newOutput.RumbleSignal.TakePeak(),
                $"Shared RMS changed haptic/lighting levels or allocated ({count} floats, {oldBytes}/{newBytes} bytes)");
            var oldSamples = new double[5]; var newSamples = new double[5];
            double Time(Action action)
            {
                var started = Stopwatch.GetTimestamp();
                for (var i = 0; i < 1000; i++) action();
                return (Stopwatch.GetTimestamp() - started) * 1e6 / Stopwatch.Frequency / 1000;
            }
            for (var sample = 0; sample < oldSamples.Length; sample++)
            {
                if (sample % 2 == 0) { oldSamples[sample] = Time(oldPath); newSamples[sample] = Time(newPath); }
                else { newSamples[sample] = Time(newPath); oldSamples[sample] = Time(oldPath); }
            }
            Array.Sort(oldSamples); Array.Sort(newSamples);
            Console.WriteLine($"Validated PCM RMS reuse ({count} floats): median duplicate/shared {oldSamples[2]:F3}/{newSamples[2]:F3} us, warmed bytes {oldBytes}/{newBytes}; actual managed rumble Mix+lighting, five alternating synthetic samples, not game FPS.");
        }
        finally { NativeMemory.Free(pcm); }
    }

    private static void CheckPopulatedPool(Action<bool, string> check)
    {
        var pool = (GameAudioLayout.SoundData*)NativeMemory.AllocZeroed(
            (nuint)(GameAudioLayout.PlayerSoundOwnership.Capacity * sizeof(GameAudioLayout.SoundData)));
        try
        {
            var ownership = new GameAudioLayout.PlayerSoundOwnership(pool);
            ownership.SetPlayer(100, 1000, 10000);
            for (var i = 0; i < GameAudioLayout.PlayerSoundOwnership.Capacity; i++)
            {
                pool[i].IsActive = 1; pool[i].SoundId = 1000 + i; pool[i].ResourceHandle = 999;
                ownership.Acquired(pool + i, ownership.Player);
            }
            var screen = new HapticSettings(ScreenEffects: true, Spatial: true);
            var player = new HapticSettings(Enabled: true, Strength: .5f);
            Compare(check, ownership, "screen only", screen, false, false, false, false, false, false, 9999, 0);
            Compare(check, ownership, "noncombat with player feedback", player, false, false, false, false, false, false, 9999, 0);
            Compare(check, ownership, "footsteps only", screen, false, false, false, true, false, false, 9999, 0);
            Compare(check, ownership, "all-combat lighting", null, true, false, false, false, true, false, 1255, 0);
            Compare(check, ownership, "player-only lighting", null, true, false, false, false, true, true, 1255, 10000);
            Compare(check, ownership, "player feedback", player, false, false, false, false, true, false, 1255, 10000);
            Compare(check, ownership, "enemy feedback", screen, false, true, false, false, true, false, 1255, 10000);
        }
        finally { NativeMemory.Free(pool); }
    }

    private static void Compare(Action<bool, string> check, GameAudioLayout.PlayerSoundOwnership ownership,
        string scenario, HapticSettings? settings, bool lighting, bool enemies, bool hit, bool footsteps,
        bool combat, bool playerLighting, int soundId, int expectedNewReads)
    {
        const int iterations = 10000;
        var oldReads = 0; var newReads = 0; var oldOwned = 0; var newOwned = 0;
        var oldHapticsActive = enemies || hit || footsteps || settings is { Enabled: true, Strength: > 0 } ||
            settings is { ScreenEffects: true, ScreenStrength: > 0 };
        var oldNeedsOwnership = oldHapticsActive || combat && playerLighting;
        Action oldPath = () =>
        {
            if (!oldNeedsOwnership) return;
            oldReads++;
            if (ownership.Inspect(soundId, combat && enemies, true).Owned) oldOwned++;
        };
        Action newPath = () =>
        {
            if (!SfxCapturePolicy.NeedsVoice(lighting, settings, enemies, hit, footsteps) ||
                !SfxCapturePolicy.NeedsOwnership(combat, 0, playerLighting, settings, enemies, hit)) return;
            newReads++;
            if (ownership.Inspect(soundId, combat && enemies, true).Owned) newOwned++;
        };
        for (var i = 0; i < 1000; i++) { oldPath(); newPath(); }
        oldReads = newReads = oldOwned = newOwned = 0;
        var oldBytes = AllocationChecks.MeasureAllocations(oldPath, warmup: 0, iterations: iterations);
        var newBytes = AllocationChecks.MeasureAllocations(newPath, warmup: 0, iterations: iterations);
        check(oldReads == (oldNeedsOwnership ? iterations : 0) && newReads == expectedNewReads && oldBytes == 0 && newBytes == 0 &&
            (expectedNewReads == 0 || oldOwned == iterations && newOwned == oldOwned),
            $"Audio admission changed relevant attribution or allocated: {scenario}, reads {oldReads}/{newReads}, bytes {oldBytes}/{newBytes}");
        var checkedOldReads = oldReads; var checkedNewReads = newReads;
        var oldSamples = new double[7]; var newSamples = new double[7];
        // Allocation controls may enter a NoGC region. Throughput timing excludes
        // that setup/cleanup and alternates order to reduce first-run bias.
        double Time(Action action)
        {
            var started = Stopwatch.GetTimestamp();
            for (var i = 0; i < iterations; i++) action();
            return (Stopwatch.GetTimestamp() - started) * 1e6 / Stopwatch.Frequency / iterations;
        }
        for (var sample = 0; sample < oldSamples.Length; sample++)
        {
            if (sample % 2 == 0)
            { oldSamples[sample] = Time(oldPath); newSamples[sample] = Time(newPath); }
            else
            { newSamples[sample] = Time(newPath); oldSamples[sample] = Time(oldPath); }
        }
        Array.Sort(oldSamples); Array.Sort(newSamples);
        Console.WriteLine($"Audio admission {scenario}: median old/new {oldSamples[3]:F3}/{newSamples[3]:F3} us, ownership reads {checkedOldReads}/{checkedNewReads} per {iterations} allocation-check calls; seven alternating warmed timing samples, populated 256-slot synthetic fixture, advisory only, not game FPS.");
    }
}
