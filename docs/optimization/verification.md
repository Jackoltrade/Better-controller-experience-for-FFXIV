# Optimization verification — 2026-11-05

Baseline: `9354236`. This is a reliability/maintainability change, not a new
feature release; package identity and version remain 0.18.0.0.

## Current results

**Escape handling passes the final user-assisted check:** Info and Reset close
without clearing the selected target, including held Escape. The failed checks
below are historical; they preceded the corrected 9d2 build.

Transient dialogs now disable Dalamud's pinning, click-through and background-blur
options. This removes the extra title-bar menu that overlapped the close button
in the reported screenshot. The compiled-dialog probe confirms those options
are off while the close button and Escape handling remain enabled.

Diagnostics is available only when Dalamud reports `IDalamudPluginInterface.IsDev`.
Regular repository installs, including testing releases, hide the tab and reject
`/bcef trace` without opening recording consent or starting a recording. Release
builds loaded through the development flow retain Diagnostics. Framework timing
activation follows the same gate; saved consent cannot override install origin.
Regular help and tooltips no longer point to an unavailable tab.

This follow-up passes a locked Release build with zero warnings/errors, all
**56/56 check groups with no skips**, 107 process assertions and binary metadata
verification. The preceding Diagnostics-snapshot coverage run also passes all 56
groups. Latest build/probe receipts: `work/dialog-titlebar/verify-results.txt`,
`probe-results.txt` and `artifacts/dialog-titlebar/plugin/build-verification.json`.

Follow-up source SHA-256:
`d90450184d859792896fcaeb599a0544d45797f7f3938ecb61e8cc5196f8aef9`.
DLL SHA-256:
`f7c2aa9b2ea4247bd9c86c8552bbaa9c9edf2486bf5cba48d7ee105253812c07`.

New tests invoke the compiled UI constructor and Plugin command/help handlers
with development and regular install flags, including testing releases, copied
recording consent and timing activation. They extend direct UI/orchestration
execution; they do not render every UI branch or exhaust native audio adapters.

## Coverage and performance limits

The Diagnostics snapshot (`91f2272d`, before the title-bar change) has
**3,473/6,187 mapped lines (56.13%)** of source-union coverage; execution
inside the compiled plugin is **1,476/6,187 (23.92%)**. Core remains the best
exercised layer. Substantial UI rendering, audio-adapter and orchestration paths
remain unreached. The new command/consent cases improve a specific boundary;
they do not close that wider coverage gap or establish whole-game coverage.

Performance remains scoped: the five helper benchmarks and stable roster
fixtures allocate zero managed bytes, while device/report loops allocate.
Synthetic loop timings do not establish an overall in-game speedup. Autosave
serialization and storage remain synchronous when a save runs. Debouncing,
retry backoff and nonblocking lock acquisition do not remove that IO latency.

## Verified optimization snapshot (`e49807e`)

The final independent quality assessment is **9.1/10**, with unrounded weighted
result 9.093 under the unchanged weights. No concrete remaining P1/P2 was found.
See [the independent review](final-review.md). The user confirms the verified optimization
staged build passes Info/Reset Escape without clearing the target, quiet stack
arrows with the actual hit still vibrating, lighting/preview, all three language
options, native walking/mount vibration and controller sound, Alt-Tab lights-off
release, and unplug/reconnect. These are user-assisted observations on the
connected controller, not simulated equivalents or exhaustive hardware coverage.
The desktop tool still fails before app access; no tool-controlled game test is
claimed. No remote CI or release publication was performed.

That snapshot's local Release gate passes (zero warnings/errors):
**56/56 groups with no skips, 516,943 assertions**, actual compiled-owner tests,
process-failure/malformed-argument checks, 107 PowerShell assertions, 44 Python
tests, catalog/workflow/whitespace checks, binary verification and actual standard
and localized packaging. The separate coverage invocation passes all 56 groups.
Snapshot proof: `work/optimization-input-fix/verify-results.txt`, `coverage-run.txt`
and `artifacts/optimization-input-fix/plugin/build-verification.json`.

Source SHA-256: `657acce8a1cba9bfa68fff570af379ccd74f5c35c81550133f850714ec06c3c9`.
DLL SHA-256: `9d2d7128971b9e1a390fdcecbea16ca47522101613b0c78c17464990f549111a`.
At that snapshot, the staged dev DLL, checks, icon and standard package matched
the verified candidate; Chinese development descriptions were preserved. Dalamud's log
records a normal unload and successful reload at 21:19 on 2026-11-05, after
staging this candidate, with the new cleanup path. This is load/lifecycle evidence;
it does not substitute for device feel, UI interaction or framework timing.

