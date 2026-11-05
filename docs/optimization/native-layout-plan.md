# Central actor metadata reads

Plan-time review record. For implemented behavior, current verification and remaining
limits, see [the parent verification report](verification.md).

Fixed target/report: docs/optimization/native-layout-plan.md, baseline 9354236. Scope gate: parent explicitly selected this plan. This follows the same fully read plan-eng-review skill; real preamble rerun, existing consent/review context reused. Parent explicitly authorizes highest-completeness choices and implementation after this review; no tie identified. No office-hours requested.

## Original proposal and binding decisions

Centralize Plugin's raw actor offsets, first reuse public Dalamud fields, preserve framework-thread object access and immutable native publication, and protect widths/bounds/null/identity/reuse/compatibility with tests. No new scanning. Parent metadata receipt: work/optimization-native-metadata/results.txt.

## Scope Challenge

What exists: GameAudioLayout already owns pinned native layouts, span validation and EnemyActor records. BossEncounterState already owns classification and regular-enemy history. Installed IGameObject exposes OwnerId, confirmed by metadata; reuse actor.OwnerId rather than reading +0x88. Public IGameObject/ICharacter do not expose DrawObject, FateId, EventId or LayoutId. Installed FFXIVClientStructs.GameObject has explicit size416 and offsets EntityId0x78, LayoutId0x7C, OwnerId0x88, EventId0xF4, FateId0xF8, DrawObject0x100. This is metadata-only evidence, not live-client validation.

Selected arrangement: extend existing GameAudioLayout and BossEncounterState, no new owner class. Estimated 5 files: those two existing Core files, Plugin.cs, new GameObjectLayoutChecks.cs and existing compiled compatibility checks. No complexity selector required (<8 files, <2 new classes/services). Scope accepted as-is. One change set integrated with orchestration. No dependency/package/platform/distribution change. No TODOS.md found. Existing offsets are still present in current Plugin code, so work is not already done. Existing history checked under orchestration review; no reversal identified.

Premise table: public OwnerId reuse verified from metadata receipt; absent remaining public fields verified in complete property dump; field offsets verified in pinned struct metadata; true live actor readable extent remains unverified. unverified=1.

REALITY: premise-table pass unverified=1 work/optimization-native-metadata/results.txt:124
REALITY: already-done pass raw actor reads still present Plugin.cs:395
REALITY: surface-check pass installed public OwnerId inspected work/optimization-native-metadata/results.txt:20
REALITY: binding-decisions pass parent requires one existing-layout reader docs/optimization/native-layout-plan.md:7
REALITY: numbers pass file/class counts estimated docs/optimization/native-layout-plan.md:13
REALITY: deferred-asks pass every actor requirement mapped below docs/optimization/native-layout-plan.md:7
REALITY: pr-count pass one integrated change set docs/optimization/native-layout-plan.md:13

## Architecture review

1. [P2] (confidence: 10/10) baseline Plugin.cs:508 `*(nint*)(address + 0x100)` and :621 `*(uint*)(actor.Address + 0x88)` duplicate unchecked layout assumptions in orchestration. Current line positions changed during extraction; parent verified all five baseline offsets. Remedy A1 selected: Layer1 reuse public OwnerId plus a single typed GameAudioLayout reader for remaining fields (10/10). Alternative: central constants but raw dereferences remain at callers (5/10). No unfamiliar architectural pattern, concurrency primitive or dependency requires research.

Reader contract: TryReadActor(nint actor, int availableBytes, uint expectedEntityId, out ActorFields fields), backed by a ReadOnlySpan<byte> reader for explicit widths and fixture coverage. ActorFields is a readonly value containing DrawObject(nint), FateId(ushort), EventId(uint), LayoutId(uint). Add pinned GameObjectSize416, RequiredActorBytes0x108 and centralized field constants including EntityId0x78. Require nonzero address and valid expected identity, enough declared extent, compare EntityId before and after reading. Return false/default on declared bounds/null/identity failure. No inferred actor identity from pointer alone. Caller owns pointer lifetime/readable-memory guarantee under existing framework thread; this reader cannot prove an arbitrary pointer readable and adds no OS memory scans.

