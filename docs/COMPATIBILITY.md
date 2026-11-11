# Client compatibility check — 2026-11-11

Performance optimization remains the current priority. No speculative native
offset or signature changes were made for patch 7.58.

## Evidence

The [official 7.58 notes](https://na.finalfantasyxiv.com/lodestone/topics/detail/27a249e737d7b041332c7018949035b03d62c081/)
identify the new patch. The upstream client-structure project's
[7.58 data](https://github.com/aers/FFXIVClientStructs/blob/80cb4a7b7e11a6702e2eb8839afd301a918c638e/ida/data.yml)
identifies build `2026.11.03.0000.0000`. Its
[bounded patch changes](https://github.com/aers/FFXIVClientStructs/compare/080486e9abc80131655d6dc47a42fe34154d06e5...80cb4a7b7e11a6702e2eb8839afd301a918c638e)
do not change the GameObject, Character, ActionManager, camera, scheduler or audio
declarations used here. None of the specifically changed UI/zoning/map-effect
methods is referenced by this plugin. This does not prove all native internals
are unchanged.

| Installed client | Recorded build | Checks |
| --- | --- | --- |
| Chinese, currently running | `2026.11.03.0000.0000` | All 48 hardcoded signature patterns match; selected motion, camera and optional development-observer body/layout predicates pass |
| International, cached installation | `2026.10.16.0000.0000` | Same bounded checks pass, but this older build is not a fresh international 7.58 test |

The two multi-match patterns are intentional and require the existing additional
native validation; a pattern match alone is not permission to bind a hook.
Actual compiled layout/API fixtures also use the installed Chinese Dalamud API 15
libraries. Raw signatures, binary hashes, predicates and primary-source receipts
are preserved locally under `work/review-followup-20261111/native-compat/`.

The current Chinese session also logs successful plugin startup at **23:07 CST**
from the configured production-DLL path, with no plugin-tagged warning/error
after that startup in the inspected log. Process identity and the on-disk DLL
hash are recorded in `work/performance-20261111/chinese-startup.json`. No
in-memory DLL hash, physical output or frame-time result was obtained from this
read-only check.

## Gameplay confirmation and remaining scope

The user selected the Chinese client and confirmed the requested optimized
production-DLL smoke check on November 11: **“Available features pass; Diagnostics
is absent.”** The question covered usual lighting/SFX/haptics, language switching,
Info/Reset Escape handling, Alt-Tab release and reconnect. This is user-assisted
confirmation of the available features in that session, not exhaustive hardware
coverage or a new FPS/latency measurement.

The international installation will be updated later; full live international
7.58 verification remains pending. Do not describe the cached international
executable check as a completed 7.58 hardware or runtime test.
