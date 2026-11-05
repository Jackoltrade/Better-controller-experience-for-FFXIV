# Boss warning and train-vibration review

Reviewed target: this plan and its approved SFX classification change. Baseline: `166ff17`. The user requested engineering review, skipped office-hours, and authorized choosing the most complete option unless the highest choices genuinely tie. No such tie arose.

## Evidence and scope challenge

The preserved line-stack trace, `work/boss-false-positive-recordings/sfx-trace-20261105T123142.6438232Z.json`, has SHA-256 `b6b622f6d9a9a14ed883f5226e35ac5e455a65038b2c53839bc1c01cdf02be1f`. It contains the shared warning bank `sound/vfx/monster4/SE_Vfx_Monster_ETC_LockOn_Share_5sec_lock.scd` classified as Boss/BossEncounter in **120 sound snapshots from 4.52 to 11.30 seconds**. The actual `SE_Vfx_Monster_Tsukuyomi_GunAtk_c.scd` released-effect impact appears from **10.27 to 12.42 seconds**. These paths already have opposite expected results in the stock warning matcher tests.

The preserved train trace, `sfx-trace-20261105T123532.1660653Z.json`, has SHA-256 `3ee36e5dc10051cc2e39353942183208daedad3ad6160aada85c89348ac83dd4`. Every sampled SFX voice has Enemy=None/HapticRoute=None; ArenaSources is empty, effective enemy settings are off, and encounter PlayerInCombat/BossInCombat/BossSfx are false. Requested enemy settings remain on. This rules out sampled added boss-SFX output in that capture, not the user's reported vibration.

The user subsequently confirmed that the train vibration stops when **Screen effect based** is disabled while **Boss skills** remains enabled. The parent/camera review also reports 1,085/1,085 camera samples with DualSense mixed shake, positive manager shake around 0.160–0.165, and zero native input in 843 samples. Together these identify the **screen-effects path**, rather than boss-SFX classification, as the source of the train vibration. Camera values lack reliable provenance distinguishing environmental motion from combat motion. A selective environmental-camera filter is therefore not justified by the available data. No boss-SFX or generic loop filter should be added for the train.

Scope accepted as-is: one existing classification conditional, an extension of the existing warning test group, and this document. Four files after the parent-approved existing NPC fixture correction, zero new runtime classes/services. The complexity selector is unnecessary. Sequential implementation, no parallelization opportunity. Parent owns integration and camera policy; this lane does not edit Plugin, UI, runner registration or packaging.

## Binding decisions

- **D1, approved by parent before implementation:** apply the existing StockTargetingCue veto to every otherwise-admitted non-player enemy, including BossEncounter. Preserve actual impacts, untagged real encounter effects, player ownership, optional-name tri-state behavior, aliases, cache identity and generation checks.
- **D2, approved:** do not implement a train filter without source attribution. Later user evidence identifies the screen path; the SFX-filter decision remains unchanged.
- **D3, approved:** obtain a private red control by restoring only the old bypass. Do not change frozen runtime/tests or shared build output during this verification.

- **D4, approved after combined integration exposed the old contract:** change the existing BossNpcSoundChecks fixture to a known non-warning resource and require 12 cached classifications across three origins and four tags. Preserve all broad admission, position, provenance and allocation assertions.

Approval readiness: PASS for D1–D4. These are exact task approvals, not inferred permission from reviewer agreement. No new remedy was selected during this retrospective review.

## What already exists

This is Layer 1 reuse. `StockTargetingCue.Matches` recognizes the narrow shared LockOn filename family. `EnemySoundCueFilter.Read` already copies a bounded native filename safely. `PlayerSoundOwnership.IsTargetingCue` caches its tri-state result per reservation, revalidates native identity and retries unknown reads. No new reader, resource taxonomy or profiling framework is needed.

## Architecture review

[P2] (confidence: 10/10) `GameAudioLayout.PlayerSoundOwnership.Inspect` previously grouped `IsTargetingCue(...)` inside `!encounterNpc && (...)`. The broad encounter role therefore bypassed an independently known warning-resource veto. The live trace and private old-condition control establish the defect. Disposition: D1 accepted and implemented.

The encounter rule still admits otherwise untagged real boss sounds. Only the known shared warning predicate moves outside that exemption. Native game audio and original controller output are untouched.

