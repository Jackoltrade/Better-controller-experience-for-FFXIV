# Public releases

The distribution target is a **custom Dalamud repository** using the normal release channel. Users do not need to enable testing builds. This does not imply official main-repository approval. The public user guides are [English](../README.md), [简体中文](../README.zh-CN.md) and [日本語](../README.ja.md).

For official main-repository review, see [Official Dalamud listing](OFFICIAL-LISTING.md).

## Before a release

1. Finish the [QA checks](QA-REPORT.md), including an in-game load/unload and focus/reconnect smoke test on the intended client build. Record what was actually tested; a native-mixer fixture does not establish compatibility with a different game patch.
2. Update version in the project, `Plugin.cs`'s assembly attribute and `DualSenseLighting.json`. They must agree. Version `0.20.1.0` uses tag `v0.20.1`.
3. Add Afdian/Ko-fi profile URLs to `Core/PluginInfo.cs` when the maintainer supplies them. Empty URLs intentionally keep those buttons disabled; do not invent profiles. Test each real destination when adding it.
4. Confirm source and all third-party licenses are included in the repository. Do not add `work/`, user configuration, diagnostic traces, native research binaries or private screenshots to a release.
5. Use matching **Dalamud API 15** libraries and .NET 10 SDK. The CI default is Dalamud's official staging ZIP; override `DALAMUD_DOWNLOAD_URL` or the manual workflow input if that feed has moved to another API. The metadata verifier rejects API mismatches.

The checked-in JSON is the standard packager's metadata source. DalamudPackager generates the official manifest and ZIP; our public packager verifies those exact files before adding CN/JP installer metadata and READMEs. The generated official output is not overwritten. Development installs read the separate `artifacts/plugin/DualSenseLighting.json`: the build adds the source `DescriptionLoc` and `PunchlineLoc` to that companion so XIVLauncherCN can display Chinese. Public packaging reads the untouched generated manifest under `artifacts/plugin/DualSenseLighting/`, independent of development metadata.

## Build locally

```powershell
./tools/build.ps1 -DalamudLibPath 'C:\path\to\Dalamud' -LockedRestore
dotnet ./artifacts/checks/LightingChecks.dll --benchmark
python -m unittest discover -s tests -p "test_*.py" -v
python tools/package-release.py --tag v0.20.1
```

On WSL, `python3 tools/build-wsl.py --locked-restore` runs the same Windows .NET 10 SDK build and verification. Pass `--dotnet-path /mnt/c/path/to/sdk/dotnet.exe` for a portable SDK. The offline Roslyn fallback is no longer used.

For a portable SDK or multiple .NET installations, pass
`-DotnetPath 'C:\path\to\sdk\dotnet.exe'` to select the host explicitly. The
script prints the selected host and checks each child process's exit status.

The checks cover report-field preservation, focus loss, reconnects, cancellation, animations, sound ownership, native haptic routing, distance cutoffs, footstep gating, translations and About links. The benchmark uses synthetic PCM and reports CPU time and current-thread managed allocations; it is not an in-game FPS measurement.

The SDK pins dependency versions in `src/DualSenseLighting/packages.lock.json`. After intentional dependency updates, restore without `-LockedRestore`, review the lockfile, then run locked restore and checks again. Commit the project, packaging target and lockfile together.

The standard Release build produces `DualSenseLighting/latest.zip`, `DualSenseLighting/DualSenseLighting.json` and `DualSenseLighting/images/icon.png` under its output directory. `tools/build.ps1` stages them under `artifacts/plugin/` after checks pass. CI uploads them separately as `official-dalamud-package`. Its ZIP contains only the DLL, generated manifest and four license notices; Dalamud handles the icon as a sidecar.

### Development builds

Production is the default and compiles diagnostics out of the DLL. For local
recording and framework timing, explicitly run:

```powershell
./tools/build.ps1 -DalamudLibPath 'C:\path\to\Dalamud' -LockedRestore -Development
```

Load `artifacts/dev-plugin/DualSenseLighting.dll` through Dalamud's development
flow. This separate optimized build includes diagnostics but still checks `IsDev`
before exposing them. Its output and intermediate directories are separate from
production, and it produces no installer ZIP. Public packaging rejects a build
unless its verified metadata declares `DiagnosticsEnabled: false`.

The public packaging tool requires that exact standard ZIP and sidecars, an optimized x64 DLL and matching identity, version, API level, source and SHA-256 proof. It writes:

