// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
namespace DualSenseLighting.Core;

// Copied motion fields consumed by the functional vehicle-motion filter.
internal readonly record struct SecondaryVectorSample(long Source, long Context, long Config,
    int Slot, bool Scoped, float X, float Y, float Z);
