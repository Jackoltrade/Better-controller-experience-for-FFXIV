# Reliability and maintainability optimization

Baseline: `9354236` (2026-11-05). The original review rated the project **7.3/10**.
Scores are engineering judgments, not assertion counts or coverage percentages.
The final independent assessment is **9.1/10** under the unchanged weights.
The original findings are addressed, and the requested current in-game smoke
checks pass. See [the independent review](final-review.md),
[verification and limits](verification.md), and the
[requirement-by-requirement completion audit](completion-audit.md).

## Findings and changes

| Finding | Result |
| --- | --- |
| Bluetooth stop intent lost after uncertain writes or discovery failure | Confirmed with the original adversarial probe. Ownership now survives handle loss until acknowledged cleanup; failed enumeration retains healthy handles. Ownership capacity is reserved before output and never evicts unresolved cleanup. |
| Separate left/right publication can split one centered sample | Confirmed. Stereo peaks now use one atomic pair and retired accumulator epochs. |
| Failed autosaves retry each framework frame | Confirmed. Debounced saves retain dirty revisions, apply a five-second retry floor, serialize storage callbacks and expose a deduplicated error. Explicit consent saves still propagate failure. |
| Allocation failure aborts later checks | Confirmed. Named independent groups continue, retain original diagnostics and return failure. Exact allocation checks include the delta and runtime context; their thresholds are unchanged. |
| Allocation inconsistency | Reproduced intermittent 6,192-byte first-window allocations on .NET 10.0.14. Cause remains unproven. A demonstrated harness-scope flaw is repaired using a shared test measurement boundary; unchanged strict checks and positive controls now pass. No retry-to-green policy was added. |
| Plugin mixes rendering, recording and lifecycle | SettingsUi owns rendering/windows; DiagnosticRecordingCoordinator owns recording intents, save ordering and deletion. Plugin retains wiring, framework and worker orchestration. |
| Raw actor offsets scattered through Plugin | Existing GameAudioLayout now owns the typed, bounded, identity-checked read. Public OwnerId replaces its raw offset. Installed client metadata is checked. |
| Exceptional cleanup can stop at the first failure | Independent cleanup attempts now cover constructor failure, worker exit and shutdown. Original initialization exceptions are preserved. |
| Selected-helper timing is insufficient | Added complete device/report and roster-pipeline fixtures, paired baseline measurements, structural coverage and optional real framework-update timing. Synthetic evidence is explicitly separated from live game timing. |
| Dependency audit unavailable | Added a fail-closed built-in NuGet audit of the locked production graph and SDK graph. The follow-up native Windows audit now passes against the official feed, including the SDK graph; an isolated known-vulnerable package is rejected with NU1903. The original feed failure came from an inherited proxy scheme. |

## Reviewed plans

GPT-6.1 Sol agents prepared and reviewed each plan using `gstack plan-eng-review`,
then implemented their assigned area. Office-hours was skipped as requested.
Highest-completeness choices were selected; no genuinely tied highest options
remained. Missing external review tooling was recorded as missing, not a clean
outside review.

- [Device recovery and stereo](device-reliability-plan.md)
- [Orchestration and lifecycle](orchestration-plan.md)
- [Native compatibility and layout](native-layout-plan.md)
- [Testing and performance](testing-performance-plan.md)

The parent owns integration, fresh Release verification, independent adversarial
probes and commit review. Local checks pass after the reviewed allocation-test
repair. The latest user timing sample is 20,286 updates, 0.044 ms average and
0.751 ms maximum; the earlier 24.489 ms peak remains unattributed. Their Info
Escape check exposed target cancellation beyond the focus-handoff issue. The
updated DLL consumes the dialog key before native Tick and passes installed
key-buffer/window tests; the user confirms both dialogs retain the selected
target. The recorded shared
stack warning is now excluded without removing the impact. The train vibration
was traced to screen effects and stops when that option is off; no speculative
boss filter was added. The user confirms quiet arrows with the impact retained,
plus lighting/preview, all three languages, native walking/mount vibration and
controller sound, Alt-Tab lights-off release, and reconnect.
Shared development DLL publication is parent-controlled.

## Preserved contracts

Native haptics, controller-speaker and adaptive-trigger routes; lighting/preview
rendering; focus-loss lights-off release; USB/Bluetooth/XInput output selection;
saved settings identity; `/bcef`; English/Chinese/Japanese; first-run and recording
consent; bounded local diagnostic storage; release allowlists and localization.
No telemetry, upload service, new device driver or background service was added.

Automated coverage of these contracts does not replace a current in-game hardware
smoke test. True plugin unload cannot guarantee stopping physically unreachable
hardware. Unresolved cleanup remains recoverable while the session is operational.
