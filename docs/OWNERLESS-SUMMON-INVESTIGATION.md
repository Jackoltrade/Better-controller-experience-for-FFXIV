# Ownerless summon investigation

Current policy, 2026-11-04: version 0.15.0 replaces the eight-player/scripted
promotion rules below with all-size active duty boss-SFX classification and
remembered pre-fight regular enemies. Native actor/playback ownership is still
required; a universal summon-parent relationship is not inferred. See the
[current coverage policy](ENEMY-HAPTICS-COVERAGE.md). The following sections are
retained research history.

Status on 2026-11-03: no universal boss-parent link has been verified. Version
0.14.16 added passive spawn diagnostics. Version 0.14.17 integrates the existing
eight-player encounter-add policy for separate mechanic NPCs, with captured
stack/fan regression fixtures and encounter-state trace details. This is encounter
grouping, rather than inferred native parentage. A follow-up checked on 2026-11-04
verifies fan routing. The user subsequently confirmed the line stack and initial
raid-wide on 0.14.18; 0.14.20 adds the separately scoped scripted-performer policy
below, with both newly covered attacks verified in-game.

## Native evidence

The pinned [GameObject definition](https://github.com/aers/FFXIVClientStructs/blob/b69073ae2d789527fc1b9a78aa7aa590e0f299dc/FFXIVClientStructs/FFXIV/Client/Game/Object/GameObject.cs)
contains `OwnerId`, layout/gimmick/event IDs and shared-group/event-handler
pointers. These fields have different purposes; their mere presence or equality
does not establish that one actor was summoned by another.

The same revision's
[Character definition](https://github.com/aers/FFXIVClientStructs/blob/b69073ae2d789527fc1b9a78aa7aa590e0f299dc/FFXIVClientStructs/FFXIV/Client/Game/Character/Character.cs)
documents `GetParentCharacter` as a mount/minion/ornament helper. Local
disassembly at RVA `0x909A50` confirms that it looks up `ObjectIndex - 1` and
checks whether that object is a character. Using it for boss summons would
misattribute adjacent object-table slots.

The modern
[NPC spawn packet](https://github.com/aers/FFXIVClientStructs/blob/b69073ae2d789527fc1b9a78aa7aa590e0f299dc/FFXIVClientStructs/FFXIV/Client/Game/Network/SpawnPackets.cs)
has the following candidate evidence:

| Field | Meaning and investigation use |
| --- | --- |
| `Common.OwnerId` | The explicit native owner. Check whether it exists at birth even when the later actor is ownerless. |
| `Common.TetherTargetId` and `TetherId` | A channeling relationship, not necessarily ownership. Older packet research called this a parent actor; that label is insufficient evidence. |
| `MaxLinks`, `LinkFamily`, `LinkRange` | NPC link-family behavior. A shared family can group NPCs without identifying a summoning boss. |
| `Common.EventId`, `LayoutId`, `GimmickId` | Script/layout context. Compare boss, mechanic actors and unrelated NPCs before interpreting a match. |
| Spawn status source IDs | Who supplied an active status, which can differ from who created the NPC. Useful corroboration, not ownership by itself. |

An older Sapphire parsing utility labels the field corresponding to modern
`OwnerId` as `spawnerId`, and the tether field as `parentActorId`. These are not
additional independent boss-parent fields. Use the pinned layout above rather
than importing those older names into production logic.

The [packet dispatcher signature](https://github.com/aers/FFXIVClientStructs/blob/b69073ae2d789527fc1b9a78aa7aa590e0f299dc/FFXIVClientStructs/FFXIV/Client/Network/PacketDispatcher.cs)
matches once at RVA `0xB362A0` in the installed executable. Disassembly shows it
copying a 0x290-byte NPC packet into a queue. Its arguments identify the arriving
entity and packet; there is no summoning-boss argument. A thread-local boss
action scope cannot safely be presumed to cover this later network spawn.

## Diagnostic build

`NpcSpawnObserver` is an independent, optional observer. Its hook is disabled
outside an explicit `/bcef trace`. It passes the original entity ID and packet
pointer through exactly once and does not modify packet data. An unsupported
signature or diagnostic error does not disable existing haptics.

The trace adds:

- `NpcSpawns`: up to 256 NPC spawn observations with elapsed time, explicit
  owner, tether, link-family, event/layout/gimmick and active status-source data.
- `NpcActors`: up to 512 distinct metadata snapshots of live battle NPCs,
  including those excluded by the current enemy filter. This also captures bosses
  that were already present before recording. It records shared-group and
  event-handler pointer identities without traversing those native objects.
- `SpawnTraceError`: a separate diagnostic setup/observation error, if any.

Packet metadata is diagnostic evidence only. It is never fed into the enemy
roster or ownership classifier. The actor snapshots and serialization run on the
framework thread during recording; the audio render path is unchanged. No packet
names, chat text or original audio are stored by this addition.

The current observer handles the validated normal-NPC spawn packet; it is not a
complete decoder for every boss/player/object spawn variant. An empty spawn list
alone cannot prove that no relationship exists. Actors already present before the
trace must be distinguished from new spawns and unsupported variants.

## Recorded relationships

The trace at 2026-11-03T15:15:02Z is from 月读 (Tsukuyomi), rather than the
earlier Spectral Necromancer fight. Local Chinese game data identifies root name
7225/base 8720 and summoned 舞扇 (fans), name 7229/base 8769. Nine fan spawns
were recorded. All have OwnerId and tether target `0xE0000000`, tether ID zero,
MaxLinks/LinkFamily zero, and no active status-source entries. LinkRange is 20,
which has no ownership meaning without a link family. A temporary spawn-time
owner therefore did not explain these actors.

The boss, its same-name sound helper and fans all have EventId `0x80034E57`.
Their shared-group and event-handler pointers are zero. Layout IDs distinguish
the boss (7345441), sound helper (7398130) and fans (7398121), but no direct boss
entity link was found. Equality of the event context is evidence of common
content, not proof of a summoning parent. The tested layout IDs were not Excel
Level row IDs, so treating them as direct Level-row relationships also failed.

An offline check reused Lumina's
[LGB parser](https://github.com/NotAdam/Lumina/blob/8802d66a3cb3032be2fcf998bb10e864221bdb13/src/Lumina/Data/Files/LgbFile.cs)
to inspect `planevent.lgb`, `planlive.lgb` and `bg.lgb` for 月读 and The Heroes'
Gauntlet. None of those top-level files contained BattleNPC entries. Their
`BoundInstanceID`/NPC link metadata therefore could not supply the recorded
boss-summon relationship. This is evidence about these inspected client files;
it does not establish that no other scene asset or native subsystem could help.

The follow-up trace at 15:16:02Z explains the line-stack miss. The stack marker's
audio is already Boss/BossMechanic, while the attack's
`Tsukuyomi_Smoke_Raid_c` audio belongs to a different ownerless NPC,
name 7230/base 8770. Its timeline 4575 is confirmed Skill/Execution but its
category is Regular. With Regular enemy skills off, the impact is correctly
captured yet has no enabled output category. No SCD filename rule is needed.

## Generic fallback and its limits

The completed `EnemyEncounterCatalog` change provides encounter-category grouping:
in a single-party eight-player duty, while both the player and a recognized boss
are in combat, hostile regular combatants become `EncounterAdd` actors in the
boss output category. They retain regular skill evidence; basic attacks and
unclassified sounds remain excluded. When that context ends, their category
returns to Regular and queued identities are invalidated. Native OwnerId stays
unchanged, and no exact boss parent or skill ID is fabricated.

Version 0.14.17 reuses this implementation for the fan and stack-impact cases.
Regression fixtures exercise both captured NPC/timeline identities and fourteen
unrelated synthetic combinations, including marker/impact separation, basic and
unknown sound rejection, boss-body exclusion, positional ownership and category
transitions. Trace frames now include the duty, queue/member/alliance counts,
combat states and the actual include-adds decision. This distinguishes a disabled
encounter policy from failed skill detection in the live check.

This policy has broader coverage than a per-boss NPC table, but is not a universal
ownerless-summon resolver. Four-player dungeons, alliance raids and other duty
types keep existing native ownership and verified fallback behavior. Shared event
context alone must not enable all actors across a multi-boss dungeon. Extending
beyond the active eight-player policy requires stronger encounter/creation
evidence or an explicitly designed broader grouping policy.

## Non-hostile encounter performers (0.14.20)

The retained 2026-11-04T00:26:33Z and 00:27:20Z recordings identify the missing
剑气波动 and complete 月下彼岸花 sources as ownerless, non-hostile actors.
Their EventId matches the active boss. The primary EventHandlerContent enum
labels the upper word 0x8003 as InstanceContentDirector, establishing common
instance membership rather than summoning parentage.

The new EncounterScript role applies only to placed BattleNpc 1/5 performers
inside the existing active eight-player policy. A matching recognized root must
still be present, and its event must be in the current active-boss event list.
Unknown appearance VFX and body audio receive no blanket helper promotion:
nonzero confirmed released timelines and the validated binding/bridge are required.
This covers execution presentations whose damage is resolved by a separate action.
Known root bosses retain their stricter attack-type requirement. No proximity,
recent-cast timer, localized name or per-encounter NPC rule is introduced.

## Further relationship trials

Start recording before the mechanic actors appear. Match spawn entity IDs against
the live boss, summoned NPCs and unrelated NPCs. First check for an explicit
spawn-time owner that later disappears. If one is observed, preserve it in a
bounded identity/lifetime-aware association rather than adding NPC-base rules.

If owners are absent, compare the other relationships and determine their actual
semantics before choosing a generic resolver. A matching event, tether, status
source or family by itself is not enough. Capture a contrasting case if it could
also describe an ordinary add or unrelated actor. If no explicit relationship is
available, report that limit; do not quietly replace it with proximity or
recent-cast timing.

The existing duty-specific fallbacks remain in place for verified mechanics.
The broader design and remaining coverage gaps are tracked in the
[coverage strategy](ENEMY-HAPTICS-COVERAGE.md).
