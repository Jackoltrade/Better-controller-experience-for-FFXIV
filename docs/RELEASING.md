# Public test releases

The distribution target is an **opt-in custom Dalamud testing repository**, not official main-repository approval. The public user guides are [English](../README.md), [简体中文](../README.zh-CN.md) and [日本語](../README.ja.md).

For official main-repository review, see [Official Dalamud listing](OFFICIAL-LISTING.md).

## Before a release

1. Finish the [QA checks](QA-REPORT.md), including an in-game load/unload and focus/reconnect smoke test on the intended client build. Record what was actually tested; a native-mixer fixture does not establish compatibility with a different game patch.
2. Update version in the project, `Plugin.cs`'s assembly attribute and `DualSenseLighting.json`. They must agree. Version `0.18.0.0` uses tag `v0.18.0`.
3. Add Afdian/Ko-fi profile URLs to `Core/PluginInfo.cs` when the maintainer supplies them. Empty URLs intentionally keep those buttons disabled; do not invent profiles. Test each real destination when adding it.
4. Confirm source and all third-party licenses are included in the repository. Do not add `work/`, user configuration, diagnostic traces, native research binaries or private screenshots to a release.
5. Use matching **Dalamud API 15** libraries and .NET 10 SDK. The CI default is Dalamud's official staging ZIP; override `DALAMUD_DOWNLOAD_URL` or the manual workflow input if that feed has moved to another API. The metadata verifier rejects API mismatches.

The checked-in JSON is the standard packager's metadata source. DalamudPackager generates the official manifest and ZIP; our public packager verifies those exact files before adding CN/JP installer metadata and READMEs. The generated official output is not overwritten. Development installs read the separate `artifacts/plugin/DualSenseLighting.json`: the build adds the source `DescriptionLoc` and `PunchlineLoc` to that companion so XIVLauncherCN can display Chinese. Public packaging reads the untouched generated manifest under `artifacts/plugin/DualSenseLighting/`, independent of development metadata.

## Build locally

```powershell
./tools/build.ps1 -DalamudLibPath 'C:\path\to\Dalamud' -LockedRestore
dotnet ./artifacts/checks/LightingChecks.dll --benchmark
python -m unittest discover -s tests -p "test_*.py" -v
python tools/package-release.py --tag v0.18.0
```

On WSL, `python3 tools/build-wsl.py --locked-restore` runs the same Windows .NET 10 SDK build and verification. Pass `--dotnet-path /mnt/c/path/to/sdk/dotnet.exe` for a portable SDK. The offline Roslyn fallback is no longer used.

For a portable SDK or multiple .NET installations, pass
`-DotnetPath 'C:\path\to\sdk\dotnet.exe'` to select the host explicitly. The
script prints the selected host and checks each child process's exit status.

The checks cover report-field preservation, focus loss, reconnects, cancellation, animations, sound ownership, native haptic routing, distance cutoffs, footstep gating, translations and About links. The benchmark uses synthetic PCM and reports CPU time and current-thread managed allocations; it is not an in-game FPS measurement.

The SDK pins dependency versions in `src/DualSenseLighting/packages.lock.json`. After intentional dependency updates, restore without `-LockedRestore`, review the lockfile, then run locked restore and checks again. Commit the project, packaging target and lockfile together.

The standard Release build produces `DualSenseLighting/latest.zip`, `DualSenseLighting/DualSenseLighting.json` and `DualSenseLighting/images/icon.png` under its output directory. `tools/build.ps1` stages them under `artifacts/plugin/` after checks pass. CI uploads them separately as `official-dalamud-package`. Its ZIP contains only the DLL, generated manifest and four license notices; Dalamud handles the icon as a sidecar.

The public packaging tool requires that exact standard ZIP and sidecars, an optimized x64 DLL and matching identity, version, API level, source and SHA-256 proof. It writes:

| File | Purpose |
| --- | --- |
| `artifacts/release/latest.zip` | Plugin DLL, English/Chinese manifest, icon, notices and three concise READMEs |
| `artifacts/release/latest-ja.zip` | Same plugin files, with Japanese installer metadata |
| `artifacts/release/pluginmaster.json` | Default testing feed, with Chinese launcher translations |
| `artifacts/release/pluginmaster-ja.json` | Japanese testing feed |
| `artifacts/release/SHA256SUMS.txt` | SHA-256 for both ZIPs |
| `artifacts/release/release-summary.json` | Tag, version, target download URL and archive contents |

