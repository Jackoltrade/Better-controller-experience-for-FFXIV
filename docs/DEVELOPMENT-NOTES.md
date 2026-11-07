# Better controller experience for FFXIV

DualSense controller customization for Final Fantasy XIV using Dalamud.

[GitHub repository](https://github.com/Jackoltrade/Better-controller-experience-for-FFXIV)

## First-run and recording choices (0.17.8, 2026-11-05)

HasCompletedSetup is independent of the older HasSeenGeneralInfo flag. New plugin
configurations start with lighting and haptics off; existing preferences are
prefilled for one explicit choice. Runtime snapshot producers require setup,
the controller worker and optional observers wait for it, and the output guard's
hooks remain disabled before consent. Feature commands and resets cannot grant
consent. Saving the choices precedes output activation; failed saves remain
pending. Both-off choices remain available without enabling output.

HasAcknowledgedRecording is a separate persistent flag. UI and command requests
share one gate, and the framework recorder checks it again before creating any
trace. Notice cancellation leaves recording off. Accepted starts persist the
acknowledgment before queueing the manual recording. Per-tab reset preserves both
flags. The existing focused popups avoid a full-screen tint and limit height to
85% of the display for scrolling. English, Chinese and Japanese are included.

TraceArchive owns the camera-timing latest path and archive-directory calculation;
the recorder, notice and Diagnostics cleanup instructions use the same paths.
The disclosure describes local metadata, small native-memory snapshots, two
storage locations, twenty archives per location, large files and public sharing.
No network client, extra driver or service was added. Automated checks cover
pre-consent zero device access/reports, inactive native haptics, all setup choices,
legacy state, persistence/reset and independent recording consent. The complete
optimized build passes 523,748 checks. On 2026-11-05 the user confirmed setup
pause/confirm/reopen behavior without a full-screen tint, plus recording notice
cancel/start/stop/save and no repeated notice on later manual recordings.

## Screen effect strength default (0.17.7, 2026-11-04)

Screen effects stay enabled in the user preset, with default strength reduced
from 35% to 25%. The existing Haptics reset reads the same Configuration defaults.
Explicit saved strengths remain authoritative. The screen-default fixture and
English, Chinese and Japanese guides use 25%; native routing and Bluetooth
balance/pulse settings are unaffected.

## Bluetooth balance default and output-specific UI (0.17.6, 2026-11-04)

The user calibrated an approximately balanced feel at +85% and requested it as
the default. HapticSettings owns the shared constant used by its constructor,
configuration and normalization. Missing saved values and reset use +0.85;
explicit custom balances remain unchanged. The localized tooltip explains that
left commands are reduced to 15%, with no increase to the right channel.

The output UI uses HapticOutputSelection.Resolve directly and omits basic-rumble
settings/tests for native DualSense output, including Automatic with USB present.
Bluetooth and XInput retain the controls; balance appears only for Bluetooth.
This avoids a second output-selection policy or reliance on worker-state lag.
The full build passes 520,902 checks; visual UI verification is pending.

## Bluetooth motor balance (0.17.5, 2026-11-04)

The user confirmed the stale-direction fix, while the isolated right channel
still feels weaker. SDL and Sony's Linux driver map compatible rumble to low/
high-frequency and strong/weak channels. Equal commands do not establish equal
perceived intensity. Basic rumble does not provide equivalent actuator waveforms.

A persisted Bluetooth-only balance value defaults to zero. Positive values
attenuate left and negative values attenuate right, never amplifying a channel.
RumbleSession applies it after the limited combat/screen mix, including tests and
enable confirmation, so clipping cannot erase the calibration. Spatial zeros,
distance cutoff and stops remain zero. XInput and USB keep their existing path.
The existing settings/configuration and diagnostics are reused; no driver,
thread, transport or extra haptic stream is added. +50% is a user trial starting
point, not a measured default. Physical calibration remains pending.

The optimized build passes 520,776 checks, including actual HID command bytes,
saturated overlap, isolated tests, confirmation, persistence/reset, normalization
and queued-frame invalidation. Test pulse readings show the calibrated commands.

## Bluetooth pulse direction timing (0.17.4, 2026-11-04)

User testing confirmed direction and distance attenuation, but reported a weak
right grip and an SFX that stayed left. Plugin-off stops that feedback. Current
motor diagnostics at 0/255 were inconclusive because stops replace live values.

A deterministic interleaving test failed before the fix: the independently
consumed scalar onset could arrive before its side values and reuse the previous
stronger impact's steering. Bluetooth now takes its onset from the consumed side
pair; the scalar remains drained for lifecycle consistency and XInput retains its
existing path. Each impact resets its steering peak. No new signal class, queue,
thread, transport or native-send modification is needed.

The existing bounded strength-test mechanism also provides equal-strength
left-only then right-only pulses. Both tests suppress other added feedback and
cancel on focus loss. Diagnostics retains the actual serialized bytes for each
pulse, so the stop command no longer hides the evidence. This isolates remaining
physical differences without assuming emulated heavy/light channels feel equal.
The optimized build passes 520,281 checks; the physical retest is pending.

## Bounded basic impacts and Bluetooth grip balance (0.17.2, 2026-11-04)

User testing confirmed v0.17 Bluetooth RGB/bottom LEDs match the preview. Basic
rumble worked, but felt rough without screen effects; the user requested short
impact pulses with strength/duration controls. RumblePulse now emits one bounded
impact on an onset, merges its earliest layers, fades its tail and avoids repeated
buzzing from steady input. Defaults are 50% extra strength and 120 ms, with a
60–300 ms duration range. These controls apply to combat Bluetooth/XInput rumble,
while screen effects and enable confirmation retain their independent behavior.

Firmware-supported Bluetooth uses compatible vibration v2. The per-connection
0x20 feature query reads metadata only and validates the 0xA3-seeded CRC and
feature-version threshold (2.21); Edge supports v2 from launch. Unknown regular
firmware retains legacy mode. This is not Bluetooth audio-haptic streaming.

The initial v0.17.1 impact tuning reduced the high/right command to 25% on both
backends. The user then confirmed variable strength but reported one weak grip.
Version 0.17.2 retains frequency weighting only on XInput. Bluetooth now preserves
left/right gains from the existing spatializer using two existing generation-safe
SfxLoudness handoffs. Centered/spatial-off feedback is balanced. Camera-facing pan,
impact bias and distance attenuation are retained in the motor levels. All signal
generations reset on backend, connection and settings changes. No original PCM
or native sends are altered.

The strength test uses two longer 600 ms pulses at 20% and 100% of the configured
basic-rumble limit, separated by 350 ms. It suppresses other added vibration while
running and is canceled by focus loss/disable. Diagnostics retains the actual
successfully written Bluetooth motor-byte range, along with mode and current
left/right commands, to distinguish command delivery from physical feel.

Automated checks cover bounded pulses/retrigger suppression, repeated GCD impacts,
settings persistence/normalization, balanced and directional Bluetooth output,
camera reversal, spatial off, distance fade/cutoff, generation changes, firmware
CRC/thresholds, improved-mode start/stop and strength-test command ranges. Current
physical grip/spatial behavior still needs user verification.

## Bluetooth lighting and basic rumble (0.17.0, 2026-11-04)

The output session now handles USB and Bluetooth DualSense/Edge interfaces.
Descriptor detection uses the 64-byte USB or 78-byte Bluetooth input report and
supports Bluetooth descriptors whose largest output report exceeds 78 bytes.
The plugin emits only the standard 78-byte 0x31 control report. It uses a per-device
four-bit sequence, 0x10 data tag, shared 47-byte payload and seeded IEEE CRC-32.
The guard validates the incoming checksum, preserves either standard or legacy
DS4Windows headers, changes only lighting fields, and recalculates the checksum.
Other Bluetooth report IDs/sizes (including audio streams) remain outside it.

LightingSession is also an IRumbleDevices target. The existing RumbleSession
supplies the same SFX envelope, distance gain, independent screen layer and two
confirmation pulses used by XInput. A single HID owner composes RGB, bottom LEDs
and optional compatible rumble in each Bluetooth packet. There is no second
Bluetooth timer, writer, input reader, remapper or service. Rumble targets one
stable Bluetooth device; lighting continues to cover all opened controllers.

Automatic selection prefers USB DualSense, then direct Bluetooth rumble, then
XInput. Explicit selections stay explicit; the new enum member is appended so
saved Automatic/DualSense/XInput values retain their numbers. Bluetooth rumble
works with lighting disabled and excludes native footstep amplification. The
worker resets the signal/envelope on backend or Bluetooth-generation changes.
Disabling lighting releases its LEDs while preserving active rumble; disabling
rumble sends a motor stop while retaining lighting. Focus loss/unload clears only
owned light/motor fields once, then closes all handles. Static active state is
refreshed at one second, within the controller's rumble keepalive interval.

The Haptics tab always displays the cable requirement for detailed DualSense
haptics. Output selection, guide, diagnostics and all three user guides distinguish
Bluetooth basic rumble from USB detailed haptics/native footstep and speaker
features. Bluetooth audio/haptic streaming is intentionally outside this change.
Protocol/source tests pass; physical Bluetooth pairing, firmware compatibility,
latency/feel and native USB coexistence on this build still need a live check.

## Project goals

- Configure RGB lighting and player indicators, including after reconnecting.
- Preserve native DualSense haptics and controller-speaker effects.
- Explore RGB animations and skill-sound-driven haptics across jobs.

## Development approach

Keep operations small and simple. Reuse existing libraries and game capabilities
where practical.

## Controller plugin

The first implementation targets **Windows, Dalamud API 15, and USB DualSense or
DualSense Edge controllers**. Bluetooth transport is not implemented.

Open `/bcef` or the plugin's configuration button to change:

- Eleven RGB effects: Solid, Rainbow, Breathing, Color Shift, Aurora, Fire,
  Heartbeat, Comet, Rainbow Pulse, SFX Loudness and SFX Color Blend.
- RGB color, brightness, and animation period.
- Bottom LEDs: the five white lights below the touchpad, with count, animation, and independent timing.
- A separate **Haptics** tab for optional, sound-driven combat feedback from your character.

Lighting and Haptics contain the configuration controls, with explanations in
tooltips: hover a control or its **(?)** marker for help. The **Diagnostics** tab
groups controller and focus status, output errors and counters, the SFX level,
left/right haptic meters, Limit Break detection and manual SFX recording.
Running `/bcef trace` starts or stops recording and selects Diagnostics so its elapsed timer is visible.
All tabs share a fixed width that scales with the UI font; height adapts to the
selected tab. Dividers separate the major settings groups; smaller groups use
larger, font-scaled gaps without extra lines. The language selector sits above
the tabs without a divider. Long diagnostic text wraps within the window.
Sound-reactive lighting has a live level meter directly below **SFX sensitivity**
to help tune its response. The controller preview fades into the window background
at all four edges, including its animated lighting layers.

Use **Language / 语言 / 言語** above the tabs to choose **English**, **简体中文**
or **日本語**. Changes apply immediately and the choice is saved. Controls,
effect names, preview labels, help and diagnostic messages are translated;
technical errors from the game or operating system retain their original text.
Tooltips use short, plain-language explanations in each language. Switching
languages keeps your selected tab, open sections and controller settings.

**Reset to defaults** appears below the Lighting and Haptics tabs. A confirmation
dialog names the tab being reset; **Cancel**, Escape or closing the dialog leaves
settings unchanged. Confirming restores only that tab and saves immediately.
The confirmation uses a small popup without a screen-wide dimming overlay;
clicking outside it also cancels.
The other tab and the selected language are preserved. Existing saved settings
are kept when updating the plugin.

Dependent settings stay visible but are grayed out when their parent feature is
off. Disabling combat haptics disables its settings; Spatial haptics, Follow skill
impacts and Distance fade each control their own dependent settings. Lighting
settings follow Enable custom lighting, and bottom LED animations require a
nonzero LED count. Help remains available for disabled controls. Turning features
off keeps their saved values. Brightness is shown as a percentage.

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

### Character combat haptics prototype

Open `/bcef` → **Haptics** to configure **Character combat haptics**. New installs
start with combat haptics enabled at **80%**, Spatial haptics and Follow skill
impacts enabled, **100% impact bias**, and recommended character-based distance
fade: full strength through **3 yalms**, fading to zero at **25**. These are also
the Haptics reset defaults. Settings are saved independently of lighting. This feature works
with Solid, RGB animations, either SFX effect, or custom lighting disabled. It
requires a USB DualSense and native controller vibration enabled in FFXIV.

The prototype reuses FFXIV's dedicated **Vibration** submix and its native
processing effect. It adds the processed PCM of your own character's combat and
weapon sounds to that path; it does not synthesize ordinary rumble from an RMS
meter or open a second controller audio stream. The existing actor-ownership
filter selects these sounds, regardless of the lighting source setting.

Native footsteps keep their existing strength unless the separate amplifier below
is enabled. Mount vibration keeps its native routing and strength.
Sounds carrying native vibration flags, vibration-only clips, and sources already
sent to the vibration mixer are skipped to avoid adding them twice. Music,
dialogue, ambience, footsteps and sounds belonging to other actors are excluded
from the additional feed. Original PCM, sends, audible playback, controller-speaker
audio and trigger commands are preserved. The strength slider changes only the
new feed; zero leaves all native vibration intact.

Added feedback pauses on the next worker tick when FFXIV loses focus, independently
of the lighting toggle. Existing native effects retain the game's behavior. Lighting
still uses its one-time lights-off release. Each eligible audio block resolves the
current vibration mixer, so no device-specific audio destination is cached across
replugging. Unsupported bindings or player attribution pause the added feedback
without falling back to other players' audio.

**Diagnostics → Combat haptics** shows added combat blocks and preserved native vibration
blocks. Automated checks use a fake native mixer to verify routing, gain, original
PCM/send preservation, stale-block rejection, focus loss, disable and graph
replacement. On 2026-11-02, the user confirmed v0.8.0 produces combat feedback
while native walking/mount vibration, controller-speaker effects and Alt-Tab's
lights-off release continue to work on their setup.

#### Native footstep vibration amplifier

Open `/bcef` → **Haptics** → **Native footsteps** and enable **Amplify native
footstep vibration**. This is independent of combat haptics and lighting. The
slider sets total strength from **100% to 600% (6×)**, defaulting to **150%**; 100%
leaves the native vibration unchanged. Settings persist after reconnecting.
An inline note explains that native footstep strength can vary by race: Lalafell
may feel lighter than Roegadyn, so the same multiplier can feel different between
characters. The amplifier scales the native signal rather than equalizing races.

The amplifier selects only the Foot bus (6) and requires an existing, verified
send into the game's native Vibration submix. It adds `(multiplier - 1)` times
that send's current gain using the same processed PCM and native format. For
example, 150% adds half the original send again. Audible footsteps without a
native vibration send stay unchanged; nearby players do not acquire synthesized
feedback. No sound filenames or movement timers are used.

Amplification pauses while mounted, logged out, or while FFXIV is in the
background. Native footstep/mount feedback itself continues normally. Combat
spatial/distance settings do not alter this native-waveform boost. Original PCM,
source sends, speaker volume, triggers and audio endpoints are not changed.
The native mixer is revalidated for every block and the saved settings resume
after dismounting, focus return and audio-device reconnection.

**Diagnostics → Combat haptics** includes the footstep amplifier status and
**Footstep blocks amplified**. `/bcef trace` also records that counter,
whether the character is unmounted, and the `NativeFootstep` route. Automated
checks verify exact added gain, speaker/PCM preservation, independent enablement,
native-only routing, mount/background pauses, stale blocks and graph replacement.

#### Spatial direction and distance

Enable **Spatial haptics** in the Haptics tab. The original mode remains available
with this checkbox off. The direction origin is your character's world position,
while left/right follows the active camera's
orientation. The sound's offset from your character determines the grip balance.
Character facing does not set the axes. Directional separation is fixed at full
strength (1.0); it has no slider or saved setting. Legacy separation values are ignored.

Direction uses the horizontal sound bearing, so an impact above the ground does
not weaken its left/right cue. At melee range the balance curve emphasizes small
side angles; this emphasis blends back to the regular curve between 3 and 6
horizontal yalms. The favored grip keeps its existing gain while the other grip
is reduced more strongly. Emitters within 0.05 horizontal yalms stay balanced,
with a smooth transition to normal directional strength at 0.25 yalms. This avoids
turning tiny centered-position jitter into a false left/right cue.

**Follow skill impacts** gently connects body/cast/weapon
layers to a recent off-center sound from your own effects. **Impact bias** is
adjustable from **25% to 100%**, defaulting to 100% for new installs and resets. At the character's origin it
blends that percentage of the cue's direction, tapering out within 0.75 horizontal
yalms of your character. At 100%, a centered layer fully follows the cue's direction.
It changes direction only: distance still comes from each sound's actual source,
and impact layers keep their own positioning. The strongest audible, player-owned
off-center sound in the last worker interval supplies the cue, expiring after
100 ms. This is approximate guidance when skills overlap, rather than an exact
action-ID association. An early cast before an impact becomes audible has no cue.
Disable the checkbox to keep centered layers balanced. Focus, actor and setting
changes invalidate guidance and pending haptic blocks.

**Distance fade** offers separate **Character (recommended)** and **Camera**
origins. Character mode measures sound-source-to-character distance, with full
configured gain within 3 yalms, fading to zero at 25 yalms with the revised
recommendation in v0.9.4. The fade is calculated for each sound using the existing
smoothstep curve. The 25-yalm boundary is silent; effects inside it retain a
continuous fade. Earlier recommended presets used a 30-yalm cutoff.

Camera mode retains sound-source-to-camera distance. Its recommended range adds
the current camera-to-character distance to both limits. For example, a camera
15 yalms from the character uses full strength through 18 camera yalms and fades
to zero at 40 camera yalms. This is recalculated as the camera moves or zooms.
The triangle-inequality bound keeps sources within 3 character yalms in the
full-strength region and retains effects inside the 25-yalm combat envelope, instead
of applying an unadjusted character range to camera distance. The current computed
limits appear in the UI. Selecting the distance origin does not change the
character-centered, camera-aligned direction calculation.

Manual limits are under **Advanced distance settings**. Turn off **Use recommended
distance fade** to adjust them. Existing saved limits are preserved there, while
the recommendation is enabled by default on upgrade. Returning to the recommendation
restores the selected origin's preset. Manual limits are retained separately for
character and camera distance, including camera limits saved in earlier versions.
Manual camera limits are absolute distances and do not receive the automatic
offset. Camera translation and zoom leave the character-distance fade unchanged;
camera rotation changes the left/right axes. Both modes multiply the game's
existing audio-volume attenuation, which is preserved.

The plugin uses the owned sound's positional coordinates, rather than assuming
that every skill sounds at its caster or target. The character position comes from
Dalamud's local-player object; it and the camera orientation are read on the
framework thread. The mixer receives an immutable listener pose, rejecting data
older than half a second. The game's native conversion matrices decode both its 8-channel
bed and 16-channel ambisonic PCM. A separate haptic buffer combines the decoded
stereo waveform and applies the character-centered, camera-aligned balance, then enters the existing
native vibration filter. Original audio samples and routing are unchanged.

Left/right meters show the gains of the last positioned sound, including combat
strength and distance fade; they are not measurements from the physical actuators.
The latest sound's distance from the selected origin and balance appear beneath them. These meters
clear after a short idle interval or settings change. Diagnostics count spatial
blocks, biased player-origin blocks, cutoff blocks and missing-position suppression.
When spatial distance fade is enabled, unpositioned sounds, stale/missing listener
poses or unsupported spatial bindings suppress the plugin's additional feed.
They cannot fall back to an unfiltered haptic route around the distance cutoff.
Original combat-haptics routing remains available with spatial mode or its distance
fade disabled. Existing native vibration sends remain untouched in every mode.

The 25-yalm cutoff is per sound source. A cast/weapon sound located at your
character can still vibrate while its enemy impact is out of range. Native game
vibration is separate from the plugin's added feed. The diagnostic trace records
both routes, positions and cutoff decisions to distinguish those cases.

The two grips convey left/right balance. Distance remains three-dimensional;
height does not dilute horizontal direction. Front/back and height have no
separate actuators. This option
does not add feedback for other actors or alter native footsteps/mount effects.
The existing foreground pause, lights-off release and reconnect behavior remain.
Automated checks cover rotation, zoom, range boundaries, both PCM layouts, immutable
source PCM, ownership/slot reuse and callback invalidation. On 2026-11-02, the user
confirmed v0.9.0 passes live camera-relative direction, distance, native-effect and
Alt-Tab checks. The revised v0.9.1 recommended range retains that routing and curve;
checks cover its 3-yalm plateau, nonzero response at 25 yalms, 30-yalm cutoff,
upgrade defaults, manual overrides and restoring the recommendation.
Version 0.9.3 adds character-centered direction with camera-aligned axes, a choice
of character or camera distance, and the zoom-aware camera recommendation.
Regression checks verify that character distance ignores camera translation,
character movement changes distance, camera rotation changes grip balance, camera
distance retains the combat envelope across zoom levels, and manual camera limits
remain absolute. Live confirmation of the new origin options is still pending;
the earlier live confirmation applies to the original camera-centered version.
Version 0.9.4 shortens the recommendation to 25 yalms and strengthens melee
separation. Regressions cover the 25-yalm boundary, zoom-compensated camera mode,
height-independent direction, stronger close-range contrast, mirrored sides,
centered jitter and continuous transitions. The new physical feel needs a reload
and in-game comparison.
Version 0.9.5 further emphasizes small melee side angles, adds gentle impact
guidance, closes the unknown-position fallback around distance fade and records
actual haptic routing in diagnostics. The fallback defect is covered by a native
mixer regression; the reported distant enemy effect still needs its source/routing
trace to establish which layer is being felt.

The subsequent v0.9.5 trace exposed a separate initialization defect: the native
stereo builder uses input-channel rows padded to 8/16 output lanes and intentional
0.89/0.11 front-channel crossfeed. Treating that as compact output rows and requiring
zero crossfeed rejected the matrix. No spatial blocks were mixed in that recording;
strict distance mode correctly paused the additional feed. This supersedes the
earlier subjective spatial checks, which did not establish an active spatial route.

Version 0.9.6 extracts/transposes the padded rows correctly, retains the native
crossfeed and checks the native builder's return status. An isolated verifier runs
the installed EXE's real matrix arithmetic through the production wrapper and
checks every 8- and 16-channel impulse; it does not open or change the running
game. Regression fixtures cover the actual coefficients, dimensions, handedness
and the recorded Machinist cast/impact positions. The recording places impact
layers 26.48 yalms from the character and cast/weapon layers at the character.
The former must be cut off while the latter can still produce local feedback.
On 2026-11-02, the user confirmed haptics returned on v0.9.6. A new 15-second trace
then verified active spatial routing with no initialization errors: all six
Machinist Leadensalute enemy-impact playbacks at 26.46 yalms used DistanceCutoff,
as did a weapon-layer observation at 25.46 yalms. Local cast/common-effect layers
at zero source distance retained spatial feedback. Spatial output increased by
4,429 blocks and cutoff suppression by 3,206 blocks; missing-position suppression
was zero. Impact guidance had also been applied earlier in the same plugin run,
but remained inactive during the far-impact recording, as expected. This verifies
per-source cutoff and routing, while the perceived close-range contrast still
depends on an in-game comparison.

### Combat SFX loudness prototype

Choose one of two separate options in the `/bcef` **Lightbar effect** list:

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

Both SFX modes offer a **SFX source** selector:

- **My character only** is the default. It accepts combat sounds attributed to the
  local player by a VFX listener, character/weapon sound callback, or action timeline.
  Known local weapon models also resolve to their owning character.
  Other actors and sounds without a confirmed owner are skipped.
- **All combat sounds** keeps the earlier behavior, including other players and
  enemies.

Volume categories do not identify the caster: a controlled teammate-only capture
contained shared hit sounds labeled as local-player sounds. The filter instead
tags the native sound-pool reservation during actor-aware playback, then
matches it to the source voice. Positional sounds use a negative layout handle
and a separate positive mixer ID; the observer follows the game's existing
lookup to connect them. Every new reservation clears
the previous owner, and logout/actor changes invalidate existing tags. This also
handles sounds whose resources load asynchronously, including modded assets.
Sounds without ownership from these paths are omitted. Merely attaching a VFX to
the player does not grant ownership: only known character/weapon models qualify,
and an explicitly different caster is always rejected. Model changes do not reset
sounds already attributed to the same actor. Coverage beyond the verified skills
still needs live checks across jobs and encounters.
Changing the source discards pending audio and resets the lighting envelope so
old global impacts are not carried into the player-only response. Source selection
is saved with the other settings and applies to both SFX modes after reconnecting.

The observer reads decoded combat voices on the game's SE and CommonVFX buses.
It excludes music, voice dialogue, ambience, footsteps, system sounds and
vibration-only sounds. It does not use the broader built-in FFT group, which also
includes voices and ambience. The filter changes only which sounds drive lighting;
other players' audio and all native controller effects remain untouched.

For each lighting tick, the observer keeps the loudest combat voice's RMS from
the preceding interval. This retains short impacts without diluting them with
quiet voices. It is a per-voice energy proxy, not a reconstruction of the final
mixed waveform. Samples are measured after source volume, panning and effects,
before the master/device volume. Increasing the SFX volume can change the response.

The optional observer is installed when an SFX mode or combat haptics is first enabled. Its native
render hook always calls the original playback exactly once with the original
arguments and return value. Lighting analysis only reads the sample buffer and
does not change playback or routing. Optional combat haptics adds a separate send
to the native vibration submix as described above. Neither writes the original
samples, sends or controller-speaker audio. The hot path has no HID
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
available. Player-only support additionally validates the VFX callback, native
pool-acquisition layout and playback-ID getter; if unavailable, All combat remains
available. It never silently falls back to global sound when My character only is
selected. New game versions may require updated bindings.

Automated checks cover RMS, noise rejection, decay, stale-callback rejection,
preview/output agreement, bus filtering and unchanged native controller bytes.
On 2026-11-02, the user confirmed v0.5.0 passed live SFX/preview synchronization,
native haptic and controller-speaker effects, and lights-off release/return on
Alt-Tab, and subsequently confirmed v0.5.2 works well. Version 0.6.1 separates the
two SFX modes and keeps the low color visible. Automated checks cover the quiet
state below the gate, mode switching, endpoints, continuity, preview agreement,
native report preservation, focus release and reconnects. Its final in-game
appearance still requires a reload.

Version 0.7.1 replaces the initial category-based source selection with caster-aware
sound ownership. Checks cover shared category-zero hits, local effects on other
volume categories, delayed resource loading, pool reuse, logout, stale callbacks,
vibration-only rejection and allocation-free classification. A replacement
15-second Gunbreaker 恶魔杀 capture recorded seven skill sound instances. The new
filter initially missed the skill because its positional layout handle differed
from the ID on the mixer voice. Version 0.7.2 observes the native handle lookup to
connect those IDs, retaining checks against slot reuse and actor changes. A new
regression reproduced that mismatch before the fix. The corrected response still
requires an in-game reload and teammate comparison.

Versions 0.7.4–0.7.5 extend attribution to character/weapon sounds and action-timeline
sound clips. The timeline observer follows the game's existing lazy owner lookup
before playback, including the first use of a clip; it does not substitute the
target's identity or use a window around the player's last action.

On 2026-11-02, a controlled v0.7.5 recording of Gunbreaker 迅连斩 (Solid Barrel)
confirmed all six cast-side layers, all six target-side layers, and all eleven
gunblade-bank instances were attributed to the player and eligible for RGB input.
The corresponding v0.7.3 recording rejected all of those layers; v0.7.4 recovered
the gunblade bank. A subsequent 15-second teammate-only recording observed 55
audible combat voices above the default gate and accepted none as the player.
The implementation has no job-specific or asset-name allowlist.

For a diagnostic recording, select an SFX mode or enable combat haptics and run
`/bcef trace`, or select **Diagnostics → Start SFX recording**. Run the command
again or select **Stop and save recording** to finish. A visible timer shows
elapsed time. One-minute segments share a session timestamp and increasing
segment number. Completed segments save off the game thread to `sfx-traces/`;
the latest 20 are retained and `sfx-trace.json` remains the latest segment.
At most one writing segment plus one active segment is held in memory. A slow
writer stops recording instead of growing a queue; saving the final partial
segment is attempted even if an earlier write failed. Native sampling and the
final manual-stop sample run on the framework thread. Unload quiesces native
hooks/output before joining pending saves. The trace records metadata rather
than audio, and uploads nothing. Name the attack and its approximate elapsed
time when a recording contains multiple mechanics.

### Bottom LED animations

The **Bottom LEDs** controls set the number of white lights, an animation,
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

### Limit Break indicators

In `/bcef` → Lighting → Bottom LEDs, select **LED mode: Limit Break**.
The existing custom indicator counts/animations remain saved and return when
switching back to **Custom animation**. RGB and SFX lighting remain independent.

- **PvE:** one completed party LB bar makes the left position blink; two add
  the center; three add the right. Duties with fewer available bars use their
  actual capacity. Partially filled bars do not count as ready.
- **PvP:** below 25% the indicators are off. At 25%, the first indicator is earned;
  at 50%, the first two; at 75%, the first three. One earned indicator blinks.
  With two or three, a single dark gap moves left to right: the previous position
  switches on in the same report that the next switches off, without an all-on
  pause between steps. The center position is unused. At 100%, all four blink together.
- **Your LB cast:** the available PvE positions or all four PvP positions flash
  together four times per second until casting ends or is cancelled. Confirmed
  instant LB uses flash for 0.8 seconds. Teammate casts/uses do not trigger this
  override. The charge display resumes afterward.

The cycle slider sets a complete blink/loading cycle. Version 0.10.3 reuses the
existing white-indicator Blink effect for ready bars and full PvP charge. All LB
updates are immediate, preserving the loading flicker fix; the preview displays
the same on/off mask without simulated fading. The preview, worker and native-output
guard use one LB snapshot and renderer. Stale/unavailable gauge data turns the white
indicators off. Focus loss still turns all custom LEDs off once and releases the
controller; reconnecting uses the current charge rather than a saved charge.

The optional observer reads `LimitBreakController` and Dalamud's local cast
properties on the framework thread. LB action IDs come from the installed game's
Action sheet categories 9 (PvE) and 15 (PvP). A checked native action-effect
observer identifies successful uses by the local actor, deduplicates multi-target
notifications and retains the original callback and arguments. Actor, job,
territory, PvP-mode and focus changes discard pending flashes. If instant-use
observation is unsupported, the settings report that limitation while gauge and
cast display remain available. No audio or haptic routes are changed.

### Live controller preview

The top of `/bcef` shows a centered touchpad close-up with a white touchpad,
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
   `<your checkout>\artifacts\plugin\DualSenseLighting.dll`.
3. Save, then enable **Better controller experience for FFXIV** in `/xlplugins` → Dev Tools.
4. Run `/bcef`.

### Build and checks

Build requirements: .NET 10 SDK and API 15 Dalamud libraries.
The project file is `src/DualSenseLighting/BetterControllerExperienceForFFXIV.csproj`.
It keeps the `DualSenseLighting.dll` assembly and internal plugin ID so existing
saved settings and dev-plugin paths continue to work after the rename.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\build.ps1 -Probe
```

The script discovers local XIVLauncherCN/XIVLauncher API 15 libraries. Override
with `-DalamudLibPath` or `DALAMUD_HOME`. Builds use `Dalamud.NET.Sdk/15.0.0`
and its standard DalamudPackager. The former offline Roslyn path has been removed
so local and CI builds use the same project, assets and packaging rules.
Use `-LockedRestore` to verify `packages.lock.json`.

From this WSL workspace, where Windows tools need to be launched directly:

```bash
python3 tools/build-wsl.py --probe --locked-restore
# For a portable Windows SDK, add --dotnet-path /mnt/c/path/to/sdk/dotnet.exe
```

Both build paths compile into a staging directory, run regression checks, and publish the DLL,
manifest and license notices in `artifacts/plugin/`. `-Probe`/`--probe` opens and
closes shared controller handles to inspect their capabilities; it sends no
controller output. Standard SDK commands are also supported:

```powershell
dotnet build .\src\DualSenseLighting\BetterControllerExperienceForFFXIV.csproj -c Release -p:DalamudLibPath="C:\path\to\Dalamud"
dotnet run --project .\tests\LightingChecks.csproj -c Release
```

Regression checks cover report preservation across every validity-byte value,
foreground/background transitions, disabling, reconnects, animation behavior,
focus changes during discovery and between controllers, discovery retry backoff,
write timeouts, cancellation, one-time lights-off release, failed release writes,
and foreground return immediately before release.
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

The user confirmed the revised version 0.2.1 touchpad close-up and player
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

## Verified optional hit feedback (0.14.6, 2026-11-03)

Player hit reactions is an independent checkbox under Haptics → Enemy SFX.
It follows the combat haptics master switch, shares enemy vibration strength,
and uses actual player hurt/guard PCM. It can run with Player SFX, both enemy
skill channels and footstep amplification off. Skill-only enemy feedback remains
separate; 强击 was user-confirmed with basic attack effects excluded on 0.14.4.

Do not assume every player hurt clip uses a battle/damage key. The live capture
used normal/damage_continuous and battle/weapon_guard. Resolve both normal and
battle hurt variants and weapon/shield guard variants from game data; retain
the original sound ownership, native-send preservation and independent opt-out.

The user confirmed 0.14.6 feedback works and stops when the checkbox is off.
Its 15-second hit-only trace adds 434 spatial hit-reaction blocks, using timeline
63 for PC hurt audio and timeline 78 for guard audio in this installation.
The off step was performed after recording, so physical opt-out is user evidence.
This test does not establish fresh speaker/mount/reconnect/focus coverage.

## Native footstep device availability (0.17.3, 2026-11-04)

The Footstep amplification toggle and multiplier now require a detected USB
DualSense/Edge and a selected native haptics backend. A shared capability check
in HapticOutputSelection reuses the existing Automatic/USB/Bluetooth/XInput
resolver for UI, runtime output, diagnostics and command feedback. Explicit
Bluetooth or XInput keeps the controls unavailable even when a USB pad also exists.

The current LightingSession scanner supplies validated Sony HID capabilities;
there is no new polling loop. Discovery also runs for explicit native mode and
while settings are open with all audio/light features off. A settings-only scan
writes no reports and releases shared handles on focus loss or when no longer
needed. Availability can take the existing one-second scan interval to update;
HID-open failure conservatively reports unavailable. This identifies USB/native
support, not whether game vibration or the audio endpoint is enabled.

NativeHapticOutput.Configure receives a separate availability flag, following the
new Bluetooth stereo argument. Unsupported/disconnected devices suppress only
the added footstep route and invalidate queued output. Saved choices survive so
reconnection restores the selection. No native sends, audible PCM, speaker output,
mount checks, Bluetooth spatial gains or basic-rumble pulse behavior change.

Preparation used a private snapshot while the Bluetooth session was editing. Its
97 footstep checks passed. Integration merges only the relevant hunks into the
finished v0.17.2 source, retaining its stereo handoff. The new tests cover output
mode/device combinations, real queued-frame invalidation, restored fresh output,
and settings-only USB discovery with zero writes, unplug/replug, focus and close.

## Screenshot haptics defaults (0.16.2, 2026-11-04)

The user's screenshot is now the new-install and Reset to defaults Haptics preset:
master and Combat on; Player SFX on at 25%; Boss SFX and Regular enemy skills on
with enemy strength 100%; player hit reactions off; screen effects on at 35%;
footstep amplification off with 600% saved. Shared spatial and distance fade stay
on, character origin with recommended 3–25 yalms. Player impact guidance remains
on at 100%. The screenshot's collapsed output selector and hidden camera limits
are not copied; existing output defaults and other tabs retain their behavior.

Only Configuration defaults change. ResetHapticsToDefaults continues to reuse a
fresh Configuration, and explicitly saved settings still deserialize unchanged.
Internal HapticSettings defaults stay inactive for shutdown/failure paths. The
screen-default check now distinguishes the user preset from those inactive
internal settings. All three guides list the new preset.

## Shared Advanced settings and player-only controls (0.16.1, 2026-11-04)

The v0.16 UI placed Follow skill impacts and Impact bias inside the shared
Advanced group while correctly disabling those two player-only controls when
Player SFX was off. That placement made the common group look player-dependent.
They now live in a separate Player SFX settings section under Player SFX. Shared
Advanced contains only spatial feedback and distance fade, with no dependency on
CharacterHaptics. Enemy-only audio continues to use the shared runtime path and
snapshot introduced in v0.16. No ownership, native routing or saved defaults change.

## Shared combat haptics hierarchy (0.16.0, 2026-11-04)

The Haptics tab has one full master and three sibling sections: Combat, Screen
effects and Footstep amplification. Combat has its own switch and gates Player
SFX, Boss SFX, Regular enemy skills and optional player hit reactions. Disabling
Combat leaves screen effects and footsteps selected and effective under the full
master. Disabling either parent retains all saved child selections and levels.
The new CombatHaptics property defaults to true when absent from old JSON;
HapticEnabled remains the persisted full master. Resets restore both parents.
`/bcef haptics` controls the master, while `/bcef combathaptics` controls Combat.

Haptics UI methods now live in Plugin.Haptics.cs, a partial of the existing Plugin;
there is no second settings model or additional UI framework. Output selection is
collapsible. Advanced combat settings contain common spatial and distance controls.
Follow-impact bias remains explicitly limited to the player's weapon/cast audio.

Removed HapticSettings.EnemySpatial and its native routing override. One immutable
snapshot now controls player, boss, regular, hit-reaction and proven arena audio.
Spatial and DistanceFalloff are independent: fade-only gives balanced grips with
the same character/camera distance limits; both off preserves original added
routing without requiring a listener. Enabled fade still fails closed on missing
position, listener or decoder data. Existing native vibration sends are preserved
before any of these checks. Screen output keeps its separate immutable snapshot;
footstep amplification still only extends the native footstep send.

Removed the obsolete player-only UI wrappers, fixed-enemy diagnostic origin and
last-enemy/last-hit spatial fields used only by that origin display. Command
feedback, status labels, translations and guides now describe the shared controls.
Earlier versioned sections below retain the historical behavior they verified.

## Active duty boss SFX and remembered regulars (0.15.0, 2026-11-04)

The user replaced fine-grained boss-skill eligibility with a broader encounter
policy for every duty size. While player and recognized boss are in combat,
registered NPC SFX follows Boss SFX without action, phase, attack-type or targeting
cue eligibility checks. The user explicitly chose to exempt regular enemies seen
before boss combat; newly seen NPCs during combat follow Boss SFX. This tradeoff
can also include ordinary enemies first seen mid-fight.

BossEncounterState is the sole mutable encounter tracker. Regular keys contain
actor address, entity ID and base ID, with capacity 4096. Raw boss combat closes
enrollment; promotion additionally needs player combat and a valid duty. Scene,
duty or player changes clear history; focus, toggles, visibility gaps and combat
ending do not. Saturation stops broad promotion until reset. Tracking also runs
before audio initialization so enabling the feature mid-fight preserves sightings
from this loaded plugin session.

Framework discovery gathers all supported BattleNpc 1/5/11 candidates and every
player/player-like NPC seed. A reused HashSet follows player ownership chains
before boss activation, regular enrollment or audio publication. The candidate
arrays cover the entire object table and grow with it; no truncated ancestor
snapshot is used. The existing selector resolves baseline hostile regulars and
native boss helpers/summons; the tracker then publishes BossEncounter or the
remembered Regular category. Queued sounds cannot cross a role/entity/scene
identity replacement. Output publication remains foreground/PvE-only.

GameAudioLayout.FindEnemy bypasses action/release/marker tests only for an exact
BossEncounter NPC identity. Pool generation, native ID, resource, owner and roster
checks are unchanged. No action tag is fabricated. Other players and unidentified
audio cannot qualify through !localPlayerOwned. Music, UI and dialogue buses,
native vibration sends, position requirements and the enemy 3–25-yalm spatial
route keep their existing behavior. ArenaSoundEffects remains the separate proof
for eligible map/environment sounds. The broad NPC path needs ownership hooks,
not the optional skill observer; a failed skill observer clears only legacy
skill/hit sources.

Removed IncludeDutyAdds, PromoteDutyAdds, IsScriptedActor, EncounterAdd and
EncounterScript eligibility branches, the scripted TimelineSound exception and
obsolete eight-player-only tests. Regular/outside-context skill filters and
Hunt/FATE/native-helper metadata remain. The UI now says Boss SFX in all three
languages; traces include BossSfx, RememberedRegulars and HistorySaturated.
The full optimized API 15 build passes 442,958 checks. The user confirmed both
opening and final-impact 魅惑拥抱 feedback on v0.15, with native effects and
Alt-Tab release passing. The retained 05:18:55Z trace has 121 Boss/Spatial
first-half observations despite Unknown action classification, and 330 for the
second half despite its legacy Caster phase. Both use BossEncounter ownership;
two regular marks persist without saturation. The separate live regular-enemy
exemption check remains pending; its automated state/route tests pass.

## Same-name scripted boss forms (0.14.25, 2026-11-04)

The user confirmed v24 白虎 ultimate and 吉祥天女 赞歌/光之奔流/圣锤 feedback,
with basic attacks quiet. The retained 04:30:26Z v24 trace then isolates a
separate 魅惑拥抱 failure: both Miwaku sound layers have native timeline owner
index 62 but no registered source actor or action tag. NPC index 62 is the
ownerless, non-hostile giant form: name 6385, base 7715, subtype 5, director
0x80034E50 and layout 6954292. The active root is a distinct entity with the
same name and base 7712. All 627 frames retain player/boss combat and the active
eight-player encounter policy.

Plugin's scripted-actor producer incorrectly required !boss, although IsBoss
matches the NPC name. Thus a scripted form sharing its root's name was dropped
before the existing director/identity selector could inspect it. Remove that
extra exclusion and reuse IsScriptedActor unchanged. Subtype, non-hostility,
native owner, layout, director, active encounter and distinct live boss checks
remain required. Role precedence for combatants, helpers and summons is unchanged.
No boss, base, model, timeline or sound allowlist is added.

Installed BNpc metadata maps the giant to model m0430. Its hide/mon_sp001 TMB
contains the Miwaku second-half sound in the supported positional C063 form.
The full installed catalog resolves that model key as known skill execution
4573, with no typed attack claim. Existing EncounterScript release handling
then accepts it; category-0/untyped presentation actions 8537/9358 remain
Unknown. Admission alone still cannot enable an unknown, casting, body or basic
attack sound. The captured same-name NPC fixture verifies roster admission and
exact timeline-owner identity. The v25 user test confirmed impact feedback with
basic attacks quiet; its retained 04:42:19Z trace records 376 Boss/Spatial
second-half observations after the giant moved from slot 62 to slot 22. Existing
native effects and spatial/focus policy were unchanged. Version 0.15.0 supersedes
this scripted eligibility exception with the broader policy above.

## Uncategorized attacks and first-use model timelines (0.14.24, 2026-11-04)

The three retained 吉祥天女 v23 recordings show correct Boss roster membership
and audible positive playback IDs for DD, LeftTargetAE and Healwave audio. Their
action tags remain Unknown, preventing added feedback. Installed action data
identifies the common cause: 赞歌, 光之奔流 and 圣锤 are category 0, AttackType 5.
The catalog previously ignored category 0 entirely. Category 0 now needs a
defined attack type (1–8) and an execution/hit timeline in the supported row range.
Untyped, sentinel, cast-only and unrelated action categories remain excluded;
explicit category-1 basic attacks keep their existing classification.

Framework discovery observes the selected enemy's live IBattleChara cast only
after publishing its roster identity. Address, entity ID, IsCasting, ActionType 1
and known skill metadata are required. This reuses existing bounded per-actor
action history and template matching before the action-result callback. Cast IDs
are not added to roster equality, and no recent-action time window is introduced.

The retained 白虎 ultimate trace has valid C012 listener/instance links and audible
ByakkohouHit audio, but all clip tags are Unknown. Concrete model keys such as
mon_sp/m0454/show/mon_sp001 do not occur literally in the sheet; their metadata
uses [SKL_ID]. Timeline lookup now preserves this priority: actor-observed
matches, exact literal keys (including Unknown), then an aggregate of every
matching template. It never takes the first wildcard match. Mixed kind/phase
masks retain their existing ambiguity. Mixed attack-type metadata does not
invent typed evidence; it can still identify a skill execution for the existing
C012 route. Root C063 caster-origin audio still requires typed attack evidence.

These changes reuse the existing C012 effect ownership, native mixer and spatial
routes. No boss, duty, action ID, localized name or sound-bank allowlist is added.
The v23 arena-ground rule already applies across territories and is unchanged.
This broadens supported playback mechanisms; it is not proof that every boss's
signature attack or all damage-only sound layers are covered. First-use instant
skills with ambiguous metadata and no earlier action evidence remain a limit.
Native effects, player ownership, distance cutoff and foreground release keep
their existing behavior. The user confirmed the three 吉祥天女 skills and 白虎
ultimate on v24; their retained trace evidence is recorded in the QA report.

## Ground effects during boss combat (0.14.23, 2026-11-04)

The user explicitly simplified the category policy: arena/ground attacks follow
Boss skills while the player and a recognized boss are in combat. This removes
the requirement to prove a separate director-to-group-to-action ownership chain.
It is intentional encounter classification, not a claim of native boss ownership
or damage-only semantics. Actor-based player and teammate audio retain their
existing filters. No extra user setting is added.

ArenaSoundEffects asynchronously loads the current territory's typed vfx.lgb,
nested SgbFile resources (bounded depth 4/groups 128), and each referenced ScdFile's
SoundBasicDesc.Attribute. Exact sound-bank/index pairs with the native Loop bit
are excluded. Bg/planmap ancestry is not imported as arena attack audio. The
exact product LoadCatalog method, compiled unchanged against CN Lumina GameData,
resolves territory 778 to MoonRelease #0 only; floor Full/Crescent and torch are
outside the loaded VFX ancestry. This scene has no looping catalogue candidate
and does not directly test loop rejection. LoopStart/LoopEnd alone are
insufficient (torch can have zero loop
points but still carry the native Loop attribute).

SpecialSoundPlayback validates the engine getter and native system-layout lookup,
then maps initialized B0=1 SoundData through its system manager +0x90 and entry
ID +0x98 to the positive mixer ID. It never uses the stale normal controller
+0x40. The retained 0.14.22 trace proves Moon slot 0/generation 2 has entry 4085;
its exact layout entry transitions to mixer 495718450, matching the bus-five voice.
Native reads are bounded and identity-checked, with no game calls or mixer locks.

Framework discovery runs at most 20 Hz, scans map instances only when an eligible
catalogue candidate exists, and uses an exact live SoundLayout.SoundData pointer,
resource/instance identity and validated native translation getter. Sources are
published immutably; the audio callback only reads those results. Ground effects
use the actual world source position and existing character-centered enemy
spatial/3–25-yalm fade. Ambiguous, unloaded, deferred or unpositioned sources stay
unrouted. Master/Boss toggle, player/boss combat, foreground, PvP/logout and scene
changes clear eligibility. The catalogue loader is joined on disposal.

Only identified arena effects gain access to environmental bus 5 in added enemy
haptics. Normal ambience, UI, music, player ownership, native vibration sends,
speaker output and AllCombat lighting bus selection are unchanged. Diagnostics
show active arena sources and optional setup/catalogue errors; recordings retain
up to 128 distinct source IDs without moving positions consuming that history.
No boss, NPC, skill or sound-filename allowlist is used.

The user confirmed v23 月下美人 feedback, quiet ordinary ambience outside combat,
and preserved native haptics/speaker effects. The retained 03:44:36Z trace joins
MoonRelease pool slot 129 to mixer ID 498109006, with 101 Boss/Spatial voice
observations and peak RMS 0.321073. Other environmental voices remain unrouted.
Its ArenaSources coordinate export was empty because Vector3 exposes fields;
the recording-only fix reuses Point.Of, with compiled-plugin regression checks.
The original trace is preserved unchanged. See the QA report for scope and limits.

## Arena active-handle evidence (0.14.22, 2026-11-04)

The retained 0.14.21 recording at 02:46:55Z captures MoonRelease in pool slot194
from4.699 to10.381 seconds. NativeTraceError is empty. Its origin-zero acquisition
has no synchronous enemy action scope. Crucially, SoundData mode+B0 is1: its
normal controller at+38/+40 is stale from an earlier UI sound in the same slot.
The recorded early normal resolution must not be used as the world effect's
playback identity. Native disassembly identifies the separate +90 system object
and +98 per-layout entry ID, resolved through the engine's matching system layout.
A recorded entry4043 changes from controller0 to positive mixer492626514, which
matches the new environmental voice. The missing proof is the active pool
header's +98 value; the final snapshot was taken after native teardown cleared it.

PoolSound now retains FirstActiveHeader and FirstActiveSeconds once per exact
(slot,generation,reservation) when active and resource-backed. Header still shows
the latest state. This preserves onset evidence alongside teardown, without
assuming which unknown field is meaningful. Tests cover delayed readiness,
cleared resources/indices, immutable history and isolation across IDs, generations
and segments. No world-audio admission or haptic routing changes are enabled.
A fresh capture and in-duty layout ownership evidence are still required.

A bounded read-only special-route probe verifies engine system-layout selection
and exact root-ID linkage for five currently active sources; one deferred
controller stays unmapped. A native bus-five tree-ambience sound follows the
same special route, so ownership must remain a separate requirement. No read
probe invokes game functions or writes game memory.

## Arena-effect audio diagnostics (0.14.21, 2026-11-04)

The 0.14.20 月下美人 recording at 00:46:34Z captures a new environmental
sound, not an omitted NPC. The action's concrete mon_sp016 TMB has no C063 or
C012 entries. Installed vfx.lgb/shared-group resources link the moon-floor VFX
to `est_spot_e3fd_MoonRelease.scd`. Its origin-zero reservation has no actor or
playback link; an unattributed bus-five voice closely matches its lifetime,
which is useful evidence but not a proven native link. Do not admit arbitrary
environment audio, infer a boss owner from a filename, or use time coincidence
as production attribution. This attack remains unsupported in 0.14.21.

During explicit recording only, SfxNativeTrace collects up to 128 origin-zero
acquisitions with exact synchronous action context and bounded module-relative
caller stacks, 64 distinct unowned bus-five voice/header snapshots, 128 sound
pool observations and 128 unique native controller-resolution observations.
All acquisitions advance per-slot generation/origin state so reused slots cannot
inherit previous provenance, including native IDs assigned after acquisition.
Raw native headers are copied with bounded RPM and retained as immutable values.
Native reads and collection failures are diagnostic-only, with no new audio hook,
playback calls, sample changes or production routing changes. Disabled callbacks
perform no allocations or native reads. Source, callback, track and root links
are checked before capture; failed transient reads can be retried on later blocks.
The snapshots and NativeTraceError are included in the existing segmented JSON.

Native evidence also identifies SoundData's embedded controller (+0x38) and a
later layout-entry playback callback that assigns its real mixer controller.
Primary LayoutEngine Sound/SharedGroup instances and ContentDirector.MapEffects
provide possible exact map ownership links. These are investigation avenues,
not enabled admission heuristics. New live evidence is needed before implementing
this separate playback/provenance path.

A bounded read-only live layout snapshot validates the typed map shape and
static getter offsets, but the game had left the tested duty (territory 129,
no active content director). It therefore does not verify the target group yet.
The snapshot enumerated 312 shared groups and 82 sound-layout objects using
bounded RPM only, with no game calls or writes. Repeat inside the encounter
before drawing conclusions about its director-managed group or sound pointer.
Outside the duty, 42 shared-group links and four exact sound-layout pointers
into the validated native sound pool were observed. That verifies this client's
hierarchy/offset method, but does not prove MoonRelease's live ownership yet.

## Scripted encounter skill performers (0.14.20, 2026-11-04)

Two retained v0.14.19 captures isolate the remaining 月读 misses. The recording
at 2026-11-04T00:26:33Z contains 剑气波动 at 24.703 seconds: its timeline owner
is object index 12, 幻影 (name 7400/base 6358), rather than the visible 豪雪 actor.
The next recording at 00:27:20Z contains the complete 月下彼岸花 at 3.944
seconds, owned by index 8, 夜露 (name 7226/base 8777). Both non-hostile BattleNpc
performers were absent from EnemyActors. Their audible voices use combat bus 2,
flags 0; source distances are about 7.0 and 5.2 yalms. Both player and boss remain
in combat, with IncludeAdds true throughout. This is neither distance cutoff nor
a disabled encounter-combat gate.

Both performers are ownerless, have valid layout IDs and share EventId
0x80034E57 with the active boss. The upper word identifies InstanceContentDirector,
as defined by [FFXIVClientStructs EventHandlerContent](https://github.com/aers/FFXIVClientStructs/blob/b69073ae2d789527fc1b9a78aa7aa590e0f299dc/FFXIVClientStructs/FFXIV/Client/Game/Event/EventHandler.cs).
Use a distinct EncounterScript role for ownerless, non-hostile BattleNpc subkinds
1/5 with a layout and that exact active director. Admission requires the existing
single-party eight-player combat context, a valid recognized boss root, and that
same director in the current frame's active-boss event list. Another active boss
with a different director cannot validate an inactive anchor. Known root bosses
are not assigned the scripted role. No new persistent encounter latch is needed.

Timeline 6429 is a typed skill execution. Complete-ultimate timeline 6432 is also
a confirmed skill execution, but its presentation action has AttackType 0 while
a separate action resolves damage. Allow validated CasterOrigin execution for
this role without changing the typed requirement for ordinary boss roots.
Classify scripts only from a nonzero, released skill timeline and validated
C063 binding or its exact C012 bridge. Appearance timeline 6431 has no Action
reference and stays unknown. Native action-only scopes, inferred helper tags,
body/voice audio, cast-start/bone tracks, and explicit basic attacks stay quiet.
Existing stock targeting-cue suppression and enemy spatial routing still apply.

Event/layout identity participates in the immutable roster. Leaving context,
losing the matching active boss event, despawning or reusing actors invalidates
queued script audio. Root action history is preserved when only its combat flag
changes; the temporary active-event span adds no framework allocation. The rule
contains no boss, NPC, skill or sound-bank allowlist. Coverage remains bounded
to the existing eight-player policy; it is not proof of a native summoner link
or universal support for all encounter presentation formats.

Live 0.14.20 recordings at 00:44:44Z and 00:45:28Z confirm the two attacks:
Wave6429 has 45 sampled Boss/Spatial voice observations; complete Ultimate6432
has 181. Both now resolve through EncounterScript, with no setup errors. These
actual captures use Default bindings; the role-scoped CasterOrigin branch also
passes fixture and installed-catalog checks. The user confirms both work and
other tested cues stay quiet. Unknown Tsubazeriai remains unadmitted.

Classification remains semantic execution rather than damage-only filtering:
known Gosetsu appearance6434 also has 85 Spatial observations with nonzero PCM.
Do not claim that every appearance/presentation cue is excluded. Unknown
appearance6431 is separately excluded; it must not be conflated with6434.

## Manual SFX recording and stock targeting cues (0.14.19, 2026-11-04)

`/bcef trace` and the Diagnostics Start/Stop button now record until the user
stops. Each segment is bounded to 60 seconds or 1,200 frames. Frozen segments
serialize as compact JSON on one background save queue, with at most one writing
segment and one active segment. Atomic publication keeps `sfx-trace.json` as the
latest segment and retains the latest 20 timestamped files in `sfx-traces/`.
Session timestamps and segment numbers link a longer recording. No audio is
recorded or uploaded. Distinct instance/node ownership snapshots and later
resolved clip bindings are retained; final framework samples precede rollover
or a manual stop. Transient Windows latest-file rename denials retry the same atomic publication
on the background writer for at most roughly 500 ms. Permanent IO failures
preserve earlier files, attempt the active partial segment independently, stop
recording and report the error.

The v0.14.18 line-stack recording at 2026-11-03T23:45:24Z distinguishes the
stock `ETC_LockOn_Share_5sec_lock` warning bank from the actual typed GunAtk
impact. Installed Lockon-sheet resources link nine dedicated `ETC_LockOn_*`
banks to shared targeting cues; the failing bank serves both horizontal and
vertical line-stack markers. Exclude only this structural stock bank family
from added enemy haptics. A bounded native filename copy is cached per sound
reservation, resource handle and sound number; acquisition invalidates it.
Unreadable/deferred resources are retried without changing native playback.
There is no per-boss filename list or broad rejection of AttackType-zero skills.

The user confirmed the line-stack and initial 月下彼岸花 impact on v0.14.18.
The post-intermission complete ultimate and 剑气波动 need fresh recordings:
the previous recorder overwrote them. Research identifies different timeline
assets for the complete ultimate, an omitted non-hostile scripted source for
剑气波动, and a boss-in-combat gate that can drop during an intermission.
Those findings do not justify enabling unknown appearance cues or claiming the
two missing attacks are fixed in this build.

## Positional caster-origin execution audio (0.14.18, 2026-11-04)

The targeted 0.14.17 recordings at 2026-11-03T16:50:27Z and 16:52:53Z capture
月下彼岸花 and the line-stack 黄泉之弹. Their `UltRaid_c` and `GunAtk_c`
sounds are correctly owned by the boss but rejected as Caster tracks; neither
miss is a distance cutoff. The separately reported round-stack recording at
16:53:30Z retains working target/default attack routes. Those are different
playback paths and must not be treated as proof that the line stack works.

The native caster selector describes spatial attachment, not skill phase.
Both missing C063 entries have duration 1 and positional flags 0x01, with no
skeletal binding. The earlier 拉格 body/preparation entry has flags 0x05 and
bind point 71. Independently verified native code at RVA 0x192EBD0 tests the
positional bit at data+0x14, the bone-binding bit at the same field, and the bind
index at data+0x15; otherwise it reads the actor origin. Validate that layout
from the original module copy before enabling the optional extension.

Flags alone also match weapon-change sounds. Reuse Action.AttackType metadata
inside the existing actor/timeline resolution pass: only unambiguous recognized
types 1..8 qualify the new CasterOrigin binding for confirmed Execution tracks.
Zero, 0xFFFFFFFF and unknown types do not qualify, and typed/setup aliases fail
closed. The captured attacks use type 5 while 深宵换装 uses zero. Metadata is
compiled at initialization; callbacks add scalar evidence checks without file
parsing, another action-history scan or new steady-state allocations.

Default/target, explicit hit-phase, C012 and encounter-add rules are preserved.
Bone attachment alone does not veto an already-confirmed hit phase. The native
audio/haptic graph, speaker routing, spatial/distance rules and foreground guard
are unchanged. The added policy recognizes typed execution audio, not exact
impact-only samples: a skill's execution asset can include its own windup.
Independent review and captured/varied regression fixtures are complete; the
final build and live line-stack/raid-wide results are recorded in the QA report.

## Separate stack impacts and encounter routing (0.14.17, 2026-11-03)

The ownerless-spawn recording identifies 月读's fans, with no spawn-time owner,
tether, link family or status source. A second recording captures the line-stack
marker as Boss/BossMechanic but its actual impact as a different Regular NPC,
name 7230/base 8770, with confirmed execution timeline 4575. The absent output
is an enemy-category issue, not missing PCM or a distance cutoff.

Reuse the completed eight-player encounter-add policy rather than adding a
月读/stack/SCD rule. New pipeline fixtures cover the captured fan/impact NPCs
and fourteen unrelated identity/timeline combinations, including category changes
and basic/unknown/body-audio exclusions. Trace frames expose the duty and party
metadata, combat states and include-adds decision. This is encounter grouping,
not a newly discovered native owner relationship. The 0.14.17 trace at
2026-11-03T16:45:32Z verifies fan timelines 4573/4597 as Boss/EncounterAdd, with
spatial and fixed-distance-cutoff routes and no setup errors. It has no stack
marker, impact NPC or stack sound, so line-stack verification needs a targeted
recording. Other duty sizes retain their existing association behavior.

## Ownerless summon diagnostics (0.14.16, 2026-11-03)

The [relationship investigation](OWNERLESS-SUMMON-INVESTIGATION.md) distinguishes
native ownership from tethers, aggro families and event/layout context. A passive
NPC packet observer is enabled only during `/bcef trace`; it adds birth metadata
and snapshots of all live battle NPCs, including actors outside the current
enemy roster. No new ownership rule is enabled without live relationship evidence.
The existing haptic categories, effect rules and focus guard are unchanged.

## Coverage goal for enemy haptics

Use the boss trials to discover reusable playback and ownership mechanisms across
encounters. Prefer general action/timeline/effect links and regression fixtures
over per-boss or per-skill exceptions. Record ownerless encounter associations as
limited fallbacks. The [coverage strategy](ENEMY-HAPTICS-COVERAGE.md) lists current
mechanisms, verification levels, known gaps and priorities for future trials.

## Delayed skill-effect ownership (0.14.15, 2026-11-03)

The third Heroes' Gauntlet boss's 兽魂的苦痛 uses execution timeline 3216,
`mon_sp/m0109/mon_sp016`. Its TMB contains C012 visual effects, including
`m0109sp16c0a1.avfx`, but no C063 sound clip. The delayed `m0109_sp16_c` sound
still resolves the boss as its caster, but the native audio listener retains no
skill timeline. Broadening unknown root-boss AVFX would also admit idle/aura
sounds such as `m0109_stlp01`, so that is not a valid fix.

Reuse GetTimeline to link validated C012 clips through their native container and
scene handler to the actual VfxObject listener. Confirmed execution/hit timelines
qualify; casting, shared/unknown and auto-attack timelines do not. Preserve the
tag in a bounded cache keyed by exact listener and resource instance, with the
same immutable caster identity required at playback. A read-only scene constructor
observer clears reused addresses for all scene effects. Repeated observations
and audio lookups allocate nothing; collisions discard evidence rather than
returning another effect's tag. The old C063 caster-track guard remains intact.
Original native calls, audio buffers and controller output are unchanged.

Version 0.14.14 also associates duty-737 rubble name 9512/base 12030 with a live
boss name 9511. The user confirmed 落石 works; its recording shows the rubble
as BossSummon, its skill track as BossMechanic and 1,220 added boss blocks with
no setup/spatial errors. In 0.14.15 the user confirmed 兽魂的苦痛 works with
basic attacks quiet. Its 2026-11-03T14:50:11Z trace retains released timeline 3216
on both C012 scene effects; the main `m0109_sp16_c` sound routes to spatial boss
feedback. Total added boss blocks rise by 2,253, with zero regular/hit/footstep
or cutoff blocks and no setup/spatial errors. The unrelated `m0109_stlp01` aura
stays unowned. This verifies the captured skill and the reusable ownership path,
not every possible AVFX format. Trace clip metadata includes the resolved listener,
resource instance and retained effect tag for future attribution checks.

## Boss effect ownership (0.14.7–0.14.8, 2026-11-03)

The user's 拉格 trace exposed a distinction between boss body/casting audio and
released mechanics. The old hostile-combatant-only roster accepted the body
track but discarded separate AVFX emitters. They share NPC name ID 10256, have
no usable OwnerId and use sound-only subtype 11 in the pinned client.

The initial 0.14.7 association incorrectly required equal base IDs. The user's
follow-up trace had zero output: caster suppression worked, helper registration
did not. Fresh headers with entity-ID cross-checks establish the combat boss's
base ID as 13294 and helper base ID as 9020. Address reuse after a fight makes
unmatched historical header reads insufficient evidence of the original actor.

0.14.8 reuses the duty boss catalog and immutable ownership roster. A sound-only
helper needs the same numeric NPC name ID as a real hostile boss in that duty,
not the same base ID. Remove it when that association disappears. Boss sound
classification separates caster/end/cast from hit/release phases, excludes
character/voice audio, and admits qualified helper AVFX as mechanic audio. Do
not fabricate an action ID or expand matching to filenames, proximity,
recent-action windows or all combat sound. Known auto-attacks remain rejected.
Regular enemies keep their verified skill behavior, and player hurt/guard
feedback keeps its independent option.

Trace actor roles and NPC/base IDs alongside sound phases. The user confirmed
0.14.8 released effects work while body/casting sounds stay quiet. Its 15-second
boss-only trace recorded 4,007 spatial boss blocks from mechanic emitters, while
the body's `d1047_sp01` remained a rejected Caster track. No setup/spatial errors
or regular-enemy/hit/footstep output occurred. The trace also recorded 19 fixed
distance-cutoff blocks. Build/API checks and 378,508 controller/UI assertions
pass. This verifies the captured mechanics, not every boss, every effect layer,
or a fresh physical check of native speaker/mount effects and reconnect/focus.

## Embedded attacks and summoned effects (0.14.9–0.14.10, 2026-11-03)

AnimationEnd describes skill execution, not necessarily casting. 幻光三段
(20427/category 3) and 投掷弱鸡匕首 (20438/category 4) contain their attack sounds
inside those timelines. Reuse the existing sound-clip observer and validate the
native C063 data plus its scheduler actor binding. Only default/target-bound
execution sounds are promoted; caster-bound, cast-start, voice, ambiguous/shared
and basic-attack sounds stay excluded. Keep file I/O and asset parsing off audio
callbacks. The user confirmed both attacks work in 0.14.9. The old 拉格 caster
track is covered by a regression fixture to prevent broadening it again.

Delayed knife attacks come from four separate battle NPCs: duty 737, name 9507,
base 11889. They lack the hostile flag and a usable OwnerId. Reuse the immutable
enemy roster and add the verified encounter association in BossMechanics;
require a live name-9505 boss and respect a conflicting native owner. Direct
native boss ownership works generically for battle NPCs. Do not infer unknown
ownerless summons from distance, timing, display text or resource filenames.
Associated summon AVFX uses the boss channel, actual positions and fixed fade.

The user confirmed the delayed attack works and basic attacks remain quiet in
0.14.10. Its trace includes all four BossSummon actors and spatial/distance-cutoff
routes for their real attack sound, with no setup errors. Core/API checks pass
with 378,545 assertions. This verifies this ownerless mechanic; other ownerless
summons need verified encounter associations. Generic native ownership has
fixture coverage, not a fresh live test in this recording.

## Exploding undead summons (0.14.11–0.14.12, 2026-11-03)

The second Heroes' Gauntlet boss is name 9508/base 11761. Its exploding summons
have no native owner and use name 9509/bases 11766–11769 or name 9510/bases
11762–11765. Extend BossMechanics' existing exact duty/name/base associations;
keep the live-boss anchor, independent categories and source-position fade.
The first missing explosion was captured at about 1.288 yalms, so changing the
fade or strength would not fix its missing ownership.

The user confirmed 0.14.11 explosion feedback, but trace analysis showed only
one variant routed. Name-9510 actors had previous auto-attack history and their
later AVFX was suppressed as unknown. A verified encounter mechanic association
now permits that AVFX despite history; explicit auto-attack scopes and character
audio remain excluded. Generic native-owned fighters and sound-only helpers
keep their stricter history rule. Do not claim every unknown sound has an exact
skill ID. The build and 378,582 controller/UI assertions pass. The user confirmed
0.14.12 explosions work with basic attacks quiet. Its trace routes name-9510
explosion AVFX spatially; name-9509 was verified on 0.14.11. Both variants are
covered across those recordings, with no setup errors or unwanted categories.

## Open-world bosses and eight-player encounter adds

The classifier now supplements the duty boss list with installed Fate, BNpcBase
and NotoriousMonster metadata. A boss-marked FATE plus the actor's native FateId
and an elite/boss rank qualifies; ordinary event enemies stay regular. Registered
B/A/S Hunt name/base pairs use Boss skills too. Sound-only helpers still need a
live matching boss under the existing roster selector. No new native hook is
needed; GameObject.FateId at +0xF8 was checked against the installed API metadata.

Regular hostile combatants temporarily use Boss skills during an active
eight-player boss encounter. Require ContentMemberType.MembersPerParty = 8,
AlliancePartyCount <= 1, an absent/eight-player queue override, and combat state
for both the local player and a recognized boss. The installed normal/extreme
Tsukuyomi rows both have QueueMaxPlayers = 0, MembersPerParty = 8 and
AlliancePartyCount = 0. Four-player rows have MembersPerParty = 4. Republishing
the normal roster when combat ends invalidates queued boss-category ownership.
The EncounterAdd role retains regular-enemy skill qualification while choosing
the boss output category. Actual bosses retain their released-effect filter.
This is encounter-category routing and does not infer an actual summoner.

EnemyEncounterChecks covers exact Hunt identity, FATE trash and membership,
missing metadata, duty-list preservation, helpers, party/alliance/combat gates,
category transitions and warmed lookup allocations. Boss/Regular tooltips are
updated in all three languages. Build validation is staged separately under
artifacts/fate-bosses. Live verification is pending.
