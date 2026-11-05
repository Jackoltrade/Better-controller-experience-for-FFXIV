The offline boss lookup reuses `DungeonBoss.csv` from
[Critical-Impact/LuminaSupplemental](https://github.com/Critical-Impact/LuminaSupplemental),
commit `bd265c2b39868387937ae5b0c038593af997e0f9`, under GPL-3.0 (see `LICENSE`).
Original path: `src/LuminaSupplemental.Excel/Generated/DungeonBoss.csv`.
The upstream file is retained unchanged as `enemy-bosses.csv`.

Run `python3 tools/generate-enemy-bosses.py` after updating that CSV;
`--check` verifies the generated C# lookup without network access.
The plugin performs no runtime downloads and needs no extra dependency.

The [coverage strategy](../docs/ENEMY-HAPTICS-COVERAGE.md) distinguishes reusable
sound/ownership mechanisms from encounter-specific associations. Boss trials
should broaden those mechanisms; catalog entries alone do not verify skill audio.

## Boss recognition and current encounter policy

The CSV identifies duty bosses by the current ContentFinderCondition ID and the
caster's BNpcName ID. Open-world classification additionally uses the installed
Fate, BNpcBase and NotoriousMonster sheets through `EnemyEncounterCatalog`.
Actual boss FATE membership and NPC rank distinguish eligible bosses from ordinary
event enemies; exact name/base pairs identify registered B/A/S Hunt marks. No
downloaded list is needed. These are recognition sources, not a universal native
boss flag or proof that every boss's audio has been tested.

Since 0.15.0, while both the player and a recognized boss are in combat in an
instanced duty, eligible NPC SFX follows **Boss SFX** without a party-size limit.
The source must still have verified native audio-to-NPC ownership, but it does
not need a summon-parent link, matching boss name or recognized skill metadata.
Basic attacks, casting and warning cues can react in this context. Players,
player-owned descendants and unidentified audio remain excluded. Arena/map audio
uses its separate catalog, non-loop and live playback/position checks; arbitrary
environmental audio is not enabled.

Regular hostile NPCs independently identified before boss combat retain the
Regular category. Their exact-instance marks survive combat ending, focus loss
and feature toggles, and reset on territory, duty or player-entity change or plugin
reload. A regular enemy first seen during boss combat can follow Boss SFX. This
is encounter grouping, not a claim of universal boss-summon ownership. An
unrecognized boss cannot activate the broad rule by itself. See the
[current policy and limits](../docs/ENEMY-HAPTICS-COVERAGE.md).

## Legacy helper and summon associations

Outside the active duty boss context, existing skill/effect filters and helper
associations remain in use. An unrecognized hostile combatant uses the Regular
category unless other recognition metadata or a verified association identifies
it as a boss source. Sound-only mechanic emitters (subtype 11 in the pinned client)
can qualify through a matching NPC name ID and a live recognized boss. An earlier
trial observed boss base 13294 and helper base 9020 sharing a name; no exact action
ID was inferred. Helpers without an established association remain excluded on
this legacy path.

Boss-owned battle NPCs also inherit the boss channel through a direct native
OwnerId link to a live boss. Some mechanic actors have no owner and a different
NPC name ID. Verified exceptions live in `Core/BossMechanics.cs`, maintained by
this project, separately from the unchanged upstream CSV. Current associations
in The Heroes' Gauntlet (duty 737) are:

| Mechanic | NPC name ID | Base entries | Boss name ID |
| --- | --- | --- | --- |
| Spectral Thief's Chicken Knife | 9507 | 11889 | 9505 |
| Spectral Necromancer's first Necrobomb variant | 9509 | 11766, 11767, 11768, 11769 | 9508 |
| Spectral Necromancer's second Necrobomb variant | 9510 | 11762, 11763, 11764, 11765 | 9508 |
| Spectral Berserker's rubble | 9512 | 12030 | 9511 |

Read-only game data, recorded actor identities and user tests establish these
mechanics. The association applies to the actor's effects, not a list of skill
IDs or sound names. A matching boss must be
present, and an explicit conflicting native owner takes precedence over this
fallback. Other ownerless summons need verified associations on this legacy
path; proximity alone is not evidence. During active duty boss combat, the
broader policy above can include their verified NPC audio without this fallback.

Verified encounter mechanic actors may emit their delayed AVFX after using a
basic attack earlier. That history alone does not exclude their mechanic effects.
On the legacy filtered path, explicit auto-attack scopes and character/voice
sounds remain excluded. Generic native-owned fighters and sound-only helpers
retain the stricter unknown-AVFX
guard after observed auto-attacks. This does not establish an exact action ID
for every asynchronously emitted sound.

The former eight-player-only promotion and stricter scripted-performer rules
are historical, superseded by the all-size active duty policy above. Their
investigation and earlier test evidence are retained in the
[coverage history](../docs/ENEMY-HAPTICS-COVERAGE.md) and
[ownerless-summon investigation](../docs/OWNERLESS-SUMMON-INVESTIGATION.md).
