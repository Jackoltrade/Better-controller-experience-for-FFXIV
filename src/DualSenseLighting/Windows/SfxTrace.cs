// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DualSenseLighting.Core;

namespace DualSenseLighting.Windows;

/// <summary>Short metadata recording. Sampling and file I/O run on the framework thread only.</summary>
internal sealed class SfxTrace
{
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly DateTimeOffset started = DateTimeOffset.UtcNow;
    private readonly List<Frame> frames = new();
    private readonly Dictionary<(int, uint), string> names = new();
    private double nextSample;
    private string localActor = "";
    private readonly string version;
    public SfxTrace(string version) => this.version = version;
    public double Remaining => Math.Max(0, 15 - clock.Elapsed.TotalSeconds);
    private sealed record Sound(int Slot, int SoundId, int PlaybackId, bool Owned, uint Number, string Name,
        byte Origin, string SourceActor, string SourceDraw, int TimelineOwnerIndex, bool IsPositional, Point? Position);
    private sealed record Point(float X, float Y, float Z)
    { public static Point? Of(Vector3? point) => point.HasValue ? new(point.Value.X, point.Value.Y, point.Value.Z) : null; }
    private sealed record Haptics(HapticSettings? Settings, Point? Character, Point? Camera, Point? CameraRight,
        int? ImpactSoundId, Point? ImpactPosition, long SpatialBlocks, long CutoffBlocks, long SuppressedBlocks, long BiasedBlocks,
        string? Error, long FootstepBlocks, bool FootstepContextReady);
    private sealed record Frame(double Seconds, Sound[] Sounds, SfxAudioCapture.VoiceObservation[] Voices, Haptics Haptics);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(nint process, nint address, byte[] data, nuint size, out nuint read);

    private static byte[]? Read(nint address, int count)
    {
        if (address == 0) return null;
        var bytes = new byte[count];
        return ReadProcessMemory(-1, address, bytes, (nuint)count, out var read) && read == (nuint)count ? bytes : null;
    }

    private string Name(GameAudioLayout.SoundReservation sound)
    {
        var key = (sound.SoundId, sound.SoundNumber);
        if (names.TryGetValue(key, out var cached)) return cached;
        // RPM is used even in our own process: a resource can disappear between
        // snapshots, and an unreadable string must not become an unsafe dereference.
        var text = sound.ResourceHandle == 0 ? null : Read(sound.ResourceHandle + 0x48, 32);
        if (text == null) return "";
        var length = BitConverter.ToInt64(text, 16);
        var capacity = BitConverter.ToInt64(text, 24);
        if (length <= 0 || length > 1024 || capacity < length) return "";
        var bytes = capacity < 16 ? text : Read((nint)BitConverter.ToInt64(text), (int)length);
        if (bytes == null) return "";
        return names[key] = Encoding.UTF8.GetString(bytes, 0, (int)length);
    }

    public bool Sample(SfxAudioCapture capture)
    {
        var seconds = clock.Elapsed.TotalSeconds;
        if (seconds < 15 && seconds >= nextSample)
        {
            localActor = $"{capture.PlayerAddress:X}";
            nextSample = seconds + 0.05;
            var sounds = capture.Reservations().Select(s => new Sound(s.Slot, s.SoundId, s.PlaybackId,
                s.Owned, s.SoundNumber, Name(s), s.Origin, $"{s.SourceActor:X}", $"{s.SourceDraw:X}", s.TimelineOwnerIndex,
                s.IsPositional, Point.Of(s.Position))).ToArray();
            var output = capture.Haptics; var pose = output?.Listener; var cue = output?.ImpactCue;
            frames.Add(new Frame(seconds, sounds, capture.Voices(), new Haptics(output?.CurrentSettings,
                Point.Of(pose?.Origin), Point.Of(pose?.CameraPosition), Point.Of(pose?.CameraRight), cue?.SoundId,
                Point.Of(cue?.Position), output?.SpatialBlocks ?? 0, output?.CutoffBlocks ?? 0,
                output?.SuppressedBlocks ?? 0, output?.BiasedBlocks ?? 0, capture.SpatialError ?? capture.HapticsError,
                output?.FootstepBlocks ?? 0, output?.FootstepContextReady ?? false)));
        }
        return seconds >= 15;
    }

    public void Save(string path)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter());
        File.WriteAllText(path, JsonSerializer.Serialize(new { PluginVersion = version, StartedUtc = started,
            DurationSeconds = clock.Elapsed.TotalSeconds, LocalActor = localActor, Frames = frames }, options));
    }
}
