# Public test QA report

Version: **0.20.0.0 local release candidate** · Checked: **2026-11-11** · Target: **Windows x64, USB/Bluetooth DualSense, XInput, Dalamud API 15**

## Current verification: v0.20.0 preparation

The project, assembly and plugin manifest agree on **0.20.0 / 0.20.0.0**.
Both optimized build variants pass **61/61 groups with no skips**, normally and
under isolated coverage: production **519,956** assertions; development
**520,035**. Fresh Python checks pass **50 tests**; build-process checks pass
**120 assertions**; runner failure propagation, workflow lint and generated
catalog validation pass. The standard/Japanese local package was generated for
`v0.20.0` from the verified production DLL.

Source SHA-256: `a9ee32979c690b3865b94779d119fe83abd78888901f27da870f4871f77d3507`.
Production DLL: `3c427c5ecab6df7bee65d745e9cb43c3035d591914f629a7979ecd34594ab39b`.
Development DLL: `6575aba0cb4a0d7c24cf66547b16e44fe49591873df4b141b195d19977df9b1d`.
Receipts are under `work/release-0.20.0/`; the independent read-only review found
no confirmed release-blocking code issue.

The local advisory audit remains unavailable (`NU1900`/`NU1905`). A fully green
release gate, whole-game FPS improvement and fresh international-client hardware
coverage are not claimed. The Chinese-client smoke below preceded the rollback
and version bump. The user's latest instruction is **commit locally; do not
push yet**, so neither remote CI nor publication has been triggered.

## Earlier November 11 verification: SN30 Pro workaround rollback

At the user's request, the experimental XInput quiet-SFX boost was reverted.
The original linear loudness response, shared pulse input/amplitude and existing
120 ms default are restored. The extra paired response handoff and its tests
were removed. A regression fixture verifies the original motor level and pulse
stop for player, boss and regular-enemy SFX. Performance optimizations and the
production/development diagnostic boundary remain in place.

Both rebuilt variants pass **61/61 groups with no skips**, normally and under
isolated coverage: production **519,956** assertions; development **520,035**.
The source-union mapping is **4,189/6,305** and **4,574/7,322** lines respectively;
compiled-plugin mapping is **2,365/5,730** and **2,924/7,322**. These are partial
managed-code coverage measures. The lower assertion count reflects removal of
the abandoned response-curve tests; the new linear-output regression adds six
assertions. Results and the initial fixture-setup failure are retained under
`work/revert-sn30-20261111/`.

Source SHA-256: `2936804225a2edd6370d6eb1b2601beac1a5bcebc920120544460c0502847251`.
Production DLL: `61182729727f351dffa64dfe21b96478370885829ffbc6ab5a14cee849b80100`
(606,208 bytes, diagnostics excluded).
Development DLL: `caca05c1303874cdc277212934b91da7a212a52f1718eb67f24cdc40e8ed57b9`
(887,808 bytes, diagnostics included).

The earlier Chinese-client gameplay confirmation below preceded this rollback;
no new hardware test is claimed. Restore still reports the previously documented
`NU1900` advisory-feed warning; no compiler warning/error occurred. No release
was published.

## Earlier November 11 performance and production-boundary verification

The optimized candidate skips unnecessary voice-ownership scans and reuses a
validated PCM loudness measurement when the same block feeds lighting and haptics.
Production compiles out diagnostic UI, recording, timing, probes, buffers,
snapshot-only storage and 157 diagnostic translation entries. Functional motion
filtering, native finite/headroom checks, impact ownership and effect cadence stay.
When audio capture is absent, the update loop also skips unused enemy cast reads
while preserving regular-enemy history and friendly exclusions.

Both normal suites pass **61/61 groups with no skips**: production **520,972**
assertions and development **521,051**. Both isolated coverage suites pass the same
groups. Compiled-adapter tests execute each actual DLL's admission policy, managed
audio path and `Render` callback using owned native-layout buffers. The callback
fixtures cover both output formats, shared lighting/haptics, screen-only operation,
unrelated buses, native haptic flags, invalid PCM, original error results and
player-only ownership. They verify original playback exactly once and unchanged
PCM; they install no live hook and send no controller output.
The compiled full-roster fixture reproduces the prior unnecessary cast reads and
checks that both capture states retain the same regular-enemy mark and roster.

That run's source-union mapping is **4,202/6,319** lines for production and
**4,587/7,336** for development. Within the compiled plugin alone, it is
**2,385/5,744** and **2,944/7,336**, respectively. The source union includes linked
Core code in the test executable; neither measure establishes complete native,
UI or orchestration coverage. The integration receipt and isolated reports are
under `work/performance-20261111/final-builds.log`, `final-coverage.log` and
`coverage-final/`. Earlier integration and negative-control receipts are retained.
Additional checks pass: **50 Python tests**, **120 build-process assertions**,
**49 coverage-process fixtures**, runner failure propagation, catalog and workflow
validation. Intentionally wrong-flavor verification rejects both actual DLLs
without changing accepted proofs. Public packaging rejects the actual development
build before reading installer inputs; standard/Japanese candidate ZIPs contain
only the verified production DLL and allowlisted public files.

Production is **607,744 bytes**, compared with **888,832 bytes** for the separate
development DLL. These are binary sizes, not resident-memory savings. Source
SHA-256: `48b4fc79eaeb679d5e16b895878c717792bfb0559814b086e08da1239dc8b531`.
Production DLL: `abbe7496ab07fef358079184a39d97f4628d41b1ca52398e5b443060e4956a9b`.
Development DLL: `e5df7a54877dcb0889f5d85428f68d7da9fd3b7fcd14e8c397b9191597ccdbdb`.

The explicit dependency audit is **incomplete**: the official advisory feed was
unreachable (`NU1900`/`NU1905`). Normal restore also emitted `NU1900`; there were
no compiler errors or compiler warnings. Do not report a completely green release
gate or a completed vulnerability audit. No new dependency or release publication
was introduced. Raw results are under `work/performance-20261111/`.

Paired component timings and zero-allocation controls are described in
[performance verification](PERFORMANCE.md). They do not establish whole-game FPS,
frame-time improvements or physical controller latency. On November 11, the user
completed the requested Chinese-client production-DLL smoke check and reported
**“Available features pass; Diagnostics is absent.”** The requested scope was
usual lighting/SFX/haptics, language switching, Info/Reset Escape handling,
Alt-Tab release and reconnect. This confirms the available features in that
session, not every device/connection combination or an FPS improvement. The
confirmation is retained in `work/performance-20261111/chinese-user-confirmation.json`.
The international 7.58 check is deferred until that installation is updated; see
[compatibility scope](COMPATIBILITY.md). Earlier hardware observations below
remain historical evidence.

## Historical verification: November 8 review follow-up

The November 8 candidate passed the locked Release build and both complete suites:
**60/60 groups and 519,872 assertions each**, with no skips. The allocation
measurement and coverage-isolation changes are described below. A fresh
user-assisted activation recording and native-effects smoke check also pass.

The original November 7 review passed the normal Release suite but failed the coverage
suite's strict allocation check. Its original result and failed coverage remain
preserved under `artifacts/review-20261107`; later evidence uses the separate
`artifacts/review-followup-20261107` directory. A passing normal run does not
clear a failing instrumented run.

That candidate included impact-aware distance, native friend/enemy
classification, regular-enemy effect ownership and the fixed vehicle-motion
filter. It suppresses the captured vehicle patterns without a learning period.
The user confirmed quiet vehicle feedback with intended effects retained, the
Engels arm fallback, and allied-NPC exclusion in the isolated Trust-party test.
Those are user-assisted checks on earlier builds in this series, not fresh
hardware verification of this follow-up. The tank-ring and missile-impact live
checks remain pending; see [enemy coverage](ENEMY-HAPTICS-COVERAGE.md) and
[screen-effect scope](SCREEN-EFFECT-HAPTICS.md).

First-activation measurements used the installed Dalamud scanner and the actual
game executable's 34,640,770-byte text section in a private mapped image. The
five motion scans took 61.10 ms on the first batch and a 43.98 ms median across
12 subsequent batches. The two recording-observer scans took 32.85 ms first
and a 32.91 ms median. These isolate scanning; they do not measure live hook
installation or a complete game-frame transition. The candidate now prepares
disabled observers in the existing background audio-initialization task;
recording-only observers prepare only for development installs. Existing focus,
permission and recording gates still control activation. No extra task or
initialization framework was added.

Transition timings are saved in the screen trace's `ActivationTiming` field.
Preparation timings cover observer constructors; first motion activation covers
filter/session setup and hook enabling, and first trace start covers session allocation/start.
They are separate intervals, not a total first-use latency or a claim about FPS
or physical controller latency.

The **2026-11-07 23:37 CST** live recording from this candidate reports:

| Native stage | Measured duration | Scope |
| --- | ---: | --- |
| Motion observer preparation | **50.37 ms** | Background constructor: scans, validation and disabled-hook acquisition |
| Recording observer preparation | **33.06 ms** | Background, development installs only |
| First motion activation | **3.75 ms** | First UpdateMotion body: filter/session setup and hook enabling |
| First recording start | **1.35 ms** | Recording-session allocation/start; excludes next-update hook enabling |

All recorded bindings succeeded, with no observer/output errors or dropped or
overwritten diagnostic frames. The user confirms ordinary feedback, native
vibration/controller sound and Alt-Tab release work after reload. This is one
live activation sample on the installed client, not a latency distribution or
physical-controller timing measurement. Camera-composition intervals in the
capture had a 32.64 ms median and 80.82 ms maximum; the maximum spans recording
startup and its cause was not isolated. These stage timings do not establish an
overall frame-time improvement or absence of unrelated frame gaps.

| Fresh check | Result |
| --- | --- |
| Locked x64 Release, installed CN API 15.0.3.8 | Zero warnings/errors; SDK 10.0.403, runtime 10.0.14 |
| Normal suite | **60/60 groups, 519,872 assertions**, no skips |
| Static coverage suite | **60/60 groups, 519,872 assertions**, populated compiled-plugin report |
| Source-union mapping | **4,287/7,107 lines (60.32%)**, 70/71 authored files mapped |
| Compiled plugin mapping | **2,330/7,107 lines (32.78%)**; UI/native/orchestration gaps remain |
| Real-allocation negative control | Private mutant fails the zero-byte assertion at **640,000 bytes**, exit 1; other 59 groups pass |
| Coverage process checks | **49 pass**, including collector failure, missing/empty reports, changed/deleted originals and combined failures |
| Build/runner process checks | **107 build assertions pass**; synchronous/async/malformed-argument failures retain later groups and nonzero exit status |
| Python regressions | **45 pass**, including the manual-only publication guard |
| Uninstrumented helper benchmarks | Five cases report zero managed allocation; synthetic scope only |
| Standard/Japanese packaging | Pass; original release binary hashes unchanged by coverage |
| Fresh live activation/smoke | Recording verifies the stage timings above; user confirms feedback, native vibration/sound and Alt-Tab release |
| Workflow/catalog/whitespace | Pass |

The November 8 release preflight repeats the locked Release and coverage suites
after updating packaging, metadata, icon and feedback destinations to the renamed
repository. Both suites retain the counts above. Default and Japanese packages
are checked for the canonical URLs. The published release used the gated tag
workflow; future publishing is now manual-only and requires an explicit release
request. The earlier native smoke test remains the
hardware evidence for this release; the URL changes do not constitute another
hardware test.

Assertion totals include repeated checks, not independent cases. Coverage maps
authored lines from loaded managed assemblies; it does not cover every plugin
path, game-native function or physical controller behavior.

The original 6,168-byte result and later failing probes are retained. A failing
heap probe found an unused allocation-context filler matching the counter jump,
but the precise runtime counter transition remains unresolved. Delegate
preparation and switching instrumentation alone did not eliminate it; those
passing probes are not presented as proof of its cause.

The shared allocation harness now measures its unchanged callback/warmup/count
contract inside a bounded, serial, process-wide no-GC region. Reservation or
cleanup failure fails the check; the zero-byte threshold is unchanged. Setup is
outside the counters and can perform GC-related work. Cold first-execution,
recurring and real-cache allocation controls, plus exception/cleanup controls,
remain strict. This is a controlled test environment, not a plugin GC policy or
an unconstrained production-allocation guarantee.