A confirmed measurement-scope flaw is repaired: ordinary cache work previously
shared an allocation interval with the test method's profiling patchpoint and
on-stack replacement. A single immediately optimized, non-inlined **test helper**
now invokes pre-created ordinary callbacks. Production tiering, original warmup
bodies/counts, iteration/index sequences, behavioral counters and strict zero
thresholds stay unchanged. Three controls prove zero, a real escaping allocation,
and allocation on changing cache associations. All 32 additional strict windows
were audited and migrated consistently; the controls run last. Timed whole-loop
benchmarks retain their distinct scope. See [the window map](allocation-window-map.md)
and [diagnostic evidence](allocation-diagnostics.md).

The historical uninstrumented 6,192/2,792-byte deltas and the 24,624-byte
instrumented settings-check failure remain preserved and unattributed. Successful
verification of the repaired measurement contract does not assign their causal
type/stack or establish universal test determinism. Earlier red receipts below
are historical stages, not the current gate result.

Snapshot source-union coverage was **3,436/6,173 mapped lines (55.66%)**; actual
compiled-plugin coverage is 1,431/6,173. Sixty-one of 62 authored files map, with
ArenaEffectSource unmapped. These are reached-line metrics, not correctness or
whole-game coverage claims.

## Historical Escape and timing investigation

Before the corrected 9d2 build, the user reported that Escape closed Info while
also reaching the game UI. This was a failed earlier check, subsequently resolved
and confirmed in the final result above. An earlier timing sample contains
**25,895 updates,
0.057 ms average, 24.489 ms maximum**. Those wall-clock aggregates establish a
real sample and a tail to investigate; they do not identify its cause or prove
stable per-frame latency. The old aggregate cannot attribute that peak. New
opt-in phase counters will help locate future slow updates; this does not
establish that the peak is fixed.

The first Escape follow-up used the existing Window API: process the dialog before
settings, request parent focus on dialog close, and close a child before drawing
when its parent is closed. With the exact built DLL and installed 15.0.3.8
WindowSystem/ImGui, a real-frame probe reproduces the old one-frame inhibition
gap and verifies uninterrupted inhibition with Escape held in the corrected
case. It also reproduces and prevents a parent-close orphan frame. Evidence:
`work/window-escape-frames/actual-owner-results.txt`. This fixture uses private
service/key fixtures; it is not a running-game input observation.

The user then reported that Escape still **cleared the selected target**. The
WindowSystem proof covered addon-close inhibition, not targeting cancellation;
the live failure supersedes any claim that focus handoff alone solved it.
Installed IL shows public framework Update runs before native Tick, and ordinary
focused windows do not clear the game's keyboard buffer. The corrected build
therefore consumes only a focused dialog's Escape through the public IKeyState
false-only setter during Update, explicitly closes that dialog, and retains
consumption until physical release. A cached callback reads only GetAsyncKeyState's
high bit while already consuming. It never writes a pressed key or installs an
input hook. Independent release works without Draw; HideUi/ShowUi publish
visibility so an invisible dialog cannot acquire a new press. Constructor
rollback and disposal independently detach both events before window/font cleanup.

The corrected 9d2 DLL passes 55 focused actual-owner checks and the installed
WindowSystem/KeyState/ImGui buffer probe. The probe checks zero Escape before a
simulated native consumer, unchanged unrelated keys, held/released input without
Draw, and subsequent independent presses. It does not execute game targeting.
Evidence: `work/window-escape-candidate/hidden-final-results.txt` and
`work/window-escape-frames/consumed-target-path-final-results.txt`. In-game target
retention was then confirmed by the user for both Info and Reset, including the
requested held-key check. The selected target remains selected.

A second user timing sample, collected on the phase-instrumented build before
the final input-guard change, contains **20,286 updates, 0.044 ms average and
0.751 ms maximum**. Its reported peak is below 1 ms. This is a new observation,
not a controlled speedup comparison or an explanation of the earlier 24.489 ms
peak. Phase details were not supplied.

Two preserved SFX recordings identify separate issues. The line-stack warning
uses the existing shared LockOn bank; BossEncounter's broad-admission exemption
incorrectly bypassed its warning veto. The veto now applies to every admitted
non-player enemy, while real impacts and untagged encounter effects remain
eligible. The old broad-NPC fixture expected warnings admitted and zero resource
reads: its failure is retained in
`work/optimization-input-fix/pre-contract-update-results.txt`. It now verifies
known non-warning sounds and twelve cached reads across its twelve reservations;
its provenance, position, broad admission and zero-allocation checks remain.
New warning/impact cases and a private old-condition red control cover the
corrected contract. See [the focused plan](boss-false-positive-plan.md).

