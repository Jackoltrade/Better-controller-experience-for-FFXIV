# Better controller experience for FFXIV — User guide

[Quick start](../README.md)

[English](USER-GUIDE.md) · [简体中文](USER-GUIDE.zh-CN.md) · [日本語](USER-GUIDE.ja.md)

Custom DualSense lighting and game-driven vibration for Final Fantasy XIV, built with Dalamud. Feel your own skills, enemy effects and camera shake, while keeping the game's native DualSense features.

**Windows x64 · Dalamud API 15**

Diagnostics and diagnostic commands described in this guide require an explicit `-Development` build loaded through Dalamud's development flow. See [Development builds](RELEASING.md#development-builds).

[Source](https://github.com/Jackoltrade/Better-controller-experience-for-FFXIV) · [Releases](https://github.com/Jackoltrade/Better-controller-experience-for-FFXIV/releases) · [Report an issue](https://github.com/Jackoltrade/Better-controller-experience-for-FFXIV/issues) · [Support on Ko-fi](https://ko-fi.com/K7C122MEWU)

![Controller lighting preview](../src/DualSenseLighting/Assets/dualsense-preview.png)

[Install](#install) · [First setup](#first-setup) · [Lighting](#lighting) · [Haptics](#haptics) · [Commands](#commands) · [Troubleshooting](#troubleshooting)

## Features

- **RGB lightbar:** solid color, Rainbow, Breathing, Color Shift, Aurora, Fire, Heartbeat, Comet, Rainbow Pulse, and two SFX-reactive modes.
- **Bottom LEDs:** choose the number and animation of the five white lights below the touchpad, or display Limit Break status.
- **Combat feedback:** separate controls for your skill/weapon sounds, boss skills, regular enemy skills and your character's hit reactions.
- **Direction and distance:** balance combat feedback around your character using the camera's facing, with optional distance fade.
- **Screen effects:** vibration follows camera shake, radial/directional/Gaussian blur and chromatic aberration.
- **Native footstep amplification:** optionally strengthen existing USB DualSense footstep vibration without amplifying mounts or speaker audio.
- **Live preview and three languages:** preview your lighting, tune SFX response with a live meter, and switch between English, 简体中文 and 日本語.

When FFXIV loses focus, the plugin turns its lights off once, releases lighting control and pauses all added vibration. Returning to the game or reconnecting restores your saved settings. The settings live in Dalamud; they are not permanent controller firmware settings.

## Compatibility

| Controller and connection | Lighting | Plugin-added vibration |
| --- | --- | --- |
| Sony DualSense / DualSense Edge, USB | RGB and bottom LEDs | Detailed native haptics, combat/screen feedback and optional footstep amplification |
| Sony DualSense / DualSense Edge, Bluetooth | RGB and bottom LEDs | Basic rumble for combat and screen effects |
| Xbox-compatible XInput, USB or wireless | Not controlled | Regular rumble for combat and screen effects |

- Requires Windows x64, FFXIV and **Dalamud API 15**. Other operating systems and raw DirectInput force feedback are not implemented.
- Detailed DualSense haptics, native footstep/mount textures and controller-speaker effects require **USB**. Enable controller vibration in FFXIV and configure its native DualSense features in the game as usual.
- Bluetooth DualSense support uses direct HID commands. It needs no mapper, virtual-controller driver or companion app, but does not provide Bluetooth audio-haptic streaming or controller-speaker audio.
- XInput output combines added rumble with the game's motor requests. A DirectInput-only device needs an XInput mapper. The plugin does not remap buttons or pair controllers.

This plugin is distributed through a custom repository and has not been reviewed for Dalamud's official main repository. Native audio/camera bindings were checked against the `2026.10.06.0000.0000` client with XIVLauncherCN. User tests cover USB native features and Bluetooth lighting, rumble, direction and distance on the development setup; they do not verify every controller model, firmware, regional client or future patch. See the [QA report](QA-REPORT.md) for automated checks, historical hardware results and pending live checks. Unsupported native layouts pause the affected feature and report the reason in Diagnostics.

## Install

Use the shared repository below for all interface languages. Packages are also available from [Releases](https://github.com/Jackoltrade/Better-controller-experience-for-FFXIV/releases).

### Dalamud installer

1. Open `/xlsettings` → **Experimental** → **Custom Plugin Repositories**.
2. Add:
   ```text
   https://raw.githubusercontent.com/Jackoltrade/Better-controller-experience-for-FFXIV/main/repo.json
   ```
3. Find **Better controller experience for FFXIV** in `/xlplugins` and click **Install**.
4. Connect your controller, then run `/bcef`.

The shared feed includes English descriptions and Chinese launcher translations. The plugin's own interface supports English, Chinese and Japanese independently of the installer text. Existing release-hosted repository URLs remain supported for current subscribers.

Enabling testing builds in Dalamud is not required.

### Manual/dev-plugin installation

1. After a release is published, download `latest.zip` from [Releases](https://github.com/Jackoltrade/Better-controller-experience-for-FFXIV/releases), or use your locally built `artifacts/plugin/` folder.
2. Extract the entire ZIP into a dedicated folder.
3. In `/xlsettings` → **Experimental** → **Dev Plugin Locations**, add that folder's `DualSenseLighting.dll`.
4. Enable the plugin in `/xlplugins` → **Dev Tools**.

The release ZIP and `artifacts/plugin/` contain production builds; loading them through Dev Plugin Locations does not enable Diagnostics. For recording, use the [separate development build](RELEASING.md#development-builds).

Use one installation at a time. Disable the old **Steam Lighting Guard** before loading this plugin, and remove the dev-plugin entry when moving to the installer. The DLL/internal ID remains `DualSenseLighting` to retain existing settings and development paths. Disable or uninstall the plugin through the same installer used to load it.

## First setup

1. Connect DualSense by **USB** for detailed haptics, or pair it with Windows over **Bluetooth** for lighting and basic rumble. Connect an XInput controller normally if using that output.
2. For USB DualSense, enable controller vibration and check the native vibration/speaker features you want to retain.
3. On first load, review the guide and choose whether to enable lighting and haptics. New installations start with both unchecked; existing installations show saved choices for a one-time confirmation. Choose **Enable selected effects** or **Keep effects off**. Reopen the guide with **Info** at the top-left or `/bcef info`.
4. Open **Haptics → Vibration output**. **Automatic** prefers USB DualSense haptics, then DualSense Bluetooth rumble, then one XInput controller. Select a mode explicitly when using multiple controllers or a remapper. For XInput, choose the first connected slot or controller 1–4.
5. Use a skill, then adjust **Player vibration strength** and **Enemy vibration strength** to taste. Bluetooth/XInput users can also tune the basic-rumble controls below.
6. Alt-Tab and return once to check the lighting handoff. Try unplugging/reconnecting while the game is open; active settings should resume without replaying old vibration.

Until you save that choice, controller output, native sound observers and added vibration stay inactive. Closing the guide or choosing **Later** leaves them paused. The guide explains native hooks, compatibility and local processing. Setup consent is remembered separately from viewing the guide and survives tab resets.

Hover controls or **(?)** for help, including grayed-out controls. Parent switches disable their children without erasing their values. Settings save automatically. **Language / 语言 / 言語** above the tabs changes the UI immediately and saves your choice.

**Reset to defaults** resets only the selected Lighting or Haptics tab after confirmation. Cancel, Close, Escape or clicking outside leaves settings unchanged. The other tab and language are preserved. Updates retain saved choices; a new default does not overwrite an explicit existing value.

## Lighting

Both side light strips share one RGB color. The controller does not expose individually addressable lightbar pixels, so a wave cannot travel along the strip.

Choose an effect, brightness, available colors and cycle time. Solid uses one color; Color Shift blends between two; Aurora and Fire use preset palettes. RGB and bottom-LED animations have separate timing. The preview uses the same lighting calculation as controller output.

| SFX mode | Behavior |
| --- | --- |
| SFX Loudness | Your color brightens with sound, then fades to black in silence. |
| SFX Color Blend | Blends from Low SFX color to High SFX color as sound gets louder. Low stays visible between sounds. |

**My character only** follows your own action/weapon sounds. **All combat sounds** also includes nearby players and enemies. This source choice affects lighting only; haptics have their own category switches. It reads game SFX, not your microphone or desktop audio.

Use skills while watching the meter beneath **SFX sensitivity**. Lower sensitivity if it stays full; raise it for quiet skills. **SFX fade** or **Color return** controls how quickly the response settles. The automatic noise gate filters faint sound and adapts to sensitivity. **Advanced SFX settings** lets you turn it off and set a manual threshold; too high can miss quiet skills. None of these settings changes game volume.

For **Bottom LEDs**, select a count from Off to five and an animation: Steady, Chase, Bounce, Blink, Heartbeat, Sparkle or Loading. Chase, Blink and Loading stay within the chosen positions. Bounce and Sparkle use all five positions, with the count determining how many light at once; Heartbeat pulses the chosen number together. Select at least two for visible Chase movement, and fewer than five for visible Bounce movement.

**Limit Break** mode shows ready party bars in PvE and your own charge in PvP, with a quick flash on your LB cast/use. Its blink/loading cycle is separate from RGB timing. If there is no active gauge or supported LB binding, Diagnostics explains why the display is paused.

## Haptics

### Master switch and combat categories

**Enable haptics** controls all plugin-added vibration. Under it, **Combat**, **Screen effects** and **Footstep amplification** are separate sections. Turning the master off pauses all three and keeps their settings. Native game vibration, speaker audio and trigger commands retain the game's behavior.

**Enable combat haptics** controls Player SFX based, Boss skills, Regular enemy skills and Player hit reactions together. Turning it off leaves screen effects and footsteps available.

| Combat category | What it follows |
| --- | --- |
| Player SFX based | Your own skill and weapon sounds; separate player strength |
| Boss skills | In active duty boss combat, verified NPC SFX and eligible arena effects, including basic attacks, casting and warning cues |
| Regular enemy skills | Confirmed skill effects from ordinary hostile enemies; basic attacks remain excluded |
| Player hit reactions | Your character's hurt/guard sounds when enemies hit you, including basic attacks; separately optional |

**Enemy SFX based** groups **Boss skills**, **Regular enemy skills** and **Player hit reactions**. They share **Enemy vibration strength** and work independently of **Player SFX based**. Already-native vibration is preserved rather than added twice. The plugin does not change audible SFX, speaker volume or adaptive triggers.

**Boss classification:** inside an instanced duty of any size, **Boss skills** uses a broader rule while both you and a recognized boss are in combat. It needs a verified NPC sound source or eligible arena-effect source; unidentified audio, other players, pets and confirmed player-side NPCs are excluded. Music, dialogue, UI sounds and looping arena ambience are excluded. It is not an “all game audio” mode.

The plugin remembers regular hostile NPCs identified **before boss combat**, even while added vibration is off. Those exact NPC instances keep the Regular enemy skills filter during the boss fight. The memory survives combat ending, focus loss and feature toggles, and resets on territory/duty/player-entity change or plugin reload. A regular enemy first seen mid-fight can follow Boss skills. Reloading mid-fight cannot recover earlier sightings. This grouping does not claim that every new NPC is a boss summon.

Outside active duty boss combat, confirmed-skill and linked-mechanic filtering remains in use. Recognized boss FATEs and registered B/A/S Hunt marks follow Boss skills; ordinary FATE enemies remain regular. Boss recognition reuses [LuminaSupplemental's data](../data/README.md) and installed game metadata. An unrecognized boss cannot activate the broad rule on its own. Some unknown owners, playback layouts or ambiguous skill layers remain unsupported; testing one mechanic does not establish all-boss coverage. See [enemy coverage and limits](ENEMY-HAPTICS-COVERAGE.md).

### USB, Bluetooth and XInput output

USB DualSense uses the game's native vibration processing for detailed sound texture. Basic Bluetooth/XInput rumble instead turns approved combat SFX onsets into short impacts, avoiding a continuous buzz through long sound tails. Lighting can be off while vibration is on.

Under **Vibration output → Basic rumble**:

- **Rumble strength:** combat-rumble limit, default **50%**. Player/enemy strength and distance fade still apply. Screen-effect strength is separate.
- **Pulse duration:** default **120 ms**, adjustable from **60–300 ms**. Longer pulses feel heavier.
- **Test rumble strength:** a longer weak pulse followed by a stronger pulse, using your strength limit.
- **Test left/right motors:** left channel, then right, using your current strength and balance.

Stand idle for either test. Other added vibration pauses briefly, and losing focus cancels the test. These controls are hidden for USB native output, including Automatic when USB is present. Basic rumble targets one controller; lighting can apply to multiple connected DualSense controllers.

Bluetooth keeps the shared combat left/right gains. Its heavy/low-frequency left channel and light/high-frequency right channel can feel different at equal command values. **Bluetooth motor balance** defaults to **+85%**: left commands are reduced to 15%, and right commands are unchanged. Positive values soften left, negative values soften right, and zero sends equal commands. It applies after combat/screen mixing and to confirmation/tests. Adjust by feel; this is a user-selected preset, not automatic calibration or a way to make the textures identical. Saved custom values are preserved.

Compatible Bluetooth firmware uses improved compatible rumble when supported. Diagnostics shows the selected mode, current motor commands and retained test commands after the motors stop. XInput uses a frequency-based motor balance rather than precise left/right panning, and combines added vibration with the game's own requests. Explicit XInput mode can use a virtual Xbox controller from a mapper; Automatic prefers a detected physical DualSense.

### Shared direction and distance

**Advanced combat settings** applies to player, boss, regular-enemy, hit-reaction and arena feedback. The controls remain available with Player SFX based off.

**Spatial haptics** uses your character's position for the center and the camera's facing for left/right, regardless of character facing. **Distance fade** works independently of Spatial haptics. Recommended character distance keeps full strength within **3 yalms**, fading to zero at **25**. Missing positions/listener data suppress added combat feedback while fade is on; native vibration remains separate.

**Camera** is another distance origin. Its recommended limits add the camera-to-character distance, following zoom. Turn off **Use recommended distance fade** to set manual limits; character and camera limits save separately. Camera zoom does not affect character-based distance.

Under **Player SFX settings**, **Follow skill impacts** steers character-origin weapon/casting layers toward a recent audible impact from your own skills. It requires Player SFX based and Spatial haptics. **Impact bias** ranges from **25–100%**, changing direction only, not source distance. Guidance is approximate when skills overlap. Left/right meters in Diagnostics show calculated gains, not measurements from the physical actuators.

### Screen effects

Screen feedback follows actual camera shake and supported blur/chromatic effects alongside other vibration, with combined output limited. Finished effects are not replayed later. Ordinary camera movement, zoom, bloom and depth of field do not trigger it; Black Hole distortion is not yet mapped.

It has its own strength, default **25%**, and ignores combat direction/distance and player/enemy strength. The screen signal is centered; Bluetooth motor balance still applies to its final commands. USB DualSense can reproduce distinct textures; basic rumble follows effect intensity. It pauses in PvP, during zone transitions, without a character and in the background. See [screen-effect inventory and timing limits](SCREEN-EFFECT-HAPTICS.md). Physical display/controller latency still applies.

### Native footstep amplification

This only strengthens footstep vibration that the game already produces. Its switch and multiplier require a **USB-connected DualSense/Edge** with **Automatic** or **DualSense haptics** output. Bluetooth, XInput and disconnected devices disable the controls while keeping the saved choice.

It follows the full haptics master independently of Combat, pauses while mounted or in the background, and supports **100–600%**. At 100%, native strength is unchanged. It does not amplify mount vibration, speaker audio or combat feedback. Native footstep strength can differ by character/race; a multiplier does not equalize those differences.

### Defaults

First setup starts with lighting and haptics unselected. Your haptics choice controls the master; the category presets below stay saved even if you leave it off. **Reset to defaults** on the Haptics tab restores this preset and turns its master on. Updates retain saved values.

| Setting | Default |
| --- | --- |
| Enable haptics | Your setup choice; on after Haptics reset |
| Enable combat haptics | On |
| Output | Automatic; first connected XInput slot if needed |
| Player SFX based | On, 25% strength |
| Boss skills / Regular enemy skills | Both on, 100% enemy strength |
| Player hit reactions | Off |
| Screen effects | On, 25% strength |
| Spatial haptics / Distance fade | Both on; character origin; recommended 3–25 yalms |
| Follow skill impacts | On, 100% bias |
| Basic combat rumble | 50% strength; 120 ms pulse |
| Bluetooth motor balance | +85% |
| Native footstep amplification | Off; saved multiplier 600% |

Enabling combat feedback gives two short confirmation vibrations, including while idle. Normal strength changes and returning to the game do not repeat them.

## Commands

`/bcef` opens settings. `/bcef help` lists commands; `/bcef info` opens the guide. On a development build loaded through Dalamud's development flow, `/bcef trace` starts or stops a diagnostic recording and opens Diagnostics. Update existing macros to use `/bcef`.

Use `/bcef <feature> on`, `off` or `toggle`, for example `/bcef combathaptics off`:

| Feature | Controls |
| --- | --- |
| `lighting` | Custom lighting |
| `haptics` | All plugin-added vibration |
| `combathaptics` | Combat categories together |
| `playersfx` | Your skill and weapon feedback |
| `footsteps` | Native footstep amplification |
| `spatialhaptics` | Combat left/right direction |
| `distancefade` | Combat distance fade |
| `followimpact` | Player impact guidance |

Commands ignore case, save automatically and work in macros without opening settings. They keep child choices while a parent is off; chat explains required parent/USB conditions. Repeating `on` does not repeat the enable cue. `footsteps off` restores native strength, rather than muting native footsteps. Boss/regular/hit/screen switches and numeric settings are currently configured in the UI.

## Diagnostics and privacy

Diagnostics and `/bcef trace` require an explicit `-Development` build from `artifacts/dev-plugin/`, loaded through Dalamud's development flow. Regular release ZIPs and the production DLL in `artifacts/plugin/` have diagnostics compiled out, even when loaded as development plugins. See [Development builds](RELEASING.md#development-builds).

Diagnostics shows controller connections, focus, output selection/errors, motor test results, sound ownership counters, calculated grip gains and Limit Break status.

Your first recording request opens **Before recording**, explaining the metadata collected, both storage locations, file retention and sharing. **Start recording** saves your acknowledgment and starts the requested capture; Cancel, Escape or clicking outside starts nothing. Later recordings remain manual, without repeating the notice. Resetting settings does not clear the acknowledgment.

For a missing or unwanted reaction:

1. Enable the category being tested and turn other added categories off to isolate it.
2. Run **`/bcef trace`** or click **Start SFX recording** in Diagnostics just before the mechanic.
3. Reproduce it, then run **`/bcef trace`** again or click **Stop and save recording**. Note the skill name and approximate elapsed time.

Recording runs until stopped, saves one-minute segments in `sfx-traces/` beside the DLL, and keeps the newest **20**. `sfx-trace.json` is the latest saved segment. Camera timing captures also use `%LOCALAPPDATA%\DualSenseLighting\screen-haptics-timing.json` and a separate `sfx-traces/` archive in that folder.

Diagnostics always shows saved recording size and both file locations, including while idle. Stop recording and wait for saves to finish, then click the trash icon and confirm **Delete recordings**. This removes the latest files and archives for both SFX and camera-timing recordings and cannot be undone.

Traces contain settings, sound/resource names, NPC/ownership metadata, positions, routing decisions, local process addresses and small native-memory snapshots. **They do not record audio and are not uploaded automatically.** Short captures around one mechanic are easier to investigate; long captures can use tens of MB per minute. Review files before sharing them publicly. See [camera timing capture details](SCREEN-EFFECT-HAPTICS.md#recording-timing-in-game).

## Troubleshooting

| Symptom | What to check |
| --- | --- |
| Dev-plugin icon stays blank | Run `/xldev` → **Plugins → Clear cached images/icons**, then reopen `/xlplugins`. Dalamud can retain a missing-image cache across plugin reloads. |
| No controller/output | Connect or pair in Windows first. Check Diagnostics and the selected output. A remapper hiding the physical device can affect detection; select XInput explicitly if using its virtual controller. |
| USB native effects work, added combat does not | Enable the full master, Combat and the desired category; check player/enemy strength. Inspect Diagnostics for binding/ownership errors and distance cutoff. |
| Bluetooth rumble is too rough, weak or uneven | Adjust combat rumble strength/duration, then use both isolated tests. Compare retained commands in Diagnostics. Tune Bluetooth motor balance; the two channels have different textures. Screen strength is separate. |
| Footstep controls are gray | Connect DualSense/Edge by USB and select Automatic or DualSense haptics. Check the master; reconnect discovery can take about a second. This boost cannot create a footstep signal absent from the game. |
| One skill or boss effect is missing | Check category, position/distance and whether the game already supplies native vibration. Unknown owners/layouts can still be skipped. Record that mechanic; no all-boss coverage is claimed. |
| Boss warning cues vibrate too | During active duty boss combat, the broad Boss skills rule intentionally includes verified NPC casting/warning sounds. It is not an impact-only filter. |
| Lights go dark on Alt-Tab | Intended handoff. Other software can reapply its lighting on its next write; the plugin cannot force Steam to restore a saved color. |
| Lighting flickers or is overwritten | Steam, DS4Windows or another app can write concurrently. Let one tool control active-game lighting; retain FFXIV's native DualSense setup for USB features. |
| A bottom-LED animation seems still | Enable lighting, keep FFXIV active and select a nonzero count. Chase needs at least two LEDs; Bounce with five fills the entire strip. |
| A game patch breaks a feature | Check Diagnostics, client region/build and Dalamud API. Native bindings can need updates; an advertised API level alone does not prove audio/camera compatibility. |

## Feedback and support

Open a [GitHub issue](https://github.com/Jackoltrade/Better-controller-experience-for-FFXIV/issues/new) or [send feedback by email](mailto:jackzhou.work@protonmail.com?subject=%5BBetter%20controller%20experience%20for%20FFXIV%5D%20Feedback). Include plugin version, client build/region, Dalamud version, controller model/firmware, USB/Bluetooth/XInput output, settings and reproduction steps. Attach a short trace when useful. The About buttons open a draft or issue form; you submit it yourself.

This plugin is completely free to use. If you enjoy it, [buy me a coffee on Ko-fi](https://ko-fi.com/K7C122MEWU) or [support me on Afdian](https://afdian.com/a/jackoltrade).

## Build from source

Install **.NET 10 SDK** and obtain compatible **Dalamud API 15** libraries. From the repository root:

```powershell
./tools/build.ps1 -DalamudLibPath 'C:\path\to\Dalamud'
```

The script can discover local XIVLauncher/XIVLauncherCN libraries; `DALAMUD_HOME` is also supported. WSL uses the same Windows SDK build. If the SDK is not on the Windows PATH, pass `--dotnet-path /mnt/c/path/to/sdk/dotnet.exe`:

```bash
python3 tools/build-wsl.py
```

By default, the scripts use `Dalamud.NET.Sdk/15.0.0`, build optimized x64 production code, run controller/core/localization checks, verify DLL identity/version/API and stage it in `artifacts/plugin/`. The standard package is `artifacts/plugin/DualSenseLighting/latest.zip`; the public packaging step adds translated installer metadata and READMEs. Use `-LockedRestore` / `--locked-restore` to verify the committed dependency lockfile. `-Probe` / `--probe` adds a read-only USB/Bluetooth capability check without sending controller output.

For Diagnostics, explicitly build the separate development variant:

```powershell
./tools/build.ps1 -DalamudLibPath 'C:\path\to\Dalamud' -Development
```

Load `artifacts/dev-plugin/DualSenseLighting.dll` through Dev Plugin Locations. This optimized build includes diagnostics and produces no installer ZIP. See [Development builds](RELEASING.md#development-builds).

CI also checks build-process arguments and exit codes in Windows PowerShell 5. Run the same regression locally with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./tests/BuildProcessChecks.ps1
```

```bash
python3 -m unittest discover -s tests -p 'test_*.py'
python3 tools/generate-enemy-bosses.py --check
python3 tools/package-release.py
```

Packaging creates `latest.zip`, the regular installer feed `pluginmaster.json`, checksum and release summary in `artifacts/release/`. It includes the three READMEs and required licenses, and excludes user settings, traces, Dalamud libraries and compiler dependencies. Packaging locally does not publish anything.

[Release guide](RELEASING.md) · [QA and evidence](QA-REPORT.md) · [Development notes](DEVELOPMENT-NOTES.md) · [Enemy coverage](ENEMY-HAPTICS-COVERAGE.md) · [Ownerless-summon research](OWNERLESS-SUMMON-INVESTIGATION.md) · [Data provenance](../data/README.md) · [Changelog](../CHANGELOG.md)

## License and credits

Licensed under **GPL-3.0-only**; see [LICENSE.txt](../LICENSE.txt). Controller adaptations draw on [DS4Windows](https://github.com/hbashton/DS4Windows); native audio/camera bindings draw on [FFXIVClientStructs](https://github.com/aers/FFXIVClientStructs); boss lookup data comes from [LuminaSupplemental](https://github.com/Critical-Impact/LuminaSupplemental). The plugin runs on [Dalamud](https://github.com/goatcorp/Dalamud).

See [THIRD-PARTY-NOTICES.txt](../THIRD-PARTY-NOTICES.txt), [HidLibrary's license](../LICENSE-HidLibrary.txt), [FFXIVClientStructs' license](../LICENSE-FFXIVClientStructs.txt) and [data provenance](../data/README.md) for source and attribution details. No sound-mod pack is required or bundled.

An independent community project, not affiliated with Sony or Square Enix.
