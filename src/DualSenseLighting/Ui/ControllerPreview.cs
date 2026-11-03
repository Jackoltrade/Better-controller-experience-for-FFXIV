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
    private readonly UiText text;

    internal ControllerPreview(ITextureProvider textures, UiText text)
    {
        this.textures = textures;
        this.text = text;
        using var stream = typeof(ControllerPreview).Assembly.GetManifestResourceStream(ControllerPreviewGeometry.SvgResource)
            ?? throw new InvalidDataException("Controller preview artwork was not included in this build.");
        geometry = ControllerPreviewGeometry.Load(stream);
    }

    internal unsafe void Draw(LightingSettings settings, bool foreground, double seconds, float sfxLevel = 0, LimitBreakState? limitBreak = null)
    {
        var uiScale = ImGui.GetFontSize() / 17f;
        var width = 420 * uiScale;
        var size = new Vector2(width, width * geometry.Size.Y / geometry.Size.X);
        var startX = ImGui.GetCursorPosX();
        var available = Math.Max(width, ImGui.GetContentRegionAvail().X);
        ImGui.SetCursorPosX(startX + (available - width) / 2);
        var origin = ImGui.GetCursorScreenPos();
        var background = *ImGui.GetStyleColorVec4(ImGuiCol.WindowBg);
        background.W = 1;
        ImGui.GetWindowDrawList().AddRectFilled(origin, origin + size, ImGui.GetColorU32(background));

        // Use Dalamud's shared texture cache; never retain or dispose the per-frame wrap.
        var shared = textures.GetFromManifestResource(typeof(ControllerPreview).Assembly, ControllerPreviewGeometry.PngResource);
        var ready = shared.TryGetWrap(out var wrap, out _);
        if (ready) ImGui.Image(wrap!.Handle, size);
        else ImGui.Dummy(size);

        if (ready)
        {
            var scale = width / geometry.Size.X;
            var frame = ControllerPreviewGeometry.Frame(settings, foreground, seconds, sfxLevel, limitBreak);
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
        FadeEdges(origin, size, uiScale);
        ImGui.SetCursorPosX(startX);
        var label = text.Get(!ready ? "Loading preview…" : !settings.Enabled ? "Custom lighting off"
            : foreground ? "Live preview" : "Preview paused — FFXIV is in the background");
        ImGui.SetCursorPosX(startX + Math.Max(0, (available - ImGui.CalcTextSize(label).X) / 2));
        ImGui.TextDisabled(label);
        ImGui.SetCursorPosX(startX);
        ImGui.Spacing();
    }

    private static unsafe void FadeEdges(Vector2 origin, Vector2 size, float scale)
    {
        // Fade the complete preview after drawing its live LEDs, using the current
        // window color so the edge blends with both dark and light themes.
        var background = *ImGui.GetStyleColorVec4(ImGuiCol.WindowBg);
        background.W = 1;
        var outer = ImGui.GetColorU32(background);
        background.W = 0;
        var inner = ImGui.GetColorU32(background);
        var horizontal = 12 * scale;
        var vertical = 8 * scale;
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilledMultiColor(origin, origin + new Vector2(horizontal, size.Y), outer, inner, inner, outer);
        draw.AddRectFilledMultiColor(origin + new Vector2(size.X - horizontal, 0), origin + size, inner, outer, outer, inner);
        draw.AddRectFilledMultiColor(origin, origin + new Vector2(size.X, vertical), outer, outer, inner, inner);
        draw.AddRectFilledMultiColor(origin + new Vector2(0, size.Y - vertical), origin + size, inner, inner, outer, outer);
    }

    private static uint Color(byte red, byte green, byte blue, byte alpha = 255) =>
        (uint)(red | green << 8 | blue << 16 | alpha << 24);
}
