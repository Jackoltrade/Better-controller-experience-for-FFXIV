// SPDX-License-Identifier: GPL-3.0-only
// Read-only action/timeline bindings adapted from FFXIVClientStructs; see THIRD-PARTY-NOTICES.txt.
#nullable enable
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Text;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using DualSenseLighting.Core;
using Lumina.Excel.Sheets;

namespace DualSenseLighting.Windows;

/// <summary>Tags sounds with confirmed action evidence, never a recent-action timing window.</summary>
internal sealed unsafe class EnemySkillFilter : IDisposable
{
    private readonly EnemySkillCatalog catalog;
    private readonly GameAudioLayout.PlayerSoundOwnership ownership;
    private readonly Hook<ReceiveDelegate> receive;
    private readonly nint moduleStart, moduleEnd;
    private readonly nint actorVtable;
    private readonly bool casterOriginSupported;
    public string? ClipBindingError { get; }
    private readonly EnemyEffectObserver? effects;
    public string? EffectBindingError { get; }
    public bool EffectBindingSupported => Ready && effects != null;
    private int faulted, disposed;
    private long skills, autoAttacks, unknown;
    public long Skills => Interlocked.Read(ref skills);
    public long AutoAttacks => Interlocked.Read(ref autoAttacks);
    public long UnknownActions => Interlocked.Read(ref unknown);
    private readonly ConcurrentDictionary<string, byte> recordedKeys = new(StringComparer.Ordinal);
    public sealed record EffectOwner(uint GetterRva, string Actor, string Node, string Intermediate, string Listener,
        byte[] NodeHeader, byte[] InstanceHeader, byte[] ListenerHeader, byte[] SoundData);
    private readonly ConcurrentDictionary<(nint, nint), EffectOwner> effectOwners = new();
    public EffectOwner[] EffectOwners => effectOwners.Values.ToArray();
    public sealed record EffectClip(uint VtableRva, uint Type, string Actor, string Clip, EnemyActionTag Tag, uint[] Methods,
        string Listener, string Instance, EnemyActionTag EffectTag);
    private readonly ConcurrentDictionary<nint, EffectClip> effectClips = new();
    public EffectClip[] EffectClips => effectClips.Values.ToArray();
    public string[] RecordedKeys => recordedKeys.Keys.ToArray();
    public void ClearRecordedKeys() { recordedKeys.Clear(); effectOwners.Clear(); effectClips.Clear(); }

    public void RecordEffectClip(nint clip, GameAudioLayout.PlayerIdentity? caster, EnemyActionTag tag)
    {
        if (caster == null || ownership.ActorOf(caster)?.Kind != EnemySoundKind.Boss) return;
        var link = effects?.Describe(clip, caster) ?? default;
        if (effectClips.TryGetValue(clip, out var existing))
        {
            // Keep the usable link observed after native initialization, rather
            // than freezing the first query while the container is still empty.
            if (link.Tag.IsReleasedEffect && (!existing.EffectTag.IsReleasedEffect || existing.Tag != tag))
                effectClips[clip] = existing with { Tag = tag, Listener = $"{link.Listener:X}", Instance = $"{link.Instance:X}", EffectTag = link.Tag };
            return;
        }
        if (effectClips.Count >= 32) return;
        var header = ReadMetadata(clip, 0x98); if (header == null) return;
        var data = (nint)BitConverter.ToInt64(header, 0x48); var entry = ReadMetadata(data - 8, 8);
        if (data == 0 || entry == null || BitConverter.ToUInt32(entry) != 0x32313043 || BitConverter.ToInt32(entry, 4) != 0x48) return;
        var table = (nint)BitConverter.ToInt64(header);
        if (table < moduleStart || table > moduleEnd - 63 * 8) return;
        var bytes = ReadMetadata(table, 63 * 8); if (bytes == null) return;
        var methods = new uint[63];
        for (var i = 0; i < methods.Length; i++)
        {
            var at = (nint)BitConverter.ToInt64(bytes, i * 8);
            if (at >= moduleStart && at < moduleEnd) methods[i] = (uint)(at - moduleStart);
        }
        effectClips.TryAdd(clip, new((uint)(table - moduleStart), BitConverter.ToUInt32(header, 0x84),
            $"{caster.Actor:X}", $"{clip:X}", tag, methods, $"{link.Listener:X}", $"{link.Instance:X}", link.Tag));
    }

