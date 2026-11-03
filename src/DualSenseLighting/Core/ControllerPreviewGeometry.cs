// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Xml.Linq;

namespace DualSenseLighting.Core;

public sealed record PreviewIndicator(int Bit, Vector2 Position, Vector2 Size);

/// <summary>Lighting coordinates come from the editable SVG, not a second drawing.</summary>
public sealed record ControllerPreviewGeometry(Vector2 Size, PreviewIndicator[] Indicators)
{
    public const string SvgResource = "DualSenseLighting.Assets.dualsense-preview.svg";
    public const string PngResource = "DualSenseLighting.Assets.dualsense-preview.png";
    public const string LightbarResource = "DualSenseLighting.Assets.dualsense-preview-lightbar.png";

    public static ControllerPreviewGeometry Load(Stream svg)
    {
        var document = XDocument.Load(svg);
        var root = document.Root ?? throw new InvalidDataException("Missing controller preview SVG.");
        float Number(XElement element, string name) => float.Parse(
            element.Attribute(name)?.Value ?? throw new InvalidDataException($"Missing preview {name}."), CultureInfo.InvariantCulture);
        var size = new Vector2(Number(root, "width"), Number(root, "height"));
        var indicators = root.Descendants().Where(e => e.Attribute("data-player-bit") != null).Select(e =>
            new PreviewIndicator((int)Number(e, "data-player-bit"), new(Number(e, "x"), Number(e, "y")),
                new(Number(e, "width"), Number(e, "height")))).OrderBy(i => i.Bit).ToArray();
        if (!float.IsFinite(size.X) || !float.IsFinite(size.Y) || size.X <= 0 || size.Y <= 0 ||
            !indicators.Select(i => i.Bit).SequenceEqual(Enumerable.Range(0, 5)))
            throw new InvalidDataException("Invalid controller preview dimensions or indicators.");
        return new(size, indicators);
    }

    public static LightingFrame Frame(LightingSettings settings, bool foreground, double seconds, float sfxLevel = 0,
        LimitBreakState? limitBreak = null) =>
        settings.Enabled && foreground ? LightingEffects.Render(settings, seconds, sfxLevel, limitBreak) : default;

    public static bool IsPlayerLit(LightingFrame frame, int bit) => bit is >= 0 and < 5 && (frame.PlayerLights & (1 << bit)) != 0;
}
