#!/usr/bin/env python3
"""Package a verified build for Dalamud's opt-in public testing channel."""
import argparse
import hashlib
import json
import re
import time
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
REPOSITORY = "https://github.com/Jackoltrade/PS5-DualSense-Controller-for-FFXIV-"
LICENSES = ("LICENSE.txt", "LICENSE-HidLibrary.txt", "LICENSE-FFXIVClientStructs.txt", "THIRD-PARTY-NOTICES.txt")
GUIDES = ("README.md", "README.zh-CN.md", "README.ja.md")


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def source_fingerprint(directory):
    digest = hashlib.sha256()
    extensions = {".cs", ".csproj", ".json", ".png", ".svg"}
    sources = [path for path in directory.rglob("*") if path.is_file() and path.suffix in extensions
               and not {"bin", "obj"}.intersection(path.relative_to(directory).parts)]
    for path in sorted(sources, key=lambda p: p.relative_to(directory).as_posix()):
        digest.update(path.relative_to(directory).as_posix().encode("utf-8") + b"\0")
        digest.update(path.read_bytes())
        digest.update(b"\0")
    return digest.hexdigest()


def package(root, build, output, tag=None):
    source = json.loads((root / "src/DualSenseLighting/DualSenseLighting.json").read_text(encoding="utf-8"))
    manifest = json.loads((build / "DualSenseLighting.json").read_text(encoding="utf-8"))
    proof = json.loads((build / "build-verification.json").read_text(encoding="utf-8"))
    version = manifest["AssemblyVersion"]
    if not re.fullmatch(r"\d+\.\d+\.\d+\.\d+", version):
        raise ValueError("Invalid assembly version")
    if manifest != source:
        raise ValueError("Published manifest is stale; rebuild before packaging")
    if proof.get("SourceSha256") != source_fingerprint(root / "src/DualSenseLighting"):
        raise ValueError("Source changed after build verification; rebuild before packaging")
    for key in ("InternalName", "AssemblyVersion", "DalamudApiLevel"):
        if proof.get(key) != manifest[key]:
            raise ValueError("Build verification differs from manifest: " + key)
    if manifest["InternalName"] != "DualSenseLighting" or proof.get("Architecture") != "x64" or proof.get("Optimized") is not True:
        raise ValueError("A verified, optimized x64 build is required")
    dll = (build / "DualSenseLighting.dll").read_bytes()
    if proof.get("DllSha256") != sha256(dll):
        raise ValueError("DLL changed after build verification")
    expected_tag = "v" + version.removesuffix(".0")
    if tag is not None and tag != expected_tag:
        raise ValueError(f"Release tag must match the assembly: {expected_tag}")
    tag = expected_tag
    payload = {"DualSenseLighting.dll": dll, "DualSenseLighting.json": (build / "DualSenseLighting.json").read_bytes()}
    for name in LICENSES + GUIDES:
        payload[name] = (root / name).read_bytes()
    # An allowlist prevents developer traces, user settings, compiler binaries,
    # Dalamud libraries and local research files from entering a public release.
    output.mkdir(parents=True, exist_ok=True)
    archive = output / "latest.zip"
    temporary = output / "latest.zip.new"
    with zipfile.ZipFile(temporary, "w", compression=zipfile.ZIP_DEFLATED) as bundle:
        for name, data in sorted(payload.items()):
            entry = zipfile.ZipInfo(name, (2020, 1, 1, 0, 0, 0))
            entry.compress_type = zipfile.ZIP_DEFLATED
            entry.external_attr = 0o100644 << 16
            bundle.writestr(entry, data)
    temporary.replace(archive)
    with zipfile.ZipFile(archive) as bundle:
        if bundle.testzip() is not None or set(bundle.namelist()) != set(payload):
            raise ValueError("Release archive failed verification")
    download = f"{REPOSITORY}/releases/download/{tag}/latest.zip"
    store = dict(manifest)
    store.update(AssemblyVersion="0.0.0.0", TestingAssemblyVersion=version,
                 TestingDalamudApiLevel=manifest["DalamudApiLevel"], IsTestingExclusive=True, IsHide=False,
                 DownloadLinkInstall=download, DownloadLinkUpdate=download, DownloadLinkTesting=download,
                 LastUpdate=int(time.time()))
    (output / "pluginmaster.json").write_text(json.dumps([store], ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    (output / "SHA256SUMS.txt").write_text(f"{sha256(archive.read_bytes())}  latest.zip\n", encoding="utf-8")
    (output / "release-summary.json").write_text(json.dumps({"Tag": tag, "AssemblyVersion": version,
        "InternalName": manifest["InternalName"], "Channel": "testing", "Archive": "latest.zip",
        "DownloadUrl": download, "Files": sorted(payload)}, indent=2) + "\n", encoding="utf-8")
    return archive


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--build-dir", type=Path, default=ROOT / "artifacts/plugin")
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts/release")
    parser.add_argument("--tag", help="Optional release tag; must agree with the compiled manifest")
    args = parser.parse_args()
    print("Public test package:", package(ROOT, args.build_dir, args.output, args.tag))