```text
Framework Update -> LocalPlayer address/entity snapshot
  null/invalid identity -> TryReadActor false -> SetPlayer(0,0,0,null)
  valid -> read pinned fields -> identity changed? false/default
    valid identity -> SetPlayer(...fields.DrawObject...) -> immutable capture identity
Framework roster -> candidate public fields + actor.OwnerId
  invalid native fields -> skip candidate
  valid -> boss classification with fields.FateId
    -> EnemyActor(fields.DrawObject, fields.EventId, fields.LayoutId)
    -> ExcludePlayerOwned -> live identity combat check -> Update encounter
    -> SelectRoster -> publish immutable capture roster -> observe casts
```

Roster extraction is limited to the existing pure ordered selection loops: add BossEncounterState.SelectRoster(candidate span, selected span). It selects, remembers independently regular actors, conditionally classifies and reselects. Plugin retains framework object enumeration, exclusions, live identity combat lookup, encounter Update, roster publication and cast observation. Perf lane benchmarks actual ExcludePlayerOwned -> Update -> SelectRoster calls, with no copied algorithm. Full framework timing remains a separate obligation owned by perf lane.

## Code quality review

2. [P2] (confidence: 9/10) baseline Plugin.cs:626-630 constructs EnemyActor from five raw reads mixed with encounter classification. Remedy A2 selected: typed field result, public OwnerId, and existing owner SelectRoster method (10/10); alternative new actor service and roster facade (7/10). Two authored callers are local-player publication and enemy candidate construction, both in Plugin.Update/UpdateEnemyActors. Reader receives the same game-object prefix and identity guarantee at both. Existing FFXIVClientStructs dependency is a compatibility receipt, not a second source of offsets. No runtime reflection, wrapper per actor, or heap result allocation.

Estimate: remove 5 raw dereference expressions and ~13 selection-loop lines; add ~45-70 reader lines and ~20 moved roster lines. Net production grows ~30-60 lines; tests add ~90-150 lines. Whole-change growth is justified by centralized identity/bounds checks and measured production seam; no line-savings claim.

## Decision ledger

| ID | Baseline | Chosen | Alternative | Actual authorization |
|---|---|---|---|---|
| A1 | scattered raw pointer reads | public OwnerId + typed identity/bounds reader,10/10 | constants only,5/10 | parent highest-completeness instruction |
| A2 | selection loops in Plugin | existing BossEncounterState method,10/10 | new roster service/facade,7/10 | parent simplicity/reuse and production perf seam |
| A3 | no focused actor-field fixture | widths/null/truncation/identity/reuse + SDK metadata agreement,10/10 | happy path fixture,6/10 | parent explicitly requires each proof |

Approval readiness: PASS for A1-A3. No unresolved decisions or equally complete tie. Existing behavior preserved except fail-closed invalid identity/native prefix; this is the explicitly requested reader contract.

## Test review

Framework: existing net10.0 regression executable and compiled plugin reflection checks. Existing GameAudioLayout/Plugin/BossEncounterState sources, UserConsentChecks, WindowingChecks and HookOriginalLifecycleChecks were read before this diagram. No new framework/eval.

```text
TryReadActor                               USER FLOWS
  zero pointer / invalid expected [GAP]    zone/unload/null player -> no stale player routing [existing+GAP]
  short length [GAP]                      actor slot/pointer reused -> old identity rejected [GAP]
  correct ushort/uint/nint widths [GAP]    valid encounter -> same roster ownership rules [existing]
  mismatch before/after -> default [GAP]   public OwnerId preserves player pet exclusion [existing+GAP]
SelectRoster                              compatible installed SDK -> layout check passes [GAP]
  inactive -> regular enrollment [existing BossEncounterStateChecks]
  active -> classified roster [existing+extend]
  saturation/empty -> conservative [existing+extend]
```

Tests: add GameObjectLayoutChecks using stackallocated byte buffers with sentinel adjacent bytes; test span lengths0..Required-1 false/default, exact required prefix success, 64-bit DrawObject untruncated, ushort FateId cannot absorb neighboring bytes, uint IDs exact; zero pointer, invalid/mismatched entity ID false/default; mutate same fixture entity ID and verify old identity rejected/new identity accepted. Test null draw pointer is valid identity metadata (no invented nonnull requirement). Value: protects=typed field and lifetime identity contract; fails_when=width offset bound or identity guard changes; why_new=no focused Plugin field reader fixture; seam=production reader; no_claim=arbitrary native readable-memory safety.

