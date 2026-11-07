# Bounded allocation investigation

## Current follow-up: 0.19.8 (2026-11-07)

The review's failing coverage result (6,168 bytes in `BossNpcSoundChecks`) and
its later reproductions remain preserved under `artifacts/review-20261107` and
`artifacts/review-followup-20261107`. A normal Release pass did not clear them.
Startup allocation tracing and JIT diagnostics changed which failures occurred;
their passing runs are not retrospective allocation attribution.

A separate failing probe pinned an anchor immediately before the counter window
and copied the following heap bytes afterward. With a 7,816-byte counter delta,
the next object had the runtime allocation-context filler layout: base 24 bytes,
length 7,816, total 7,840. It had no managed RuntimeType. This supports an unused
context/accounting interaction, not a lookup-created managed object. However,
CoreCLR's normal `fix_allocation_context` accounting should remain neutral, so
the exact counter transition is still **unresolved**. The unsafe heap reader is
an ignored diagnostic artifact, not part of the plugin or test suite.

Explicit delegate preparation allocated outside one probe's window but did not
solve the complete coverage run. Static instrumentation alone also failed a later
run. Neither is claimed as a causal fix; no threshold, original warmup or
measured iteration count was relaxed.

The final test-only harness instead reserves a bounded no-GC interval around
each serial allocation measurement, after its existing warmup. This isolates
the callback contract from allocation-context retirement. Reservation failure,
budget exhaustion or invalidated-region cleanup fails rather than falling back
or retrying. The region is process-wide; the requested 16 MiB is not a total
process-memory cap. `disallowFullBlockingGC: true` prevents the full-blocking
fallback, not all GC-related setup work. Setup is outside the byte counters.
Production code, default tiering and plugin GC behavior are unchanged.

Both final normal and coverage suites pass 60/60 groups and 519,872 assertions.
Six detector controls retain zero, cold 64-byte first execution, recurring
640,000-byte execution and real cache-allocation sensitivity, and verify cleanup
after callback and combined callback/cleanup failures. A private mutant adding
an escaping byte[37] per lookup fails at exactly 640,000 bytes, with all other
59 groups passing and exit 1 propagated. This validates the measurement contract;
it does not establish zero allocations on every unconstrained production path.

Coverage now uses the collector's supported static mode on shadow copies. It
retains the same source exclusions and a populated compiled-plugin gate. Each
run has separate reports and original-input before/after hashes; 49 process
checks verify failure propagation and preservation of evidence. The final
source-union coverage is 4,287/7,107 mapped lines (60.32%), with 2,330 compiled
plugin lines hit (32.78%); neither is whole-plugin or game-native coverage.

