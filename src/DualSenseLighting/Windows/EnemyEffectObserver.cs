// SPDX-License-Identifier: GPL-3.0-only
// Read-only scene VFX layout adapted from FFXIVClientStructs; see THIRD-PARTY-NOTICES.txt.
#nullable enable
using System;
using System.Runtime.InteropServices;
using System.Threading;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using DualSenseLighting.Core;

namespace DualSenseLighting.Windows;

/// <summary>Links delayed AVFX sounds to the exact skill effect that created them.</summary>
internal sealed unsafe class EnemyEffectObserver : IDisposable
{
    private readonly EnemyEffectOwnership effects = new();
    private readonly Hook<ConstructorDelegate> constructor;
    private readonly nint moduleStart, moduleEnd, searchBase, sceneTable, listenerTable;
    private int stopped;
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint ConstructorDelegate(nint effect);

    public EnemyEffectObserver(IGameInteropProvider interop, ISigScanner scanner)
    {
        moduleStart = scanner.Module.BaseAddress; moduleEnd = moduleStart + scanner.Module.ModuleMemorySize;
        searchBase = scanner.SearchBase;
        var matches = scanner.ScanAllText(GameAudioLayout.SceneVfxConstructorSignature);
        if (matches.Length != 1) throw new NotSupportedException("Delayed boss effect ownership is unsupported on this game build.");
        var function = matches[0];
        var code = GameAudioLayout.InspectionAddress(function, moduleStart, scanner.SearchBase, scanner.Module.ModuleMemorySize, 0x3B);
        if (code == 0) throw new NotSupportedException("Scene effect constructor validation failed.");
        sceneTable = function + 0x2A + Marshal.ReadInt32(code + 0x26);
        listenerTable = function + 0x34 + Marshal.ReadInt32(code + 0x30);
        if (sceneTable < moduleStart || sceneTable > moduleEnd - 16 || listenerTable < moduleStart || listenerTable > moduleEnd - 16)
            throw new NotSupportedException("Scene effect listener validation failed.");
        constructor = interop.HookFromAddress<ConstructorDelegate>(function, Construct);
        try { constructor.Enable(); }
        catch { constructor.Dispose(); throw; }
    }

    private nint Construct(nint effect)
    {
        var result = constructor.OriginalDisposeSafe(effect);
        // All scene effect creations invalidate a reused address, including idle,
        // basic-attack, player and unrelated effects. Playback is unchanged.
        if (result != 0) effects.Forget(result + 0x90);
        return result;
    }

    public void Observe(nint clip, GameAudioLayout.PlayerIdentity caster, EnemyActionTag tag)
    {
        if (Volatile.Read(ref stopped) != 0) return;
        var listener = GameAudioLayout.ClipEffectListener(clip, moduleStart, moduleEnd, sceneTable, listenerTable, searchBase);
        // Scene VfxObject.ResourceInstance is +0x2A0; listener is object +0x90.
        if (listener != 0) effects.Observe(listener, *(nint*)(listener + 0x210), caster, tag);
    }

    public EnemyActionTag Resolve(nint listener, nint table, GameAudioLayout.PlayerIdentity? caster) =>
        Volatile.Read(ref stopped) == 0 && listener != 0 && table == listenerTable
            ? effects.Resolve(listener, *(nint*)(listener + 0x210), caster) : default;

    public (nint Listener, nint Instance, EnemyActionTag Tag) Describe(nint clip, GameAudioLayout.PlayerIdentity caster)
    {
        var listener = GameAudioLayout.ClipEffectListener(clip, moduleStart, moduleEnd, sceneTable, listenerTable, searchBase);
        var instance = listener == 0 ? 0 : *(nint*)(listener + 0x210);
        return (listener, instance, effects.Resolve(listener, instance, caster));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref stopped, 1) == 0) constructor.Dispose();
    }
}