```text
Mixer voice -> Inspect reservation and owner
  player owned ------------------------------> existing player channel
  non-player
    unknown kind / ineligible ordinary action -> no added enemy haptics
    otherwise eligible
      cached stock warning = true ------------> no added enemy haptics
      false / unreadable optional name --------> existing enemy attribution
        identity/generation changed -----------> discard attribution, retry later
```

Failure mode: overly broad matching could hide real impacts. The existing matcher rejects the recorded GunAtk path and other genuine VFX banks. Missing/unreadable names keep their existing admission-and-retry semantics; this change does not silently introduce fail-closed resource loading.

## Code quality review

No additional issues found. A misplaced boolean exemption caused the bug; a new abstraction would obscure it. Two conditional lines are rearranged without adding a runtime helper, cache or new state. Existing player and ordinary-enemy call contracts remain intact. No production seam was introduced for testing. The earlier uncommitted allocation-harness edits in the same test file were preserved.

## Test review

The existing console C# check executable is the test framework. Six assertions extend `EnemyTargetingCueChecks`; no new runner registration is required.

```text
Code path                                    User behavior / evidence
BossEncounter + unknown warning [tested]      Marker appears: no added warning vibration
BossEncounter + released warning [tested]     Skill tag cannot re-admit the warning
Recorded GunAtk effect [tested]               Actual line-stack impact remains admitted
Untagged real encounter effect [tested]       Broad boss admission remains available
Positional playback alias [tested]            Same warning veto applies through aliases
Player-owned playback [tested]                Existing player channel remains admitted
Unknown/read-fault/cache/generation [existing tests]
Live corrected marker behavior [pending live check]
Train screen-motion distinction [provenance unavailable; no SFX implementation]
```

Value: protects=stock warnings excluded without losing real impacts; fails_when=encounter bypass returns; why_new=old tests used ordinary boss roles; seam=none; no_claim=physical live vibration acceptance.

Value: protects=player ownership and reservation aliases; fails_when=veto leaks into player route or alias bypass; why_new=new encounter branch; seam=none; no_claim=native resource-loader acceptance.

Existing deferred-name, reader-failure, concurrent-read, reservation-generation, native filename and strict allocation checks remain enabled. The previous BossNpcSoundChecks contract deliberately returned a known warning and expected zero filename reads. The parent combined gate caught this obsolete assumption; the corrected fixture uses a known non-warning and verifies 12 cached reads without reducing broad NPC admission. No tests are retired. The missing live check is visible, not replaced by assertion count.

## Performance review

No separate performance defect identified. The previously exempt encounter branch now performs the existing cached predicate lookup. A stable reservation needs one bounded native filename classification; unknown reads retain the prior retry behavior. The reader and cached callback path have exact-zero allocation checks. No speedup, FPS or physical-controller claim follows from this correction.

## Verification and falsification

The private optimized Windows .NET 10.0.14 project passed: EnemyTargetingCueChecks **74**, EnemyEncounterChecks **267**, BossEncounterStateChecks **150**, EnemyHapticChecks **370**, EnemyEffectChecks **31**; **892 assertions total** in the initial focused run. The expanded run also includes BossNpcSoundChecks **83**, for **975 assertions total**, with zero build warnings/errors. Raw receipts and the derived trace summary are in `work/independent-review/boss-warning/`.

The private red control restores **only** the old bypass condition in a copied GameAudioLayout. The same six new assertions fail and the process returns the expected exit code 1. Marker-admission assertions directly detect the original defect. Some positive-control assertions also check filename-reader counts; their red failures establish the skipped classification/cache path, not that the old implementation suppressed impacts or player sounds. Current-source green and old-condition red together demonstrate regression sensitivity. Raw red build/results are retained separately.

The train hypothesis was falsified as an SFX explanation by the recorded route values and subsequent user toggle experiment. The next action belongs to screen-effect policy/provenance, not a boss exception. The bounded SFX analysis and tests are complete; no longer-duration or generic-loop kill is proposed.

## NOT in scope

- A train-specific or boss-specific resource exception: the identified train source is screen motion.
- Selective environmental-camera suppression: camera-source provenance is unavailable.
- Broad looping-sound suppression: duration/looping is not a safe distinction between ambient and real boss effects.
- Plugin/UI/packaging changes, shared SDK builds or deployment: parent-owned integration scope.

## Reality checks

