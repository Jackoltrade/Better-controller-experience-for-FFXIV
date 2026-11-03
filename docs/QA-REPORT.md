# Public test QA report

Version: **0.13.3.0** · Checked: **2026-11-03** · Target: **Windows x64, USB DualSense, Dalamud API 15**

The local release build and automated checks pass. This report separates those results from earlier in-game observations and the checks still needed before a public release. It does not establish compatibility with every regional client or game patch.

## Scope and results

| Area | Evidence | Result and limits |
| --- | --- | --- |
| Release compilation | `python3 tools/build-wsl.py` | Plugin, checks and metadata verifier build successfully using the installed Windows .NET 10 runtime and offline Roslyn compiler. Optimizations and x64 targeting are enabled. |
| Binary identity | `tools/VerifyAssembly` against the actual DLL and installed API 15 libraries | Assembly identity, four-part version, project version, product name, PE architecture and optimization metadata agree. The proof records DLL and source SHA-256 values. |
| Core and native-output behavior | 373,441 assertions in `tests/Program.cs` and its companion checks | Pass. Covers report field preservation, RGB/bottom-LED effects, preview geometry, sound ownership, native haptic routing, spatial direction/distance, footsteps, LB indicators, localization, About URLs and controller-session failure paths. These are fixtures and simulated sessions, rather than a full game integration test. |
| Focus, reconnect and cleanup | Controller-session tests plus source review of `Plugin`, `GameOutputGuard` and native observers | Tests pass for focus races, one-time lights-off release, reconnect, multiple devices, write timeout, cancellation and release failures. Startup now cleans up partial initialization, removes only its own command registration and joins optional observers on unload. Actual hook installation/unload still needs a current in-game smoke test. |
| Enable confirmation | `HapticEnableCueChecks`, native signature/disassembly checks and user testing | Automated checks pass for two equal 100 ms pulses with a 100 ms silent gap, balanced grips, bounded PCM, zero render allocations, no startup/settings replay, cancellation, deadline expiry and current-graph routing. The submix-render and sample-rate signatures each match once in the installed executable. On 2026-11-03 the user confirmed two pulses on enabling v0.13.1 while idle, with native skill/footstep/speaker effects still working afterward. |
| First-open guide | Compiled configuration and localization checks plus source review | New and old configurations start with the guide unseen; the visit flag persists through serialization and both tab resets. Info is always available at the top-left, and the guide uses cached, stable IDs across language changes. The dialog appears only when settings are opened. Its in-game layout and first-open/manual-reopen flow still need a visual check. |
| Feature commands | `PluginCommandChecks`, compiled-configuration checks and source review | Parser checks pass for all six features, utility commands, on/off/toggle, case, whitespace and invalid/numeric input. Compiled configuration checks confirm idempotence, persistence and changes limited to the requested property, including when parent features are off. Commands share the UI snapshot/update path. In-game `/ds5` registration, chat feedback and macro operation still need a smoke test. |
| Configuration compatibility | Separate reflection checks against the compiled `Configuration` class | 1,564 checks cover new-install defaults, independent tab resets, language preservation, persistence and existing saved choices. Internal shutdown defaults remain inactive. |
| Package boundaries | `python3 -m unittest discover -s tests -p 'test_*.py' -v` | Eight tests pass: exact archive contents/testing metadata, repeatable ZIP, changed DLL, stale manifest, wrong tag, changed source, missing notice and unoptimized build. Synthetic payloads test packaging logic; they do not substitute for the real DLL verifier. |
| Workflows | actionlint 1.7.12 on both workflow files | Pass. The build job has read-only permissions; publishing is manual, uses an existing version tag and has a separate write-enabled job. Releases are serialized to avoid concurrent feed updates. GitHub-hosted jobs have not been run. Optional shellcheck/pyflakes integrations were not used. |
| About and translation text | Source/link checks, all-language formatting/glyph checks and a user screenshot | Rightmost About tab, centered wrapped text, source CTA, fixed-subject feedback email, new GitHub issue CTA, project/compatibility/license/credits and translated support prompts are present. A Chinese in-game screenshot confirms centered content and paired icon buttons before the latest spacing adjustment. Ko-fi uses the maintainer-supplied profile URL; Afdian remains unset pending its profile. The revised spacing, English/Japanese layout and actual browser/mail-app launching remain unconfirmed. |
| Documentation | Local link checks on all three user guides and maintainer documents | 33 local file links resolve. Direct UI text and About CTA strings were checked against the translation catalog; the `(?)` marker is language-independent. The English, Simplified Chinese and Japanese guides cover the same installation, configuration, troubleshooting, feedback and build topics. |
| USB capability probe | Compiled checks with `--probe` | One supported USB DualSense interface, output report length 48; a shared handle opened and closed successfully, with zero reports written. This is a capability check, not a physical lighting or haptics test. |

## Changes made during review