Compiled compatibility test inspects installed FFXIVClientStructs GameObject StructLayout.Size and FieldOffsetAttribute plus declared widths against centralized constants, and verifies Dalamud IGameObject.OwnerId remains public. Value: protects=pinned SDK agreement and public reuse; fails_when=installed SDK changes offsets widths or removes API; why_new=current tests cover mixer bindings, not actor prefix; seam=none; no_claim=live binary layout across all clients.

Extend BossEncounterStateChecks with SelectRoster outcomes for regular memory, active remap, player-owned removal before selection, saturation and scene reset. Value: protects=production ordered roster semantics; fails_when=selection/classification order changes; why_new=new method must carry existing caller integration; seam=none; no_claim=actual game roster timing. No obsolete tests.

## Performance review

No performance issue proven. Reader is constant-size, zero heap allocation and no locks. One extra identity read protects reuse but does not synchronize memory mutation. Existing actor limit and spans remain. Reuse public OwnerId may perform its own native read; no cache added. Existing perf lane handles real roster pipeline and allocation/time baselines; no fabricated speedup. No units change (bytes offsets; seconds/timing unchanged).

## Failure modes

- Null/truncated/identity mismatch: skip actor or clear player publication, unit fixtures prove false/default. User sees conservative missing added feedback; native effects remain untouched.
- SDK mismatch: compatibility checks fail with field name, expected and actual value; do not publish until resolved.
- Concurrent actor reuse after reader: framework/object-table lifetime assumption still applies; preserve existing address/entity checks before cast observation. No broad memory-safety guarantee.
- Empty or saturated encounter: existing conservative roster rules retained and extended tests.

Critical proposed gaps:0. Live client compatibility unknown; retain existing documented support limits.

## Implementation Tasks

- [ ] N1 (P2,human~1h/agent~15min): add typed reader and widths/identity/extent fixtures in existing GameAudioLayout + GameObjectLayoutChecks. Depends:none.
- [ ] N2 (P2,human~1h/agent~15min): replace Plugin raw reads, reuse OwnerId, clear publication on invalid local identity. Depends:N1.
- [ ] N3 (P2,human~1h/agent~15min): move existing ordered selection loops into BossEncounterState.SelectRoster; extend existing behavioral checks; give API to perf lane. Depends:N2 only for Plugin edit sequencing.
- [ ] N4 (P2,human~1h/agent~15min): compiled SDK compatibility check, isolated build/regressions; parent combined validation after lanes settle. Depends:N1-N3.

Sequential within shared Plugin/Core ownership; perf benchmarks can start against agreed API independently then compile after N3. Device lane owns LightingSession/NativeHapticOutput; no edits here. No commit/publication/shared build while editing.

## NOT in scope

No new signature scanning, arbitrary memory validation, alternate client support claim, native hooks or broader actor object model. No new roster service. No changed config/UI behavior.

## Review execution and completion

Reuse earlier real startup/context/history/availability probes; new real preamble launched for this target. No design/CEO doc found earlier, maintenance scope unchanged. Outside Claude CLI absent, bounded native fallback tools absent; no outside coverage credit. QA/test discovery artifact and required log must use actual gstack state under authorized escalation; tasks JSONL skipped because jq not installed, as skill directs. Tests described are planned, not executed at review time. Architecture1,codequality1,test gaps3,performance0:5 issues/groups. Reality tier1 rows7; unverified1. Lake3/3. No TODO proposal. Real review complete before native call-site implementation; parent authorized implementation.

## GSTACK REVIEW REPORT

| Review | Trigger | Why | Runs | Status | Findings |
|---|---|---|---|---|---|
| CEO Review | not requested | maintenance scope | 0 | not run | none claimed |
| Outside Review | claude-code | independent plan challenge | 0 | unavailable | CLI/fallback absent |
| Eng Review | /plan-eng-review | architecture and tests | 1 | ISSUES OPEN (logged) | 5 mapped issues/groups,0 critical proposed gaps |
| Design Review | not requested | no UI change | 0 | not run | none |
| DX Review | not requested | no DX change | 0 | not run | none |

**OUTSIDE COVERAGE:** claude-code plan-review unavailable, no clean credit.
**VERDICT:** Native-area plan complete; required review log persisted and review-read verified; ISSUES OPEN, implementation not yet validated.
NO UNRESOLVED DECISIONS
