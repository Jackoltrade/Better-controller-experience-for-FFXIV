# Controller recovery and coherent Bluetooth stereo

Plan-time review record. For implemented behavior, current verification and remaining
limits, see [the parent verification report](verification.md).

Selected review target and report destination: `docs/optimization/device-reliability-plan.md`.
Baseline: `9354236`; review date: 2026-11-05. Parent accepted the amended retained-intent/admission/ownership plan and authorized implementation. Device, stereo and focused tests are implemented; independent parent verification remains pending.

## Binding decisions

- Apply the user's simplicity and reuse principle. Preserve the single device worker, existing report builder, foreground guard, rumble shaping, native output protection and cancellation deadlines.
- Fix complete failure paths, not just the report's two demonstrations. No broad rewrite, extra dependency, persistent device journal or new background thread.
- User-authorized automatic selection covers routine choices: choose the genuinely most complete option; escalate genuine highest-completeness ties to the parent before dependent work. No such tie remains in this lane.
- `/office-hours` is skipped by explicit user instruction. This plan owns device recovery, atomic stereo publication and their focused regressions. Plugin.cs integration is owned by the parent/another lane.

## Evidence and premise table

| Premise | Receipt | Verdict |
|---|---|---|
| A local timeout deletes output history | `Core/LightingSession.cs:167`: `catch (Exception error) { Remove(path); onError(error); }`; `Remove` removes `sent` | Confirmed by static read |
| Enumeration failure disposes healthy devices and output history | `Core/LightingSession.cs:179`: `catch (Exception error) { Dispose(); ... }` | Confirmed by static read |
| A newly opened zero request can skip stop | `Core/LightingSession.cs:118`: new `OutputState` plus unchanged-zero guard | Confirmed by static read |
| Left/right can be consumed in different intervals | `Windows/NativeHapticOutput.cs:400-401` separate `Observe`; `Plugin.cs:273` separate `TakePeak` | Confirmed by static read |
| Reproductions issue no stop and split one balanced sample | `artifacts/review-adversarial/Program.cs`; Windows .NET probe rerun at baseline | Freshly reproduced with real Core source and fake HID |
| Current tests miss the local timeout/enumeration scenarios | `tests/BluetoothChecks.cs:382-437`: write-failure and parent-cancellation coverage only | Confirmed by static read |
| Scalar handoff already solves generation/reset without allocations | `Core/SfxLoudness.cs:12-57` | Confirmed by static read |
| Hardware watchdog duration and actual game timing | No current measurement | Unverified, two premises |

Fresh probe: `artifacts/release-qa-sdk/dotnet/dotnet.exe run --project 'D:\dev\dalamud plugins\ps5 controller for ffxiv\artifacts\review-adversarial\ReviewProbe.csproj' --configuration Release --no-restore`. Default sandbox launch failed before .NET startup with `WSL ... UtilBindVsockAnyPort:307: socket failed 1`; parent supplied the existing Windows-host escalation convention. Rerun with approved `sandbox_permissions=require_escalated` exited zero and reproduced exactly: enumeration `errors=1, motor state=255/255, writes at release=1, writes after reopen/zero=1`; timeout same values; stereo `tick1=0/32768, tick2=4915/0`. This confirms missing stop reports and split stereo using fake HID and real Core source, not physical watchdog duration. No shared DLL was published.

History: `git log --oneline -- <the two production paths>` shows existing controller integration in `f942aa3` and `88b69b4`; no revert hits for these paths. HEAD remains `9354236`. No local TODO/AGENTS file was found; supplied AGENTS instructions apply. No new remote fetch was performed in this restricted lane, so already-merged remote work is not independently excluded.

## What already exists

- `LightingSession`: one worker, per-path handle map and output history, one-second discovery cadence, 500 ms write deadlines, focus release, report sequence and checksum construction. Extend these rather than introduce another transport service.
- `DualSenseReport`: nullable light/motor fields protect native routes and USB behavior. Reuse for recovery reports.
- `SfxLoudness`: generation and peak in one CAS word, bounded producer retries, allocation-free take. Reuse its algorithmic pattern, without changing its scalar contract.
- `BluetoothChecks` and `BluetoothStereoChecks`: fake devices, real reports, real NativeHapticOutput and RumbleSession. Extend these existing boundaries, not private implementation hooks.
- Framework: custom .NET 10 executable (`tests/Program.cs`), not xUnit. Existing tests already protect report flags, CRC, sequence wrap, ownership, calibration, camera orientation, distance cutoff, scalar delay and stale prepared frames.

## Scope challenge

