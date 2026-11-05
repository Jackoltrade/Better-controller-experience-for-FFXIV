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

## Lifecycle owner verification addendum (166ff17)

Fixed review target remains this file. Parent requested stronger actual-owner fault injection and explicitly assigned EnemySkillFilter/EnemyEffectObserver cleanup corrections under existing D3 nested-resource scope. No new production seam or framework. Real /plan-eng-review startup rerun; prior complete skill/context/reuse/history review remains applicable. Office-hours explicitly skipped. Highest-completeness choices below are authorized; no tie.

Scope: two existing nested native owners and one dedicated compiled test fixture. Three files, zero new production classes/services, one integrated correction. No package, platform, configuration, output-route or distribution change. Sequential native-owner edits, then private candidate build/test. Tests use the installed Dalamud API, generated managed Hook subclasses and DispatchProxy. Hook creation/enabling/disabling/disposal callbacks are injected; no OS hooks installed or game/controller calls. XInput restoration uses an owned unmanaged sentinel plus a balanced system-library reference. Metadata access restrictions are bypassed only in the generated test assembly to form exact private delegate types.

Architecture findings:

- [P1] (confidence:10/10) EnemySkillFilter.cs:257 `if (Interlocked.Exchange(ref disposed, 1) == 0) { effects?.Dispose(); receive.Dispose(); }`. Baseline actual SfxAudioCapture -> EnemySkillFilter -> EnemyEffectObserver fixture throws during effect hook disposal; observed order is `effect,render`, omitting receive. Repeating disposal cannot recover it because disposed is already set. Reproduction: work/lifecycle-owner-checks/pre-fix-results.txt. D7 selects existing ResourceCleanup.Run for both acquired owners (10/10), versus unchanged sequential cleanup (4/10). Parent explicitly authorized this correction.
- [P2] (confidence:10/10 for EnemyEffectObserver,9/10 static for matching EnemySkillFilter catch) EnemyEffectObserver.cs:38 `catch { constructor.Dispose(); throw; }` and EnemySkillFilter.cs:160 `catch { receive.Dispose(); throw; }` replace an Enable failure if rollback Dispose also throws. Actual EnemyEffectObserver constructor fixture validates relative tables in owned memory, injects Enable and Dispose failures and observes only the Dispose error. D8 selects preserving the original Enable exception when cleanup succeeds and aggregating both errors with Enable first when cleanup fails (10/10), versus suppressing cleanup evidence (7/10). No additional error is swallowed.

Code quality review: reuse ResourceCleanup for multi-owner teardown, retain existing Interlocked idempotency. Constructor correction is a small nested catch in each existing owner; no callback registry, new lifecycle abstraction or changed hook signature. Source audit found GameScreenEffects already attempts both hooks in nested finally; ArenaSoundEffects owns managed catalog work, not hooks; NpcSpawnObserver and GameLimitBreak own one hook. No further speculative production changes.

Test review and execution map:

```text
Guard actual constructor -> second acquisition fails -> first Dispose also fails -> original acquisition retained
Guard actual Enable -> second hook fails -> Disable fails -> both acquired hooks disposed
Sfx actual Dispose -> each of 8 hook positions fails -> all later hooks attempted -> tracing off -> repeat no-op
Sfx -> EnemySkillFilter -> effect Dispose fails -> receive still disposed -> later Sfx render disposed [pre-fix GAP reproduced]
Effect actual constructor -> Enable fails -> rollback succeeds OR throws -> original/both errors retained [pre-fix GAP reproduced]
XInput actual Dispose -> one slot restore throws -> other slots restore game motors -> all hooks + owned module released -> repeat no-op
Worker actual canceled RunWorker -> first rumble release throws -> Bluetooth + owned HID stop/close still run
Plugin actual Dispose -> detach throws + worker task fault -> pending observer result disposed -> hooks/save/font/token attempted -> snapshots off -> repeat no-op
```

Value: protects=actual acquired-owner cleanup and error provenance; fails_when=an owner omits a later resource or masks initial failure; why_new=generic cleanup tests did not exercise owner wiring; seam=none; no_claim=OS hook backend success or arbitrary native callback interleavings. Actual Plugin/Sfx constructor acquisition beyond available managed hooks is not claimed dynamically covered; full game-data/native setup is unavailable to these fixtures. Tests use actual public disposal/constructor/worker boundaries, not copies of cleanup loops. No existing tests retired.

