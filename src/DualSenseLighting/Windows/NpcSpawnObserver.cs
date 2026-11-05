// SPDX-License-Identifier: GPL-3.0-only
// Packet signature adapted from FFXIVClientStructs; see THIRD-PARTY-NOTICES.txt.
#nullable enable
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using DualSenseLighting.Core;

namespace DualSenseLighting.Windows;

/// <summary>Passive NPC spawn diagnostics, enabled only during an explicit SFX trace.</summary>
internal sealed class NpcSpawnObserver : IDisposable
{
    private const string Signature = "48 89 5C 24 ?? 57 48 81 EC ?? ?? ?? ?? 48 8B DA 8B F9 E8 ?? ?? ?? ?? 3C ?? 75 ?? E8 ?? ?? ?? ?? 3C ?? 75 ?? 80 BB ?? ?? ?? ?? ?? 75 ?? 8B 05 ?? ?? ?? ?? 39 43 ?? 0F 85 ?? ?? ?? ?? 0F B6 53 ?? 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 0F B6 53 ?? 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 8D 44 24 ?? C7 44 24 ?? ?? ?? ?? ?? BA ?? ?? ?? ?? 66 90 48 8D 80 ?? ?? ?? ?? ?? ?? ?? 0F 10 4B ?? 48 8D 9B ?? ?? ?? ?? 0F 11 40 ?? 0F 10 43 ?? 0F 11 48 ?? 0F 10 4B ?? 0F 11 40 ?? 0F 10 43 ?? 0F 11 48 ?? 0F 10 4B ?? 0F 11 40 ?? 0F 10 43 ?? 0F 11 48 ?? 0F 10 4B ?? 0F 11 40 ?? 0F 11 48 ?? 48 83 EA ?? 75 ?? ?? ?? ?? 4C 8D 44 24";
    private readonly Hook<SpawnDelegate> hook;
    public sealed record Spawn(double Seconds, NpcSpawnMetadata Metadata);
    private sealed class Session
    {
        public readonly long Started = Stopwatch.GetTimestamp();
        public readonly ConcurrentDictionary<uint, Spawn> Spawns = new();
        public int Active = 1;
    }
    private Session? session;
    private int disposed;
    public string? Error { get; private set; }
    public Spawn[] Snapshot() => Volatile.Read(ref session)?.Spawns.Values.ToArray() ?? Array.Empty<Spawn>();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void SpawnDelegate(uint entityId, nint packet);
    [DllImport("kernel32.dll")]
    private static extern bool ReadProcessMemory(nint process, nint address, byte[] data, nuint size, out nuint read);

    public NpcSpawnObserver(IGameInteropProvider interop, ISigScanner scanner)
    {
        var matches = scanner.ScanAllText(Signature);
        if (matches.Length != 1) throw new NotSupportedException("NPC spawn diagnostics do not recognize this game build.");
        // This handler enqueues a complete 0x290-byte NPC packet. It neither
        // receives a summoning boss argument nor creates the actor synchronously.
        hook = interop.HookFromAddress<SpawnDelegate>(matches[0], Observe);
    }

    public void SetTracing(bool active)
    {
        if (Volatile.Read(ref disposed) != 0) return;
        try
        {
            if (active && Error == null)
            {
                Volatile.Write(ref session, new Session());
                if (!hook.IsEnabled) hook.Enable();
            }
            else
            {
                var current = Volatile.Read(ref session);
                if (current != null) Volatile.Write(ref current.Active, 0);
                if (hook.IsEnabled) hook.Disable();
            }
        }
        catch (Exception error) { Error = error.Message; }
    }

    private void Observe(uint entityId, nint packet)
    {
        try
        {
            var current = Volatile.Read(ref session);
            if (current != null && Volatile.Read(ref current.Active) != 0 && current.Spawns.Count < 256 && packet != 0)
            {
                var bytes = new byte[NpcSpawnMetadata.PacketSize];
                if (ReadProcessMemory(-1, packet, bytes, (nuint)bytes.Length, out var read) && read == (nuint)bytes.Length)
                {
                    var metadata = NpcSpawnMetadata.Read(entityId, bytes);
                    if (metadata != null) current.Spawns[entityId] = new(Stopwatch.GetElapsedTime(current.Started).TotalSeconds, metadata);
                }
            }
        }
        catch (Exception error) { Error = error.Message; }
        // Observation errors cannot skip, repeat or alter the game's spawn packet.
        hook.OriginalDisposeSafe(entityId, packet);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        var current = Volatile.Read(ref session);
        if (current != null) Volatile.Write(ref current.Active, 0);
        hook.Dispose();
    }
}
