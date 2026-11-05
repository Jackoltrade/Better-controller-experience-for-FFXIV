# Allocation window scope map

Snapshot: raw-windows.txt captured before this consistent-migration wave; line numbers below refer to that snapshot, not post-edit locations. Its hash and preserved red coverage log/XML are in baseline-sha256.txt. No causality is assigned to the coverage-only24,624-byte failure.

Counts:35 current-thread read pairs across20 files =32 raw strict regression windows +2 timed benchmark windows +1 canonical helper implementation. EnemyEffectChecks already uses the helper and is listed separately. One async process-total benchmark pair is a different metric. Original warmup bodies/counts, measured iteration/index sequences, result counters and semantic assertions must stay exact. Pre-create ordinary non-loop Actions outside counters; no new overload or blanket method attributes.

When warmup differs or measured counters/indexes must begin fresh, retain original warmup before measurement and call MeasureAllocations(step,0,N). Do not run measured-only operations or increment measured counters during warmup. Setup allocations and assertions stay outside the callback; unsafe storage must remain live synchronously through the callback.

Owner codes: TP=this test/performance lane; B=sol_orchestration; Q=final_quality_review; ROOT=parent. Only assigned owners edit their files. Root reviews final diffs/integration; no other agent changes Program or the canonical helper.

| ID | Original file/read lines | Warmup and measured work | Counter/index/semantic requirements | Owner |
|---|---|---|---|---|
| R01 | BluetoothStereoChecks113/115 |1000 Observe(.5,.25,epoch)+TakePeak;10000 same |Strict0; epoch unchanged; publisher already joined |TP |
| R02 | BluetoothStereoChecks161/163 |1000 Configure(requested,true,true);10000 same |Capture CurrentSettings+ScreenSettings snapshots AFTERwarmup; preserve both ReferenceEquals and0; externalwarm/helper0 |TP |
| R03 | BossEncounterStateChecks214/217 |2000 Classify(neutral)+Classify(changed);10000 comparisons |matched starts0 AFTERwarmup; matched10000 plus0 |B |
| R04 | BossEncounterStateChecks234/240 |2000 RememberRegular+WasRegular;10000 Update+RememberRegular+WasRegular |Warmup excludes Update; found starts0; finalfound10000/state.Count1/0 |B |
| R05 | BossEncounterStateChecks331/337 |2000 copy fixture->actors +ExcludePlayerOwned;10000 same+length count |matched starts0 AFTERwarmup; retain output sequence,10000/0 |B |
| R06 | BossNpcSoundChecks96/102 |100 EnemyKindOf only;10000 EnemyKindOf+TryGetEnemyPosition |Warm differs; preserve reads0 and zeroalloc; pool stays live |B |
| R07 | CameraShakeChecks111/113 |0 explicit newwarmup;100 Observe(Vector3.Zero,Vector3.Zero,At(2+i/120)) |Measured i=1..100inclusive exactly; retain timestamp calculation; no new warmup |ROOT |
| R08 | EnemyCasterOriginChecks99/102 |2000 TimelineSound(ClipSoundBinding);10000 IsReleasedEffect count |accepted starts0; expected10000/0; stack clip/data pointers stay live |Q |
| R09 | EnemyCasterOriginChecks182/185 |2000 TimelineKey only;10000 TimelineKey.HasAttackType AND Action.HasAttackType |count starts0; expected10000/0; preserve short-circuit |Q |
| R10 | EnemyClipChecks35/37 |2000 TimelineSound(execution,Target);10000 same |Strict0; preserve arguments |Q |
| R11 | EnemyClipChecks99/101 |2000 ClipSoundBinding;10000 same |Strict0; stack clip/data/table lifetime; no capture of ref-like locals |Q |
| R12 | EnemyEncounterChecks47/49 |2000 IsBoss lookup1 only;10000 lookup1+lookup2 |Warm differs; same two metadata keys and strict0 |B |
| R13 | EnemyHapticChecks129/131 |2000 SelectEnemyActors(knives,selected);10000 same |Strict0; retain arrays, no new arrays inside step |B |
| R14 | EnemyHapticChecks278/286 |20000 SetEnemies+EnemyKindOf+TryGetEnemyPosition;10000 same plus ResolveCaster+ResolveTimelineCaster and successcount |Existing20000 is preserved, NOTnewinflated warmup; measuredcount starts0 and10000; warm differs |B |
| R15 | EnemyHapticChecks506/510 |1000 Inspect only;10000 Inspect with property pattern+accepted |accepted starts0;10000/0; pointer pool lifetime unchanged |B |
| R16 | EnemySkillChecks105/109 |2000 TimelineKey only;10000 TimelineKey.IsSkill AND Action.IsSkill |count starts0;10000/0; preserve short-circuit |Q |
| R17 | EnemySkillChecks111/114 |0 additional warmup;10000 actor.ObserveAction+TimelineKey |This follows R16; do not introduce another warmup; strict0 |Q |
| R18 | EnemySkillChecks164/166 |2000 ClassifySound;10000 same |Strict0; all exact source/caster/origin/action args |Q |
| R19 | EnemyTargetingCueChecks54/56 |100 StockTargetingCue.Matches(UTF8literal);10000 same |Keep literal materialization in step; cannot capture ReadOnlySpan local; strict0 |Q |
| R20 | EnemyTargetingCueChecks109/111 |100 EnemyKindOf124;10000 same |reads must remain7; strict0; no reader reset |Q |
| R21 | EnemyTargetingCueChecks268/270 |100 EnemySoundCueFilter.Read(resource);1000 same |Strict0; unmanaged resource/filename storage remains until finally |Q |
| R22 | EnemyUncategorizedSkillChecks276/281 |2000 ObserveCast+two TimelineKeys;10000 measured conditional adds rejectedcast+Action lookup |Warm differs; matched starts0;10000/0; exact positive/rejected conditions |Q |
| R23 | HapticEnableCueChecks55/57 |1 explicit allocationCue.Render(pcm,1);blocks=(HapticEnableCue.TotalFrames+255)/256 same |Keep one warm render, strict0; cue retention/expiry setup unchanged |ROOT |
| R24 | Program358/360 |20 Observe(samples)+TakePeak;1000 same |Strict0; same sample array owned outside callback |ROOT |
| R25 | Program567/573 |0 explicit newwarmup;1000 ownership.Acquired(pool+3,Player)+Contains456 |Keep pointer lifetime; do not capture an address-taken struct; strict0 |ROOT |
| R26 | Program1006/1009 |1 explicit Observe+TryTake;1000 same |Keep epoch/source/sound/time and strict0; original warm remains |ROOT |
| R27 | RumblePulseChecks80/82 |Reset+one Update(.5,0,120);10000 Update(.5,index*.033,120) |Measured index1..10000inclusive, no warm index advance; strict0 |ROOT |
| R28 | ScreenEffectHapticChecks171/177 |1 explicit MixScreenEffects;100 step SetNative(short(1+i%8),.02,-.02)+Mix(now+i) |Measured i0..99; keep modulo pattern and timestamp; pointer/span lifetime; strict0 |ROOT |
| R29 | ScreenEffectHapticChecks454/456 |0 explicit newwarmup;100 changing.Render(pcm,.25,profiles[i%len]) |i0..99 exact; no addedprofilewarm; strict0; keep preceding state |ROOT |
| R30 | SfxNativeTraceChecks267/269 |2000 ObserveResolution;10000 same |Strict0 AND Resolutions.Length1; entry stays until finally |ROOT |
| R31 | SfxNativeTraceChecks312/314 |ExerciseDisabled(...,2000);ExerciseDisabled(...,10000) |IMPORTANT: measured helper itself loops. Do not merely wrap the10000-loop call in Action. Use one non-loop exercise step (4existing calls) with helper10000; retain exact externalwarm2000 and false/true stopped cases, every empty-array/TraceError assertion and unreadable-page lifetime |ROOT |
| R32 | SpecialSoundPlaybackChecks205/208 |2000 resolve(sound,out_);10000 resolve+PlaybackId comparison |mapped starts0;10000/0; preexisting native delegate, pointers/frozen fixture stay live |Q |

