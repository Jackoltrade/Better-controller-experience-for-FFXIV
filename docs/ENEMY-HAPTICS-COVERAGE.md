# Enemy haptics coverage

The goal of the boss trials is broad coverage from a small set of representative
fights. Each trial should identify a reusable sound-playback or ownership pattern
that can support other encounters. A successful test of one skill establishes
evidence for that path; it does not establish that every skill of that boss, or
every boss using similar assets, has been tested.

## Current policy (0.16.0)

During an instanced duty, when both the player and a recognized boss are in
combat, positively identified NPC SFX follows **Boss SFX**. There is no party-size
restriction: the same path handles four-player dungeons, eight-player trials,
alliance raids and larger duties. NPC sounds no longer require an action ID,
skill phase, attack type or per-boss sound rule in this context. Basic attacks,
casting effects and warning cues can therefore react intentionally.

The user chose to remember regular enemies seen **before boss combat**. Those
entities retain the Regular category. Enrollment closes as soon as a recognized
boss is fighting, including before the local player joins. Marks use actor,
entity and base identity, persist through combat ending, visibility gaps, focus
loss and feature toggles, and reset with territory, duty or player changes.
Tracking runs before audio initialization too. A cache limit disables broad
promotion until context reset instead of silently forgetting old marks.

| Mechanism | Current scope | Evidence |
| --- | --- | --- |
| Active duty boss context | All eligible NPC sound sources except remembered regulars; no action/phase/target-cue eligibility filter | Boss NPC source and mechanic-routing fixtures; user confirmed both 魅惑拥抱 phases on v0.15 with native effects and focus release passing; retained Unknown/Caster-tagged sources both use Boss/Spatial |
| Regular-enemy memory | Only independently classified hostile regular enemies before boss combat; neutral/preloaded performers are not marked as trash | Lifecycle, identity, saturation, source-classification and allocation fixtures |
| Player-side exclusion | All player identities and player-like NPCs/pets seed an ownership closure; their NPC descendants cannot become Boss sources | Nested/reverse owner chains, cycles, invalid IDs and stable-compaction fixtures |
| Exact native audio ownership | Existing actor/draw, animation and timeline-index associations, pool generations and playback IDs | Prior boss traces plus stale entity/scene/slot/playback regression checks |
| Arena/map ground audio | Current map VFX catalog, native non-loop metadata, exact layout/pool/backend playback and live position | MoonRelease installed metadata/native proof and live v0.14.23 月下美人 verification |
| Legacy skill filtering | Remembered regulars and sounds outside active duty boss context retain skill/action/C012 filtering | Regular-enemy, native helper/summon and Hunt/FATE regression checks |
| Boss identification | Existing duty/NPC catalog with installed Hunt/FATE metadata | Metadata fixtures; catalog entries are not a supported-boss percentage |

The old eight-player promotion functions, scripted-role exceptions and their
obsolete tests have been removed. The versioned research below records why the
original paths were added; it does not define the new active-boss policy.
Native audio ownership, spatial output, native-send preservation and foreground
release remain in use. Unknown ownership is not reclassified merely because it does not belong to the
local player. Non-positional audio cannot bypass enabled distance fade. UI, music and dialogue are not
NPC SFX; environmental audio still needs its separate arena proof.

## Known coverage limits

- An ordinary mob first seen during boss combat can follow Boss SFX, just like a
  new boss summon. This is the explicit tradeoff of remembering pre-fight regulars;
  the policy does not claim to recover universal native summon ownership.
- Reloading during a fight cannot recover earlier sightings. Load before seeing
  regular enemies when validating their exemption.
- Boss engagement still needs a recognized live boss. Unlisted boss identities
  can require identification data updates; broad audio does not discover bosses.
- Unknown native owners, unsupported playback layouts and missing source
  positions can still prevent feedback. Other players' unclassified effects must
  never become boss sounds through a negative local-player ownership check.
- All combat sources use the shared Advanced combat spatial and distance controls.
  The recommended character fade reaches zero at 25 yalms; users can change or
  disable it. Missing positions suppress added feedback while distance fade is on.
  A correctly classified sound can still be muted, distant, disabled or already
  handled by native vibration.
- Outside active duty boss context, legacy ambiguous skill/effect metadata and
  unlinked ownerless helpers can still be skipped.

## Impact-aware distance fade (0.19.5)

Combat audio keeps its native positional emitter. A sound created synchronously
inside a confirmed action result can also retain a copied impact position:
an explicitly area-targeted action's position, its confirmed animation target,
or its sole affected target. Multiple targets without a confirmed primary target
do not select an arbitrary nearby player.

