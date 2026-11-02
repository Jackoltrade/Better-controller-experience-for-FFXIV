// SPDX-License-Identifier: GPL-3.0-only
// Native signatures/layout adapted from FFXIVClientStructs; see THIRD-PARTY-NOTICES.txt.
#nullable enable
using System;
using System.Runtime.InteropServices;
using System.Threading;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using DualSenseLighting.Core;

namespace DualSenseLighting.Windows;

/// <summary>Observes native source-voice output without changing playback, routing or samples.</summary>
internal sealed unsafe class SfxAudioCapture : IDisposable
{
    private const string RenderSignature = "48 8B C4 48 89 58 18 55 56 57 41 54 41 55 41 56 41 57 48 8D A8 D8 F9 FF FF";
    private const string CallbackSignature = "48 8D 4F 50 48 8D 05 ?? ?? ?? ?? 48 89 5F 18 8D 53 60 48 89 47 08";
    private readonly Hook<RenderDelegate> render;
    private readonly nint callbackVTable;
    private readonly int* frames;
    private readonly int* channels;
    private readonly int* spatialAudio;
    private long matchedBlocks, rejectedVoices;
    private int faulted, disposed;

    public SfxLoudness Loudness { get; } = new();
    public long MatchedBlocks => Interlocked.Read(ref matchedBlocks);
    public long RejectedVoices => Interlocked.Read(ref rejectedVoices);
    public bool Faulted => Volatile.Read(ref faulted) != 0;
    public bool LayoutSupported => GameAudioLayout.SampleCount(*frames, *channels, *spatialAudio, 0) != 0;

    // The validated executable returns zero in EAX; retain the original return value as well as its arguments.
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int RenderDelegate(GameAudioLayout.SourceVoice* voice, float* scratch);

    public SfxAudioCapture(IGameInteropProvider interop, ISigScanner scanner)
    {
        nint Unique(string signature)
        {
            var matches = scanner.ScanAllText(signature);
            if (matches.Length != 1) throw new NotSupportedException("SFX prototype does not recognize this game mixer version.");
            return matches[0];
        }
        nint Relative(string signature, int displacement, int nextInstruction)
        {
            var code = Unique(signature);
            var address = code + nextInstruction + Marshal.ReadInt32(code + displacement);
            var start = scanner.Module.BaseAddress;
            if (address < start || address > start + scanner.Module.ModuleMemorySize - 8)
                throw new NotSupportedException("SFX mixer address validation failed.");
            return address;
        }
        var function = Unique(RenderSignature);
        var end = scanner.Module.BaseAddress + scanner.Module.ModuleMemorySize;
        if (function > end - 0x2000) throw new NotSupportedException("SFX mixer function is outside its module.");
        var body = new ReadOnlySpan<byte>((void*)function, 0x2000);
        // Fail closed if the source layout changes despite retaining the function's prologue.
        if (body.IndexOf(new byte[] { 0x83, 0xbf, 0xa8, 0, 0, 0, 3 }) < 0 ||
            body.IndexOf(new byte[] { 0x48, 0x8b, 0x8f, 0xb0, 0, 0, 0 }) < 0 ||
            body.IndexOf(new byte[] { 0xf3, 0x0f, 0x10, 0x8f, 0x30, 5, 0, 0 }) < 0 ||
            body.IndexOf(new byte[] { 0xf3, 0x0f, 0x10, 0xbf, 0x38, 9, 0, 0 }) < 0)
            throw new NotSupportedException("SFX source-voice layout is unsupported.");
        callbackVTable = Relative(CallbackSignature, 7, 11);
        var firstCallback = Marshal.ReadIntPtr(callbackVTable);
        if (firstCallback < scanner.Module.BaseAddress || firstCallback >= end)
            throw new NotSupportedException("SFX callback type validation failed.");
        frames = (int*)Relative("48 63 1D ?? ?? ?? ?? E8 ?? ?? ?? ??", 3, 7);
        channels = (int*)Relative("89 15 ?? ?? ?? ?? 0F 11 05 ?? ?? ?? ??", 2, 6);
        spatialAudio = (int*)Relative("39 05 ?? ?? ?? ?? 48 89 5C 24 40", 2, 6);
        render = interop.HookFromAddress<RenderDelegate>(function, Render);
        try { render.Enable(); }
        catch { render.Dispose(); throw; }
    }

    private int Render(GameAudioLayout.SourceVoice* voice, float* scratch)
    {
        var count = 0;
        var generation = Loudness.Generation;
        // Classify while the game's callback/track links still describe this voice,
        // before the original can finish it and release its owner back into a pool.
        if ((generation & 1) != 0 && !Faulted && Volatile.Read(ref disposed) == 0)
        {
            try
            {
                if (scratch != null && GameAudioLayout.IsCombatVoice(voice, callbackVTable))
                    count = GameAudioLayout.SampleCount(*frames, *channels, *spatialAudio, voice->OutputFormat);
                else Interlocked.Increment(ref rejectedVoices);
            }
            catch { Fault(); }
        }

        // Always call playback exactly once, with the original pointers. No analysis exception
        // may skip this call, and none of the audio samples or native parameters are written.
        var result = render.Original(voice, scratch);
        if (count > 0 && Loudness.Active && !Faulted)
        {
            try
            {
                Loudness.Observe(new ReadOnlySpan<float>(scratch, count), generation);
                Interlocked.Increment(ref matchedBlocks);
            }
            catch { Fault(); }
        }
        return result;
    }

    private void Fault()
    {
        Interlocked.Exchange(ref faulted, 1);
        Loudness.SetActive(false); // Reporting is deferred to the UI; never log from the audio callback.
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        Loudness.SetActive(false);
        render.Dispose();
    }
}
