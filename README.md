# Better controller experience for FFXIV

[English](README.md) · [简体中文](README.zh-CN.md) · [日本語](README.ja.md)

Custom controller lighting and vibration for Final Fantasy XIV. Feel skills, enemy effects and screen shake while keeping the game's native controller features.

**Public testing project · Windows x64 · Dalamud API 15**

## Features

- **Lighting:** RGB effects, sound-reactive colors and animated bottom LEDs, with a live preview and Limit Break display.
- **Combat haptics:** separate player, boss, regular-enemy and hit-reaction controls, with shared direction and distance settings.
- **Screen effects:** vibration follows camera shake and other visual effects.
- **Footsteps:** optional amplification of native USB DualSense footsteps.
- **Background protection:** added effects pause when you switch apps and resume when you return or reconnect.

The interface supports English, Simplified Chinese and Japanese. Settings save automatically.

## Vibration curves

Player and enemy effects scale with their sound; screen effects use a stronger response for subtle movement. **The screen-effect curve shows the deliberate boost to weak effects.** These curves show digital signal targets, not measured vibration force.

![Vibration response curves at default settings](docs/assets/haptics-curves.svg)

[Download the interactive curves](https://github.com/Jackoltrade/Better-controller-experience-for-FFXIV/raw/refs/heads/main/docs/haptics-curves.html), then open the HTML file in your browser. Choose a curve and use the sliders below it to explore strength, footsteps, distance fade, grip separation and rumble duration. It works offline and does not change your plugin settings. GitHub READMEs cannot run interactive controls directly.

## Controllers

| Connection | Lighting | Added vibration |
| --- | --- | --- |
| DualSense / DualSense Edge, USB | RGB and bottom LEDs | Detailed haptics, combat, screen effects and footstep amplification |
| DualSense / DualSense Edge, Bluetooth | RGB and bottom LEDs | Basic combat and screen-effect rumble |
| Xbox-compatible XInput | — | Basic combat and screen-effect rumble |

Detailed DualSense haptics and controller-speaker effects need **USB**. Enable controller vibration in FFXIV. DirectInput-only devices need an XInput mapper.

## Commands and macros

| Command | Action |
| --- | --- |
| `/bcef` | Open settings |
| `/bcef help` | List commands |
| `/bcef info` | Open the setup guide |
| `/bcef trace` | Start or stop a diagnostic recording (development installs only) |

Use `/bcef <feature> on`, `off` or `toggle` in chat or a macro:

```text
/bcef haptics on
/bcef combathaptics toggle
/bcef lighting off
```

Features: `lighting`, `haptics`, `combathaptics`, `playersfx`, `footsteps`, `spatialhaptics`, `distancefade`, `followimpact`.

**Update older macros from `/ds5` to `/bcef`.** Command changes save automatically.

## Help and feedback

**Diagnostics** and `/bcef trace` are available only for plugins loaded through the development flow. Regular installs can report problems through **About → GitHub**. On a development install, check Diagnostics for connection and output errors. For a missing or unwanted reaction, run `/bcef trace` before the effect, reproduce it, then run it again to stop. Recordings contain diagnostic metadata, not audio; nothing uploads automatically. Review files before sharing. Size, locations and confirmed cleanup are available in Diagnostics.

This is a testing project; support can vary by client, patch and controller. See the [user guide](docs/USER-GUIDE.md), [coverage limits](docs/ENEMY-HAPTICS-COVERAGE.md) and [QA report](docs/QA-REPORT.md).

[Report an issue](https://github.com/Jackoltrade/Better-controller-experience-for-FFXIV/issues/new) with your plugin/client version, controller, connection type and reproduction steps. A short recording helps with sound-routing issues.

The plugin is completely free to use. [Ko-fi](https://ko-fi.com/K7C122MEWU) · [Afdian](https://afdian.com/a/jackoltrade)

## Development and license

[Build and release](docs/RELEASING.md) · [Official listing](docs/OFFICIAL-LISTING.md) · [Development notes](docs/DEVELOPMENT-NOTES.md) · [Changelog](CHANGELOG.md)

**GPL-3.0-only.** Built with Dalamud, drawing on DS4Windows, FFXIVClientStructs and LuminaSupplemental. See [license](LICENSE.txt), [third-party notices](THIRD-PARTY-NOTICES.txt) and [data credits](data/README.md). Independent community project, unaffiliated with Sony or Square Enix.