The correction requires the same live caster and matching action/execution
timeline inside that native callback. Explicitly caster-attached execution sounds
use the impact for distance while preserving the emitter's left/right bearing.
Independently positioned VFX retain their actual position; a sound with no native
position can use a proven impact. Unknown links keep existing distance behavior.
The copied value follows the sound reservation's generation through asynchronous
loading and is discarded on reuse or actor replacement. There is no recent-hit
window, per-boss exception, new output delay, or audio-thread object-table lookup.

The recommended character-origin fade still reaches zero at 25 yalms **from the
resolved distance point**. Custom ranges and the existing camera-origin option
remain in effect. Native vibration sends, controller sound, footsteps and the
screen-motion filter follow their existing paths.

Validation: synthetic geometry, target-selection, ownership-generation and native
output fixtures cover the correction and the 25-yalm boundary. The existing Engels
recordings prove distant arm emitters but contain no action-result target-position
capture and mostly have action-ID-zero animation sounds. They do **not** establish
that the arm sounds have a usable impact link. New traces include bounded action
results and per-reservation impact provenance to verify that remaining case.

## Self-targeted execution fallback (0.19.6)

Some attacks play their sound on a self-targeted animation while a separate helper
applies the hit. The sound has no recoverable impact point. These sounds retain
their existing strength and directional balance instead of receiving extra
distance attenuation. This intentionally allows an admitted animation sound from
a distant emitter; it does not establish that its impact was nearby.

Eligibility requires an unambiguous skill execution timeline, a validated C063
caster/caster-origin attachment, and the same timeline's sole target equaling its
resolved owner. The pinned owner/count/target getters are validated before reading
that relationship. Cast-only, unknown, basic-attack and non-caster-bound tracks do
not qualify. Existing audio admission and targeting-cue exclusions still apply.
The evidence follows the sound reservation's generation, with no recent-action
window, new hook, boss-specific entry or additional runtime cache.

A known impact position always retains the configured fade, including the
recommended 25-yalm cutoff. Native-position and listener requirements remain in
place; self-targeting is not itself an impact position. Traces mark the admitted
fallback as `SelfTargetedExecution` and route `Animation`. The temporary raw
actor/timeline diagnostic dumps used in v0.19.5 were removed after investigation.

Automated checks cover eligibility, native-layout bounds, reuse, native vibration
preservation and known-impact cutoff precedence. Replaying the recorded headers
against installed action data admits 19 captured Engels tracks, including three
detached-arm tracks. The user subsequently confirmed arm feedback works on
v0.19.6. That confirms this attack path, rather than every self-targeted animation.

## Friendly NPCs and regular-enemy effects (0.19.7–0.19.8)

Combat NPCs enter enemy routing only when the game's action-target classifier
returns `Enemy`. Self, party, alliance, friendly and pet categories are excluded
before regular history or boss promotion; unavailable/unknown classification is
also excluded. Their identities seed the existing ownership exclusion, keeping
their descendants out of enemy audio. Dedicated neutral effect helpers and NPC
parts retain their separate paths. The bundled FFXIVClientStructs API is queried
on the framework thread, with no new hook, faction cache or NPC allowlist.

The earlier v0.19.7 guard used the `Hostile` status flag. A subsequent Trust-party
recording showed four friendly NPCs with that flag set, so that guard was
insufficient and has been removed. The game classifier instead uses the
player-relative targeting relationship, including native battalion relationships.

The existing exact effect-instance tracker now handles registered regular enemies
as well as bosses. Delayed C012/AVFX sounds can retain confirmed skill ownership
after the creating timeline returns. Dedicated helpers can inherit the category
of a matching live regular enemy; an independently recognized boss anchor retains
priority. Fresh helpers of remembered regular enemies stay regular during boss
combat. Unknown, basic-attack and marker sounds retain their admission guards.

The Copied Factory recordings establish three different issues: friendly 2P
entered broad boss routing; the tank's expanding rings lost delayed regular-skill
provenance; missile launch feedback worked while separate hit helpers were absent
from the regular roster. Automated fixtures cover these registration/provenance
paths and their exclusions. Live v0.19.8 Trust recordings confirm all four allies
classify as `Friendly` and stay out of the enemy roster, while actual enemies
classify as `Enemy`. The isolated check produced no added vibration from allies.
An earlier check had Screen effects enabled and recorded radial-blur feedback;
that independent visual-effect path does not use enemy-SFX classification.
The isolated recording contained enemy basic attacks, not a confirmed enemy
skill, so it does not establish positive skill feedback. Live checks of the
expanding rings and missile impact remain pending; the actual missile-impact
asset was not conclusively identified in the old trace. Recordings include
`TargetCategory` alongside the older `Hostile` diagnostic and regular-enemy
effect metadata for follow-up.