Primary references: [CoreCLR counter implementation](https://github.com/dotnet/runtime/blob/v10.0.14/src/coreclr/vm/comsysthread.cpp),
[allocation-context handling](https://github.com/dotnet/runtime/blob/v10.0.14/src/coreclr/gc/gc.cpp),
[no-GC region contract](https://learn.microsoft.com/en-us/dotnet/api/system.gc.trystartnogcregion?view=net-10.0),
[Microsoft static instrumentation](https://github.com/microsoft/codecoverage/blob/main/docs/instrumentation.md).

## Historical investigation after 166ff17

Investigated after commit `166ff17` on Windows10.0.26100 x64, portable SDK10.0.403 / runtime .NET10.0.14. No production or test acceptance logic changed during this investigation.

The historical6,192-byte allocation delta and the later malformed-argument guard failure remain **unattributed**. The guard's original child output was not retained, so those two failures cannot be assumed to share a cause. This report closes a bounded diagnostic investigation, not the underlying uncertainty or a deterministic-test claim.

## What changed in the evidence

The earlier EventListener trace only sampled allocation ticks and did not capture the failed window. A6KB delta could therefore escape it completely. Its lack of a matching event was not evidence of no allocation.

Inspection of the matching runtime source found a more precise diagnostic: enabling both high- and low-allocation keywords **at process startup** records every object allocation. The runtime selects its slow allocation helper at startup; enabling an ordinary EventListener afterward cannot enable this behavior. Type names and allocation stacks can be collected with those events. These are version-specific diagnostic controls, not a production setting. [Runtime startup and sampling implementation](https://github.com/dotnet/runtime/blob/v10.0.14/src/coreclr/vm/eventtrace.cpp), [keyword definitions](https://github.com/dotnet/runtime/blob/v10.0.14/src/coreclr/vm/ClrEtwAll.man).

The isolated startup configuration was:

```text
DOTNET_EnableEventPipe=1
DOTNET_EventPipeConfig=Microsoft-Windows-DotNETRuntime:0x432A0019:5,Bcef-Allocation-Window:0xFFFFFFFFFFFFFFFF:5
DOTNET_EventPipeCircularMB=128
DOTNET_EventPipeOutputPath=<unique local trace path>
```

A diagnostic-only EventSource brackets the existing allocation interval. Its begin event occurs before the first counter read, and its end event occurs after the second read. The reader matches events to the target's actual OS thread and interval. These marker bounds are a slight superset of the counter bounds; the positive and empty controls validate that marker emission did not introduce additional in-window allocations in the observed runs.

The official Microsoft TraceEvent4.0.0 reader parses the startup trace offline and resolves types and call stacks. It was downloaded from the official NuGet flat-container feed and used only under ignored `work/`; it adds no shipped dependency. Original runtime sources, pinned package metadata, reader/target sources, binaries and hashes are retained locally.

## Results

| Experiment | Bound and result | Interpretation |
|---|---|---|
| Allocation-padding + forced full GC |40 windows across5 padding sizes; every counter delta0 | Ordinary forced collection/context retirement did not reproduce the anomaly in this control; it does not rule out every GC/runtime interaction |
| Uninstrumented known heap allocation |40 windows, each counter delta64 | Counter detects the expected aligned allocation |
| Uninstrumented volatile-store control |40 windows, all0 | No-op control remained allocation-free in this process |
| Uninstrumented unchanged cache |40 windows, all0 | Named current path did not reproduce here; not a claim about all cold runs |
| Startup all-object positive control |40 windows, each counter64; exactly one `System.Byte[]` event per window on the matching OS thread; stack `Program.Allocate` → `Program.Main`; zero lost events | Trace can attribute a small real allocation by type/stack/thread, below the old sampling threshold |
| Startup unchanged-cache control |40 windows; counter0, zero allocation events inside;8,560 events outside; zero lost events | Interval matching works without confusing ordinary startup allocations with cache work |
| Exact `EnemyEffectChecks` fixture, with diagnostic window markers |8 predeclared cold processes, stopping immediately on any failure; each31 assertions passed, measured counter0 and zero object-allocation events inside; each trace lost0 events | No failed window was captured; tracing changes allocation helpers and timing, so this cannot establish the historical cause or replace uninstrumented test results |

The byte-array event reports object size61 for `new byte[37]`; the allocation counter reports its aligned64 bytes. Those values describe different sizes and agree after alignment. Reader output preserves both.

No failed sample was discarded or rerun for a passing verdict. The8-process trace search is complete. Further identical cold-run batches would add weak evidence while profiling may prevent the timing condition; none are proposed here.

## Source and compiled-path verification

The matching runtime's counter reads the current thread's allocation context as `alloc_bytes + alloc_bytes_uoh - (alloc_limit - alloc_ptr)`. This justified testing context retirement as one hypothesis; the40 forced-GC controls did not reproduce it. It does not justify assigning the anomaly to GC bookkeeping. [Runtime counter implementation](https://github.com/dotnet/runtime/blob/v10.0.14/src/coreclr/vm/comutilnative.cpp).

Compiled IL from the diagnostic assembly confirms:

- `EnemyEffectOwnership.Observe` has one `newobj`, the `Entry` constructor at IL0089. The unchanged listener/instance/caster/tag branch returns at IL0078 before it. Invalid inputs take the existing `Forget` path.
- `Resolve` has no `newobj`, `newarr` or `box` instruction; it returns a value-type tag or default. Called framework/helper code can still have first-use/runtime effects, so local IL alone is not proof about every runtime allocation.
- The exact fixture retains its2,000 warmup pairs,10,000 measured pairs and strict-zero criterion. The measured Observe/Resolve loop contains no `await`, assertion callback or diagnostic-string formatting. Type/stack/thread attribution was validated on the positive control and no allocation was observed in the eight traced fixture windows.

The metadata reader only inspects IL; it does not rewrite or instrument production assemblies. No cache redesign or extra warmup follows from this evidence.

## Minimal recommendation

Keep the committed strict assertion, exact delta/runtime/thread context and independent named group boundary. Keep full child output in process-guard failures. Do not add retries, thresholds, tiering-off passes, forced-GC passing policies or inflated warmup.

If another uninstrumented failure occurs, retain that first failed run and its complete child output. A separate, explicitly labeled startup trace can then investigate a reproduction with type/stack/window correlation; it must never turn the original failure green. GC generation counters and OS-thread identity may be added outside the measured interval in a future test-only diagnostic if needed. No further maintained code is required now without a captured causal finding.

## Receipts

All raw receipts are under `work/testing-performance-causal/`:

- `--gc.txt`, `--positive.txt`, `--empty.txt`, `--cache.txt`: bounded counter/padding/GC controls.
- `positive.nettrace`, `positive-trace-parsed.txt`:40 small allocations with resolved type and call stack.
- `cache.nettrace`, `cache-trace-parsed.txt`:40 empty observed windows.
- `actual-0` through `actual-7` `.nettrace`, `-target.txt` and `-parsed.txt`: every cold fixture outcome, including event-loss counts.
- `actual-ownership-il.txt`: decoded compiled methods and allocation branch.
- `sha256.txt`: relevant authored source, diagnostic binaries and official reader DLL hashes.
- `run.ps1`, `run-trace.ps1`, `run-actual.ps1`, `inspect.ps1`: exact local commands; EventPipe variables are removed before reader execution.

The previous uninstrumented failures remain recorded under `work/testing-performance-probe/` and the final malformed-process diagnostic under `work/testing-performance-current/`. No actual game or controller behavior was exercised by these isolated cache probes.

## Follow-up: fresh2,792-byte failure in the asynchronous full child

A subsequent authoritative run supplied new evidence: `work/optimization-followup/verify-results.txt` records a proper54/54 compiled-plugin pass, followed by a malformed-argument child with two failed groups. `EnemyEffectChecks` reported **2,792 bytes** on managed thread15, .NET10.0.14; the other failure was the expected missing metadata arguments. The strict assertion's immediate failure is now known for this new child. Its allocation type/stack remains unknown. This is **not** recovery of the earlier lost child output, and2,792 must not be conflated with the separate6,192-byte standalone deltas.

The full child differs from the earlier standalone fixture: `Program.Main` awaits earlier asynchronous Bluetooth/device checks and reaches the allocation test on a ThreadPool continuation. A private linked-source copy preserved that complete Main/async sequence and the `--plugin` malformed arguments, with only diagnostic markers and counter/thread metadata added to `EnemyEffectChecks`. Original2,000/10,000 loop bounds and strict-zero acceptance were unchanged. All diagnostic preparation, GC-count reads and output occurred outside the two allocation-counter reads.

Exactly two all-object startup traces were run:

| Full child | Measured continuation | Allocation delta | GC0/1/2 during interval | Allocation events inside / outside | Lost events |
|---|---|---:|---|---|---:|
|0 | Managed15, OS37484, ThreadPool=true |0 |0/0/0 |0 /350,631 |0 |
|1 | Managed18, OS37292, ThreadPool=true |0 |0/0/0 |0 /351,860 |0 |

Each retained the expected metadata-only failure,53/54 groups and exit1. Neither reproduced the extra allocation failure. These instrumented outcomes do not turn the original2,792-byte failure green. Startup tracing changes the allocation helper and timing; both traces are bounded negative evidence, not a causal explanation.

One additional **untraced** process resumed after `Task.Delay`, then alternated10 unchanged-cache and10 volatile-store no-op windows. Every window executed on managed5/OS12000, ThreadPool=true, with counter0 and GC0/0/0. This demonstrates the simpler continuation control did not reproduce it; it does not replicate the full suite's prior state or establish a general property of ThreadPool execution.

The investigation stops at these declared bounds. No production or maintained test acceptance code changed. Retain the fresh failed child's strict failure and capture full diagnostics on any future occurrence; do not add retries, relaxed thresholds, special passing runtime settings or extra warmup. Thread/GC metadata outside the window remains a useful test-only diagnostic recommendation, while cause attribution still requires an actual failed allocation window.

Additional receipts under `work/testing-performance-causal/`:

- `authoritative-2792-verify-results.txt` and `authoritative-2792-checks/`: retained fresh failure output and its current compiled checks, before later shared output can replace them.
- `full-child-0` and `full-child-1` `.nettrace`, `-target.txt`, `-parsed.txt`: complete bounded full-child outcomes and actual-thread allocation correlation. Each trace is approximately33MB.
- `full-child/`: private source project and inserted markers; `run-full-child.ps1` is the exact2-run command.
- `pool-control.txt`, `pool-control/`, `run-pool-control.ps1`: the one untraced continuation comparison process.
- `followup-2792-sha256.txt`: authoritative failure/binary, private instrumented fixture and raw trace hashes.

## Native JIT code: one default-tiering full child

A different bounded diagnostic inspected the actual current `artifacts/optimization-followup/checks/LightingChecks.dll --plugin` child with targeted `DOTNET_JitDisasm` and `DOTNET_JitStdOutFile`. No EventPipe allocation mode, tiering/PGO override, source marker, warmup change or acceptance change was used. Exactly one process ran; it passed the31 `EnemyEffectChecks` assertions and retained only the expected malformed metadata failure,53/54 groups and exit1. That outcome does not explain or clear the earlier2,792-byte failure.

Four native code versions were emitted and archived:

| Method | Observed version | Relevant finding |
|---|---|---|
| `EnemyEffectOwnership.Observe` | Tier0 | Unchanged association returns at native block IG08, offset0176; allocation helper `CORINFO_HELP_NEWSFAST` is in changed-association block IG09 at017F |
| `EnemyEffectOwnership.Resolve` | Tier0 | No allocation helper is emitted; getters/default returns and range-failure path remain |
| `EnemyEffectChecks.Run` | Instrumented Tier0 | First counter read follows warmup and precedes the measured loop; loop includes profiling and patchpoint helpers; second read follows the10,000-iteration comparison |
| `EnemyEffectChecks.Run` | Tier1-OSR, entry IL061A | Actual on-stack replacement entry is inside the measured loop; the previous counter value is retained in register R14, and the ending counter read follows the remaining iterations |

The measured caller therefore includes **runtime profiling/OSR work in addition to the intended Observe/Resolve calls**. In Tier0, measured-loop block IG51 calls `CORINFO_HELP_PATCHPOINT` with entry061A; a Tier1-OSR version for exactly that entry was produced. This establishes the extra measurement scope, not that the helper allocated the observed2,792 or6,192 bytes.

Native instruction ordering does **not** show diagnostic allocation moved into the measured interval. Tier0 calls the ending counter and subtracts the baseline before constructing the interpolated diagnostic. Tier1-OSR similarly reads/subtracts at IG04/offset00AE; `ArrayPool<char>.Rent`, formatting, the emitted `System.Version` allocation and diagnostic-string construction occur afterward. No ending-counter hoist ahead of the measured loop was observed in these versions.

Only Tier0 Observe/Resolve bodies were emitted. Both the Tier0 and observed OSR measured loops call them rather than inlining them. No claim is made about unobserved Tier1 versions, every called framework helper, or a different failing process's code. Disassembly itself can affect timing, and the failed window did not recur.

The upstream BenchmarkDotNet measurement reader explicitly bypasses Tier0 to avoid unexpected allocations at its measurement boundary. This corroborates the need to examine harness tier transitions; it is **not causal proof for this failure**. No such annotation or wrapper was adopted solely to obtain a passing run. [BenchmarkDotNet measurement reader](https://github.com/dotnet/BenchmarkDotNet/blob/master/src/BenchmarkDotNet/Engines/GcStats.cs).

If further causal work is warranted, target managed heap allocations made during the default runtime patchpoint/JIT transition on the actual measurement thread, ideally using a native debugger/allocator breakpoint without enabling all-object startup helpers. Retain the original failed run regardless of any diagnostic outcome. A canonical optimized measurement-boundary wrapper is a candidate only after evidence attributes contamination to the harness/runtime; it must not silently change production callback optimization or replace causal proof with green repetitions. No further cold invocations were performed for this report.

Receipts: `full-child-jit-default.txt` retains all emitted versions; `jit-Observe-Tier0.asm`, `jit-Resolve-Tier0.asm`, `jit-Run-InstrumentedTier0.asm`, `jit-Run-Tier1OSR.asm` split them without changing instructions. `full-child-jit-target.txt` contains complete child output, `run-jit-disasm.ps1` the exact command, `jit-target-sha256.txt` the inspected target hash, and `jit-evidence-sha256.txt` the disassembly/upstream-source receipt hashes. All remain under `work/testing-performance-causal/`.


## Accepted test measurement scope repair

Independent review and parent approval subsequently selected a narrow repair for the confirmed harness scope mismatch, without attributing the historical byte deltas. `tests/AllocationChecks.cs` now owns one test-only `MeasureAllocations(Action,int,int)` method with AggressiveOptimization|NoInlining. A pre-created ordinary, non-loop callback invokes the real Observe/Resolve methods. Production code and callback tiering remain default;2,000warmup calls,10,000measured calls and the strict-zero assertion remain unchanged. The initial repair put controls immediately after EnemyEffectChecks. The subsequent consistency amendment moves controls to the very end, after CheckSfxSession/before Complete, preserving every original group's order and avoiding control pre-warm of any old target.

One private Release validation passed54/54executed source groups with1explicit compiled-plugin skip and515,781assertions. The target passed31assertions, then its new controls passed3: no-op0, escaping byte[37] exactly64bytes/step on x64, and real changing cache associations allocate while retaining the current identity. One process-guard invocation passed its sync/async/pass and malformed-argument contracts. No validation was retried for a passing verdict.

Actual native code verifies the new helper is FullOpts,69bytes, with an indirect Action.Invoke call in each loop, ordered counter reads and no PATCHPOINT/COUNTPROFILE. The target lambda, Observe and Resolve were emitted Tier0; no production method annotation or global tiering/PGO flag was applied. This repairs what the test measures and validates the detector, **not** why the earlier2,792/6,192 bytes were reported or that every future callback execution is deterministic. Those original failed runs remain authoritative.

The focused architecture/code/test/performance review, D6 approval and regression contracts are in `testing-performance-plan.md`. Raw build, codegen, source/Main, process-guard and hash receipts are in `work/allocation-boundary-repair/`. Parent SDK metadata, coverage and release validation follow separately.


## Consistent regression-window scope

A later retained coverage run failed BluetoothStereoChecks.Configure with24,624bytes while EnemyEffectChecks and controls passed;54/55groups and red XML were preserved. Neither OSR nor coverage instrumentation is assigned as the cause. Parent approved applying the same known callback-scope repair consistently, with every original operation, warmup/body/count, measured index/timestamp sequence, result counter and final assertion preserved.

The complete map and ownership/readiness receipts are in `allocation-window-map.md`:32raw strict sites migrated across four disjoint owners, plus already-migrated EnemyEffectChecks. Only the canonical helper and two deliberately unchanged timed benchmark windows retain raw current-thread read pairs; the async benchmark retains its process-total pair. Existing warmups remain external when their body differs or measured counters/indexes must begin fresh; no additional warmup is inserted. AllocationChecks controls are last, after every preexisting group. No new helper overload/framework/global tiering setting or relaxed assertion was added.

The2BluetoothStereo migrations alone were checked in an isolated Release executable:81existing assertions passed, followed by3existing detector controls; build0warnings. ROOT's full integration/compiled-plugin/coverage verification remains authoritative after all files freeze. The preserved coverage failure is under `work/allocation-window-audit/coverage-failure.*`, with hashes and baseline source contexts alongside it. This is consistent measurement scope, not causal attribution or a determinism claim.
