# PS5 Controller for FFXIV

DualSense controller customization for Final Fantasy XIV using Dalamud.

## Project goals

- Configure RGB lighting and player indicators, including after reconnecting.
- Preserve native DualSense haptics and controller-speaker effects.
- Explore RGB animations and skill-sound-driven haptics across jobs.

## Development approach

Keep operations small and simple. Reuse existing libraries and game capabilities
where practical.

## Lighting plugin

The first implementation targets **Windows, Dalamud API 15, and USB DualSense or
DualSense Edge controllers**. Bluetooth transport is not implemented.

Open `/ds5light` or the plugin's configuration button to change:

- Solid color, rainbow color cycle, or breathing effect.
- RGB color, brightness, and animation period.
- Player indicator: off or one through five lights.

Settings are saved through Dalamud and reapplied after reconnecting. The settings
apply to all supported USB controllers connected to this game process. The
controller exposes one lightbar RGB color; a spatial wave with independently
addressed LEDs is not available through this protocol.

### Live controller preview

The top of `/ds5light` shows a centered touchpad close-up with a white touchpad,
tapered RGB lightpipes and five white player indicators along the lower rim.
Color, brightness, rainbow and breathing use the same effect engine and clock as
the hardware output. Player indicators follow the actual five-bit masks, including
the centered single light, inner pair, outer pair plus center, and four-light layout.
Their centers are deliberately not five equally spaced positions. The white marks
are widened for readability at the settings window's normal size.

The preview pauses in the background and shows the custom lights off when disabled.
It previews the configured effect; it does not read the controller's current LEDs.

