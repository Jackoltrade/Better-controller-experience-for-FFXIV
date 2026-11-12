# Further performance investigation: v0.20.0

The implemented changes in this investigation are included in the **v0.20.1
release candidate**. Version numbers, hashes and measurements below describe the
original v0.20.0 investigation and remain historical receipts. See the
[current QA record](../QA-REPORT.md#current-verification-v0201) for the fresh
v0.20.1 build results, dependency-audit results and remaining gameplay limits.

Investigated 2026-11-12 against `f78499c2e3950d3827e0d5fb93b1434787aea221`
(`codex/release-0.20.0`). The initial investigation used isolated prototypes.
The user subsequently authorized implementation of all feasible improvements.
Those changes are now implemented locally; see the implementation receipt below.

The improvements preserve the current sampling rates and enabled features.
The strongest candidates were vectorized PCM validation/RMS,
batched arena pool header reads, and one final checksum for newly created
Bluetooth reports. None establishes a whole-game FPS improvement yet.

## Measured candidates

Release probes ran on an AMD Ryzen AI 9 HX 370, 24 logical processors,
Windows build 26100 and .NET 10.0.14 x64. This is **not low-spec test hardware**.
Each comparison uses seven alternating warmed batches in the same process.
Values below are median batch CPU times per operation, not individual-frame
percentiles. Full arrays are retained in the local evidence directory.

| Candidate | Current | Prototype | Reduction in measured stage | Execution path |
| --- | ---: | ---: | ---: | --- |
| RMS, 2,048 floats, default SIMD | 1.719 us | 0.369 us | 78.6% | Audio callback |
| RMS, 4,096 floats, default SIMD | 3.345 us | 0.421 us | 87.4% | Audio callback |
| RMS, 2,048 floats, AVX2 disabled | 1.604 us | 0.409 us | 74.5% | Audio callback |
| RMS, 4,096 floats, AVX2 disabled | 3.212 us | 0.767 us | 76.1% | Audio callback |
| Arena pool header copying, 256 slots | 85.866 us | 2.154 us | 97.5% | Framework thread |
| Bluetooth lighting report creation | 1.862 us | 0.774 us | 58.5% | Output worker |
| Bluetooth rumble report creation | 1.252 us | 0.562 us | 55.2% | Output worker |
| Bluetooth lighting + rumble report creation | 2.437 us | 0.573 us | 76.5% | Output worker |

SIMD and arena read stages allocated zero warmed managed bytes in both paths.
Report creation retained its existing allocation: 104 bytes per Bluetooth report
and 88 bytes per 63-byte USB report on this runtime. Allocation measurements are
separate from throughput timing. The prototypes do not reduce report allocation.

### 1. Vectorize the remaining complete PCM scan

`Core/SfxLoudness.cs:56` validates and sums every sample with a scalar loop.
v0.20.0 already shares this result between eligible lighting and haptic paths;
the remaining scan is still performed for each admitted block. Its total cost
depends on the number of admitted voices, which was not measured in-game here.

The prototype uses existing `System.Numerics.Vector` operations, widens samples
to double before multiplying, and retains double accumulation. It rejects NaN,
infinity and values outside [-16, 16], including invalid values in the tail.
Small blocks and machines without hardware acceleration use the existing scalar
method. It introduces no package or CPU-specific intrinsic dependency. The
[.NET SIMD guidance](https://learn.microsoft.com/en-us/dotnet/standard/simd) and
[Vector.Widen API](https://learn.microsoft.com/en-us/dotnet/api/system.numerics.vector.widen?view=net-10.0)
describe the portable operations used here.

Validation covered empty, short and unaligned lengths, 2,048/4,096-float blocks,
random normal and tiny values, silence, exact +/-16 boundaries, and five invalid
values injected at **every position** of 17-, 2,048- and 4,096-float blocks.
The random/reference comparison allowed at most one float ULP and observed zero
ULPs of difference in all three execution modes. Different reduction order is
not a proof of bit-identical output for every possible input.

Default execution used eight float lanes; disabling AVX2 selected four lanes.
Disabling all hardware intrinsics exercised the scalar fallback: 1.551/1.517 us
for 2,048 floats and 3.125/3.137 us for 4,096. These small differences are noise;
there is no demonstrated acceleration in scalar mode. Restricting CPU features
on this host does not reproduce an older processor's memory or scheduling costs.

Before shipping, run the existing shared-level and compiled Render checks against
both build variants, including stale generations and original PCM preservation.
The research probe calls the production scalar method, but does not exercise
the SIMD candidate through installed native audio hooks.

### 2. Batch arena sound-pool header reads

`Windows/ArenaSoundEffects.cs:240` performs one `ReadProcessMemory` call per
208-byte pool slot, across 256 slots. `Update` admits this work at up to 20 Hz
during active boss routing when a nonempty arena sound catalog is available.
Every 100 ms it can also walk the arena instance map on that framework update.

The prototype copies the same 53,248 bytes into one reusable buffer with one
native call, then examines the slot headers. It uses allocated fixture memory
owned by the probe, the real Windows API, and identical slot flags/counts. It
does **not** execute the complete production arena discovery method, resource
decoding, map walk, source matching or live identity validation.

This is the most direct measured opportunity for reducing periodic game-thread
work. The observed saving is about **0.084 ms on an eligible poll**, or about
1.67 ms of CPU time per second at 20 polls/s on this host. Those are stage-cost
calculations, not measured reductions in frame time or whole-process CPU use.

Keep `TrySource`'s live reservation, resource, playback and instance checks, and
the final world/layout checks. A copied header snapshot grants no authority to
accept a sound after its slot has been reused. Preserve the existing poll rate.
Use one framework-owned buffer, with no pooling framework or background native
object traversal. A failed or partial bulk read must clear published sources
through the current error path. Windows requires the complete requested region
to be readable; see [ReadProcessMemory](https://learn.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-readprocessmemory).

Required integration cases include unreadable/partial pool copies, pool reuse
after the copy, territory/layout changes, duplicate playback IDs, ambiguous
instances, and unchanged source positions. No live arena compatibility claim is
made by this owned-buffer experiment.

### 3. Finalize a newly built Bluetooth report once

`Core/DualSenseReport.cs:64` initializes a checksum, calls `Apply` which validates
and rewrites it, and rewrites it again if motors are included. That is three CRC
passes for lighting and four for lighting plus rumble; rumble alone uses two.

For a new report whose buffer/header were just created locally, populate the
lighting and motor fields and write one checksum at the end. Extract and reuse
the existing common-lighting field writer so the eventual implementation does
not duplicate protocol rules. Keep external `Apply` validation and firmware
report validation unchanged. No different CRC algorithm is needed.

The prototype matched **all output bytes in 8,192 cases** across USB lengths
48/63/64 and Bluetooth 78, optional lighting/motors, all sequence byte values,
immediate player LEDs and both rumble variants. Every resulting Bluetooth CRC
was also validated by the existing production checker. This is a small,
low-risk worker optimization; its whole-game effect should be modest at 30 Hz.

## Smaller improvements and their practical limits

| Finding | Feasible change | Evidence and expected scope |
| --- | --- | --- |
| Quiet camera observations still materialize `ScreenEffectSample` before `SetScreenEffects` rejects them | After updating motion/filter state, publish null directly when the measurement has no effect or nonpositive level | Production-linked probe measured 48 bytes per quiet camera frame. Avoiding that object would save 2,880 bytes/s at 60 updates/s. Active sample + publication uses 80 bytes/frame. This is a small GC reduction. Preserve diagnostic recording behavior. |
| Enemy casts are inspected whenever capture exists, even when enemy publication is disallowed | Gate the cast-only loop in `Plugin.cs:614` by its actual enemy consumer; preserve diagnostic consumers | Static finding. `SetEnemies(..., allowed: false)` already empties ownership, so these reads have no eligible enemy recipient. Keep duty history and the roster scan. Extend `IdleRosterChecks` to cover disallowed live capture before implementing. |
| Arena polling constructs fresh lists/sets/arrays and formats native addresses for published sources | Reuse framework-owned scratch collections; compile address formatting out of production when only diagnostics consumes it | Static finding; allocation savings unmeasured. Continue publishing immutable results to the mixer. Avoid resource-handle caches without lifetime checks. |
| The existing roster replay has zero warmed allocation in stable 0/1/32/128/1,024-candidate fixtures | Keep the existing reusable arrays and identity retention | Re-ran the actual existing roster fixture. It excludes Dalamud property reads, casts, model traversal and arena discovery, so it cannot rule out framework-thread hotspots. |

The listener snapshot allocated 64 bytes per valid publication in the probe.
Retain its immutable publication until profiling justifies a more complicated
replacement. Reusing a mutable pose object across the framework and audio
threads would introduce torn observations. Its allocation alone is not evidence
of a meaningful FPS problem.

## Changes to defer

- Reducing the 33 ms worker rate: this can change XInput/Bluetooth onset, stop,
  screen-feedback and lighting timing. The measured improvements need no rate cut.
- Throttling or pausing the entire duty roster: history deliberately survives
  focus loss and feature changes. Missing enemies before a boss starts can change
  later trash/mechanic classification. Separate history from output-only work
  before attempting any cadence change.
- Caching sound-ID attribution across callbacks: `PlayerSoundOwnership.Inspect`
  searches a bounded 256-slot pool and validates generations and native reuse.
  An index may be worth profiling later, but lifetime, collision and concurrency
  handling are much riskier than the measured candidates.
- Moving ObjectTable/native scene traversal to a new worker, adding a thread per
  voice, or queuing audio blocks: these complicate lifetimes and can add latency.
- Reusing an asynchronous HID report buffer without proving completion ownership,
  or removing cancellation/deadline allocation: neither is justified by the small
  report allocation, and both can weaken cleanup.

## Implementation and gameplay verification order

First implement one-final-checksum construction, disallowed cast-read gating and
quiet-frame allocation avoidance as small independent changes. Then implement
the measured RMS fast path with the scalar fallback. Batch arena headers in a
separate change with the native failure/reuse tests above. Keep user feature
defaults, sampling rates and public diagnostic exclusion intact.

For low-spec acceptance, compare plugin-disabled and enabled runs on an actual
target PC using the same area, camera, graphics settings, FPS cap and scripted
actions. Alternate A/B/B/A runs and record frame-time median, p95/p99 and long
frame counts, together with CPU model, controller/backend and enabled features.
Include idle city, ordinary combat, an arena boss and a crowded encounter; test
screen-only, combat-only and combined SFX lighting/haptics. Measure background
CPU and audio-thread contention as well as framework updates. Use development
timing only in explicitly identified measurements, since the public DLL omits it.

Behavior checks must retain native footsteps, speaker/trigger routing, stereo
direction and distance, first skill/arena reactions, targeting-warning rejection,
foreground release, reconnect and settings transitions. There is no present
evidence that lower fidelity or fewer enabled features are necessary.

## Local receipts and reproduction

All research files are under ignored `work/performance-followup-v0200/`:

- `Probe.csproj`, `Program.cs`, `Candidates.cs`: linked production Core sources,
  production `NativeHapticOutput` without `BCEF_DIAGNOSTICS`, the existing roster
  fixture, and isolated candidate functions. No plugin or device is loaded.
- `results-default.json`: final run including the native arena header experiment.
- `results-sse.json`, `results-scalar.json`: earlier audio/report probes, before
  adding the independent arena experiment; the candidate algorithms are identical.
- `evidence.json`: source/input hashes, result hashes and scope.

From Windows PowerShell in the repository root, run:

```powershell
& '.\work\performance-followup-v0200\run.ps1'
& '.\work\performance-followup-v0200\run.ps1' -NoAvx2 -SkipBuild
& '.\work\performance-followup-v0200\run.ps1' -NoIntrinsics -SkipBuild
```

The script uses the existing local .NET SDK cache, builds only the isolated
probe, and writes results in that directory. Each of the three completed modes
passed **42,254 explicit checks**, including the report and RMS checks above;
the existing roster fixture also completed its internal guards. The final probe
build had zero warnings/errors. Native arena reads assert the same flag count
on every measured/warmup scan. No timing threshold decides pass/fail.

The initial research did not rerun the public plugin's full suite, since it
changed only documentation and ignored probes. Its results above remain
prototype evidence; the implementation was verified separately as follows.

## Implementation receipt: 2026-11-12

All six feasible changes are implemented: SIMD RMS with scalar fallback, single
final Bluetooth CRC, bulk arena pool headers, arena scratch/publication reuse
and production address-formatting removal, quiet production camera publication,
and disallowed cast-read gating. Listener snapshot publication and the changes
listed under "Changes to defer" retain their existing behavior.

`SetLighting` is shared by new-report construction and incoming-report editing.
The arena snapshot is only a discovery input: `TrySource` still reads current
native slot/playback/instance identity, and `Update` still rechecks world/layout
state before publication. Both existing arena poll intervals are unchanged.
Scratch storage is owned exclusively by the framework thread; the mixer still
receives an immutable array. Equal publications retain their current array.

Two initial runs exposed assumptions in the expanded uninitialized-plugin roster
fixture: it depended on the process's live foreground window, and its development
variant lacked recording state. Foreground is now explicitly passed into roster
orchestration from the existing caller check, making enabled/background tests
deterministic. The fixture now initializes the development recording owner.
Both failures and the subsequent complete run are retained, without relaxing
assertions or allocation limits.

### Functional checks

| Mode | Production | Development |
| --- | --- | --- |
| Default CPU features | 62/62 groups, 593,239 assertions | 62/62 groups, 593,317 assertions |
| AVX2 disabled | 62/62 groups, 593,239 assertions | 62/62 groups, 593,317 assertions |
| All hardware intrinsics disabled | 62/62 groups, 593,239 assertions | 62/62 groups, 593,317 assertions |

All six runs have zero skips and zero failed groups. Repeated assertions are not
independent test cases. Both compiled DLLs also pass the existing optimized x64,
API 15 and production/development boundary verifier. The Python suite passes
50 tests and the generated boss catalog matches its CSV.

New checks cover scalar-reference RMS accuracy, whole-block invalid-sample
rejection, unchanged source PCM and zero warmed RMS allocation, along with
USB/Bluetooth bytes and corrupted incoming CRC rejection. The compiled audio
checks execute the RMS cases against each actual plugin DLL. The expanded cast
fixture covers enabled/disabled enemies, present/absent capture and foreground/
background while retaining regular-enemy history. The real compiled camera reader
has zero warmed quiet-frame allocation in production and retains development
quiet-frame samples.

The compiled full arena fixture uses owned native memory and the existing
special-playback resolver fixture. It checks correct playback/position, retained
publication/scratch identities, live slot/resource/sound-number/playback/instance
revalidation, nonfinite positions, a partially unreadable pool via `VirtualProtect`,
scene mismatch, duplicate IDs, ambiguous instances, cyclic maps and recovery.
Production address strings are empty; development strings remain present.

### Actual compiled-method comparison

The committed v0.20.0 source was frozen before implementation and built into
separate baseline DLLs. The final optimized **production** DLL and frozen baseline
were loaded in separate assembly contexts and compared in one process. Seven
alternating warmed batches ran on the same Ryzen AI 9 host described above.

| Actual compiled method | Baseline us/call | Implemented us/call | Baseline / implemented bytes/call |
| --- | ---: | ---: | ---: |
| PCM RMS, 2,048 floats | 1.269 | 0.190 | 0 / 0 |
| PCM RMS, 4,096 floats | 2.546 | 0.372 | 0 / 0 |
| Bluetooth lighting creation | 2.975 | 0.517 | 104 / 104 |
| Bluetooth rumble creation | 0.966 | 0.489 | 104 / 104 |
| Bluetooth lighting + rumble creation | 3.077 | 0.492 | 104 / 104 |
| Arena `Candidates`, inactive 256-slot pool | 70.074 | 1.953 | 32 / 0 |

The last row executes the actual private candidate method, including its Windows
reads and header filtering, with an owned inactive pool. It excludes active
resource decoding, playback matching, scene maps and publication. The existing
helper/worker/device benchmark also completed and all helper zero-allocation
contracts passed. Batch values include scheduling/JIT noise and must not be
treated as whole-plugin or game-frame speedups.

Current receipts are under ignored `work/performance-implementation-v0200/`:
`current/run-20261112-002904.log`, `sse/`, `scalar/`, `paired-performance.json`,
`worker-benchmarks.json` and `implementation-evidence.json`. Frozen source and
baseline DLLs are under `baseline/`. At the user's request, the verified production
DLL and its verification metadata are staged at the existing
`artifacts/plugin/DualSenseLighting.dll` location; the duplicate
`current/production/` staging directory was removed. The development verification
receipt remains under `current/development/`. Local reproduction scripts now reuse
the standard `artifacts/plugin/` and `artifacts/dev-plugin/` locations.
The scripts are `verify.ps1` and `benchmark.ps1` in the same directory.

Local normal and Japanese test ZIPs are under `local-package/`. The standard
packager verified current source/DLL fingerprints and package boundaries; both
ZIPs contain exactly the tested production DLL. These local artifacts have not
been uploaded or published.

Package restore reports NU1900 because the official vulnerability feed could not
be reached. Dependencies and the lockfile are unchanged; a fresh online audit
remains unverified. The gstack check launcher could not run because Bun is absent;
the repository's Windows SDK/check runner and verifier were invoked directly.
No live game frame capture, low-spec hardware run or release publication was
performed. The user-authorized overwrite of the existing local test DLL is
recorded above. Version remains 0.20.0.