    public void RecordEffectOwner(GameAudioLayout.PlayerIdentity? caster, nint node, nint instance, nint listener, uint getter)
    {
        // Explicit trace only: bounded metadata snapshots while these native objects
        // are alive. No sample data, hooks or reads through speculative pointers.
        if (ownership.ActorOf(caster)?.Kind != EnemySoundKind.Boss || effectOwners.Count >= 64 ||
            effectOwners.ContainsKey((instance, node))) return;
        var nodeHeader = ReadMetadata(node, 0x260);
        var instanceHeader = ReadMetadata(instance, 0x300); var listenerHeader = ReadMetadata(listener, 0x80);
        if (nodeHeader == null || instanceHeader == null || listenerHeader == null) return;
        var sound = ReadMetadata((nint)BitConverter.ToInt64(nodeHeader, 0x238), 0x40);
        effectOwners.TryAdd((instance, node), new(getter, $"{caster?.Actor:X}", $"{node:X}", $"{instance:X}", $"{listener:X}",
            nodeHeader, instanceHeader, listenerHeader, sound ?? Array.Empty<byte>()));
    }

    private static byte[]? ReadMetadata(nint address, int size)
    {
        if (address == 0) return null;
        var bytes = new byte[size];
        fixed (byte* data = bytes)
            return ReadProcessMemory(-1, address, data, (nuint)size, out var read) && read == (nuint)size ? bytes : null;
    }
    private readonly record struct ActionScope(GameAudioLayout.PlayerIdentity? Caster, EnemyActionTag Tag,
        GameAudioLayout.PlayerIdentity? HitPlayer = null);
    [ThreadStatic] private static ActionScope currentAction;

    // Diagnostics only: exact synchronous action context at a native sound
    // acquisition. It is evidence to inspect, never a fallback audio owner.
    public (nint Actor, uint Entity, EnemyActionTag Action) DiagnosticScope
    {
        get
        {
            var scope = currentAction;
            var source = Ready ? ownership.ActorOf(scope.Caster) : null;
            return source is { } actor ? (actor.Actor, actor.EntityId, scope.Tag) : default;
        }
    }
    public bool Ready => Volatile.Read(ref faulted) == 0 && Volatile.Read(ref disposed) == 0;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ReceiveDelegate(uint casterEntityId, nint caster, nint targetPosition,
        LimitBreakActionHeader* header, nint effects, nint targetIds);
    [DllImport("kernel32.dll")]
    private static extern bool ReadProcessMemory(nint process, nint address, byte* buffer, nuint size, out nuint read);

