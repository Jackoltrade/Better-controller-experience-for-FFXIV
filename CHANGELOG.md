# Changelog

## 0.13.3 — Feature commands

- Replace `/ds5light` with `/ds5`; update existing macros to the new command.
- Add case-insensitive `on`, `off` and `toggle` controls for lighting, combat haptics, footstep amplification, spatial haptics, distance fade and impact following.
- Add `/ds5 help` and `/ds5 info`; retain `/ds5 trace`.
- Save feature changes without opening settings; share the UI update path and combat-enable cue. Report state and disabled-parent requirements in the selected language.

## 0.13.2 — First-open guide

- Show a translated setup guide the first time settings are opened, with supported controllers, USB requirements, game settings and background behavior.
- Add a top-left Info button to reopen it, with a language selector inside the guide.
- Remember that the guide has been shown across reloads and per-tab resets.

## 0.13.1 — Haptics enable confirmation

- Give two short vibrations when combat haptics is enabled, including while idle.
- Use the existing native vibration bus, with a short-lived mixer detour and no rumble-mode HID commands or speaker audio.
- Cancel the cue on disable, loss of focus, unsupported routing or unload; expire old requests instead of replaying them later.
- Keep startup, focus return and ordinary settings changes silent.

## 0.13.0 — Public testing preparation

- Add a rightmost About tab with project, version, author, license and credits.
- Add source-code, support and feedback actions in English, Simplified Chinese and Japanese. Email feedback uses a fixed plugin-specific subject. Enable Ko-fi support with the maintainer’s profile; Afdian remains unavailable until its URL is supplied.
- Center About text and add icon buttons, with support actions on one row and email/GitHub feedback actions on another.
- Add comprehensive user guides in all three languages.
- Add optimized x64/API/version verification, an allowlisted release ZIP, checksum and an opt-in testing repository manifest.
- Add build/package CI and an explicitly triggered public-test publishing workflow.
- Cache UI labels, reuse already-normalized lighting settings, and reduce unrelated WriteFile hook work.
- Harden partial-startup cleanup, command ownership and repeat disposal.

Includes existing RGB/SFX lighting, animated bottom LEDs/LB display, character combat and spatial haptics, optional native footstep amplification, focus handoff, reconnect recovery, confirmed defaults reset, preview edge fades and localized diagnostics.
