// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using DualSenseLighting.Core;

namespace DualSenseLighting.Ui;

internal sealed class SettingsWindow(Action draw) : Window(
    PluginInfo.Name + "###DualSenseLightingConfig", ImGuiWindowFlags.AlwaysAutoResize)
{
    public Action? Closed { get; set; }

    public override void PreDraw()
    {
        // Font-relative pixels preserve the existing fixed width across tabs.
        // Window.SizeConstraints would apply global scaling a second time.
        var width = 560 * ImGui.GetFontSize() / 17f;
        ImGui.SetNextWindowSizeConstraints(new Vector2(width, 0), new Vector2(width, float.MaxValue));
    }

    public override void Draw() => draw();

    public override void OnClose() => Closed?.Invoke();
}