REALITY: premise-table pass Warning bypass is verified; train is screen-path identified, selective camera provenance remains unavailable. docs/optimization/boss-false-positive-plan.md:7
REALITY: already-done finding The exact approved correction is already uncommitted; this review adds no duplicate runtime task. src/DualSenseLighting/Core/GameAudioLayout.cs:479
REALITY: surface-check pass Existing private launcher built and executed both current-source and old-condition fixtures. work/independent-review/boss-warning/run-red.ps1:1
REALITY: binding-decisions pass D1–D4 preserve the parent's exact scope and later user source correction. docs/optimization/boss-false-positive-plan.md:17
REALITY: numbers pass Counts and time ranges are derived from preserved JSON; test results are raw receipts. work/independent-review/boss-warning/trace-evidence.json:1
REALITY: deferred-asks pass No linked issue; screen-source selective policy stays explicit rather than being implemented as an SFX workaround. docs/optimization/boss-false-positive-plan.md:13
REALITY: pr-count pass One parent-owned follow-up commit; this lane creates no extra PR or release. docs/optimization/boss-false-positive-plan.md:15
REALITY: falsification-box pass Old bypass fails the new regression; train SFX hypothesis is rejected by user toggle and recorded routes. work/independent-review/boss-warning/red-results.txt:1
REALITY: stop-reasons pass No new runtime stop reason or hold code; existing cue veto is tested. tests/EnemyTargetingCueChecks.cs:126
REALITY: reporter-environment pass Traces are live user evidence; Windows synthetic checks do not replace a corrected in-game replay. docs/optimization/boss-false-positive-plan.md:85

## Implementation Tasks

- Completed D1: repair the existing conditional and extend the current fixture. Verification is green current-source plus red old-condition, followed by parent integration and a live marker check.
- Completed D3: retain a private negative-control build without changing frozen production/tests.
- Completed D4: update the existing real-NPC fixture contract and include its 83 assertions in the private green set.
- No new architecture, code-quality or performance implementation tasks were generated. No TODO file change is proposed; selective camera policy remains parent-owned and evidence-limited.

## Skill execution and completion limits

The full 2,249-line `gstack-plan-eng-review/SKILL.md` was read. Scope challenge, architecture, code-quality, test and performance review were performed against the actual code and traces. Office-hours was skipped under the user's explicit instruction. Startup reported an available gstack update and a workflow lease conflict; no global upgrade was performed in this frozen lane.

Initial hook attempts failed because Bun was absent from the ambient PATH. This was repaired using `/home/jackoltrade/.bun/bin`; reality rows, review-read and prior-learnings then executed. Those initial failures are not a claim that Bun is unavailable. Product brain-cache access encountered a read-only state-directory write. Existing task/source context supplied the scope; no missing digest was treated as evidence.

The fresh Claude resolver reports `CODEX_MODE: not_installed`. The host lacks the required Plan/background-timer/TaskStop fallback contract. Outside review is unavailable, never clean. Parent independently reviewed and approved the exact correction. Required review-log records and QA artifacts were persisted using the authorized state-directory write. The reality checker reports `rows=10/10`, `missing=none`, `verdict=complete`. Records use ISSUES OPEN because the review found a corrected defect, not a fabricated clean result; a native review does not supply external-provider coverage.

## GSTACK REVIEW REPORT

| Review | Trigger | Why | Runs | Status | Findings |
|---|---|---|---:|---|---|
| CEO Review | None | Routine correction | 0 | Not applicable | No product expansion |
| Outside Review | Claude Code resolver | Independent challenge | 1 | Unavailable | CLI absent; required fallback tool contract absent |
| Eng Review | `/plan-eng-review` | Architecture and tests | 1 | ISSUES OPEN (native; see execution receipts) | Confirmed warning defect corrected; red control proves regression sensitivity |
| Design Review | None | No UI scope | 0 | Not applicable | None |
| DX Review | None | No developer-facing scope | 0 | Not applicable | None |

**OUTSIDE COVERAGE:** Claude Code, plan-review phase, unavailable. No completed external review.

**VERDICT:** Approved native SFX correction with synthetic red/green proof; parent integration and live marker verification remain. Train source is screen effects; selective camera provenance is unavailable. The native review records one corrected finding. External coverage and a live corrected-marker check remain unavailable; no full external-review success is claimed.

NO UNRESOLVED DECISIONS