## How future trials should broaden coverage

1. Classify a miss using the trace: boss identification, actor ownership,
   regular-memory exemption, delayed playback, position/distance, or final output route. Skill/effect phase matters only on the legacy filtered path.
   Distinguish absent audio from audio that was observed and rejected.
2. Look for a reusable link in native metadata or the existing creation/playback
   chain. Fix that mechanism for all qualifying actors rather than adding a skill
   ID or SCD-name exception.
3. Add the smallest regression fixture to the existing check suites. Include a
   positive example and the corresponding exclusion: remembered regulars,
   teammate/pet effects, inactive context or stale actor/effect identities. Basic
   attack and cast rejection belongs only to the legacy filtered path.
4. Validate a representative fight and inspect its actual route. Label results
   separately as live-verified, fixture-tested, inferred to share a supported
   mechanism, or unsupported. A targeted association stays labeled as such.
5. Choose the next trial for a new mechanism: ownership chains, different helper
   identities, lingering fields/projectiles, arena objects or phase transformations.
   Repeating an already verified pattern on many bosses has lower priority unless
   it reveals a regression.

Ownerless associations remain a compatibility fallback while looking for broader
creation or ownership evidence. Do not expand them into the main coverage strategy
or enable all nearby boss audio to hide missing links. Native game haptics and
speaker playback, shared combat spatial/distance settings, the recommended
25-yalm cutoff and foreground/lights-off guards remain required regression behavior.

The [ownerless summon investigation](OWNERLESS-SUMMON-INVESTIGATION.md) records
native fields checked, rejected shortcuts and the passive spawn diagnostic build.

Detailed live evidence is in the [QA report](QA-REPORT.md) and
[development notes](DEVELOPMENT-NOTES.md). Automated assertion totals measure
regression checks; they do not count supported bosses.

## FATEs, Hunts and eight-player encounter adds

`EnemyEncounterCatalog` reads the installed Fate, BNpcBase and NotoriousMonster
sheets once. Boss FATEs use map markers 60502/60802 or event icons 60722/63915,
plus the actor's actual FateId and a base-entry rank of 1, 2 or 6. Rank-zero event
mobs stay regular; a nearby elite with no matching FATE membership does not
become a FATE boss. Hunt matching uses the exact NPC name/base pair from B/A/S
entries in NotoriousMonster. This adds metadata-based coverage without a
per-encounter name or skill allowlist. The current installation supplies 477 boss
FATE rows and 207 distinct registered Hunt pairs; these are metadata counts, not
live-tested encounter totals. Events with other markers/ranks remain unconfirmed.

In a single-party eight-player duty, regular hostile combatants temporarily use
the boss category while both the player and a recognized boss are in combat.
Party size comes from ContentMemberType; QueueMaxPlayers is an optional override
and is zero in the locally checked normal/extreme Tsukuyomi duties. Alliance
raids, four-player duties and ordinary trash pulls do not activate this rule.
When the context ends, the normal category is republished and stale sound
ownership is invalidated. Encounter adds retain their regular skill detection under the distinct
EncounterAdd role; actual bosses keep the stricter released-effect filter. Spatial
routing and the basic-attack exclusion still apply. It does not establish
a permanent owner link between the adds and the boss.

## Targeting-marker exclusion (0.14.19)

Stock `sound/vfx/monster{digits}/SE_Vfx_Monster_ETC_LockOn_*.scd` banks are
shared warning cues rather than attack impacts. Installed Lockon-sheet AVFX
metadata confirms nine dedicated banks, including the line-stack green arrows.
These are excluded from added enemy vibration, with actual impact banks left
on the existing action/ownership route. Tests cover the shared family, native
string bounds, reservation reuse and cache contention. This does not claim
that every future marker follows the same naming pattern. Live verification
of the new exclusion is still required.

## Arena/shared-group audio policy (0.14.23)

月下美人 uses a map VFX shared-group sound. Versions 0.14.21–0.14.22
established its separate native playback identity. Version 0.14.23 follows the
user's explicit broader rule: map VFX ground effects follow Boss skills during
player/boss combat, without asserting native action ownership. Exact sound/index
catalogue membership, native non-loop metadata and live pool/layout/playback
identity remain required. The user confirmed this effect and native-feature
preservation on v23; its retained trace records the exact arena mixer ID on the
Boss/Spatial route. This does not enable arbitrary environmental audio.

Separately, known scripted execution presentations can include appearance audio
(observed 6434); only unknown/non-action cues such as 6431 are rejected
automatically. The policy does not isolate damage-only PCM.