Performance review: corrections run only on construction failure or disposal; params-array allocation is teardown-only. No hot callback change and no measured speedup claimed.

Decision ledger amendment: D7 and D8 approved via explicit parent highest-completeness authorization, original D3 scope retained. Approval readiness: PASS D1-D8. Critical proposed gaps0; unresolved decisions0. Required proof: save baseline failure output, build private updated plugin with installed SDK, run actual-owner checks and report exact successes/failures. Root owns shared regression registration. Outside review remains unavailable (previous CLI/fallback probe); no CLEAR or hardware certification claimed.

Lifecycle addendum validation: baseline private compiled fixtures reproduced missing receive disposal and masked constructor errors; copied post-fix plugin and private test executable built with zero warnings/errors and passed 38 actual-owner assertions. Final bounded fixture revision rechecked successfully: 38 assertions; copied plugin SHA256 53ae073472682fde48772f0109066ae98ac1e1eab88a2a7709bca85dadfc2f9c. Evidence files: work/lifecycle-owner-checks/pre-fix-results.txt and post-fix-results.txt. Two actual nested-owner defects are added to the reviewed finding total (13). No arbitrary in-game interleaving or actual OS-hook backend disposal success is certified.

## Live QA addendum: Escape propagation from dialogs

Fixed target/report remains this orchestration/windowing plan. The user reports Escape closes Info and also affects game UI. Installed CN15.0.3.8 configuration has IsFocusManagementEnabled=True (root readback). Existing RespectCloseHotkey metadata checks did not cover the input lifecycle. Real plan-eng-review startup rerun; full skill already read, context reused, office-hours explicitly skipped. Root accepted the measured canonical proposal before runtime edits.

Root cause verified against installed DLL IL and a private real-ImGui frame probe. WindowHost closes a focused dialog after Draw and retains its focused flag for that frame. On the next frame, the closed host calls OnClose then clears focus. Current SettingsUi registers settings before dialog and never restores parent focus: the parent host has already drawn unfocused before the closed dialog is processed. WindowSystem then sets ShouldInhibitAtkCloseEvents=False for one frame, so the same held Escape can reach native close/system-menu consumers. This is more specific than a missing RespectCloseHotkey property.

