// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System.Numerics;

namespace DualSenseLighting.Core;

/// <summary>A live non-looping arena VFX sound and its exact layout position.</summary>
public readonly record struct ArenaEffectSource(int SoundId, Vector3 Position, string Resource, uint SoundNumber,
    int PoolSlot, string SoundData, string SoundLayout, uint InstanceId, uint SubId);
