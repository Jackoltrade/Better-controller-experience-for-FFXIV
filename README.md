# PS5 DualSense Controller for FFXIV

[English](README.md) · [简体中文](README.zh-CN.md) · [日本語](README.ja.md)

Custom lighting and sound-driven haptics for **DualSense** and **DualSense Edge** in Final Fantasy XIV, built with Dalamud.

**Public testing · Windows x64 · USB only · Dalamud API 15**

[Source code](https://github.com/Jackoltrade/PS5-DualSense-Controller-for-FFXIV-) · [Releases](https://github.com/Jackoltrade/PS5-DualSense-Controller-for-FFXIV-/releases) · [Report an issue](https://github.com/Jackoltrade/PS5-DualSense-Controller-for-FFXIV-/issues)

![Controller preview artwork](src/DualSenseLighting/Assets/dualsense-preview.png)

## What it does

- **RGB lighting:** solid color, Rainbow, Breathing, Color Shift, Aurora, Fire, Heartbeat, Comet, Rainbow Pulse, and two sound-reactive modes.
- **Bottom LEDs:** choose how many white lights below the touchpad to use, animate them, or show Limit Break status.
- **Combat haptics:** feel your character's skill and weapon sounds through the game's existing DualSense vibration processing.
- **Spatial feedback:** use left/right grip balance and distance fade to place sounds around your character, aligned with the camera.
- **Native footsteps:** optionally boost footstep vibration independently. Mount vibration keeps its native strength.
- **Live preview and meter:** see the same lighting calculation as the controller, with a sound-response meter beneath sensitivity.
- **English, 简体中文 and 日本語:** switch languages above the tabs; your choice is saved.

When FFXIV loses focus, custom lights turn off once and the plugin releases its lighting handles. Added combat and footstep feedback pause. Native speaker and trigger effects are preserved. Settings return when you come back or reconnect the controller.

## Requirements and current compatibility

- Windows x64, FFXIV with **Dalamud API 15**, and a USB-connected Sony DualSense or DualSense Edge.
- Enable native controller vibration in FFXIV to use haptics. Use the game's own DualSense setup for controller-speaker effects.
- Bluetooth, virtual gamepads and button remapping are outside this plugin's current scope.

This is a **public test build**, prepared for a custom repository. It is not an officially reviewed main-repository plugin. The native audio/camera bindings were validated on the `2026.10.06.0000.0000` client build with XIVLauncherCN; other regional builds and future patches need testing. Unsupported audio layouts pause the affected features and report details in Diagnostics.

## Install a public test build

Public downloads become available after the maintainer publishes the first release. If Releases is empty, follow [Build from source](#build-from-source).

### Through Dalamud's plugin installer

1. Open `/xlsettings` → **Experimental** → **Custom Plugin Repositories**.
2. Add this repository URL after its first public release:
   ```text
   https://github.com/Jackoltrade/PS5-DualSense-Controller-for-FFXIV-/releases/download/plugin-repository/pluginmaster.json
   ```
3. Enable testing plugins in Dalamud, then find **PS5 DualSense Controller for FFXIV** in `/xlplugins` and install its testing build.
4. Connect the controller by USB and run `/ds5`.

The repository is **testing-only**: it does not advertise these builds as stable.

### Manual testing

1. Download `latest.zip` from a [public test release](https://github.com/Jackoltrade/PS5-DualSense-Controller-for-FFXIV-/releases).
2. Extract the entire ZIP into a dedicated folder.
3. In `/xlsettings` → Experimental → **Dev Plugin Locations**, add that folder's `DualSenseLighting.dll`.
4. Enable **PS5 DualSense Controller for FFXIV** in `/xlplugins` → Dev Tools.

Disable the old **Steam Lighting Guard** before loading this plugin. Use one installation at a time: remove the dev-plugin entry when moving to the normal installer.

The DLL and internal ID remain `DualSenseLighting` so existing configuration and dev-plugin paths stay compatible with the renamed project.

## Configure the plugin

Run **`/ds5`**. Hover a setting or its **(?)** marker for help. Disabled controls show which parent feature needs enabling; their saved values are retained.

The first time you open the settings, a short guide explains supported controllers, the USB requirement and background behavior. Reopen it with **Info** in the top-left corner. You can switch languages inside the guide; closing it is remembered across reloads and tab resets.

### Commands

`/ds5` opens settings, `/ds5 help` lists commands, `/ds5 info` opens the setup guide, and `/ds5 trace` starts a 15-second diagnostic recording.

Use `/ds5 <feature> on`, `off` or `toggle` to change a feature without opening settings. For example: `/ds5 combathaptics on`.

| Feature | Controls |
| --- | --- |
| `lighting` | Custom lighting |
| `combathaptics` | Added character combat feedback |
| `footsteps` | Native footstep amplification |
| `spatialhaptics` | Directional combat feedback |
| `distancefade` | Distance fade |
| `followimpact` | Bias toward skill impacts |

Commands ignore case, save automatically and work in macros. Repeating `on` for an enabled feature leaves it unchanged and does not repeat the enable cue. Subfeatures retain their parent requirements; chat explains when a parent needs enabling. `footsteps off` restores native footstep strength rather than muting it.

### Lighting

Choose the lightbar effect, colors, brightness and animation cycle. Both side strips share one RGB color; the hardware does not expose individually addressable pixels for a spatial wave.

| Sound mode | Behavior |
| --- | --- |
| SFX Loudness | One color gets brighter with sound and fades to black in silence. |
| SFX Color Blend | Moves from your Low color to your High color as sounds get louder. Low stays visible between sounds. |

**My character only** follows your own actions and weapons. **All combat sounds** also reacts to nearby players and enemies. This choice affects lighting; added combat haptics always use your character's sounds.

Adjust **SFX sensitivity** while using skills and watching the meter underneath. Lower it if the meter stays full; raise it for quiet skills. The automatic noise gate ignores faint sounds and adapts to sensitivity. Advanced settings let you set its threshold yourself. These settings do not change game volume.

For **Bottom LEDs**, choose a count and animation. Chase, Blink and Loading stay within the selected positions. Bounce and Sparkle can travel across all five positions, with the count setting how many light up; Heartbeat pulses the selected number together. Limit Break mode shows ready party bars in PvE or your own charge in PvP, with a quick flash when you use your LB.

### Haptics

New installations start with:

| Setting | Default |
| --- | --- |
| Character combat haptics | On, 80% strength |
| Spatial haptics | On |
| Follow skill impacts | On, 100% impact bias |
| Distance origin | Character |
| Distance fade | Recommended: full strength within 3 yalms; zero at 25 |
| Native footstep amplifier | Off; saved multiplier 150% |

**Spatial haptics** use your character's position and the camera's facing to determine left and right. **Follow skill impacts** gently steers character-origin sounds toward a recent impact from your own skill. Impact bias changes direction; it does not move the actual sound or its distance.

Turning combat haptics on gives two short confirmation vibrations, even while idle. Changing strength or returning to the game does not repeat them.

Character distance fade is independent of camera zoom. Camera distance is a separate option; its recommended range adds the camera-to-character offset. Manual ranges are under Advanced distance settings. Skills and jobs have different sound mixes, so their vibration strength can differ.

The **native footstep amplifier** only boosts sounds the game already routes to footstep vibration. It works separately from combat haptics, pauses while mounted or in the background, and supports 100–600%. At 100% it leaves native strength unchanged. It does not amplify speaker audio or mount feedback.

**Reset to defaults** restores only the selected Lighting or Haptics tab after confirmation. Cancel, Close, Escape or clicking outside the popup leaves settings unchanged. Your language and the other tab are preserved. Updates retain existing settings.

### Diagnostics and feedback

Diagnostics contains controller/focus status, output errors, audio attribution counters, left/right grip meters and Limit Break status.

To record a difficult sound:

1. Enable a sound-reactive light effect or combat haptics.
2. Run **`/ds5 trace`**, or select **Record SFX diagnostics (15 seconds)** in Diagnostics.
3. Use the relevant skill during the countdown and stop when recording completes.

The recording replaces `sfx-trace.json` beside the DLL. It contains sound names, ownership metadata, positions, routing decisions and local process addresses; **it does not record audio**. Nothing is uploaded automatically. Review a trace before attaching it to a public report.

[Send feedback by email](mailto:jackzhou.work@protonmail.com?subject=%5BPS5%20DualSense%20Controller%20for%20FFXIV%5D%20Feedback), or open a [GitHub issue](https://github.com/Jackoltrade/PS5-DualSense-Controller-for-FFXIV-/issues). Include the plugin version, game build/region, controller model, connection type and steps to reproduce. The About tab's email button fills in a fixed plugin-specific subject; you send the message yourself.

## Common questions

**The controller goes dark after Alt-Tab.** This is the intended handoff. Other software can set its lighting when it next writes to the controller. The plugin cannot force Steam or another app to reapply its own settings.

**Combat haptics are missing, but native vibration works.** Confirm USB, enable character combat haptics, check strength, then inspect Diagnostics. A distant impact may be past the cutoff. Unknown-position sounds are skipped while spatial distance fade is on. Native feedback and the added skill feed are separate.

**An animation seems still.** Check lighting is enabled and FFXIV is active. Choose at least two LEDs for Chase. Bounce with five LEDs fills the whole strip, leaving no visible movement.

**Can Steam, DS4Windows or another lighting tool run too?** The plugin uses shared handles and releases lighting control in the background. In FFXIV, competing tools can still send their own commands. For the simplest setup, let this plugin own lighting while the game is active and retain native DualSense support.

## About and support

About is the rightmost tab. It lists version, author, license, compatibility and credits, with centered text and icon buttons. The support buttons sit together, as do the email and GitHub feedback buttons. GitHub feedback opens a new issue for you to describe and submit. Like my work? Buy me a coffee—or a bubble tea. [Support on Ko-fi](https://ko-fi.com/K7C122MEWU). **The Afdian button will be enabled when its profile URL is supplied.** This plugin is completely free.

## Build from source

Install the **.NET 10 SDK** and obtain compatible Dalamud API 15 libraries. Then, from the repository root:

```powershell
./tools/build.ps1 -DalamudLibPath 'C:\path\to\Dalamud'
```

The script can discover local XIVLauncher/XIVLauncherCN libraries. `DALAMUD_HOME` also works. On this project's WSL development setup, with the Windows runtime and an existing local Roslyn compiler:

```bash
python3 tools/build-wsl.py
```

Both paths build optimized x64 code, run native/core/localization checks, verify DLL identity/version/API level, and publish into `artifacts/plugin/`. The offline compiler is a build-time dependency and is not bundled. A read-only USB capability probe is available with `-Probe` or `--probe`.

```bash
python3 -m unittest discover -s tests -p 'test_*.py'
python3 tools/package-release.py
```

Packaging produces a verified `latest.zip`, testing-only `pluginmaster.json`, checksum and release summary in `artifacts/release/`. It excludes user settings, diagnostic traces, Dalamud binaries and compiler dependencies.

[Maintainer release guide](docs/RELEASING.md) · [QA report](docs/QA-REPORT.md) · [Technical development notes](docs/DEVELOPMENT-NOTES.md)

## License and credits

This project is licensed under **GPL-3.0-only**; see [LICENSE.txt](LICENSE.txt). Controller adaptations draw on [DS4Windows](https://github.com/hbashton/DS4Windows). Native audio/camera bindings draw on [FFXIVClientStructs](https://github.com/aers/FFXIVClientStructs). See [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt) and the bundled MIT licenses for attribution. The plugin runs on [Dalamud](https://github.com/goatcorp/Dalamud).

An independent community project, not affiliated with Sony or Square Enix.