| File | Purpose |
| --- | --- |
| `artifacts/release/latest.zip` | Plugin DLL, English/Chinese manifest, icon, notices and three concise READMEs |
| `artifacts/release/latest-ja.zip` | Legacy package for subscribers to the older Japanese installer feed |
| `artifacts/release/pluginmaster.json` | Default installer feed, with Chinese launcher translations |
| `artifacts/release/pluginmaster-ja.json` | Legacy Japanese installer feed; new installs use the shared `repo.json` |
| `artifacts/release/SHA256SUMS.txt` | SHA-256 for both ZIPs |
| `artifacts/release/release-summary.json` | Tag, version, target download URL and archive contents |

The supplied `thumbnail.png` is copied unchanged to the 512×512 plugin icon, `src/DualSenseLighting/Assets/icon.png`. Both build paths copy it to `images/icon.png` beside the DLL, and packaging checks it matches the source. The manifest's `IconUrl` supplies the repository image for third-party listings once published; development plugins load the bundled file. This follows [Dalamud's icon loader](https://github.com/goatcorp/Dalamud/blob/master/Dalamud/Interface/Internal/Windows/PluginImageCache.cs).

The store entry sets `IsTestingExclusive=false` and uses the verified DLL's actual `AssemblyVersion` and `DalamudApiLevel`. It has no separate `TestingAssemblyVersion` or `TestingDalamudApiLevel`. Each feed points all three download links to its matching versioned ZIP; the testing download link is retained for existing clients without advertising a separate beta. Bundled user guides keep links to other bundled files local; images and technical documents outside the ZIP link to the matching source tag.

The root feed advertises the last verified published package on the normal channel. Update it only after publishing and verifying the new versioned ZIP; changing the feed does not rebuild or replace that ZIP. Both release-hosted feeds also use the normal channel for newly published releases.

## Installer descriptions

Keep the default English `Description` and `Punchline` concise. Both should make
regular controller rumble visible alongside DualSense features. The Chinese
launcher reads `DescriptionLoc` / `PunchlineLoc` and shows the original on hover;
this is a [CN launcher feature](https://github.com/ottercorp/Dalamud/blob/5f72511ebfddf65db3cbca5aaec4317dbb7e6d62/Dalamud/Interface/Internal/Windows/PluginInstaller/PluginInstallerWindow.cs#L2227), independent of the character's server or plugin UI language.

[Official Dalamud manifests](https://github.com/goatcorp/Dalamud/blob/a74096043b58d69b08af214ac724f3d42c3a238b/Dalamud/Plugin/Internal/Types/PluginManifest.cs)
use default description fields. The shared `repo.json` contains English defaults
and the Chinese launcher's localized fields. The plugin settings UI independently
supports English, Chinese and Japanese.

For compatibility with existing Japanese-feed subscribers, Japanese copy remains in
`src/DualSenseLighting/Manifest.ja.json`, containing only `Description` and
`Punchline`. Packaging retains its release-hosted feed and ZIP with the same DLL,
internal ID and version. These are legacy endpoints; all new installation guides
use the one shared raw feed.

Publishing verifies both versioned ZIPs before uploading missing assets, then
updates both installer feeds. Translation edits cannot override plugin identity,
download destinations or executable content.

## Publish with GitHub Actions

Publish only when the maintainer explicitly requests a release. A request to
commit, push, build, package or tag changes alone does not authorize publishing.
The release workflow is manual-only; neither branch nor tag pushes publish.

1. Review and commit the intended source, user guides and workflows, then push them to `main`.
2. Create and push the matching version tag, for example `v0.19.8`.
3. Once a release is explicitly requested, manually run **Publish a public plugin release** from GitHub Actions with that existing tag and a compatible Dalamud library URL.
4. The reusable build job checks out the tag, compiles and runs the tests, validates the DLL, and uploads the public package artifact.
5. Only after all checks pass does the publishing job create a normal GitHub release, upload the ZIP/checksum/summary, and update the `plugin-repository` release's installer feeds.

The build job has read-only repository permissions. Only the manual publishing job receives `contents: write`. No workflow commits to `main`, force-pushes a branch, sends feedback email, or uses a personal access token.

The publishing job first exercises its recovery logic offline. If publication
stops partway through, retry with the original build artifact: existing versioned
assets must match byte for byte, missing assets are added, and a completed draft
is published before the installer feed is updated. A matching prerelease is
promoted only after all assets have been verified. A differing asset stops the
retry without overwriting versioned files or changing release status. A rebuilt artifact may
differ even at the same source tag; use a new version when the bytes differ.

## Installer feed

The primary installer URL reads `repo.json` from the root of `main`:

```text
https://raw.githubusercontent.com/Jackoltrade/Better-controller-experience-for-FFXIV/main/repo.json
```

All languages use this one store-entry array. After each new release, refresh
it from the published `pluginmaster.json` asset. Its download links point to versioned
GitHub Release ZIPs. English installer descriptions and Chinese launcher
translations share the same JSON. Japanese remains available in the plugin UI.
The raw URL becomes available when `repo.json` is committed and pushed to `main`;
GitHub Pages is independent of this feed.

The existing release-hosted installer URLs remain supported. The release
workflow refreshes those assets after uploading each verified package, so
existing subscribers can keep their current URLs.

### Raw-feed setup and updates

1. After an explicitly requested release finishes successfully, download the
   published default feed into the project root. In PowerShell, from the project folder:

   ```powershell
   $bcefFeedBase = 'https://github.com/Jackoltrade/Better-controller-experience-for-FFXIV/releases/download/plugin-repository'
   Invoke-WebRequest -UseBasicParsing -Uri "$bcefFeedBase/pluginmaster.json" -OutFile repo.json
   ```

2. Check that `AssemblyVersion` matches the published version,
   `IsTestingExclusive` is `false`, and `DownloadLinkInstall` points to its published ZIP. Preserve the plugin
   identity and localized descriptions. This command displays
   the relevant fields:

   ```powershell
   Get-Content repo.json -Raw -Encoding UTF8 | ConvertFrom-Json |
     Select-Object InternalName, AssemblyVersion, IsTestingExclusive, DownloadLinkInstall
   ```

3. Review and commit the root feed with the installation instructions:

   ```text
   git add repo.json README.md README.zh-CN.md README.ja.md docs/RELEASING.md docs/USER-GUIDE.md docs/USER-GUIDE.zh-CN.md docs/USER-GUIDE.ja.md
   git commit -m "Use one shared installer repository feed"
   git push origin main
   ```

4. Open the raw URL in a browser. Confirm that it returns a JSON array with
   `InternalName: DualSenseLighting` and the expected version. Then add the
   same URL in `/xlsettings` under **Experimental → Custom Plugin
   Repositories** and check installation in `/xlplugins` with testing builds disabled.

5. After each later release, repeat the download, validation and feed commit.
   Use an update message such as `Update installer feed for v0.19.9`.

**The release workflow currently updates only the release-hosted feeds. The root
`repo.json` needs this separate commit after each publication.** A commit or push
of this JSON snapshot advertises an already published package; it does not
create another plugin release.

After publishing, fetch the public feed and ZIP in a fresh session, verify the checksum, then test installer discovery/install/update with testing builds disabled. Confirm the built-in name, internal ID and version match. An installed newer version will not downgrade through this feed.

## Manual publishing alternative

Upload the verified ZIP, checksum and summary to a normal GitHub release for the matching tag. Upload `pluginmaster.json` and `pluginmaster-ja.json` as the `plugin-repository` release's assets, replacing their previous copies, then refresh the root feed as described above. Keep the source tag available for GPL correspondence. Check all three user guides' links and install through the public feed before announcing the release.

## Interactive documentation

The README links to the browser-based curve explorer at
`https://jackoltrade.github.io/Better-controller-experience-for-FFXIV/`.
GitHub's README renderer removes scripts, so the interactive controls run on
GitHub Pages rather than inside the README.

Enable hosting once in **Settings → Pages → Build and deployment → Source →
GitHub Actions**. After these changes are committed and pushed to `main`,
**Publish interactive documentation** stages `docs/haptics-curves.html` as the
site's `index.html` and deploys it. Future changes to that page or its Pages
workflow deploy automatically. The workflow can also be run manually on `main`.

After the first successful deployment, open the site and check the curve picker,
sliders and hover details. The page is self-contained and needs no build tools
or external runtime downloads. See [GitHub Pages configuration](https://docs.github.com/en/pages/getting-started-with-github-pages/configuring-a-publishing-source-for-your-github-pages-site).

## Compatibility and review

USB lighting uses the standard DualSense report. Audio, camera and LB integration depend on native game layouts and must be tested after patches. Current historical in-game evidence is documented in [development notes](DEVELOPMENT-NOTES.md); the latest QA report distinguishes that evidence from current tests and pending checks.

Official repository submission is a separate process. Follow Dalamud's current [custom repository documentation](https://dalamud.dev/plugin-publishing/custom-repositories/), [metadata documentation](https://dalamud.dev/plugin-development/plugin-metadata/) and [submission requirements](https://dalamud.dev/plugin-publishing/submission/), including its current policy on AI-assisted code. This project does not claim official review or approval.
