// SPDX-License-Identifier: MIT
// Minimal read-only fields adapted from FFXIVClientStructs SpawnPackets.cs.
#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace DualSenseLighting.Core;

/// <summary>Diagnostic evidence only. Tethers, families and event IDs are not ownership.</summary>
public sealed record NpcSpawnMetadata(uint EntityId, uint BaseId, uint NameId, uint OwnerId, uint LayoutId,
    uint EventId, uint GimmickId, uint TetherTargetId, ushort TetherId, byte MaxLinks, byte LinkFamily,
    byte LinkRange, byte SubKind, byte Battalion, NpcSpawnMetadata.StatusSource[] StatusSources)
{
    public const int PacketSize = 0x290;
    public readonly record struct StatusSource(ushort StatusId, uint SourceEntityId);

    public static NpcSpawnMetadata? Read(uint entityId, ReadOnlySpan<byte> packet)
    {
        if (entityId is 0 or 0xE0000000 || packet.Length != PacketSize || packet[0x81] != 2) return null;
        var sources = new List<StatusSource>();
        for (var i = 0; i < 30; i++)
        {
            var status = packet.Slice(0x98 + i * 12, 12);
            var id = BinaryPrimitives.ReadUInt16LittleEndian(status);
            var source = BinaryPrimitives.ReadUInt32LittleEndian(status[8..]);
            if (id != 0 && source is not (0 or 0xE0000000)) sources.Add(new(id, source));
        }
        return new(entityId, U32(packet, 0x40), U32(packet, 0x44), U32(packet, 0x54), U32(packet, 0x48),
            U32(packet, 0x50), U32(packet, 0), U32(packet, 0x58), BinaryPrimitives.ReadUInt16LittleEndian(packet[0x7C..]),
            packet[0x0B], packet[0x0C], packet[0x0D], packet[0x82], packet[0x85], sources.ToArray());
    }

    private static uint U32(ReadOnlySpan<byte> packet, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(packet[offset..]);
}