Scope accepted as-is. Estimated changed files: six (LightingSession, NativeHapticOutput, new StereoRumbleSignal, BluetoothChecks, BluetoothStereoChecks, parent-owned Plugin integration). Two new types after parent review: the signal and its small epoch accumulator. Complexity structure gate therefore applies: D5 automatically confirms the original six-file/two-type arrangement under the user's complete-choice instruction. A single type without an epoch holder would require quantization, blocking or per-publication allocation; pausing to investigate cuts no feature and establishes no better complete arrangement. Keep all recovery, exact-float, generation and test contracts fixed; pending remedies not decided here: none. No package, deployment artifact, external service or additional PR required.

[Layer 1] Use built-in 64-bit CAS and existing signal semantics. Microsoft documents atomic compare-and-store for Int64: [Interlocked](https://learn.microsoft.com/dotnet/api/system.threading.interlocked?view=net-10.0). Aside executable unavailable; primary documentation checked using web fallback. No need for a queue, audio-thread lock or heap object per sample.

Falsification box: if a successful stop is already sent before history removal in the existing timeout/enumeration paths, P1 is false. Source trace and fresh Windows-host fake-HID probe show no such stop. Implementation acceptance reruns these observable report cases expecting a real zero report. Stereo hypothesis is false if both channels share one atomic read/write; current call sites use separate objects and the probe reproduces separate directional ticks from one balanced sample.

## Architecture review

1. **[P1] (confidence: 9/10) `Core/LightingSession.cs:167`**: stop intent shares the lifetime of the failed handle. Remedy R1 separates output ownership/uncertainty from handle lifetime. Accepted by authorized complete-choice auto-selection.
2. **[P2] (confidence: 9/10) `Windows/NativeHapticOutput.cs:400`**: independent stereo state permits split consumption. Remedy R2 publishes and takes one stereo pair in an epoch-owned atomic state. Accepted by authorized complete-choice auto-selection.

Keep confirmed current output and pending cleanup distinct. Pending cleanup describes only fields this session might have written; it is not desired output and never replays stale motors.

```text
Discover(lighting, rumble, seconds)
  disabled/background/not due -> return
  GetPaths throws -> report; retain healthy handles, selection and ownership
  successful snapshot
    missing path -> retire handle; retain owned/uncertain cleanup intent
    new path -> Open individually
      null/throw -> report failure; continue other paths
      success -> fresh sequence; mark fresh Bluetooth motor synchronization pending without sending; publish availability/generation

Tick(settings, time, token)
  caller canceled -> throw; final release belongs to worker
  disabled/background -> Release
  discover -> render -> each live handle
    focus lost -> Release
    known owned cleanup OR fresh motor sync plus actual rumble request -> forced field-scoped zero report
      success -> clear only acknowledged pending fields
      failure/cancel -> retain pending fields; retire failed handle if appropriate
    normal desired report
      unchanged -> skip (only after pending cleanup handled)
      before any new ownership -> reserve pending-intent capacity or refuse/report output
      before await -> remember possibly claimed nonzero light/motor fields
      success -> update current state; clear acknowledged zero-field ownership
      caller cancel -> retain uncertainty; rethrow
      local timeout/IO error -> retire handle; preserve stop intent; report

Release(allowForeground, token)
  each owned/pending path, honoring existing focus guard
    existing handle or one bounded reopen attempt
    stop only owned fields, using fresh handle sequence if reopened
      success -> remove acknowledged intent
      timeout/error/cancel -> retain intent regardless of path absence; future matching reappearance clears it
  finally -> close every live handle independently; publish cleared availability
  no polling, no timer, no retained native handle
```

## Proposed recovery implementation

R1 keeps a finite pending-clear map keyed with the existing case-insensitive path comparer. Each entry holds only lighting-owned and motor-owned flags. Its capacity is a named constant of 32 paths (proposed resource policy, not a measured hardware limit); admission is checked before any write that could acquire new output ownership. Normal live output state remains separate and connection-local, including sequence, last refresh time and a fresh-Bluetooth-sync flag. This avoids accidentally restoring an old sequence or strength after reconnect.

An active ownership reservation is not itself a pending immediate stop: while its healthy current handle is producing desired output, retain the reservation without sending cleanup every tick. Forced cleanup applies to uncertain/retired ownership, requested zero/disable/release, or fresh request-authorized synchronization. Keep this distinction explicit using the current live output state and a small pending-clear marker; a successful stop may free the reservation before a later new nonzero write reserves it again.

Before any possibly nonzero write begins, reserve the path entry and record claimed fields. A throwing write may have reached the controller. A successful zero report acknowledges clearing only its included fields; unrelated desired fields are unchanged. Cleanup intent survives local deadline, I/O exception, parent cancellation, path absence, repeated failed cleanup and handle disposal until an acknowledged clear. Never prune owned intent on a failed Open/write, elapsed time or discovery removal. USB native haptics remain untouched.

Opening a Bluetooth handle only marks fresh synchronization pending; it grants no motor ownership and sends no motor report. Plugin discovery also runs for lighting and an open diagnostic/settings window, so an unconditional zero would disturb the game or Steam. Fulfill fresh synchronization only when `SetVibration(0, motors)` actually requests plugin rumble, including an explicit zero, or known owned/uncertain cleanup requires it. Then send a valid motor-zero before new nonzero output, including never-seen paths: the same physical controller may return under a new OS path. A lighting-only Tick and diagnostic-only discovery leave motor enable flags untouched. Do not clear unowned lighting. A failed authorized synchronization remains pending while that path remains discoverable and retires/retries like an uncertain write.

Discovery must obtain the path snapshot before mutating live devices. Enumeration exceptions leave healthy handles and Bluetooth selection intact, retrying at the existing cadence. Open failures are isolated per path so an inaccessible new controller does not disconnect another one. Confirmed removals still retire availability/generation. Failure retirement resets desired Bluetooth motors through existing PublishDevices logic, preventing replay; reconnect clears pending fields before accepting new output.

Release makes at most one bounded stop attempt per known owned/pending path in that call, reopening once if its old handle is already retired. It never turns the unfulfilled fresh-sync flag of an unowned lighting-only handle into a motor stop. If recovery cannot open/write an absent path, retain its metadata without retaining the handle or starting a retry loop. Later matching-path reappearance must fulfill known cleanup even when haptics are disabled and only lighting discovers the device. Later new-path rumble requests synchronize fresh handles independently, but cannot acknowledge the old path's ownership without proven identity. Do not open or write in synchronous Dispose; it remains handle cleanup only. Dispose/Remove must finish bookkeeping and attempt all closes even if one device Dispose throws. A cleanup callback exception must not skip remaining handle closure; preserve/report the first failure after cleanup where compatible with the existing API. No promise can be made that disconnected hardware receives a stop, or that intent survives final object/process destruction.

Bound storage by fail-closed ownership admission, never eviction: at 32 unresolved/active owned paths, refuse new ownership on a 33rd distinct path before its first potentially nonzero write, report through the existing deduplicated output error channel, and allow existing-path updates plus all zero/cleanup operations. No reservation is needed for an unowned zero-only synchronization because it cannot start output. Do not delete an unrelated pending entry to make space. A successful scoped clear removes its flags; remove the path only when neither field remains owned, releasing capacity automatically. If a report contains a newly owned light field and an already owned motor field when admission fails, omit/reject only unauthorized ownership consistently before creating/sending that report; prefer rejecting that desired report as a whole while preserving cleanup priority. Capacity exhaustion can pause new controller output after extreme unresolved path churn, with a visible error; it cannot lose an existing safety obligation. No disk journal or identity API is introduced.

History of correction: parent QA rejected one-attempt metadata retirement because accepted nonzero -> timeout -> failed cleanup -> haptics disabled -> same path lighting-only return would otherwise lose its motor stop. That incomplete proposal is superseded by retained intent plus admission capacity. A physically changed path cannot be proven to match old lighting ownership; its fresh actual rumble request can safely synchronize motors, while the old record remains reserved until its own acknowledged clear or a future independently proven reset. This plan introduces no speculative reset proof.

### Ownership and reconnect matrix

| Entry condition | Lighting/diagnostic only, no rumble request | Explicit zero rumble request | New nonzero rumble request | Release |
|---|---|---|---|---|
| Brand-new BT handle, no remembered ownership | No motor flags/write; fresh sync stays pending | Send real zero, clear fresh-sync flag | Zero synchronization then new desired command | No motor stop unless actually claimed |
| Same path after uncertain motor write | Known pending owned clear sends zero even without new request | Send real zero despite unchanged-zero optimization | Clear uncertainty before new command | One bounded stop attempt, reopen once if needed |
| Same owned path returns after repeated failures and haptics disabled | Mandatory remembered motor clear, even if lighting-only; retain until success | Real zero acknowledges cleanup | Clear uncertainty first, then new desired command | Retry once this call; retain on failure |
| New path for same physical device | No motor takeover; identity cannot be proven | Fresh handle sends real zero without path match | Fresh zero before new desired command | Stop only if actually claimed |
| Enumeration throws while handle healthy | Keep live handle/history; lighting may continue | Existing owned state determines stop | Existing selected handle remains usable | Clear owned output on retained handle |
| USB handle | Native motor flags untouched | No Bluetooth motor synchronization | No Bluetooth motor synchronization | Clear owned lights only |
| Capacity full, distinct unowned path | Refuse/report new owned lighting; never evict | Zero-only synchronization allowed | Refuse/report before any nonzero write | Existing obligations still clear and free capacity |

The first synchronization and subsequent desired write share the existing worker; a focus/cancellation check applies before each write. Tests assert the ordering, field masks and stop bytes rather than merely report count.

### Parent-approved release and settings addendum

Release launches bounded independent per-device writes before awaiting all, then acknowledges the ownership map sequentially on the logical worker. Async write continuations never mutate dictionaries. Each write retains its existing 500 ms deadline; no extra timer/thread/polling is added. A synchronous write/Open failure, throwing diagnostic observer or failing Dispose cannot prevent later device attempts/closure. Caller cancellation is returned only after started tasks finish and bookkeeping/closure is complete; uncertain intent remains. Synchronous platform Open latency cannot be bounded by a CancellationToken and is not misrepresented as covered.

Measured isolated fixture: 32 owned paths, 31 stalled clear completions and one healthy device; all 32 stop submissions occurred before the first cancellation deadline, every handle closed, only the healthy stop was acknowledged, and a failed path was subsequently cleared on reappearance. Observed release elapsed 515.6 ms and 513.5 ms on two runs; these are measurements, not timing thresholds or hardware claims.

Native Configure now computes `screenChanged` once from `ScreenOutputKey`, reusing it for the effective reset and independent screen snapshot. `ShapeKey` lists scalar/spatial response fields once; the five gated feature booleans are compared as one effective tuple. All are value tuples, so unchanged Configure calls allocate no record/key object. Effective immutable records are still created only on actual differences. Public init-property mutation tests automatically cover newly added HapticSettings fields; current snapshot must change for every field, while only ScreenEffects/ScreenStrength/OutputMode/RumbleController and the two backend selections reset the screen snapshot. This protects screen independence without blind full-record equality or duplicated six-field comparisons.

Final implementation adds three focused types: ownership metadata, stereo signal, and its epoch accumulator. Parent accepted this small six-file arrangement and the release/settings amendments; no additional service boundary or dependency was introduced.

## Code quality review

3. **[P2] (confidence: 9/10) `Core/LightingSession.cs:179`**: discovery catches a snapshot failure by calling all-device disposal. R1 isolates enumeration and Open errors, and makes per-device cleanup independent. This is required for the same recovery contract, not a separate feature.
4. **[P2] (confidence: 8/10) `Core/LightingSession.cs:279`**: one throwing device Dispose prevents later closes and map clearing. Fault-injection coverage is required; physical HidDevice.Dispose normally succeeds, but the interface permits failure. R1 addresses it without a generic lifecycle framework.

Use small helpers only where they centralize the same field-ownership rule: mark possible output, acknowledge clears, retire one handle, attempt scoped clear. Do not combine report construction, device selection and error presentation in a new service. Keep state transition comments adjacent to those helpers.

R2 introduces `Core/StereoRumbleSignal.cs` (final class name `StereoRumbleSignal`) with `Active`, `SetActive`, capture of an epoch token, `Observe(left,right,epoch)` and `TakePeak() -> (float Left,float Right)`. One epoch accumulator stores two exact float32 bit patterns in an Int64 CAS word. Reset/active transition swaps the accumulator reference, allocating only on a control transition, never per audio block. Prepared frames retain the captured epoch reference, like existing impactEpoch; late callbacks can mutate only their retired accumulator, which the consumer never reads. Componentwise maxima retain existing tick aggregation; reject an invalid pair as silence. Producer attempts remain bounded like SfxLoudness; a failed bounded publication drops a whole pair. Take snapshots the current epoch and atomically clears its pair, then verifies epoch identity before returning; a concurrent fault/reset causes silence, not stale output. No pooled/reused epoch objects, avoiding ABA identity reuse.

Comparison prompted by parent review: packing a 32-bit generation with two ushort levels is allocation-free but adds quantization and would change exact motor-boundary expectations. Epoch-owned full-float packing preserves exact existing arithmetic and the same zero-allocation audio path with one tiny object only per activation/reset transition. It also reuses the existing impactEpoch identity pattern. Therefore choose the full-float epoch accumulator; no tie remains. A lock blocks the audio thread; immutable pair records allocate per block; a queue changes aggregation and complexity. Do not extract a generic packed-signal framework: scalar and stereo encode different contracts and sharing scaffolding increases coupling. Caveat: Configure may run from the audio fault catch (`NativeHapticOutput.cs:441`); an exceptional fault reset can allocate an epoch, while the normal publication path must allocate zero.

Native integration replaces RumbleLeft/RumbleRight with `RumbleStereo`. Frame stores `StereoEpoch` instead of independent side generations. Configure and ResetRumbleSignals deactivate/reset the pair once, then reactivate when Bluetooth is enabled. Mix computes sides once and observes once. Parent-owned Plugin change: replace `(output.RumbleLeft.TakePeak(), output.RumbleRight.TakePeak())` with `output.RumbleStereo.TakePeak()`. Scalar RumbleSignal remains unchanged for XInput and existing delayed-side handling.

```text
Prepare -> capture settings/impact epoch + scalar generation + stereo epoch
Mix guard rejects stale settings/epoch/route
  invalid PCM -> no publication
  scalar -> existing SfxLoudness
  Bluetooth -> MeasureStereo -> one pair CAS (max left, max right)
    inactive / contention bound reached -> drop whole pair
worker -> one TakePeak CAS -> (left,right), same interval
reset/backend/focus/reconnect -> replace current epoch with empty accumulator
old frame/publisher -> retired accumulator only -> never consumed
reset racing Take -> final epoch mismatch -> return silence
```

## Decision ledger

All decisions below use the user's explicit complete-option auto-selection instruction, relayed in the parent task. Parent accepted the amended plan and implementation; independent verification remains pending.

| ID | Current | Chosen remedy | Rejected alternative | Approval/disposition |
|---|---|---|---|---|
| R1 / D1 | Failure deletes output history | Retain owned stop intent until acknowledged clear; capacity 32 with fail-closed admission before new ownership; repeated failures never evict | One-attempt retirement loses stop on disabled lighting-only return; unlimited history lacks a memory bound | Parent QA explicitly requires original safety guarantee; complete finite-admission remedy accepted under auto-choice instruction |
| R2 / D2 | Two independent peak words | One exact-float pair CAS word per control epoch and one consumer read | Consumer-only tuple wrapper races; ushort packing quantizes; per-block records allocate; lock blocks audio | Authorized automatic complete choice; accepted after parent proposed a strictly better precision-preserving epoch option; no tie or fabricated score |
| R3 / D3 | Existing regression contracts | Preserve ownership masks, CRC, sequences, USB protection, focus gating, rumble timing/calibration and stale-frame rejection; add failure assertions below | New smoke tests alone fail to protect the existing behavior | Necessary proof of accepted R1/R2; accepted |
| R4 / D4 | New handle assumes motors zero | Mark fresh synchronization pending; authorize zero only on actual rumble request (explicit zero included) or retained owned cleanup; same/new paths with finite reserved intent | Unconditional opening-time zero disturbs unowned game/Steam rumble; forgotten same-path intent misses disabled cleanup | Parent review reopened ownership policy; amended per explicit steering; preserve USB/native and all unowned fields |
| Structure / D5 | Six files; ownership metadata, signal and epoch holder | Confirm focused arrangement with all contracts intact | Smaller arrangement has no proven complete alternative; an investigation-only pause leaves arrangement unresolved | Authorized complete-choice automatic selection and parent acceptance; scope accepted as-is |

Approval readiness: PASS for R1, R2, R3, R4 and structure D5 under the authorized auto-decision instruction and parent review steering. No destructive action, scope expansion or unresolved tie. Original proposal is the recovery/stereo scope in the parent task; runtime evidence is separate in the premise table.

## Test review and coverage diagram

5. **[P1] (confidence: 9/10) `tests/BluetoothChecks.cs:382`**: existing failure tests do not force actual local-deadline cancellation after accepted output, nor enumeration failure after nonzero output. Add these regressions to existing fixtures.
6. **[P2] (confidence: 9/10) `tests/BluetoothStereoChecks.cs:139`**: delayed scalar tests do not protect atomic stereo publication/consumption. Add signal-boundary interleaving and NativeHapticOutput integration regressions.

```text
CODE PATH / USER FLOW                         CURRENT -> PLANNED
start/stop report, CRC, sequence wrap          existing behavioral tests -> preserve
USB + second controller ownership isolation  existing behavioral tests -> preserve
focus loss / return / full disable            existing behavioral tests -> preserve
parent canceled after accepted start/stop     existing behavioral tests -> preserve
local timeout after accepted start or stop    GAP -> forced stop after reopen + release
enumeration error after accepted start        GAP -> healthy handle retained; release stops
Open failure alongside healthy controller    GAP -> healthy device stays usable
missing path / return / zero request          reconnect smoke -> real stop intent assertion
new or changed Bluetooth path                GAP -> request-authorized zero sync; lighting-only untouched
release failure then later reconnect          GAP -> pending stop survives close
Dispose throws before another device closes   GAP -> every close attempted and counts reset
centered publication between worker ticks     GAP -> pair entirely in one consumption interval
inactive / reset / stale prepared frame       existing tests -> migrate to pair + stale CAS test
direction / calibration / delayed scalar      existing behavioral tests -> preserve exact values
invalid samples / endpoint / max merge        scalar coverage only -> pair boundary assertions
```

The diagram lists concrete behavior groups, not code coverage percentages. No measured coverage claim. Existing paths are backed by tests read statically; this run did not execute them.

### Test Plan

Affected routes: no web pages; controller worker, Bluetooth transport and native audio callback.
Key interactions: enable rumble, lose focus, disconnect/reconnect, change backend/settings, disable plugin while a write is uncertain.

- **Critical path: accepted nonzero -> enumeration throws -> release.** Retain handle, unchanged generation, and issue valid zero motor bytes with owned light fields only.
  Value: protects=stop after discovery error; fails_when=scan error disposes healthy history; why_new=existing failure fixture only throws writes; seam=none; no_claim=physical delivery (fake HID).
- **Critical path: accepted start or stop -> local deadline -> release/reopen -> zero request.** Test both direct release retry and later discovery, with one accepted nonzero report before cancellation; final successful report clears owned fields.
  Value: protects=uncertain write recovery; fails_when=timeout erases pending ownership; why_new=existing cancellation is parent-token only; seam=none; no_claim=driver cancellation timing (fake HID).
- **Critical path: audio balanced pair -> consume -> next consume.** Exactly one nonzero interval contains both equal channels, allowing both legal whole-pair orderings; no right-only/leftover-left sequence.
  Value: protects=coherent centered output; fails_when=separate channel atomics return mixed intervals; why_new=delayed scalar fixture cannot detect this; seam=none; no_claim=audible/tactile quality.
- **Edge: repeated failed cleanup, then disable rumble and return same path during lighting-only discovery.** Retain known ownership across failures and absence; send real zero without requiring any new rumble request, with fresh sequence/CRC; failure still retains intent.
  Value: protects=disconnect cleanup; fails_when=retirement loses ownership or replays command; why_new=reconnect currently checks no replay only; seam=none; no_claim=stable OS identity across path changes.
- **Edge: fill all 32 reservations with accepted nonzero output whose clear fails, then attempt a 33rd path.** No nonzero or newly owned light report reaches that path; existing pending records survive; a successful clear frees capacity and the formerly refused path can start. Zero-only operations remain allowed.
  Value: protects=bounded safety state; fails_when=admission evicts intent or accepts untracked output; why_new=new finite-capacity contract; seam=none; no_claim=physical delivery (fake HID).
- **Edge: one Open or Dispose throws in a multi-device set.** Healthy devices remain available; every close attempted and availability cleared; no output leaks to a second controller.
  Value: protects=fault isolation; fails_when=one device aborts all discovery/cleanup; why_new=no throwing Open/Dispose fixture; seam=none; no_claim=OS resource reclamation (fake handles).
- **Edge: same fake physical receiver returns with a changed path.** On actual zero/nonzero rumble request, newly opened handle issues zero motor bytes before any new effect without matching path metadata. Lighting-only Tick/discovery/release never sets motor flags; no unowned light bits are set.
  Value: protects=new-path motor safety; fails_when=new handles assume physical silence; why_new=existing reconnect asserts no report; seam=none; no_claim=OS physical-device identity mapping.
- **Edge: stereo reset between capture and publish; settings/backend/focus/reconnect reset.** Stale publication rejected, silence remains silent; new generation accepts new pair.
  Value: protects=generation integrity; fails_when=old callback republishes after reset; why_new=prepared-frame tests omit signal CAS boundary; seam=none; no_claim=arbitrary native hook teardown.
- **Edge: finite bounds, invalid pair, multiple side maxima, zero/endpoints and exact floats.** Reject NaN/infinity, preserve exact original normalized float levels, retain componentwise maxima.
  Value: protects=packed signal numeric contract; fails_when=packing swaps sides or amplifies invalid input; why_new=new encoding has no existing coverage; seam=none; no_claim=physical motor calibration.

Tests to retire: none. Extend current fixtures; do not add production-only testing seams. Regression assertions include all existing Bluetooth report flags, ownership, calibration and PCM-preservation checks. No prompt/LLM changes or evals apply. Windows hardware reconnect/focus smoke is [integration required before claiming hardware verification], distinct from fake-device regression acceptance.

## Performance review

No additional performance issue found outside the accepted fixes. Keep discovery at one second and writes at 500 ms deadline. Pending-intent storage is capped at 32 path records by admission; successful cleanup removes entries. Release writes run concurrently with per-device deadlines and sequential acknowledgment, so independent write waits overlap; synchronous platform Open latency remains outside that bound. Failed release does not run again on every disabled worker tick merely because metadata remains: retry on eligible rediscovery/known-path return, new relevant request or explicit final release.

The normal audio publication path remains allocation-free, takes one packed state and uses bounded producer retries (existing bound: four). Worker take may retry on contention as existing SfxLoudness does. Epoch objects allocate only on reset/active transitions, including an exceptional audio-thread fault reset. No lock, queue, per-block object or extra PCM scan. Planned verification measures warmed publish/take allocation delta with zero managed allocations required; throughput is recorded rather than inventing a numeric speedup. Hardware/game timing remains unknown. Exact float levels preserve existing motor arithmetic, with no new quantization tolerance.

Performance lane measured a new 72-byte allocation per synchronously completed report from the extracted `Task<byte[]>` helper. Replaced that private return type with standard `ValueTask<byte[]>`, with exactly one await at each caller and no changed state/error semantics. The concurrent release path retains its control-path task aggregation. Post-fix behavioral suite passes; paired allocation remeasurement is owned by the performance lane and must establish the allocation result independently.

## Failure modes

| Path | Failure | Handling / planned test | User effect |
|---|---|---|---|
| scan | enumeration unavailable | retain devices; report existing deduplicated error; enumeration regression | existing output remains releasable |
| write | accepted but canceled | preserve scoped uncertainty; bounded stop/reopen tests | temporary failed device, retry at existing scan cadence |
| release | disconnected or stop also fails | close handles; retain intent; matching-path return clears even with rumble disabled | stop cannot be delivered while physically unreachable |
| admission | all safety records occupied | refuse/report new ownership before nonzero write; capacity regression | new controller output pauses until a prior clear succeeds |
| reopen | inaccessible new path | isolate failure, continue healthy paths; Open fixture | other controllers keep working |
| teardown | one close throws | continue closure/bookkeeping; Dispose fixture | error reported; no skipped healthy cleanup |
| stereo | reset races observation | atomic generation rejects old publication; reset test | silence rather than stale pulse |
| stereo | competing producer exhausts bound | drop whole pair; stress invariant | possible dropped sample, never split channels |

Critical gaps in the proposed plan: zero (each new failure has handling and planned observable proof). This is not a claim that proposed tests already pass. A physically disconnected controller, process termination and watchdog duration remain explicit limits. Changed OS paths receive motor synchronization on actual plugin rumble request even without identity matching. Old absent-path intent never retires merely from failure or absence; lack of identity proof can permanently consume one finite reservation. This can pause new output at capacity, preserving safety and bounded memory rather than silently discarding owned cleanup.

## Implementation Tasks

Estimates assume reuse of existing fixtures and working Windows .NET host; they are planning estimates, not measured completion times.

- [x] **T1 (P1, human: ~4h / CC: ~30min)**: device ownership recovery. Independent intent, scan fault isolation, fresh-sequence cleanup, capacity admission and exception-safe handle retirement implemented; focused failure/ownership/release regressions pass.
- [x] **T2 (P2, human: ~3h / CC: ~25min)**: coherent stereo. Exact-float packed epoch signal and NativeHapticOutput integration implemented; atomic-pair, stale-epoch, numeric, zero-allocation and exhaustive settings-key regressions pass. Parent owns Plugin integration.
- [ ] **T3 (P1, human: ~1h / CC: ~15min)**: integrated acceptance. Run the custom Release regression executable in isolated output paths on the Windows host, rerun the historical adversarial scenarios against changed source, inspect whitespace/diff, and record actual outcomes. Do not publish dev DLLs while lanes edit. Hardware smoke only if an actual controller/game session is available; otherwise explicitly report unverified.

## Worktree parallelization strategy

| Step | Modules touched | Depends on |
|---|---|---|
| T1 | Core device transport; Bluetooth checks | parent approves this plan |
| T2 | Core stereo signal; Windows audio output; stereo checks | parent approves this plan; Plugin consumption coordinated |
| T3 | integrated tests and artifacts | T1 and T2 plus parent integration |

This lane implements sequentially because both changes touch Core and the shared executable build. Other parent lanes can work on disjoint modules. Shared Plugin.cs integration and any final build/publication are sequenced by parent; no independent build may overwrite common release artifacts.

## NOT in scope

- Plugin UI/settings/recording extraction, autosave recovery, actor offsets and test-runner hardening belong to other assigned lanes; this plan does not silently omit them from the parent objective.
- Persistent recovery journal and watchdog measurement are not needed to correct lost stop commands; no survival guarantee across process termination is made.
- No new release, commit, push, deployment, dependency or standalone abstraction cleanup. Zero TODO proposals outside the accepted lane.

## Review workflow receipts and limitations

Skill read in bounded chunks through line 2249; preamble executed with session `1-1793871489-548515b2`, `SESSION_KIND: interactive`, telemetry off. Update checker failed and lease acquisition warned; no upgrade was attempted. Context recovery and history/decision/readiness helpers ran; no prior review was returned. `bun` was absent from PATH; existing `/home/jackoltrade/.bun/bin/bun` enabled the helper reads. Design/CEO doc finders returned no path. No durable product/recent-decision digest was available; attempted brain-cache reads failed with EROFS under `/home/jackoltrade/.gstack`.

Outside voice: enabled configuration is unset; Claude resolver returned null. Native Plan agent type, TaskStop and background timer tools required by the skill fallback are absent. Outside voice unavailable; no agent dispatched and no independent-review credit. Repair: make Claude Code available, or run this plan through the parent review. No invented cross-model agreement.

Parent persisted the required engineering and outside-unavailable logs at `2026-11-05T09:57:56.237873+00:00` with this exact plan_path. This lane ran `gstack-review-read` and verified both records: engineering `issues_open`, six mapped issues, zero unresolved/critical gaps; outside `unavailable`, source none, provider claude-code. No CLEAR claim. `jq` is absent, so task JSONL is skipped under the skill's documented fallback; Markdown tasks remain above. Auxiliary QA artifact was saved/read back with parent-authorized escalation: `/home/jackoltrade/.gstack/projects/Jackoltrade-PS5-DualSense-Controller-for-FFXIV-/jackoltrade-main-eng-review-test-plan-20261105-181403.md`.

REALITY: premise-table pass Evidence table; unverified=2 docs/optimization/device-reliability-plan.md:15
REALITY: already-done finding Local history checked; remote freshness unavailable docs/optimization/device-reliability-plan.md:30
REALITY: surface-check pass Existing probe CLI attempted and failure recorded docs/optimization/device-reliability-plan.md:28
REALITY: binding-decisions pass User choices preserved docs/optimization/device-reliability-plan.md:6
REALITY: numbers pass File/type counts and time estimates labeled docs/optimization/device-reliability-plan.md:41
REALITY: deferred-asks pass No linked issue; cross-lane requests named docs/optimization/device-reliability-plan.md:205
REALITY: pr-count pass One parent branch wave; no additional PR docs/optimization/device-reliability-plan.md:41
REALITY: falsification-box pass Disconfirming evidence and bounded follow-up specified docs/optimization/device-reliability-plan.md:45
REALITY: stop-reasons pass Each new cleanup/reset failure has planned regression docs/optimization/device-reliability-plan.md:179
REALITY: reporter-environment pass Fresh Windows .NET probe reproduces baseline; physical hardware unverified docs/optimization/device-reliability-plan.md:28

## Completion summary

- Scope accepted as-is; architecture: two issues, code quality: two, test gaps: two, performance: no new issue.
- Coverage diagram, QA value cards, implementation tasks, dependency ordering and failure matrix written. Post-ValueTask isolated Release suite: 52/52 executed groups passed, one optional group skipped, zero failed, 515,767 assertions (other lanes' current tests included); full receipt `work/device-reliability/results.txt`. Device group 73,908 assertions, stereo group 81. Latest 32-path release fixture measured 513.4 ms. Balanced-pair stress reports one aggregate invariant after 10,000 intervals rather than inflating the assertion count per interval. No hardware/watchdog or final production DLL claim.
- Ten applicable reality rows emitted; two unverified hardware/timing premises. Helper result: `PLAN_REALITY: phase=eng rows=10/10 missing=none unverified=2 verdict=complete`.
- No unresolved decision, no highest-completeness tie; zero proposed TODO additions. Lake score not assigned: remedy choices were authorized complete-contract selections, not numerical option comparisons.
- Outside coverage unavailable; required review-state records verified as persisted by parent. Engineering quality 9+ is an implementation and validation goal, not an earned numerical score.
- Operational learning: helper scripts need the existing Bun binary on PATH; Windows executable launch needs the approved escalated host convention to avoid sandbox vsock startup failure. No learning-state write is permitted.

## GSTACK REVIEW REPORT

| Review | Trigger | Why | Runs | Status | Findings |
|---|---|---|---|---|---|
| CEO Review | /plan-ceo-review | Scope and strategy | 0 | Not run | Routine reliability scope; no product direction change |
| Outside Review | Claude Code / plan review | Independent second opinion | 1 | Unavailable; persisted | CLI resolver null; required native fallback tools absent |
| Eng Review | /plan-eng-review | Architecture and tests | 1 | ISSUES OPEN; persisted by parent | 6 mapped issues, 0 critical plan gaps, 0 unresolved choices; implementation verified in isolated suite |
| Design Review | /plan-design-review | UI/UX gaps | 0 | Not applicable | No UI scope |
| DX Review | /plan-devex-review | Developer experience | 0 | Not applicable | No new developer-facing flow |

**OUTSIDE COVERAGE:** Claude Code, plan-review, unavailable; no completed external review. Actual unavailable record read back from skill state.

**VERDICT:** Parent accepted plan and addenda; isolated implementation checks pass. Historical engineering review status remains ISSUES OPEN because mapped findings count resolved work too; no historical CLEAR claim, eng review required for final integrated changes.

NO UNRESOLVED DECISIONS
