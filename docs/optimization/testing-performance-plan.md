# Testing and performance optimization plan

Plan-time review record. For implemented behavior, current verification and remaining
limits, see [the parent verification report](verification.md).

Selected review target: `docs/optimization/testing-performance-plan.md`.
Baseline: `9354236`; reviewed 2026-11-05. Planning and bounded probes only; no production implementation authorized by this document.

## Original plan and constraints

Make failures deterministic and diagnosable; let independent check groups complete; measure meaningful structural coverage; extend performance evidence to the actual framework roster pipeline and device/report loop. Reuse the console harness, GC/Stopwatch APIs, production roster logic and ILightingDevices boundaries. Keep runtime dependencies unchanged. Preserve all existing assertions, argument modes, protocol tests and ownership behavior. Do not infer game FPS, hardware latency or hook lifetime safety from synthetic measurements.

## Step 1: evidence and verification matrix

| Premise | Receipt | Verdict / next proof |
|---|---|---|
| Allocation assertion omits delta | `tests/EnemyEffectChecks.cs:69`: `check(GC.GetAllocatedBytesForCurrentThread() == before, ...` | Confirmed statically; strict zero remains valid contract for repeated unchanged entries |
| First failure aborts independent groups | `tests/Program.cs:20`: `if (!condition) throw new Exception(message);`; Main directly invokes groups | Confirmed; catch only at group boundary, preserve fail-fast assertions inside a group |
| Helper microbench does not cover complete loops | `tests/PerformanceChecks.cs:30-49` lists five selected helpers | Confirmed; no full roster or TickAsync fixture |
| Production repeated ownership lookup allocates | `Core/EnemyEffectOwnership.cs:34-36` returns if identity/tag unchanged | NOT established; review's one failing run is evidence of inconsistency, not source attribution |
| Existing isolated unchanged ownership loop reproduces failure | `work/testing-performance-probe/results.txt` | 120 windows, 10,000 measured pairs each: 40 original .NET 10.0.1 default zero; portable .NET 10.0.14 first window 6,192 bytes and remaining39 zero; 40 original tiering disabled zero. One process per setting; not 120 cold-process trials |
| Full console diagnostic run reproduces failure | `work/testing-performance-probe/full-results.txt` | Eight fresh original-runtime processes, all zero and 514,925 checks; diagnostic copy excludes optional `--plugin --dalamud` checks, explaining difference from historical 515,057 |
| Dependency-free structural instrumentation is already available | `tests/LightingChecks.csproj:1-8` custom Exe; repository/SDK cache search no collector | No collector found. Built-in GC counts are allocation counters, not line/branch coverage |
| Whole framework Update is independently executable | `Plugin.cs:468-566`, `UpdateEnemyActors:568-674` reads Dalamud and raw pointers | No; roster orchestration extraction lane must supply real production seam before aggregate harness |

Probe method: copied the console project into ignored `work/testing-performance-probe/full`, reused production Core sources, changed only the allocation diagnostic to capture delta before formatting. Built isolated outputs using verified portable SDK10.0.403; executed old installed host .NET10.0.1. No game, controller writes, dev DLL publication or shared build output. Warmup remains 2,000 and measured iterations 10,000. The standalone probe also compares tiering disabled, without treating that setting as a fix.

Allocation falsification box: portable isolated first-window 6,192-byte delta reproduces inconsistently, while eight original full-suite processes do not. Ten additional portable default cold processes reproduce once; three portable tiering-disabled processes do not. Split probe: 24 portable processes (six each SpinWait control/Resolve/Observe/both) reproduce once in SpinWait control and once in Resolve, but SpinWait first-use is a confound. Twenty further cold processes (ten volatile-store control, ten combined operations) give no nonzero deltas. Runtime sampled allocation events in one trace process report catalog-array allocations but no failed window, so they cannot attribute 6,192 bytes. Final positive allocation control returns exactly64 bytes in all40 windows, confirming detection of a known heap allocation; volatile-store empty control remains zero. Current hypothesis is transient runtime/first-use activity, not established; no production defect is inferred. These bounded runs and one EventListener capture are complete; do not loop until green. On a future nonzero delta, retain the failed first run, original runtime/environment, group order, exact delta and binary hash; run once with runtime allocation events or EventPipe if available to locate type/stack, plus empty-loop and known-allocation controls. Sampling may miss a small allocation and must not be called proof of absence. If a managed allocation is attributable to production, fix it there and add a behavioral regression. If attributable to measurement setup, move only that proven setup outside the measured region. If still unknown, retain strict failure and disclose unresolved cause. No retries, tolerance, discarded first sample, forced GC or tiering switch as a pass policy.

## What already exists

- `tests/Program.cs` contains synchronous and async check groups and ILightingDevices fakes; reuse Main's order, parameters and failure contract.
- `PerformanceChecks.Measure` supplies warmup, GC current-thread delta, Stopwatch ticks and JSON; extend its result contract rather than add a benchmarking framework.
- `EnemyEffectOwnership` already fast-paths unchanged entries; no cache rewrite is justified.
- `LightingSession.TickAsync`, `Discover`, `ReleaseAsync` and `DualSenseReport.Create` are public real boundaries. Implement benchmark fakes at ILightingDevices, consume bytes with a checksum and do not clone/store each report as Program's regression fake does.
- `BossEncounterState`, `GameAudioLayout.SelectEnemyActors` and SfxAudioCapture's roster publishing are existing operations. The orchestration lane will extract only their actual ordered pipeline, preserving object-table capture on framework thread.
- CI already runs locked build, checks, hot-path benchmark, Python packaging and binary verification. Extend those paths and upload evidence; preserve package allowlists.

## Scope Challenge

Scope accepted as-is. Estimated selected changes: Program.cs, EnemyEffectChecks.cs, PerformanceChecks.cs, one focused harness test file, one coverage script/config pair, build workflow, and this plan (8 files estimated); at most one small test group helper, no production test-only service. Compare Original arrangement (existing console executable with separate new full-loop fixture file) against Smaller arrangement (existing console executable and measurement file with focused nested fixture). Both retain the same features and public device seam. Auto-selected Smaller arrangement because it preserves every approved contract with fewer runtime/test boundaries. [Layer 1] existing harness first; [Layer 1] Microsoft collector only for structural measurement.

