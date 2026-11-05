# Orchestration reliability and responsibility boundaries

Plan-time review record. For implemented behavior, current verification and remaining
limits, see [the parent verification report](verification.md).

Selected review target and report destination: `docs/optimization/orchestration-plan.md`.
Baseline: `9354236`, branch `main`. Evidence: static source reads only unless labeled otherwise. Implementation is explicitly withheld pending parent QA. Quality target: 9+, assessed after implementation evidence, not promised by this plan.

## Original proposal

Repair configuration autosave recovery and exceptional shutdown. Move settings rendering and recording coordination into actual owners, retaining native effects, immutable snapshots, configuration identity, `/bcef`, English/Chinese/Japanese, explicit consent, Dalamud WindowSystem dialogs, and focus/replug release. Reuse existing helpers and keep operations granular.

## Scope Challenge

Scope accepted as-is. No feature additions or cuts. Estimated changed production files: Plugin.cs, Plugin.Haptics.cs, Plugin.Privacy.cs, three new owners, and at most four native lifecycle files after ownership agreement; 6 orchestration files plus separately coordinated lifecycle files. Estimated new classes: 3. Tests extend existing consent/window/trace checks and add focused scheduling/lifecycle checks. All new filenames below are proposed, not existing evidence.

Structure decision D1: select smaller arrangement, three cohesive owners: SettingsUi, DiagnosticRecordingCoordinator, ConfigurationSaveScheduler. Alternative: seven presenters/controllers/services, completeness 8/10 versus selected 10/10 because distributed mutable ownership makes consent and recorder ordering harder to verify. Both retain all approved contracts. Human estimate 1-2 days, agent estimate 2-4 hours including integration; estimates assume installed Windows SDK and reusable test harness. Cleanup stays a small local best-effort function, not a fourth service. Parent task expressly authorizes highest-completeness auto-decisions; no equal-completeness tie identified. No implementation authorization implied by this review.

Premise table:

| Premise | Evidence | Status |
|---|---|---|
| Failed autosaves stay overdue | Plugin.cs:689 SavePluginConfig precedes deadline clear at 690 | verified static |
| Plugin has concentrated UI/recording responsibilities | Plugin.cs:371-460 recording; 693-1343 UI; Plugin.Privacy.cs | verified static |
| Exceptional cleanup can stop early | Plugin.cs:1363 worker.GetResult precedes native cleanup | verified static |
| Native callbacks remain after an actual failing Dispose | no injected native teardown run | unverified, risk only |

Unverified premises: 1. No prior implementation found in current source. Git history for Plugin.cs inspected; no revert matches. No TODOS.md or repository AGENTS.md found; user supplied simplicity instruction applies. Existing C# SDK compiles source by default; no new package, publish artifact, architecture or platform is introduced. Existing Windows plugin packaging remains authoritative. One integrated change set, no extra PR requirement.

REALITY: premise-table pass source receipts recorded; unverified=1 Plugin.cs:1363
REALITY: already-done pass current implementation still contains defect Plugin.cs:687
REALITY: surface-check pass existing save, framework and WindowSystem APIs inspected Plugin.cs:147
REALITY: binding-decisions pass user preserves behavior and requires simple reuse docs/optimization/orchestration-plan.md:7
REALITY: numbers pass estimates labeled and source regions counted Plugin.cs:371
REALITY: deferred-asks pass no requested contract deferred docs/optimization/orchestration-plan.md:7
REALITY: pr-count pass one integration change set docs/optimization/orchestration-plan.md:24
REALITY: units pass scheduler seconds match existing monotonic clock Plugin.cs:684
REALITY: perf-table pass no speedup claimed; future whole-loop baseline owned by perf lane Plugin.cs:470

## What already exists