    public EnemySkillFilter(IGameInteropProvider interop, ISigScanner scanner, IDataManager data,
        GameAudioLayout.PlayerSoundOwnership ownership)
    {
        this.ownership = ownership;
        var casts = data.GetExcelSheet<ActionCastTimeline>();
        catalog = new EnemySkillCatalog(data.GetExcelSheet<Lumina.Excel.Sheets.Action>().Select(row =>
            new EnemyActionDefinition(row.RowId, row.ActionCategory.RowId, row.AnimationEnd.RowId, row.ActionTimelineHit.RowId,
                casts.GetRowOrDefault(row.AnimationStart.RowId)?.Name.RowId ?? 0, row.AttackType.RowId)),
            data.GetExcelSheet<ActionTimeline>().Select(row => new EnemyTimelineDefinition(row.RowId, row.Key.ToString())));
        moduleStart = scanner.Module.BaseAddress; moduleEnd = moduleStart + scanner.Module.ModuleMemorySize;
        var actors = scanner.ScanAllText(GameAudioLayout.SchedulerActorSignature);
        if (actors.Length == 1)
        {
            var code = GameAudioLayout.InspectionAddress(actors[0], moduleStart, scanner.SearchBase, scanner.Module.ModuleMemorySize, 0x50);
            if (code != 0 && GameAudioLayout.ActorBindingLayout(new ReadOnlySpan<byte>((void*)code, 0x50)))
            {
                var table = actors[0] + 16 + Marshal.ReadInt32(code + 12);
                if (table >= moduleStart && table < moduleEnd - 8) actorVtable = table;
            }
        }
        var positions = scanner.ScanAllText(GameAudioLayout.ClipPositionSignature);
        if (positions.Length == 1)
        {
            var code = GameAudioLayout.InspectionAddress(positions[0], moduleStart, scanner.SearchBase, scanner.Module.ModuleMemorySize, 0x110);
            casterOriginSupported = code != 0 && GameAudioLayout.ClipPositionLayout(new ReadOnlySpan<byte>((void*)code, 0x110));
        }
        // Optional extension: unsupported clip bindings must not disable proven
        // mechanic-emitter, regular-enemy or player hit-reaction ownership.
        if (actorVtable == 0) ClipBindingError = "Boss attack sound binding layout is unsupported; embedded attack tracks are skipped.";
        else if (!casterOriginSupported) ClipBindingError = "Caster-origin skill sound layout is unsupported; those tracks are skipped.";
        var matches = scanner.ScanAllText(GameAudioLayout.ActionEffectSignature);
        if (matches.Length != 1) throw new NotSupportedException("Enemy skill action observation does not recognize this game build.");
        var call = GameAudioLayout.InspectionAddress(matches[0], moduleStart, scanner.SearchBase, scanner.Module.ModuleMemorySize, 5);
        if (call == 0) throw new NotSupportedException("Enemy skill action address validation failed.");
        var function = matches[0] + 5 + Marshal.ReadInt32(call + 1);
        var original = GameAudioLayout.InspectionAddress(function, moduleStart, scanner.SearchBase, scanner.Module.ModuleMemorySize, 0x90);
        if (original == 0 || !GameAudioLayout.ActionEffectLayout(new ReadOnlySpan<byte>((void*)original, 0x90)))
            throw new NotSupportedException("Enemy skill action layout validation failed.");
        receive = interop.HookFromAddress<ReceiveDelegate>(function, Receive);
        try { receive.Enable(); }
        catch { receive.Dispose(); throw; }
        // Optional extension: retain the proven sound/helper paths if this
        // client's scene-effect layout cannot be validated.
        try { effects = new EnemyEffectObserver(interop, scanner); }
        catch (Exception error) { EffectBindingError = error.Message; }
    }

    public void ObserveEffect(nint clip, GameAudioLayout.PlayerIdentity? caster, EnemyActionTag timeline)
    {
        if (Ready && caster != null && ownership.ActorOf(caster)?.Kind == EnemySoundKind.Boss)
            effects?.Observe(clip, caster, timeline);
    }

    public EnemyActionTag EffectTag(nint listener, nint table, GameAudioLayout.PlayerIdentity? caster) =>
        Ready && caster != null && ownership.ActorOf(caster)?.Kind == EnemySoundKind.Boss
            ? effects?.Resolve(listener, table, caster) ?? default : default;

    public EnemyActionTag Classify(GameAudioLayout.PlayerIdentity? caster, EnemyActionTag timeline, byte origin)
    {
        if (!Ready || caster == null) return default;
        // A sound synchronously created by this exact action has stronger evidence
        // than a timeline shared by basic attacks and skills. Other actors cannot inherit it.
        var scope = currentAction;
        if (ReferenceEquals(scope.HitPlayer, caster)) return new(EnemyActionKind.HitReaction, scope.Tag.ActionId);
        if (ReferenceEquals(caster, ownership.Player)) return timeline.IsHitReaction ? timeline : default;
        var source = ownership.ActorOf(caster);
        return source.HasValue ? catalog.ClassifySound(source.Value, caster, origin,
            ReferenceEquals(scope.Caster, caster) ? scope.Tag : default, timeline) : default;
    }