No linked issue or TODOS.md was found. No feature deferral is needed; hardware/game evidence remains explicitly outside synthetic proof. PR count remains one coordinated optimization wave, not additional shipping work.

## Reality check receipts

REALITY: premise-table finding three unverified premises: historical cause, collector support, actual object-table maximum; docs/optimization/testing-performance-plan.md:10
REALITY: already-done pass local HEAD/history read for selected paths with no newer optimization; remote not refreshed during isolated review, parent must rebase-check before implementation; docs/optimization/testing-performance-plan.md:4
REALITY: surface-check finding existing process launcher and benchmark modes read; new collector invocation/version intentionally requires real --help preflight before adoption; docs/optimization/testing-performance-plan.md:27
REALITY: binding-decisions pass explicit parent auto-selection, no implementation, office-hours skip preserved; docs/optimization/testing-performance-plan.md:8
REALITY: numbers pass raw results parsed completely; 120 initial samples with one6192, additional520 with one6192, split960 with two6192, volatile-control800 zero; docs/optimization/testing-performance-plan.md:18
REALITY: deferred-asks pass no linked issue; hardware/game claims explicitly excluded, no silent missing request; docs/optimization/testing-performance-plan.md:8
REALITY: pr-count pass one coordinated optimization wave, no additional PR creation; docs/optimization/testing-performance-plan.md:40
REALITY: falsification-box finding portable reproduces first-window6192, cause remains unproven after bounded split/no-op/runtime-event probes; docs/optimization/testing-performance-plan.md:25
REALITY: stop-reasons pass runner/collector/malformed args/timeout failure behavior maps to named test requirements; docs/optimization/testing-performance-plan.md:8
REALITY: reporter-environment pass original .NET10.0.1 full diagnostic runs separated from portable .NET10.0.14 evidence; docs/optimization/testing-performance-plan.md:19
REALITY: units pass microseconds per tick, bytes per tick/write, seconds and milliseconds timeout separated; docs/optimization/testing-performance-plan.md:8
REALITY: perf-table finding actual paired full-loop p50/p95 results future implementation requirement, no fabricated speedup; docs/optimization/testing-performance-plan.md:16

PLAN_REALITY: 12/12 applicable rows emitted; unverified=3. Findings are explicit work/measurement limits, not hidden passes.

## Engineering review

### 1. Architecture

[P2] (confidence 10/10) `tests/Program.cs:20` quote: `if (!condition) throw new Exception(message);`. Whole-suite control currently shares assertion exception lifetime with an individual group. Adopt named sequential group execution with try/catch at each independent boundary; assertions inside a group still throw, so failed preconditions cannot cascade into unsafe native fixture accesses. Async groups are awaited before the next begins. Record name, pass/fail, assertion delta and full exception. At end print total groups, failures and assertion count; return nonzero if any group failed. Do not catch fatal process corruption or create parallel execution over static/native fixtures.

[P2] (confidence 10/10) `Plugin.cs:593` quote: `Span<uint> playerSources = stackalloc uint[objects.Length];`. This proves the current roster pass depends on the full object table, not a fixed helper input. Aggregate proof must exercise the production extracted pipeline and actual snapshot boundary; a copied algorithm would miss regressions. Coordinate the extraction API with orchestration owner; do not add a production seam used only by tests.

Dispositions: A1 named boundary runner accepted via D1; A2 actual roster seam dependency accepted via D4. No auth/network boundary is introduced; diagnostic output contains runtime metadata and aggregate counts, never controller paths or player names.

### 2. Code quality

[P2] (confidence 10/10) `EnemyEffectChecks.cs:69` quote: `check(GC.GetAllocatedBytesForCurrentThread() == before, "Repeated effect observation/lookup allocated in native callbacks");`. Failure lacks the value required to investigate. Capture after-before first, then format delta and runtime/architecture/thread ID/tiering settings outside the interval. Include group name and stable sample index. The runner, not each assertion, prints broad environment information once.

Reuse Measure for future samples after clarifying synchronous vs async contract. Verified callers: existing five PerformanceChecks cases at lines30-49; proposed roster and report cases motivated by this plan. Shared behavior is warmed synchronous timing/allocation; async worker aggregate measurement differs and must not use a current-thread counter across await. Keep that difference explicit instead of a mode-heavy measurement abstraction. Estimated implementation changes: remove 5-15 repeated measurement/diagnostic lines, add 20-45 shared/result lines, net growth 5-40 lines; tests and integration add 60-150 lines estimated. Reliability and coherent units justify growth, not an invented line-saving claim.

Dispositions: Q1 diagnostics D2 accepted; reuse existing helper D4 accepted; reject runner framework and cross-await current-thread measurement.

### 3. Tests and structural coverage

Detected framework: custom .NET10 console executable, Action<bool,string> assertions; Python unittest and PowerShell build-process checks are separate contracts. No test framework migration.

```
Main(args)
 + --probe ---------------------- real read-only device probe [existing]
 + --benchmark ------------------ warmed helper JSON [existing]; aggregate cases [GAP]
 + normal / plugin metadata args
     + named sync group ---------- success [existing]; failure + later group [GAP]
     + named async group --------- success [existing]; throw + later group [GAP]
     + optional metadata group --- valid paths [existing]; invalid/missing args [GAP]
     + aggregate exit ------------ zero if all pass; nonzero if any failure [GAP]
EnemyEffectChecks.Run
 + changed/colliding/rejected ----- ownership behavior [★★★ existing]
 + warm unchanged pair ----------- exact-zero check [★★ existing]
 + nonzero diagnostic ------------ delta/context + fail preserved [GAP]
Roster production pipeline (orchestration dependency)
 + snapshot / eligibility guards -- existing helpers tested; full adapter [GAP]
 + seeds -> exclusion -> boss state -> select -> classify -> publish -> cast -> arena
 |   empty / stable / churn / saturation / reset / hostile ownership [GAP aggregate]
 + framework adapter native reads + territory sheet cache [GAP live/instrumented]
LightingSession.TickAsync
 + cancel -> no device write [★★★ existing]
 + disabled/background -> ReleaseAsync -> write/skip/error/dispose [★★★ existing]
 + discovery -> success/null open/throw/backoff [★★★ existing]
 + each device -> unchanged skip / report create + checksum / sync or async write
     + success / local timeout / caller cancellation / failure + recovery
       [★★★ existing; recovery fixes owned by device lane; aggregate perf GAP]
USER FLOWS
 + CI failure -> see group, exact delta, remaining independent results [GAP integration]
 + controller disconnect/reconnect -> stop and recover [device lane; no hardware proof]
 + user seeks speed evidence -> compare raw paired base/branch distributions [GAP]
```

