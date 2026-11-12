# Changelog

## 0.20.1 — Lower controller-effect CPU usage

- Reduce active audio-processing work with vectorized loudness measurement while retaining complete sample validation and a scalar fallback.
- Reduce arena discovery overhead with one bounded sound-pool read and reusable scratch storage; retain live sound, ownership and scene validation.
- Build Bluetooth output reports with one final checksum, preserving the exact controller bytes and incoming-report validation.
- Reduce background work without slowing enemy observations or the 33 ms worker: skip unused lighting rendering, already-empty motion-track clearing and output-roster construction when boss combat has closed history enrollment.
- Preserve regular-enemy history and rebuild the complete roster on the first foreground update. Avoid unused cast reads and quiet-frame allocations.
- Include the v0.20.0 production cleanup: regular installs omit Diagnostics, recording and timing tools; an explicitly selected development build retains them.
- Extend compiled-adapter, focus-transition, native-memory and scalar-path checks. Whole-game CPU/FPS gains and physical focus-return latency remain unmeasured.

## 0.20.0 — Performance and production build cleanup

- Reduce audio CPU work by skipping unnecessary sound-ownership checks and sharing validated loudness measurements between SFX lighting and haptics.
- Skip unused enemy cast reads while preserving encounter history and friendly-NPC exclusions.
- Compile Diagnostics, recording, timing counters and inspection probes out of regular installs. Keep them in a separate development build.
- Verify both build variants, native playback preservation and package boundaries; confirm available features on the Chinese client. Whole-game FPS gains remain unmeasured, and international 7.58 gameplay verification is deferred.

## 0.18.0 — Better controller experience for FFXIV

- Restore Chinese installer descriptions for development installs after the SDK migration; keep public packaging tied to standard SDK metadata.
- Rename the Chinese player skill SFX option to 玩家技能音效联动 throughout the UI and guides.
- Migrate to Dalamud.NET.Sdk 15.0.0 with locked dependencies, verified standard packaging and CI artifacts; remove the obsolete offline compiler.
- Integrate settings with Dalamud WindowSystem, preserving sizing/fonts and popup-only Escape handling.
- Remove README installation/start sections and document official-listing submission readiness.
- Use the supplied `thumbnail.png` as the plugin thumbnail in development and release packages.
- Remove the visible email address from About while retaining its feedback buttons.
- Rename the plugin, project file, About metadata, chat messages and release title. Preserve the internal plugin ID and existing settings/install paths.
- Replace `/ds5` with `/bcef` for settings, help, info, recording and all feature commands. Update existing macros to use `/bcef`; the old command is no longer registered.
- Shorten the English, Chinese and Japanese READMEs and move detailed setup, tuning and troubleshooting to linked user guides.
- Shorten installer descriptions, explicitly include regular controller rumble, add Chinese launcher metadata and a Japanese installer feed/package with the same DLL.
- Make Chinese UI text and guides more conversational, with consistent Boss, 小怪 and 手感 wording.
- Use PlayStation's official Simplified Chinese feature term 触觉反馈 for DualSense haptics; keep ordinary motor rumble labeled 普通振动.

## 0.17.10 — Clearer help and recording cleanup

- Add a controller icon with touchpad-side light strips and matching cyan vibration waves to development builds and release packages.
- Enable the Afdian support button with the maintainer's profile link.
- Shorten tooltips and remove explanations already shown by the controls and preview. Remove the SFX lighting prototype labels.
- Rename the haptic categories to Player SFX based, Enemy SFX based and Boss skills; shorten the About support section.
- Always show recording size and both storage locations in Diagnostics. Add a trash button with confirmation to remove saved SFX/camera recordings and their archives.
- Wait for recording writes before cleanup, preserve unrelated files, and report deletion failures. Update English, Chinese and Japanese copy.

## 0.17.9 — Clearer setup and recording dialogs

- Put lighting and haptics choices first, with short descriptions and an always-visible action footer.
- Group compatibility, privacy and file locations in expandable sections. Keep recording disclosures visible and translate the new labels into Chinese and Japanese.
- Use consistent spacing, taller viewport-centered dialogs, a full language label and an always-visible footer without a full-screen tint.
- Repair sound-slot reuse attribution, late hook forwarding and unnecessary arena metadata loading. Verify package links, release recovery and exact-tag checkout before public testing.

- Check native build-process exit codes directly, preserve quoted arguments and add a PowerShell regression in CI; support explicit portable SDK selection.
- Mark the Windows-only assembly for platform analysis; standard SDK builds pass without platform warnings.

## 0.17.8 — First-run choices and recording notice

- Pause added controller output and native observers until the first-run lighting/haptics choice is saved. New users start with both off; existing users confirm saved choices once.
- Explain native hooks, compatibility, local audio processing and the absence of added drivers, telemetry and automatic uploads in English, Chinese and Japanese.
- Require a separate first-recording acknowledgment for UI and `/ds5 trace`, disclosing metadata, both storage locations, retention and sharing. Closing or canceling does not start recording.
- Share canonical camera-timing paths with cleanup instructions. Keep setup and recording choices across reloads and tab resets.

## 0.13.3 — Feature commands

- Replace `/ds5light` with `/ds5`; update existing macros to the new command.
- Add case-insensitive `on`, `off` and `toggle` controls for lighting, combat haptics, footstep amplification, spatial haptics, distance fade and impact following.
- Add `/ds5 help` and `/ds5 info`; retain `/ds5 trace`.
- Save feature changes without opening settings; share the UI update path and combat-enable cue. Report state and disabled-parent requirements in the selected language.

## 0.13.2 — First-open guide

- Show a translated setup guide the first time settings are opened, with supported controllers, USB requirements, game settings and background behavior.
- Add a top-left Info button to reopen it, with a language selector inside the guide.
- Remember that the guide has been shown across reloads and per-tab resets.

## 0.13.1 — Haptics enable confirmation

- Give two short vibrations when combat haptics is enabled, including while idle.
- Use the existing native vibration bus, with a short-lived mixer detour and no rumble-mode HID commands or speaker audio.
- Cancel the cue on disable, loss of focus, unsupported routing or unload; expire old requests instead of replaying them later.
- Keep startup, focus return and ordinary settings changes silent.

## 0.13.0 — Public testing preparation

- Add a rightmost About tab with project, version, author, license and credits.
- Add source-code, support and feedback actions in English, Simplified Chinese and Japanese. Email feedback uses a fixed plugin-specific subject. Enable Ko-fi support with the maintainer’s profile; Afdian remains unavailable until its URL is supplied.
- Center About text and add icon buttons, with support actions on one row and email/GitHub feedback actions on another.
- Add comprehensive user guides in all three languages.
- Add optimized x64/API/version verification, an allowlisted release ZIP, checksum and an opt-in testing repository manifest.
- Add build/package CI and an explicitly triggered public-test publishing workflow.
- Cache UI labels, reuse already-normalized lighting settings, and reduce unrelated WriteFile hook work.
- Harden partial-startup cleanup, command ownership and repeat disposal.

Includes existing RGB/SFX lighting, animated bottom LEDs/LB display, character combat and spatial haptics, optional native footstep amplification, focus handoff, reconnect recovery, confirmed defaults reset, preview edge fades and localized diagnostics.