- Configuration.Snapshot/HapticsSnapshot own normalization and setup gating; reuse their records. Keep the same Configuration instance passed to SavePluginConfig.
- SettingsWindow and DialogWindow already provide Dalamud sizing, Escape and stable ImGui IDs. Reuse them, ControllerPreview, UiText and existing labels verbatim.
- SfxTrace, TraceArchive, capture.SetTracing and TraceSaveCompletion already implement the actual recording/storage boundaries. Move coordination; do not replace serialization, retention or camera recorder.
- Existing UserConsentChecks protect default-off and recording gating; WindowingChecks protect compiled Window API; SfxTraceChecks and TraceArchiveChecks protect disk formats and bounds.
- NativeHapticOutput.Configure:146-157 contains the repeated property comparison. It belongs to the native lane. Do not replace it with per-tick record construction here; preserve effective gating and generation/reset behavior.

## Architecture review

1. [P2] (confidence: 10/10) Plugin.cs:689: `pluginInterface.SavePluginConfig(configuration);` then line690 `saveAt = double.PositiveInfinity;`. A thrown save keeps the deadline overdue. Update:477/554 invokes Save without catch. Disposition: D2 accepted bounded retry and dirty retention.
2. [P2] (confidence: 9/10) Plugin.cs:1363 `worker.GetAwaiter().GetResult();` precedes every native dispose. A worker finalizer failure prevents remaining cleanup. Constructor catch:190 similarly joins before sequential cleanup. Disposition: D3 accepted exception-isolated cleanup; actual native leak under injected failure remains unverified.

Proposed data flow:

```text
UI / command edits same Configuration
  -> Plugin.Changed -> publish normalized immutable settings -> worker/native
                    -> saveScheduler.MarkDirty(now)
Framework.Update -> saveScheduler.TrySaveDue(now)
  not dirty / before deadline -> no operation
  due -> SavePluginConfig(same Configuration)
    success -> clean, clear error episode
    failure -> keep dirty, retryAt = now + 5 seconds, report once per episode

UI /bcef trace -> consent checks -> coordinator.RequestToggle
Framework.Update -> coordinator.Update(capture, settings, objects)
  complete old task -> report/save partial on failure
  delete request -> reject busy OR stop camera, await camera save, delete
  toggle odd -> stop active OR start if consent + capture ready + no writer
  active sample -> stop request / segment boundary / sampling error
    writer busy -> stop and serialize final partial behind prior writer
    writer idle -> publish finished segment, start next

Dispose / constructor failure
  detach callbacks and commands -> publish disabled -> stop recording
  cancel -> join worker -> join optional initialization -> dispose acquired owners
  join recording save -> final dirty save attempt -> font/token cleanup
  each step catches/reports independently; later steps always attempted
```

SettingsUi owns WindowSystem, both windows, font/preview rendering, dialog state, setup choices and all drawing methods presently in Plugin*.cs. It takes the existing configuration/UiText plus narrow callbacks for settings changes, explicit persistence, trace intents, rumble tests and command-help refresh. It receives a readonly value snapshot of runtime status once per Draw, using existing snapshot/native telemetry values; it receives no Plugin reference and owns no hooks or worker. Use a readonly struct or stable accessors to avoid per-frame heap allocations. Do not create a property-bag service locator. Plugin.Open/command route to SettingsUi.Open/ShowInfo/ShowRecordingNotice and retain command strings.

DiagnosticRecordingCoordinator owns trace fields, toggle/delete intents, status/error/size cache, monotonic trace clock, segment queue and shutdown flush. It is framework-owned; UI only posts intent and reads status. Sampling receives concrete capture/settings/object-table inputs on the framework thread. Reuse SfxTrace directly. Preserve single completed writer plus one active segment and final partial chaining. Do not introduce an unbounded queue. It has no ImGui dependency; storage paths and informational status strings preserve current translations.

ConfigurationSaveScheduler owns dirty/deadline/revision/error-episode state and invokes injected production save/report actions. It does not own Configuration, perform background serialization, or swallow explicit consent saves. MarkDirty schedules 0.5 seconds after an edit; failure schedules 5 seconds after attempt; further edits during failure preserve the retry floor so dragging a slider cannot cause repeated storage attempts. Success clears only the revision captured before saving; reentrant edits retain their own debounce. A single scheduler lock serializes save callbacks and dirty events; framework updates use nonblocking TryEnter, explicit saves wait, unload waits once. Error reads are volatile. Explicit SaveNow remains throwing; no unconditional acknowledgement after setup publication. Framework TrySaveDue catches/report-deduplicates storage failure. Unload attempts dirty save once regardless of deadline and reports failure, with no retry loop.

