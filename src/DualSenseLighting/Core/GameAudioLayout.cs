// SPDX-License-Identifier: MIT
// Minimal bindings adapted from aers/FFXIVClientStructs. See LICENSE-FFXIVClientStructs.txt.
#nullable enable
using System.Runtime.InteropServices;

namespace DualSenseLighting.Core;

/// <summary>Read-only subset of the pinned native mixer layout; no setters or audio calls.</summary>
public static unsafe class GameAudioLayout
{
    [StructLayout(LayoutKind.Explicit, Size = 0xA48)]
    public struct SourceVoice
    {
        [FieldOffset(0xA8)] public int State;
        [FieldOffset(0xB0)] public nint Callback;
        [FieldOffset(0xD8)] public int QueuedBufferCount;
        [FieldOffset(0x530)] public float Volume;
        [FieldOffset(0x534)] public float Pitch;
        [FieldOffset(0x938)] public float PreviousVolume;
        [FieldOffset(0x990)] public int SendCount;
        [FieldOffset(0xA40)] public uint OutputFormat;
    }

    [StructLayout(LayoutKind.Explicit, Size = 0x328)]
    public struct VoiceOwner
    {
        [FieldOffset(0x08)] public nint CallbackVTable;
        [FieldOffset(0x10)] public Track* Track;
        [FieldOffset(0x20)] public SourceVoice* Source;
    }

    [StructLayout(LayoutKind.Explicit, Size = 0x2F0)]
    public struct Track
    {
        [FieldOffset(0x08)] public VoiceOwner* Voice;
        [FieldOffset(0x10)] public RootSound* Owner;
    }

    [StructLayout(LayoutKind.Explicit, Size = 0x480)]
    public struct RootSound
    {
        [FieldOffset(0x19)] public byte BusId;
        [FieldOffset(0x30)] public uint ExtendedFlags;
    }

    public static bool IsCombatSound(byte busId, uint flags) => (busId is 2 or 10) && (flags & 4) == 0;

    public static int SampleCount(int frames, int channels, int spatialAudio, uint format)
    {
        // The prototype supports the validated normal and ambisonic layouts only.
        if (frames != 256 || channels != 8 || spatialAudio is not (0 or 1) || format > 1) return 0;
        return frames * (format == 1 && spatialAudio == 1 ? 16 : channels);
    }

    public static bool IsCombatVoice(SourceVoice* source, nint callbackVTable)
    {
        if (source == null || callbackVTable == 0 || source->State != 3 || source->QueuedBufferCount is < 1 or > 2 ||
            source->SendCount is < 1 or > 5 || !float.IsFinite(source->Pitch) || source->Pitch <= 0 ||
            !float.IsFinite(source->Volume) || !float.IsFinite(source->PreviousVolume) ||
            (source->Volume <= 0.0001f && source->PreviousVolume <= 0.0001f)) return false;
        // The game itself calls this callback interface. Check its exact vtable before
        // treating it as a VoiceSead owner, then validate both links back to this voice.
        var callback = source->Callback;
        if (callback == 0 || *(nint*)callback != callbackVTable) return false;
        var owner = (VoiceOwner*)(callback - 8);
        if (owner->Source != source || owner->Track == null || owner->Track->Voice != owner) return false;
        var sound = owner->Track->Owner;
        return sound != null && IsCombatSound(sound->BusId, sound->ExtendedFlags);
    }
}
