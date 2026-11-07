# Screen-effect source investigation

## Current result: v0.19.4

The user replaced the learning requirement with **fixed patterns stored in
code**, matched as soon as a short observed sequence fits. The purpose comment
now identifies vehicle-related screen vibration observed in Hell on Rails,
Sigmascape V1.0 / Sigmascape V1.0 (Savage) (O5/O5S), and Lapis Manalis.
Official duty names: [Hell on Rails](https://na.finalfantasyxiv.com/lodestone/playguide/db/duty/2db4beb1880/),
[Sigmascape V1.0 (Savage)](https://na.finalfantasyxiv.com/lodestone/playguide/db/duty/c6a225d5436/),
[Lapis Manalis](https://na.finalfantasyxiv.com/lodestone/playguide/db/duty/ce91893321f/).

`KnownMotionPatterns.cs` contains six resampled native-offset profiles: a
four-second XY rotational cycle, a two-second Y positional cycle, and four
short positional overlays. The private deterministic generator retains the
input recording SHA-256 values and interval selection; runtime code contains
no source pointers, boss IDs or territory restrictions. Both profile endpoints
are included. Profile buffers are immutable after construction.

`KnownMotionFilter` projects the first nonzero observation onto the fixed
profiles, keeps bounded possible phases and checks their time progression
against following samples. A plausible first observation provisionally suppresses
the known contribution
while advancing samples confirm it. A mismatch disarms provisional suppression
for that track continuity; ordinary reseeding cannot hide arbitrary in-range
motion indefinitely. Full output returns on the mismatching update. This removes
the 64-bin learning/template/cycle state, repeat qualification, fade ramp and
duty-cache/background-observation logic from earlier builds. Recording lifetime
still uses immutable observer sessions and existing consent/publication paths.

Only native secondary writers with validated scoped identity are considered.
Latest per-slot writes must exactly sum to the observed secondary camera
vectors. Unknown overlaps, stale samples, nonfinite data, backward clocks,
contention and capacity loss retain full feedback. The independent-effect guard
preserves primary AVFX, blur/chromatic and scripted effects throughout their
activity. The factor scales plugin screen amplitude after its response curve;
original native PCM and SFX/footstep/speaker routing are unchanged.

This deliberately identifies matching **content**, not vehicle ownership or a
unique playback activation. A skill using the same stored waveform can be
filtered; a different vehicle waveform remains unrecognized. No new recording
is needed to implement the saved profiles.

Release build and combined verification passed: zero warnings/errors, 58/58
executed groups, zero skips/failures, 519,914 assertions (repeated assertions are
not independent cases). Checks cover the fixed bank, phase ambiguity, changed
frequency/amplitude/axes, duplicate timestamps, candidate/source capacity loss,
unknown overlaps, exact slot accounting, gaps/contention and foreground-only
observer lifecycle. Native layout audits remain 49/49 secondary and 51/51 AVFX.
Independent read-only review found no material blocker.

The bank was generated from `09:05:00` position and `09:12:59` rotation files.
The historical v0.19.2 replay also used five held-out files, including another
duty and a rotation capture with no resets. Its first suppressed frames occurred 18–122 ms after
capture start; this includes initial camera/source observation alignment, not
physical controller latency. The two generation captures suppress 1,938/1,964
and 2,344/2,346 raw secondary frames respectively in that baseline. Independent primary/post/
scripted frames remain full in every replay. The source-loss capture remains
excluded from qualification in the conservative replay; that is not a claim
about its precise lost-event timing in production.

The historical v0.19.3 correction addressed startup/new-overlay confirmation
bursts. Its initial `12:32:43` test had 865 frames and five two-frame leaks;
provisional suppression and validated native phase-reset handling removed all
of those in replay. The user then reported rarer pulses still occurring.

Two newer v0.19.3 recordings are retained: `12:52:39` (2,618 frames, no leaked
vehicle frames) and `12:53:29` (3,972 frames, four leaks in two two-frame windows
at approximately 31.192 and 33.896 seconds). Source loss, overflow and truncation
are zero; the camera recorder dropped four and two observations respectively,
without overwriting the ring. Both leaked windows are secondary rotation only,
with no independent primary/post/scripted effect active.

Candidate inspection shows smooth points on the stored rotational waveform
narrowly crossing the original X tolerance: errors around 0.0000580 and
0.0000653 against a limit near 0.000055. Native timing and callback timing can
diverge slightly. v0.19.4 retains the normal exact-prediction fast path; only a
miss tries the current/adjacent profile segments and clamps phase correction to
10% of this update's predicted advance. It rechecks the original amplitude
limits and rejects larger jumps. Seed and correction share one projection
implementation. There is no new timer, history cache, allocation or native hook.
The allowance can accumulate over successive updates, permitting sustained
modest tempo drift; it must not be described as isolated-jitter-only detection.

Both new files now replay with all 6,590 frames filtered, including both leaked
windows. The older 865-frame Lapis Manalis recording also remains fully quiet,
and all eleven-capture independent-effect checks retain full gain. This verifies
the recorded cases, not universal semantic separation or physical behavior.
Independent review found no material blocker. Added checks cover small timing
jitter and correction beyond the bound; existing amplitude/axis, doubled-tempo,
zero-clock, native-reset, nonloop-end and startup regressions still pass.
Live confirmation on 2026-11-06: after reloading v0.19.4 and testing the affected
vehicle segment with normal skill/attack feedback, the user reported
"No pulses; intended effects still work." This closes the reported regression
for the tested scenario; broader coverage and performance limits above remain.

The warmed eleven-capture benchmark ran 1,698,270 operations with zero managed
allocations, averaging about 0.24 microseconds per replayed operation. Separate
synthetic checks measured approximately 0.19 microseconds for zero channels,
0.30 for out-of-range rejection, 1.15 for the steady fixture, and 45 for an
in-range initial search including invalidation. These are measurements, not
upper bounds or proof of an overall speedup. They exclude native hooks/copies,
parsing and overall game frame time. Correction work is bounded to three local
segments and occurs only when the normal prediction fails.

The opt-in 50% v0.19.0 prototype passed the user's live check but reset learning
after Alt-Tab. The subsequent automatic-learning build v0.19.1 was superseded
before live confirmation. Their learning requirements and timings below are
historical and do not describe the fixed-pattern matcher.

## Historical investigation before implementation

The combined camera signal alone could not safely support a global timer,
because it would also fade a new attack overlapping a long shake. The following
research established the separate source observations used above. Statements
about unimplemented fading below describe those earlier diagnostic builds.

Latest boss-focused recordings: the oldest file contains one fresh quake
construction, the middle file contains blur without a captured quake, and the
newest contains seven fresh quake constructions at distinct addresses. No
second construction at a reused quake address or separating same-instance
playback restart was observed. Exact boss-versus-player ownership links are
absent for these quakes, so the construction counts are source evidence rather
than a claim that every new quake belongs to the boss. Details and capture
limits are recorded below.

Latest position-attribution result: all 1,852 eligible camera frames exactly
match the float32 sum of the latest observed position writes to slots 0 and 1.
The remaining 112 frames are explicitly unknown because one slot had not yet
been observed after a capture-confidence gap. No frames mismatch and no capture
loss/overflow/overwrite was reported. One persistent slot-0 source and four
alternating slot-1 writers account for the observed secondary position. The
fresh skill's two distinct quake constructions overlap that motion. This closes
the position-attribution gap, without proving native birth/end or detecting a
fresh playback through unchanged instance identities. No more recording is
needed for this attribution step; fading was not yet implemented in that build.

Earlier overlap result: fresh modded-skill quakes coexist with continuing
secondary **position** motion in duty `896`. The capture contains ten distinct
quake constructions in five pairs and 2,178 frames with nonzero secondary Y
position. Its 3,057 observed secondary rotation writes are all zero, so they
cannot identify the position emitter. The revised observer captures the
verified position setter as well as rotation, under the same scheduler source
identity. Its live position attribution is confirmed above. The reset-stack
experiment yielded zero game frames for all 18 observations and has been
removed rather than retained as unused logic.

Earlier controlled-skill result: the user's three modded 全蚀斩 casts produced
six observed type-6 quake constructions, two per cast, all at distinct runtime
addresses. They share one inherited resource but have different per-cast owner
and parent identities. This demonstrates fresh instance tracking for this skill,
without observing actual pointer reuse. That capture was outside a duty and
contained no secondary rotation/reset events, so it did not test overlap with
the continuous source or live reset caller capture.

Latest cross-duty result: a second duty reproduces nearly identical secondary
rotation amplitudes and a waveform compatible with the earlier four-second
cycle. All 568 camera vectors exactly match its latest preceding source write,
without capture loss. Source, parent and config addresses differ, so this is
evidence of a common mechanism rather than a proven shared asset or creator.
The subsequent overlap capture exposed the separate position path; source
fading had not yet been implemented at that stage.

The preceding immediate ownership check validated the isolated source and
parent timeline. The owner index was
`0`, the action key was null and the scheduler-resource pointer was null.
Consequently no resource-name pointer was followed and no authored label or
player/boss/environment category was inferred. All 598 camera vectors in that
trace exactly match the latest preceding scoped write. No additional game hook
or fade behavior was added for these ownership checks. Another idle capture
would not supply the missing creator or playback-start evidence.

The preceding reset-observation recording confirms that this continuing source
resets at ordinary loop boundaries. All 1,365 camera vectors exactly match its
latest preceding write, with no capture loss. Its cursor wrap and native reset
call must both preserve continuity; neither certifies a new attack. A true
bind/activation boundary for same-object reuse remains unproven.

## What the installed client shows

Checked on 2026-11-06 against client `2026.10.06.0000.0000`, executable SHA-256
`958b78e278ad896493c484eb313ff605ab41edefef74f92b0ee078e69b281bf9`.

| Path | Finding | Consequence |
| --- | --- | --- |
| AVFX quake, authored types 6 and 9 | Each runtime node has its own allocation and curve state. Its sampler runs before comparison with a shared winning candidate. | Construction and sample calls can expose distinct source identities. |
| Final AVFX shake application | Copies the selected vectors into shared camera offsets. Candidate selection replaces values; it is not an additive mixer. | Subtracting successive camera values cannot recover each source's contribution. |
| Blur | Compares candidate amount × weight before writing shared post-processing values. | Final blur parameters are not individual source identities. Per-source blur capture remains unverified. |
| Scripted shake | A shared slot holds one preset, cursor and gain. A start call replaces that state, including on restart. The actual frame update is inlined elsewhere. | A cursor wrap is not a new effect. Overlapping AVFX and scripted shake are separate paths; this slot does not preserve multiple scripted effects. |

The quake constructor is at RVA `0x3C8D10`; its sampler is at `0x3C8EA0`.
Two factory paths allocate `0x240` bytes for these nodes. Both call sites and
the constructor's nested sample call validate the seven-argument constructor
and three-argument sampler ABI. The constructor-derived virtual adapter also
targets that sampler. Signatures must match uniquely, and original instruction
and adapter checks must pass before the optional diagnostic hooks are created.
These checks establish a binding for this executable, not coverage of every
screen-effect producer or another client build.

## Focused developer capture

The existing consented `/bcef trace` flow now adds source observations to
the camera companion file (`camera-haptics-timing-v5`). Additional hooks are
created lazily when needed. AVFX identity hooks run only for permitted foreground
recording; secondary motion hooks also run while screen feedback is enabled and
foreground permission is valid. They preserve the native
arguments, return value and original call. Observation never changes the camera
or native vibration; the automatic filter scales only the plugin screen layer.

The source event buffer retains the first 16,384 events, with 512 identity slots.
Callbacks use preallocated value records, bounded memory copies and a
nonblocking recording lock. They perform no file IO. Stop retires the buffer;
the existing save worker makes the detached snapshot and publishes it through
the existing archive/retention system. `Lost`, `Overflow` and `Truncated` expose
missing evidence. The separate 4,096-frame camera ring reports `Overwritten`
as well as contention drops, so a long recording cannot appear complete merely
because `Dropped` is zero. Disabled hooks and the managed recording buffer are
separate from the overhead of enabled native snapshots, which still needs a
live measurement.
Normal filtering does not allocate the 16,384-event diagnostic buffer.

Each event contains a clock, observation epoch and observed construction generation.
Sample events also contain numeric identities for the runtime node, owner,
parent, inherited resource, authored wrapper and curve data. Only the named fields are saved, not native
memory dumps. Float arguments and four native numeric fields are stored as exact
IEEE-754 bit patterns; their time units are not assumed. Native construction is
registered before calling the original constructor because it can sample the
new object before returning.

`StartKnown` means **construction entry was observed in this uninterrupted
capture epoch**, not that an authored playback began then. Objects already
running when recording starts have an unknown start. Permission changes,
unreadable observations and dropped callbacks invalidate identity confidence.
An observed reconstruction at the same address gets a new generation. All end
evidence is explicitly `Unknown`: a missing update is not proof of destruction.
This capture does not yet identify the winning source, recover discarded
candidate waveforms, prove playback restart semantics, or join the inherited
resource to a scene `VfxResourceInstance`. Coverage of all possible construction
routes remains unproven.

## First live recording and the secondary path

The 格莱杨拉波尔 recording on 2026-11-06 contains 12 distinct sampled AVFX
addresses and 11 observed constructions. Several sample windows overlap, but
these are short observations, not certified lifetimes. The 82 generation
identities are largely confidence resets after 46 dropped source events; they
must not be interpreted as 82 effects. The source buffer did not overflow.
The camera ring overwrote 18 early frames and dropped one observation.

The continuous manager signal is already present about 19 seconds before the
first captured AVFX construction. Subsequent external read-only probes, with
the running executable hash checked, found primary offsets zero and continuing
secondary rotation. The secondary manager's two tracked lists were empty;
its rotation vector at `+0x90` was nonzero. That first capture identified a
different route, not the originating effect or its age. It is not evidence that one of the 12
AVFX objects should be faded. The user reports no extra camera-shake mod/helper.

Static inspection then identified the indexed rotation setter at RVA
`0x471B40` and its native caller `0x1914220`. The setter copies a vector into
manager `+0x90 + index * 0x10`; it does not add contributions. The caller
converts evaluated degrees to radians with native distance attenuation before
calling the setter. Its controller type is independently selected from generic
constructor candidates by matching the vtable's `+0xD8` dispatch to the validated
producer. No constructor hook or inferred creation time is added: a separate
reset path bypasses that constructor.

The next diagnostic revision adds a thread-local, nesting-restored association
between that producer and its indexed setter calls. `SecondaryRotation` events
contain source/context/config identities, signed slot, scope status and exact
vector bits. Unscoped writes have generation zero and unknown identity; a
rotation source cannot inherit an AVFX construction generation at the same
address. These are observed setter arguments, not an exhaustive history of all
camera writers or a guarantee of the final camera winner. Native calls and
their arguments remain unchanged. Source/error support is explicit in the
header. Camera frames now record primary and secondary vectors separately from
the same bounded snapshot already used for their combined signal.

The subsequent idle trace contains 2,462 scoped writes from one source/context/
config/slot tuple over 23.39 seconds. All 2,457 camera vectors exactly match the
latest timestamp-preceding setter argument by float32 bits. Primary offsets and
captured scripted output are zero. This establishes attribution in that interval;
it does not establish a native birth/end or classify every future producer.
Controller audio inputs are partly shared, so the complete physical vibration
cannot all be attributed to this source.

A separate bounded scalar probe found a configured duration of 120 native units
and a normal sample-cursor wrap while source, context, config and owner remained
unchanged. Constructor, rebind and reset paths can reuse the same object. No
reliable immutable native generation or unwrapped age was found. Reset calls
also have generic playback-control callers; they must not automatically become
new generations. The next diagnostic revision records the validated
`0x19028C0` reset calls, their source identities and preceding cursor/state as
observations only. It does not change the recording's generation, playback,
camera or controller output.

That revision's live test recorded 1,368 scoped rotations and three resets at
1.430233, 5.4303602 and 9.4306278 seconds. Each reset saw a previous sample cursor
of 120 and state zero; source, context, config, slot and observation generation
remained unchanged. Intervals were 4.0001272 and 4.0002676 seconds. All 1,365
camera vectors exactly matched the latest preceding setter argument. There were
no lost events, dropped/overwritten frames or capacity overflows. This is
positive evidence that the reset participates in the observed ordinary loop,
not a native restart marker. A longer idle recording would not resolve the
remaining same-object activation ambiguity.

## Ownership, unique IDs and the dungeon-only clue

The user's requested distinction is between player skills, boss skills and
continuing encounter/environment effects. These are separate from playback
identity:

| Evidence | What it identifies | What it does not establish |
| --- | --- | --- |
| Runtime node plus observed construction generation | A separately observed effect instance within a capture epoch | Every replay of a pooled object |
| Validated scheduler owner index resolved against the current actor roster | A timeline's associated actor | That actor authored the skill, or a new playback started |
| Authored timeline key or shared resource | Content used by one or more instances | A unique runtime instance or cast |
| Verified playback-start transition | A fresh activation of an existing instance | Not established for the continuing secondary source yet |

Assigning a larger synthetic counter does not supply a missing native start
event. Distinct instances must stay distinct even when actor, skill or resource
are the same. Unknown ownership must remain unknown rather than becoming
environment or boss by default.

There is a concrete way to reuse existing ownership code. For the observed
secondary source type, `source + 0x30` points to a parent timeline. Its validated
vtable `0x24452D0` slot 29 points to a getter whose exact original instructions
read a signed index at `timeline + 0x18C`. This matches
`SfxAudioCapture.GetTimeline` and can reuse `ResolveTimelineCaster` against the
current local-player index and enemy roster. Slot 21 likewise has the exact
`timeline + 0xA8` string-pointer getter already checked by
`EnemySkillFilter.Timeline`. Bounded UTF-8 reads can expose the authored key
without invoking either native getter. The public
[FFXIVClientStructs scheduler layout](https://github.com/aers/FFXIVClientStructs/blob/0adb2e545ac53cacc9341c1d030eb1a9495af49c/FFXIVClientStructs/FFXIV/Client/System/Scheduler/Base/SchedulerTimeline.cs)
is supporting context; the installed executable's validated bytes remain the
binding authority.

The live probe checked the executable hash, source type/context/config, parent
type and exact owner-index getter before reading the index. It rechecked source
and parent identities afterward. The result was index `0`, with no simultaneous
actor-roster snapshot; it cannot establish even a current actor match from
historical traces. Moreover, a timeline attached to a player need not be a
player skill. On the second probe, the source no longer matched, so no timeline
key or retired object's metadata was read. The user confirmed being outside the
duty at that point. These asynchronous observations are not destruction events
and do not exclude address reuse between checks.

The subsequent return-duty capture independently reproduced the isolated
secondary contribution: 567 scoped writes, 565 exact preceding-event camera
matches and no lost source events or frames. Two resets with cursor `120` and
state zero were 3.9906 seconds apart. The parent passed both getter checks with
index `0` and a null action key. Native controller-audio inputs also appeared in
some frames, so total physical vibration cannot be attributed solely to the
captured screen source.

For a null action key, a separate resource name may still identify authored
content. The installed timeline's `+0x98` resource acquisition path reaches a
concrete constructor and name assignment. These validate resource vtable
`0x2444DC0`, embedded-name vtable `0x2444DB8` at `+0x38`, and a null-terminated
name pointer at `+0x40`. A bounded read-only probe checks those types and pointer
identities before decoding at most 256 UTF-8 bytes; it invokes no resource or
game method. This is an authored-resource label, never a playback generation or
automatic environment classification. Some other current upstream resource
offsets differ from this executable, so the probe uses the verified native
chain rather than applying that layout wholesale.

The immediate watcher subsequently inspected the source from the next saved
trace at 2026-11-06 08:10:20 UTC. Both source and parent checks passed; the parent
still reported index `0` and a null action key, and its scheduler-resource
pointer was also null. The probe correctly skipped all resource/name
dereferences. Independent analysis verified 598/598 camera vectors against the
latest preceding setter, with 600 scoped writes and no capture loss. The
parent and config matched the preceding return-duty capture, while source and
context differed. Thus neither parent nor config can replace the individual
runtime-node identity. These are observations across captures, not exact birth
or end times. Null metadata on this timeline does not prove no upstream origin
exists elsewhere in the game.

For AVFX quake sources, there is a separate shared-owner opportunity:
`node + 0x1F8` is propagated by the common runtime constructor and uses the same
owner flag field as animation-sound nodes. The existing sound path follows that
owner's `+0x270` listener to actor/effect provenance. Its listener type and exact
cache association still need validation for a quake source. This path must not
be substituted for the secondary source's different timeline hierarchy, and an
inherited resource pointer must not be cast to a scene resource merely because
its name resembles one.

The dungeon-only symptom, idle/out-of-combat persistence and source change after
leaving the duty make an arena/environment timeline worth investigating. They
do not justify classifying all dungeon shakes as boss effects, or making all
unresolved sources fade. The live ownership/key/resource checks are now
complete for this observed timeline and do not expose an authored label.
Further work needs a validated creation/activation provenance link rather than
another idle trace or a guessed category from its raw owner index.

A final bounded constructor review found four direct calls through three
immediate callers. The same scheduler type can be created in a queue's embedded
timeline slots or as an anonymous nested timeline using in-memory data. The
constructor initializes both resource and action-key pointers to null, so their
absence is a supported construction state, not an environment classifier.
The two-pointer constructor ABI and those call paths were established, but
neither an exhaustive creator classification nor a separating same-object
playback-start event was proved. Adding a constructor ID would therefore
identify observed allocations without resolving reused playback semantics.

No further idle-loop recording is needed for the reset/lifetime or metadata
questions above. The initial implementation requirements were:

1. Establish the actual bind/activation transition for same-object playback.
   Constructor-only identity, pointer equality, cursor wraps and the observed
   reset operation are insufficient on their own.
2. Keep source age separate from the combined camera signal. Retain age through
   ordinary waveform zero crossings and authored loops; discard age on an
   uncertain transition, tracking loss, focus/session change or a verified new
   playback. Unattributed effects must keep their existing strength.
3. Apply attenuation to the identified plugin contribution, preserving primary
   attack, blur/chromatic, scripted and native controller feedback. Because the
   existing response uses a square root, halving raw camera motion would not
   halve controller amplitude. The eventual factor must act at the appropriate
   gain boundary.
4. Verify a concrete implementation with an aged source and a new overlapping
   attack. The earlier attack capture and later idle captures do not jointly
   prove that output policy.

Those diagnostic builds did not implement fading. A global timer,
resource-name grouping or camera-vector subtraction would not meet the
source-isolation requirement. The pattern-based implementation described at
the top of this document does not claim exact playback lifetimes or a review score.

The bounded activation review found that bind/rebind operations assign config
and playback parameters, unlike the periodic reset controls. Their observed
call paths do not prove that every fresh playback invokes them, or that every
same-config bind means a new playback. Adding constructor/bind hooks alone
would not justify an exhaustive generation claim.

An earlier proposal qualified only wall-clock continuity of the observed
source/context/config/slot tuple. It was deferred while investigating ownership
and unique-playback tracking. The later user request selected observed-pattern
matching instead. The current implementation requires matching cycles, not
elapsed age alone, but retains the limitation that an unchanged pattern through
the same tuple can remain faded. It must not be described as exact per-playback
lifetime tracking.

## Cross-duty comparison and the next discriminating capture

The user confirmed that the position/overlap recordings are from a third
dungeon, different from the two earlier cases. The captured content-finder IDs
are `1076`, `286` and `896`. Keep these as independent encounter contexts:

| Captured duty | Continuing motion observed | Evidence and limits |
| --- | --- | --- |
| `1076` | Secondary X/Y rotation, with ordinary four-second resets | Isolated writer exactly matches the preceding rotation writes; raw timeline ownership is anonymous. |
| `286` | Nearly identical secondary X/Y rotation at different runtime/config addresses | Independent cross-duty waveform similarity; the short trace has only one reset, so it does not independently establish the reset period or a shared asset. |
| `896` | Secondary Y position, with one persistent slot-0 writer and four alternating slot-1 writers | Exact reconstruction of all 1,852 frames with complete slot history; fresh AVFX skill effects coexist with the motion. Same-instance replay remains unproven. |

This third case demonstrates why rotation-only or waveform-based rules would
miss legitimate continuing motion. The revised observation uses the shared
native scheduler producer and explicit position/rotation writers rather than
encounter IDs. Multiple cases broaden observed coverage without proving every
boss, camera path or playback transition. The next useful variation is a short
capture of a boss attack arriving during continuing movement, with the attack
name and approximate timing, rather than another idle trace or an unbounded
whole-fight file.

The second duty's trace reports content-finder duty `286`, versus `1076` in the
earlier captures. Its secondary rotation ranges are approximately
`[-0.0010868, 0.0010665]` radians for X and `[-0.0031416, 0.0031416]` for Y,
closely matching the earlier traces. Primary offsets, secondary position/Z and
captured scripted output are zero. Its single observed reset has the same
cursor `120` and state zero. One reset cannot establish a reset period by
itself; numerical waveform fitting is compatible with the earlier four-second
oscillation without proving the assets are identical.

The immediate read-only probe validated the same scheduler class and again
observed raw owner index `0`, a null action key and a null scheduler-resource
pointer. Different runtime/config addresses remain distinct identities. These
commonalities justify investigating a shared game mechanism without adding a
boss-name rule or classifying sources by amplitude, period or dungeon alone.

An experimental revision added bounded native stacks to existing reset
observations. The live overlap test made 18 capture requests; every result
contained one native frame and zero game-module frames. The chosen unwinder
supplied no useful caller evidence through this hook boundary. That is not
evidence that no caller or activation path exists. The added stack helper,
reservation budget, payloads and their tests were removed; the older
SFX-specific stack mechanism was restored without changing its behavior.

The current diagnostic observer covers the verified indexed position and
rotation setters using the same native producer scope and bounded vector
recorder. Explicit `SecondaryPosition` events carry a separate position payload;
position, rotation and reset share the same per-source generation. The legacy
serialized `SecondaryRotation` family name is retained for existing v5 files.
The binding validates the actual position call, arguments, shared manager,
lock imports and vector-copy layout. Its hooks now run for either a
permitted foreground recording or foreground screen filtering. No native
camera/controller values or playback
generations are changed, and missing samples do not establish an end.

Prefer a 25–30-second recording containing the continuing shake and three
separate casts of the user's modded 全蚀斩, with each cast's visible camera
effect finished before the next. A separate short recording of one boss attack
can check overlap. The current capture is unsuitable for an unbounded whole
fight: the earlier rotation-only idle rate gave roughly 39 seconds of
camera-ring history and 157 seconds until the source-event buffer filled.
Recording both position and rotation adds events and can roughly halve the
latter; multiple writers and attacks shorten it further. These are estimates,
not guaranteed combat durations. Long captures can retain the beginning of the source stream and the end
of the camera stream, losing their overlap. Finding an observed reuse case is
useful evidence; failing to find one cannot certify that reuse never occurs.

## Controlled modded-skill evidence

Read-only inspection of the user-supplied Classic Paladin mod's current metadata
maps `ws_s11.tmb` to 全蚀斩 and explicitly references
`vfx/papapld/camera/quanshizhan_z.avfx`. The camera asset contains two type-6
AVFX quake effectors and a radial-blur effector. The effective authored timeline
schedules the quake entries at intervals 2–20 and 22–43 in its native units.
These interval values are not labeled seconds, and raw zero loop fields are
not interpreted as a runtime looping guarantee. The authored file is evidence
about content, not an instance or native playback epoch. No mod files were
changed and no filename-based production rule was added.

The subsequent 16.33-second capture contains three pairs of constructions at
approximately 6.666/7.319, 9.496/10.162 and 12.637/13.319 seconds, followed by
152 sample events. All six quake addresses are distinct and have observed
construction starts in one uninterrupted epoch. Each pair shares its own owner
and parent, while all six share the inherited resource and two authored wrapper
identities. This is concrete evidence against grouping casts solely by shared
resource or wrapper. Code/tests cover observed reconstruction at a reused
address, but this particular trace does not exercise that native reuse case.

The skill's measured camera motion in that earlier trace is on the primary AVFX
path, accompanied by blur. Captured secondary and scripted fields remain zero, as do reset-stack
requests. The SFX context reports duty zero. Loss, overflow, drops and camera
overwrite counts are zero. These facts explain why this otherwise useful
recording cannot yet validate an aged secondary source plus a fresh attack, or
the caller evidence on actual reset events. Those limits prompted the
inside-duty overlap capture.

The following 20.89-second inside-duty trace supplies that aggregate overlap:
all 2,178 frames have nonzero secondary Y position, ranging approximately from
`-0.04409` to `0.03000`, while ten observed type-6 constructions form five
pairs. Each pair has a distinct owner and all ten source addresses differ.
They share one inherited resource and two wrappers. There is no observed
pointer reuse in these constructions. The native reset observations comprise
one source cycling about every two seconds and four additional sources reset
together about eight seconds apart. All measured secondary rotations are zero;
rotation observations do not attribute the position signal to any of those
sources. No captured-data loss or overflow was reported. This evidence supports
separate fresh AVFX instances coexisting with continuous motion, without yet
identifying the individual position writer or validating a fade.

The next position-enabled capture contains 1,964 camera frames, 2,770 position
writes, 2,770 rotation writes, 17 reset observations, two quake constructions
and 137 quake samples. Source losses, overflow, camera drops and overwrites
are zero. Three explicit gaps invalidate observation confidence; they do not
establish native construction or destruction. After both slots have preceding
observations in the current epoch, the latest slot-0 vector plus latest slot-1
vector matches all 1,852 eligible camera XYZ triplets exactly by float32 bits.
The other 112 frames remain unknown. An independent reverse-search analysis
reproduced these counts without forcing missing slot values to zero.

Slot 0 is written by one persistent source. Four other sources alternate
overwriting slot 1. These are overwrite semantics within each slot, followed
by addition of the two slot vectors in the native camera writer; summing all
five source samples would be wrong. Existing source/context/config tuples
match the earlier inside-duty capture, while confidence generations change
across explicit gaps. All 92 primary-active camera frames also have nonzero
secondary position. The two newly constructed quake nodes at approximately
5.489 and 6.154 seconds have distinct addresses and share their per-cast owner
and inherited resource. No address reuse was observed. This establishes the
source separation needed to explore sustained-instance attenuation while
preserving these newly allocated attacks. It does not establish a fresh-playback
marker for a surviving same-identity source or validate any fade output.

## Three boss-focused recordings

The user subsequently clarified the intended scenarios: the two older files
include boss-SFX vibration, while the newest includes actual boss-fight camera
shake over the continuing shake. Treat the oldest two as SFX/combat context,
not controlled tests of boss camera-source reuse. Their incidental camera
effects cannot automatically be assigned to the boss. The newest is the
relevant reported boss screen-shake case, although its seven quake
constructions still lack exact per-effect actor attribution. Attack names and
precise timing were unavailable; do not invent them or require them to retain
the observed source-separation result.

All three files preserve the continuing secondary rotation alongside combat.
They have no source-capacity overflow or camera-ring overwrite. Each reports
one dropped camera observation; the newest additionally reports two lost
source observations. Those losses invalidate confidence epochs rather than
creating evidence of native reconstruction.

| File start (UTC, 2026-11-06) | Captured duty | Quake construction evidence | Other evidence |
| --- | --- | --- | --- |
| 09:12:59 | `286` | One observed construction at approximately 4.851 seconds; 187 samples over about two seconds | Primary position/rotation accompanies the unchanged continuing secondary source. |
| 09:13:42 | `286` | No captured quake construction or sample | Blur appears in 100 camera observations around 1.535–2.526 seconds. Individual blur-source identities are not captured, so this does not establish reuse. |
| 09:27:08 | `1076` | Seven observed constructions at seven distinct addresses | Two sources around 12.976 and 13.737 seconds share one owner, inherited resource and authored wrapper, yet have distinct runtime addresses. This is reuse of authored context, not observed reuse of a quake instance. |

The first two files retain the exact same secondary source/context/config
tuple. The last file uses another tuple. The measured continuing rotation
matches its latest preceding slot-0 write for all retained camera frames
(2,346, 840 and 2,112 respectively), and its native resets retain the familiar
cursor `120`, state zero and approximately four-second cadence. No additional
secondary writer or distinguishing activation transition appears during the
recorded primary effects.

None of the eight constructed quake sources has an exact actor-provenance
join in the saved SFX metadata. In the oldest file, its owner and inherited
resource match neither the recorded effect-owner intermediates nor scene
instances. The newest file has no saved effect-owner/clip entries. Combat and
sound-acquisition timestamps alone cannot supply those missing pointer links.
Consequently attack names/timing can help identify the intended episode, but
the camera files alone do not prove which constructions are boss-owned.

The native sampled phase-like fields `1C8`, `1CC` and `1D4` do not decrease
within these physical quake instances. The `1D0` field varies and decreases
normally, so it is not a restart counter. Extra observed generations after the
newest trace's two lost events must not be counted as new native playbacks.
Positive construction events establish fresh observed allocations; absence of
reconstruction cannot exclude unobserved replay paths. No fade output or
universal fresh-playback guarantee is established by these recordings.

## Verification limits

Managed checks cover nested initial updates, pre-existing objects, address
reuse, gaps, contention, capacity loss, retirement, detached snapshots,
nonfinite-value serialization and callback allocations. The native static
audit checks signatures, call arguments, adapter dispatch and copy bounds
against the executable above. Release compilation and these checks do not prove
live invocation cadence, native source lifetimes or physical haptic behavior.

The historical reset-caller diagnostic build passed all 57 executed check
groups with zero skips/failures and zero Release build warnings/errors. Its
subsequent live test established that the stack mechanism supplied no game
caller frames; passing managed tests did not establish native unwinding
coverage. The replacement position-and-rotation build also passed all 57
executed groups, zero skips/failures and zero Release warnings/errors. Its
checks cover separate position payloads, shared scheduler identities, exact
scalar bits, gaps/retirement/capacity/allocation behavior, four-hook failure
cleanup and unchanged original forwarding for both setters. The source-based
native binding audit passed 49 checks against the installed executable. The
subsequent live attribution is documented above; it establishes the observed
position-source mapping, not a fade or universal playback-lifetime guarantee.