## Already migrated and canonical support

| ID | Site | Scope | Disposition |
|---|---|---|---|
| C01 | EnemyEffectChecks ->AllocationChecks.MeasureAllocations |2000warm,10000Observe+Resolve, strict0,31behaviorassertions |Already reviewed/migrated; target order unchanged |
| C02 | AllocationChecks18/20 |Canonical helper's own counter pair, variable warmup/iterations, ordinary Action.Invoke |Keep sole AggressiveOptimization + NoInlining here; never migrate recursively |
| C03 | AllocationChecks.Run |No-op0, escapingnewbyte37=64/step, changingrealcacheassociationdetected;2000/10000each |ROOT moves group to VERY END after existing CheckSfxSession/beforeComplete, so controls cannot pre-warm any original group |

## Deliberately separate benchmark scopes, unchanged

| ID | Site | Warmup/measurement/index | Why excluded |
|---|---|---|---|
| B01 | PerformanceChecks70/74 |2000action(i=0..1999), then iterations20k or100k i=0..N-1, Stopwatch reads inside allocation interval |Reports complete timed-loop CPU/runtime overhead; changing to callback-only allocation without separate review would change its declared metric |
| B02 | RosterPerformanceChecks59/63 |200Tick(0..199), then iterations100/1000Tick(i+200),10batches, checksum/state/caseguards |Reports complete roster-loop wall/managed work, including control/checksum logic; preserve all current scope and indices |
| B03 | LoopPerformanceChecks65/77 process-total pair |200async tickwarmup;1000/100/1measuredtick(s),10batches per scenario; includesawait |Process-total allocation across continuation switches, not current-thread callback allocation; intentional noise scope. Do not route through sync Action helper |

