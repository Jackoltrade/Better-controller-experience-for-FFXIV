// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Buffers.Binary;
using DualSenseLighting.Core;

internal static class NpcSpawnMetadataChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var bytes = new byte[NpcSpawnMetadata.PacketSize]; bytes[0x81] = 2;
        void Set(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
        Set(0x40, 11762); Set(0x44, 9510); Set(0x48, 300); Set(0x50, 400);
        Set(0, 500); Set(0x54, 0xE0000000); Set(0x58, 600);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x7C), 700);
        bytes[0x0B] = 1; bytes[0x0C] = 2; bytes[0x0D] = 3; bytes[0x82] = 5; bytes[0x85] = 4;
        var row = NpcSpawnMetadata.Read(100, bytes)!;
        check(row.EntityId == 100 && row.BaseId == 11762 && row.NameId == 9510 && row.LayoutId == 300 &&
            row.EventId == 400 && row.GimmickId == 500 && row.SubKind == 5 && row.Battalion == 4,
            "Spawn metadata fields did not follow the pinned NPC packet layout");
        check(row.OwnerId == 0xE0000000 && row.TetherTargetId == 600 && row.TetherId == 700 &&
            row.MaxLinks == 1 && row.LinkFamily == 2 && row.LinkRange == 3,
            "A tether, aggro family or event/layout ID was confused with native ownership");
        Set(0x54, 0); row = NpcSpawnMetadata.Read(100, bytes)!;
        check(row.OwnerId == 0, "An ownerless spawn received a guessed owner from other relationships");
        Set(0x54, 800); row = NpcSpawnMetadata.Read(100, bytes)!;
        check(row.OwnerId == 800 && row.TetherTargetId == 600, "An explicit owner was replaced with a tether target");
        check(row.StatusSources.Length == 0, "Empty spawn statuses acquired fabricated sources");
        // Only active status entries with an actual source ID are useful evidence.
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x98), 123); Set(0xA0, 900);
        Set(0xAC, 901); // second slot has a source but no status.
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0xB0), 124); Set(0xB8, 0xE0000000);
        var last = 0x98 + 29 * 12;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(last), 125); Set(last + 8, 902);
        row = NpcSpawnMetadata.Read(100, bytes)!;
        check(row.StatusSources.Length == 2 && row.StatusSources[0] == new NpcSpawnMetadata.StatusSource(123, 900) &&
            row.StatusSources[1] == new NpcSpawnMetadata.StatusSource(125, 902) && row.OwnerId == 800,
            "Spawn status sources were misread, truncated or treated as an owner");
        check(NpcSpawnMetadata.Read(0, bytes) == null && NpcSpawnMetadata.Read(0xE0000000, bytes) == null,
            "Spawn diagnostics accepted an invalid native entity ID");
        foreach (var kind in new byte[] { 0, 1, 3, 4, 8, 9, 15, 255 })
        {
            bytes[0x81] = kind;
            check(NpcSpawnMetadata.Read(100, bytes) == null, "Non-NPC spawn data entered the summon investigation");
        }
        bytes[0x81] = 2;
        check(NpcSpawnMetadata.Read(100, bytes.AsSpan(0, bytes.Length - 1)) == null &&
            NpcSpawnMetadata.Read(100, ReadOnlySpan<byte>.Empty) == null &&
            NpcSpawnMetadata.Read(100, new byte[NpcSpawnMetadata.PacketSize + 1]) == null,
            "Spawn diagnostics parsed a truncated or differently sized packet");
    }
}
