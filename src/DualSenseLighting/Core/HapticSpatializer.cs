// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Diagnostics;
using System.Numerics;

namespace DualSenseLighting.Core;

public sealed record HapticListenerPose(Vector3 Origin, Vector3 CameraRight, long Timestamp, Vector3 CameraPosition = default)
{
    // Anchor the virtual listener at the character, with camera-aligned axes.
    public static HapticListenerPose? FromCamera(Vector3 characterPosition, Vector3 cameraPosition, Matrix4x4 view, long timestamp)
    {
        var right = new Vector3(view.M11, view.M21, view.M31);
        var up = new Vector3(view.M12, view.M22, view.M32);
        var back = new Vector3(view.M13, view.M23, view.M33);
        // Scene camera matrices can have M44 == 0. Validate the actual basis and
        // translation, rather than inverting an assumed .NET affine matrix.
        if (!HapticSpatializer.ValidPosition(characterPosition) || !HapticSpatializer.ValidPosition(cameraPosition) ||
            !HapticSpatializer.ValidPosition(right) ||
            !HapticSpatializer.ValidPosition(up) || !HapticSpatializer.ValidPosition(back) ||
            MathF.Abs(right.LengthSquared() - 1) > 0.02f || MathF.Abs(up.LengthSquared() - 1) > 0.02f ||
            MathF.Abs(back.LengthSquared() - 1) > 0.02f || MathF.Abs(Vector3.Dot(right, up)) > 0.02f ||
            MathF.Abs(Vector3.Dot(right, back)) > 0.02f || MathF.Abs(Vector3.Dot(up, back)) > 0.02f) return null;
        var cameraOrigin = Vector3.Transform(cameraPosition, view);
        if (!HapticSpatializer.ValidPosition(cameraOrigin) || cameraOrigin.LengthSquared() > 0.25f) return null;
        return new(characterPosition, Vector3.Normalize(right), timestamp, cameraPosition);
    }
}

public readonly record struct HapticSpatialFrame(bool Valid, float Pan, float Distance, float Left, float Right);

public static class HapticSpatializer
{
    private static readonly float ClosePanLimit = MathF.Tanh(6);
    public static bool ValidPosition(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) && MathF.Abs(value.X) < 1_000_000 && MathF.Abs(value.Y) < 1_000_000 && MathF.Abs(value.Z) < 1_000_000;

    private static float SmoothStep(float value)
    {
        var t = Math.Clamp(value, 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static float DirectionalPan(Vector3 offset, Vector3 cameraRight)
    {
        // Height should affect distance, not dilute the left/right bearing of
        // a nearby impact. Centered emitters have no reliable side to favor.
        var horizontal = new Vector2(offset.X, offset.Z);
        var right = new Vector2(cameraRight.X, cameraRight.Z);
        var distance = horizontal.Length(); var rightLength = right.Length();
        if (distance <= 0.05f || rightLength <= 0.001f) return 0;
        var bearing = Math.Clamp(Vector2.Dot(horizontal / distance, right / rightLength), -1, 1);
        // Increase small angular differences at melee range without amplifying
        // either grip. Blend back to normal panning between 3 and 6 yalms.
        var close = 1 - SmoothStep((distance - 3) / 3);
        var emphasized = MathF.Tanh(6 * bearing) / ClosePanLimit;
        var pan = bearing + (emphasized - bearing) * close;
        return pan * SmoothStep((distance - 0.05f) / 0.20f);
    }

    public static (float Near, float Far) DistanceRange(HapticSettings settings, HapticListenerPose? listener)
    {
        // Triangle inequality: a source within N yalms of the character is at
        // most camera offset + N yalms from the camera. Preserve that combat
        // envelope when the recommended camera mode follows camera zoom.
        var offset = settings.AutomaticDistance && settings.DistanceOrigin == HapticDistanceOrigin.Camera && listener != null
            ? Vector3.Distance(listener.Origin, listener.CameraPosition) : 0;
        return (settings.NearDistance + offset, settings.FarDistance + offset);
    }

    public static HapticSpatialFrame Evaluate(HapticSettings settings, HapticListenerPose? listener, Vector3 source, long now)
    {
        if (!settings.Spatial || listener == null || !ValidPosition(source) ||
            now < listener.Timestamp || now - listener.Timestamp > Stopwatch.Frequency / 2) return default;
        var offset = source - listener.Origin;
        var directionLength = offset.Length();
        var distance = settings.DistanceOrigin == HapticDistanceOrigin.Camera
            ? Vector3.Distance(source, listener.CameraPosition) : directionLength;
        if (!float.IsFinite(distance)) return default;
        var pan = DirectionalPan(offset, listener.CameraRight);
        var fade = 1f;
        if (settings.DistanceFalloff)
        {
            var range = DistanceRange(settings, listener);
            fade = 1 - SmoothStep((distance - range.Near) / (range.Far - range.Near));
        }
        return new(true, pan, distance, fade * (1 - Math.Max(0, pan)), fade * (1 + Math.Min(0, pan)));
    }

    public static HapticSpatialFrame BiasTowardImpact(HapticSettings settings, HapticListenerPose listener,
        Vector3 source, HapticSpatialFrame original, Vector3 impact)
    {
        if (!settings.FollowImpact || !original.Valid || !ValidPosition(impact)) return original;
        var delta = source - listener.Origin;
        var radius = new Vector2(delta.X, delta.Z).Length();
        var blend = settings.ImpactBias * (1 - SmoothStep(radius / 0.75f));
        if (blend <= 0) return original;
        if (settings.DistanceFalloff)
        {
            var impactDistance = Vector3.Distance(impact, settings.DistanceOrigin == HapticDistanceOrigin.Camera
                ? listener.CameraPosition : listener.Origin);
            if (impactDistance >= DistanceRange(settings, listener).Far) return original;
        }
        var impactPan = DirectionalPan(impact - listener.Origin, listener.CameraRight);
        var pan = original.Pan + (impactPan - original.Pan) * blend;
        var fade = Math.Max(original.Left, original.Right);
        return original with { Pan = pan, Left = fade * (1 - Math.Max(0, pan)), Right = fade * (1 + Math.Min(0, pan)) };
    }

    // The game's own conversion matrix handles both the 8-channel bed and
    // 16-channel ambisonics. Only our separate haptic buffer is re-panned.
    public static bool Render(ReadOnlySpan<float> samples, ReadOnlySpan<float> stereoMatrix,
        Span<float> output, HapticSpatialFrame spatial)
    {
        if (!spatial.Valid || samples.Length is not (2048 or 4096) || output.Length != 2048 ||
            stereoMatrix.Length != samples.Length / 256 * 2) return false;
        output.Clear();
        var channels = samples.Length / 256;
        for (var frame = 0; frame < 256; frame++)
        {
            var mono = 0f;
            for (var channel = 0; channel < channels; channel++)
                mono += samples[frame * channels + channel] *
                    (stereoMatrix[channel] + stereoMatrix[channels + channel]) * 0.5f;
            var left = mono * spatial.Left; var right = mono * spatial.Right;
            if (!float.IsFinite(left) || !float.IsFinite(right) || MathF.Abs(left) > 16 || MathF.Abs(right) > 16)
            { output.Clear(); return false; }
            output[frame * 8] = left; output[frame * 8 + 1] = right;
        }
        return true;
    }
}
