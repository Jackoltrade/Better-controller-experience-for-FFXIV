# Performance verification

The November 2026 performance report did not include frame times, hardware details,
or the enabled features. It establishes a concern to investigate, not an FPS baseline.

## Implemented changes

- Skip sound-ownership inspection when the enabled features do not need it.
  Screen-effects-only mode does not need to identify individual audio voices.
  Player/enemy combat, player-only SFX lighting and arena ownership exclusions
  retain their live lookups; native sound identities are never cached across reuse.
- Reuse the fully validated PCM loudness measurement when the same audio block
  feeds both haptics and SFX lighting. If haptics skipped measurement or the block
  sizes differ, lighting performs its original measurement.
- Compile diagnostic recorders, native inspection probes, trace buffers, counters,
  timing instrumentation, recording consent and diagnostic UI out of production.
  Diagnostic-only translations and reservation snapshots are excluded too.
  Actual screen filtering, native vibration headroom checks and enemy/impact
  attribution remain functional code in both builds.
- Skip cast-property reads when no audio capture exists. Duty roster/history
  maintenance continues so enemies seen before a boss fight retain their marks.

## Evidence and scope

The populated 256-slot sound-pool fixture compares the previous admission condition
with the new condition and calls the real ownership inspector. Unneeded cases
drop from 10,000 ownership reads to zero; eligible player/enemy cases retain all
10,000 reads and return the same ownership result. Timing uses seven alternating
warmed runs, separately from allocation measurement. No timing threshold gates
correctness.

The shared-measurement fixture compares actual managed haptic processing plus
lighting for 2,048- and 4,096-float blocks. It checks identical signal levels,
silent/invalid input, retired generations and zero warmed managed allocations.
These results cover CPU components and managed fixtures, not complete game-frame
cost, physical controller latency or low-spec-PC FPS.

Separate fixtures execute the actual compiled `Render` callback in both build
variants, with owned native-layout buffers and an original-playback sentinel.
They verify shared signal levels, original playback exactly once, unchanged PCM
and rejection of unrelated, unowned or invalid input. These exercise the adapter
without installing live hooks or writing to a controller.

The compiled full-roster fixture also checks capture-absent and capture-present
updates: identical remembered regular enemies and selected actors, with no cast
reads in the absent case. The accepted previous DLL fails only that dead-read
assertion; the rebuilt variants pass. This is a work-count check, not a measured
frame-time improvement.

In the pre-rollback performance verification run, the shared 2,048-float fixture measured 11.121
versus 4.713 microseconds per operation; the 4,096-float fixture measured 12.359
versus 5.158. These are within-run paired component measurements. Unchanged helper
controls also varied between baseline and candidate processes, so cross-process
timing differences alone are not attributed to these changes. The deterministic
work reductions are zero unnecessary ownership scans and one PCM scan instead of
two for an eligible shared block.

The later user-requested SN30 Pro rollback restores the earlier linear XInput
response and removes its experimental paired handoff. It preserves the ownership
admission, shared PCM measurement, idle cast-read skip and production diagnostic
removal described above. Both rebuilt variants pass the same 61 check groups;
see the current [QA record](QA-REPORT.md). The timings above remain evidence from
the identified pre-rollback candidate, not a new measurement of the rollback.

The frozen working-source baseline and raw measurements are kept locally under
`work/performance-20261111/`. Earlier failures are diagnostic evidence; the final
verification record must identify the accepted source and DLL hashes.

## Production and development builds

`tools/build.ps1` builds production by default. `tools/build.ps1 -Development`
explicitly enables diagnostics in a separate optimized DLL under
`artifacts/dev-plugin/`. The development build also requires Dalamud's development
install flag to expose recording. It does not produce an installable ZIP.

Public packaging requires a verified `DiagnosticsEnabled: false` build and rejects
diagnostic or unclassified inputs before changing an existing candidate. The CI
build tests both variants; repository ZIPs come only from production. Release
publication remains manual.

## Gameplay check and measurement limits

The user completed the requested Chinese-client smoke check on the production
DLL and reported that available features pass and Diagnostics is absent. This
checks behavior in that session; no FPS/frame-time comparison was reported.

To quantify whole-game impact, compare identical areas and camera/graphics settings with the plugin disabled and
enabled, recording frame times rather than FPS alone. Test screen-only, combat-only
and combined SFX lighting/haptics separately. Check normal skill reactions, native
footsteps/speaker output, controller reconnect and background release. A successful
synthetic run does not replace those observations.