Coverage uses Microsoft's static managed instrumentation on isolated shadow
copies. Original input hashes are checked even after failure. Each run retains
its own settings, command, log, report and fingerprints; missing or unpopulated
compiled-plugin coverage fails CI. No instrumented DLL is packaged for release.
See [the allocation investigation](optimization/allocation-diagnostics.md) for
the retained failures and measurement limits.

Helper and stable-roster allocation results apply only to their tested paths.
Device/report loops allocate, and autosave still performs synchronous storage
when it actually saves. The fresh live check covers this initialization
refactor on the tested setup; earlier hardware observations below remain
historical evidence rather than fresh validation of every feature or device.

## Historical evidence: 0.18.0 (2026-11-05)

The following sections retain the earlier build counts, hardware observations
and investigation history. They do not describe the current 0.19.8 candidate.

## Dialog title-bar cleanup

The screenshot showed Dalamud's extra window-options menu overlapping the close
button. Transient dialogs now disable pinning, click-through and background-blur
controls; the menu is absent while Close and Escape remain enabled. The Release
build passes all 56 groups, and an installed-API probe confirms the compiled
dialog's option and close-hotkey state. No new in-game visual pass is claimed.

## Development-only Diagnostics follow-up

Diagnostics now follows Dalamud's runtime `IsDev` flag. Regular and repository
testing installs hide the tab and reject recording requests; a Release build
loaded through the development flow retains it. Compiled UI/command checks cover
install origin, copied consent, timing activation, help and the normal Info path.
The Release and separate coverage runs pass 56/56 groups with no skips.

Fresh mapped coverage is 56.13% across source and 23.92% within the compiled
plugin. UI rendering, audio-adapter and orchestration gaps remain. Helper/roster
zero-allocation results do not apply to device loops or imply an in-game speedup;
autosave storage is still synchronous when it runs. See
[current verification](optimization/verification.md) for identities and scope.

## Historical reliability and maintainability optimization

The follow-up refactor and verification are documented in
[the optimization report](optimization/verification.md). Its results supersede
older assertion counts and selected-helper performance figures below. The
historical in-game observations remain historical; they are not new hardware
validation of this refactor.

The latest user check clarified that Escape closes Info while clearing the game
target. The current build consumes the dialog key before native Tick and passes
the installed key-buffer/WindowSystem probes and 56/56 automated groups. The user
confirms both Info and Reset retain the selected target. This supersedes the earlier Escape success
below. The latest timing sample is 20,286 updates, 0.044 ms average and 0.751 ms
maximum; the earlier 24.489 ms maximum remains unattributed. The shared stack-arrow
sound is now vetoed for BossEncounter actors while the impact remains eligible.
The train trace and a user toggle test identify screen-effect feedback rather
than boss SFX; no speculative environmental filter was introduced. The user also
confirms quiet stack arrows with the actual hit retained and the full requested
current lighting/preview, three-language, native vibration/sound, Alt-Tab and
reconnect smoke checks. The final independent assessment is 9.1/10; see the
linked optimization report for its rubric and limits.

## Earlier candidate: Dalamud SDK and Windowing migration (0.18.0)

The plugin now uses **Dalamud.NET.Sdk/15.0.0** and its DalamudPackager.
`packages.lock.json` pins the build dependencies; CI uses locked restore.
The project explicitly includes the icon, preview resources and four license
notices. A small packaging target limits the standard ZIP to the DLL, generated
manifest and notices; its icon is a standard sidecar. The obsolete offline Roslyn
compiler has been removed, and the WSL launcher calls the same Windows SDK build.

Public EN/CN and Japanese packages consume and verify the standard SDK output.
They retain localized descriptions that upstream Packager does not serialize,
without modifying its official manifest or ZIP. CI uploads standard and public
packages separately. Internal ID, DLL name, configuration paths and `/bcef`
commands are preserved.

The settings and shared dialog shell use `WindowSystem` and `Window`, retaining
the language font, fixed tab width and settings content. Info, first-run choices,
recording consent, delete confirmation and reset confirmation use the separate
dialog window. Its normal close hotkey remains enabled, allowing Dalamud to own
Escape, focus and native-menu suppression. Closing the settings owner closes its
dialog too. UI visibility is published to the worker only as a discovery signal;
window registration is cleaned up on unload and failed initialization.

The user's first live check found that Escape in Info reached the native game
instead. Investigation confirmed that the first implementation's temporary
`RespectCloseHotkey=false` also disabled native-menu suppression. That workaround
and its popup-state tracking were removed; the shared registered dialog avoids
manual key polling or suppression. The revised dialog build passes its automated
checks. On **2026-11-05**, the user confirmed that Escape in both Info and Reset
closes only the dialog, leaves settings open and does not trigger the native game
menu.

| Check | Result |
| --- | --- |
| Release build | .NET SDK 10.0.403, installed CN API 15.0.3.8: zero warnings/errors |
| Feature regressions | **515,041 checks pass** on both SDK runtime and original .NET10.0.1 host, including compiled Window API registration/delegation/cleanup |
| Clean source build | Copied 149 public source files without Git/build outputs; locked restore and all **515,041 checks pass** using the verified NuGet cache |
| Package/workflow regression tests | **41 pass**, including corrupt/extra/stale standard package rejection and localization preservation |
| Actual package audit | Six standard ZIP entries and ten per public ZIP; final DLL/proof, assets, notices, localization-only differences and untouched SDK output verified |
| Windows PowerShell build process | **106 assertions pass**, including argument preservation and failed process handling |
| Workflow syntax | actionlint **1.7.12** passes; remote GitHub Actions was not run |
| Hot-path allocations | All five synthetic benchmarks report **zero managed allocations** |
| Boss catalog | Generated lookup matches the checked-in source CSV |
| Controller discovery | One USB DualSense found; shared handle opened/closed with **zero output reports** |
| Live dialog Escape | User confirms Info and Reset dismiss correctly while settings and native game menu remain unchanged |
| Documentation and source proof | Updated links, standard/package identity, source/DLL hashes and whitespace checks pass |

On this machine the initial Windows NuGet request could not reach the official
feed. The three pinned public packages were downloaded from NuGet's official
endpoint through the working Linux connection, restored from a temporary local
feed, and cached. That temporary source is removed. Locked restore then passes
with the repository's canonical NuGet configuration; lock hashes match NuGet's
content metadata and cached archives match the official downloads.

The Info/Reset Escape smoke check is user-confirmed. The Computer Use runtime
fails before connecting to the desktop (`sandboxCwd is not a local file URI`),
so this live result is user-assisted. At that earlier stage, other UI and hardware checks were pending:
tab widths, preview and CJK fonts, X/Close and command reopen, lighting and
combat/screen/footstep feedback, native haptics/speaker, Alt-Tab lights-off release
and reconnect. If Dalamud gamepad navigation is enabled, include cancel while a
combo or confirmation is open. Earlier hardware reports below predate this
migration and are not new hardware validation.

Source SHA-256: `ee6a8e724da4953a2191d79d4021157913a7e29b5db59e52e7282e9e779cb40a`.
DLL SHA-256: `261bd977e1a3871d746c163bd0d26d8014ae9f8b14f046a637a3e561c0f1fe98`.

| Local package | Bytes | SHA-256 |
| --- | ---: | --- |
| `latest.zip` | 518,059 | `c3c477f0fdc5a169b3cecd855f84a9c17f55c785d67a687e5f8946a4e89b7567` |
| `latest-ja.zip` | 517,961 | `7ae579214a78b1c7260a7e71e66489319a77422530f16819b9b86bcfdb064285` |

The plugin thumbnail now uses the supplied root `thumbnail.png`, copied
byte-for-byte into the existing icon asset and all development/standard-package
sidecars. It is a 512×512 PNG; both public ZIPs contain that exact image.
The previous source proof was verified before this non-embedded artwork change,
and the metadata verifier refreshed it afterward. The runtime DLL and standard
ZIP are byte-identical; controller tests were not repeated for this icon update.

A subsequent Chinese copy update renames 我的音效联动 to 玩家技能音效联动
in eight UI/help/status values and the Chinese README/user guide. The catalog
comparison confirms only that exact Chinese substitution; English keys and
Japanese values are unchanged. The updated SDK Release build has zero warnings
or errors and passes **515,057 checks**. Both public packages contain the updated
DLL and Chinese README. The clean-copy and original-runtime checks above predate
this wording-only revision; hardware tests were not repeated.

The SDK migration had dropped `DescriptionLoc` and `PunchlineLoc` from the
flat development companion, causing English installer copy in XIVLauncherCN.
`Write-DevelopmentManifest` now adds only these two source fields to the verified
generated metadata for development installs. The standard SDK manifest/ZIP remain
untouched by this overlay, and public packaging reads that standard manifest
instead of development metadata. Regression coverage checks Unicode, generated
identity/default preservation, invalid input and independence from dev-only data.
**106 Windows build assertions**, **41 Python tests**, and **67 checks using the
installed CN manifest classes/renderer IL** pass. The normal locked Release build
reproduces the exact metadata tested by the CN parser and passes **515,057 feature
checks**, with zero warnings/errors. The user confirmed on **2026-11-05** that
both the installer summary and expanded description are Chinese again. No runtime
haptic/lighting behavior changed in this metadata fix.

## Earlier candidate: branding and artwork (0.18.0)

The project file, product/installer name, title bar, About, chat prefix and release
title use the new brand. `/bcef` replaces `/ds5` for settings, help, info, trace and
all eight feature switches. The compiled DLL contains the new prefix and no old
command literal. Internal ID, assembly/namespace and storage paths remain stable
so existing settings and development installs carry over. Historical evidence
below retains names and commands from the versions originally tested.

