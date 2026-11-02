// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.IO;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using DualSenseLighting.Core;

namespace DualSenseLighting.Ui;

internal sealed class ControllerPreview
{
    private readonly ITextureProvider textures;
    private readonly ControllerPreviewGeometry geometry;

    internal ControllerPreview(ITextureProvider textures)
    {
        this.textures = textures;
        using var stream = typeof(ControllerPreview).Assembly.GetManifestResourceStream(ControllerPreviewGeometry.SvgResource)
            ?? throw new InvalidDataException("Controller preview artwork was not included in this build.");
        geometry = ControllerPreviewGeometry.Load(stream);
    }

    internal void Draw(LightingSettings settings, bool foreground, double seconds, float sfxLevel = 0)
    {
        var uiScale = ImGui.GetFontSize() / 17f;
        var width = 420 * uiScale;
        var size = new Vector2(width, width * geometry.Size.Y / geometry.Size.X);
        var startX = ImGui.GetCursorPosX();
        var available = Math.Max(width, ImGui.GetContentRegionAvail().X);
        ImGui.SetCursorPosX(startX + (available - width) / 2);
        var origin = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddRectFilled(origin, origin + size, Color(18, 22, 28), 10 * uiScale);

        // Use Dalamud's shared texture cache; never retain or dispose the per-frame wrap.
        var shared = textures.GetFromManifestResource(typeof(ControllerPreview).Assembly, ControllerPreviewGeometry.PngResource);
        var ready = shared.TryGetWrap(out var wrap, out _);
        if (ready) ImGui.Image(wrap!.Handle, size);
        else ImGui.Dummy(size);

        if (ready)
        {
            var scale = width / geometry.Size.X;
            var frame = ControllerPreviewGeometry.Frame(settings, foreground, seconds, sfxLevel);
            var peak = Math.Max(frame.Red, Math.Max(frame.Green, frame.Blue));
            var lightbar = textures.GetFromManifestResource(typeof(ControllerPreview).Assembly, ControllerPreviewGeometry.LightbarResource);
            if (peak > 0 && lightbar.TryGetWrap(out var lightbarWrap, out _))
            {
                // Tint a cached mask exported from the SVG's tapered ribbons and subtle glow.
                var tint = new Vector4(frame.Red / (float)peak, frame.Green / (float)peak, frame.Blue / (float)peak, peak / 255f);
                ImGui.GetWindowDrawList().AddImage(lightbarWrap!.Handle, origin, origin + size, Vector2.Zero, Vector2.One, ImGui.GetColorU32(tint));
            }
            foreach (var indicator in geometry.Indicators)
            {
                if (!ControllerPreviewGeometry.IsPlayerLit(frame, indicator.Bit)) continue;
                var minimum = origin + indicator.Position * scale;
                var maximum = minimum + indicator.Size * scale;
                var halo = new Vector2(8 * scale, 4 * scale);
                ImGui.GetWindowDrawList().AddRectFilled(minimum - halo, maximum + halo, Color(224, 237, 255, 20), 6 * scale);
                ImGui.GetWindowDrawList().AddRectFilled(minimum - halo * 0.45f, maximum + halo * 0.45f, Color(237, 245, 255, 58), 3 * scale);
                ImGui.GetWindowDrawList().AddRectFilled(minimum, maximum, Color(250, 252, 255), 2.25f * scale);
            }
        }
        ImGui.SetCursorPosX(startX);
        var label = !ready ? "Loading preview…" : !settings.Enabled ? "Custom lighting off"
            : foreground ? "Live preview" : "Custom preview paused — FFXIV is in the background";
        ImGui.SetCursorPosX(startX + Math.Max(0, (available - ImGui.CalcTextSize(label).X) / 2));
        ImGui.TextDisabled(label);
        ImGui.SetCursorPosX(startX);
        ImGui.Spacing();
    }

    private static uint Color(byte red, byte green, byte blue, byte alpha = 255) =>
        (uint)(red | green << 8 | blue << 16 | alpha << 24);
}