## Code Quality review

3. [P2] (confidence: 9/10) Plugin.cs:404 `private void QueueTraceSave(SfxTrace recording)` and :718 `private void DrawSettings()` demonstrate unrelated storage/UI ownership in Plugin. Disposition: D4 accepted actual owners above, not partial-file movement.
4. [P2] (confidence: 9/10) Plugin.cs:194 `rumble?.Dispose();` through :200 `uiFont?.Dispose();` and :1369-1375 are sequential fallible teardown. SfxAudioCapture.cs:814-826 and GameOutputGuard.cs:102-103 have the same resource-release risk. Disposition: D3 includes inner resource owners; parent explicitly assigned SfxAudioCapture, GameOutputGuard and XInputRumble lifecycle cleanup to this lane. NativeHapticOutput remains sibling-owned. Preserve initial constructor exception; report cleanup errors separately. Do not add a generic cleanup framework.

Existing UI moves approximately 950-1250 implementation lines including privacy/haptics; recording moves approximately 180-260 overlapping source lines excluded from UI count. Estimate 80-160 added wiring/owner declarations, 20-40 removed orchestration lines, net production growth 60-140 lines. Tests/integration add 150-300 lines, total growth 210-440. This extraction is justified by single ownership and independently testable failure handling, not line savings. UI and command edits already share Changed; preserve that shared boundary rather than introducing another settings comparison layer.

## Decision ledger

All decisions below are planning dispositions under parent request: auto-select clear highest completeness, escalate actual ties. No destructive action; all runtime contracts await implementation QA.

| ID | Baseline | Selected (completeness) | Alternative (completeness) | Accepted scope / evidence |
|---|---|---|---|---|
| D1 | one Plugin plus partials | three actual owners (10/10) | many presenters/services (8/10) | same contracts, fewer mutable owners |
| D2 | 0.5s debounce; uncaught autosave | 5s fixed retry, dirty + one error episode (10/10) | catch only, retry next frame (4/10) | Save:687-690; protect explicit SaveNow throws |
| D3 | cleanup stops on first exception | attempt every acquired resource, retain first constructor failure (10/10) | catch worker only (7/10) | Plugin:183-201,1345-1384; inner native owners coordinated |
| D4 | UI and recorder in Plugin | move state and operations to actual owners (10/10) | partial-file shuffle (3/10) | Plugin.cs / Plugin.Privacy.cs / Plugin.Haptics.cs |
| D5 | config/trace/window tests, no scheduler fault proof | regressions for each changed observable contract (10/10) | happy path extraction checks (7/10) | tests listed below; retain all existing tests |
| D6 | unconditional successful-save acknowledgement | revision-aware acknowledgement and serialized saves (10/10) | deadline-only fix without reentrant edits (6/10) | parent QA explicitly required reentrant/concurrent dirty retention |

D2 rationale: fixed retry is easier to reason about than exponential backoff for a local configuration file. One warning per failure episode avoids log flooding even if messages vary. On success, a later failure is a new episode. Existing localized consent failure UI remains. Autosave failure is visible in SettingsUi with a translated concise message and retry indication; add EN/CN/JP entries together. No popup or chat spam.

Approval readiness: PASS for planning D1-D6 under exact parent auto-decision authorization. Implementation remains withheld. No genuine highest-completeness tie. Scope selection is not a substitute for each remedy disposition above.

## Test review

Detected framework: bespoke net10.0 regression executable, tests/LightingChecks.csproj and Program.Main. No new framework. Existing source/tests read separately before diagramming. Existing tests do not construct Plugin or simulate ImGui drawing; WindowingChecks explicitly describes that limit.

