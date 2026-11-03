// SPDX-License-Identifier: GPL-3.0-only
// Camera fields/signatures adapted from FFXIVClientStructs; see THIRD-PARTY-NOTICES.txt.
#nullable enable
using System;
using System.Diagnostics;
using System.Numerics;
using DualSenseLighting.Core;

namespace DualSenseLighting.Windows;

internal sealed unsafe class GameHapticCamera
{
    private readonly nint manager, getActive;
    public GameHapticCamera(nint manager, nint getActive) { this.manager = manager; this.getActive = getActive; }

    // Framework thread only. Dalamud supplies the character origin; the camera
    // supplies orientation and the optional camera-distance reference.
    public HapticListenerPose? ReadListener(Vector3 characterPosition)
    {
        var camera = ((delegate* unmanaged<nint, nint>)getActive)(manager);
        if (camera == 0) return null;
        return HapticListenerPose.FromCamera(characterPosition, *(Vector3*)(camera + 0x60),
            *(Matrix4x4*)(camera + 0xB0), Stopwatch.GetTimestamp());
    }

    public static float[] StereoMatrix(nint builder, int inputChannels)
    {
        // The native builder writes input rows padded to 8/16 output lanes.
        var matrix = stackalloc float[256]; new Span<float>(matrix, 256).Clear();
        if (((delegate* unmanaged<float*, int, int, float, int>)builder)(matrix, inputChannels, 2, 1) != 0)
            throw new NotSupportedException("Native stereo matrix creation failed.");
        return HapticStereoMatrix.FromNative(new ReadOnlySpan<float>(matrix, inputChannels * inputChannels), inputChannels);
    }
}