Installed receipts: work/window-escape-inspection/installed-il.txt (DrawInternal offsets0D01-0D9C, closed branch0070-007C, WindowSystem suppression0264-0294; actual AtkUnitBaseReceiveGlobalEventDetour and AgentHudOpenSystemMenuDetour consumers). Primary upstream cross-checks: [WindowHost](https://raw.githubusercontent.com/goatcorp/Dalamud/master/Dalamud/Interface/Windowing/WindowHost.cs), [WindowSystem](https://raw.githubusercontent.com/goatcorp/Dalamud/master/Dalamud/Interface/Windowing/WindowSystem.cs), [Window API](https://raw.githubusercontent.com/goatcorp/Dalamud/master/Dalamud/Interface/Windowing/Window.cs). Installed IL, not current upstream alone, establishes this client's ordering.

Private native-ImGui/installed-WindowSystem fixture uses owned key-state buffers and private service objects, not game input or hooks. It opens the dialog from the parent Draw exactly like SettingsUi. Evidence: work/window-escape-frames/production-order-results.txt.

| Scenario | Escape closes dialog frame2 | Held frame3 | Held frame4 |
|---|---|---|---|
| Current settings-first | parent open, dialog closed, inhibition True | both unfocused, inhibition False | parent focus/inhibition recover |
| Dialog-first + native OnClose parent focus | parent open, dialog closed, inhibition True | parent focused, inhibition True | parent focused, inhibition True |

Architecture review: [P2] (confidence10/10) SettingsUi.cs:81 registers parent before dialog; DialogWindow has no parent-focus close callback. The frame probe reproduces the one-frame inhibition gap. Selected E1: register dialog before settings and use DialogWindow.OnClose -> settingsWindow.BringToFront when the parent remains open. OnClose then runs before the parent host draws on the next frame; its canonical RequestFocus is applied by that host in the same frame. No manual key state, global key hook, private runtime field, fullscreen dim or popup-modal behavior enters production. A dialog opened during parent Draw appears on the next frame; root explicitly accepted this bounded one-frame opening delay. Title X, buttons and native Escape all share native close handling; closing the parent still closes the dialog and does not reopen the parent.

Code quality review: extend DialogWindow with the same Closed callback pattern already used by SettingsWindow. Wire it in SettingsUi and reverse the two existing AddWindow calls. Optional granular registration method may be used if needed to exercise the actual owner order without static IL-mirroring assertions; no new class/interface or broad context object. Alternative pre-draw transition tracking adds persistent state and first-frame close edge cases; custom Escape hooks are rejected because the native API solution is verified.

Test review:

```text
parent opens Info/Reset/Recording/Delete -> dialog focus [real WindowHost]
Escape down -> only dialog closes -> closing-frame native inhibition remains True
held Escape -> Dialog.OnClose -> parent RequestFocus -> parent host applies focus -> inhibition remains True
Escape up -> native edge latch resets -> main remains open
next distinct Escape -> main can close normally
Title X / button -> same OnClose callback -> parent focus request, never reopen closed parent
parent X / dispose -> both closed, no stale focus request
first-run Info -> same dialog shell; no consent activation from closing
```

Required regression depth: extend compiled WindowingChecks to exercise actual Dialog.OnClose and SettingsUi registration order, plus real installed WindowHost/frame inhibition where feasible. Keep existing stable IDs, ShowCloseButton, no-fullscreen flags, title/close behavior and font stack checks. The private real frame probe is independent behavior evidence; it does not certify the user's live key timing or OS integration until live QA. Value: protects=dialog-only close and continuous native Escape suppression; fails_when=parent restoration runs after parent draw; why_new=RespectCloseHotkey property test missed ordering; seam=native Window API; no_claim=live game event capture before user retest.

Performance review: two window-registration calls reordered and one native focus request at close. No frame timer change, keyboard polling, file IO or per-frame allocation added. The separately reported25,895 update timings are outside this fix and remain owned by the root/performance lane.

Scope: SettingsUi.cs, DialogWindow.cs, WindowingChecks.cs; SettingsWindow behavior reused. Zero new classes/services. Sequential UI fix and private regression, then root's SDK build/staging and user live retest. No shared DLL build/publication by this lane. D/E decision: selected canonical E1 under simplicity/reuse authorization, no unrelated scope. Approval readiness: PASS under explicit root review; no human permission requested. Outside coverage remains unavailable; no CLEAR claim.

Escape addendum follow-up: real frame probe also verified dialog-first alone renders one orphan dialog frame when the parent is closed before Draw. Evidence work/window-escape-frames/parent-close-results.txt, mode1 frame8 dialogDrawn=True versus mode2 frame8 False. Root explicitly approved the smallest correction: before WindowSystem.Draw, clear dialog.IsOpen if settings.IsOpen is false. No additional tracking state; main close/child close callbacks stay unchanged.

D3 lifecycle amendment (root-owned implementation): independent review confirmed captured callbacks or failed unsubscribe can start Open, Draw, Update or OnCommand after Shutdown. Root explicitly approved volatile disposed guards at those four entry points and setting disposed=1 at Shutdown entry without an early return there. This prevents post-shutdown starts; it does not claim cancellation of an already-entered callback. Root owns Plugin.cs; final_quality_review owns the corresponding actual-owner test extension. This UI lane does not edit those files.

Escape addendum validation: root's exact artifacts/optimization-live-fix/plugin/DualSenseLighting.dll passed the real installed ImGui/WindowSystem frame probe using actual compiled SettingsUi.RegisterWindows, SettingsUi.Draw, SettingsWindow and DialogWindow. Mode2 frames2-4 keep parent open and native inhibition True while only dialog closes; frame8 parent close draws no orphan. Evidence work/window-escape-frames/actual-owner-results.txt and receipt.json. Root reports full Release build zero warnings/errors and56/56 regression groups passing (516,911 assertions), including expanded actual owner WindowingChecks. These checks establish the repaired frame-state contract; user live retest remains required. E1 canonical fix and parent-close precondition accepted by root before edits. One Escape ordering finding adds to prior13, total14 reviewed issue/gap groups. No timing code edited by this UI lane; root separately integrated timing into the frozen UI file.

## Live retest correction: Escape also cancels the selected target

The user retested exact staged DLL4acb71f000ac98a91894a0956362c0ffe69bf92867a50108d8858ccf448aa801 and reported that Escape closes Info and clears the current target. E1 repaired a real focus gap, but its post-draw AtkClose/SystemMenu inhibition fixture did not cover raw keyboard target cancel. It is not a live fix receipt. New exact-DLL probe work/window-escape-frames/raw-target-path-results.txt shows mode2 frames2-4 inhibit=True **and rawEscape=1**. The game keyboard buffer remains available to other native consumers.

Installed CN15.0.3.8 IL in work/window-escape-inspection/target-key-routing-il.txt establishes: Framework.HandleFrameworkUpdate runs RunFrameworkTick/public Update before native Tick original; InterfaceManager.OnNewInputFrame clears all keys only for WantTextInput; ordinary Window focus inhibition does not consume keyboard state. KeyState.SetRawValue permits only zero and writes the mapped game-buffer integer. Primary references: [IKeyState](https://raw.githubusercontent.com/goatcorp/Dalamud/master/Dalamud/Plugin/Services/IKeyState.cs), [KeyState](https://raw.githubusercontent.com/goatcorp/Dalamud/master/Dalamud/Game/ClientState/Keys/KeyState.cs), [Framework](https://raw.githubusercontent.com/goatcorp/Dalamud/master/Dalamud/Game/Framework.cs), [InterfaceManager](https://raw.githubusercontent.com/goatcorp/Dalamud/master/Dalamud/Interface/Internal/InterfaceManager.cs). Installed ordering, not upstream version alone, is the compatibility evidence.

Focused plan-eng-review rerun (session1581-1793883617-f60a71b1), existing fully read skill/context reused; office-hours skipped by explicit instruction. Startup reports an upgrade available; no upgrade is included in this maintenance change. Parent explicitly approved E2 before runtime edits. Architecture decision E2: reuse injected public IKeyState at Plugin.UpdateCore entry. SettingsUi.ProcessInput samples Escape only for an open parent's focused or focus-requested dialog, closes only that dialog, and writes false to Escape before native Tick. WindowHost cannot make that particular Escape decision because it reads the same buffer that must already be cleared. All other close paths, native OnClose/focus handoff, title X and existing styles remain unchanged. Root owns the small Plugin dependency/call integration.

Code quality/review correction E2b: release-edge-only was rejected before freeze because installed UiBuilder.OnDraw returns without plugin Draw while UI is hidden (work/window-escape-inspection/render-gating-il.txt). Independent ImGui OS events are verified in Win32Backend.ProcessWndProcW/AddKeyEvent (backend-key-feed-il.txt), but missed Draw callbacks can lose its release edge. No ImGui query remains in Framework.Update. Parent approved the simpler independent Windows observation: existing Native helper uses GetAsyncKeyState(0x1B), SHORT(int) ABI, high bit only. [Microsoft contract](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getasynckeystate) specifies the high bit is current down, low bit is unreliable, and zero may also occur on inaccessible/inactive desktops; zero ends our ownership after final clearing. No keyboard hook, key synthesis, background polling service, hidden-event subscription or frame-count protocol is added. This Windows-only plugin already has user32 interop.

SettingsUi.ProcessInput(IKeyState) forwards to the granular internal (IKeyState, Func<bool>) operation using one cached static Native.IsEscapeDown delegate. Initial entry closes the dialog and clears Escape without observing physical state. Each later consumed Update clears Escape first, then ends the latch when physical state is up. It works without any Draw, including hidden/minimized/released/closed-parent transitions. Only while latched is physical state queried. The same initial press cannot leak on a short click; the final release frame is also cleared. A separate later Escape is unconsumed. Root owns Native/Plugin integration. The injected release signal is a narrow deterministic test seam, not a new framework.

Failure/test review: actual compiled owner tests cover every DialogKind, pending focus, unfocused/closed-parent exclusion, strict false-only Escape writes, and held-key suppression after dialog closure. Private installed KeyState plus owned native buffer test must show zero before a simulated native target reader, with a distinct unrelated key unchanged; independent physical release without Draw must end the latch and allow a later ordinary parent Escape. No actual game target consumer is invoked, and live target retention is not certified by these tests. First-run/recording consent, parent-close child cleanup and title-X behavior retain existing tests. No per-frame allocation, logging, file IO or global keyboard capture is added; cost is a few conditionals, one key-buffer store and one OS key observation only while latched.

E2 selected automatically as highest completeness under parent authorization: canonical Window flags alone demonstrably leave rawEscape=1; global hooks/native patches add unnecessary scope. No genuine tie. Dependencies: root inject IKeyState/call -> private candidate -> actual-owner/raw-buffer/ImGui regressions -> parent full SDK QA/staging. Outside review remains unavailable as previously recorded; no CLEAR credit. Raw input plus hidden-window acquisition are two distinguished gaps, making16 reviewed issue/gap groups. No unresolved design decision in the approved E2b contract.

E2b validation receipt: private exact-candidate compiled-owner checks pass46 assertions with zero build warnings/errors (work/window-escape-candidate/consumption-checks-results.txt). Real installed WindowSystem/KeyState/ImGui fixture passes (work/window-escape-frames/consumed-target-path-results.txt): before simulated native consumer, frames2-5 Escape=0 while unrelated key=123; independent ImGui still sees held Escape; parent remains open; frame5 deliberately skips owner.Draw but release still ends consumption; frame7 next separate Escape remains1 and closes parent normally. Candidate SHA25637f15d4aaa83b93a9b0beeaa725a42c86327b1f8aa468203110fe6d734bd6192. This is owned-buffer routing evidence, not an actual game target-retention test. Required real gstack log records ISSUES OPEN15/unresolved0/critical0 at166ff17; readback saved work/window-escape-inspection/e2-review-read.txt. Plan-reality7/7 complete,1unverified. No outside-review clean credit.

E2b visibility ownership refinement (reviewed before edits): a hidden open dialog retains stale native IsFocused because UiBuilder skips its Draw. Installed public IUiBuilder has no per-plugin hidden getter; ShouldModifyUi only forwards global IsDispatchingEvents and UiPrepared only checks scene availability (ui-builder-public-surface.txt/ui-builder-visibility-il.txt). Parent approved reusing canonical HideUi/ShowUi in SettingsUi, with one volatile inputHidden flag published to Framework.Update. Input consumption already in progress still clears/releases while hidden; only new acquisition requires visible eligibility. Readonly IUiBuilder is obtained from the existing constructor dependency. Both subscriptions are removed as independent ResourceCleanup actions before windows/font in constructor rollback and Dispose; original construction failure preserved, disposal idempotent. Actual constructor proxies exercise the real embedded preview/font registration path, not a copied lifecycle loop. The pre-visibility exact DLL deterministically fails `Hidden dialog acquired a new game Escape` (work/window-escape-candidate/hidden-before-results.txt); final candidate9d2d7128971b9e1a390fdcecbea16ca47522101613b0c78c17464990f549111a passes55 actual compiled window/constructor/event/input assertions, zero warnings/errors (work/window-escape-candidate/hidden-final-results.txt). Root also reports all56 groups/0skips/516,943 assertions and verifier/process107 pass. Real installed frame/key-buffer proof rechecked successfully against the same final DLL (work/window-escape-frames/consumed-target-path-final-results.txt). The initial red receipt is explicitly extracted from tool session94321, because the full-output rerun overlapped the root DLL overwrite and passed; it is not presented as original full process output. No additional Plugin integration or new service.

## GSTACK REVIEW REPORT

| Review | Trigger | Why | Runs | Status | Findings |
|---|---|---|---|---|---|
| CEO Review | not requested | existing maintenance scope | unknown | not run | none claimed |
| Outside Review | claude-code plan review | independent second opinion | 0 | unavailable | Claude CLI missing; bounded fallback unavailable |
| Eng Review | /plan-eng-review | architecture & tests | 3 (addenda) | ISSUES OPEN (logged) | 16 issues/gap groups mapped; 0 critical proposed gaps |
| Design Review | not requested | behavior-preserving extraction | 0 | not run | existing UI retained |
| DX Review | not requested | no developer product change | 0 | not run | none claimed |

**OUTSIDE COVERAGE:** claude-code, plan-review, unavailable; no clean credit.
**VERDICT:** Plan authored and reviewed, parent QA accepted; required log and review-read verified; ISSUES OPEN records reviewed findings; E1 Atk/frame verification passed but live target-cancel retest failed; approved E2b exact-candidate owned-buffer/actual-owner validation passed; native target live verification remains.
NO UNRESOLVED DECISIONS
