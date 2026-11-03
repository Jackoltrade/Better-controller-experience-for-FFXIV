# PS5 DualSense Controller for FFXIV

DualSense controller customization for Final Fantasy XIV using Dalamud.

[GitHub repository](https://github.com/Jackoltrade/PS5-DualSense-Controller-for-FFXIV-)

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

Open `/ds5` or the plugin's configuration button to change:

- Eleven RGB effects: Solid, Rainbow, Breathing, Color Shift, Aurora, Fire,
  Heartbeat, Comet, Rainbow Pulse, SFX Loudness and SFX Color Blend.
- RGB color, brightness, and animation period.
- Bottom LEDs: the five white lights below the touchpad, with count, animation, and independent timing.
- A separate **Haptics** tab for optional, sound-driven combat feedback from your character.

Lighting and Haptics contain the configuration controls, with explanations in
tooltips: hover a control or its **(?)** marker for help. The **Diagnostics** tab
groups controller and focus status, output errors and counters, the SFX level,
left/right haptic meters, Limit Break detection and the 15-second SFX recording.
Running `/ds5 trace` selects Diagnostics so its countdown is visible.
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

Open `/ds5` → **Haptics** to configure **Character combat haptics**. New installs
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

Open `/ds5` → **Haptics** → **Native footsteps** and enable **Amplify native
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
**Footstep blocks amplified**. `/ds5 trace` also records that counter,
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

Choose one of two separate options in the `/ds5` **Lightbar effect** list:

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

For a short diagnostic recording, select an SFX mode or enable combat haptics and run `/ds5 trace`,
or open **Diagnostics → Record SFX diagnostics (15 seconds)**. A visible countdown
ends automatically and saves `sfx-trace.json` beside the plugin DLL. It records
the loaded plugin version, asset names, ownership origins, playback links,
source/listener positions, active impact cue, current haptic settings, per-voice
routing/bias decisions, cutoff/suppression counts, bus/flag metadata and loudness;
it does not record audio samples. The latest recording replaces the previous one.
Recording ends on plugin unload too. The additional per-voice analysis runs only
while recording.

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

In `/ds5` → Lighting → Bottom LEDs, select **LED mode: Limit Break**.
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

The top of `/ds5` shows a centered touchpad close-up with a white touchpad,
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
3. Save, then enable **PS5 DualSense Controller for FFXIV** in `/xlplugins` → Dev Tools.
4. Run `/ds5`.

### Build and checks

Preferred build requirements: .NET 10 SDK and API 15 Dalamud libraries.
The project file is `src/DualSenseLighting/PS5DualSenseControllerForFFXIV.csproj`.
It keeps the `DualSenseLighting.dll` assembly and internal plugin ID so existing
saved settings and dev-plugin paths continue to work after the rename.

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
dotnet build .\src\DualSenseLighting\PS5DualSenseControllerForFFXIV.csproj -c Release -p:DalamudLibPath="C:\path\to\Dalamud"
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
