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

- Eleven RGB effects: Solid, Rainbow, Breathing, Color Shift, Aurora, Fire,
  Heartbeat, Comet, Rainbow Pulse, SFX Loudness and SFX Color Blend.
- RGB color, brightness, and animation period.
- Player indicators: off or one through five lights, animation, and independent timing.

Settings are saved through Dalamud and reapplied after reconnecting. The settings
apply to all supported USB controllers connected to this game process. The
controller exposes one lightbar RGB color; a spatial wave with independently
addressed LEDs is not available through this protocol.

### RGB animations

| Effect | Behavior | Color controls |
| --- | --- | --- |
| Solid | Holds a constant color | Your color |
| Rainbow | Travels smoothly around the full color spectrum | Automatic |
| Breathing | Gently fades in and out | Your color |
| Color Shift | Blends between two colors and back | Your color and Second color |
| Aurora | Teal, blue and violet drift with a soft shimmer | Preset palette |
| Fire | Warm ember-to-gold flicker with overlapping rhythms | Preset palette |
| Heartbeat | One bright pulse, a softer echo, then a pause | Your color |
| Comet | A quick flare with a long fading afterglow | Your color |
| Rainbow Pulse | Cycles the spectrum with three breaths per loop | Automatic |
| SFX Loudness (Prototype) | Original single-color brightness pulses; fades to black in silence | Your color |
| SFX Color Blend (Prototype) | Blends from Low to High as sound gets louder; keeps Low visible between sounds | Low SFX color and High SFX color |

Brightness applies to every RGB effect. The lightbar cycle slider sets a complete
loop (1–60 seconds), independently of the white indicator animation. Both side
lightpipes share the same color and brightness. The live preview uses exactly the
same effect calculation as controller output, including Fire's repeatable flicker.
Color Shift's second color is saved with the rest of the settings. Existing saved
Solid, Rainbow and Breathing selections keep their original meaning.

Try Aurora with a 12-second cycle for a slow shimmer, Fire at 3 seconds for an
energetic flicker, or Comet at 2 seconds with your favorite color.

### Combat SFX loudness prototype

Choose one of two separate options in the `/ds5light` **Lightbar effect** list:

- **SFX Loudness (Prototype)** retains the original behavior: one chosen color
  brightens with sound and fades to black in silence. Existing saved SFX selections
  retain this original mode.
- **SFX Color Blend (Prototype)** adds the low/high color behavior. Set **Low SFX
  color** for quiet sounds and **High SFX color** for the loudest sounds. Sound level
  blends between them at the configured brightness. Low remains visible even when
  the sound is below the noise gate or silent; the blend does not apply extra
  loudness dimming. **Color return** controls how quickly it returns toward Low.

**SFX sensitivity** scales the input in both modes. **SFX fade** controls the
brightness decay in the original mode. The noise gate is automatic by default,
so there is no separate threshold to tune during normal use.
The level meter and controller preview share the same envelope value used by the
controller worker and the native game-output guard. White indicators keep their
independent count, animation and timing.

These colors reuse the existing saved primary/secondary color pair, also used by
Color Shift. Their labeled pickers use the same color control as the other modes.
The low/high mapping is applied by the shared renderer, so the preview, worker
and guarded native controller reports use the same colors at every sound level.

The gate prevents very quiet sounds and faint tails from causing brightness or
color flutter; it does not change game audio. Its automatic threshold follows sensitivity
to keep a small 1.6% dead zone in the lighting response. At the default sensitivity,
it matches the 0.002 RMS threshold used in the successful live prototype test.
This is a conservative default, not a learned estimate that could mistake quiet
skills for background noise. A manual override remains under **Advanced SFX
settings** for unusual setups.

This prototype reads decoded combat voices on the game's SE and CommonVFX buses.
It excludes music, voice dialogue, ambience, footsteps, system sounds and
vibration-only sounds. It includes combat SFX from other players and enemies;
local-player-only attribution is not implemented. It does not use the broader
built-in FFT group, which also includes voices and ambience.

For each lighting tick, the observer keeps the loudest combat voice's RMS from
the preceding interval. This retains short impacts without diluting them with
quiet voices. It is a per-voice energy proxy, not a reconstruction of the final
mixed waveform. Samples are measured after source volume, panning and effects,
before the master/device volume. Increasing the SFX volume can change the response.

The optional observer is installed only when SFX mode is first selected. Its native
render hook always calls the original playback exactly once with the original
arguments and return value. Analysis only reads the sample buffer; it never
changes playback, sound routing or haptic/speaker audio. Its hot path has no HID
writes, logging, file access or managed allocations. A bounded atomic handoff
feeds the existing lighting worker at approximately 30 updates per second.
Silent voices are excluded using the mixer's current/previous volume threshold,
so their reused scratch memory cannot be mistaken for new audio. Initial signature
scanning runs off the game frame thread; unload joins any pending initialization.