```text
CODE PATHS                                      USER FLOWS
save scheduler [GAP proposed]                    edit slider/command/language
  clean / not due -> zero calls                    -> 0.5s debounce -> same config saved
  due success -> clean [GAP]                     disk denied -> warning + retry [GAP]
  due failure -> dirty + retry floor [GAP]       setup choice -> save fails -> stays disabled
  edit during retry / success -> new episode       [★★★ existing config consent; GAP UI boundary]
  explicit failure -> throws [GAP]               recording notice save fails -> no recording
record coordinator [existing storage tests]      [→E2E GAP orchestration]
  consent false / capture absent / writer busy   /bcef trace rapid double toggle / stop / unload
  start -> sample -> rollover -> slow writer       [GAP coordinator; storage ★★★ tested]
  stop / exception -> final partial saved        delete while camera writer active [GAP]
  delete waits camera -> disk failure/status     settings/dialog close/Escape/localization
lifecycle [GAP exceptional orchestration]           [★★ WindowingChecks + UiTextChecks; →E2E]
  acquisition stage throws -> prior resources    native output/focus/replug unchanged
  cleanup step throws -> every later attempt       [★★★ existing session/native checks]
  delayed initializer -> join+dispose once
  repeated Dispose -> no repeated effects
```

Coverage ratios are unknown: no coverage instrumentation or complete branch enumeration executed. Do not turn existing assertion counts into coverage. No LLM/eval scope. Tests obsolete: none.

Critical required test requirements:

- Add ConfigurationSaveChecks using the actual scheduler boundary: fake save throws repeatedly, calls do not increase before 5 seconds; dirty retained; edit during failure cannot bypass deadline; success resets episode and clears dirty; later edit saves after debounce; same Configuration reference observed; explicit SaveNow throws and cannot activate consent. Value: protects=bounded save recovery; fails_when=deadline clears early or stays overdue; why_new=no scheduler failure coverage; seam=production save action; no_claim=real filesystem permissions.
- Extend UserConsentChecks at the production setup/recording acceptance boundary after extraction: failed explicit save restores all modified consent flags and effects remain inactive; successful save precedes Changed/tracing; reset/language do not grant consent. Value: protects=persist-before-activation; fails_when=autosave catch hides consent failure; why_new=current checks exercise Configuration only; seam=production save action; no_claim=ImGui interaction.
- Add coordinator checks with existing trace fixtures: rapid toggle parity, missing capture, pending save blocks restart, rollover with slow writer stops boundedly, earlier failed save still saves final partial, delete waits camera writer and cannot race restart, unload final partial completes. Value: protects=bounded recording and no lost final segment; fails_when=writer ownership/ordering changes; why_new=storage checks omit Plugin coordination; seam=real coordinator storage boundary; no_claim=live native snapshot correctness.
- Add lifecycle checks at real cleanup owner boundaries: fail each stage individually, verify subsequent cleanup attempts and first exception preservation; late successful initializer disposed once; failed initializer observed; repeated unload has no repeated operations. Extend HookOriginalLifecycleChecks for original playback if touched. Value: protects=all acquired resources attempted on failure; fails_when=early exception skips later cleanup; why_new=existing hook tests cover already-disposed original only; seam=actual cleanup actions; no_claim=arbitrary in-game hook interleavings.
- Extend WindowingChecks to extracted UI identity/wiring; retain stable window/dialog identifiers, both registrations, close behavior, Escape and default focus. Manual in-game EN/CN/JP pass across setup/reset/record notice/delete, `/bcef` feature commands and focus/replug remains required. Value: protects=existing settings and consent access; fails_when=extraction drops callback or stable ID; why_new=owner changes; seam=none; no_claim=visual correctness without manual run.

## Performance review

5. [P2] (confidence: 9/10) Plugin.cs:1158-1166 synchronously enumerates trace files and reads FileInfo.Length during UI draw once per second. Scale and duration unknown. D4 moves ownership/cache to coordinator but preserves frequency; no async IO redesign without measured evidence. Distribution of cache refresh onto framework is not a performance improvement: avoid relocating disk IO onto another latency-sensitive callback just for tidiness. If expensive, follow measurement before changing policy.