All three READMEs are reduced from **280 to 64 lines** with detailed, translated
user guides linked separately. Links and release-tag transformations pass. Parent
review covers **82 Chinese copy changes**, using 我的音效联动、Boss 技能、小怪技能、
挨打时振动 and natural 手感 wording. English keys, Japanese values, placeholders
and numeric limits are preserved; six values were tightened during parent review.
The DualSense feature name is standardized as 触觉反馈, following the [official
Simplified Chinese support terminology](https://www.playstation.com/zh-hans-cn/support/hardware/accessories/); ordinary rumble remains 普通振动.

Installer descriptions explicitly include regular controller rumble alongside
DualSense lighting and USB haptics. **49 isolated native-manifest checks** pass
against installed API 15.0.3.8 and Json.NET in English, Chinese and Japanese
cultures. The [CN renderer](https://github.com/ottercorp/Dalamud/blob/5f72511ebfddf65db3cbca5aaec4317dbb7e6d62/Dalamud/Interface/Internal/Windows/PluginInstaller/PluginInstallerWindow.cs#L2227)
uses DescriptionLoc/PunchlineLoc when supplied, with the original on hover,
independent of server/UI language. [Official installed metadata](https://github.com/goatcorp/Dalamud/blob/a74096043b58d69b08af214ac724f3d42c3a238b/Dalamud/Interface/Internal/Windows/PluginInstaller/PluginInstallerWindow.cs#L3027)
uses local defaults, so the Japanese feed includes a Japanese ZIP manifest.
Users choose one feed; changing settings language does not switch installer
metadata. Existing English installations need an update or reinstall from the
Japanese package. Both ZIPs have ten files, identical DLL/assets/guides and the
same name, internal ID, version and API; only four description fields differ.

A follow-up review compares the current catalog with the previously built DLL:
exactly three user-edited Chinese values changed; all English keys, Japanese text,
placeholders and numeric tokens are unchanged. The edits are retained. About no
longer renders the email-address line; both feedback buttons remain available.
The initial build stopped at an existing ownership/allocation assertion; an
unchanged full retry passed. No related test or haptics code was changed. This
intermittent result is recorded without claiming its cause has been identified.

The dev-plugin icon is present at the configured DLL's `images/icon.png` path,
512×512 and byte-identical to source. Eight read-only installed-Dalamud IL checks
confirm negative image caching and the supported `/xldev` → Plugins → Clear cached
images/icons action. The user confirmed that clearing this cache makes the
thumbnail appear. Instructions are included in all three user guides.

The runtime DLL passed its last standard SDK build with **515,025 controller/UI checks**,
with **zero warnings and errors**. Optimized x64/API 15 verification passes and
source stayed unchanged while building. **32 Python tests** and workflow lint
pass, including rejected invalid/stale translations, immutable Japanese ZIP
comparison and partial two-feed upload recovery. Input validation precedes output
writes. No remote publication, installer update or live-game chat test was performed.
The icon refresh result above is user-assisted; About rendering has not received
a new in-game visual check.

A subsequent artwork-only update adds the supplied FFXIV meteor logo behind the
controller in a silver-blue tint. The cutout is an RGBA PNG with a transparent
center and background. The 512×512 icon was visually reviewed and matches both
plugin output folders and both packaged icons byte-for-byte. The original source
matched the previous build before adding the new image assets. Only non-embedded
artwork changed: the runtime DLL is byte-identical, and the optimized x64/API 15
metadata verifier refreshed the source proof. Runtime/controller tests were not
repeated for this artwork-only change. Both generation prompts are retained with
the artwork. The new thumbnail still needs its existing Dalamud image cache
cleared to appear in a running launcher.

The README installation/start sections are removed in all three languages.
Local links and packaged version-pinned links pass. A separate official-listing
readiness note records the current D17/PAC requirements and concrete local gaps;
it does not establish main-repository approval. This documentation-only update
leaves source/DLL proofs unchanged; no runtime/controller checks were repeated.

Source SHA-256: `2ff4a3499f523ca0919a8c18745892265ba708584fc89b813dee79a2cf352746`.
DLL SHA-256: `51a642595a0c4bafd72acb721e40973f495a67583a7a476a6b2951dda1ea29f1`.

| Local package | Bytes | SHA-256 |
| --- | ---: | --- |
| `latest.zip` | 689,295 | `52e0e5c6e8481895f2c60cdce88d8381fa478ce3c937b96e16acaab649292002` |
| `latest-ja.zip` | 689,196 | `4dcec530500bb8bcd651122530a427d87c07a4065c3fee99a4dea26b65ae0f32` |

## Previous audit: clearer help and recording cleanup (0.17.10)

Tooltips now focus on behavior that is not obvious from the controls. The SFX
lighting options no longer carry prototype labels; haptics uses Player SFX based,
Enemy SFX based and Boss skills. About copy and all affected controls are updated
in English, Simplified Chinese and Japanese. These label changes do not change
sound classification or native controller output. The Afdian support button now
opens https://afdian.com/a/jackoltrade.

Diagnostics always displays saved-recording size and both file locations, with
idle refresh. Its trash button opens a themed confirmation popup; Cancel has
default focus, and Cancel/Escape do not request deletion. Recording and save work
block cleanup. Framework scheduling stops the separate camera recorder, waits
for its save task and reserves the trace queue while removing recordings. IO
failures remain visible, and storage refreshes after success or failure.

Cleanup reuses the canonical archive parser, deleting only recognized latest
files and top-level archives in the two recording locations. It validates all
requested locations before deleting and rejects directory/link entries. Existing
unrelated files and subdirectories remain untouched. The **39 Windows temporary
fixture checks** cover both locations, repeated cleanup, deduplication, invalid
names, linked paths, validation failures and real locked-file failure/recovery.
No existing user recordings were deleted during QA.

An isolated native-ImGui fixture exercises the actual compiled confirmation
popup, before the support-link and icon-only updates, in English, Chinese and Japanese at 1280×720 and 640×480, plus 1.5× scale.
All **144 assertions across nine cases** pass: popup and actions remain visible,
Cancel/Escape close without posting, busy Delete stays disabled, and explicit
Delete posts only the framework intent. No handler or real recording path is
invoked by this fixture.

The combined Windows SDK build passes **514,864 controller/UI checks**, including
localization and recording cleanup, with **zero warnings and zero errors**. The
optimized x64/Dalamud API 15 verifier passes; source stayed unchanged during the
build. The lower assertion count than 0.17.9 reflects removal of unused translated
text and its per-character checks. The 24 Python release checks, workflow lint and
generated boss-catalog verification also pass. This update has not been verified
inside the live game; prior hardware observations below remain historical.

Source SHA-256: `85969ced250517cb7b20fce72bd062767bfdcf101fbb90955f811377cc7be32e`.
DLL SHA-256: `304b7b90f8e6323c546e435e5172a608a446af96232b6dc3ed00c45140505942`.

The local ten-file testing ZIP is **682,481 bytes**; its manifest and DLL match
the verified build and all three guides are included. SHA-256:
`8ee1b8197db3269c72348475f77e9ef6192b57123646cb5059df5ff9278941c9`.
No release, tag or repository update was published.

The revised controller artwork uses touchpad-side light strips and matching cyan
vibration waves. The 512×512 PNG, developer-output `images/icon.png` and packaged
image match byte-for-byte. Both the plugin manifest and testing feed contain the
repository `IconUrl`; remote listing artwork becomes available after publication.
The package now includes only this one additional icon, preserving its allowlist.
The stale-icon rejection fixture passes alongside the other release checks.
Image editing used the built-in imagegen tool; its prompt and full-size output
are retained beside the original artwork in Assets. No in-game icon/cache refresh
has been verified during this update.

## Previous audit: consent layout and release validation (0.17.9)

Setup puts lighting and haptics choices first. Compatibility, privacy details and
recording paths expand on demand; the recording notice keeps its data, sharing
and retention disclosures visible. Shared themed popups show the full language
label and a taller body, capped at 90% of the game work area. Body sizing reserves
space around the actual header and footer,
including stacked translated buttons and a bounded save-error summary. Full
errors are available in a tooltip. Save still precedes activation or recording;
closing, Escape and secondary actions grant no consent. New text is translated
into Simplified Chinese and Japanese. An isolated real-cimgui fixture confirms
popup and footer bounds in seven English/Chinese/Japanese, scaled and narrow
720p cases, including save errors. It reproduced parent-centered off-screen
placement, repaired by centering within the main viewport. Parent review of two
English renders confirms readable initial content and visible action rows. These
use actual compiled popup methods, not a recreated UI; the private font/theme and
software rasterizer do not establish every in-game language/theme combination.

The final combined build passes **524,818 checks** and the optimized x64/API 15
metadata verifier. Source SHA-256 was unchanged before and after building:
`855bd9a8d451d5518a3e8f9474711e139c73112ad0d1871c9f35df5b1a0374de`.
DLL SHA-256: `0497b9e35681524bd3669e543100dec31ce7de6ec9ee1a88c776d8d4d2256e9b`.

The runtime review reproduced and repaired three defects:

- **Sound-slot reuse:** one copied, generation-validated ownership snapshot now
  supplies the voice's hit/category/position data. The old Core returned another
  sound's coordinates; a repaired finite stress run completed 17.2 million
  lookups without foreign or torn coordinates. This does not prove every native
  lifecycle interleaving.
- **Late native forwarding:** disposed `Hook.Original` calls could throw during
  legitimate unload. Detours use Dalamud's `OriginalDisposeSafe`; actual compiled
  detours forward an owned native sentinel exactly once after completed disposal.
  Upstream trampoline/check-call lifetime assumptions remain; full native unload
  quiescence is not established by that fixture.
- **Unused arena loading:** separate preparation/output gates skip unused
  territory metadata work while prewarming requested duty data before the first
  boss impact. Twelve actual-observer lifecycle assertions cover inactivity,
  serialization, faults and stale territory results.

All five warmed benchmarks allocate zero managed bytes in measured loops.
Current SDK-candidate averages on the original runtime, in microseconds/call:
RMS **4.415**, spatial PCM **5.331**, position **0.271**, lighting/envelope
**0.205**, cached labels **0.078**. A separate
like-for-like experiment measured full voice attribution at 228→137 ns; the
simpler lighting-only lookup pays about 38 ns for added lifetime validation.
Timings vary with load and are not game frame-time or FPS evidence.

Release validation passes **23 Python tests**, workflow lint and generated-catalog
verification. Fourteen package/reference fixtures are cross-platform; nine
Ubuntu workflow fixtures use a fake GitHub CLI and explicitly skip on Windows.
Release retries verify existing versioned assets before adding missing ones or
updating the feed. Stable releases and differing assets fail closed. Explicit
tag references avoid same-named branch checkout. Bundled guides pin omitted
documents/images to the release source tag. The actual nine-file v0.17.9 ZIP is
399,997 bytes, SHA-256
`2ca46f10df5145f7ce2bf66fc85d2719d06a46a6f6bba67979e7855594aeb3fe`.
Its feed is testing-only, API 15; nothing has been published externally.

Manual security review found no supported remote command execution, arbitrary
write or credential-exfiltration path in inspected code. Other players influence
server-validated actors and effect timing, not local commands, pointers or file
destinations. Bounded native/PCM inputs, report validation, cache capacities,
strict trace filenames/retention and release allowlists were reviewed. A
high-specificity scan found no credential signatures in 12 reachable local
commits; this is not an exhaustive secrets/dependency audit. Native game code,
installed plugins/mods and release maintainers remain trusted. Traces stay local
and can include addresses, IDs, paths and positions; no audio or automatic upload
is added. No independent sandbox or vulnerability-free guarantee is claimed.

The standard Windows SDK pipeline now passes using official .NET SDK 10.0.403,
verified against Microsoft's SHA-512. Product and checks build with **zero warnings
and zero errors**. A Windows platform annotation resolves the SDK analyzer
warnings without disabling analysis. All 524,818 checks also pass when running
the SDK-built candidate on the original .NET 10.0.1 host. An isolated offline
Roslyn compilation passes the same checks and metadata verifier, leaving the
SDK-built publication unchanged.

This exposed a build-tool defect: native PowerShell invocation could return no
output and leave LASTEXITCODE at an old value, allowing stale artifacts to be
copied. The shared .NET Process launcher now checks actual process exit codes,
drains stdout/stderr concurrently and preserves native arguments. An explicit
-DotnetPath option selects portable SDKs. The promoted Windows PowerShell 5 CI
regression passes **88 assertions**, including empty/quoted/trailing-backslash
arguments, failed starts, exit 37, large concurrent pipes, SDK detection and host
selection. Its negative control rejects the silent/stale-exit behavior.

Installer preflight passes **46 assertions against actual Dalamud API 15.0.3.8**
and its Newtonsoft models/query/file helpers: testing-only feed deserialization,
global/per-plugin testing selection, API/version predicates, root-level package
lookup, manifest save/reload and copying all nine files in owned scratch space.
Exact-revision installer source confirms first-install testing-exclusive routing.
No live service bootstrap, HTTP, full installer or plugin load is claimed by that
fixture. Published-feed discovery/install/update remains a post-publication
handoff, rather than an unfinished local-preparation gate.
The optional formal security helper refused startup on its private-state
ownership check and produced no report; the manual review above is separate.
Desktop automation failed before window access. The user confirmed v0.17.9
lighting/preview, native haptics/speaker effects, Alt-Tab release and reconnect
all pass. The final language-label/height polish followed that response and has
the headless geometry/render checks above; its final in-game appearance still
needs visual confirmation. Hardware results remain separate from fixtures.
No Git commit, push, tag, GitHub release or installer-feed update was performed.

## First-run choices and recording disclosure (0.17.8)

The setup flag is separate from merely viewing Info. New installations start with
both output switches off; existing saved choices are shown for confirmation once.
Until the choice is saved, runtime snapshots, controller work and optional game
observers remain inactive, with output-guard hooks disabled. Closing, Escape or
Later leaves setup pending. A both-off choice is supported. Feature commands and
resets cannot bypass the setup gate. Consent flags survive reload and tab reset.

UI and command recording requests require a separate first-recording notice.
The Start action saves acknowledgment before queueing a capture. Cancel, Escape
or outside dismissal grants nothing; subsequent captures are still manually
started. The framework checks consent again before starting its trace. Disclosed
metadata includes timestamps, resource paths, positions, IDs, native addresses
and small memory snapshots. No recorded audio or automatic upload is added.

Both the plugin-side SFX trace and Local AppData camera-timing files/archives now
appear in the notice and cleanup instructions, using canonical TraceArchive
paths. The localized, focused popups have no full-screen modal tint and use a
display-height limit. Automated checks cover blocked device access/output, native
haptic inactivity, all four feature choices, legacy flags, independent recording
consent, persistence/reset and storage paths.

On 2026-11-05 the user confirmed both in-game checks on v0.17.8: reloading
opened Info with saved choices, closing/Later kept added effects paused, reopening
and confirming resumed the selected effects, and no full-screen tint appeared.
The first recording notice canceled without starting; accepting started a capture,
manual stop saved it, and later manual recordings did not repeat the notice.
Fresh-install choices and every language/layout combination remain covered by
fixtures/source review rather than a separate physical UI test.

The complete optimized x64/API 15 build passes **523,748 checks**.
Published DLL SHA-256: `a206ca299d227c54e27cd4a0a6d8b779e56c4a4dca0ab4e9491e0925eb378392`.

## Screen-effect default strength (0.17.7)

The user requested 25% instead of 35%. Configuration now uses 25% for new
installations and the existing Haptics Reset to defaults path. Explicit saved
strengths are preserved. The screen-default fixture and all three user guides
agree; screen effects remain enabled in the preset. The full optimized x64/API 15
build passes **520,902 checks** and is published at the same development DLL path.

Published DLL SHA-256: `f9e427b22de41f7cceafb86e24104ad62dd926de63aa830530469a311f4ee7bb`.

## Bluetooth default and basic-rumble visibility (0.17.6)

The user found a roughly balanced feel at +85%, by reducing left strength, and
requested that value as the default. New configurations, missing saved properties,
reset and invalid-value normalization now use the shared +0.85 default. Explicit
saved custom balances, including zero, remain configurable and are preserved.
The English, Chinese and Japanese tooltip explains heavy/light rumble, the 15%
left command at +85%, unchanged right strength and the Bluetooth-only scope.

Basic-rumble settings and both tests are hidden when output resolves to native
DualSense haptics. The UI reuses backend selection for Automatic, explicit native,
Bluetooth and XInput, including immediate selection changes and USB preference.
Bluetooth balance is displayed only for Bluetooth output. In-game visibility
and tooltip layout on this build still need a visual check.

The optimized x64/API 15 build passes **520,902 checks**, including persisted/reset
defaults and actual default-balanced Bluetooth packet bytes, plus all previous
native-output, focus, reconnect and localization checks.

Published v0.17.6 DLL SHA-256: `df27bc6e83a088bca8f84ac91bbfd66da0a816bb9725124ea18f6676c353a7ae`.

## Bluetooth motor-balance calibration (0.17.5)

Live testing on v0.17.4 confirmed that the previously fixed-left SFX now follows
direction. The isolated left pulse still feels stronger than the right pulse.
Compatible rumble maps the left to low-frequency/heavy vibration and the right
to high-frequency/light vibration; equal command values do not ensure equal feel.

Added a persisted Bluetooth-only motor-balance slider. Default zero preserves
equal commands. Positive values attenuate left; negative values attenuate right.
For this controller, +50% is a proposed starting point (half left, unchanged right),
not an automatically measured optimum. Calibration cannot equalize the two
different textures or provide audio-haptic streaming.

Calibration is applied once after the Bluetooth combat/screen mix is limited, so
overlapping saturated effects cannot defeat the saved balance. It also covers
enable confirmation and both motor tests. Zero channels stay zero; spatial
direction, fade, stop behavior, XInput and native USB routing are preserved.
Tests exercise actual Bluetooth packet bytes for both balance directions, test
pulses, retained diagnostics, screen/combat overlap and disable. Configuration
default/persistence/reset, invalid values and real queued-frame invalidation pass.

The optimized x64/API 15 build passes **520,776 checks**. The calibrated physical
feel and control layout remain pending user verification.

Published v0.17.5 DLL SHA-256: `78e1e580a1ac825d617c3569692a72931b59facae62b45600050d89be3d4cc82`.

## Bluetooth onset/direction timing and retained motor tests (0.17.4)

The user confirmed camera direction and distance fade work, but reported a weak
right grip and a particular SFX that remained on the left. Turning plugin haptics
off stops that feedback. The reported current motor readings were 0/255 on both
sides; a stop-state reading cannot establish the command during a test pulse.

A forced audio/worker interleaving reproduced a separate code defect: a scalar
onset could arrive before its side samples and reuse the previous louder sound's
grip balance. The regression failed on the old implementation. Bluetooth now
derives onset strength from the same consumed side sample used for steering and
resets steering at each new impact. Tests cover both side orders, quieter opposite
impacts, delayed side delivery and late scalar delivery without replay.

The basic-rumble controls now offer an isolated left-channel pulse followed by a
right-channel pulse, at the same configured strength and duration, with a quiet
gap. Other added feedback is suppressed during both motor tests, and focus loss
cancels them. Diagnostics retains each pulse's actual accepted serialized left
and right command bytes after the final stop, including right-only output.
New tests cover retained readings, reset, stop and release behavior.

The optimized x64/API 15 build passes **520,281 checks** and retains v0.17.3's
USB-only footstep capability behavior. The user confirmed the previously fixed-side
SFX now follows direction; the right isolated pulse remains weaker. The v0.17.5
section records subsequent calibration work. Compatible rumble is not full
audio-haptic streaming.

Published v0.17.4 DLL SHA-256: `0f3053e62d8f11e6467d4de904d6c5bd8d54d282ba32494dc3c11db8fe881453`.

## USB-only native footstep availability (0.17.3)

Footstep amplification's checkbox and multiplier require a detected USB
DualSense/Edge with Automatic or DualSense haptics output. Bluetooth, XInput and
missing USB devices disable both controls. A wrapped, translated message explains
how to make them available. UI, command feedback, diagnostics and native output
share the same HapticOutputSelection capability rule.

The existing scanner also discovers devices for explicit native mode and while
settings are open with all output features off. Discovery itself writes no reports;
hotplug availability updates on the existing one-second scan interval. Native
output suppresses the added footstep route when unsupported and invalidates
queued frames on disconnect. Saved choices survive reconnect.

The integration retains the completed v0.17.2 Bluetooth stereo/pulse settings,
including its stereo argument and left/right signal reset. The full optimized
x64/API 15 build passes **519,714 checks**. Footstep checks now include the device
and output-mode matrix, real queued-frame disconnect/reconnect, and settings-only
USB discovery with zero report writes, unplug/replug, focus release and closing
settings with all features disabled. Independent source review found no merge or
routing issue. Current in-game gray-out/hotplug behavior still needs user checking;
HID support does not prove the game vibration/audio endpoint is enabled.

Published v0.17.3 DLL SHA-256: `b16ed7b4256e8c5c1a4283acd312e1e0fdd43b4f08499474044cdfc777d9d909`.

## Basic pulse controls and Bluetooth spatial balance (0.17.2)

The current optimized x64/API 15 build passes **519,575 checks**. Basic combat
rumble has a 50% default strength limit and 120 ms default duration (60–300 ms).
Pulse fixtures cover onset response, finite duration, faded stops, sustained/jittery
sound suppression, repeated attacks, invalid input and zero steady-state allocation.
Persistence/reset/default fixtures and in-flight settings invalidation pass.

Bluetooth firmware metadata and improved compatible-rumble packets have independent
CRC vectors and threshold/Edge/fallback checks. The longer strength test has distinct
weak/strong motor commands, pauses other added layers, and stops on focus loss.
Its diagnostic range is read from serialized packets after successful writes.

BluetoothStereoChecks exercises the actual native-output handoff and RumbleSession:
right impact, camera reversal to left, balanced spatial-off mode, distance fade,
25-yalm cutoff, stereo generation invalidation, backend switch and unchanged PCM.
XInput keeps a frequency-based motor balance rather than pretending to have
identical directional actuators. Native USB haptics and screen output remain
covered by the existing tests.

Live user evidence: v0.17 RGB and bottom LEDs match the preview. Rumble worked,
but felt rough with screen effects off. On v0.17.1 the user ultimately reported
strength differences and Improved mode, but a persistently weak right side. The
v0.17.2 fix removes that Bluetooth weighting and retains spatial sides. The user
confirmed direction and distance fade, but still reported a weak right grip and a
particular fixed-left SFX; the v0.17.4 section records the subsequent investigation.

## Bluetooth lighting and basic rumble (0.17.0)

The optimized x64/API 15 build passes **517,313 controller and UI checks** and
publishes to the same local development DLL path. No public release is implied.

BluetoothChecks validates the standard 0x31 layout, RGB/white indicator positions,
compatible motor bytes, sequence wrap and independent Python-zlib CRC fixtures.
It exercises both standard and legacy native report headers over every validity
mask, unchanged trigger/audio/microphone fields, checksum repair after filtering,
invalid-checksum rejection and exclusion of larger audio report layouts. HID caps
fixtures cover USB, ordinary Bluetooth and larger Bluetooth descriptor maxima.

Session checks cover preview-consistent combined lighting/rumble, USB/native-field
preservation, one Bluetooth rumble target, lighting-off rumble, static keepalive,
real motor stops, two confirmation pulses, full disable/focus release, reconnect,
connection generations and write failure recovery without replaying old motors.
Automatic/manual backend priority and persisted enum values are also checked.
All prior USB/native-haptic, focus, SFX ownership, spatial/distance, screen effect,
footstep, XInput, configuration and localization checks pass.

The Haptics tab shows the USB-only detailed-haptics note in English, Simplified
Chinese and Japanese. The user confirmed Bluetooth RGB/bottom LEDs match and rumble is present.
Disconnect/reconnect, focus release and the latest grip/spatial fix still need
separate physical verification. Existing
USB live results below are historical evidence, not a v0.17 hardware retest.

## Scope and results

Boss trials target reusable mechanisms, with live verification kept separate from
fixture coverage and encounter-specific fallbacks. See the
[coverage strategy](ENEMY-HAPTICS-COVERAGE.md). Assertion and catalog-entry totals
must not be presented as the number of supported bosses.

| Area | Evidence | Result and limits |
| --- | --- | --- |
| Release compilation | Standard .NET SDK 10.0.403 pipeline plus isolated offline Roslyn compilation | Both compile successfully and pass controller checks and metadata verification. The SDK build has zero warnings/errors; its DLL also passes on the original .NET 10.0.1 host. Optimizations and x64 targeting are enabled. |
| Binary identity | `tools/VerifyAssembly` against the actual DLL and installed API 15 libraries | Assembly identity, four-part version, project version, product name, PE architecture and optimization metadata agree. The proof records DLL and source SHA-256 values. |
| Core and native-output behavior | 524,818 assertions in `tests/Program.cs` and its companion checks | Pass. Covers report field preservation, RGB/bottom-LED effects, preview geometry, sound ownership, native haptic routing, spatial direction/distance, footsteps, LB indicators, localization, About URLs and controller-session failure paths. These are fixtures and simulated sessions, rather than a full game integration test. |
| Focus, reconnect and cleanup | Controller-session tests, source review and user testing | Tests pass for focus races, one-time lights-off release, reconnect, multiple devices, write timeout, cancellation and release failures. The user confirmed v0.17.9 lighting/preview, native haptics/speaker effects, Alt-Tab release and reconnect on the tested setup, before the final popup polish. Startup cleans up partial initialization, removes only its own command registration and joins optional observers on unload. The late-forwarding fixture does not establish full native unload quiescence. |
| Enable confirmation | `HapticEnableCueChecks`, native signature/disassembly checks and user testing | Automated checks pass for two equal 100 ms pulses with a 100 ms silent gap, balanced grips, bounded PCM, zero render allocations, no startup/settings replay, cancellation, deadline expiry and current-graph routing. The submix-render and sample-rate signatures each match once in the installed executable. On 2026-11-03 the user confirmed two pulses on enabling v0.13.1 while idle, with native skill/footstep/speaker effects still working afterward. |
| Screen effect haptics | `ScreenEffectHapticChecks`, configuration combinations and native signature/disassembly checks | Shake, radial/Gaussian/directional blur and chromatic aberration mix independently with native/SFX feedback. USB adds centered screen PCM within each frame's remaining grip headroom; Bluetooth applies its saved final motor balance. Spatial-setting changes preserve screen output. Finished/stale effects are not queued or replayed. Current bindings include the separate scripted LowCut shake producer; see [inventory and evidence](SCREEN-EFFECT-HAPTICS.md) for verified signatures and client limits. Per-effect physical timing and untested client layouts remain outside the fixture evidence. |
| Camera-shake synchronization | `CameraShakeChecks` and screen-output checks | Current post-composition translation/rotation offsets control the envelope, with a fresh scripted sampler when its camera is rendered. Nonzero impulses respond on the next output block; zero stops immediately and held/stale offsets expire. The previous velocity/acceleration estimator and priority delay are historical. Checks cover pauses, irregular frame rates, invalid samples, resets and independent mixing. Normal camera movement is excluded. Software timing does not prove actuator/display synchronization. |
| First-run and recording notices | UserConsentChecks, compiled configuration/localization checks and source review | Setup consent is separate from Info being viewed. Pending setup sends no controller reports or native haptic output; legacy configurations need one explicit choice. Lighting/haptics choices and independent recording acknowledgment persist through reload/reset. Both storage locations use canonical paths. Focused popups avoid a full-screen tint. The user confirmed the v0.17.8 saved-choice setup flow, pause/confirm/reopen, no full-screen tint, and recording cancel/start/stop/save with no repeated notice on 2026-11-05. Fresh-install and all-language layout combinations have not each been physically tested. |
| Feature commands | `PluginCommandChecks`, compiled-configuration checks and source review | Parser checks pass for all eight features, utility commands, on/off/toggle, case, whitespace and invalid/numeric input. Compiled configuration checks confirm idempotence, persistence and changes limited to the requested property, including when parent features are off. Commands share the UI snapshot/update path. In-game `/ds5` registration, chat feedback and macro operation still need a smoke test. |
| Configuration compatibility | Actual Configuration source plus compiled reflection checks | Covers new-install defaults, independent tab resets, language preservation, persistence and existing saved choices. All 64 category selections are checked against both parent switches, including old JSON missing the new combat property. Internal shutdown defaults remain inactive. |
| Package and publication boundaries | `python3 -m unittest discover -s tests -p 'test_*.py' -v` | All 23 tests pass on POSIX: 14 cross-platform package/reference tests and nine Ubuntu Bash publication tests that explicitly skip on Windows. Coverage includes exact payload/testing metadata, pinned guide links, proof/version/tag/URL validation, rejected tampering and offline publication recovery. The publication tests use a fake GitHub CLI; they neither publish a release nor replace real DLL verification. |
| Workflows | actionlint 1.7.12 on both workflow files | Pass. The build job has read-only permissions; publishing is manual, uses an existing version tag and has a separate write-enabled job. Releases are serialized to avoid concurrent feed updates. GitHub-hosted jobs have not been run. Optional shellcheck/pyflakes integrations were not used. |
| About and translation text | Source/link checks, all-language formatting/glyph checks and a user screenshot | Rightmost About tab, centered wrapped text, source CTA, fixed-subject feedback email, new GitHub issue CTA, project/compatibility/license/credits and translated support prompts are present. A Chinese in-game screenshot confirms centered content and paired icon buttons before the latest spacing adjustment. Ko-fi uses the maintainer-supplied profile URL; Afdian remains unset pending its profile. The revised spacing, English/Japanese layout and actual browser/mail-app launching remain unconfirmed. |
| Documentation | Local and packaged-guide link checks | Source links and anchors resolve. Bundled-file links stay local in the ZIP; omitted documents and images point to the matching source tag. Current packaged guide text matches the English, Simplified Chinese and Japanese sources after that transformation. Direct UI text and About CTA strings were checked against the translation catalog; the `(?)` marker is language-independent. The three guides cover the same installation, configuration, defaults, commands, privacy, troubleshooting, feedback and build topics. |
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
| RMS over 4,096 samples | 20,000 | 4.415 | 0 |
| Spatial PCM, 16 input channels | 20,000 | 5.331 | 0 |
| Spatial position and impact bias | 100,000 | 0.271 | 0 |
| Lighting render and sound envelope | 100,000 | 0.205 | 0 |
| Cached localized UI label lookup | 100,000 | 0.078 | 0 |

Before reusing normalized settings, the lighting/envelope workload allocated **14,400,952 bytes per 100,000 calls** and averaged 0.282 microseconds/call on this machine. The measured allocation became zero after that change. Timing varies with the machine and load; CI checks warmed allocations, without a brittle timing threshold.

These are current SDK-candidate measurements on the original .NET 10.0.1 host. The separate enable-confirmation PCM allocation check also passes.

This benchmark excludes actual game frames, HID I/O, full window rendering and native hook installation. It does **not** show that the whole plugin is allocation-free or prove an FPS improvement. Native audio processing uses bounded buffers; logging and trace serialization stay outside the audio render callback. Optional sound/LB signature scanning runs during asynchronous initialization rather than on a game frame.

## User-selected Haptics defaults (0.16.2)

Viewed the provided screenshot and cross-checked its visible values against the
saved configuration. New installations and Haptics Reset to defaults now use:

| Setting | Default |
| --- | --- |
| Full master / Combat | Both on |
| Player SFX | On, 25% |
| Boss SFX / Regular enemy skills | Both on, enemy strength 100% |
| Player hit reactions | Off |
| Screen effects | On, 35% |
| Footstep amplification | Off, saved multiplier 600% |
| Spatial / Distance fade | Both on, character origin, recommended 3–25 yalms |
| Follow skill impacts | On, 100% bias |

The existing reset path takes its values from Configuration. Saved explicit
choices continue to round-trip through the category/multi-parent tests; internal
shutdown settings remain inactive. The optimized x64/API 15 build passes
**443,288 checks**, and all three user guides reflect the new defaults.
No fresh physical haptics test is claimed for this defaults-only change.

Published DLL SHA-256: `7b226985f123cc203b8398737f7bec789695aae04e33f376604d8c4243f9ad29`.

## Advanced controls with Player SFX off (0.16.1)

The player-only Follow skill impacts and Impact bias controls moved from shared
Advanced to a separate Player SFX settings header below the player strength.
Shared Advanced now contains only spatial and distance controls and has no
CharacterHaptics dependency. The master and Combat switches still gate the whole
combat section; distance controls follow their own fade/recommendation switches.
Runtime routing and saved settings are unchanged from v0.16.

The optimized x64/API 15 build passes **443,288 checks**, including the existing
player/boss/regular/hit/arena shared spatial matrix, native-send preservation,
focus/reconnect behavior and all-language text formatting. Source scope review
confirms player-only disabled state ends before Enemy SFX and shared Advanced.
Current in-game appearance still needs user verification.

Published DLL SHA-256: `0bb17d2d74c379e2fe43020e56716cd2b3a1ed1fe0b389412ffe706b3704c115`.

## Shared combat controls and Haptics layout (0.16.0)

The optimized x64/API 15 build passes **443,238 controller and UI checks**. The
verified DLL is published to the existing development-plugin path. Its SHA-256 is
`cc6eff6dfdeeb4208729dffd031c5b72397d141e400d1a816b258277f0045f76`.

The full Enable haptics master contains Combat, Screen effects and Footstep
amplification. Combat has a separate group switch with Player SFX, Boss SFX,
Regular enemy skills, optional player hit reactions and shared Advanced settings.
Disabled parents gray out their children while preserving all saved selections.
Spatial direction and distance fade now apply equally to player, boss, regular,
hit-reaction and proven arena sources. The two toggles work independently;
follow-impact bias remains player-only.

`HapticConfigurationChecks` covers 64 category selections across all four
master/combat states, actual gain and audio demand, JSON round trips, old configs,
resets and commands changing only their intended property. The existing master
property is retained; the new CombatHaptics property defaults to true when absent.
`haptics` controls the full master; `combathaptics` controls only Combat.

`EnemyHapticChecks` now covers all five combat routes across spatial/fade toggle
combinations, centered fade-only PCM, original routing when both are off, missing
listener/position behavior, custom and recommended camera ranges, character
cutoff, player-only impact guidance and stale-setting rejection. Native-send
precedence and unchanged source PCM/voice state are asserted. Existing screen
output, native footsteps, enable cue, rumble, focus and reconnect fixtures pass.

Read-only independent review found no blocking issue in production or test logic.
Cleanup removed the fixed EnemySpatial preset/override, player-only UI wrappers,
obsolete last-enemy/hit spatial bookkeeping, fixed-origin diagnostics and unused
Haptics translations. Haptics rendering lives in one partial Plugin file and uses
the existing configuration/snapshot path. All new UI text has Chinese/Japanese
translations. The boss catalog generator and whitespace checks pass.

Current in-game layout, gray-out behavior and physical common-control response
still need user verification on this version. Earlier boss/native/focus live
results below are historical evidence, not a physical v0.16 retest.

## Broad boss SFX and pre-fight regular memory (0.15.0)

The final optimized x64/API 15 build passes **442,958 checks**. Party-size gates
and obsolete eight-player/scripted exceptions have been removed. The new
BossEncounterStateChecks covers scene/player lifetime, before-fight enrollment,
regular exemption, identity reuse, classification, capacity, nested player/pet
ownership, cycles, invalid IDs, stable compaction and warmed zero allocation.
BossNpcSoundChecks covers all three actor-source mechanisms with unknown,
auto-attack and casting tags, exact position, stale lifetime rejection, skipped
marker-name lookup, unchanged legacy filters and audio-bus/native-send guards.
Rewritten mechanic-routing checks exercise the tracker and ownership together,
including queued-voice invalidation and restored Regular filtering.

Source review confirms full object-table candidate/seed collection, pre-audio
tracking, continued history while output is paused, and no party/queue/alliance
size condition in boss engagement. Player and player-owned sources are excluded
before they can activate the rule. The broad path remains available when optional
skill lookup fails, while legacy skill/hit routes are cleared. Existing native
haptic, speaker-send, focus-release, reconnect, localization and compiled-trace
checks pass in the same build. The new policy does not modify audio samples or
native sends and uses the existing spatial route.

On 2026-11-04 the user confirmed both opening and final-impact effects of
吉祥天女's 魅惑拥抱 react on v0.15, with native effects and Alt-Tab release passing.
The retained 05:18:55Z recording has BossSfx true throughout. Miwaku_Firsthalf
keeps its honest Unknown/timeline-3202 action tag yet has 121 sampled Boss/Spatial
voice observations (peak RMS 0.212897). Miwaku_Secondhalf has 330 Boss/Spatial
observations (peak RMS 0.202336), despite its legacy Caster phase. Both sources
use BossEncounter ownership. Native, spawn and arena errors are empty. This
directly verifies that the new route no longer depends on skill/phase eligibility.

The recording retains two regular-enemy marks with no saturation. A separate
in-game check of marked ordinary enemies remaining in their category, and live
coverage of additional duty sizes, remain pending; the counter alone does not
prove audible regular-enemy rejection. Previous v25 and earlier live results
below establish their own versions. An unseen ordinary mob first entering during
the boss fight can still be grouped as Boss; the user explicitly chose the
pre-fight-sighting policy. Loading/reloading mid-fight cannot restore earlier
sightings, and unknown/unpositioned audio remains outside eligibility.

## Scripted boss forms sharing a name (0.14.25)

The optimized x64/API 15 DLL passes **443,421 checks**. The added captured-NPC
fixture proves boss-name recognition and scripted eligibility can overlap, then
checks active encounter/director admission, distinct actor identity, exact
timeline-owner index 62, missing/wrong event and absent-root rejection, and the
existing released C063 positional route. Synthetic timeline evidence in that
fixture is identified as such; it does not manufacture a captured action link.
Parent source review verifies the producer no longer adds a !boss exclusion.

The retained 04:30:26Z v24 recording contains both Miwaku layers at native
timeline-owner index 62, with SourceActor 0. Its giant NPC has name 6385/base
7715 and shares the active root's director 0x80034E50. Every one of 627 frames
retains active player/boss combat. The missing producer admission is therefore
separate from the category/template fixes already verified on v24.

Installed model m0430 assets identify the final second-half sound in
hide/mon_sp001 with C063 duration 1/flags 0x01. Existing untyped execution 4573
and EncounterScript handling can classify that supported form. The first-half
sound is in mon_sp002; its globally shared auto/skill metadata can remain
unknown without stronger action evidence. This fix targets the final impact
without relaxing those ambiguity guards.

An independent installed-data fixture passes 20 checks through actual C063
bytes, full catalog metadata, same-name roster admission, exact index-62
identity, release classification and the positive-playback Boss route. It also
retains the body, auto, inactive/wrong-director and untyped-root guards. Evidence:
`work/enemy-skills/lakshmi-signature-chain.txt`. The fixture verifies the core
chain; source review and the live test below cover Plugin's producer change.

On 2026-11-04 the user confirmed the final impact vibrates with basic attacks
quiet. The retained v25 recording at 04:42:19Z registers the same-name giant as
Boss/EncounterScript at object index 22, rather than the previous index 62.
Miwaku_Secondhalf has Skill/ReleasedEffect timeline 4573 and 376 sampled
Boss/Spatial voice observations, with peak RMS 0.202827. Miwaku_Firsthalf remains
Unknown and its 120 sampled observations stay None/None. Native, spawn and arena
errors are empty. This verifies the producer fix and final-impact path without
depending on an actor address, object slot or sound-name allowlist. It does not
establish every boss's signature attack is covered.

## Uncategorized skills and model-template coverage (0.14.24)

The final combined optimized x64/API 15 DLL passes **440,360 checks**. New fixtures
cover typed category-0 attacks, invalid category/type/timeline rejection, exact
cast identity and action-type guards, cast-before-result history, first-use
model-template lookup, overlapping template ambiguity, literal/history priority,
C012 ownership and warmed allocation-free lookup. Existing native output,
spatial fade, targeting-cue exclusion, footsteps, focus release and reconnect
checks pass in the same build.

A separate audit compiles the actual current Core sources against installed CN
Lumina data: 51,501 Action rows, 25,001 ActionTimeline rows, 12,359 distinct keys,
79 same-key alias groups and 442 model templates. All 12,359 literal keys retain
the expected merged metadata; the complete audit passes 12,399 assertions.
Captured 吉祥天女 actions 8519/8542/8668 now classify as typed skills. Exact
per-actor live cast evidence resolves their concrete model keys before results;
圣锤's literal timeline 5377 resolves directly. 白虎's concrete show/mon_sp001
and hit keys resolve on first use and traverse the existing exact C012 ownership
and Boss route. Basic-attack and prior 月读 guards remain in place.

In that audit, 100,000 warmed calls average 4.605 microseconds for the 白虎
fallback and 3.650 microseconds for an unknown key, with zero managed allocation
in both cases. These are isolated catalog timings, not whole-plugin frame costs.
Evidence is retained in `work/enemy-skills/lakshmi-byakko-catalog-full.txt`.

The original v23 traces establish captured sound, actor and native-effect links;
the catalog audit and fixtures establish the new classification path. On
2026-11-04 the user confirmed 白虎's ultimate and all three 吉祥天女 skills work,
with basic attacks quiet. The retained 04:28:01Z v24 白虎 trace has 118 sampled
Boss/Spatial observations for ByakkohouHit and 130 for Byakkohou. Its distant
aura/re-arm layers use DistanceCutoff, retaining the existing fade policy. The
04:29:03Z trace confirms 74 Boss/Spatial Healwave observations; 光之奔流 and 圣锤
have user confirmation, rather than a new retained trace in this verification.
The same metadata rules apply across encounters without adding
boss/skill/sound allowlists. Ambiguous or unsupported sources can still be
skipped; this does not establish every boss's signature attack is covered.

## Ground-effect category policy (0.14.23)

The actual optimized x64/API 15 DLL passes **386,548 checks**. New reflected
special-backend fixtures verify all three native entry lists, exact ID joins,
stale UI-handle rejection, deferred/incorrect controllers, malformed lists,
bounds, lifecycle transitions, safe unreadable memory and warmed allocation-free
lookup. Gain/spatial checks admit bus 5 only for known arena effects in the Boss
category, preserve existing native-send flags, retain the 25-yalm cutoff and leave
combat-lighting bus selection unchanged. Existing native/report, speaker-send,
foreground release, reconnect and localization checks remain green.

The unchanged product catalogue method was exercised against installed CN Lumina
data: territory 778 contains MoonRelease #0 only; both floor banks and torch are
outside the loaded VFX ancestry. Separate native metadata inspection confirms
the torch's Loop attribute. This scene supplies no looping catalogue candidate,
so the parser run does not directly test rejecting one. The v22 recording
supplies exact native MoonRelease entry 4085 → mixer 495718450 evidence,
rather than a timing-based association.

The user authorized the broader encounter category. It does not require proving
a causal boss-action owner for every ground effect. Eligible map VFX one-shots
follow Boss skills during player/boss combat and use live layout positions.
Native reads remain in framework discovery, and audio callbacks only read
immutable results.

The preserved v23 recording at 03:44:36Z contains one arena source:
MoonRelease #0, pool slot 129, mixer ID 498109006. The matching voice has 101
sampled Boss/Spatial observations with peak RMS 0.321073. The other 978 sampled
environmental-voice observations stay None/None. Native and arena errors are
empty. On 2026-11-04 the user confirmed 月下美人 feedback, quiet ordinary
ambience outside combat, and working native haptics/controller-speaker effects.
This establishes the tested ground-effect path, not universal encounter coverage.

That recording exposed an export-only issue: System.Numerics.Vector3 serialized
as an empty object in ArenaSources. Saving now reuses the trace's existing
X/Y/Z point representation. Compiled-plugin serialization checks verify both
coordinates and playback/layout identity; haptic routing is unchanged by this fix.

## Arena active-handle snapshots (0.14.22)

The optimized x64/API 15 build passes **384,569 checks**. Pool diagnostics now
retain the first active, resource-backed header alongside the latest teardown
state, isolated by slot, generation and native ID. Fixtures prove that resource
clearing and +0x98 index reset cannot erase that first snapshot, and that another
ID/generation/segment cannot inherit it. Haptic routing is unchanged.

The 0.14.21 trace shows MoonRelease's special B0=1 route retains a stale normal
UI controller in +0x40; normal-controller resolution is not valid attribution
for that sound. Read-only engine research separately maps five active special
sounds to their exact live RootSound IDs; another deferred controller remains
unmapped. A bus-five tree ambience example confirms that bus alone must not
classify boss skills. MoonRelease's active special ID and in-duty director/group
ownership still need confirmation; the skill remains unfixed in this build.

## Arena-audio diagnostics (0.14.21)

The optimized x64/API 15 build passes **384,562 checks**. New Windows fixtures
exercise bounded native acquisition/voice/pool/resolution evidence, exact scoped
action metadata, safe unreadable/PAGE_NOACCESS memory, slot reuse and delayed ID
assignment, immutable snapshots, distinct-ID limits, and disabled zero-allocation
callbacks. They allocate fake native objects in the test process and construct
no game hooks. Native audio/report preservation and foreground-release tests
remain green. No new environment haptic admission is enabled.

The 月下美人 trace and installed resources establish a separate map/shared-group
sound path. The current capture lacks a proven reservation-to-playback and boss
association. A fresh 0.14.21 recording has been requested; the skill is not fixed
in this diagnostic build.

## Scripted encounter performers (0.14.20)

The actual optimized x64/API 15 DLL builds and passes **384,527 checks**.
The two retained 0.14.19 recordings prove missing source association: native
owner indices 12 and 8 identify the non-hostile 幻影 and 夜露 actors omitted
from EnemyActors. Both share the active boss's instance director. Combat bus,
flags, positions and combat context are valid; neither miss is distance fade.

`EnemyEncounterScriptChecks` covers the captured Wave/Ultimate metadata and
14 unrelated synthetic identities, active director association, known execution
with untyped presentation, C012, boss-only gain and actual sound positioning.
Negative fixtures reject pets/hostile/owned actors, other director types, missing
layout/root/context, a different active boss event, unknown appearance, body,
cast/bone/auto/action-only sound and inferred helper tags. Event, scene, despawn
and focus/context transitions invalidate queued sound; losing active-root combat
proof does not reset the root's action history. Root-boss untyped preparation
remains excluded. Native report/audio preservation and focus/reconnect checks
also pass. The primary agent reviewed and integrated the delegated work.

The user confirmed both attacks work on 0.14.20. Retained recordings at
00:44:44Z/00:45:28Z verify Wave6429 (45 sampled Boss/Spatial observations) and
complete Ultimate6432 (181), both through EncounterScript with no setup errors.
Their observed bindings are Default, while installed full-catalog checks also
verify the scoped CasterOrigin exception. Unknown Tsubazeriai remains quiet.

Known scripted presentation executions are not all excluded: Gosetsu appearance
6434 also has 85 Spatial observations with nonzero PCM. The unknown appearance
6431 remains excluded. Do not present this as damage-only filtering. This is a
generic active-eight-player encounter policy, not a universal summoner resolver.

## Manual recording and targeting warnings (0.14.19)

The integrated Windows build passes **382,799 checks** and verifies the actual
optimized x64/API 15 DLL. `SfxTraceChecks` exercises that staged assembly with
an empty managed capture, without constructing Plugin or native hooks: recording
continues past 15 seconds, collects a final manual-stop/boundary sample, caps
segments at 1,200 frames, freezes duration and retains session/segment metadata.
Archive fixtures cover timestamp collisions, the latest 20 captures, unrelated
files, failed publication and a persistent Windows reader lock. A reproduced
transient Windows latest-rename denial is recovered by retrying only the same
atomic replacement on the background writer, for at most roughly 500 ms;
permanent errors retain their native details and existing recordings. The
external cause of the temporary denial was not established.

Sixty-eight targeting-cue checks cover the dedicated shared LockOn bank family,
actual impact admission, stale reservation identities, deferred resource names,
contention, native string bounds and warmed callback allocation counts. Installed
Lockon-sheet AVFX resources independently distinguish the captured green-arrow
bank from GunAtk. No native sound buffers, speaker sends or distance/focus policy
were changed. The primary agent reviewed the delegated changes and ran the final
integrated build. The user confirmed Start/Stop and saving on v0.14.19. The
retained 2026-11-04T00:20:59Z capture lasts 34.46 seconds (517 frames, 4.43 MB),
which verifies recording continues beyond the old limit. Its archive matches
the latest file. The arrow-quiet/impact-working result remains pending.

Fresh recordings of the post-add complete 月下彼岸花 and 剑气波动 were
retained on 2026-11-04. Their missing scripted sources are addressed separately
in 0.14.20; the 0.14.19 changes alone did not resolve those source/phase paths.

## Positional caster-origin attacks (0.14.18)

The targeted 0.14.17 line-stack recording at 2026-11-03T16:52:53Z captures
`Tsukuyomi_GunAtk_c` as a correctly owned boss skill on timeline 3209, but the
caster-binding policy rejects it. The signature raid-wide recording at 16:50:27Z
similarly captures `Tsukuyomi_UltRaid_c`, timeline 4573, with 199 sampled
unrouted voice observations, zero output/cutoff blocks and no setup errors.
The round-stack recording at 16:53:30Z confirms the separate target/default
tracks already route spatially. Missing ownerless-add grouping therefore does
not explain these caster-origin misses.

Both missing TMB entries have duration 1 and positional flags 0x01. The earlier
excluded 拉格 execution/body track uses flags 0x05 and bind point 71. Native
resolver RVA 0x192EBD0 independently confirms the positional and bone-bind flag
accesses and actor-origin fallback. Its signature matches once, and startup
validation checks the C063 type, BaseClip.Data load and position/bind offsets in
the original module copy. Unsupported layout disables only the new route.

Flags alone also match 深宵换装 weapon-change sounds, so independent review
prompted an additional positive metadata gate. CasterOrigin promotion requires
recognized AttackType 1..8 with no conflicting setup evidence, resolved in the
existing actor/template pass. The captured attacks have type 5; the observed
weapon changes have type zero. Zero, 0xFFFFFFFF, unknown types and mixed
typed/setup aliases stay excluded. A genuine existing hit-phase track retains
its prior eligibility, including when it uses a skeletal binding.

Two agents independently researched/reviewed the native rule and supplied tests;
the primary agent reviewed and integrated their work. All 379,217 controller/UI
checks pass, including captured and varied attack identities, unchanged
default/target/C012/encounter behavior, native layout fallbacks, bone/cast/voice/
auto/shared/unknown exclusions and warmed allocation-free metadata/binding
lookups. The final DLL is verified optimized x64/API 15. No additional runtime
hook or TMB/SCD file parsing was added. **The user confirmed the line-stack
and initial signature raid-wide impact on v0.14.18.** The complete ultimate
after the add phase is a different, still-missing route. Typed execution audio can include part of a
skill's windup; the rule does not isolate impact-only samples inside an SCD.

## Ownerless spawn evidence and separate stack impacts (0.14.16–0.14.17)

The 0.14.16 trace at 2026-11-03T15:15:02Z captures nine 月读 fan spawns
(name 7229/base 8769), with root name 7225/base 8720. All nine have no owner
or tether, no link family and no active status-source relationship. They share
event context `0x80034E57` with the boss and its same-name helper; shared-group
and event-handler pointers are zero. No temporary spawn-time owner or universal
parent link was established. Spawn/setup/spatial errors are absent.

The recording at 15:16:02Z captures the stack-marker sound as Boss/BossMechanic
and the actual `Tsukuyomi_Smoke_Raid_c` impact as Regular, name 7230/base 8770,
with confirmed Skill/Execution timeline 4575. Boss-only settings therefore react
to the marker but omit the separately attributed impact.

0.14.17 integrates the completed active eight-player encounter-add rule and
adds full acquisition/category regression fixtures for the fan and stack-impact
patterns, varied across fourteen more NPC/timeline identities. The rule changes
the category of confirmed enemy skill audio while both player and boss are in
combat; it does not fabricate native ownership. Context exit invalidates queued
category identities. Tests retain marker feedback, actual impact positions,
basic/unknown sound rejection and boss-body filtering. The trace now records
the duty, queue/member/alliance counts, combat states and include-adds decision.
All 378,977 controller/UI assertions, eight packaging tests, catalog-generation
validation and whitespace checks pass. The build is optimized x64/API 15.
**Live fan routing:** the user reports fan feedback on 0.14.17. The trace at
2026-11-03T16:45:32Z records duty 537, MembersPerParty 8, QueueMaxPlayers 0,
AlliancePartyCount 0, both combat flags true and IncludeAdds true. All nine
fan spawns are represented as Boss/EncounterAdd. Their confirmed skill sounds
use timelines 4573 and 4597; sampled fan voices include 125 spatial boss routes
and 91 distance-cutoff routes. Total boss blocks rise by 1,968 and cutoff blocks
by 974; regular/hit/footstep blocks remain zero, with no diagnostic or spatial
errors. Fifty sampled boss auto-attack observations have no added output route.

The same recording contains no stack marker, no `Smoke_Raid` sound, no name-7230
impact actor and no relevant stack-effect clip. It records fans and the spear
attack, so it cannot establish whether the separate line-stack impact is fixed
or still filtered. A targeted recording starting when stack arrows appear is
pending. Four-player and alliance duties keep their existing association
behavior; this is not a global proof for all stack mechanics.
See the [relationship investigation](OWNERLESS-SUMMON-INVESTIGATION.md).

## Spectral Berserker's rubble and delayed AVFX (0.14.14–0.14.15)

The 0.14.14 trace at 2026-11-03T14:32:04Z captures both 落石 and
兽魂的苦痛. The user confirms 落石 now works. Its rubble actors
(name 9512/base 12030) inherit the live duty-737 boss (name 9511) through the
existing verified-mechanic resolver. The rubble execution timeline 4561 is tagged
BossMechanic; the recording has 1,220 total added boss blocks and no
setup/spatial errors. This confirms rubble attribution, not the other skill.

兽魂的苦痛 remains unknown in that recording. Local action/TMB data and
trace clip metadata identify execution timeline 3216, two C012 effect clips and
no C063 sound clips. The scene effect's later `m0109_sp16_c` audio resolves its
caster but loses the timeline scope. Unrelated root-boss idle/aura AVFX also has
no timeline, so accepting all unknown boss AVFX would be incorrect.

0.14.15 links confirmed C012 execution/hit clips to their exact scene listener,
resource instance and immutable caster identity, reusing the existing timeline
observer. A read-only constructor hook invalidates reconstructed scene objects.
The signature matches once in the installed executable; the clip/container read,
scene getter, listener interface and resource offset are checked against native
code/layout evidence. Fixtures cover the captured skill, basic/shared/cast
rejection, different actors and resources, reconstructed addresses, bounded-cache
collisions, changed native layouts and warmed allocation-free lookup/observation.
All 378,614 controller/UI assertions and eight packaging tests pass. The DLL is
optimized x64/API 15. Native playback calls, audio buffers, haptic routing and the
lights-off release path are unchanged.

**Live 0.14.15 result:** the user confirms 兽魂的苦痛 vibrates while basic
attacks stay quiet. The trace at 2026-11-03T14:50:11Z resolves both timeline-3216
C012 effects to concrete listeners/resources with retained ReleasedEffect tags.
The main `m0109_sp16_c` AVFX sound is Boss/Combatant, name 9511/base 12029,
timeline 3216/ReleasedEffect, with 72 sampled spatial voice observations. Total
boss and spatial blocks each rise by 2,253; regular, hit-reaction, footstep and
cutoff blocks stay at zero. Setup/spatial errors are absent and the skill
observer remains ready. The unrelated `m0109_stlp01` idle/aura sound stays
unowned throughout the recording. This validates the captured AVFX-only skill,
not every possible effect format. Native speaker/mount behavior and Alt-Tab have
automated regression coverage and earlier physical checks, without a new physical
test in this recording.

## Spectral Necromancer's exploding summons (0.14.11–0.14.12)

The 0.14.10 trace at 2026-11-03T13:33:59Z identifies the boss as name 9508,
base 11761 in duty 737. Both exploding summon variants were regular combatants:
name 9509/base 11766–11769 and name 9510/base 11762–11765. They have no usable
native OwnerId. The recorded explosion voice
`SE_Vfx_Monster_c0101_hide_sp01_c.scd` was on combat bus 2, approximately 1.288
yalms from the player, with no skill tag and no output route. This was attribution
filtering, not the 25-yalm distance cutoff.

0.14.11 adds the eight verified duty/name/base associations through the existing
BossMechanics resolver. A live matching boss remains required. The user reported
working explosions with basic attacks quiet. Its trace at 13:43:18Z shows all
eight entries as BossSummon, but only name-9509 explosion audio was routed:
name-9510 voices remained Unknown. A previous auto-attack in that actor's history
was vetoing its asynchronously emitted mechanic AVFX. Total boss blocks rose by
1,759, with no setup errors or regular/hit/footstep blocks; this result was partial
and did not verify both variants.

0.14.12 lets verified encounter mechanic actors emit their AVFX despite that
history. Explicit native auto-attack tags still reject before this rule, and
character/voice sounds stay excluded. Generic native-owned fighters without a
verified encounter association retain the history guard. No timing, filename,
distance or display-name heuristic was added. Tests cover all eight entries,
auto-attack history, explicit basic attacks, cross-duty/name/base mismatches,
missing or wrong boss anchors and preservation of the knife association.

**Live 0.14.12 result:** the user confirmed explosions work with basic attacks
quiet. The trace at 2026-11-03T13:58:45Z shows all eight entries as BossSummon;
the previously missed name-9510 explosion sound and shared effect layer now route
spatially, with 3,323 added boss blocks, zero regular/hit/footstep or cutoff blocks
and no setup/spatial errors. Name-9509 audio was already verified on 0.14.11;
these two recordings establish coverage of both variants, rather than a single
wave resampling both together. Native speaker/mount effects, reconnect and
background release have existing automated coverage but no fresh physical check
in this recording. Ownerless encounter associations remain a verified coverage
list, not a universal native parent link.

## Embedded attack tracks and delayed summons (0.14.9–0.14.10)

The user's level-80 幻光盗贼 recording on 0.14.8 contained confirmed skill
timelines but no added feedback. Local action data identifies 幻光三段 as action
20427, category 3, execution timeline 3981 (`ability/dgr_ninja/abl021`), and
投掷弱鸡匕首 as 20438, category 4, execution timeline 7684
(`human_sp/c0101/human_sp094`). Treating every AnimationEnd timeline as caster
audio excluded their embedded attack tracks. Both share hit timeline 1875 with
basic attacks, so admitting that shared hit key would be incorrect.

0.14.9 distinguishes cast-start from execution metadata and checks each native
C063 sound clip's scheduler actor binding. Default/target-bound execution tracks
can route as released effects; caster-bound tracks stay excluded. The Ninja
target track and default-bound throw qualify while the Ninja caster track and
the previously excluded 拉格 caster track do not. The constructor signature
matches once in the installed client; original code, field writes, C063 type,
header and size are checked. Unrecognized binding layouts skip this extension
without disabling existing helper, regular-enemy or player-hit routing. No new
hook, synchronous asset loading, sound-filename rule or per-skill allowlist was
added. The user confirmed both skills work with basic attacks quiet. That is
physical user evidence; the subsequent recording covered other stages rather
than resampling both original skills together.

The follow-up 0.14.9 trace at 2026-11-03T13:21:55Z exposed four unregistered
weak-knife effect sources. Read-only headers identify name 9507/base 11889,
combatant subtype 5, without the hostile flag or a usable OwnerId. The root
boss has name 9505/base 11756 in duty 737. 0.14.10 adds direct native boss-owner
resolution and an explicit duty/name/base association for this ownerless
mechanic. A live matching boss remains required. Conflicting native ownership
is not overridden. Associated summons use the boss toggle, their actual sound
positions and the existing fixed 3–25-yalm fade. Confirmed auto-attacks and
character sounds stay excluded. The generic native-owner branch has fixture
coverage; the live knives exercise the encounter-association branch.

**Live 0.14.10 result:** the user confirmed delayed knife feedback with basic
attacks quiet. The 15-second trace at 2026-11-03T13:29:50Z includes all four
knife actors as BossSummon, with their actual AVFX
`SE_Vfx_Monster_c0101_m0451_hide_sp02_c.scd` tagged BossMechanic. Voice samples
show that sound routing spatially or being cut off by distance. Total boss
blocks increased by 891; cutoff blocks by 427, with zero regular/hit/footstep
blocks and no setup/spatial errors. The original throw's default-bound track
also routed. These totals include other boss skills, not just knives.

Regression fixtures cover default/caster/target bindings, voice/unknown/shared
rejection, cached and first-use actor links, changed native offsets, the two
reported actions, original caster-track exclusion, all four mapped summons,
native ownership, conflicting owners, missing bosses, actual source positions,
despawns and zero-allocation lookups. Existing native-send preservation, master
switch, spatial fade and focus/reconnect tests pass. Fresh physical native
speaker/mount/focus/reconnect checks remain outstanding. Unknown ownerless
summons are outside coverage until their encounter association is verified.

## Released boss effects and mechanic emitters (0.14.7–0.14.8)

The 15-second 0.14.6 boss-only trace from 2026-11-03T12:18:22Z had no setup
error, but its 1,046 added boss blocks came from the boss body's
`mon_sp/d1047/mon_sp001` track and `SE_Vfx_Monster_d1047_sp01.scd`.
Separate AVFX sources playing `d1047_gim01`, `d1047_gim02` and
`TornadoDragon_RakuraiPC_maint` were unregistered, explaining missing effects.
The pinned boss catalog includes 拉格, NPC name ID 10256, in duty 783.

0.14.7 separated caster/end/cast tracks from released effects but required
helpers to share both the boss's name and base IDs. The user reported no
vibration. Its 2026-11-03T12:44:22Z trace confirms zero boss blocks, no setup
error, successful caster-track suppression, and no registered mechanic helpers.
Fresh read-only headers, cross-checked against the trace's actor entity IDs,
show the fighting boss uses base ID 13294 and the lightning helpers use 9020.
The first post-fight inspection had not established the living boss's base ID;
requiring equality was incorrect.

0.14.8 admits the observed sound-only subtype 11 when its numeric NPC name ID
matches a live hostile boss in the current duty. A different base entry is
expected. This stays limited to known boss identities and sound-only helper
objects; it does not admit arbitrary nearby actors or all environmental audio.
Their AVFX is tagged as mechanic audio without fabricating a specific action
ID. Known basic attacks remain excluded. Unknown helper AVFX is also rejected
if that helper has been observed auto-attacking. Boss character/voice tracks and
known caster tracks stay excluded; regular-enemy skills and optional player
hurt/guard feedback retain their independent behavior.

Regression checks cover the recorded 13-emitter/different-base volley,
caster/hit separation, ambiguous shared phases, body audio suppression, mechanic
AVFX, auto-attacking helpers, mismatched/orphaned helpers, despawns, diagnostic
metadata, regular-enemy compatibility and zero classification allocations.
Existing native-output, master-switch, hit-reaction, 25-yalm fade and background
checks pass. Traces include actor roles, NPC/base IDs and sound phases.

**Live 0.14.8 result:** the user confirmed released effects work while body and
casting sounds stay quiet. The 15-second trace starting 2026-11-03T12:50:13Z
records 4,007 boss spatial blocks, with Player SFX, regular enemies, player hit
reactions and footsteps all disabled. Twelve helper sources each supplied
`d1047_gim01` and `TornadoDragon_RakuraiPC_maint` audio tagged BossMechanic;
another helper's confirmed gimmick sound clip also routed. The boss body's
`d1047_sp01` track remained Caster with IsReleasedEffect false, and its
character audio remained excluded. There were no setup/spatial errors, no
regular/hit/footstep blocks and 19 distance-cutoff blocks. This verifies the
requested separation for the effects present in this recording; `gim02` was
not sampled in this follow-up.

Native speaker/mount effects, reconnect and foreground transitions still need
a fresh hardware check on this build; existing automated checks pass, but do
not establish those physical results. The association does not prove the exact
action or cover every boss's helpers.

## Captured hurt and guard variants (0.14.6)

The 0.14.5 recording reported Player hit reactions enabled, a ready observer,
valid listener, and zero mixed spatial blocks. The local player's PC battle
sound remained unclassified. Recorded player timeline keys were
`normal/damage_continuous` and `battle/weapon_guard`, which the previous mapping
missed. The installer data confirms normal damage rows 62/63, battle damage
68/69 and defensive weapon/shield guard motions 78/79 in this client.

The player reaction mapper now resolves all six motion keys from game data.
It continues to exclude outgoing shared hit VFX, player skill animations and idle
motions. Regression fixtures include the exact captured keys and assign them
exclusively to the optional reaction channel; existing native-output checks
verify independent routing and opt-out while Player SFX remains enabled.
Recordings now expose an explicit hit-reaction block counter.
**Confirmed on 2026-11-03:** the user tested hit-reaction-only mode and reported
“Feedback works and stops when off.” The 15-second v0.14.6 trace records a ready
observer with no setup error, both enemy skill channels and Player SFX disabled,
and 434 added hit-reaction/spatial blocks (1,188 → 1,622). It tags the PC battle
hurt sound as timeline 63 and the gunblade guard sound as timeline 78. This
confirms sound attribution and playback independently of skill feedback. The
checkbox-off step occurred after recording and is verified by the user's report.
Native speaker/mount/footstep behavior and reconnect/focus transitions were not
separately retested in this response.

## Delayed player hurt sounds (0.14.5)

The user confirmed 0.14.4 produces 强击 feedback while basic attack effects stay
quiet. Its trace contains 576 spatial enemy blocks and identifies the resolved
`mon_sp/m0176/mon_sp003` timeline as skill row 3203. Player hit reactions were
still silent: the recorded PC battle sound was owned by the local player but had
no action tag. The synchronous attack scope did not cover that later sound.

Local game data exposes dedicated base hurt motions `battle/damage` and
`battle/damage_continuous` (rows 68/69 in this installation). The player timeline
path now recognizes these separately from outgoing hit VFX, and carries that tag
into deferred character-sound playback. Row IDs are resolved from game data.
Traces now list both player and enemy timeline keys. Fixture checks cover these
hurt motions and rejection of outgoing/shared hit and idle timelines. The native
routing/checkbox path is unchanged. The missing normal/guard variants were
identified and physically verified in the 0.14.6 follow-up above.

## Actor timelines and optional hit reactions (0.14.4)

The user's 0.14.3 trace confirms enemy detection starts successfully: one skill
and four basic attacks were observed during recording, with a live listener and
no setup error. No enemy PCM was added. Strong-attack audio was present as
`SE_Vfx_Monster_Kuma_KyogekiRight_c.scd`, but its timeline was unclassified. Some
monster timelines are model-resolved versions of Excel keys containing
`[SKL_ID]`; globally shared keys cannot establish which action this actor used.

The catalog now retains a bounded, actor-scoped history of confirmed action IDs
and compares their end/hit/cast timeline templates against the resolved key.
There is no recent-action timer or sound/monster allowlist. If this actor uses a
timeline for both basic attacks and skills it stays ambiguous. Actor despawn,
identity replacement and territory changes invalidate that history with the
existing identity. Unknown timeline keys are recorded only during diagnostics.
Normal classification and history publication remain allocation-free in fixtures.

Player hit reactions are separately configurable and enabled by default to retain
requested hit feedback, including basic attacks. A hit requires enemy action
attribution, the local player's target ID, and a damage/block/parry effect in the
bounded native target-effect array. Only actual reaction PCM is routed; no pulse
is synthesized from the combat event. The option remains independent of Player
SFX and Boss/Regular Enemy skills, shares enemy strength, and follows the master,
foreground, PvE and native-route guards. Disabling it does not fall back to the
player skill route. Native game feedback remains untouched.

Tests now cover 32 category combinations, model-template expansion for a known
actor, isolation between actors, shared-template rejection, target-specific hit
classification, heal/miss/status rejection, independent hit routing and opt-out.
Follow-up verification: the user confirmed 强击 in skill-only mode on 0.14.4,
then optional hit feedback and checkbox opt-out on 0.14.6. See the versioned
evidence above; the initial setup recording alone did not prove playback.

## Enemy skill filtering (0.14.2–0.14.3)

The previous enemy channel checked the caster and audio bus but not the action
kind, so basic attacks were eligible. A regression reproduced that omission.
Enemy sounds now require game-data skill evidence: a matching synchronous action
callback or an unambiguous skill timeline. Auto-attacks (ActionCategory 1) and
unknown/shared layers without action evidence are skipped. Player sound capture,
native vibration and native speaker routing keep their existing behavior.

Local data confirms 图拉尔蜜獾 has BNpcName 13089 and 强击 is a weaponskill
category (3), while basic melee/ranged attacks use category 1. Several 强击 action
rows share `normal_hit/normal_hit` with auto-attacks; a filename, volume or recent
skill timer cannot reliably separate those sounds. The catalog uses action IDs
and exact timeline metadata instead of an enemy/job/skill allowlist. Native
callbacks keep scoped attribution across nested calls and restore it afterward.
Unknown asynchronous layers can still be omitted; live coverage is not universal.

The user's 0.14.2 recording contained no effective enemy channel, no listener and
no mixed enemy blocks. The screenshot reported action-layout validation failure.
Read-only inspection confirmed the live action entry point starts with an existing
hook jump (`FF 24 25 ...`), while the original executable has the expected
prologue and action-header read. Version 0.14.2 wrongly validated live patched
bytes. Version 0.14.3 validates Dalamud's original module copy using SearchBase,
then hooks the live address. Limit Break validation uses the same helper so either
initialization order is supported. The actual native action layout check remains.
The regression simulates that observed patched entry point, checks the original
copy passes, and confirms missing/truncated/incompatible code is still rejected.

Traces now include requested versus effective settings, enemy setup errors and
skill/basic/unknown action counters alongside each sound's action/timeline tag.
The 0.14.3 follow-up confirmed detection starts successfully. Skill playback
was subsequently confirmed on 0.14.4 and optional hit reactions on 0.14.6, as
recorded above.

## Haptics hierarchy (0.14.1)

Historical behavior; superseded by the v0.16 hierarchy above.

The Combat haptics checkbox is now a master switch over Player SFX, Enemy SFX
and Footstep amplification. All three sections dim and stop accepting input while
the master is off. Player spatial controls remain local to Player SFX. Enemy
feedback keeps its fixed spatial preset and Boss/Regular Enemy switches.
The existing command `combathaptics` now controls the master; `playersfx`
controls the former player-only switch. Saved subfeatures are retained while paused.

`HapticConfigurationChecks` compiles the actual Configuration class in the
standalone checks. It tests all 16 player/boss/regular/footstep combinations through
master off/on, JSON reload, old JSON without a master property, category commands
while the master is off, and scoped resets. The single snapshot method gates all
four effective outputs; it does not rewrite saved category or strength settings.
Native enable-cue checks also cover enemy-only and footstep-only selections and
cancellation when the master disables every category. Localization and build
checks pass. Current in-game layout, click behavior and master-off physical output
still need user verification on this version.

## Enemy haptics prototype (0.14.0)

Boss and regular-enemy skill feedback have independent, initially disabled switches
and a shared 25% strength default. Both always use character-relative source
position, camera-facing grip direction and a fixed 3–25-yalm fade, independently
of the player's editable spatial settings. Hostile battle NPCs are published on
the framework thread; audio callbacks reuse existing caster/timeline ownership
and never read Dalamud's object table. Player-only lighting remains isolated.

`EnemyHapticChecks` exercises independent toggles, actual PCM routing, native-send
preservation, missing-position/listener suppression, camera-facing grip balance,
character-distance cutoff, player-impact cue isolation, native slot/entity reuse,
roster/territory/logout invalidation and background/PvP-context cancellation.
Steady-state roster publication and caster/position lookup allocate no managed
memory in the warmed fixture. Configuration reflection checks cover persistence,
old-config defaults and scoped reset. These are synthetic checks, not proof that
every enemy's native sound callback is attributed in the live client.

Boss classification reuses a pinned LuminaSupplemental duty/BNpcName list, with
generated lookup verification in CI. Unlisted bosses use the regular-enemy
channel. Unknown caster/position and environmental emitters are skipped. See
[data provenance](../data/README.md). No runtime network request was added.

**Live verification pending:** test a known dungeon boss and regular monsters with
each toggle separately, then stay idle while a teammate hits the same boss. Check
left/right balance, 25-yalm cutoff, Alt-Tab, native footsteps/mount/speaker effects
and USB reconnect. `/ds5 trace` includes enemy kind and separate mixed-block
counts for investigating missing layers. No live enemy test is claimed yet.

## Existing hardware evidence

On 2026-11-03, the user tested v0.13.1 by enabling combat haptics while idle and confirmed: **two pulses; native effects still work**. This verifies the new cue's physical playback and preservation of the effects included in that check. Startup, slider-change and cancellation behavior have automated coverage; they were not separately confirmed by that response.

Earlier user-confirmed tests covered stable lighting with FFXIV open, controller reconnect, native haptics and speaker effects, RGB/preview agreement, lights-off release on Alt-Tab, own-character combat sounds, spatial grip balance and distance cutoff. Those checks are recorded in the conversation and summarized in [development notes](DEVELOPMENT-NOTES.md).

Native audio/camera research used the `2026.10.06.0000.0000` client with XIVLauncherCN. It is historical compatibility evidence, not a claim that every later build or region has been validated. The current read-only USB probe result is recorded in the table above; it does not prove physical effects.

## Public test preparation and handoff

Use the current-candidate section above for local build and release-audit status.
Versioned entries retain the evidence and outstanding checks recorded at that time.

1. **Completed user checks:** v0.17.9 lighting/preview, native haptics and speaker effects, Alt-Tab release and reconnect passed on the user's setup. The v0.17.8 setup pause/confirm/reopen and recording cancel/start/stop/save flow also passed, with no full-screen tint or repeated recording notice. These results stand alongside the current automated checks; the final label/height adjustment does not invalidate those controller results.
2. **Remaining appearance and coverage limits:** the final full language label and taller popup followed the latest hardware response. Seven actual-cimgui English/Chinese/Japanese geometry cases and two English renders pass; final in-game appearance at the user's scale/theme has not been separately confirmed. Other themes, controller/firmware models, XInput hardware and regional clients retain their stated evidence limits. Physical actuator/display timing, every unload interleaving, live macro/chat variants and browser/mail-app launching are not established by those geometry checks. Record such results when tested rather than treating untested variants as failed local preparation.
3. **Support destinations:** Ko-fi uses the supplied `https://ko-fi.com/K7C122MEWU` profile. Afdian intentionally remains disabled until a profile is supplied; obtaining one is not a public-test prerequisite. Verify any newly configured destination without submitting an issue or sending email merely to test it.
4. **Post-publication handoff:** after an authorized source push/tag and successful publishing workflow, fetch the public feed and ZIP, verify the checksum, and test fresh Dalamud testing-channel discovery/install/update before announcing availability. These checks require the release to exist and are handoff instructions, not unfinished local package preparation.

Windows desktop automation failed before window access and supplied no in-game
visual evidence. The later real-cimgui geometry/render evidence is documented
separately above. No GitHub release or installer-feed update was performed by
this QA work.

## Regular rumble output (2026-11-04)

Added automatic DualSense/XInput output selection and a manual backend/slot override.
The regular backend reuses the existing audio ownership, enemy classification, strength,
and distance filters. It converts approved PCM loudness into a bounded motor envelope
on the existing worker, without sending controller calls from the audio callback.
XInput motor requests are blended per motor with the game's latest request; disabling
feedback restores the native request. Footstep amplification remains DualSense-only.
No button state, remapping, new driver, or raw DirectInput force-feedback path was added.

Automated checks cover automatic DualSense preference, a single selected XInput slot,
manual overrides, strength limits, distance cutoff and missing positions, player/enemy/
hit-reaction routing, stale audio cancellation, silence, foreground/master cancellation,
confirmation pulses, disconnect/reconnect, native motor blending, unload cleanup, config
migration/reset, and translated controls. Existing native DualSense checks also run.

Hardware validation remains pending: test a USB DualSense, a wired Xbox-compatible pad,
and a wireless XInput pad in FFXIV; verify native rumble during added feedback, Alt-Tab,
master off, controller changes/reconnect, and plugin unload. Check automatic selection
with both controller types present and the manual override with a virtual XInput mapper.
