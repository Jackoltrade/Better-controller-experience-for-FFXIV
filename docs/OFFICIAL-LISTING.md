# Official Dalamud listing

Checked **2026-11-05** for **Better controller experience for FFXIV**, v0.18.0.

## Submission route

For the global main repository, open a pull request to
[goatcorp/DalamudPluginsD17](https://github.com/goatcorp/DalamudPluginsD17).
New plugins start in `testing/live`. Plogon builds a specific public source commit;
approval and testing precede promotion to stable. Our custom GitHub release feed
is a separate distribution channel. [Submission process](https://dalamud.dev/plugin-publishing/submission/)
· [Approval process](https://dalamud.dev/plugin-publishing/approval-process/)

The submission uses the existing internal ID, preserving installed settings:

```text
testing/live/DualSenseLighting/
  manifest.toml
  images/icon.png
```

Example configuration, with the final public commit still to be supplied:

```toml
[plugin]
repository = "https://github.com/Jackoltrade/Better-controller-experience-for-FFXIV.git"
commit = "REPLACE_WITH_REVIEWED_PUBLIC_COMMIT_SHA"
owners = ["Jackoltrade"]
project_path = "src/DualSenseLighting"
```

This example is configuration, not a submission PR description.

## Current project readiness

| Area | Current state | Preparation needed |
| --- | --- | --- |
| Public repository | Repository is public; this revision includes complete v0.18 source and assets | Push the reviewed commit, verify CI and pin its SHA |
| D17 build | Dalamud.NET.Sdk/15.0.0 with standard DalamudPackager output | Completed locally; verify the pushed CI build |
| Dependency lock | packages.lock.json included with the SDK project; CI requires locked restore | Verify locked restore in the pushed CI run |
| Settings window | SettingsWindow and shared DialogWindow registered with WindowSystem; compiled API checks and live Info/Reset Escape checks pass | Complete the remaining layout and close/reopen checks |
| Icon | Valid 512×512 PNG; emitted as images/icon.png | Include it with the D17 manifest and address AI-asset disclosure |
| Native compatibility | Existing hardware evidence is from the CN development setup | Validate supported global client layouts and load/unload behavior |

The SDK supplies framework references, reproducible-build settings, dependency
locking and DalamudPackager. Our project explicitly includes preview resources,
the icon and license notices. Release packaging uses an allowlist; public
localized packages verify the standard output and retain CN/JP installer text.
The settings window now uses Dalamud's Windowing API while preserving its fixed
width, language font and worker visibility signal. See the
[build and release procedure](RELEASING.md).
[D17 preparation and technical criteria](https://github.com/goatcorp/DalamudPluginsD17#publishing-your-plugin)
· [Sample project](https://github.com/goatcorp/SamplePlugin/blob/master/SamplePlugin/SamplePlugin.csproj)

## Feature and AI review

Combat plugins require prior discussion with the approval team. Treat our
eligibility as a question for them, not an established approval. Current behavior:

| Feature | PvP behavior |
| --- | --- |
| Player SFX haptics, spatial direction and distance | Available |
| RGB/SFX lighting and own LB LEDs | Available |
| Native footstep amplification | Available with supported USB output |
| Enemy/hit haptics | Paused |
| Screen-effect haptics | Paused |

Explain that feedback derives from game sounds, positions and visible gauges,
including enemy/boss classification. Ask specifically about combat/PvP scope and
any required gating; do not describe the entire plugin as paused in PvP.
[Plugin restrictions](https://dalamud.dev/plugin-publishing/restrictions/)

The global AI policy requires disclosure beyond basic autocomplete, personal
testing and a maintainer who understands and can explain the implementation.
Copilot/Auto-level submissions receive discretionary review; submissions without
meaningful human involvement are rejected. The maintainer must write the PR
description and AI disclosure personally. AI-created assets also need disclosure
in the plugin description; reviewers may request a handmade icon. Earlier
AI-assisted thumbnail versions and prompt records remain in Assets; the active
icon now comes from the user-provided thumbnail.png. Confirm its provenance and
disclose any retained AI-created material. AI-assisted translations are permitted
with review. [AI policy](https://dalamud.dev/plugin-publishing/ai-policy/)

## Chinese launcher

The CN server's published implementation merges upstream global listings with
CN builds, giving CN overrides priority. Global inclusion can therefore flow
through to the CN list, subject to synchronization, API and client compatibility;
a separate CN PR is not universally required. [Merge implementation](https://github.com/ottercorp/XLWebServices-fastapi/blob/e9e3a27e6e35fb24419575a03c284747a85e0572/app/utils/tasks.py#L269)

CN-specific adaptations or overrides use
[ottercorp/DalamudPluginsD17](https://github.com/ottercorp/DalamudPluginsD17#提交),
whose README documents its submission and review process. Follow that route's
current rules rather than assuming a global approval establishes CN compatibility.

The recommended first step is a maintainer discussion of our exact combat/PvP
behavior and AI involvement. Then finish the live smoke checks, publish a tested
public commit, and prepare the testing-track submission.