This is a behavioral branch map, not a line/branch coverage percentage. No denominator covering all repository branches has been measured; do not invent one from assertion totals.

Critical test requirements (extend existing group fixtures where possible):

| Requirement / location | Type | Value card |
|---|---|---|
| CRITICAL runner: first sync or async group throws, later sentinel executes, failed name and exception preserved, process exits nonzero; all pass returns zero; existing mode and plugin args preserved / focused harness test + Program | process integration | Value: protects=complete honest suite result; fails_when=throw aborts remaining groups or failure returns zero; why_new=no group boundary exists; seam=none; no_claim=native crash survival |
| Allocation diagnostics: known allocator in measurement control reports positive exact delta; empty control zero where runtime supports; original ownership criterion unchanged / EnemyEffectChecks and measurement tests | unit/diagnostic | Value: protects=allocation evidence is actionable; fails_when=format allocates inside sample or nonzero is swallowed; why_new=message omits delta; seam=none; no_claim=unknown historical cause solved |
| Roster boundary: empty, one, 8/24-player-like roots, configured max and resize; unchanged vs identity churn; reset on territory/logout/PvP; owner chains and saturated history / orchestration existing tests + PerformanceChecks | integration | Value: protects=benchmark runs real ordered pipeline; fails_when=stage skipped or reset leaks identity; why_new=helpers alone cannot prove ordering; seam=real production extraction; no_claim=Dalamud adapter cost or FPS |
| Device path: 0/1/2/8 devices, USB63/64/BT78, steady skip/refresh/animation, discovery/reconnect, release/reset, failure and cancellation / existing BluetoothChecks + PerformanceChecks | integration | Value: protects=measured work matches real report path; fails_when=fake omits bytes or recovery bypasses write; why_new=performance case absent; seam=ILightingDevices; no_claim=physical HID latency |
| Coverage collection: command exit preserved, repository source mapped, exclusions/counts printed, linked production sources distinguished from loaded plugin assembly, missing collector clearly fails measurement / coverage script | script integration | Value: protects=honest structural denominator; fails_when=missing artifact reported as covered or tests counted as production; why_new=no collector exists; seam=none; no_claim=behavior quality from percentage |

Structural coverage selection: no SDK built-in switch instruments this custom console harness. Do not invent an IL instrumenter or migrate to xUnit merely for collection. Reuse Microsoft's dev-only `dotnet-coverage` console collector with a pinned tool version and explicit local tool path/manifest, separate from shipped dependencies. Before adoption, implementation must run its actual `--help`, verify supported Windows/.NET10 behavior and source/PDB mapping, collect XML/Cobertura, and publish line/branch or block metrics actually supplied by the tool. A format lacking branch data is reported as line/block only; do not relabel blocks as branches. Separate total authored production sources, compiled linked subset, loaded plugin code and unexecuted native/UI adapters. Exclude tests/generated/vendor from production percentage while listing every exclusion and counts. Coverage-instrumented runs are separate from allocation/performance runs because instrumentation perturbs measurements. If acquisition/network/tool support blocks collection, preserve branch matrix and emit `STRUCTURAL_COVERAGE_UNAVAILABLE`, never a numeric coverage claim or fabricated 9+ testing score. Collector version/support proof remains an implementation preflight, not a currently verified command.

