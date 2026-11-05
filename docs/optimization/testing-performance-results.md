# Testing and performance verification

Verified 2026-11-05 against the private optimization candidate. Source planning and decisions remain in `testing-performance-plan.md`. No in-game FPS or physical HID latency was measured.

## Test execution

- Current candidate collector run: **54/54 executed check groups passed, zero skips, 515,934 successful assertions**. Both compiled-plugin metadata/behavior checks and linked-source checks ran. Assertions include repeated loops and are not independent test cases.
- Latest isolated linked-source run on original .NET10.0.1: **53/53 executed groups passed, one explicit compiled-plugin skip, 515,778 assertions**. The portable .NET10.0.14 run also passed. The missing compiled-plugin scope is printed as `SKIP`, not an executed pass.
- Process checks: injected synchronous and asynchronous group failures both retain the later sentinel and return exit1; passing groups return0. Malformed `--plugin` arguments report the exact usage error, run later independent groups, and return failure.
- Windows PowerShell5 build process checks: **107 assertions passed**, including the prior Unicode/quoting/regional cases. The one additional assertion verifies the extracted shared helper parses. The actual launcher is reused by build, audit, collector and process tests.
- Python packaging/release/coverage-summary tests: **44 passed**. Coverage-summary tests enforce source-line deduplication, test-source exclusion, unmapped-source reporting and refusal to report coverage when no production sources map.

Raw receipts: `work/testing-performance-current/{build,checks,checks-original,runner-process,build-process,final-coverage-run}.txt`. The final compiled candidate and checks SHA256 values are in `final-candidate-sha256.txt`; scripts and harness hashes are in `tooling-sha256.txt`.

Final parent-pass caveat: the main54/54 compiled-plugin run passed, but its subsequent malformed-argument process probe failed an expectation. That guard did not retain child stdout, so the original failure cannot be assigned to an allocation, timeout or other group. One subsequent diagnostic invocation with retained stdout produced exactly the expected usage failure, completed the final SFX group and returned exit1, with every strict guard condition satisfied. This does **not** explain the earlier failure or establish deterministic green behavior. Failure guards now include captured child output. One post-change validation of the complete process-check script passed, retaining its result in `work/testing-performance-current/final-process-guard-result.txt`; the original failure remains unexplained. No automatic retries or relaxed expectations were added.

## Structural coverage

Microsoft `dotnet-coverage` **18.12.0** was installed from the official NuGet package into isolated development tooling; no runtime package dependency was added. Downloaded package SHA256: `9801be478cc1da9b4c911563c7549bcf018b12a525dfa56e9edc33cc60ab66ef`.

Final collection used `artifacts/optimization-candidate/checks/LightingChecks.dll`, `--plugin artifacts/optimization-candidate/plugin/DualSenseLighting.dll` and installed Dalamud15.0.3.8 references. Embedded portable symbols worked; no separate symbol build was needed. Dynamic collection included both modules and kept every existing strict allocation assertion enabled.

| Scope | Covered / mapped unique source lines | Meaning |
|---|---:|---|
| Linked-source `LightingChecks` module | 2,736 / 2,871 | Production copies compiled into the console harness; not the whole plugin |
| Actual `DualSenseLighting` module | 540 / 6,014 | Compiled-plugin behavior reached by reflection/integration fixtures; most Core behavior also executes in the linked copy |
| Union of authored production source locations | **3,132 / 6,014 (52.078%)** | Each source line counted once across classes/modules, using maximum observed hit count |

The union contains **2,882 zero-hit mapped lines**. **60/61 authored production files** map to reported lines; `Core/ArenaEffectSource.cs` is unmapped and excluded from any numeric claim, not treated as covered. Tests, bin/obj, generated `EnemyBossCatalog.cs` and third-party modules are excluded. Eligible module line counts above are deduplicated from the collector's source rows; raw module line-rate attributes can differ because of generated/duplicate sequence points. Raw collector branch totals are retained with their own scope; no source-filtered branch percentage is inferred.

`Plugin.cs`, real HID/native entry paths and several observers have zero-hit lines. UI rendering and actual game adapters were not executed. This result is structural evidence, not a claim that half the behaviors are correct or that all plugin code is measured. Earlier ~95% linked-only figures were a smaller denominator and must not be presented as whole-plugin coverage.

Raw XML, version/help output and source/module summary: `work/testing-performance-current/final-coverage/`. CI requires the compiled-plugin module to expose eligible source lines and fails if the collector/report is unavailable.

## Allocation inconsistency

The historical allocation failure remains **unattributed**. Bounded probes reproduced a **6,192-byte first-window delta** intermittently on portable .NET10.0.14, including one Resolve-only window and one SpinWait-only control; SpinWait first-use is a confound. Ten volatile-store no-op processes and ten additional combined-operation processes gave zero deltas. Forty positive-control windows each reported exactly64 bytes. The sampled runtime-event run did not contain a failed window and therefore supplies no causal stack/type attribution. Original-runtime full-suite probes did not reproduce it.