Save scheduler adds constant-time checks per update and zero work on clean frames. No background save of mutable Configuration. Owner references/delegates allocated once; runtime status uses readonly value snapshot; no LINQ or reflection added per frame. Existing native comparison and coherent stereo changes belong native lane. Perf owner measures complete framework and worker loops before/after, including roster and output; do not claim zero-allocation whole-plugin results from helper probes. UI disk-size work and synchronous SavePluginConfig may still block one frame when actually attempted; measured latency remains unknown.

## Failure modes

| Path | Realistic failure | Handling and required proof | User visibility |
|---|---|---|---|
| Autosave | disk full/denied | dirty + 5s retry + episode dedup tests | translated nonmodal error |
| Consent | explicit save fails | throwing SaveNow + rollback, no output/recording | existing notice error |
| Recorder | segment writer stalls | one active + prior writer; stop; final partial chain test | existing slow-storage status |
| Recorder | save/delete fails | observe task, preserve prior failure, unblock after completion | existing translated status |
| Cleanup | worker finalizer faults | independent release/dispose/join attempts | warning per failed stage |
| Initializer | completes during unload | join then dispose once; no publish after detach | warning on failure |
| UI | rendering throws | balanced existing ImGui try/finally + WindowSystem | existing host error handling |

Failure modes: 0 critical gaps in proposed plan; proof still must be implemented. Native arbitrary interleavings remain a disclosed limit, not a claimed pass.

## Implementation Tasks

Effort estimates assume reuse of current harness; no time measurements were performed.

- [ ] T1 (P2, human ~2h / agent ~20min): implement scheduler and its meaningful failure regressions; integrate every current saveAt write, preserve explicit consent SaveNow semantics. Files: proposed Core/ConfigurationSaveScheduler.cs, Plugin*.cs, tests/ConfigurationSaveChecks.cs. Depends: none.
- [ ] T2 (P2, human ~4h / agent ~45min): extract DiagnosticRecordingCoordinator with actual ownership and integration tests. Preserve camera save ordering, trace bounds and native snapshot thread. Files: proposed DiagnosticRecordingCoordinator.cs, Plugin.cs, Plugin.Privacy.cs, trace checks. Depends: T1 only where shared Plugin edits need sequencing.
- [ ] T3 (P2, human ~5h / agent ~60min): extract SettingsUi with narrow inputs/callbacks and all settings/consent/dialog drawing. Files: proposed Ui/SettingsUi*.cs, all Plugin*.cs, UiText.Translations.cs if adding autosave status. Preserve existing strings/IDs except new error. Depends: T1,T2.
- [ ] T4 (P2, human ~3h / agent ~45min): isolate constructor/worker-finally/Dispose cleanup and all acquired nested resource cleanup; retain initial failure and report teardown failures. Files: Plugin.cs plus coordinated native owners. Depends: native ownership agreement; T3 for UI disposal owner.
- [ ] T5 (P2, human ~2h / agent ~30min): run isolated locked Release build + regression executable + package checks; parent performs one shared Windows build only after all edits settle; perform documented in-game checks when available. Depends: T1-T4 and native/perf lane integration.

Sequential implementation within orchestration lane because all extraction steps modify Plugin*.cs. Native layout, native session/stereo and test-harness/perf lanes can proceed independently in their owned modules. Shared native teardown edits must be assigned before editing. Integration order: scheduler -> recorder -> UI -> lifecycle -> combined validation; independent native fixes need not wait for UI extraction. No shared Windows build or dev DLL publication while editing. No commits/push in this task.

## NOT in scope

No UI redesign, dependency, configuration schema migration, replacement serializers, new async configuration writer, generic lifecycle framework or broad game/native abstraction. Native stop-state/stereo and layout fixes remain sibling-lane owned, not omitted from the overall optimization. Live hardware timing cannot be asserted without hardware execution.

## Outside Voice and workflow execution limits