Focus loss immediately hides the custom preview, resets the audio envelope on the
next worker tick, and performs the existing one-time lights-off release. Pending
audio blocks are discarded across focus changes, disabling or switching modes,
including callbacks that span a focus change. Returning to FFXIV restores the
selected mode's quiet state (black for Loudness, Low color for Color Blend) until
fresh SFX arrives. Both modes turn all lights off on focus loss, disabling or
unloading, even though Color Blend has a visible foreground quiet state.
Unplugging/replugging reapplies the saved mode. The existing native controller
report guard still preserves haptic, speaker and trigger fields.

The native bindings were checked against the local game build
`2026.10.06.0000.0000` and upstream FFXIVClientStructs revision
`b69073ae2d789527fc1b9a78aa7aa590e0f299dc`. Signatures must be unique, source-layout
instruction checks must match, and the native callback type and ownership links
must agree before inspecting samples. The prototype accepts the validated
256-frame, eight-channel layout, plus its 16-channel ambisonic variant. If setup
fails, the settings window reports the error and other lighting modes remain
available. New game versions may require updated bindings.

Automated checks cover RMS, noise rejection, decay, stale-callback rejection,
preview/output agreement, bus filtering and unchanged native controller bytes.
On 2026-11-02, the user confirmed v0.5.0 passed live SFX/preview synchronization,
native haptic and controller-speaker effects, and lights-off release/return on
Alt-Tab, and subsequently confirmed v0.5.2 works well. Version 0.6.1 separates the
two SFX modes and keeps the low color visible. Automated checks cover the quiet
state below the gate, mode switching, endpoints, continuity, preview agreement,
native report preservation, focus release and reconnects. Its final in-game
appearance still requires a reload.

### Player indicator animations

The **Player indicators** controls set the number of white lights, an animation,
and its cycle length (1–12 seconds). This runs independently of the RGB lightbar.
For Chase, Blink and Loading, the count chooses the available positions. For
example, choosing four excludes the center LED from those effects. Bounce and
Sparkle use the count as the number lit at once and can travel across the full
five-position strip. Heartbeat pulses the configured number together in the
standard centered/symmetric layout.

| Animation | Behavior |
| --- | --- |
| Steady | Holds the existing centered/symmetric player layout |
| Chase | Moves one light across the selected positions and wraps around |
| Bounce | Moves a group of the configured size back and forth across the full strip |
| Blink | Flashes the selected player layout on and off |
| Heartbeat | Pulses the configured number together twice, then rests |
| Sparkle | Flashes the configured number at varied positions across the full strip |
| Loading | Fills the selected positions from left to right, then restarts |

Chase needs at least two selected positions for visible travel. Bounce moves even
with a count of one, but stays fully lit with five because the group fills the
strip. Indicators retain their fixed white color and on/off behavior. The settings
window explains the count's meaning for the currently selected animation.

The live preview follows the same patterns and clock as the hardware. Animation
state returns after focus changes or reconnecting; the automatic lights-off release and
background guard still apply. Existing saved counts load as Steady, so installing
the update does not start an animation until one is selected.

### Live controller preview

The top of `/ds5light` shows a centered touchpad close-up with a white touchpad,
tapered RGB lightpipes and five white player indicators along the lower rim.
The D-pad and action-button details are omitted to keep the preview focused on lighting.
All RGB effects and brightness use the same effect engine and clock as
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

When FFXIV loses focus, the worker turns off the custom RGB and player indicators
once, then closes its handles and stops creating lighting updates. This behavior
is automatic and has no toggle or application selection. It does not read or apply
Steam's profile over another lighting app's settings.

A narrow guard inside FFXIV removes lightbar, player-indicator, brightness
and boot-pulse validity bits from the game's outgoing USB reports. Other fields,
including haptic, audio, trigger and microphone controls, pass through. The guard
checks the actual HID vendor/product, gamepad usage and report layout before
modifying a buffer. Only the plugin's own shared output handles bypass this guard,
allowing the one-time lights-off update. It does not hook Steam or other processes.

Returning to FFXIV reapplies the selected effect. Disabling custom lighting or
unloading also turns off the lights for controllers whose lighting the session
changed. The background guard remains enabled until the plugin is unloaded.

Other apps can freely change the LEDs after release. Their color or animation
appears when they send their next lighting update; the plugin cannot restore an
arbitrary app's saved settings or make that app send a new report. If no app writes
a new setting, the lights remain off instead of freezing at the last custom frame.

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
RGB-animation checks cover color endpoints, pulse timing, seamless loops,
brightness limits, independent player indicators and lifecycle behavior for every
effect. Indicator-animation checks cover every effect and count, cycle boundaries, effect-specific
count/range behavior, the LED-only immediate-update flag, independent
output with solid RGB, and animation resume/reconnect/release behavior.

### In-game verification

The automated checks and a read-only probe are separate from live game validation.
Before treating compatibility as verified on a particular setup, check:

| Action | Expected result |
| --- | --- |
| Select a solid color and player indicator | LEDs follow the selection while FFXIV is active |
| Select rainbow or breathing | Animation stays steady while playing |
| Alt-Tab while rainbow is red | Custom lights turn off once; any other lighting app can then apply its effect |
| Change another app's lighting while FFXIV remains in the background | FFXIV's guard does not replace the app's updates |
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