The train trace has no boss-SFX output. Its matching camera trace records 1,085
screen-shake mixes, including 843 frames without native input. The user confirmed
that switching screen-effect haptics off stops the continuous vibration while
Boss skills stays on. This identifies the screen-effects path; it does not
establish that a train sound asset caused the camera offsets. No boss-SFX or
duration-based ambient filter was added. Selective environmental-shake filtering
is not supported by this trace's source metadata.

Post-shutdown guards also reject callbacks starting after teardown or a failed
constructor. The compiled-owner regression poisons retired dependencies and
invokes Open, Draw, Update and OnCommand. The saved pre-guard DLL fails all four
entries; the current full gate passes. This does not cancel a callback already
past its guard. Evidence: `work/lifecycle-callback-checks/baseline-results.txt`.

Timing now includes seven fixed phases and counts of updates at least 1, 5 and
16.7 ms. Disabled/capped collection reads no clock; enabled work allocates zero
managed bytes. The mutable frame is passed by reference and completed in
finally, including early exits and failures. Phase means use their own sample
counts. Maxima are independent, and elapsed time includes thread pauses.
An advisory ten-batch no-op probe measured about 0.327 microseconds incremental
enabled instrumentation per seven-phase frame. It is not game CPU/latency proof.
Raw results: `work/update-tail-counter-checks/results.txt`. A read-only Sol review
found no actionable defect in this integration.

The fresh coverage invocation passes all 56 groups. A prior attempt tried to
install the collector at an empty default path and failed to reach NuGet; that
receipt is retained as `work/optimization-live-fix/coverage-missing-tool.txt`.
The successful run explicitly reuses the already installed pinned 18.12.0
collector. No test failure was bypassed or retried for a passing result.


## Follow-up investigation chronology after 166ff17

The native Windows NuGet audit now **passes** against the official feed. Both
restored graphs record `enableAudit=true`, `auditMode=all`, `auditLevel=low` and
no audit warnings: the actual locked production graph (DalamudPackager 15.0.0,
DotNet.ReproducibleBuilds 1.2.39), and the SDK graph including Dalamud.NET.Sdk
15.0.0. An isolated Newtonsoft.Json 12.0.1 negative control fails restore with
NU1903 for GHSA-5crp-9r3c-p9vr and returns nonzero. No vulnerable dependency was
added to the plugin. Raw receipts: `work/optimization-followup/audit-results.txt`,
`audit-control-results.txt`, and each graph's `project.assets.json`.