Applied `/mnt/c/Users/Jackoltrade/.codex/skills/gstack-plan-eng-review/SKILL.md`, fully read in five bounded chunks after initial truncated read. Real preamble ran: SESSION_KIND interactive, QUESTION_TUNING false, TELEMETRY off, ARTIFACTS_SYNC off. Update check crashed (status UNKNOWN), lease held elsewhere. Context-recovery returned no artifact output. Parent explicitly requested skipping office-hours. No new unfamiliar architectural/concurrency pattern or dependency proposed; reuse existing C# actions, records and tasks.

Initial helper call failed because bun was absent from PATH. Parent supplied `/home/jackoltrade/.bun/bin`; reran with PATH prefix. Reality helper now passes: `PLAN_REALITY: phase=eng rows=9/9 missing=none unverified=1 verdict=complete`. Review-read ran and returned NO_REVIEWS; no design/CEO plan paths printed. Outside preflight using the real claude-bin resolver returned `CODEX_MODE: not_installed`. Required bounded fallback Plan agent type/background timer/TaskStop tools are unavailable, so no fallback launched. Repair: install/authenticate Claude Code, or rerun outside review when the bounded host tools exist. Parent QA is separate, not outside-provider completion. Required helper state is `/home/jackoltrade/.gstack`, outside lane writable roots; parent can persist logs and auxiliary artifacts with authorized escalation. Full QA requirements/tasks are included above for parent use. Parent persisted both logs using authorized escalation; review-read verified them at 2026-11-05T09:53:08.600869+00:00. Status remains ISSUES OPEN; no CLEAR claim.

## Completion summary

Scope accepted as-is. Architecture: 3 findings; code quality: 2; tests: diagram and 5 grouped critical coverage gaps; performance: 1. Reality rows: 9/9 helper validated; unverified premises 1. NOT in scope and reuse recorded. TODO proposals 0. Failure modes: 0 critical proposed gaps. Unresolved remedy decisions 0. Outside voice unavailable. Parallelization: one sequential orchestration lane beside independent sibling lanes. Lake score 6/6 selected 10/10 planning options. Parent approved implementation. Current production changes await combined candidate compilation/regression execution; no live-game timing collected.

## Parent QA amendment: dirty revisions and persistence serialization

[P2] (confidence:10/10) initial ConfigurationSaveScheduler.SaveNow called save then unconditionally acknowledged dirty state. Parent provided the deterministic case: save callback invokes MarkDirty. Revised scheduler captures revision N, preserves dirty revision N+1, resets error/retry only on successful IO, and serializes explicit save callbacks. Framework autosave never waits on a busy explicit save; forced teardown waits. Production setup acceptance now publishes saved snapshots without MarkDirty or unconditional clear. Configuration property mutation remains owned by existing UI/command boundaries; the scheduler protects storage callback/dirty ordering and same Configuration instance, without inventing background serialization. Added callback/reentrant success/failure tests and bounded concurrent save/edit tests. D6 accepted under explicit parent instruction. This adds one Architecture finding, total11 issue/gap groups. No independent scope change.

The real full Plugin.Update now has opt-in aggregate count/total/max Stopwatch timing, capped at one million samples, surfaced in Diagnostics in EN/CN/JP. Disabled by default; no per-frame allocations or logging added. This fulfills parent/perf-lane integration request; no game timing is claimed collected.

## GSTACK REVIEW REPORT

| Review | Trigger | Why | Runs | Status | Findings |
|---|---|---|---|---|---|
| CEO Review | not requested | existing maintenance scope | unknown | not run | none claimed |
| Outside Review | claude-code plan review | independent second opinion | 0 | unavailable | Claude CLI missing; bounded fallback unavailable |
| Eng Review | /plan-eng-review | architecture & tests | 1 | ISSUES OPEN (logged) | 11 issues/gap groups mapped; 0 critical proposed gaps |
| Design Review | not requested | behavior-preserving extraction | 0 | not run | existing UI retained |
| DX Review | not requested | no developer product change | 0 | not run | none claimed |

**OUTSIDE COVERAGE:** claude-code, plan-review, unavailable; no clean credit.
**VERDICT:** Plan authored and reviewed, parent QA accepted; required log and review-read verified; ISSUES OPEN because mapped implementation remains.
NO UNRESOLVED DECISIONS