Editable artwork: `src/DualSenseLighting/Assets/dualsense-preview.svg`. This is a
standalone vector file with no embedded photograph. The original supplied reference
is retained as `preview.jpg`. The redraw uses its 1800-pixel source proportions and
the requested touchpad crop. Layout was cross-checked against Sony's
[controller part names](https://controller.dl.playstation.net/controller/lang/en/DS_partnames.html),
[DualSense introduction and product photos](https://blog.playstation.com/2020/04/07/introducing-dualsense-the-new-wireless-game-controller-for-playstation-5/),
and [hands-on player-indicator photos](https://x.com/Alejandroid1979/status/1312738751311949825).
The proportions are inferred from photographs, rather than a mechanical CAD model.

The cached body texture and tapered lightbar alpha mask are exported from that
single SVG and embedded in the DLL. The player positions are also read from the
SVG. All three share identical dimensions, so UI scaling does not stretch the
artwork or misalign the lights. Dalamud's shared texture service handles display;
the plugin adds no image-rendering runtime library.

To regenerate artwork from WSL using the system's librsvg and Cairo:

```bash
python3 tools/render-preview.py --sample --player-mask 0x1b
python3 tools/build-wsl.py
```

The sample exports to `artifacts/preview/`. The renderer is a development tool;
ordinary builds use the checked-in PNG exports.

### Focus and compatibility

The worker checks the foreground window's process before acquiring devices and
again before each write. It uses shared, asynchronous HID handles and never reads
controller input. Animation updates are capped at approximately 30 per second;
an unchanged solid color is refreshed once per second while foreground.

When FFXIV loses focus, the worker restores the controller's saved Steam lighting
once, then closes its handles and stops creating lighting updates. The default-on
**Restore Steam lighting when leaving FFXIV** option includes RGB, saturation,
brightness and player indicators. The profile is matched to the controller's serial
and Steam's active account, and read again on each handoff so current saved settings
are used. If no valid profile is available, the error is shown in Diagnostics and
the handles are closed without repeated background writes.

A narrow guard inside FFXIV removes lightbar, player-indicator, brightness
and boot-pulse validity bits from the game's outgoing USB reports. Other fields,
including haptic, audio, trigger and microphone controls, pass through. The guard
checks the actual HID vendor/product, gamepad usage and report layout before
modifying a buffer. Only the plugin's own shared output handles bypass this guard,
allowing the one-time Steam handoff. It does not hook Steam or other processes.

Returning to FFXIV reapplies the selected effect. Disabling custom lighting or
unloading also attempts a Steam handoff for controllers whose lighting the session
changed. The background guard remains enabled until the plugin is unloaded.

This handoff reads Steam's saved configuration; it does not inspect arbitrary
applications' LED settings. Other apps can freely change the LEDs after the handoff.
Turn the restore option off if a different lighting application should handle the
transition itself. With restoration off, the last color remains until that app sends
an update.

### Load the local build

1. Disable the older **Steam Lighting Guard** plugin. This replacement refuses to
   load alongside it to avoid two guards changing the same reports.
2. In `/xlsettings` → Experimental → Dev Plugin Locations, add the DLL:
   `D:\Dev\Dalamud plugins\PS5 Controller for FFXIV\artifacts\plugin\DualSenseLighting.dll`.
3. Save, then enable **DualSense Lighting** in `/xlplugins` → Dev Tools.
4. Run `/ds5light`.

The plugin does not change the earlier Steam helper or its startup shortcut.
That helper may continue restoring Steam lighting when a controller reconnects;
this plugin reapplies its own settings while FFXIV is active.

### Build and checks

Preferred build requirements: .NET 10 SDK and API 15 Dalamud libraries.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\build.ps1 -Probe
```

The script discovers local XIVLauncherCN/XIVLauncher API 15 libraries. Override
with `-DalamudLibPath` or `DALAMUD_HOME`. Without an SDK, it can reuse an installed
Roslyn compiler, or accept its directory via `-CompilerPath`. The compiler is a
build-time dependency only. No extra runtime DLLs are bundled with the plugin.

From this WSL workspace, where Windows tools need to be launched directly:

```bash
python3 tools/build-wsl.py --probe
```

Both build paths compile into a staging directory, run regression checks, and publish the DLL,
manifest and license notices in `artifacts/plugin/`. `-Probe`/`--probe` opens and
closes shared controller handles to inspect their capabilities; it sends no
controller output. Standard SDK commands are also supported:

```powershell
dotnet build .\src\DualSenseLighting\DualSenseLighting.csproj -c Release -p:DalamudLibPath="C:\path\to\Dalamud"
dotnet run --project .\tests\LightingChecks.csproj -c Release
```

Regression checks cover report preservation across every validity-byte value,
foreground/background transitions, disabling, reconnects, animation behavior,
focus changes during discovery and between controllers, discovery retry backoff,
write timeouts, cancellation, Steam preference validation and account/controller
matching, one-time handoffs, and foreground return during a profile read.
Preview checks cover embedded artwork, aspect ratio and mask alignment, player
positions and bitmasks, animation phase/color agreement, and paused/disabled states.

### In-game verification

The automated checks and a read-only probe are separate from live game validation.
Before treating compatibility as verified on a particular setup, check:

| Action | Expected result |
| --- | --- |
| Select a solid color and player indicator | LEDs follow the selection while FFXIV is active |
| Select rainbow or breathing | Animation stays steady while playing |
| Alt-Tab while rainbow is red | Saved Steam lighting returns instead of freezing red |
| Change Steam lighting while FFXIV remains in the background | FFXIV's guard does not replace Steam's updates |
| Return to FFXIV | The saved game effect resumes |
| Unplug/replug while FFXIV is active | Saved lighting returns within approximately one second |
| Trigger native controller sound and haptics | Native effects remain functional with custom lighting |
| Unload/reload the plugin | Handles close; settings persist across the reload |

On 2026-11-02, the user confirmed live custom lighting works and version 0.1.1
restores Steam lighting on Alt-Tab and resumes the custom effect on returning to
FFXIV. The latest read-only probe matched the connected controller to RGB
149/255/0 with player indicators off. Native report preservation is covered by
automated checks; native haptic/speaker behavior still needs its own live check.
The user also confirmed the revised version 0.2.1 touchpad close-up and player
layout look correct; version 0.2.2 widens the white indicator marks by 60% while
preserving their centers and height.

If output fails, the UI's Diagnostics section shows the most recent error and
foreground/background report counters. Device access is retried without exclusive
access or elevated privileges. Inspection failures pass the original report
through and increment an error counter rather than suppressing native effects.

## Source reuse and license

Lighting packing and hue conversion are adapted from
[hbashton/DS4Windows](https://github.com/hbashton/DS4Windows) at commit
`2637673e0ecd03b042c30476224da398475d9cc7`. The shared HID enumeration approach
comes from its HidLibrary sources. The Dalamud output interception approach
reuses our earlier local Steam Lighting Guard prototype.

This project is licensed under GPL-3.0-only. Upstream copyright and license notices
are retained in `THIRD-PARTY-NOTICES.txt`, `LICENSE.txt` and
`LICENSE-HidLibrary.txt`. Full DS4Windows, remapping, virtual-controller drivers,
and audio processing are not bundled.
