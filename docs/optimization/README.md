# Reliability and maintainability optimization

Baseline: `9354236` (2026-11-05). The original review rated the project **7.3/10**.
Scores are engineering judgments, not assertion counts or coverage percentages.
This change addresses every confirmed finding; it does **not** claim the requested
9+ target has been independently established. See [verification and limits](verification.md).

## Findings and changes

| Finding | Result |
| --- | --- |
| Bluetooth stop intent lost after uncertain writes or discovery failure | Confirmed with the original adversarial probe. Ownership now survives handle loss until acknowledged cleanup; failed enumeration retains healthy handles. Ownership capacity is reserved before output and never evicts unresolved cleanup. |
| Separate left/right publication can split one centered sample | Confirmed. Stereo peaks now use one atomic pair and retired accumulator epochs. |
| Failed autosaves retry each framework frame | Confirmed. Debounced saves retain dirty revisions, apply a five-second retry floor, serialize storage callbacks and expose a deduplicated error. Explicit consent saves still propagate failure. |
| Allocation failure aborts later checks | Confirmed. Named independent groups continue, retain original diagnostics and return failure. Exact allocation checks include the delta and runtime context; their thresholds are unchanged. |
| Allocation inconsistency | Reproduced intermittent 6,192-byte first-window allocations on .NET 10.0.14. Cause remains unproven; controls and raw investigation are documented in the test plan. No retry-to-green policy was added. |
| Plugin mixes rendering, recording and lifecycle | SettingsUi owns rendering/windows; DiagnosticRecordingCoordinator owns recording intents, save ordering and deletion. Plugin retains wiring, framework and worker orchestration. |
| Raw actor offsets scattered through Plugin | Existing GameAudioLayout now owns the typed, bounded, identity-checked read. Public OwnerId replaces its raw offset. Installed client metadata is checked. |
| Exceptional cleanup can stop at the first failure | Independent cleanup attempts now cover constructor failure, worker exit and shutdown. Original initialization exceptions are preserved. |
| Selected-helper timing is insufficient | Added complete device/report and roster-pipeline fixtures, paired baseline measurements, structural coverage and optional real framework-update timing. Synthetic evidence is explicitly separated from live game timing. |
| Dependency audit unavailable | Added a fail-closed built-in NuGet audit of the locked production graph and SDK graph. Local Windows feed access remains unavailable; a separate Linux read of the official advisory data found no entries for the three pinned build package IDs. |

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
probes and commit review. Shared development DLL publication is parent-controlled.

## Preserved contracts

Native haptics, controller-speaker and adaptive-trigger routes; lighting/preview
rendering; focus-loss lights-off release; USB/Bluetooth/XInput output selection;
saved settings identity; `/bcef`; English/Chinese/Japanese; first-run and recording
consent; bounded local diagnostic storage; release allowlists and localization.
No telemetry, upload service, new device driver or background service was added.

Automated coverage of these contracts does not replace a current in-game hardware
smoke test. True plugin unload cannot guarantee stopping physically unreachable
hardware. Unresolved cleanup remains recoverable while the session is operational.
