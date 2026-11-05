# Screen effect haptics

Checked on 2026-11-04. Turn on **Haptics → Enable haptics**, then
**Screen effects → Enable screen effect haptics**. New installations and Reset to
defaults enable the category at 25% strength after you opt into haptics during
setup. It follows the full haptics master
switch independently of
**Enable combat haptics**, Player SFX and Enemy SFX, on USB native haptics and
Bluetooth/XInput basic rumble.
Screen effects and Footstep amplification are siblings of the Combat group.
Turning combat off leaves them available; turning the haptics master off pauses
all added vibration and keeps every category's saved choices. Screen effects
pause in PvP, during zone transitions, without a character, and in the background.

The screen signal is centered, with no plugin distance fade, directional
panning, listener requirement or dependency on SFX strength. USB native haptics
adds the same screen sample to both grips. Bluetooth applies the saved motor
balance to the final combined commands, so centered input does not mean equal
left/right commands or physical feel. The shared **Advanced combat settings**
(spatial feedback and distance fade)
and player-only impact bias do not affect or interrupt screen feedback. Its own
strength, the haptics master switch, foreground state and output selection still
take effect immediately.

## Strength, texture and timing

Camera shake is captured immediately after the game's original camera update
composes its translation and rotation offsets. The current applied offset
magnitude controls the vibration envelope: the first nonzero frame responds
immediately, and a zero-offset frame stops immediately. Repeated nonzero offsets
are held briefly to preserve slower effect tracks, then settle to silence. A soft
response curve preserves differences between stronger shakes. No normal camera
position, zoom, pan or character distance enters this calculation.

Scripted scenes also have an independent authored shake sampler that modifies
the LowCut camera pose before scene-manager composition. It bypasses the AVFX
offsets above. The observer captures that sampler's scaled X/Y outputs, preserves
its native return value and parameters, and uses them only when that LowCut
camera is the active rendered camera. Unused Z channels are excluded. Samples
must be fresh and produced since the preceding composition. Camera changes reset
both trackers. The stronger current shake signal wins without adding duplicate
motion. This covers the shared scripted producer across scenes; it uses no boss,
territory, action, resource-name or encounter-specific rules.

