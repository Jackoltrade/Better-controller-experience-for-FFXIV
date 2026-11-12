# Further background CPU feasibility

The three implemented changes documented here are included in the **v0.20.1
release candidate**. The v0.20.0 versions, measurements and implementation
receipts below retain their original scope. See the [current QA
record](../QA-REPORT.md#current-verification-v0201) for the fresh v0.20.1 build
results, dependency-audit results and remaining gameplay limits.

Investigated 2026-11-12 against the optimized local v0.20.0 source and verified
test DLL at `artifacts/plugin/DualSenseLighting.dll`. The user subsequently
authorized the three supported changes and replacement of that existing DLL.
Implementation and verification are recorded below; the earlier component
measurements remain feasibility evidence.

## Conclusion

Further reductions are feasible without changing the enemy-observation cadence
or adding a longer focus-return polling interval. Three small candidates have
supporting component probes: skip unused background lighting rendering, avoid
resetting already-clear motion tracks, and omit output roster construction while
boss combat has closed history enrollment.

These are incremental savings. The full duty snapshot, ownership exclusion and
history maintenance must continue. No whole-process background CPU percentage or
live focus-return latency was measured, and a large component percentage must
not be presented as the same reduction in total plugin CPU use.

## Candidates supported by probes

### 1. Avoid rendering lighting that the background guard cannot use

`Windows/GameOutputGuard.cs:81` currently evaluates `LightingEffects.Render` and
its time/SFX/limit-break inputs before calling `DualSenseReport.Apply`, including
while background. `Apply` ignores the supplied lighting frame on that branch.

Render only when foreground and custom lighting is enabled; otherwise pass the
default frame. Keep native handle/capability revalidation, incoming CRC validation,
background lighting-flag edits and original native playback/output intact.
There is no new tracking, output or resume state to maintain.

The probe calls the actual `LightingEffects.Render` and `DualSenseReport.Apply`
with reusable USB63/Bluetooth78 buffers. Removing rendering produced identical
background report bytes. It does not call the complete installed native hook or
measure handle inspection and driver work.

### 2. Make repeated motion-filter invalidation cheaper

`GameScreenEffects.UpdateMotion` invalidates its filter every inactive framework
update. `KnownMotionFilter.Clear` visits 32 sources and 64 tracks each time.
All ordinary filter mutation enters through `Enter`, which sets the existing
under-lock `clockSeen` flag; a completed clear resets that flag.

The isolated candidate retains **every invalidation call**, the monitor and the
pending-invalid flag. Inside `Clear`, it skips source/track clearing only when
`clockSeen` already indicates that those tracks are clear. It still initializes
the latest-index map. Fresh tracks already have the required constructor defaults.
This reuses an existing invariant instead of introducing another lifecycle state.

Both current and candidate implementations passed the existing motion-filter
suite. Additional controls exercise construction, a contended invalidation and
an observation arriving after a clear followed by another invalidation. This is
bounded evidence, not an exhaustive concurrency proof.

Do **not** simply invalidate once on focus loss. A callback can pass
`SecondaryMotionSession.Active` before retirement, then enter the filter after
the first invalidation. Removing subsequent invalidations needs a session epoch
validated under the filter lock. That is a more complicated alternative and is
unnecessary for the smaller candidate above.

### 3. Preserve history; skip output selection when enrollment is closed

`Plugin.UpdateEnemyActors` must retain its complete observation, player/pet
exclusion, live boss-combat detection and `BossEncounterState.Update` every
existing framework update. `RememberRegular` refuses enrollment whenever the
boss is in combat, even before the local player joins combat.

After that unchanged work, `SelectRoster` has no persistent history side effects
when boss combat is true. While background it can be omitted, since audio enemy
publication is already empty. Run the original full selection/publication on the
first foreground framework update. Keep arena reset/preparation handling.

When the boss is **not** in combat, keep the original selection/enrollment pass.
Do not replace it with remembering every raw regular candidate: helper anchoring
and NPC-owner promotion affect membership, insertion order and saturation.

The probe compares actual Core pipelines over the same sequence, checking every
seen identity with `WasRegular`, count, saturation and encounter activity at
each frame. Every foreground frame compares the complete resulting roster.
Cases include transient NPCs while inactive, helpers, NPC-owned summons, reverse
player ownership chains, boss combat before the local player joins, boss ending,
saturation, territory/player/duty resets, logout and reused identities.

The candidate only skips selection when background and boss-combat is true;
preboss controls retain the original work. Production and development integration
must confirm no diagnostic consumer needs the discarded scratch output.

## Component observations

Release run: .NET 10.0.14 x64, Windows build 26100, reported AMD64 Family 26 Model
36 processor. Seven alternating warmed batches; raw arrays are retained. Times
are median batch microseconds per operation, not game-frame percentiles.

| Measured scope | Current us | Candidate us | Warmed managed allocation |
| --- | ---: | ---: | --- |
| Repeated empty motion invalidation | 0.201 | 0.093 | Zero in both |
| USB63 background render + report edit, solid | 0.288 | 0.046 | Zero in both |
| USB63 background render + report edit, fire | 0.532 | 0.049 | Zero in both |
| BT78 background render + report edit, solid | 2.758 | 2.558 | Zero in both |
| BT78 background render + report edit, fire | 2.766 | 2.571 | Zero in both |
| Synthetic background boss pipeline, 32 candidates | 24.767 | 4.117 | Zero in both |
| Synthetic background boss pipeline, 128 candidates | 15.833 | 1.792 | Zero in both |
| Synthetic preboss control, 32 candidates | 1.986 | 2.001 | Zero in both |
| Synthetic preboss control, 128 candidates | 12.929 | 13.133 | Zero in both |

The roster fixture covers copy, exclusion, state update and selection. It does
not execute Dalamud/native property reads, live boss-combat detection, player
model traversal, actual publication, arena discovery or the complete framework
callback. Raw roster batches show substantial tiering/scheduling variation;
those values establish feasibility rather than a reliable low-spec speedup.
Preboss controls execute the same work and show no demonstrated saving.
Bluetooth's required CRC work remains, limiting the relative guard-stage win.

The probes passed 146 explicit motion/report assertions and 346 roster assertions.
Repeated assertions are not independent gameplay tests. No timing threshold was
used for correctness. Repeated observe/invalidate also passed its control; active
motion processing has not been subjected to an exhaustive workload comparison.

## Other options and their constraints

| Option | Feasibility and constraint |
| --- | --- |
| A settled-background fast path in the 33 ms worker | Plausible, unmeasured and more stateful. Perform initial pause/release, then skip redundant backend/configuration/envelope work only while settings, capture identity and output state remain unchanged. Keep cleanup servicing, shutdown and late initialization; discard background test requests/confirmation cues so they cannot play on return. Keep the same polling interval. |
| Share framework foreground samples | Small plausible saving from fewer native queries. Independent native callbacks and per-device-write guards must retain their own current checks. |
| Short-circuit inactive LB reads | Plausible small saving. Avoid gauge/job reads only while still clearing tracker actor/execution/cast state. A bare return would leave stale execution state. |
| Earlier audio Render admission | Possible small saving; PCM and ownership inspection already stop. A published admission flag needs generation/settings/diagnostic and capture-activation tests. Keep original playback and lifetime hooks. |
| Slower worker timer or long background sleep | Conflicts with unchanged responsiveness unless an independently reliable focus wake is added. The existing worker observes focus on its approximately 33 ms schedule; longer waits can add delay. |
| Event-driven focus wake | Possible later, but adds lifecycle and synchronization work. Framework-only wake depends on when the host next updates. A WinEvent hook requires a message loop on its registering thread. It is not the simplest first optimization. |

The timer advice follows the existing worker design and the documented
[single-consumer/coalescing behavior of PeriodicTimer](https://learn.microsoft.com/en-us/dotnet/api/system.threading.periodictimer.waitfornexttickasync).
Windows documents the [message-loop requirement for SetWinEventHook](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwineventhook).
Neither API promises a hard wall-clock resume deadline under OS scheduling load.

## Work not to suspend under the requested contract

- Duty snapshots, hostile-NPC classification, ownership-chain exclusion, boss
  combat transitions, regular-history enrollment, saturation and identity resets.
  An NPC can appear and disappear entirely between slowed snapshots.
- Player draw-child/model refresh. A weapon/model can acquire a playing sound
  while background; missing that acquisition cannot be reconstructed later just
  by refreshing the model on return.
- Sound acquisition/reservation-link bookkeeping and AVFX-constructor invalidation.
  These protect against reused native slots/addresses, even if every other field
  looks identical. Disabling them can retain stale or lose current ownership.
- Animation/character/clip thread-static scope barriers. An original-only fast
  return that omits scope clearing/restoration can misattribute nested callbacks.
- Cleanup retries or output guards, and original game audio/native haptics.

## Implementation and verification

All three probed changes are implemented. `Plugin.UpdateEnemyActors` skips
`SelectRoster` only for `!foreground && bossInCombat`, after the original complete
observation/exclusion/state update. `GameOutputGuard` gathers lighting inputs only
for enabled foreground lighting. `KnownMotionFilter.Clear` skips track traversal
only when its existing under-lock `clockSeen` invariant shows it is already clear.

The 33 ms worker body is byte-for-byte unchanged from the previous optimized
source. Hashes also confirm unchanged enemy-history Core logic, player/sound
publication, acquisition/resolution/action and effect-lifetime adapters, and
secondary-motion session/hook adapters. No new wake mechanism, slower sampling,
queued audio, mutable cross-thread snapshot or delayed foreground rebuild was added.

The standard `tools/build.ps1` pipeline built and verified both production and
development flavors and staged the verified DLLs at their existing artifact
locations. The production DLL overwrote `artifacts/plugin/DualSenseLighting.dll`;
its source fingerprint, verifier metadata and packaged SDK payload match.
Version remains 0.20.0 and production diagnostics remain excluded.

| Verification | Production | Development |
| --- | --- | --- |
| Standard build and checks | 62/62 groups, 594,809 assertions | 62/62 groups, 594,887 assertions |
| Hardware intrinsics disabled | 62/62 groups, 594,809 assertions | 62/62 groups, 594,887 assertions |

All four runs have zero skips and zero failed groups; assertion loops are not
independent gameplay tests. The Python suite passes 50 tests and the generated
boss catalog still matches its source CSV. Both binary metadata/boundary
verifications pass. The gstack check launcher still requires unavailable Bun,
so the repository's standard Windows build/check scripts were used directly.

Before rebuilding, the expanded suite was run against the previous verified
production DLL. It failed only the new background-selection work assertion,
as expected, with 61/62 groups completing successfully. That negative control
is retained in `work/background-cpu-implementation/negative-checks.txt`.

The new compiled roster fixture executes the full `UpdateEnemyActors` adapter
against owned actor buffers and API proxies. It compares all remembered actor
identities, encounter activity and saturation to a reference that performs full
selection on every frame. It verifies preboss transient enrollment, helper and
owner promotion, reversed player ownership chains, boss combat before local
combat, background boss ending, saturation, territory/player/duty transitions,
logout/login and the exact first foreground output. Independent fixture memory
addresses are normalized when comparing otherwise complete output records.
The fixture supplies resolved duty metadata, uses null audio capture and installs
no game hooks; earlier ownership/publication and lifecycle checks remain enabled.

Compiled output-guard tests cover USB48/63/64 and Bluetooth78, exact native bytes,
zero unused time/SFX/LB input reads, current inputs on the very next foreground
call, disabled/enabled transitions and corrupt incoming CRC rejection. Existing
native-original and lifecycle tests still run. Motion-filter checks now include
fresh/repeated empty state, all 32 sources/64 tracks populated then cleared,
zero warmed invalidation allocation, contended pending invalidation, late
observations after a pause and correct resumed matching.

Two implementation cross-reviews found no actionable defect. Live background
thread CPU and focus-to-first-effect behavior still need a game session; these
fixtures do not establish physical controller latency or a whole-game CPU gain.
Restore reports NU1900 because the official vulnerability feed could not be
reached. Dependencies and the lockfile are unchanged; a fresh online audit is
not claimed. No release was published.

Local evidence is under ignored `work/background-cpu-feasibility/`: `results.json`,
the run log, `Program.cs`, `RosterProbe.cs`, copied motion-filter variants/checks,
`run.ps1` and `evidence.json`. That initial investigation built only a standalone
console probe and checked the then-current plugin/source unchanged.
Implementation receipts, the original DLL/hash record, standard build/scalar logs
and final fingerprints are under `work/background-cpu-implementation/`.
