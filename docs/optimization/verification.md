# Optimization verification — 2026-11-05

Baseline: `9354236`. This is a reliability/maintainability change, not a new
feature release; package identity and version remain 0.18.0.0.

## Parent integration results

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

## Coverage and performance

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
bounded aggregate counters and no per-frame log or allocation. **No current
in-game samples have been collected.**

## Audit, independent review and remaining limits

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

Remaining validation: current in-game settings/dialog/CJK rendering, consent,
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
./tools/audit-dependencies.ps1
./tools/measure-coverage.ps1 -PluginPath ./artifacts/plugin/DualSenseLighting.dll -DalamudLibPath <API-15-directory>
python ./tools/summarize-coverage.py ./artifacts/coverage/coverage.cobertura.xml --output ./artifacts/coverage/summary.json --require-plugin
python -m unittest discover -s tests -p "test_*.py" -v
dotnet ./artifacts/checks/LightingChecks.dll --benchmark
python ./tools/package-release.py
```

Coverage and performance run separately. Keep raw failing output as well as the
summary; the CI verification-evidence artifact is uploaded even on failure.
