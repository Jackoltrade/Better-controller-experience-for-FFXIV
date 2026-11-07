// SPDX-License-Identifier: GPL-3.0-only
// Native layouts adapted from FFXIVClientStructs; see THIRD-PARTY-NOTICES.txt.
#nullable enable
using System;
using System.Runtime.InteropServices;

namespace DualSenseLighting.Core;

public enum PlayerIndicatorMode { Animation, LimitBreak }

public sealed record LimitBreakState(bool IsPvP, byte BarCount, ushort CurrentUnits, ushort BarUnits,
    double UpdatedAt, bool Casting = false, double FlashStartedAt = 0, double FlashUntil = 0)
{
    public static LimitBreakState Empty { get; } = new(false, 0, 0, 0, double.NegativeInfinity);
    public bool Available => BarCount is >= 1 and <= 3 && BarUnits > 0 && (!IsPvP || BarCount == 1);
    public int ReadyBars => Available ? Math.Min(CurrentUnits / BarUnits, BarCount) : 0;
    public int ReadyQuarters => Available ? Math.Min(CurrentUnits * 4 / BarUnits, 4) : 0;
    public bool IsFresh(double seconds) => Available && double.IsFinite(seconds) &&
        double.IsFinite(UpdatedAt) && seconds >= UpdatedAt - 0.05 && seconds - UpdatedAt <= 0.75;
    public bool IsFlashing(double seconds) => IsFresh(seconds) &&
        (Casting || seconds >= FlashStartedAt && seconds < FlashUntil);
}

[StructLayout(LayoutKind.Explicit, Size = 0x10)]
public struct LimitBreakGauge
{
    [FieldOffset(0x08)] public byte BarCount;
    [FieldOffset(0x0A)] public ushort CurrentUnits;
    [FieldOffset(0x0C)] public ushort BarUnits;
    [FieldOffset(0x0F)] public byte IsPvP;

    public readonly bool Valid => BarCount is >= 1 and <= 3 && BarUnits > 0 &&
        IsPvP <= 1 && (IsPvP == 0 || BarCount == 1);
}

[StructLayout(LayoutKind.Explicit, Size = 0x28)]
public struct LimitBreakActionHeader
{
    [FieldOffset(0x00)] public ulong AnimationTargetId;
    [FieldOffset(0x08)] public uint ActionId;
    [FieldOffset(0x0C)] public uint GlobalSequence;
    [FieldOffset(0x1F)] public byte ActionType;
    [FieldOffset(0x21)] public byte NumTargets;
}
