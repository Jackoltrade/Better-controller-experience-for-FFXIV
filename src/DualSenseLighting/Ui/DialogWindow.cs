// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using DualSenseLighting.Core;

namespace DualSenseLighting.Ui;

internal sealed class DialogWindow(Action draw) : Window(
    PluginInfo.Name + "###DualSenseLightingDialog",
    ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoCollapse)
{
    public const float ViewportHeightFraction = 0.9f;
    private string title = PluginInfo.Name;
    private bool pushedStyles;

    public string Title
    {
        get => title;
        set { title = value; WindowName = value + "###DualSenseLightingDialog"; }
    }

    public float Width { get; set; } = 560;

    public override void PreDraw()
    {
        var scale = ImGui.GetFontSize() / 17f;
        var viewport = ImGui.GetMainViewport();
        var display = viewport.WorkSize;
        var width = Math.Min(Width * scale, Math.Max(1, display.X - 32 * scale));
        ImGui.SetNextWindowSizeConstraints(new Vector2(width, 0), new Vector2(width, display.Y * ViewportHeightFraction));
        ImGui.SetNextWindowPos(viewport.WorkPos + display / 2, ImGuiCond.Appearing, new Vector2(0.5f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(20, 18) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(10, 8) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(12, 6) * scale);
        pushedStyles = true;
    }

    public override void Draw() => draw();

    public override void PostDraw()
    {
        if (!pushedStyles) return;
        ImGui.PopStyleVar(3);
        pushedStyles = false;
    }
}