Sources: [Microsoft console coverage tool](https://learn.microsoft.com/en-us/dotnet/core/additional-tools/dotnet-coverage), [allocation counter scope](https://learn.microsoft.com/en-us/dotnet/api/system.gc.getallocatedbytesforcurrentthread?view=net-10.0). The counter excludes native allocations and cannot establish total worker allocations when a continuation changes thread.

Tests made obsolete: none. No snapshot tests of method names or assertions that mirror helper internals. No LLM/eval scope. New hardware/game E2E checks are not required to validate a deterministic runner, but remain necessary for hardware/game claims.

Dispositions: five actionable test gaps accepted D1-D4; structural collector D3 accepted as dev-only established tooling. Historical allocation root cause remains an explicit unknown, not a decision or passed fix.

### 4. Performance

[P2] (confidence 10/10) `PerformanceChecks.cs:49` quote: `results.Add(Measure("Cached localized UI labels", 100000, _ => sink += text.Label("Lighting").Length));`. All existing cases stop at helpers. Expand existing JSON schema to include runtime, OS, architecture, CPU, build/commit hash, fixture size, mode, iterations, warmup, raw sample batches, allocation scope and checksum. Keep helper zero-allocation gates; do not set arbitrary zero-allocation targets on device/report paths that allocate reports, linked tokens and task machinery today.

[P2] (confidence 10/10) `LightingSession.cs:108` quote: `foreach (var path in open.Keys.ToArray())`. A real loop has per-tick/device work absent from helpers. Measure complete TickAsync with checksum sink, not report retention. Size matrix: devices0/1/2/8, USB63/64 and BT78, unchanged skip, 1s refresh, changing lighting/rumble, discovery/no discovery, open returning null, unplug/reopen, failed enumeration while healthy, write failure, local deadline/caller cancellation, focus/disable release. Include backend generation/reset and stop-recovery scenarios from device lane. Keep timeout wall time distinct from CPU work; 500ms deadline tests run a bounded single event, not thousands of waits.

Roster matrix: 0/1/32/128 and actual object-table maximum captured from host (do not assume a fixed capacity); player seeds0/8/24; deep owner chains and player summons; boss helper/summon classification; unchanged roster, identity churn, territory reset, no-player/PvP/background guards, saturated history, cast update. Fixtures are constructed before samples. Benchmark the extracted actual pipeline; separately time adapter snapshot and full Update in a consenting instrumented game session if available. The latter includes Dalamud/raw reads, sheet lookups and optional trace/save work absent in Core replay.

Allocation: synchronous helpers/roster use current-thread delta around no-await intervals. Complete async worker scenarios use process-total allocation delta in an isolated process with idle control and explicit background-noise caveat; collect wall time around awaited operation, separate known synchronous completion from forced-async fixture. Never call current-thread delta across await a complete worker total. Report total bytes, bytes/tick, bytes/write and report length. Native allocations and actual driver time remain unmeasured without separate instrumentation.

Comparison protocol: baseline and candidate same SDK/runtime/build mode, CPU power state and fixtures, interleaved paired processes A/B/B/A where practical, at least 10 independent batch samples per steady case [estimated measurement design], warmed each process identically. Publish raw batch values and p50/p95 of batch us/tick, not individual-frame latency; no inferential significance from ten batch means. Include checksum/report counts and reset fixture state per sample. Separate cold discovery/reset/failure distributions from steady values. Preserve failed samples. Initially hard-gate semantic counts, existing zero-allocation helper contracts and deterministic unexpected allocation growth; timing comparisons are advisory until stable repeated baseline variance supports a documented threshold. A >10% timing delta is an investigation flag, not a universal pass/fail claim. Report reproduced-on-main status, missing cases and variance. No speedup is claimed until paired evidence exists.

Dispositions: P1-P2 accepted D4. Device production optimization belongs to its owner; measurement must first expose allocations rather than quietly rewrite that code for a prettier score.

## Binding decisions and decision ledger

Authorization reference: parent task explicitly requests full plan engineering review, auto-select genuinely highest completeness, surface actual ties, and no implementation. Office-hours explicitly skipped. Preamble reported interactive; this does not revoke user's explicit auto-decision authorization. No irreversible action selected.

| ID | Independently selectable choice | Alternatives / completeness | Actual disposition and reason |
|---|---|---|---|
| D1 / A1 | Continue independent groups after a failure | A named sequential boundaries 10/10; B catch each assertion 5/10; C keep abort 3/10 | A auto-selected under parent authorization; preserves valid preconditions within a group and reports later independent results |
| D2 / Q1 | Investigate allocation failure without hiding it | A exact delta + runtime + bounded trace controls 10/10; B retry/tolerance 3/10; C only clearer string 6/10 | A auto-selected; no evidence justifies relaxed assertion or runtime switch |
| D3 / test | Obtain structural evidence for custom console | A established dev-only Microsoft collector + honest denominator 10/10; B manual branch matrix only 7/10; C new IL tool 5/10 | A auto-selected; no runtime dependency, no test framework migration; unavailable collection is explicitly reported |
| D4 / A2 / perf | Complete representative processing measurement | A real roster extraction + real TickAsync/report boundary, paired samples and async allocation scope 10/10; B copy helper algorithm 5/10; C five existing helpers 3/10 | A auto-selected; coordinates production seam with orchestration owner and report fixes with device owner |

No genuine taste tie found. Coverage vendor is a mechanism choice: Microsoft's console support fits existing harness without runtime dependency; a future tool-support failure reopens D3, not an automatic custom replacement. Scope structure choice retains identical features and regression contracts. Approval readiness: PASS for D1-D4 as plan choices under explicit delegated auto-selection; this document grants no implementation authorization.

## Implementation tasks

- [ ] **T1 (P2, human ~3h / Codex ~30m, estimated)** tests harness: implement named sequential sync/async groups, preserved mode/args, aggregate result and process failure proof. Files: `tests/Program.cs`, focused harness checks. Derives A1. Coordinate all other lanes' group registration here.
- [ ] **T2 (P2, human ~2h / Codex ~30m, estimated)** allocation diagnostics: capture strict delta before formatting, context once, known/empty controls and first-failure evidence retention. Files: `tests/EnemyEffectChecks.cs`, existing measurement helper. Derives Q1. No speculative production allocation fix.
- [ ] **T3 (P2, human ~3h / Codex ~40m, estimated)** structural collector: prove local supported invocation, pin dev tool, source exclusions/denominator, publish actual metrics, preserve failure status. Files: tool manifest/coverage script, CI. Derives test gap. Network/support unavailable is a documented gate outcome, never a false percentage.
- [ ] **T4 (P2, human ~4h / Codex ~50m, estimated)** complete loop evidence: expand PerformanceChecks using production roster seam and real session/report APIs, aggregate async-aware allocation, fixture/checksum/reset matrices and raw paired results. Files: `tests/PerformanceChecks.cs`, focused fixtures; CI evidence upload. Derives A2/P1/P2. Depends on orchestration seam and coordinated device contracts, not their final timing results.
- [ ] **T5 (P2, human ~1h / Codex ~20m, estimated)** final evidence reconciliation: run locked build, complete console suite including plugin metadata, original runtime diagnostic, Python/PowerShell packaging checks, coverage and uninstrumented benchmarks; update score receipts and remaining limitations. Derives all test/perf gaps. Depends T1-T4 and production lanes' completed checks.

Effort includes Windows launch/fixture/collector validation, so ratios are lower than pure test generation. These estimates are planning bounds, not measurements.

## Dependencies and work allocation

| Step | Modules | Depends on |
|---|---|---|
| harness T1/T2 | tests runner/allocation | none; sequence shared Main edits |
| coverage T3 | tools + workflow evidence | none; coordinate final shared workflow edit |
| roster seam | production orchestration | orchestration owner's plan/authorization |
| device contracts | production device module and its tests | device owner's plan/authorization |
| full-loop T4 | performance fixtures | real roster seam; device APIs stable |
| final T5 | whole build/release verification | all lane work |

Within this lane, sequential implementation because runner/performance helpers share one executable; no parallelization opportunity. Across the authorized optimization team, harness/coverage can progress while production owners work independently. Shared Program.cs registrations owned here; CI workflow edits sequenced once; Windows full builds/development publication serialized by parent. No new worktree required for read-only probes; implementation may use isolated workspaces as parent assigns.

## Failure modes and visibility

Runner group throws: group prints full failure; independent groups still run; integration test proves nonzero exit. Malformed optional arguments: clear usage/failure rather than index exception; mode behavior regression test. Allocation remains nonzero: preserve sample and fail; no retry. Collector unavailable or XML lacks branch data: explicit unavailable/limited metric and retained manual map. Async continuation changes thread: complete measurement uses total-process delta and declares noise scope. Benchmark fake grows retained reports: use constant-size checksum state and semantic report counts. Roster extraction diverges from real Update: integration owner proves real caller invokes extracted seam; game adapter timing remains separately labeled. Timeout benchmark spends minutes waiting: single bounded failure transition reported apart from steady batches. CI uploads missing after failure: artifacts upload under always() but package gate remains failure. Zero new critical silent failures in proposed paths, contingent on listed tests being implemented.

## NOT in scope

- No custom test framework, benchmark framework, IL instrumenter, or runtime dependency; existing boundaries suffice.
- No allocation-threshold relaxation, automatic retries, disabled tiering or forced-GC passing policy; source cause has not been proved.
- No real game FPS, physical stop-watchdog duration, HID transport latency or arbitrary native unload safety claim; requires hardware/game evidence.
- No additional PRs, commits, push, build publication or production edits during review.

## Completion and score evidence

Scope accepted as-is; reality12/12 applicable rows emitted with3 unverified premises; Lake Score4/4 complete options selected. Architecture 2 issues; code quality1; tests5 actionable gap families; performance2. All mapped to T1-T5, none implemented. Testing/performance quality9+ is a target, not a measured current result. Candidate testing9 requires deterministic process failures, strict diagnostic allocation contract, actual structural metrics with honest exclusions, and unchanged protocol regressions. Candidate performance9 requires paired real-pipeline and device/report results with pathological/reset cases and async allocation scope; actual in-game score remains unmeasured. Passing 514,925 repeated checks is not that many independent cases.

Outside voice: preflight enabled (`codex_reviews=unset`), Claude CLI unavailable (`CODEX_MODE: not_installed`); fallback Plan-agent type/TaskStop/background timer contract absent, so OUTSIDE_VOICE_UNAVAILABLE. No replacement general-purpose reviewer or external clean credit. Repair: install/authenticate Claude CLI and rerun the existing skill's outside step. Brain digest lookup hit EROFS for global project state; context/decision/learning searches returned no usable entries. Preamble update check UNKNOWN; no upgrade performed. Telemetry off.

QA test plan and required Review Log use the skill-required global state path through delegated review-workflow escalation. jq is not installed, so task JSONL aggregation is not persisted under the skill's explicit fallback; complete T1-T5 tasks are retained above. Required QA plan persisted at the skill state project directory; review log and review-read executed. Task JSONL skipped because jq is unavailable. No new TODO entries; zero unresolved plan decisions. Sequential lane with parallel production owners as documented. No durable code learning beyond existing reliable ProcessStartInfo helper; retain runtime/probe receipts rather than claim root cause.

Suppressed findings: runtime/tiering as specific cause of intermittent failure (confidence3/10), not established by current bounded runs; no causal stack/type capture exists. No speculative production fix proposed.

## Implementation addendum: audited build dependencies

Parent authorized T1-T5 implementation and this narrow CI addition. Review prior to edits: actual public Dalamud.NET.Sdk15.0.0 `Sdk/SdkPackageVersions.props` pins SDK/packager15.0.0; `Sdk/Sdk.props:68-69` adds DalamudPackager and DotNet.ReproducibleBuilds1.2.39. Parent official NuGet feed receipt at `work/optimization-dependency-audit/results.json` finds no records for these three IDs in base updated2026-11-04T11:49:31Z/update2026-11-05T05:49:37Z. This is a dated observation, not future audit proof.

[P2] (confidence9/10) build workflow currently relies on restore warnings; unavailable audit feed can still allow a green package. Add CI built-in NuGet restore audit for all transitive dependencies at low severity, fail closed on NU1900-NU1905. Use evaluated PackageReference items plus pinned SDK package from project Sdk attribute to generate an isolated audit input; audit SDK itself, not only its imported package references. Audit the actual production locked graph with isolated intermediate output first, then the explicit resolver-SDK graph, so future locked transitive versions cannot be replaced by a freshly resolved audit graph. No custom vulnerability range evaluator or repository audit disabling. Preserve normal local build behavior; CI audit gate runs separately before packaging. Use explicit official audit source and retain command output. Tests: current clean feed succeeds where reachable; a temporary unreachable audit source must fail with NU1900 and nonzero status; audit project records the exact evaluated versions. This adds one tools script and a workflow gate, no runtime dependency or new service. Coverage collector pinned18.12.0 selected from official NuGet stable index; local isolated tool install preflight still required. D5 auto-select fail-closed native audit10/10 over warning-only5/10; no taste tie. Approval readiness extends PASS to D5 under parent explicit implementation/audit authorization.

## Implementation outcome

Parent subsequently authorized T1-T5 implementation. T1-T4 are implemented and verified within their stated scope; T5 compiled-plugin collection is complete, while shared build/package/fresh overall review remain the parent gate. Detailed raw-derived outcomes and limits are in `docs/optimization/testing-performance-results.md`. Final collector run:54/54groups,0skips,515,934assertions; authored source union3,132/6,014mappedlines(52.078%),60/61authoredfilesmapped,2,882zero-hitlines. The earlier~95% linked subset is not whole-plugin coverage. Embedded portable symbols worked. Original-runtime linked run53/53groups with1explicitplugin-scope skip,515,778assertions. Native NuGet audit fails closed on actual unavailable-feed NU1900/NU1905; no online success is claimed. Full actual framework timing is now opt-in and ready but no in-game samples were collected. No arbitrary score is assigned.

## Follow-up no-op acknowledgement addendum

Architecture/code review: reuse LightingSession.Acknowledge and return before
its dictionary lookup when both clearing flags are false. Reserve always creates
at least one owned field; a false/false acknowledgement has no state to retire.
Keep all true-clear, admission, cancellation and foreground logic unchanged.
This one-line existing-helper guard is the complete choice; no ownership-map
redesign or CRC scope is justified. Parent reviewed the proof and approved it.

Test/performance review: compare private 166ff17 before/after copies using the
same existing device/sink fixture, default runtime, ABBA/BAAB processes, all
chronological batches and matching counts/checksums/allocation medians. Separate
JIT-event runs from timing evidence. Run critical Bluetooth recovery/admission
and stereo checks, then the full integration build. Results support only a small
USB lookup saving, no consistent Bluetooth steady-state gain; large pooled shifts
coincide with runtime recompilation. See verification.md and
work/independent-review/ack-profile/. Formal new startup reported update/lease
failures; no new persisted full-skill/external-review success is claimed. The
original reviewed device/performance plans and parent approval remain in force.

## Focused engineering review: callback allocation measurement boundary

Selected target remains this plan. Parent explicitly authorized this narrow scope after independent `final_quality_review` approval; office-hours skipped by prior user direction. Real plan-eng-review startup reran (session1-1793877836-ea53f565): protocol1, interactive, telemetryoff; update statusUNKNOWN/leaseunavailable, no upgrade performed. Existing product/context, ordinary console framework and outside-review availability are unchanged; no setup preference was inferred from startup. Claude CLI remains unavailable; parent independent native review is recorded as native approval, not external-provider completion.

Scope Challenge: accepted as-is. Four changed files estimated: this plan, EnemyEffectChecks.cs, Program.cs and one new test helper/controls file; one new class, no runtime service/dependency. Reuse .NET MethodImpl attributes and existing console group runner. Reject a test framework/benchmark dependency or global runtime flags. No new TODO or PR split. The original failure records remain red. This is a test contract repair, not a claim that OSR allocated the observed2,792/6,192 bytes.

Architecture finding [P2] (confidence10/10): `work/testing-performance-causal/full-child-jit-default.txt:1587` contains `call CORINFO_HELP_PATCHPOINT` inside the two reads; the emitted Run Tier1-OSR entry061A lies in the10,000-step measured loop. Intended contract is callback managed allocations; existing interval also executes harness profiling/OSR. Native code keeps diagnostic allocations after the ending counter, so moving message formatting is not the selected remedy. Repair with a single non-inlined, immediately optimized test-only measurement method; it invokes a pre-created ordinary non-loop Action, keeping actual cache methods/default production callback tiering unchanged. Disposition: approved D6, parent exact task authorization after independent review.

Code quality: no additional finding. One small test helper owns warmup/counters/iterations and behavior controls; no copying cache logic. The callback remains exactly Observe then Resolve. Delegate creation, fixture setup, strings and assertions stay outside counter reads. Added helper/control source estimated60lines, removed loop source5lines, replaced with pre-created callback+one call estimated4lines; net growth is deliberate proof, not a claimed extraction saving. No runtime file touched.

Test review:
```
existing fixture -> pre-created Action -> MeasureAllocations
 + warmup2000 [unchanged]
 + read current-thread bytes ->10000 Action.Invoke calls ->read bytes
 + return delta ->strict0 assertion [unchanged]
controls ->same helper
 + noop ->0
 + escaping newbyte[37] ->64bytes/step on x64
 + real changing cache association ->positive allocation +published effect identity
actual Main ->prior async/device groups ->ThreadPool continuation ->EnemyEffectChecks fixture ->AllocationChecks controls
 + ordinary source-only run [explicit compiled-plugin skip]
 + malformed --plugin child ->only expected metadata failure if other groups pass
```
Value: protects=callback allocation contract excludes loop JIT work; fails_when=helper allocates or swallows callback bytes; why_new=native disassembly proved harness scope mismatch; seam=test-only helper; no_claim=historical allocation cause solved.
Value: protects=counter detects real escaping allocation; fails_when=known64bytes/step ignored; why_new=positive detector control absent; seam=none; no_claim=all runtime contexts proven deterministic.
Value: protects=real cache association allocation stays visible; fails_when=helper hides newEntry allocation; why_new=no callback-allocation control exercised counter; seam=none; no_claim=physical/native allocations measured.
Required regression: retain2,000warmup/10,000measured steps and exactzero, production methods unannotated/default tiering, no retries or failed-sample discard. Initial D6 order put controls immediately after EnemyEffectChecks; D7 supersedes that placement and moves them after every original group, following CheckSfxSession and before Complete. JIT verification must show no measurement-wrapper PATCHPOINT and an indirect Action call boundary, with actual callback/Observe/Resolve code retaining normal tiering. Run private full async child and process guard once, preserve every result; parent performs final SDK/coverage/package verification afterward.

Performance review: this is correctness of measurement scope, not an application speed optimization. AggressiveOptimization|NoInlining applies only to the helper; no global tiering/PGO change and no warmup inflation. The NoInlining boundary prevents caller-propagated callback specialization; verify actual generated code rather than trusting the attribute alone. Remaining normal runtime effects inside the callback still belong to its measured contract. No overall performance number or automatic9+ score follows.

D6 decision ledger: current value=large mixed fixture with measured-loop profiler/OSR; A=test-only optimized/non-inlined measurement boundary+ordinary pre-created Action+strict controls (completeness10/10, selected); B=leave known scope mismatch (5/10); C=global tiering-off/tolerances (3/10, rejected). Root's exact authorization and independent native approval select A; no genuine tie. Setup/navigation grants no additional remedy. Historical allocation attribution remains unknown, not an unresolved policy choice. Approval readiness: PASS D6 under exact parent implementation authorization.

T6 (P2, human~2h/Codex~20min estimated): add one helper and its three controls, replace only fixture warmup/measurement loops with callback invocation, register controls. T7 (P2, human~1h/Codex~15min estimated): private build, actual JIT boundary verification, one full async source run and one complete process-guard invocation; retain outputs and failures. T7 depends T6; sequential shared tests module, no parallelization or shared runtime publication. Failure modes: helper throws/null callback propagates; real callback allocation fails strictassert; native code unexpectedly inlines/removes boundary invalidates verification; historical failures stay disclosed. No silent untested failure accepted; zero new critical silent gaps. Outside coverage unavailable; native independent review approved with stated conditions. Review adds1architecture finding,0code-quality findings,3new control gaps and0application-performance findings; all mapped to T6/T7.

Focused amendment verification: private Release build succeeded with0warnings. First source/Main invocation after the order-preserving repair passed54/54executedgroups with1explicitcompiled-plugin-scope skip and515,781assertions; target31assertions precedes controls3assertions. One process-guard invocation passed sync/async/pass and malformedmetadata-onlyexit1 contracts. Native helper code is FullOpts69bytes, has two indirect Action.Invoke loops and ordered counter reads, with no PATCHPOINT or COUNTPROFILE. Ordinary callback lambda, Observe and Resolve were emitted Tier0; no production attributes changed. No retries performed. Historical2,792/6,192-byte causes remain unresolved; this verification establishes the repaired measurement boundary and positive detector controls only. Raw outputs/code/hashes: `work/allocation-boundary-repair/`. Parent SDK/compiled-plugin/coverage checks follow after freeze.

## Consistent regression allocation-window scope amendment

Fresh retained evidence: final coverage (`work/optimization-final/coverage-run.txt`) passed EnemyEffectChecks and controls but failed BluetoothStereoChecks.Configure with24,624bytes, leaving54/55groups and red XML. The preceding uninstrumented55-group run passed. Neither coverage instrumentation nor OSR is assigned as this delta's cause. Original red log/XML are copied under `work/allocation-window-audit/`.

Architecture: apply the already approved D6 callback-only measurement boundary consistently to raw strict-zero regression windows. One existing MeasureAllocations(Action,int,int) helper remains the only attributed method. No new overload/framework, runtime annotation, global flag, tolerance or retry policy. Timed microbench/roster windows intentionally measure whole-loop CPU/runtime work and remain unchanged; process-total async measurement is a separate scope.

Code quality: preserve every original callback operation, warmup count/body, measured iteration count, index/timestamp sequence, counter and final semantic assertion. Where warmup and measured steps differ, keep the exact warmup outside the counter and use helperwarmup0 with a pre-created measured Action; never add measured mutations/counter increments to warmup. Unsafe/ref-struct lifetimes remain within their existing owning scope; no assertions, new arrays or delegate construction enter the measured callback. Existing closure/pointer captures need compile and lifetime review.

Test review: migrate BluetoothStereo's Observe/TakePeak and Configure windows with1,000existing warmup and10,000measured calls each. Preserve both snapshot ReferenceEquals assertions. Other strict windows are mapped in `docs/optimization/allocation-window-map.md`; root explicitly assigns disjoint owners and reviews every diff. Move AllocationChecks controls to the very end of Program, after existing CheckSfxSession and before Complete, preserving every old group's order and avoiding control pre-warm of any old target. ROOT owns Program from this point. All assertions remain strictzero plus their original semantic counters/read counts/state assertions.

Performance review: this corrects regression-test scope only. It does not redefine timed benchmark work or claim any application speedup. Coverage runs remain distinct and can still fail if instrumented callbacks allocate. Preserve fresh failed runs even if changed tests later pass; no causal/determinism/9+ claim follows from migration.

D7: parent exact authorization selects consistent callback scope across regression windows (10/10) over waiting for each raw loop to fail before repairing the same known contract mismatch (5/10). No new causal claim or independently pending runtime policy is bundled. Architecture finding: the existing raw harness loops share the measured-loop scope defect; confidence9/10 structural equivalence, with actual machine-code proof established for D6 rather than asserted for every old site. Accepted regressions: exact operation/count/index/counter/unsafe-lifetime preservation. Approval readiness: PASS D7 under parent explicit scope/ownership instructions; no taste tie. Sequential owner edits, root full integration/coverage after all freeze; no extra cold probe batches.

D7 readiness receipt: all32raw strict windows are migrated by their assigned owners; source search now retains only canonical helper and the two intentional timed current-thread windows, plus the distinct async process-total pair. Controls are last (CheckSfxSession then AllocationChecks then Complete), with all original groups in the original order. BluetoothStereo-only Release verification passed81assertions, then controls3, with0compilerwarnings; rawreceipt `work/allocation-window-audit/focused-run.txt`. B8/Q12/ROOT10 migrations are frozen for root integration review. The preserved red coverage54/55 log/XML is not overwritten or causally reclassified. Parent private full/metadata/process-guard/coverage verification is the next gate; no new overall green or deterministic claim is made here.

## Focused review: opt-in update-tail attribution

The selected review target remains this plan. The user observed 25,895 updates, with a mean of 0.057 ms and a maximum of 24.489 ms. These are elapsed callback intervals, not CPU time or evidence of a particular cause. The independent Sol source review is retained at `work/independent-review/update-tail/focused-plan.md`. The parent approved its minimal instrumentation scope and the API below.

Office-hours was skipped as explicitly directed, and the complete approved scope was selected automatically. The real plan-eng-review startup used session `1-1793881467-e12dd9d2`: protocol 1, interactive mode and telemetry off. Update status was UNKNOWN, and the workflow lease was unavailable. No upgrade or successful external review is claimed. Existing context and reuse decisions remain binding.

Scope Challenge: accepted as-is. This lane owns only the new `Core/UpdateTiming.cs`, new `tests/UpdateTimingChecks.cs` and this addendum. The parent owns Plugin, UI and Program integration. The design uses one narrow counter owner, a mutable frame value and readonly snapshot values. It adds no generic profiling service, framework, dependency, reset or GC event collector. It reuses Stopwatch, Interlocked, the existing opt-in checkbox/runtime state and the canonical allocation helper. The real timestamp delegate is cached once; fixed phase arrays are allocated once per owner. Disabled and capped paths read no clock.

Architecture finding [P2] (confidence: 9/10): the existing Plugin.Update records only whole-callback count, total and maximum. UpdateCore includes recording coordination, observer initialization/result pickup, player publication, roster/arena processing, screen effects, recording sampling and save/guard work. A single maximum cannot localize a tail or show its frequency. Add count, total and maximum for these seven fixed phases, plus whole-update counts at or above 1 ms, 5 ms and 16.7 ms. Counters accumulate until reload, with at most 1 million admitted frames. No attribution to storage, roster processing or GC is justified yet. Disposition: D8 approved by the parent after independent source review.

Code quality: keep UpdateTiming internal and narrow. Begin(bool) reserves a slot before reading the clock; reservations are separate from completed frames. Frame.Next records the previous phase. Complete records the active phase and whole interval in finally, then becomes inert on the same frame variable. The parent must pass that variable by ref into UpdateCore; copying it duplicates mutable completion state and is outside the contract.

Snapshots are readonly values whose numeric fields are read atomically. They are not coherent snapshots of one frame. Do not allocate snapshot arrays or sum independent phase maxima. Threshold ticks use exact ceiling at construction, with frequency ratios 1/1000, 1/200 and 167/10000. Count means completed frames. The production clock is Stopwatch; no generic exceptional-clock service, logging or I/O path is introduced.

Test review:

```
Begin(false/capped) -> default Frame -> Next/Complete inert [zero clock reads]
Begin(true) -> reserve slot -> timestamp -> RecordingCoordination phase
  Next(Observers/PlayerPublication/RosterArena/ScreenEffects/RecordingSample/SaveGuard)
    -> timestamp -> record previous phase -> advance the same frame
  finally Complete -> timestamp -> record active phase + whole + thresholds -> inert
ReadTotal/ReadPhase -> readonly numeric snapshots [no clock reads or new arrays]
```

Use an injected monotonic clock, frequency and sample limit to test: zero disabled clock reads; early return; thrown-body accounting in finally with the original exception preserved; skipped phases; completed versus admitted counts; cap behavior; idempotent completion of the same variable; threshold equality and just-below cases at 1 MHz; fractional-frequency ceiling; snapshot values; and clock-free reads. Enabled and disabled seven-phase callbacks use the canonical allocation helper to verify zero allocation. Clock injection supports arithmetic tests, not a benchmark-only production seam.

Value: protects=timing lifecycle and disabled overhead; fails_when=disabled timing reads the clock or exit loses an interval; why_new=existing counters lack phase coverage; seam=injected clock; no_claim=the 24 ms cause is known.

Value: protects=threshold frequencies and completed-frame counts; fails_when=rounding or cap admission overcounts; why_new=new diagnostic contract; seam=injected clock; no_claim=snapshots describe one coherent frame.

Value: protects=zero allocation on the hot path; fails_when=frames or snapshots allocate on the heap; why_new=new instrumentation; seam=none; no_claim=game or driver timing is validated.

Performance review: compare enabled seven-phase timing against no-op and disabled paths in bounded experiments. Preserve raw batches and account for wrapper/runtime noise. A complete seven-phase path reads eight timestamps, and elapsed totals include instrumentation overhead. The disabled path performs no clock reads, allocations or logging per frame. Independent maxima must not be combined into an imaginary frame. Counters remain cumulative until reload; zero count means no samples collected. The cause of the 24.489 ms interval remains unknown. Live sampling with fixed settings follows integration.

D8 approved API:

- `UpdateTiming.Phase`: a fixed enum with seven values.
- `Begin(bool)`: returns a mutable Frame with `Next(Phase)` and `Complete()`.
- `ReadTotal()`: returns readonly totals for completed count, total ticks, maximum ticks and counts at or above 1 ms, 5 ms and 16.7 ms.
- `ReadPhase(Phase)`: returns readonly count, total and maximum.
- The default constructor uses the cached Stopwatch delegate/frequency and a 1 million sample cap. An internal constructor accepts a clock, frequency and limit for deterministic tests.

The parent explicitly approved the API, by-ref requirement, completed-count distinction and ceiling arithmetic. The complete option (10/10) was selected over guessing at an optimization from one maximum (3/10); there was no taste tie. Approval readiness: PASS for D8 under the parent's exact authorization. This does not approve reset, a GC collector or production algorithm changes.

T8: implement the counter owner, frame, snapshots and deterministic tests (estimated human effort: 3 hours; Codex: 30 minutes). T9: verify private tests, zero allocation and no-op overhead (estimated human effort: 1 hour; Codex: 15 minutes). Parent integration and live sampling depend on both tasks. Only the parent edits Plugin, UI and Program.

Failure coverage: exceptions record the active phase in finally; early setup records only visited phases; UI reads expose independently atomic values; capped frames read no clock; by-ref integration prevents accidental frame copies. No silent critical gap is accepted. This review identifies one architecture finding, the test requirements above and one performance measurement gap; no additional code-quality finding. Independent native review and parent authorization do not count as external-provider coverage. The original user sample is measured evidence, thresholds and cap are approved constants, and live phase/recurrence distributions remain unverified.

D8 verification receipt: the private Release build passed with zero warnings, and all 17 deterministic assertions passed. These include injected-clock and real-clock zero-allocation checks using the canonical helper. The cause of the user's 24.489 ms interval remains unknown.

The separate no-op experiment ran on Windows 10.0.26100, .NET 10.0.14 x64, with 10 interleaved batches of 10,000 calls each and 2,000 warmup calls per batch. Median times were 0.000545 µs for an empty delegate, 0.017145 µs for disabled seven-boundary calls and 0.344005 µs for enabled seven-phase calls. The observed incremental median was approximately 0.32686 µs. The first empty-loop batch, 0.88647 µs, was retained. These are warm synthetic batch observations with JIT/scheduling noise, not measured game-frame overhead or causal attribution. The benchmark completed 120,000 enabled intervals and zero disabled intervals. All 30 raw observations and counters are retained in `work/update-tail-counter-checks/results.txt`.

Source readback confirms the parent's integration uses Begin followed by `try UpdateCore(framework, ref timing)` and `finally timing.Complete()`, including the fixed phase and early-setup boundaries. The parent reports a current Release result of 56/56 groups passed with zero skips, including the 17 new timing assertions; independent review found no defect. This lane made no Plugin, UI or Program edits. Final metadata, coverage, packaging and live verification remain the parent's responsibility. Live phase data is uncollected until the user enables diagnostics. No reset or GC-overlap collector was added.

The fresh outside-review preflight found no installed Claude CLI, and the required bounded native fallback tools are absent. External coverage remains unavailable, not clean. QA requirements and the required focused review log were persisted separately. JSONL task aggregation remains unavailable because jq is absent, following the skill's documented fallback.

## GSTACK REVIEW REPORT

| Review | Trigger | Why | Runs | Status | Findings |
|---|---|---|---|---|---|
| CEO Review | none | routine verification scope | 0 | not requested | none |
| Outside Review | Claude Code preflight | independent second opinion | 1 attempt | unavailable | CLI absent; required native fallback contract absent |
| Eng Review | /plan-eng-review | architecture, tests, performance | 3 current | ISSUES OPEN | Update-tail amendment:5 mapped requirements/findings; prior scope retained;0 critical silent gaps |
| Design Review | none | no UI scope | 0 | not applicable | none |
| DX Review | none | console diagnostics covered here | 0 | not requested | none |

**OUTSIDE COVERAGE:** host Codex; provider claude-code; phase plan-review; unavailable, no outside findings or agreement claimed.
**VERDICT:** mapped implementation plan with ISSUES OPEN; eng review required after implementation. Implementation receipts are linked above; fresh overall review remains the parent gate, with no predetermined9+ claim.
NO UNRESOLVED DECISIONS