    public void ObserveCast(nint actor, uint entityId, uint actionId, byte actionType)
    {
        if (!Ready || actionId == 0) return;
        var identity = ownership.ResolveCaster(actor, 0);
        if (identity != null && ownership.ActorOf(identity).HasValue)
            catalog.ObserveCast(identity, entityId, actionId, actionType);
    }

    public EnemyActionTag Timeline(nint timeline, nint table, GameAudioLayout.PlayerIdentity caster, bool tracing, nint clip = 0)
    {
        if (!Ready || timeline == 0 || table < moduleStart || table > moduleEnd - 30 * 8) return default;
        // The pinned SchedulerTimeline vfunc 21 is exactly `return this->ActionTimelineKey`.
        // Validate its concrete getter before reading the field. RPM bounds the string read.
        var function = *(nint*)(table + 21 * 8);
        if (function < moduleStart || function > moduleEnd - 8 || *(ulong*)function != 0xC3000000A8818B48UL) return default;
        var key = *(nint*)(timeline + 0xA8);
        if (key == 0) return default;
        var bytes = stackalloc byte[256];
        if (!ReadProcessMemory(-1, key, bytes, 256, out var read) || read != 256) return default;
        var text = new ReadOnlySpan<byte>(bytes, 256);
        var length = text.IndexOf((byte)0);
        if (length <= 0) return default;
        var player = ReferenceEquals(caster, ownership.Player);
        if (tracing && recordedKeys.Count < 64)
            recordedKeys.TryAdd((player ? "player:" : "enemy:") + Encoding.UTF8.GetString(text[..length]), 0);
        if (player) return catalog.PlayerTimelineKey(text[..length]);
        var tag = catalog.TimelineKey(text[..length], caster);
        var source = ownership.ActorOf(caster);
        return tag.IsSkill && source is { Kind: EnemySoundKind.Boss }
            ? EnemySkillCatalog.TimelineSound(tag, GameAudioLayout.ClipSoundBinding(clip, actorVtable, casterOriginSupported)) : tag;
    }

    private void Receive(uint casterEntityId, nint caster, nint targetPosition, LimitBreakActionHeader* header, nint effects, nint targetIds)
    {
        var previous = currentAction;
        currentAction = default;
        try
        {
            try
            {
                var identity = Ready && header != null ? ownership.ResolveCaster(caster, 0) : null;
                if (identity != null && identity.EntityId == casterEntityId)
                {
                    currentAction = new(identity, catalog.Action(header->ActionId, header->ActionType));
                    if (!ReferenceEquals(identity, ownership.Player))
                    {
                        identity.ObserveAction(header->ActionId);
                        var player = ownership.Player;
                        var count = header->NumTargets;
                        if (player.Actor != 0 && effects != 0 && targetIds != 0 && count is >= 1 and <= 32 &&
                            GameAudioLayout.PlayerSoundOwnership.HitsPlayer(player.EntityId,
                                new ReadOnlySpan<ulong>((void*)targetIds, count), new ReadOnlySpan<byte>((void*)effects, count * 64)))
                            currentAction = currentAction with { HitPlayer = player };
                        if (currentAction.Tag.Kind == EnemyActionKind.Skill) Interlocked.Increment(ref skills);
                        else if (currentAction.Tag.Kind == EnemyActionKind.AutoAttack) Interlocked.Increment(ref autoAttacks);
                        else Interlocked.Increment(ref unknown);
                    }
                }
            }
            catch { Interlocked.Exchange(ref faulted, 1); }
            receive.OriginalDisposeSafe(casterEntityId, caster, targetPosition, header, effects, targetIds);
        }
        finally { currentAction = previous; }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0) { effects?.Dispose(); receive.Dispose(); }
    }
}
