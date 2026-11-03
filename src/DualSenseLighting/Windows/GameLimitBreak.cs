// SPDX-License-Identifier: GPL-3.0-only
// Native signatures/layout adapted from FFXIVClientStructs; see THIRD-PARTY-NOTICES.txt.
#nullable enable
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using DualSenseLighting.Core;
using ActionRow = Lumina.Excel.Sheets.Action;

namespace DualSenseLighting.Windows;

/// <summary>Read-only gauge/cast sampling; observes confirmed actions without changing their processing.</summary>
internal sealed unsafe class GameLimitBreak : IDisposable
{
    private const string GaugeSignature = "44 8B C7 48 8D 0D ?? ?? ?? ?? 48 8B D3";
    private const string EffectSignature = "E8 ?? ?? ?? ?? 48 8B 8D ?? ?? ?? ?? 48 33 CC E8 ?? ?? ?? ?? 48 81 C4 00 05 00 00";
    private readonly nint gauge;
    private readonly LimitBreakTracker tracker;
    private readonly Func<double> seconds;
    private readonly Hook<ReceiveDelegate>? receive;
    private long errors;
    public string? InstantFlashError { get; }
    public long Errors => Interlocked.Read(ref errors);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ReceiveDelegate(uint casterEntityId, nint caster, nint targetPosition,
        LimitBreakActionHeader* header, nint effects, nint targetIds);

    public GameLimitBreak(IGameInteropProvider interop, ISigScanner scanner, IDataManager data, Func<double> seconds)
    {
        this.seconds = seconds;
        // Resolve localized game data once, outside the native observer and framework update.
        var actions = data.GetExcelSheet<ActionRow>().Where(row => row.ActionCategory.RowId is 9 or 15)
            .ToDictionary(row => row.RowId, row => row.ActionCategory.RowId == 15);
        if (actions.Count == 0) throw new NotSupportedException("Limit Break actions are unavailable in this game data.");
        tracker = new LimitBreakTracker(actions);

        nint Relative(string signature, int displacement, int nextInstruction, int size)
        {
            var matches = scanner.ScanAllText(signature);
            if (matches.Length != 1) throw new NotSupportedException("Limit Break detection does not recognize this game build.");
            var address = matches[0] + nextInstruction + Marshal.ReadInt32(matches[0] + displacement);
            var start = scanner.Module.BaseAddress;
            if (address < start || address > start + scanner.Module.ModuleMemorySize - size)
                throw new NotSupportedException("Limit Break address validation failed.");
            return address;
        }
        gauge = Relative(GaugeSignature, 6, 10, 0x10);
        Hook<ReceiveDelegate>? hook = null;
        try
        {
            var function = Relative(EffectSignature, 1, 5, 0x90);
            var body = new ReadOnlySpan<byte>((void*)function, 0x90);
            if (!body.StartsWith(new byte[] { 0x40, 0x55, 0x53, 0x56, 0x57, 0x41, 0x54 }) ||
                body.IndexOf(new byte[] { 0x41, 0x0f, 0xb7, 0x49, 0x1c }) < 0)
                throw new NotSupportedException("Instant Limit Break observation is unsupported on this game build.");
            hook = interop.HookFromAddress<ReceiveDelegate>(function, Receive);
            receive = hook;
            hook.Enable();
        }
        catch (Exception error)
        {
            hook?.Dispose();
            receive = null;
            InstantFlashError = error.Message; // Gauge/cast support remains available.
        }
    }

    // Only called on Dalamud's framework thread; callbacks see immutable actor identity.
    public LimitBreakState Read(IPlayerCharacter? player, uint territory, bool pvp, double time, bool active)
    {
        var value = *(LimitBreakGauge*)gauge;
        var available = active && player is not null && value.Valid && (value.IsPvP != 0) == pvp;
        tracker.SetActor(player?.Address ?? 0, player?.EntityId ?? 0, territory, player?.ClassJob.RowId ?? 0, pvp, available);
        if (!available) return LimitBreakState.Empty;
        return tracker.Sample(value, player!.IsCasting, player.CastActionType, player.CastActionId, time);
    }

    private void Receive(uint casterEntityId, nint caster, nint targetPosition, LimitBreakActionHeader* header,
        nint effects, nint targetIds)
    {
        try
        {
            if (header != null)
                tracker.ObserveExecution(casterEntityId, header->ActionId, header->ActionType, header->GlobalSequence, seconds());
        }
        catch { Interlocked.Increment(ref errors); }
        receive!.Original(casterEntityId, caster, targetPosition, header, effects, targetIds);
    }

    public void Dispose()
    {
        tracker.SetActor(0, 0, 0, 0, false, false);
        receive?.Dispose();
    }
}