The supplied `thumbnail.png` is copied unchanged to the 512×512 plugin icon, `src/DualSenseLighting/Assets/icon.png`. Both build paths copy it to `images/icon.png` beside the DLL, and packaging checks it matches the source. The manifest's `IconUrl` supplies the repository image for third-party listings once published; development plugins load the bundled file. This follows [Dalamud's icon loader](https://github.com/goatcorp/Dalamud/blob/master/Dalamud/Interface/Internal/Windows/PluginImageCache.cs).

The store entry sets `IsTestingExclusive=true`, a placeholder stable version `0.0.0.0`, and the actual `TestingAssemblyVersion`/`TestingDalamudApiLevel`. Each feed points all three download links to its matching versioned ZIP. Bundled user guides keep links to other bundled files local; images and technical documents outside the ZIP link to the matching source tag. Do not promote it to stable by editing a version number without the corresponding testing and review.

## Installer descriptions

Keep the default English `Description` and `Punchline` concise. Both should make
regular controller rumble visible alongside DualSense features. The Chinese
launcher reads `DescriptionLoc` / `PunchlineLoc` and shows the original on hover;
this is a [CN launcher feature](https://github.com/ottercorp/Dalamud/blob/5f72511ebfddf65db3cbca5aaec4317dbb7e6d62/Dalamud/Interface/Internal/Windows/PluginInstaller/PluginInstallerWindow.cs#L2227), independent of the character's server or plugin UI language.

[Official Dalamud manifests](https://github.com/goatcorp/Dalamud/blob/a74096043b58d69b08af214ac724f3d42c3a238b/Dalamud/Plugin/Internal/Types/PluginManifest.cs)
use default description fields. Japanese copy lives in
`src/DualSenseLighting/Manifest.ja.json`, containing only `Description` and
`Punchline`. Packaging generates a Japanese feed and ZIP with Japanese defaults
and the same DLL, internal ID and version. This keeps both available and installed
listings localized. Users choose one feed; changing the settings UI language does
not switch installer metadata. Existing installations need an update or reinstall
from the selected package to replace local English metadata.

Publishing verifies both versioned ZIPs before uploading missing assets, then
updates both installer feeds. Translation edits cannot override plugin identity,
download destinations or executable content.

## Publish with GitHub Actions

These workflows are prepared in source; editing them does not publish a release.

1. Review and commit the intended source, user guides and workflows, then push them to `main`.
2. Create and push the matching version tag, for example `v0.18.0`.
3. Run **Publish an opt-in public test release** from GitHub Actions with that existing tag and a compatible Dalamud library URL.
4. The reusable build job checks out the tag, compiles and runs the tests, validates the DLL, and uploads the public package artifact.
5. Only after all checks pass does the publishing job create a **prerelease**, upload the ZIP/checksum/summary, and update the `plugin-repository` release's `pluginmaster.json` asset.

The build job has read-only repository permissions. Only the manual publishing job receives `contents: write`. No workflow commits to `main`, force-pushes a branch, sends feedback email, or uses a personal access token.

The publishing job first exercises its recovery logic offline. If publication
stops partway through, retry with the original build artifact: existing versioned
assets must match byte for byte, missing assets are added, and a completed draft
is published before the installer feed is updated. A stable release or a differing
asset stops the retry without overwriting versioned files. A rebuilt artifact may
differ even at the same source tag; use a new version when the bytes differ.

## Installer feed

The persistent installer URL is:

```text
https://github.com/Jackoltrade/PS5-DualSense-Controller-for-FFXIV-/releases/download/plugin-repository/pluginmaster.json
```

It becomes valid after the first successful publishing run. Subsequent runs replace that JSON asset after uploading the new versioned ZIP; users opt into testing plugins in Dalamud. An initial GitHub repository or an uploaded Actions artifact alone does not make this feed live.

After publishing, fetch the public feed and ZIP in a fresh session, verify the checksum, then test installer discovery/install/update with testing enabled. Confirm the built-in name, internal ID and version match. A stable plugin already installed at a higher version will not downgrade into the test feed.

## Manual publishing alternative

Upload the verified ZIP, checksum and summary to a GitHub prerelease for the matching tag. Upload `pluginmaster.json` as the `plugin-repository` release's asset, replacing its previous copy. Keep the source tag available for GPL correspondence. Check all three user guides' links and install through the public feed before announcing the release.

## Compatibility and review

USB lighting uses the standard DualSense report. Audio, camera and LB integration depend on native game layouts and must be tested after patches. Current historical in-game evidence is documented in [development notes](DEVELOPMENT-NOTES.md); the latest QA report distinguishes that evidence from current tests and pending checks.

Official repository submission is a separate process. Follow Dalamud's current [custom repository documentation](https://dalamud.dev/plugin-publishing/custom-repositories/), [metadata documentation](https://dalamud.dev/plugin-development/plugin-metadata/) and [submission requirements](https://dalamud.dev/plugin-publishing/submission/), including its current policy on AI-assisted code. This project does not claim official review or approval.