- Reuse valid immutable lighting settings instead of cloning them on every render. Invalid, non-finite and out-of-range settings still use the existing normalization path.
- Cache translated ImGui labels while retaining stable English widget IDs, so switching languages preserves tab/control identity.
- Reject unrelated buffer sizes before the global output hook checks the owned-handle set. Supported reports still validate device capabilities on each call, avoiding stale handle identities after reconnects.
- Enable Release optimization and x64 in the local offline compiler, matching the public SDK build's intent.
- Include font, preview, session and guard creation in partial-startup cleanup; check command registration before installing UI callbacks; make disposal idempotent and wait for optional native observers.
- Stop accepting cached player ownership after the ownership observer reports a fault, including when the independent native footstep path remains available.
- Add a build proof and source fingerprint so packaging rejects a changed DLL, changed plugin source, stale manifest, mismatched tag or missing required notice.

The About tab uses Dalamud's existing link-opening helper only when a button is clicked. It adds no background network requests, new timers or controller polling. Shared texture caching and foreground-only output remain in use.

## Performance measurements

Run the compiled checks with `--benchmark`, for example:

```powershell
dotnet ./artifacts/checks/LightingChecks.dll --benchmark
```

The local performance review measured the following synthetic workloads after 2,000 warm-up calls:

| Workload | Measured calls | Average microseconds/call | Managed bytes allocated across measured calls |
| --- | ---: | ---: | ---: |
| RMS over 4,096 samples | 20,000 | 4.167 | 0 |
| Spatial PCM, 16 input channels | 20,000 | 4.755 | 0 |
| Spatial position and impact bias | 100,000 | 0.254 | 0 |
| Lighting render and sound envelope | 100,000 | 0.158 | 0 |
| Cached localized UI label lookup | 100,000 | 0.062 | 0 |

Before reusing normalized settings, the lighting/envelope workload allocated **14,400,952 bytes per 100,000 calls** and averaged 0.282 microseconds/call on this machine. The measured allocation became zero after that change. Timing varies with the machine and load; CI checks warmed allocations, without a brittle timing threshold.

These timings were recorded before the enable-confirmation addition; its separate PCM render allocation check also passes.

This benchmark excludes actual game frames, HID I/O, full window rendering and native hook installation. It does **not** show that the whole plugin is allocation-free or prove an FPS improvement. Native audio processing uses bounded buffers; logging and trace serialization stay outside the audio render callback. Optional sound/LB signature scanning runs during asynchronous initialization rather than on a game frame.

## Existing hardware evidence

On 2026-11-03, the user tested v0.13.1 by enabling combat haptics while idle and confirmed: **two pulses; native effects still work**. This verifies the new cue's physical playback and preservation of the effects included in that check. Startup, slider-change and cancellation behavior have automated coverage; they were not separately confirmed by that response.

Earlier user-confirmed tests covered stable lighting with FFXIV open, controller reconnect, native haptics and speaker effects, RGB/preview agreement, lights-off release on Alt-Tab, own-character combat sounds, spatial grip balance and distance cutoff. Those checks are recorded in the conversation and summarized in [development notes](DEVELOPMENT-NOTES.md).

Native audio/camera research used the `2026.10.06.0000.0000` client with XIVLauncherCN. It is historical compatibility evidence, not a claim that every later build or region has been validated. The current read-only USB probe result is recorded in the table above; it does not prove physical effects.

## Checks still required for the first public release

1. Load and unload this version in the intended FFXIV client. Run `/ds5 help` and test feature commands with the settings window closed; confirm localized chat feedback, saved values, parent requirements and no duplicate enable cue when repeating `on`. Open settings for the first time, dismiss the setup guide, then reopen settings and reload the plugin to confirm it does not repeat automatically. Check the top-left Info button and language selector reopen/translate the guide correctly. Open `/ds5`, select About and check English, 简体中文 and 日本語 for centered text, paired icon buttons, clipping, missing glyphs and tab order. Confirm the source button opens this repository, GitHub feedback opens its new-issue form, and email feedback opens a draft to `jackzhou.work@protonmail.com` with subject `[PS5 DualSense Controller for FFXIV] Feedback`. Do not send an email or submit an issue just to test a link.
2. Switch combat haptics off and on while idle and confirm two short vibrations. Confirm that startup, strength changes and focus return do not repeat them, and disabling or losing focus cancels a pending cue. Verify lighting/preview, native skill/footstep/mount vibration, speaker effects, focus loss/return and USB reconnect on this build. Record the actual controller model, client region/build and Dalamud version. Bluetooth and untested regional layouts are outside the current compatibility claim.
3. Ko-fi is configured as `https://ko-fi.com/K7C122MEWU`. Confirm its button opens the supplied profile. Add the Afdian URL when supplied, verify it, rebuild and repackage; its button remains disabled until then.
4. After source review and an authorized push, run the prepared GitHub build workflow. Before announcing a release, verify the published feed and ZIP checksum, then test discovery/install/update through Dalamud's testing installer in a fresh installation.

The Windows desktop automation tool failed during initialization before it could list the game window, so it provided no visual or interaction evidence for the new About tab. The current limitation and the pending checks above must remain visible in release decisions. No GitHub release or installer feed has been published by this QA work.