The prior error was environmental: inherited HTTP(S) proxy variables used
`socks5h://`. Normalizing that prefix to `socks5://` **only in the audit process**
allows the existing audit script to work. Repository configuration and Windows
network settings are unchanged. [.NET's proxy documentation](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclient.defaultproxy)
lists supported proxy schemes. The earlier unavailable-audit account below is a
historical receipt, superseded by this successful built-in audit and negative
control; OS/runtime/native-library security remains outside this NuGet audit.

A focused 166ff17 before/after experiment also corrected the interpretation of
the original pooled Bluetooth timings. Eight default-runtime processes retained
40 chronological batches per case. Both variants begin around 38–40 µs for eight
Bluetooth devices and later settle around 14–15 µs; repeated native-code-load
JIT events closely accompany the major shifts. The earlier 18.104→43.952 µs
pooled medians are phase-confounded, not proof of a fixed 2.4× code regression.
This does not assign every microsecond to JIT or eliminate normal noise.

A one-line guard now avoids an ownership-map lookup when neither light nor motor
cleanup is being acknowledged. Real clearing behavior is unchanged. In the narrow
private comparison, USB median-of-process-medians is 4.754→4.656 µs per eight
reports; Bluetooth last-ten-batch medians overlap (14.744→14.677). No consistent
Bluetooth steady-state gain is claimed. Report checksums/counts and allocation
medians match, and the 73,989 Bluetooth/stereo assertions pass. Original raw
measurements are retained rather than replaced by selected steady windows.
Receipts and reviewed addendum: `work/independent-review/ack-profile/`.

The standard locked build and all 54 groups passed again and were staged at the
registered development path. Dalamud's log confirms reload at 18:47 on
2026-11-05. The desktop tool still fails at initialization (`sandboxCwd is not a
local file URI`), before game input. At that stage user-assisted UI/hardware and
framework timing checks were pending; the final results above supersede that
earlier state.

Follow-up lifecycle verification now exercises actual compiled owners through
managed fake Hook callbacks, rather than only testing the cleanup helper. The
pre-fix fixtures reproduce effect-disposal failure skipping the receive hook and
constructor rollback replacing an Enable exception. Existing ResourceCleanup now
releases both nested owners; rollback preserves the original exception or
aggregates both failures with the original first. All **38 actual-owner
assertions** pass in the private build and in the parent full proper-argument run.
No live OS detours are installed by these fixtures. Receipts:
`work/lifecycle-owner-checks/{pre-fix,post-fix}-results.txt`.

The fresh parent follow-up build is optimized with zero warnings/errors; its
proper-argument full suite passes **54/54 groups, 515,972 assertions** and binary
verification. The subsequent malformed-argument process guard is **red**: it
retained an additional EnemyEffectChecks failure of **2,792 bytes on managed
thread 15**, alongside the intended usage failure. Later groups complete
(52/54, two failures). This is a new captured child; it does not recover the older
lost child output. Its immediate failure is now known, but the allocation's
causal type/stack is still unproven. The entire verification command is **not**
reported passing. A separate instrumented run passes the same 54 groups and all
515,972 assertions. Current source-union coverage is 3,285/6,022 eligible mapped
lines (54.55%); the actual plugin module covers 907/6,022, including 51 Plugin
lines and actual XInput/capture/observer cleanup paths. This expands reached
behavior; it does not turn the red uninstrumented child into a pass or certify
live OS hooks. Sixty of 61 authored files map; ArenaEffectSource remains unmapped. See [allocation diagnostics](allocation-diagnostics.md) and
`work/optimization-followup/verify-results.txt`.

## Initial committed-candidate results (166ff17)

Windows x64; .NET SDK 10.0.403 / runtime 10.0.14; installed CN Dalamud 15.0.3.8.
Local compilation used the verified package cache with NuGet audit disabled for
that offline build only. The separate audit gate below was not reported green.

| Check | Result |
| --- | --- |
| Locked restore and optimized Release build | Pass; zero warnings/errors |
| Full regression executable with current compiled plugin and Dalamud arguments | 54/54 groups pass; zero skips/failures; 515,934 repeated assertions |
| Compiled plugin integration | Recording coordination, setup/recording consent rollback, installed native layout and Window API checks execute within the full run |
| Binary verification | Optimized x64, API 15, source/manifest/project identity match |
| Independent runner process checks | Sync and async failures preserve later groups and return failure; malformed metadata arguments fail clearly; success returns zero |
| Windows PowerShell 5 process and development-manifest checks | 107 assertions pass; CN development copy retained |
| Python packaging/workflow/coverage-summary tests | 44 pass |
| Generated enemy catalog, actionlint, whitespace | Pass |
| Actual packaging from this verified private build | Pass; six standard ZIP entries and ten per public ZIP; allowlists, payload hashes, icon/notices and localization validated |
| Independent recovery probes | Enumeration failure and a timeout applied to the nonzero motor write both recover to modeled motor state 0/0 with actual clear reports |
| Independent stereo stress | 250,000 centered observations; no split pair; retired epoch cannot publish into the active interval |
| Multi-device release fixture | 32 owned paths with 31 stalled writes: approximately 513 ms; healthy write submitted before the stalled deadlines expire |

The integration run caught an extracted UI reference to its former owner and an
unsupported filename in the isolated recording test. Both were corrected before
the final complete run. No production archive allowlist or test threshold was
relaxed. Assertion counts are not independent test-case counts.

A subsequent malformed-argument process guard failed with its generic message,
after the complete 54/54 suite and binary verification had passed. Its original
child output was not retained, so the cause is **unrecovered**; it is not attributed
to the allocation flake without evidence. A new single diagnostic invocation
produced exactly the intended usage failure and completed every later group.
The guard now includes the captured child output on every mismatch, with its
strict expectations unchanged. This improves diagnosis; it does not prove the
original intermittent failure is fixed.

Verified source fingerprint:
`c5ab09e054b011fba562c1e17d323f328af3d967b2a0815091f6750577ae5539`

Verified plugin DLL SHA-256:
`528a6f1c7b9115535f00ddd95bd8f94652d16d4fe850c4a3b0e294d6aac47ec5`

## Initial coverage and performance

These measurements describe the initial committed candidate. Current coverage,
user timing and follow-up instrumentation results appear above.

Microsoft `dotnet-coverage` 18.12.0 collected both the linked-source check module
and the actual plugin with embedded portable symbols. The same full run passed
under instrumentation. Source-union coverage is **3,132/6,014 mapped eligible
production lines (52.08%)**, with **2,882 zero-hit lines** and **60/61 authored
files mapped**. `Core/ArenaEffectSource.cs` is unmapped. Generated code, tests,
build outputs and third-party modules are excluded.

The linked module covers 2,736/2,871 eligible lines (95.33%). The compiled plugin
covers 540/6,014 (8.98%); most Core behavior is exercised through the linked copy.
Plugin orchestration and live native adapters include zero-hit code. Neither the
linked percentage nor the union is proof of whole-plugin/native-game coverage.
No source-filtered branch percentage is inferred from the collector's total.

Uninstrumented A/B/B/A device replay used the same harness and frozen baseline
on .NET 10.0.14. Allocation medians are unchanged: one USB animation report
298.368 bytes/tick, eight USB reports 2,095.808, one Bluetooth report 312.464.
An intermediate extra 72 bytes/report from an async helper was measured and
removed using a single-consumer ValueTask.

Representative median CPU times increased: eight 63-byte USB devices
4.457→5.493 µs/tick; one Bluetooth device 6.783→7.130; eight Bluetooth devices
18.104→43.952. These are synthetic advisory timings, not a speedup or a claim
about game FPS/physical HID latency. Recovery cases now perform additional clear
writes and are not equal workloads. Roster fixtures execute the actual exclusion,
encounter-state, selection and publication pipeline, including churn, reset,
player ownership chains and saturation; warmed batches allocate zero. That new
seam has no equivalent callable baseline, so no roster speedup is claimed.

Diagnostics can optionally measure complete framework Update duration, with
bounded aggregate counters and no per-frame log or allocation. At that stage,
no in-game sample had been collected; the user's sample above is now available.

## Initial audit, independent review and remaining limits

This historical audit state is superseded by the successful native audit and
negative control recorded in the follow-up chronology above.

The new NuGet gate audits the actual locked production graph and explicit SDK
graph, including transitive dependencies. NU1900–NU1905 fail the gate. Local
Windows feed failure was verified to return nonzero; it is an **unavailable audit**,
not a clean result. A separate Linux read of NuGet's official advisory base
(updated 2026-11-04) and update (2026-11-05) found no entries for the three pinned
build package IDs: Dalamud.NET.Sdk, DalamudPackager and
DotNet.ReproducibleBuilds. This scoped cross-check does not replace a successful
built-in audit or audit the OS/runtime/native libraries. CI has not run remotely.

A fresh read-only Sol review found no concrete remaining P1/P2 in the candidate.
Its category assessments range from 8.5 to 9.0; it **withholds an overall 9+**.
The UI owner still contains substantial rendering code, native teardown has not
been fault-injected in a running hooked game, coverage includes unexecuted
adapters, and current hardware/frame-time evidence is missing. Those are explicit
limits, not grounds to claim the original target was achieved.

The intermittent first-window 6,192-byte allocation was reproduced during the
investigation; its causal stack/type remains unknown. Exact-zero checks still
fail on nonzero allocation and report the delta/runtime. Independent groups
continue; there is no tolerance, retry-to-green or tiering-off pass policy.

At that stage, remaining validation included in-game settings/dialog/CJK rendering, consent,
lighting/preview, native haptics/speaker, XInput/Bluetooth, Alt-Tab/reconnect and
framework timing; a successful reachable-feed audit. Historical user hardware
reports are not substituted for those checks. True unload cannot guarantee a
stop on unreachable hardware; retained path cleanup has a finite 32-entry
admission bound and never silently evicts unresolved intent.

## Reproduce

Use the existing Windows .NET 10 SDK and compatible API 15 references:

```powershell
./tools/build.ps1 -DalamudLibPath <API-15-directory> -LockedRestore
./tests/CheckRunnerProcessChecks.ps1
./tests/BuildProcessChecks.ps1
./tests/CoverageProcessChecks.ps1
./tools/audit-dependencies.ps1
./tools/measure-coverage.ps1 -PluginPath ./artifacts/plugin/DualSenseLighting.dll -DalamudLibPath <API-15-directory>
python -m unittest discover -s tests -p "test_*.py" -v
dotnet ./artifacts/checks/LightingChecks.dll --benchmark
python ./tools/package-release.py
```

Coverage and performance run separately. Coverage uses static instrumentation of
shadow copies and validates its summary inside each `artifacts/coverage/run-*`
directory. Release DLLs and PDBs remain unchanged. Keep raw failing output as well
as the summary; the CI verification-evidence artifact is uploaded even on failure.
