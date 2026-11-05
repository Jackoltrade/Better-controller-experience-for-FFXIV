// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.InteropServices;
using DualSenseLighting.Core;

internal static unsafe class GameObjectLayoutChecks
{
    internal static void Run(Action<bool, string> check)
    {
        Span<byte> bytes = stackalloc byte[GameAudioLayout.GameObjectSize];
        bytes.Fill(0xAB);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[GameAudioLayout.ActorEntityIdOffset..], 42);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[GameAudioLayout.ActorLayoutIdOffset..], 0xCAFEBABE);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[GameAudioLayout.ActorEventIdOffset..], 0x12345678);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[GameAudioLayout.ActorFateIdOffset..], 0x3456);
        BinaryPrimitives.WriteInt64LittleEndian(bytes[GameAudioLayout.ActorDrawObjectOffset..], 0x1234567887654321);
        for (var length = 0; length < GameAudioLayout.RequiredActorBytes; length++)
            check(!GameAudioLayout.TryReadActor(bytes[..length], 42, out var missing) && missing == default,
                "Truncated actor metadata was read or returned stale fields");
        check(GameAudioLayout.TryReadActor(bytes[..GameAudioLayout.RequiredActorBytes], 42, out var fields) &&
            fields.DrawObject == unchecked((nint)0x1234567887654321) && fields.FateId == 0x3456 &&
            fields.EventId == 0x12345678 && fields.LayoutId == 0xCAFEBABE,
            "Actor metadata offsets or native field widths changed");
        foreach (var expected in new uint[] { 0, 0xE0000000, 43 })
            check(!GameAudioLayout.TryReadActor(bytes, expected, out fields) && fields == default,
                "Actor metadata accepted invalid or stale identity");
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[GameAudioLayout.ActorEntityIdOffset..], 43);
        check(!GameAudioLayout.TryReadActor(bytes, 42, out _) && GameAudioLayout.TryReadActor(bytes, 43, out _),
            "Actor address reuse retained the old entity identity");
        check(!GameAudioLayout.TryReadActor(0, GameAudioLayout.GameObjectSize, 42, out fields) && fields == default,
            "Null actor pointer was dereferenced");
        fixed (byte* pointer = bytes)
        {
            check(!GameAudioLayout.TryReadActor((nint)pointer, GameAudioLayout.RequiredActorBytes - 1, 43, out _),
                "Pointer reader ignored its declared object extent");
            check(GameAudioLayout.TryReadActor((nint)pointer, bytes.Length, 43, out _), "Pointer and span readers disagree");
        }
        bytes.Slice(GameAudioLayout.ActorDrawObjectOffset, 8).Clear();
        check(GameAudioLayout.TryReadActor(bytes, 43, out fields) && fields.DrawObject == 0,
            "A null draw-object pointer incorrectly invalidated a live actor identity");
    }

    internal static void CheckInstalledLayout(Action<bool, string> check, Assembly plugin)
    {
        var actor = Type.GetType("FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject, FFXIVClientStructs", true)!;
        check(actor.StructLayoutAttribute!.Size == GameAudioLayout.GameObjectSize, "Installed SDK GameObject size changed");
        void Field(string name, int offset, int width)
        {
            var field = actor.GetField(name)!;
            var actual = field.GetCustomAttribute<FieldOffsetAttribute>()!.Value;
            var size = field.FieldType.IsPointer ? IntPtr.Size : Marshal.SizeOf(field.FieldType);
            check(actual == offset && size == width, $"Installed SDK {name} layout differs: offset {actual:X}, size {size}");
        }
        Field("EntityId", GameAudioLayout.ActorEntityIdOffset, 4);
        Field("LayoutId", GameAudioLayout.ActorLayoutIdOffset, 4);
        Field("EventId", GameAudioLayout.ActorEventIdOffset, 4);
        Field("FateId", GameAudioLayout.ActorFateIdOffset, 2);
        Field("DrawObject", GameAudioLayout.ActorDrawObjectOffset, 8);
        var api = Type.GetType("Dalamud.Game.ClientState.Objects.Types.IGameObject, Dalamud", true)!;
        check(api.GetProperty("OwnerId")?.PropertyType == typeof(uint), "Installed Dalamud no longer exposes typed OwnerId");
    }
}