## Required integration evidence

Per-file compile/behavior check and root diff review must confirm same operation counts, warmup bodies, measured index ranges, semantic counters and lifetime cleanup. Final coverage remains separate from uninstrumented evidence and can fail honestly. The preserved54/55 red XML/log is not replaced or reclassified by later success. This map authorizes no causal attribution to OSR/instrumentation and no retries/tolerance/addedwarmup/globaltier flags.

## Migration readiness

All32 listed raw strict-regression sites are now routed through the existing canonical helper. B completed8 sites, Q completed12, ROOT completed10 and TP completed the2 BluetoothStereo sites. EnemyEffectChecks remains the previously migrated target. No new helper overload or method attribute was introduced. ROOT reviewed assigned-file diffs; the two Bluetooth changes retain their exact external1,000warmups,10,000measured calls, snapshot timing and ReferenceEquals assertions.

A final source search finds current-thread counter pairs only in AllocationChecks (the canonical implementation), PerformanceChecks and RosterPerformanceChecks (the deliberately unchanged timed scopes). LoopPerformanceChecks retains its separate process-total pair. AllocationChecks controls are now the last group, after CheckSfxSession and before Complete; all preexisting groups retain their order.

TP's isolated Bluetooth verification compiled with0warnings and passed81original assertions; the existing three positive/no-op controls ran afterward,84total. This is scoped verification, not a replacement for ROOT's current whole-suite/private compiled-plugin and coverage run. The original coverage54/55 failure and24,624-byte delta remain preserved and unattributed.

Parent integration receipt: `work/optimization-qa/verify-results.txt` passes all
55 groups with no skips, including the three detector controls last. The separate
coverage invocation passes the same 55 groups with all strict checks enabled.
Parent reviewed every migration against the original warmup/index/result map.
Historical allocation failures remain preserved and unattributed.