Shake uses a single 60 Hz carrier whose intensity follows this measured motion.
The carrier makes the signal usable by the DualSense actuators; it adds no random
noise, repeating beat or independent pulse sequence. This removes both the
previous pre-tick sampling delay and the historical acceleration estimate's
one-frame delay. Dalamud's framework event runs before the original game tick
([framework source](https://github.com/goatcorp/Dalamud/blob/master/Dalamud/Game/Framework.cs)).
Audio buffering, display presentation and controller hardware still affect
physical synchronization; software checks do not establish zero physical delay.

Blur retains its smoother, distinct textures; chromatic aberration uses a
higher-frequency texture. The strongest observed effect selects the texture
when several effects overlap, without stacking gains. All textures stop when finished.

The previous extra 75% PCM amplitude reduction is removed. Small effect levels
use a square-root response. Real nonzero shakes also receive a minimum drive,
starting at 40% of the selected screen strength after that response, so a light
shake does not have to grow heavy before producing useful output. Numerical
silence, invalid data and zero strength still produce no output. The screen
strength slider remains the maximum output gain, and saved settings are preserved.
The plugin does not change the game's own visual attenuation.

Feedback starts from the next available sample/audio block, without the previous
50 ms startup wait. A short amplitude ramp avoids clicks. Five bounded carrier/
texture tables are prepared once (about 188 KiB total), with no allocation or
trigonometric calculation during audio rendering. Physical synchronization and
feel still need in-game validation. Bluetooth and XInput follow the same measured
intensity at the worker update rate, without the USB DualSense audio texture.

## Authored effect inventory

The complete AVFX effector enum currently has these ten entries. This is an
inventory of effect types, not a promise that every game effect is documented.
Names and IDs come from [VFXEditor's effector enum](https://github.com/0ceal0t/Dalamud-VFXEditor/blob/ed6b39dcca04225c15c404a35a46945119462015/VFXEditor/Formats/AvfxFormat/Base/Enums.cs).

| ID | Authored type | Screen haptics |
| --- | --- | --- |
| 0 | Point light | Excluded: world lighting |
| 1 | Directional light | Excluded: world lighting |
| 2 | Reserved | Unknown; excluded |
| 3 | Chromatic aberration | Applied strength × alpha × refraction/separation |
| 4 | Gaussian blur | Applied blur amount × weight |
| 5 | Directional blur | Applied blur amount × weight |
| 6 | Camera quake, variable | Current magnitude of applied translation/rotation offsets |
| 7 | Radial blur | Applied blur amount × weight |
| 8 | Black hole | Recognized in the inventory; runtime mapping unverified, excluded |
| 9 | Camera quake | Current magnitude of applied translation/rotation offsets |

Camera shake also includes the second pair of scripted offsets summed by the
native camera renderer. Ordinary camera movement, rotation and zoom are never
used as an effect signal. Multiple active effects use the strongest level,
rather than adding their strengths together.

The broader renderer separately lists ambient occlusion, anti-aliasing, sky,
moon, sky halo/rainbow, halo, fog, depth of field, camera motion blur, radial blur,
glare, god rays, lens flare, tone mapping, saturation, dark-blend color filtering,
vignetting, tone adjustment, chromatic aberration and letterboxing in
[PostEffectFlags](https://github.com/aers/FFXIVClientStructs/blob/b69073ae2d789527fc1b9a78aa7aa590e0f299dc/FFXIVClientStructs/FFXIV/Client/Graphics/PostEffect/PostEffectManager.cs).
These flags often describe persistent rendering settings. Only the active,
nonzero authored blur/chromatic parameters above drive feedback; bloom, lighting,
DoF, color grading, letterboxing and general motion blur do not.

## Independent mixing

Screen feedback follows the current camera sample alongside all other feedback.
There is no source ranking, input-contribution ledger, priority timer or delayed
replay. This replaces the earlier lowest-priority policy at the user's request.

The USB DualSense path reads the current vibration submix after native/plugin sources
have mixed, then renders the current screen effect. For each audio frame it limits
only the added screen sample to the remaining range in the two grip channels:
`max(0, 1 - max(abs(nativeLeft), abs(nativeRight)))`. Both grips receive the same
screen sample. The native PCM is unchanged. A single full-scale native sample
limits only that frame, rather than muting the whole screen block. Other bed
channels do not reduce grip output. Zero input count means the old native buffer
is ignored; invalid layouts or samples disable that block safely.

The waveform advances during limiting, so a busy native block does not restart
or delay the camera rhythm. A short attack prevents clicks; weaker camera frames
and zero offsets apply immediately. Settings, foreground, graph and freshness
guards still apply. Shake samples expire after two observed frame intervals,
bounded to roughly 33–100 ms and measured from the last offset change. Finished
or stale effects are never queued.

XInput keeps native/SFX blending unchanged when screen output is zero. Screen
motor levels are independent and added to that blend, with final values clamped
to the motor range. Native requests, SFX release envelopes and confirmation pulses
do not suppress screen output. Clearing the camera signal immediately removes
only the screen layer; background, controller selection and disconnect handling
release plugin output as before.

Bluetooth starts with equal screen motor levels, combines them with current
combat/confirmation output and clamps to the motor range, then applies
**Bluetooth motor balance** once to that final mix. Its default **+85%** reduces
the left command to 15% while leaving the right unchanged. Positive settings
attenuate left, negative settings attenuate right, and zero keeps equal incoming
commands equal. This calibration also affects screen-only output; it does not add
spatial panning or distance fade. Saved custom values remain authoritative, and
the basic combat-rumble strength/pulse controls do not set screen strength.

## Native evidence and verification

Singleton signatures follow FFXIVClientStructs revision
`b69073ae2d789527fc1b9a78aa7aa590e0f299dc`. The installed
`2026.10.06.0000.0000` executable independently confirms:

| Signal | Installed-client evidence |
| --- | --- |
| Post-effect singleton | Signature at RVA `0x66F65`; flags at `+0x4470` |
| AVFX blur application | RVA `0x38B310`; mode `+0x469C`, amount `+0x46A4`, weight `+0x46AC`; modes 0 radial, 1 Gaussian, 2 directional |
| Chromatic application | Same function; alpha `+0x47C8`, refraction/separation `+0x47CC/+0x47D0`, effect strength `+0x479C` |
| Camera singleton | Signature at RVA `0x18AA870` |
| Quake application | RVA `0x38BD20`; camera offsets at `+0x10/+0x30` |
| Camera offset composition | RVA `0x421D60`; sums `+0x10/+0x20` and `+0x30/+0x40` |
| Camera update hook | Same RVA; native wrapper at `0xD4470` passes the manager in RCX to a void function; capture runs after the original returns |
| Scripted shake sampler | RVA `0x18ACBE0`; byte return and two vector output pointers; samples authored curves and scales outputs by its current envelope at `+0xC` |
| Scripted shake consumer | Signature at RVA `0x18AFAA0`; call at `0x18AFB58`; consumed X/Y values reach the LowCut position/look-at pose; static shaker at `0x2A9F818`, control manager at `0x2A9F7F0` |
| Vibration input | MixIn RVA `0x1EB4100`, submix render `0x1EB4770`; input pointer `+0x80`, frame capacity `+0x88`, current input count `+0x122` |

All ten screen/input signatures match exactly once. Runtime setup checks original
module bytes and expected field instructions. Reads use bounded copies and
revalidate singleton identities; unsupported layouts disable this category.
The camera observer uses the validated update hook, enabled only when feedback
or tracing is active and permitted. The scripted sampler binding is optional;
if its guards fail, Diagnostics reports the error and the established paths remain
available. The audio path reuses the existing confirmation
submix hook and native MixIn; it allocates no buffers on the audio thread and
never writes camera parameters.

`ScreenEffectHapticChecks` covers classification, invalid/persistent effects,
freshness, immediate concurrent mixing, bounded PCM, balanced grips, zero audio
allocations, per-frame clipping protection, graph replacement, setting invalidation
and XInput coexistence with native/SFX/confirmation output. It also verifies stronger/distinct textures, their frequency
content, immediate onset, equal PCM across near/far and rotated listener poses,
and uninterrupted screen output across spatial-setting changes on native
DualSense and XInput. Bluetooth motor-balance checks additionally cover calibrated
screen/combat overlap, confirmation, test pulses and unchanged XInput output.
`CameraShakeChecks` verifies current-frame impulse peaks and zero stops, light
shake response, graded amplitude and changing rhythm, real pauses, held extrema,
static offsets, rotational shake, irregular frame intervals, 30/60/120 FPS
consistency, invalid/stale samples, reset behavior and the resulting output envelope.
Configuration checks cover the master switch, combat group and independent category combinations.
Build and automated verification do not establish physical controller feel or
all encounter coverage. In-game timing/strength and current hook coexistence
still need a smoke test.

## Recording timing in game

Reload the updated local plugin and enable screen effect haptics. Run `/bcef trace`
to start; on first use, review the recording notice and choose **Start recording**.
Reproduce both gentle and strong camera shaking for about 20 seconds,
then run `/bcef trace` again to stop. A companion log is saved to
`%LOCALAPPDATA%\DualSenseLighting\screen-haptics-timing.json`.

The bounded 4096-frame log records current shake offsets, their raw magnitude,
unchanged-frame count, camera sample times, target gain, backend and the first
successful native audio mix for each observed sample. `FirstMixMilliseconds`
measures software capture-to-mix delay. Suppressed/expired samples have no first
mix time. The first successful result is preserved even if later audio blocks
are suppressed. Storage contention drops trace records instead of waiting on
the audio thread; `Dropped` reports those drops. Saving runs off the audio thread.

The current `camera-haptics-timing-v4` format records `Policy: Independent`.
`ShakeLevel`, `ManagerShakeLevel` and `ScriptedShakeLevel` identify the actual
shake signals; `Effects` and `DominantEffect` distinguish blur/chromatic effects.
`CameraLevel` is the aggregate screen-effect level. `ScriptedObservedClock` and
`ScriptedSampleState` distinguish missing, different-camera, stale, consumed and
applied scripted samples. Binding status and errors are in the header.

`Output` records the current native grip/other-channel peaks, minimum available
headroom, number of limited frames, actual generated `OutputPeak`, target gain
and the mix result. There are no priority or source-classification fields.
The first successful output snapshot and timestamp are preserved for each camera
sample; they do not describe every later audio block for that same sample.

Timing publication reuses `TraceArchive`: the newest 20 complete camera sessions
are kept in `%LOCALAPPDATA%\DualSenseLighting\sfx-traces` with timestamped
`sfx-trace-*.json` names, as well as the latest convenience file above. These
camera archives are separate from ordinary SFX archives in the plugin directory.
Their JSON format identifies them as camera timing captures.

Camera sampling is independent of output selection. First-mix timing instruments
USB native DualSense mixing; it does not measure Bluetooth/XInput motor submission,
display presentation or physical actuator latency. Those still require an in-game
check.

## Recorded failure and regression coverage

Early cutscene traces contained only zero scene-manager shake offsets, exposing
an independent scripted camera producer that bypassed that path. The shared
scripted sampler observer was added without encounter-specific rules. Capture
was moved after the original camera composition, and the historical acceleration
estimator was replaced with the current offset magnitude.

Later traces exposed a different issue: priority gates suppressed camera feedback
in the presence of native vibration. Footstep/silent-send exceptions reduced
some cases but kept the camera dependent on unrelated source classification.
In the recorded fight, 273 active samples were suppressed by native input or its
release hold. One shake waited 671.7 ms; the final strong burst never mixed.
Native grip peaks stayed below 0.032, so clipping did not require these gaps.

The current path removes that ranking and accounting entirely. Regression checks
exercise simultaneous native/SFX/camera output, immediate recovery after busy
blocks, silent and stale buffers, per-frame headroom, a single full-scale native
sample, invalid input, balanced grips, settings/graph changes and audio-thread
allocation limits. XInput checks cover simultaneous native/SFX/screen levels,
confirmation overlap, immediate screen stop and lifecycle cleanup. Recorded
camera levels/native peak bounds can also be replayed through the shared mixer;
such a replay verifies software behavior, not physical controller timing.

Historical verification after the independent-mixing refactor:
`tools/build-wsl.py` passed 443,122 checks. The
fight replay contained 622 active samples, including one expired sample. All
621 fresh samples mixed on their first available block, compared with 347 mixes
in the original recording; the expired sample stayed silent. Native PCM was
reconstructed conservatively from the recorded per-block peak bounds, rather
than captured waveform data. The maximum combined grip peak was 0.914759. An
isolated reproduction also changed from zero screen mixes under the old priority
gate to an immediate mix with the new path. In-game actuator/display timing still
requires live verification.