The assertion remains exact-zero with unchanged warmup/iteration counts. It now records exact delta and runtime/thread context before any diagnostic string is formatted. `EnemyEffectChecks` is an independent named group, so its failure preserves later group results. No retry, tolerance, forced-GC policy, tiering switch or extra warmup hides a failure.

## Paired device/report measurements

Frozen baseline source and compiled checks were saved before device changes under `work/testing-performance-baseline/`. The same frozen new benchmark harness compiled against baseline/current Core and UI sources. Four processes ran A/B/B/A on Windows10.0.26100, .NET10.0.14 x64; host CPU reported AMD Ryzen AI9 HX370,24 logical processors. Source/harness hashes are retained in `work/testing-performance-current/perf-input-sha256.txt`.

Each steady device case has20 baseline and20 candidate batch observations, normally1,000 ticks per batch. Fixtures are re-created per sample, warmup is200 ticks, bytes are consumed into a bounded checksum and reports are not retained. Complete async measurements use process-total managed allocation with an idle control, not current-thread deltas across `await`. Native allocations/driver latency are excluded. Batch timing is advisory and includes scheduling/background noise; it is not individual game-frame p95.

| Case | Baseline median us/tick | Candidate median us/tick | Baseline / candidate bytes per tick | Writes per tick |
|---|---:|---:|---:|---:|
| 1 USB63, unchanged + refresh | 0.354 | 0.377 | 58.8 / 58.8 | 0.034 / 0.034 |
| 8 USB63, animation | 4.457 | 5.493 | 2,095.808 / 2,095.808 | 8 / 8 |
| 1 BT78, animation | 6.783 | 7.130 | 312.464 / 312.464 | 1 / 1 |
| 8 BT78, animation | 18.104 | 43.952 | 2,221.904 / 2,221.904 | 8 / 8 |
| 2 BT78, reconnect event | 5.250 | 20.700 | 1,352 / 1,928 | 2 / 4 |

The reconnect case intentionally performs additional stop/clear-before-restore writes; it is not an equal-workload speed comparison. Enumeration failure likewise retains and refreshes healthy devices instead of dropping them. The8-Bluetooth animation fixture also shows substantial CPU timing overhead despite unchanged write counts and allocations; candidate batch p95 is63.837us versus42.260us baseline. This is reported openly, with timing noise and synthetic scope retained; no general speedup is claimed. The matrix includes0/1/2/8 devices, USB63/64 and BT78, refresh/animation, forced asynchronous completion, discovery failure, null open, write failure, reconnect, focus/disable release, caller cancellation and local deadline. Deadline wall time is separate from ordinary steady-state CPU work.

Measurements initially exposed an extra72 managed bytes per successful report from an `async Task<byte[]>` helper. The device owner changed its return to `ValueTask<byte[]>`; the second complete A/B/B/A run removed that regression and restored the baseline bytes/tick in the steady cases above. Pre-correction raw candidate output is retained in `before-valuetask/`.

Raw four-process outputs: baseline `perf-0.json`/`perf-3.json`, candidate `perf-1.json`/`perf-2.json`; combined raw-derived table: `work/testing-performance-current/paired-summary.json`. All five existing helper allocation contracts remained zero in these processes.

## Roster and real framework timing

The candidate benchmark executes the actual production sequence: snapshot copy, `ExcludePlayerOwned`, encounter `Update`, extracted `SelectRoster`, and `PlayerSoundOwnership.SetEnemies`. It validates publication and uses real identity retention, classification and ownership logic. Stable0/1/32/128/1,024-candidate cases reported zero managed allocations in every measured batch. Identity churn and territory resets report their actual allocations; player-owned chains and bounded-history saturation are distinct fixtures.

This synthetic sequence excludes Dalamud property/native reads, cast/arena adapters and the rest of `Plugin.Update`. The baseline predates the callable roster seam, so no pre-change whole-roster timing or speedup is claimed.1,024 is a synthetic fixture size, not a verified maximum object-table size.

The production owner added default-off, local diagnostic timing around the **entire actual `Plugin.Update`**, retaining bounded count/total/max measurements without per-frame log output. It can collect real framework timing in a game session. **No in-game samples were collected here**; zero samples display “not collected”.

## Dependency audit and remaining limits

CI now runs native NuGet auditing for the actual locked production dependency graph, with isolated intermediate files, low severity and all transitive packages. A separate exact-version audit input includes the resolver SDK itself, which is not a normal project PackageReference. Versions come from the production Sdk attribute and evaluated SDK imports: Dalamud.NET.Sdk15.0.0, DalamudPackager15.0.0, DotNet.ReproducibleBuilds1.2.39.

NU1900-NU1905 fail the audit gate. A real local run against the official source failed with NU1900/NU1905 because Windows could not load its service index, and the process returned failure. This verifies fail-closed behavior, not a successful online audit. The parent's separately retained official feed snapshot found no records for the three pinned IDs (base updated2026-11-04, incremental update2026-11-05); it is a dated observation, not a replacement for future CI audit success. No repository audit disabling or custom advisory-version parser was introduced.

Final shared build, package validation and fresh overall code-quality assessment belong to the parent verification pass. These measurements do not establish physical stop-watchdog duration, arbitrary hook-unload interleavings, actual game performance or an automatic9+ score.
